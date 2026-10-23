using UnityEngine;

public enum CombatCue { Block, Parry, EnemyAttackStart, PerfectEvade }

[DisallowMultipleComponent]
public sealed class CombatCueAudio : MonoBehaviour
{
    private AudioSource source;
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
            case CombatCue.Block: clip = settings.blockSound; volume = settings.blockVolume; break;
            case CombatCue.Parry: clip = settings.parrySound; volume = settings.parryVolume; break;
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
