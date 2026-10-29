using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where one character's sounds come out.
/// - Body sounds (feet, hands, cloth) play from a few 3D voices at the point where they happen, so overlapping
///   one-shots each keep their own pitch.
/// - The mouth says one thing at a time. A grunt (Say) fades out whatever was playing before it starts. A breath
///   (Breathe) is refused while a grunt plays, is about to start, or is still due from a cue's delayed part. A cue
///   holds the mouth as soon as it fires, so a breath can't sneak into the gap before its grunt.
/// - Cues from Tables/SoundCues play here. Their delayed parts follow the body, and each cue reports how far it
///   carried (Noise) for enemy hearing.
/// Volume and pitch per sound come from Tables/Sounds. Voice counts and distances: Player.csv row Sounds.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterSounds : MonoBehaviour, ITableBound
{
    private const string VoiceBank = "Voice";

    [SerializeField, Range(1, 16), Tooltip("How many body sounds can overlap.")]
    private int bodyVoices = 8;
    [SerializeField, Range(0f, 1f), Tooltip("How 3D the body sounds are: 1 places feet, hands and cloth where they happen, 0 plays them flat.")]
    private float bodySpatialBlend = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("How 3D the voice is. The player hears their own breathing flat (0).")]
    private float voiceSpatialBlend = 0f;
    [SerializeField, Min(.1f), Tooltip("Sounds keep their full volume within this distance (m).")]
    private float fullVolumeDistance = 2f;
    [SerializeField, Min(1f), Tooltip("Sounds have faded out at this distance (m).")]
    private float maxDistance = 40f;
    [SerializeField, Min(0f), Tooltip("Height of the mouth above the feet (m).")]
    private float mouthHeight = 1.6f;
    [SerializeField, Min(.01f), Tooltip("How long the mouth takes to fade out what it was saying when something else takes over (s).")]
    private float mouthFadeTime = .06f;

    /// <summary>A grunt started: its key and length (s). Breathing waits for it to finish. Breaths don't raise it.</summary>
    public event Action<string, float> Spoke;

    /// <summary>True while a grunt is playing, waiting for the mouth, or due from a cue; breaths wait.</summary>
    public bool MouthBusy => (queued.Clip != null && queued.IsLine) || Time.unscaledTime < lineUntil || Time.time < reservedUntil;

    private sealed class Voice
    {
        public AudioSource Source;
        public float FreeAt;
    }

    private struct Utterance
    {
        public AudioClip Clip;
        public string Key;
        public float Pitch, Level, Noise;
        /// <summary>A grunt or other line, as opposed to a breath.</summary>
        public bool IsLine;
    }

    private struct Pending
    {
        public float At, Volume;
        public SoundCues.Item Item;
        public Vector3 LocalPoint;
        public string Surface;
    }

    private readonly List<Voice> body = new List<Voice>();
    private readonly List<Pending> pending = new List<Pending>();
    private readonly Dictionary<string, SoundBank> banks = new Dictionary<string, SoundBank>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reported = new HashSet<string>();
    private Transform voices;
    private AudioSource mouth;
    private Utterance queued;
    private float mouthLevel, mouthFadeRate, lineUntil, reservedUntil;
    private bool mouthSaysLine, rebuild = true;

    void ITableBound.OnTableApplied() => rebuild = true;

    /// <summary>
    /// Plays a SoundCues row at a world point. The surface picks the "{surface}" keys and the cue's surface version;
    /// volume scales every part and the noise.
    /// </summary>
    public void Play(string cue, Vector3 point, string surface = null, float volume = 1f)
    {
        if (!isActiveAndEnabled || volume <= 0f) return;
        var found = SoundCues.Find(cue, surface);
        if (found == null) return;
        float loudness = 1f;
        bool measured = false;
        foreach (var item in found.Items)
        {
            if (item.Delay > 0f)
            {
                pending.Add(new Pending
                {
                    At = Time.time + item.Delay, Volume = volume, Item = item,
                    LocalPoint = transform.InverseTransformPoint(point), Surface = surface
                });
                if (IsVoice(item)) HoldMouth(item.Delay);
                continue;
            }
            float played = PlayItem(item, point, surface, volume);
            // The ground makes a step louder or quieter (Tables/Sounds: carpet .25), and so how far it carries.
            if (item.OnSurface && !measured && played > 0f) { loudness = played; measured = true; }
        }
        Noise.Make(point, found.Noise * volume * loudness, gameObject);
    }

    /// <summary>
    /// A grunt or other line (a Voice bank key) from the mouth. Whatever the mouth was saying fades out first.
    /// Returns its length (s), or 0 if none played.
    /// </summary>
    public float Say(string key, float volume = 1f, float noise = 0f)
    {
        if (!isActiveAndEnabled || volume <= 0f || !TryUtter(key, volume, noise, true, out var line)) return 0f;
        Speak(line);
        return line.Clip.length / line.Pitch;
    }

    /// <summary>A breath (a Voice bank key), unless a grunt has the mouth. Returns whether the mouth took it.</summary>
    public bool Breathe(string key, float volume = 1f, float noise = 0f)
    {
        if (!isActiveAndEnabled || volume <= 0f || MouthBusy || !TryUtter(key, volume, noise, false, out var breath)) return false;
        Speak(breath);
        return true;
    }

    /// <summary>A looping sound on this character's body (a slide's scrape), on the movement channel.</summary>
    public SoundLoop CreateLoop(string loopName)
    {
        var source = NewSource(loopName, transform, AudioChannel.Movement, bodySpatialBlend);
        return new SoundLoop(source);
    }

    /// <summary>A sound bank by id (Movement, Voice), loaded once.</summary>
    public SoundBank Bank(string id)
    {
        if (banks.TryGetValue(id, out var bank) && bank != null) return bank;
        bank = SoundBank.Load(id);
        if (bank == null) Report($"there is no sound bank Resources/Audio/{id}.");
        return banks[id] = bank;
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var due = pending[i];
            if (due.At > now) continue;
            pending.RemoveAt(i);
            PlayItem(due.Item, transform.TransformPoint(due.LocalPoint), due.Surface, due.Volume);
        }
        FadeMouth(Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        pending.Clear();
        queued = default;
        lineUntil = reservedUntil = 0f;
        if (mouth != null) mouth.Stop();
    }

    private bool TryUtter(string key, float volume, float noise, bool isLine, out Utterance utterance)
    {
        utterance = default;
        var bank = Bank(VoiceBank);
        var clip = bank != null ? bank.Pick(key) : null;
        if (clip == null)
        {
            Report($"the Voice bank has no '{key}'.");
            return false;
        }
        var settings = bank.SettingsOf(key);
        utterance = new Utterance
        {
            Clip = clip, Key = key, Pitch = Mathf.Max(.1f, settings.RandomPitch()),
            Level = Mathf.Clamp01(settings.Volume * volume), Noise = noise * volume, IsLine = isLine
        };
        return true;
    }

    // Starts at once on a quiet mouth; otherwise what is playing fades out and this waits for it.
    private void Speak(Utterance utterance)
    {
        Build();
        // A grunt waiting its turn is not pushed aside by a breath.
        if (queued.Clip != null && queued.IsLine && !utterance.IsLine) return;
        if (mouth.isPlaying && mouthLevel > 0f)
        {
            queued = utterance;
            if (mouthFadeRate <= 0f) mouthFadeRate = mouthLevel / mouthFadeTime;
            return;
        }
        Begin(utterance);
    }

    private void Begin(Utterance utterance)
    {
        queued = default;
        mouthFadeRate = 0f;
        mouthLevel = utterance.Level;
        mouthSaysLine = utterance.IsLine;
        mouth.Stop();
        mouth.clip = utterance.Clip;
        mouth.pitch = utterance.Pitch;
        GameAudio.SetGain(mouth, mouthLevel);
        mouth.Play();
        Noise.Make(mouth.transform.position, utterance.Noise, gameObject);
        if (!utterance.IsLine) return;
        float length = utterance.Clip.length / utterance.Pitch;
        lineUntil = Time.unscaledTime + length;
        Spoke?.Invoke(utterance.Key, length);
    }

    private void FadeMouth(float dt)
    {
        if (mouth == null || mouthFadeRate <= 0f) return;
        mouthLevel = Mathf.MoveTowards(mouthLevel, 0f, mouthFadeRate * dt);
        GameAudio.SetGain(mouth, mouthLevel);
        if (mouthLevel > 0f && mouth.isPlaying) return;
        mouthFadeRate = 0f;
        mouth.Stop();
        if (queued.Clip != null) Begin(queued);
    }

    // A cue's grunt is due in a moment: a breath gives way now, so it has faded by the time the grunt comes.
    private void HoldMouth(float delay)
    {
        reservedUntil = Mathf.Max(reservedUntil, Time.time + delay);
        if (queued.Clip != null && !queued.IsLine) queued = default;
        if (mouth != null && mouth.isPlaying && !mouthSaysLine && mouthLevel > 0f && mouthFadeRate <= 0f)
            mouthFadeRate = mouthLevel / mouthFadeTime;
    }

    private static bool IsVoice(SoundCues.Item item) => string.Equals(item.Bank, VoiceBank, StringComparison.OrdinalIgnoreCase);

    // Plays one part of a cue. Returns the sound's table volume, 0 when nothing played.
    private float PlayItem(SoundCues.Item item, Vector3 point, string surface, float volume)
    {
        if (IsVoice(item))
            return Say(item.Key, item.Volume * volume) > 0f ? 1f : 0f;
        var bank = Bank(item.Bank);
        if (bank == null) return 0f;
        string key = item.OnSurface ? bank.Find(item.Key, surface, out _) : item.Key;
        var clip = key != null ? bank.Pick(key) : null;
        if (clip == null)
        {
            Report($"the {item.Bank} bank has no '{item.Key}{(item.OnSurface ? SoundCues.SurfaceMarker : "")}'.");
            return 0f;
        }
        var settings = bank.SettingsOf(key);
        var voice = FreeVoice();
        // Stealing a busy voice must end its old shot before its pitch and position change.
        voice.Source.Stop();
        voice.Source.transform.position = point;
        voice.Source.pitch = settings.RandomPitch();
        voice.Source.PlayOneShot(clip, Mathf.Clamp01(settings.Volume * item.Volume * volume));
        voice.FreeAt = Time.unscaledTime + clip.length / Mathf.Max(.1f, voice.Source.pitch);
        return settings.Volume;
    }

    // A voice that has finished, else the one finishing soonest.
    private Voice FreeVoice()
    {
        Build();
        Voice best = body[0];
        foreach (var voice in body)
        {
            if (voice.FreeAt <= Time.unscaledTime) return voice;
            if (voice.FreeAt < best.FreeAt) best = voice;
        }
        return best;
    }

    private void Build()
    {
        if (!rebuild && voices != null) return;
        rebuild = false;
        if (voices != null) Destroy(voices.gameObject);
        body.Clear();
        voices = new GameObject("Sounds").transform;
        voices.SetParent(transform, false);
        for (int i = 0; i < bodyVoices; i++)
            body.Add(new Voice { Source = NewSource("Body " + (i + 1), voices, AudioChannel.Movement, bodySpatialBlend) });
        mouth = NewSource("Mouth", voices, AudioChannel.Voice, voiceSpatialBlend);
        mouth.transform.localPosition = Vector3.up * mouthHeight;
        queued = default;
        mouthLevel = mouthFadeRate = 0f;
    }

    private AudioSource NewSource(string sourceName, Transform parent, AudioChannel channel, float spatialBlend)
    {
        var source = new GameObject(sourceName).AddComponent<AudioSource>();
        source.transform.SetParent(parent, false);
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = fullVolumeDistance;
        source.maxDistance = Mathf.Max(fullVolumeDistance + .1f, maxDistance);
        source.dopplerLevel = 0f;
        GameAudio.Bind(source, channel);
        return source;
    }

    private void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("[CharacterSounds] " + message, this);
    }
}
