using UnityEngine;

public enum CombatCue { Block, Parry, EnemyAttackStart, PerfectEvade }

[DisallowMultipleComponent]
public sealed class CombatCueAudio : MonoBehaviour
{
    private AudioSource source;
    private AudioClip previousBlock, previousParry;
    private bool paused;
    public CombatCue LastCue { get; private set; }
    public int PlayCount { get; private set; }
    public static CombatCueAudio Play(GameObject owner, CombatCue cue)
    {
        var audio = owner.GetComponent<CombatCueAudio>() ?? owner.AddComponent<CombatCueAudio>();
        audio.Emit(cue); return audio;
    }
    private void Emit(CombatCue cue)
    {
        LastCue = cue; PlayCount++;
        var settings = CombatFeedbackSettings.Instance;
        if (settings == null || !Application.isPlaying) return;
        AudioClip clip; float volume;
        switch (cue)
        {
            case CombatCue.Block: clip = Pick(settings.blockSounds, ref previousBlock); volume = settings.blockVolume; break;
            case CombatCue.Parry: clip = Pick(settings.parrySounds, ref previousParry); volume = settings.parryVolume; break;
            case CombatCue.PerfectEvade: clip = settings.perfectEvadeSound; volume = settings.perfectEvadeVolume; break;
            default: clip = settings.enemyAttackStartSound; volume = settings.enemyAttackVolume; break;
        }
        if (clip == null) return;
        if (source == null) { source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; }
        source.spatialBlend = cue == CombatCue.EnemyAttackStart ? 1f : 0f;
        source.minDistance = 2f; source.maxDistance = 20f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
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
    public void Stop() { if (source != null) source.Stop(); }
    private void Update()
    {
        if (source == null || LastCue != CombatCue.EnemyAttackStart) return;
        if (Time.timeScale <= 0f) { if (!paused) source.Pause(); paused = true; return; }
        if (paused) { source.UnPause(); paused = false; }
        source.pitch = Mathf.Clamp(Time.timeScale, .01f, 3f);
    }
    private void OnDisable() => Stop();
}
