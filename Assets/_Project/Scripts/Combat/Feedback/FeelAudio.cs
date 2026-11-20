using System;
using System.Collections.Generic;
using UnityEngine;

public enum GunSoundProfile { Rifle, Pistol, Heavy }
// LowAmmo: the gun's own warning on a nearly empty magazine. LowAmmoClick: the click every gun shares on top of it.
// LastRound: the shot that empties the magazine. SlideBack and SlideForward: a bolt or slide pulled back and let go, each
// on its own when a reload clip times them (ReloadMoments); Bolt is both as one cue.
// ShieldHit and ShieldBreak: a player's shield taking a hit and running out (backlog 321), until clips go in
// Art/Audio/HitConfirm/ShieldHit and ShieldBreak.
public enum FeelSound { RifleShot, PistolShot, HeavyShot, DryFire, LowAmmo, MagOut, MagIn, Bolt, HitMarker, Kill, HeadshotKill, BodyFall,
    LowAmmoClick, LastRound, SlideBack, SlideForward, ShieldBreak, ShieldHit, Draw }

/// <summary>
/// Shot, mechanical and confirm sounds. A gun sound is, in order: the clips assigned to it (GunFeel / WeaponData),
/// a Tables/SoundCues row "Gun&lt;sound&gt;/&lt;gun&gt;" (GunBolt/Rifle: the slide pulled, then let go), the Guns sound bank's
/// "&lt;gun&gt;/&lt;sound&gt;" then "&lt;sound&gt;" (Art/Audio/Guns/Rifle/Shot, Art/Audio/Guns/DryFire); with none of them
/// it is silent. Only the shield's two sounds are still made here until their files are chosen. Every shot also gets
/// the room's tail (GunReverb). Created on first use during Play; no scene setup.
/// </summary>
[DisallowMultipleComponent]
public sealed class FeelAudio : MonoBehaviour
{
    private const int FlatVoices = 12, SpatialVoices = 6, Rate = 44100;
    // A table may ask for a voice louder than its clip (PlayOneShot amplifies above 1); this caps a typo at +12 dB.
    private const float MaxVolume = 4f;
    private const string GunBank = "Guns", GunCue = "Gun";
    private static FeelAudio instance;
    private static readonly Dictionary<FeelSound, AudioClip[]> synthesized = new Dictionary<FeelSound, AudioClip[]>();
    private static readonly Dictionary<AudioClip, float> peaks = new Dictionary<AudioClip, float>();
    // Voices by [Wet, Dry, Shot]: wet ones take the room's reverb (ReverbArea); dry ones are for confirm sounds; shots have
    // voices of their own, dry too (their room is GunReverb's tail).
    // Each voice is stopped before it plays again (VoiceRing). PlayOneShot alone never ended the sounds before it, and the
    // MX68's 1.7 s shots at 750 RPM piled past Unity's 32 real voices in about 30 rounds; from there every new shot
    // started virtual and the gun went silent while it kept firing (user, 2026-11-09). A full-auto string now holds six
    // shots at once (the newest 0.48 s at 750 RPM, its room carried on by GunReverb's tail), and the last one rings out.
    private const int Wet = 0, Dry = 1, Shot = 2;
    private const int ShotVoices = 6, OtherShotVoices = 8;
    private readonly VoiceRing[] flat = new VoiceRing[3], spatial = new VoiceRing[3];
    private readonly Dictionary<FeelSound, AudioClip> previous = new Dictionary<FeelSound, AudioClip>();
    private readonly Dictionary<string, SoundBank> banks = new Dictionary<string, SoundBank>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Pending> pending = new List<Pending>();
    // The gun of the player's last shot, for a mechanical sound that comes without one (the empty click after it).
    private GunSoundProfile lastGun = GunSoundProfile.Rifle;

    // A cue's later part, played where and as its cue was.
    private struct Pending
    {
        public float At, Volume, Pitch, MaxDistance;
        public SoundCues.Item Item;
        public Vector3? Point;
        public int Set;
    }

    public static FeelSound ShotFor(GunSoundProfile profile)
        => profile == GunSoundProfile.Pistol ? FeelSound.PistolShot : profile == GunSoundProfile.Heavy ? FeelSound.HeavyShot : FeelSound.RifleShot;

