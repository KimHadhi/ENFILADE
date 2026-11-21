using System.Collections.Generic;
using Sonity;
using Sonity.Internal;
using UnityEngine;

/// <summary>
/// The one way sounds are to be played (Docs/Features/SoundRebuild.md 3.7; stage 1 of its chapter 7): Sonity plays them,
/// and this thin layer tells it what from the game's own homes, which stay where they are.
/// - What a sound is: a sound bank's folder (SoundBank: Art/Audio/&lt;bank&gt;/&lt;key&gt;, every clip in it), its volume and
///   pitch spread a row of Tables/Audio/Sounds.csv ("&lt;bank&gt;/&lt;key&gt;"). From those this builds a Sonity SoundEvent and
///   SoundContainer while the game runs, once per sound and channel and space, and writes the numbers again when the
///   tables are read again. No Sonity asset is made by hand, so a folder and a row stay all a sound is, for a modder too
///   (the user, 2026-11-21: "내 철학 지켜. 모딩, 유지 보수, 유동성 챙기고 안정성 좋은 걸로").
/// - What Sonity does with it: the voices (Tables/Audio/SoundEngine Engine.voiceLimit at once, the quietest given up
///   first), how many of one sound at once (Engine.voices), the clip picked without repeats, the AudioSources pooled and
///   reused rather than made and destroyed per sound.
/// - What stays the game's: the channels, through the mixer (GameAudio.Group: the settings' volume, ducking and slow
///   motion, written there by GameAudio from the same numbers as the older way), where a 3D sound is heard (Space), and
///   what a zombie hears (SoundCues' noise, Noise.Make: game rules, not sounds).
/// Sonity's own defaults (a pitch spread, a sound turning 2D near the ears, spread, reverb that grows with distance) are
/// set off here, so a sound played this way sounds as the older way played it. A volume past 1 (Sonity's voices are
/// AudioSources, at most 1) plays in the channel's Loud group at the volume over its boost.
/// Sonity itself stays as bought in Assets/Plugins/Sonity, unchanged: its upgrade deletes the folder and imports anew.
/// </summary>
public static class GameSounds
{
    /// <summary>
    /// Where a 3D sound is heard: whole within Near (m), gone at Far, fading straight between (Unity's Linear rolloff) or as
    /// Near over the distance (Unity's Logarithmic, which then holds at Far's level).
    /// </summary>
    public readonly struct Space
    {
        public readonly float Near, Far;
        public readonly bool Logarithmic;

        public Space(float near, float far, bool logarithmic = false)
        {
            Far = Mathf.Max(.1f, far);
            Near = Mathf.Clamp(near, 0f, Far);
            Logarithmic = logarithmic;
        }

        /// <summary>The space of a 3D sound whose caller names none: Engine.near and Engine.far.</summary>
        public static Space Default => new Space(Number("near", 1.5f), Number("far", 14f));
    }

    // A built sound: what it was built from, and the table version its numbers were written at.
    private sealed class Built
    {
        public string Bank, Key;
        public AudioChannel Channel;
        public bool Spatial;
        public Space Where;
        public SoundEvent Event;
        public SoundContainer Container;
        public int Version = -1;
    }

    private static readonly Dictionary<string, Built> built = new Dictionary<string, Built>();
    private static readonly Dictionary<string, SoundBank> banks = new Dictionary<string, SoundBank>();
    // One volume and one pitch handed with every play, set just before it (Sonity reads them once, at the start), so a
    // play makes no garbage.
    private static readonly SoundParameterVolumeRatio volumeGiven = new SoundParameterVolumeRatio(1f);
    private static readonly SoundParameterPitchRatio pitchGiven = new SoundParameterPitchRatio(1f);
    private static readonly SoundParameterInternals[] given = { volumeGiven, pitchGiven };
    private static int version;
    private static bool wired;

