using UnityEngine;

public enum CombatCue { Block, Parry, ParryBreak, Grab, EnemyAttackStart, PerfectEvade }

[DisallowMultipleComponent]
public sealed class CombatCueAudio : MonoBehaviour
{
    private AudioSource source;
    private EnemyNetwork network;
    private AudioClip previousBlock, previousParry, previousParryBreak, previousGrab;
    private bool paused;
    public CombatCue LastCue { get; private set; }
    public int PlayCount { get; private set; }
    // The pitch the attack cue was started at (a grab's is lower, a leap's higher, a brute's lowest: EnemyCombatAI.CuePitch).
    private float cuePitch = 1f;
    /// <summary>
    /// inWorld plays it where the owner is, for a cue that isn't happening to this PC's player. Gain: how many times
    /// the cue's own volume (an attack aimed at this PC's own player is louder, ZombieVoice.SignalGain).
    /// </summary>
    public static CombatCueAudio Play(GameObject owner, CombatCue cue, float pitch = 1f, bool inWorld = false, float gain = 1f)
    {
        var audio = owner.GetComponent<CombatCueAudio>() ?? owner.AddComponent<CombatCueAudio>();
        audio.Emit(cue, pitch, inWorld, gain); return audio;
    }
    /// <summary>Makes the owner's cue voice now (at spawn), so the first cue of a fight adds no components mid-frame.</summary>
    public static void Prepare(GameObject owner)
    {
        if (owner == null || !Application.isPlaying) return;
        var audio = owner.GetComponent<CombatCueAudio>() ?? owner.AddComponent<CombatCueAudio>();
        audio.EnsureSource();
    }
    private void EnsureSource()
    {
        if (source != null) return;
        source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
        GameAudio.Bind(source, AudioChannel.Combat);
    }
    private void Emit(CombatCue cue, float pitch, bool inWorld, float gain)
    {
        LastCue = cue; PlayCount++;
        if (cue == CombatCue.EnemyAttackStart) cuePitch = pitch;
        var settings = CombatFeedbackSettings.Instance;
        if (settings == null || !Application.isPlaying) return;
        AudioClip clip; float volume;
        switch (cue)
        {
            case CombatCue.Block: clip = Pick(settings.blockSounds, ref previousBlock); volume = settings.blockVolume; break;
            case CombatCue.Parry: clip = Pick(settings.parrySounds, ref previousParry); volume = settings.parryVolume; break;
            case CombatCue.ParryBreak: clip = Pick(settings.parryBreakSounds, ref previousParryBreak); volume = settings.parryBreakVolume; break;
            case CombatCue.Grab: clip = Pick(settings.grabSounds, ref previousGrab); volume = settings.grabVolume; break;
            case CombatCue.PerfectEvade: clip = settings.perfectEvadeSound; volume = settings.perfectEvadeVolume; break;
            default: clip = settings.enemyAttackStartSound; volume = settings.enemyAttackVolume; break;
        }
        if (clip == null) return;
        EnsureSource();
        source.spatialBlend = cue == CombatCue.EnemyAttackStart || inWorld ? 1f : 0f;
        source.minDistance = 2f; source.maxDistance = 20f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.pitch = pitch;
        source.PlayOneShot(clip, Mathf.Clamp01(volume * Mathf.Max(0f, gain)));
    }
    // Avoid repeating the same variant when another non-null clip is available.
    private static AudioClip Pick(AudioClip[] clips, ref AudioClip previous)
    {
        if (clips == null || clips.Length == 0) return null;
        int alternatives = 0; AudioClip fallback = null;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            fallback = clip;
            if (clip != previous) alternatives++;
        }
        if (alternatives == 0) return previous = fallback;
        int choice = Random.Range(0, alternatives);
        foreach (var clip in clips)
            if (clip != null && clip != previous && choice-- == 0) return previous = clip;
        return previous = fallback;
    }
    /// <summary>Cuts an attack's cue short. A stunned enemy calls this every frame, so a parry's cue on it must ring out.</summary>
    public void Stop() { if (source != null && LastCue == CombatCue.EnemyAttackStart) source.Stop(); }
    private void Update()
    {
        if (source == null || LastCue != CombatCue.EnemyAttackStart) return;
        if (Time.timeScale <= 0f) { if (!paused) source.Pause(); paused = true; return; }
        if (paused) { source.UnPause(); paused = false; }
        // The cue tells when the blow lands, so it keeps the attacker's time whichever way that was slowed. A slowed game
        // also lowers every world sound (GameAudio.WorldPitch), which already counts that time, so it is taken back out.
        // The cue's own pitch stays in it: written without it, every attack's cue went back to one pitch a frame after
        // it started, and a grab, a leap and a brute's blow could not be told apart by ear as they were meant to be.
        if (network == null) TryGetComponent(out network);
        source.pitch = Mathf.Clamp(cuePitch * Time.timeScale * (network != null ? network.Pace : 1f) / GameAudio.WorldPitch, .01f, 3f);
    }
    private void OnDisable() => Stop();
}
