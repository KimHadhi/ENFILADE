using UnityEngine;

public enum FistSound { None, Swing, Hit, WallHit }

/// <summary>Fist swing, enemy hit and wall contact cues. Clips live in Resources/Combat/Feedback.</summary>
[DisallowMultipleComponent]
public sealed class PlayerFistAudio : MonoBehaviour
{
    private AudioSource source;
    private AudioClip previousSwing, previousHit, previousWall;
    public FistSound LastSound { get; private set; }
    public AudioClip LastClip { get; private set; }
    public int PlayCount { get; private set; }

    public void PlaySwing()
    {
        var settings = CombatFeedbackSettings.Instance;
        Emit(FistSound.Swing, settings != null ? Pick(settings.fistSwingSounds, ref previousSwing) : null,
            settings != null ? settings.fistSwingVolume : 0f);
    }
    public void PlayHit()
    {
        var settings = CombatFeedbackSettings.Instance;
        Emit(FistSound.Hit, settings != null ? Pick(settings.fistHitSounds, ref previousHit) : null,
            settings != null ? settings.fistHitVolume : 0f);
    }
    public void PlayWallHit()
    {
        var settings = CombatFeedbackSettings.Instance;
        Emit(FistSound.WallHit, settings != null ? Pick(settings.fistWallSounds, ref previousWall) : null,
            settings != null ? settings.fistWallVolume : 0f);
    }
    private void Emit(FistSound sound, AudioClip clip, float volume)
    {
        LastSound = sound; LastClip = clip; PlayCount++;
        if (clip == null || !isActiveAndEnabled || !Application.isPlaying) return;
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
    /// <summary>Random clip that differs from the previous pick whenever an alternative exists.</summary>
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
    private void OnDisable() { if (source != null) source.Stop(); }
}