    /// <summary>
    /// 2D sound, for anything the player does or is told. gun picks a gun's own mechanical sounds. A suppressed shot gets
    /// only a little of the room's tail (GunReverb).
    /// </summary>
    public static void Play(FeelSound sound, AudioClip[] overrides, float volume, float pitch = 1f, GunSoundProfile? gun = null,
        bool suppressed = false)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        var self = Get();
        var profile = self.GunOf(sound, gun, true);
        if (HasAny(overrides) || !self.PlayGun(sound, profile, null, volume, pitch, 0f))
            self.Voice(self.Pick(sound, overrides), null, volume, pitch, 0f, SetOf(sound));
        if (IsShot(sound)) GunReverb.Tail(profile, null, volume, suppressed: suppressed);
    }

    /// <summary>2D sound of one given clip, such as a sound bank's pick (a confirm sound), on the same voices as Play.</summary>
    public static void PlayClip(AudioClip clip, float volume, float pitch = 1f)
    {
        if (!Application.isPlaying || clip == null || volume <= 0f) return;
        Get().Voice(clip, null, volume, pitch, 0f, Dry);
    }

    /// <summary>3D sound at a world point (bodies landing, someone else's gun). gun picks a gun's own mechanical sounds.</summary>
    public static void PlayAt(FeelSound sound, AudioClip[] overrides, Vector3 point, float volume, float pitch = 1f, float maxDistance = 14f,
        GunSoundProfile? gun = null, bool suppressed = false)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        var self = Get();
        var profile = self.GunOf(sound, gun, false);
        if (HasAny(overrides) || !self.PlayGun(sound, profile, point, volume, pitch, maxDistance))
            self.Voice(self.Pick(sound, overrides), point, volume, pitch, maxDistance, SetOf(sound));
        if (IsShot(sound)) GunReverb.Tail(profile, point, volume, maxDistance, suppressed);
    }

    /// <summary>3D sound of one given clip at a world point, such as a sound bank's pick (a ricochet), on the same voices as PlayAt.</summary>
    public static void PlayClipAt(AudioClip clip, Vector3 point, float volume, float pitch = 1f, float maxDistance = 14f)
    {
        if (!Application.isPlaying || clip == null || volume <= 0f) return;
        Get().Voice(clip, point, volume, pitch, maxDistance, Wet);
    }

    /// <summary>
    /// Seconds from a gun sound's start to its loudest moment (a magazine's clack, a bolt hitting home), averaged over the
    /// clips it picks from, found the way Play finds them. A caller that knows when the hit happens starts it this much early.
    /// 0 with no clips, and for clips whose samples can't be read.
    /// </summary>
    public static float PeakTime(FeelSound sound, AudioClip[] overrides, GunSoundProfile gun)
    {
        if (!Application.isPlaying) return 0f;
        var self = Get();
        AudioClip[] clips = HasAny(overrides) ? overrides : null;
        float delay = 0f;
        string name = GunEvent(sound);
        if (clips == null && name != null)
        {
            if (SoundCues.TryFind(GunCue + name + "/" + gun, out var cue) && cue.Items.Length > 0)
            {
                var cueBank = self.Bank(cue.Items[0].Bank);
                if (cueBank != null) cueBank.TryGet(cue.Items[0].Key, out clips);
                delay = cue.Items[0].Delay;
            }
            else
            {
                var bank = self.Bank(GunBank);
                if (bank != null && !bank.TryGet(gun + "/" + name, out clips)) bank.TryGet(name, out clips);
            }
        }
        if (!HasAny(clips)) return 0f;
        float sum = 0f;
        int count = 0;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            sum += Peak(clip);
            count++;
        }
        return delay + sum / count;
    }

    // Where a clip is loudest, in seconds; 0 when its samples can't be read (compressed in memory, streamed, not loaded).
    private static float Peak(AudioClip clip)
    {
        if (peaks.TryGetValue(clip, out float at)) return at;
        if (clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.samples <= 0 || clip.channels <= 0) return peaks[clip] = 0f;
        if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData()) return 0f;
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) return 0f;
        int loudest = 0;
        float max = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float level = Mathf.Abs(data[i]);
            if (level > max) { max = level; loudest = i; }
        }
        return peaks[clip] = loudest / clip.channels / (float)clip.frequency;
    }

    private static FeelAudio Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Feel Audio", groupName: "Audio").AddComponent<FeelAudio>();
        return instance;
    }

    /// <summary>
    /// Builds the voices, loads the gun sounds and the room tails and makes the shield's two sounds, which the first shot
    /// or hit would otherwise stop to do.
    /// </summary>
    public static void Prepare()
    {
        if (!Application.isPlaying) return;
        Get().Bank(GunBank);
        GunReverb.Prepare();
        Synthesized(FeelSound.ShieldBreak);
        Synthesized(FeelSound.ShieldHit);
        // The reload hits a timed reload lines up (WeaponFeedback), read now rather than on the first reload.
        foreach (GunSoundProfile gun in Enum.GetValues(typeof(GunSoundProfile)))
        {
            PeakTime(FeelSound.MagIn, null, gun);
            PeakTime(FeelSound.SlideForward, null, gun);
        }
    }

    private void Update()
    {
        if (pending.Count == 0) return;
        float now = Time.time;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var due = pending[i];
            if (due.At > now) continue;
            pending.RemoveAt(i);
            PlayItem(due.Item, due.Point, due.Volume, due.Pitch, due.MaxDistance, due.Set);
        }
    }

    private static bool IsShot(FeelSound sound) => sound == FeelSound.RifleShot || sound == FeelSound.PistolShot || sound == FeelSound.HeavyShot;

    // Out of the rooms' reverb: shots carry their own room (GunReverb), and confirm sounds aren't in the world.
    private static bool IsDry(FeelSound sound)
        => IsShot(sound) || sound == FeelSound.HitMarker || sound == FeelSound.Kill || sound == FeelSound.HeadshotKill
            || sound == FeelSound.ShieldBreak;

    // Which voices a sound takes: a shot its own, else dry or wet.
    private static int SetOf(FeelSound sound) => IsShot(sound) ? Shot : IsDry(sound) ? Dry : Wet;

    // A shot names its gun; one's own shot is remembered for the sounds that follow it without one.
    private GunSoundProfile GunOf(FeelSound sound, GunSoundProfile? gun, bool own)
    {
        if (!IsShot(sound)) return gun ?? (own ? lastGun : GunSoundProfile.Rifle);
        var shot = sound == FeelSound.PistolShot ? GunSoundProfile.Pistol : sound == FeelSound.HeavyShot ? GunSoundProfile.Heavy : GunSoundProfile.Rifle;
        if (own) lastGun = shot;
        return shot;
    }

    // The gun sound's name in the cue table and the Guns bank; null for sounds that aren't a gun's.
    private static string GunEvent(FeelSound sound)
    {
        switch (sound)
        {
            case FeelSound.RifleShot:
            case FeelSound.PistolShot:
            case FeelSound.HeavyShot: return "Shot";
            case FeelSound.DryFire:
            case FeelSound.LowAmmo:
            case FeelSound.LowAmmoClick:
            case FeelSound.LastRound:
            case FeelSound.MagOut:
            case FeelSound.MagIn:
            case FeelSound.Bolt:
            case FeelSound.SlideBack:
            case FeelSound.SlideForward:
            case FeelSound.Draw: return sound.ToString();
            default: return null;
        }
    }

    // The cue row for this gun, else the Guns bank's clips for it, else its shared ones. False: nothing there.
    private bool PlayGun(FeelSound sound, GunSoundProfile gun, Vector3? point, float volume, float pitch, float maxDistance)
    {
        string name = GunEvent(sound);
        if (name == null) return false;
        int set = SetOf(sound);
        if (SoundCues.TryFind(GunCue + name + "/" + gun, out var cue) && cue.Items.Length > 0)
        {
            foreach (var item in cue.Items)
            {
                if (item.Delay > 0f)
                    pending.Add(new Pending
                    {
                        At = Time.time + item.Delay, Item = item, Point = point, Volume = volume, Pitch = pitch, MaxDistance = maxDistance, Set = set
                    });
                else PlayItem(item, point, volume, pitch, maxDistance, set);
            }
            return true;
        }
        var bank = Bank(GunBank);
        if (bank == null) return false;
        string key = gun + "/" + name;
        if (!bank.TryGet(key, out _))
        {
            key = name;
            if (!bank.TryGet(key, out _)) return false;
        }
        var settings = bank.SettingsOf(key);
        Voice(bank.Pick(key), point, volume * settings.Volume, pitch * settings.RandomPitch(), maxDistance, set);
        return true;
    }

    private void PlayItem(SoundCues.Item item, Vector3? point, float volume, float pitch, float maxDistance, int set)
    {
        var bank = Bank(item.Bank);
        var clip = bank != null ? bank.Pick(item.Key) : null;
        if (clip == null) return;
        var settings = bank.SettingsOf(item.Key);
        Voice(clip, point, volume * item.Volume * settings.Volume, pitch * settings.RandomPitch(), maxDistance, set);
    }

    // A flat voice, or a 3D one at the point, of the set's (Wet, Dry, Shot); dry ones and shots skip the rooms' reverb.
    // The voice's last sound ends here.
    private void Voice(AudioClip clip, Vector3? point, float volume, float pitch, float maxDistance, int set)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource voice;
        if (point.HasValue)
        {
            voice = spatial[set].Take();
            voice.transform.position = point.Value;
            voice.maxDistance = Mathf.Max(voice.minDistance + .1f, maxDistance);
        }
        else voice = flat[set].Take();
        voice.pitch = Mathf.Clamp(pitch, .25f, 3f);
        voice.PlayOneShot(clip, Mathf.Clamp(volume, 0f, MaxVolume));
    }

    private SoundBank Bank(string id)
    {
        if (banks.TryGetValue(id, out var bank) && bank != null) return bank;
        return banks[id] = SoundBank.Load(id);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        for (int set = Wet; set <= Shot; set++)
        {
            var flatVoices = new AudioSource[set == Shot ? ShotVoices : FlatVoices];
            for (int i = 0; i < flatVoices.Length; i++)
            {
                var voice = flatVoices[i] = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.spatialBlend = 0f; voice.priority = 32; voice.bypassReverbZones = set != Wet;
                GameAudio.Bind(voice, AudioChannel.Combat);
            }
            flat[set] = new VoiceRing(flatVoices);
            var spatialVoices = new AudioSource[set == Shot ? OtherShotVoices : SpatialVoices];
            string label = set == Shot ? "Feel Shot Voice " : set == Dry ? "Feel Dry Voice " : "Feel Voice ";
            for (int i = 0; i < spatialVoices.Length; i++)
            {
                var voice = new GameObject(label + i).AddComponent<AudioSource>();
                voice.transform.SetParent(transform, false);
                voice.playOnAwake = false; voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = 1.5f; voice.bypassReverbZones = set != Wet;
                GameAudio.Bind(voice, AudioChannel.Combat);
                spatialVoices[i] = voice;
            }
            spatial[set] = new VoiceRing(spatialVoices);
        }
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>
    /// Assigned clips first, otherwise the shield's made variations; null (silence) for anything else. Avoids repeating
    /// the last pick.
    /// </summary>
    private AudioClip Pick(FeelSound sound, AudioClip[] overrides)
    {
        AudioClip[] pool = HasAny(overrides) ? overrides : Synthesized(sound);
        if (pool == null) return null;
        previous.TryGetValue(sound, out var last);
        AudioClip chosen = null; int candidates = 0;
        foreach (var clip in pool)
        {
            if (clip == null || (clip == last && pool.Length > 1)) continue;
            // Reservoir sampling: uniform pick without allocating.
            if (UnityEngine.Random.Range(0, ++candidates) == 0) chosen = clip;
        }
        if (chosen == null) chosen = last;
        previous[sound] = chosen;
        return chosen;
    }

    private static bool HasAny(AudioClip[] clips)
    {
        if (clips == null) return false;
        foreach (var clip in clips) if (clip != null) return true;
        return false;
    }

    // Null for a sound that isn't made here.
    private static AudioClip[] Synthesized(FeelSound sound)
    {
        if (!IsMade(sound)) return null;
        if (synthesized.TryGetValue(sound, out var clips)) return clips;
        const int variations = 2;
        clips = new AudioClip[variations];
        for (int i = 0; i < variations; i++)
        {
            float[] data = Synthesize(sound, new System.Random(((int)sound + 1) * 7919 + i * 104729));
            var clip = AudioClip.Create("Synth " + sound + " " + i, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clips[i] = clip;
        }
        synthesized[sound] = clips;
        return clips;
    }

    // ---------------------------------------------------------------- synthesis

    // Only the shield's two sounds are still made here: no file has been chosen for them yet (SoundRebuild.md 5.4). Every
    // other sound plays its files or nothing; the synthesized shots, mechanics, confirms and fall went in 0.9.237
    // ("소리를 코드로 만들면 안 되잖아", user 2026-11-20), as their recorded files had long played first.
    private static bool IsMade(FeelSound sound) => sound == FeelSound.ShieldBreak || sound == FeelSound.ShieldHit;

    private static float[] Synthesize(FeelSound sound, System.Random random)
    {
        switch (sound)
        {
            // A pane of energy giving way: a bright crack, the charge falling out of it (a falling whine over a low knock),
            // and glass raining down in short high pings that thin out.
            case FeelSound.ShieldBreak: return Finish(Mix(.6f, Hiss(random, 0f, .03f, .9f, 3200f), Sweep(900f, 170f, .09f, .45f),
                Sweep(170f, 55f, .06f, .55f), Tinkles(random, 11)), .9f, 1.4f);
            // Energy taking a blow, never flesh: a sharp electric crack, a falling zap and a glassy ping over a short knock.
            case FeelSound.ShieldHit: return Finish(Mix(.24f, Hiss(random, 0f, .012f, 1f, 4500f), Sweep(2600f, 1100f, .035f, .45f),
                Bell(3400f * Jitter(random, .05f), .03f, .3f, 1.52f, 2.31f), Sweep(220f, 90f, .03f, .5f)), .85f, 1.6f);
            default: return null;
        }
    }

    /// <summary>High-passed noise burst: the bright crack at the start of something brittle breaking.</summary>
    private static float[] Hiss(System.Random random, float start, float decay, float gain, float cutoff)
    {
        int n = Mathf.CeilToInt((start + decay * 6f) * Rate); var data = new float[n]; float low = 0f, cut = Alpha(cutoff);
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = i / (float)Rate - start, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += cut * (white - low);
            data[i] = (white - low) * Mathf.Exp(-t / decay) * gain * Mathf.Clamp01(t / .0005f);
        }
        return data;
    }

    /// <summary>Glass coming apart: a scatter of short high bells, thick at first and thinning out over a third of a second.</summary>
    private static float[] Tinkles(System.Random random, int count)
    {
        var data = new float[Mathf.CeilToInt(.6f * Rate)];
        for (int k = 0; k < count; k++)
        {
            float spread = (float)random.NextDouble();
            float start = .004f + spread * spread * .32f;
            float frequency = 2600f + (float)random.NextDouble() * 4200f;
            float gain = (.5f - .3f * spread * spread) * Jitter(random, .2f);
            var ping = Shift(Bell(frequency, .018f + (float)random.NextDouble() * .03f, gain, 1.47f, 2.09f), start);
            for (int i = 0; i < Mathf.Min(ping.Length, data.Length); i++) data[i] += ping[i];
        }
        return data;
    }

    private static float[] Sweep(float from, float to, float decay, float gain)
    {
        int n = Mathf.CeilToInt(decay * 5f * Rate); var data = new float[n]; float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate; phase += 2f * Mathf.PI * (to + (from - to) * Mathf.Exp(-t / (decay * .4f))) / Rate;
            data[i] = Mathf.Sin(phase) * Mathf.Exp(-t / decay) * gain * Mathf.Clamp01(t / .002f);
        }
        return data;
    }

    /// <summary>Inharmonic partials decaying at different rates read as struck metal.</summary>
    private static float[] Bell(float fundamental, float decay, float gain, float ratio2, float ratio3)
    {
        int n = Mathf.CeilToInt(decay * 6f * Rate); var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, w = 2f * Mathf.PI * fundamental * t;
            data[i] = (Mathf.Sin(w) * Mathf.Exp(-t / decay) + .5f * Mathf.Sin(w * ratio2) * Mathf.Exp(-t / (decay * .55f))
                + .3f * Mathf.Sin(w * ratio3) * Mathf.Exp(-t / (decay * .3f))) * gain * Mathf.Clamp01(t / .0008f);
        }
        return data;
    }

    private static float[] Shift(float[] data, float seconds)
    {
        int offset = Mathf.RoundToInt(seconds * Rate); var shifted = new float[data.Length + offset];
        Array.Copy(data, 0, shifted, offset, data.Length); return shifted;
    }

    private static float[] Mix(float seconds, params float[][] layers)
    {
        var data = new float[Mathf.CeilToInt(seconds * Rate)];
        foreach (var layer in layers)
            for (int i = 0; i < Mathf.Min(layer.Length, data.Length); i++) data[i] += layer[i];
        return data;
    }

    /// <summary>Soft saturation, peak normalisation and a short fade so no clip ends on a click.</summary>
    private static float[] Finish(float[] data, float peak = .9f, float drive = 1.2f)
    {
        float max = 0f, norm = (float)Math.Tanh(drive);
        for (int i = 0; i < data.Length; i++) { data[i] = (float)Math.Tanh(data[i] * drive) / norm; max = Mathf.Max(max, Mathf.Abs(data[i])); }
        float scale = max > 0f ? peak / max : 0f;
        int fade = Mathf.Min(data.Length, Rate / 50);
        for (int i = 0; i < data.Length; i++)
        {
            float tail = data.Length - i < fade ? (data.Length - i) / (float)fade : 1f;
            data[i] *= scale * tail;
        }
        return data;
    }

    private static float Alpha(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
    private static float Jitter(System.Random random, float amount) => 1f + (float)(random.NextDouble() * 2.0 - 1.0) * amount;
}
