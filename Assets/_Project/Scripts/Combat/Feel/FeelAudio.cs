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
    LowAmmoClick, LastRound, SlideBack, SlideForward, ShieldBreak, ShieldHit }

/// <summary>
/// Shot, mechanical and confirm sounds. A gun sound is, in order: the clips assigned to it (GunFeel / WeaponData),
/// a Tables/SoundCues row "Gun&lt;sound&gt;/&lt;gun&gt;" (GunBolt/Rifle: the slide pulled, then let go), the Guns sound bank's
/// "&lt;gun&gt;/&lt;sound&gt;" then "&lt;sound&gt;" (Art/Audio/Guns/Rifle/Shot, Art/Audio/Guns/DryFire), and last a synthesized
/// sound built once, so the game has a full audio layer before any clips exist. Every shot also gets the room's tail
/// (GunReverb). Created on first use during Play; no scene setup.
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

    /// <summary>2D sound, for anything the player does or is told. gun picks a gun's own mechanical sounds.</summary>
    public static void Play(FeelSound sound, AudioClip[] overrides, float volume, float pitch = 1f, GunSoundProfile? gun = null)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        var self = Get();
        var profile = self.GunOf(sound, gun, true);
        if (HasAny(overrides) || !self.PlayGun(sound, profile, null, volume, pitch, 0f))
            self.Voice(self.Pick(sound, overrides), null, volume, pitch, 0f, SetOf(sound));
        if (IsShot(sound)) GunReverb.Tail(profile, null, volume);
    }

    /// <summary>2D sound of one given clip, such as a sound bank's pick (a confirm sound), on the same voices as Play.</summary>
    public static void PlayClip(AudioClip clip, float volume, float pitch = 1f)
    {
        if (!Application.isPlaying || clip == null || volume <= 0f) return;
        Get().Voice(clip, null, volume, pitch, 0f, Dry);
    }

    /// <summary>3D sound at a world point (bodies landing, someone else's gun). gun picks a gun's own mechanical sounds.</summary>
    public static void PlayAt(FeelSound sound, AudioClip[] overrides, Vector3 point, float volume, float pitch = 1f, float maxDistance = 14f,
        GunSoundProfile? gun = null)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        var self = Get();
        var profile = self.GunOf(sound, gun, false);
        if (HasAny(overrides) || !self.PlayGun(sound, profile, point, volume, pitch, maxDistance))
            self.Voice(self.Pick(sound, overrides), point, volume, pitch, maxDistance, SetOf(sound));
        if (IsShot(sound)) GunReverb.Tail(profile, point, volume, maxDistance);
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
    /// 0 for the synthesized fallback, whose hit is at its start, and for clips whose samples can't be read.
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
    /// Builds the voices, loads the gun sounds and the room tails and synthesizes every fallback, which the first shot
    /// would otherwise stop to do.
    /// </summary>
    public static void Prepare()
    {
        if (!Application.isPlaying) return;
        Get().Bank(GunBank);
        GunReverb.Prepare();
        foreach (FeelSound sound in Enum.GetValues(typeof(FeelSound))) Synthesized(sound);
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
            case FeelSound.SlideForward: return sound.ToString();
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

    /// <summary>Assigned clips first, otherwise a synthesized variation; avoids repeating the last pick.</summary>
    private AudioClip Pick(FeelSound sound, AudioClip[] overrides)
    {
        AudioClip[] pool = HasAny(overrides) ? overrides : Synthesized(sound);
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

    private static AudioClip[] Synthesized(FeelSound sound)
    {
        if (synthesized.TryGetValue(sound, out var clips)) return clips;
        int variations = sound == FeelSound.RifleShot || sound == FeelSound.PistolShot || sound == FeelSound.HeavyShot ? 4
            : sound == FeelSound.HitMarker ? 3 : 2;
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

    private static float[] Synthesize(FeelSound sound, System.Random random)
    {
        switch (sound)
        {
            case FeelSound.RifleShot: return Gunshot(random, .55f, crack: .0045f, body: .042f, thumpFrom: 150f, thumpTo: 46f, thumpDecay: .075f, tail: .17f, crackGain: 1f);
            case FeelSound.PistolShot: return Gunshot(random, .42f, crack: .0035f, body: .03f, thumpFrom: 210f, thumpTo: 70f, thumpDecay: .05f, tail: .11f, crackGain: 1.25f);
            case FeelSound.HeavyShot: return Gunshot(random, .8f, crack: .006f, body: .065f, thumpFrom: 110f, thumpTo: 34f, thumpDecay: .12f, tail: .26f, crackGain: .9f);
            case FeelSound.DryFire: return Finish(Mix(.09f, Click(random, 0f, .7f, 2600f), Click(random, .019f, .45f, 1900f)));
            // Dry mechanical tick (rising pitch is applied at play time), not a bell: no ringing tail.
            case FeelSound.LowAmmo:
            case FeelSound.LowAmmoClick: return Finish(Mix(.06f, Click(random, 0f, .9f, 3200f), Sine(1400f * Jitter(random, .02f), .006f, .35f)), .8f);
            case FeelSound.LastRound: return Finish(Mix(.09f, Click(random, 0f, .7f, 2600f), Click(random, .019f, .45f, 1900f)));
            case FeelSound.MagOut: return Finish(Mix(.3f, Clack(random, 0f, .9f, 1500f), Slide(random, .015f, .07f, .3f), Clack(random, .095f, .35f, 1100f)));
            case FeelSound.MagIn: return Finish(Mix(.3f, Slide(random, 0f, .06f, .35f), Clack(random, .065f, 1f, 1700f), Click(random, .078f, .5f, 3200f)));
            case FeelSound.Bolt: return Finish(Mix(.38f, Clack(random, 0f, .7f, 1300f), Slide(random, .02f, .07f, .25f), Clack(random, .13f, 1f, 1900f),
                Shift(Bell(1800f, .05f, .3f, 1.44f, 2.3f), .13f)));
            // Bolt's two halves.
            case FeelSound.SlideBack: return Finish(Mix(.13f, Clack(random, 0f, .7f, 1300f), Slide(random, .02f, .07f, .25f)));
            case FeelSound.SlideForward: return Finish(Mix(.25f, Clack(random, 0f, 1f, 1900f), Bell(1800f, .05f, .3f, 1.44f, 2.3f)));
            case FeelSound.HitMarker: return Finish(Mix(.07f, Click(random, 0f, .8f, 5200f), Sine(1900f * Jitter(random, .03f), .012f, .55f), Sine(3800f, .006f, .25f)), .85f);
            // Kill confirms stay low and dull: no bell/ding layer, which is piercing at confirm volume.
            case FeelSound.Kill: return Finish(Mix(.65f, Sweep(120f, 40f, .17f, 1f), Noise(random, 0f, .055f, .8f, 900f), Whoosh(random)), .95f);
            case FeelSound.HeadshotKill: return Finish(Mix(.7f, Sweep(130f, 38f, .2f, 1f), Noise(random, 0f, .06f, .85f, 1100f), Whoosh(random),
                Click(random, 0f, .6f, 2200f)), .95f);
            case FeelSound.BodyFall: return Finish(Mix(.45f, Sweep(95f, 42f, .09f, 1f), Noise(random, 0f, .035f, .8f, 700f),
                Shift(Noise(random, 0f, .05f, .3f, 1800f), .015f)), .9f);
            // A pane of energy giving way: a bright crack, the charge falling out of it (a falling whine over a low knock),
            // and glass raining down in short high pings that thin out.
            case FeelSound.ShieldBreak: return Finish(Mix(.6f, Hiss(random, 0f, .03f, .9f, 3200f), Sweep(900f, 170f, .09f, .45f),
                Sweep(170f, 55f, .06f, .55f), Tinkles(random, 11)), .9f, 1.4f);
            // Energy taking a blow, never flesh: a sharp electric crack, a falling zap and a glassy ping over a short knock.
            case FeelSound.ShieldHit: return Finish(Mix(.24f, Hiss(random, 0f, .012f, 1f, 4500f), Sweep(2600f, 1100f, .035f, .45f),
                Bell(3400f * Jitter(random, .05f), .03f, .3f, 1.52f, 2.31f), Sweep(220f, 90f, .03f, .5f)), .85f, 1.6f);
            default: return new float[64];
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

    /// <summary>
    /// Layered gunshot: supersonic crack (high-passed noise), muzzle blast body (low-passed noise), chest thump
    /// (falling sine), a tiny mechanism click and a filtered tail with early reflections for space.
    /// </summary>
    private static float[] Gunshot(System.Random random, float seconds, float crack, float body, float thumpFrom, float thumpTo,
        float thumpDecay, float tail, float crackGain)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var dry = new float[n];
        float lowBody = 0f, lowTail = 0f, lowCrack = 0f, phase = 0f;
        float bodyCut = Alpha(1700f * Jitter(random, .12f)), tailCut = Alpha(520f * Jitter(random, .15f)), crackCut = Alpha(2600f);
        float thumpDecayJ = thumpDecay * Jitter(random, .1f);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate, white = (float)(random.NextDouble() * 2.0 - 1.0);
            lowCrack += crackCut * (white - lowCrack);
            lowBody += bodyCut * (white - lowBody);
            lowTail += tailCut * (white - lowTail);
            float attack = Mathf.Clamp01(t / .0006f);
            float frequency = thumpTo + (thumpFrom - thumpTo) * Mathf.Exp(-t / .022f);
            phase += 2f * Mathf.PI * frequency / Rate;
            dry[i] = (white - lowCrack) * Mathf.Exp(-t / crack) * crackGain
                + lowBody * Mathf.Exp(-t / body) * 2.6f * attack
                + Mathf.Sin(phase) * Mathf.Exp(-t / thumpDecayJ) * .95f * attack
                + lowTail * Mathf.Exp(-t / tail) * 3.2f * Mathf.Clamp01(t / .012f)
                + (t < .004f ? Mathf.Sin(2f * Mathf.PI * 3300f * t) * Mathf.Exp(-t / .0012f) * .35f : 0f);
        }
        // Early reflections, darker and quieter than the direct blast.
        var output = (float[])dry.Clone();
        int[] delays = { (int)(.029f * Rate * Jitter(random, .1f)), (int)(.063f * Rate * Jitter(random, .1f)), (int)(.118f * Rate * Jitter(random, .1f)) };
        float[] gains = { .32f, .2f, .11f };
        for (int d = 0; d < delays.Length; d++)
        {
            float low = 0f, cut = Alpha(1400f - d * 350f);
            for (int i = delays[d]; i < n; i++) { low += cut * (dry[i - delays[d]] - low); output[i] += low * gains[d]; }
        }
        return Finish(output, .95f, drive: 1.9f);
    }

    private static float[] Click(System.Random random, float start, float gain, float tone)
    {
        int n = Mathf.CeilToInt((start + .02f) * Rate); var data = new float[n]; float low = 0f, cut = Alpha(tone);
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = i / (float)Rate - start, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += cut * (white - low);
            data[i] = ((white - low) * Mathf.Exp(-t / .0011f) + Mathf.Sin(2f * Mathf.PI * tone * .7f * t) * Mathf.Exp(-t / .0035f) * .5f) * gain;
        }
        return data;
    }

    /// <summary>Metal-on-polymer impact: a short low-passed burst plus a damped knock.</summary>
    private static float[] Clack(System.Random random, float start, float gain, float tone)
    {
        int n = Mathf.CeilToInt((start + .06f) * Rate); var data = new float[n]; float low = 0f, cut = Alpha(tone * 2f);
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = i / (float)Rate - start, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += cut * (white - low);
            data[i] = (low * Mathf.Exp(-t / .009f) * 2.2f + Mathf.Sin(2f * Mathf.PI * tone * t) * Mathf.Exp(-t / .014f) * .45f) * gain;
        }
        return data;
    }

    private static float[] Slide(System.Random random, float start, float duration, float gain)
    {
        int n = Mathf.CeilToInt((start + duration) * Rate); var data = new float[n]; float low = 0f, lower = 0f;
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = (i / (float)Rate - start) / duration, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += Alpha(2500f + 3000f * t) * (white - low); lower += Alpha(700f) * (low - lower);
            data[i] = (low - lower) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * gain;
        }
        return data;
    }

    private static float[] Noise(System.Random random, float start, float decay, float gain, float cutoff)
    {
        int n = Mathf.CeilToInt((start + decay * 6f) * Rate); var data = new float[n]; float low = 0f, cut = Alpha(cutoff);
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = i / (float)Rate - start; low += cut * ((float)(random.NextDouble() * 2.0 - 1.0) - low);
            data[i] = low * Mathf.Exp(-t / decay) * gain * 2.4f;
        }
        return data;
    }

    /// <summary>Kill "whump": band-limited noise swelling for 60 ms, then falling away, like a camera shutter through air.</summary>
    private static float[] Whoosh(System.Random random)
    {
        int n = Mathf.CeilToInt(.42f * Rate); var data = new float[n]; float low = 0f, lower = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += Alpha(400f + 2600f * Mathf.Exp(-t / .12f)) * (white - low); lower += Alpha(180f) * (low - lower);
            float envelope = t < .06f ? t / .06f : Mathf.Exp(-(t - .06f) / .09f);
            data[i] = (low - lower) * envelope * .9f;
        }
        return data;
    }

    private static float[] Sine(float frequency, float decay, float gain)
    {
        int n = Mathf.CeilToInt(decay * 7f * Rate); var data = new float[n];
        for (int i = 0; i < n; i++) { float t = i / (float)Rate; data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-t / decay) * gain; }
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