    private static float Number(string name, float fallback) => GameTables.Number(GameAudio.EngineTable, GameAudio.EngineRow, name, fallback);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        built.Clear();
        banks.Clear();
        version = 0;
        if (wired) GameTables.Reloaded -= OnTablesReloaded;
        wired = false;
    }

    // Tables read again (a row changed while playing): every built sound writes its numbers again as it next plays.
    private static void OnTablesReloaded() => version++;

    // ---- Playing ----

    /// <summary>
    /// A bank's sound, flat (2D) on its channel: the clip picked by Sonity, its volume and pitch from Sounds.csv times
    /// <paramref name="volume"/> (0–1) and <paramref name="pitch"/>. False when the bank or the key has no clip.
    /// </summary>
    public static bool Play(string bank, string key, AudioChannel channel, float volume = 1f, float pitch = 1f)
    {
        var sound = Get(bank, key, channel, null);
        var manager = Manager();
        if (sound == null || volume <= 0f || manager == null) return false;
        Hand(volume, pitch);
        // Flat because it is built flat (no distance, spatial blend 0), whoever owns it: Sonity's own 2D calls are its
        // legacy ones, behind a define.
        sound.Play(manager.transform, given);
        return true;
    }

    /// <summary>The same sound in 3D at a point, heard over <paramref name="where"/> (Space.Default when the caller has no rule of its own).</summary>
    public static bool PlayAt(string bank, string key, AudioChannel channel, Vector3 at, Space where, float volume = 1f, float pitch = 1f)
    {
        var sound = Get(bank, key, channel, where);
        var manager = Manager();
        if (sound == null || volume <= 0f || manager == null) return false;
        Hand(volume, pitch);
        // One owner for every one-shot: a sound's voices are counted over the whole game (LimitedGlobally), not per owner.
        sound.PlayAtPosition(manager.transform, at, given);
        return true;
    }

    private static void Hand(float volume, float pitch)
    {
        volumeGiven.VolumeRatio = Mathf.Clamp01(volume);
        pitchGiven.PitchRatio = Mathf.Clamp(pitch, .1f, 4f);
    }

    // ---- Building ----

    // The built sound for this bank key, channel and space, its numbers written again when the tables changed.
    private static SoundEvent Get(string bank, string key, AudioChannel channel, Space? where)
    {
        if (string.IsNullOrEmpty(bank) || string.IsNullOrEmpty(key)) return null;
        if (!wired) { wired = true; GameTables.Reloaded += OnTablesReloaded; }
        string name = Name(bank, key, channel, where);
        if (!built.TryGetValue(name, out var sound))
        {
            sound = new Built { Bank = bank, Key = key, Channel = channel, Spatial = where.HasValue, Where = where ?? default };
            if (!Make(sound, name)) return null;
            built[name] = sound;
        }
        if (sound.Version != version && !Write(sound)) return null;
        return sound.Event;
    }

    private static string Name(string bank, string key, AudioChannel channel, Space? where)
    {
        string name = bank + "/" + key + "|" + channel;
        if (!where.HasValue) return name;
        var space = where.Value;
        return name + "|" + space.Near.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "-"
            + space.Far.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + (space.Logarithmic ? "log" : "");
    }

    /// <summary>
    /// A Sonity sound built from a bank key, not kept: the editor's check (SoundEngineCheck) builds every one this way.
    /// Null, with the reason, when the bank or key has no clip. The caller destroys what it gets (Forget).
    /// </summary>
    internal static SoundEvent Build(string bank, string key, AudioChannel channel, Space? where, out string problem)
    {
        var sound = new Built { Bank = bank, Key = key, Channel = channel, Spatial = where.HasValue, Where = where ?? default };
        problem = null;
        if (!Make(sound, Name(bank, key, channel, where)) || !Write(sound))
        {
            problem = Clips(sound) == null ? "no clip" : "not built";
            Forget(sound.Event, sound.Container);
            return null;
        }
        if (sound.Event.internals.data.audioMixerGroup == null) problem = "no mixer group";
        return sound.Event;
    }

    /// <summary>Destroys a sound Build made.</summary>
    internal static void Forget(SoundEvent sound) => Forget(sound, sound != null && sound.internals.soundContainers.Length > 0
        ? sound.internals.soundContainers[0] as SoundContainer : null);

    private static void Forget(SoundEvent sound, SoundContainer container)
    {
        if (container != null) Object.DestroyImmediate(container);
        if (sound != null) Object.DestroyImmediate(sound);
    }

    // The bank's clips for the key; null without any.
    private static AudioClip[] Clips(Built sound)
    {
        var bank = Bank(sound.Bank);
        return bank != null && bank.TryGet(sound.Key, out var clips) && clips != null && clips.Length > 0 ? clips : null;
    }

    private static SoundBank Bank(string id)
    {
        if (banks.TryGetValue(id, out var bank) && bank != null) return bank;
        return banks[id] = SoundBank.Load(id);
    }

    // The Sonity objects, empty: Write fills them. Each gets an id of its own, as Sonity tells its sounds apart by it
    // (SoundManagerInternals keys its instances by internals.sonityGuid; the editor gives an asset's, and these are none).
    private static bool Make(Built sound, string name)
    {
        if (Clips(sound) == null) return false;
        var container = ScriptableObject.CreateInstance<SoundContainer>();
        container.name = name;
        container.hideFlags = HideFlags.HideAndDontSave;
        container.internals.cachedName = name;
        container.internals.sonityGuid = SonityGuid.NewGuid();
        var made = ScriptableObject.CreateInstance<SoundEvent>();
        made.name = name;
        made.hideFlags = HideFlags.HideAndDontSave;
        made.internals.cachedName = name;
        made.internals.sonityGuid = SonityGuid.NewGuid();
        made.internals.soundContainers = new SoundContainerBase[] { container };
        made.internals.InitializeTimelineSoundContainerSetting();
        sound.Container = container;
        sound.Event = made;
        return true;
    }

    // The game's numbers onto the Sonity objects: the clips, Sounds.csv's volume and pitch spread, the channel's group,
    // the space, and Sonity's own defaults set to what the older way played.
    private static bool Write(Built sound)
    {
        var clips = Clips(sound);
        if (clips == null || sound.Event == null || sound.Container == null) return false;
        var settings = Bank(sound.Bank).SettingsOf(sound.Key);
        var holds = sound.Container.internals;
        holds.audioClips = clips;
        var data = holds.data;

        // Volume: up to full on the channel's group; past it in the Loud group, at the volume over its boost.
        bool loud = settings.Volume > 1f;
        float loudRatio = GameAudio.LoudRatio;
        data.volumeRatio = Mathf.Clamp01(loud ? settings.Volume / loudRatio : settings.Volume);
        data.volumeDecibel = VolumeScale.ConvertRatioToDecibel(data.volumeRatio);
        data.volumeRandomEnable = false;

        // Pitch: Sounds.csv's spread pitchMin–pitchMax as Sonity's middle and semitones either side.
        float low = Mathf.Max(.05f, settings.PitchMin), high = Mathf.Max(low, settings.PitchMax);
        data.pitchRatio = Mathf.Sqrt(low * high);
        data.pitchSemitone = PitchScale.RatioToSemitones(data.pitchRatio);
        data.pitchRandomEnable = high > low * 1.0001f;
        data.pitchRandomRangeSemitoneBipolar = data.pitchRandomEnable ? PitchScale.RatioToSemitones(high / low) * .5f : 0f;

        // Where it is heard. Flat sounds have no distance; 3D ones stay fully 3D at any distance, with no spread, and
        // fade over the curve Space gives (Sonity flattens Unity's own rolloff and fades the voice itself).
        bool flat = !sound.Spatial;
        data.spatialBlend = flat ? 0f : 1f;
        data.spatialBlendDistance3DIncrease = 1f;
        data.spatialSpreadDegrees = 0f;
        data.spatialSpreadRatio = 0f;
        data.distanceEnabled = !flat;
        data.reverbZoneMixDistanceIncrease = 1f;
        data.reverbZoneMixRatio = 1f;
        data.dopplerAmount = 0f;
        if (!flat)
        {
            data.distanceScale = sound.Where.Far;
            data.volumeDistanceRolloff = 0f;
            data.volumeDistanceCurve = Fade(sound.Where);
        }

        // The channel: its group, and the UI channel as GameAudio.Bind has it (heard through the pause, past the
        // listener's filters and the rooms' reverb); music out of the rooms' reverb too.
        var channel = sound.Channel;
        data.audioMixerGroup = GameAudio.Group(channel, loud);
        data.bypassListenerEffects = channel == AudioChannel.UI;
        data.bypassReverbZones = channel == AudioChannel.UI || channel == AudioChannel.Music;

        var eventData = sound.Event.internals.data;
        eventData.audioMixerGroup = data.audioMixerGroup;
        eventData.volumeRatio = 1f;
        eventData.volumeDecibel = 0f;
        eventData.polyphony = Mathf.Max(1, Mathf.RoundToInt(Number("voices", 4f)));
        eventData.polyphonyMode = PolyphonyMode.LimitedGlobally;
        eventData.ignoreGlobalPause = channel == AudioChannel.UI;
        sound.Version = version;
        return true;
    }

    // Unity's rolloffs over Sonity's distance (0 at the sound, 1 at Far): straight from Near to Far, or Near over the
    // distance (sampled densely near, where it falls fastest) and holding at Far's level beyond.
    private static AnimationCurve Fade(Space where)
    {
        float near = where.Far > 0f ? Mathf.Clamp01(where.Near / where.Far) : 0f;
        var times = new List<float> { 0f };
        var values = new List<float> { 1f };
        if (near > 0f) { times.Add(near); values.Add(1f); }
        if (!where.Logarithmic)
        {
            times.Add(1f);
            values.Add(0f);
        }
        else
        {
            const int Steps = 16;
            float from = Mathf.Max(near, .001f);
            for (int i = 1; i <= Steps; i++)
            {
                float share = i / (float)Steps;
                float at = from + (1f - from) * share * share;
                times.Add(at);
                values.Add(Mathf.Clamp01(from / at));
            }
        }
        var keys = new Keyframe[times.Count];
        for (int i = 0; i < keys.Length; i++)
        {
            float before = i > 0 ? Slope(times, values, i - 1, i) : 0f;
            float after = i < keys.Length - 1 ? Slope(times, values, i, i + 1) : 0f;
            keys[i] = new Keyframe(times[i], values[i], before, after);
        }
        return new AnimationCurve(keys);
    }

    private static float Slope(List<float> times, List<float> values, int a, int b)
    {
        float span = times[b] - times[a];
        return span > 1e-5f ? (values[b] - values[a]) / span : 0f;
    }

    // ---- Sonity's SoundManager ----

    // Made as the first scene loads, so its voice pool is ready before the first sound (it makes its AudioSources at
    // once): inactive while it is set up, as its own Awake reads the settings. It keeps itself across scenes.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() => Manager();

    private static SoundManager Manager()
    {
        var running = SoundManagerBase.Instance as SoundManager;
        if (running != null) return running;
        if (!Application.isPlaying) return null;
        var host = new GameObject("Sonity Sound Manager");
        host.SetActive(false);
        var manager = host.AddComponent<SoundManager>();
        var settings = manager.Internals.settings;
        int limit = Mathf.Clamp(Mathf.RoundToInt(Number("voiceLimit", 32f)), 1, 255);
        settings.voiceLimit = limit;
        settings.voicePreload = limit;
        settings.dontDestroyOnLoad = true;
        // It writes these onto the listener as it starts: today's master volume and pause, never its own defaults.
        settings.globalVolumeRatio = Mathf.Clamp01(GameSettings.Current.masterVolume);
        settings.globalVolumeDecibel = VolumeScale.ConvertRatioToDecibel(settings.globalVolumeRatio);
        settings.globalPause = AudioListener.pause;
        settings.speedOfSoundEnabled = false;
        host.SetActive(true);
        GameAudio.Reapply();
        return manager;
    }
}
