using UnityEngine;

public enum FistSound { None, Swing, Hit, HeavyHit, WallHit }

/// <summary>
/// Fist swing, enemy hit and wall contact cues. Clips live in Resources/Combat/Feedback. The swing has its own voice: a
/// contact pulls it down, so the hit lands clean instead of inside a whoosh.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFistAudio : MonoBehaviour
{
    // How much of the swing is left once the punch has landed.
    private const float SwingUnderHit = .3f;
    private AudioSource swingVoice, hitVoice;
    private AudioClip previousSwing, previousHit, previousHeavy, previousWall;
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
    public void PlayHeavyHit()
    {
        var settings = CombatFeedbackSettings.Instance;
        Emit(FistSound.HeavyHit, settings != null ? Pick(settings.fistHeavyHitSounds, ref previousHeavy) : null,
            settings != null ? settings.fistHeavyHitVolume : 0f);
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
        if (sound == FistSound.Swing)
        {
            if (swingVoice == null) swingVoice = NewVoice();
            GameAudio.SetGain(swingVoice, 1f);
            swingVoice.PlayOneShot(clip, Mathf.Clamp01(volume));
            return;
        }
        if (hitVoice == null) hitVoice = NewVoice();
        if (swingVoice != null) GameAudio.SetGain(swingVoice, SwingUnderHit);
        float variance = CombatFeedbackSettings.InstanceOrDefaults.hitPitchVariance;
        hitVoice.pitch = 1f + Random.Range(-variance, variance);
        hitVoice.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
    private AudioSource NewVoice()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false; source.spatialBlend = 0f;
        GameAudio.Bind(source, AudioChannel.Combat);
        return source;
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
    private void OnDisable()
    {
        if (swingVoice != null) swingVoice.Stop();
        if (hitVoice != null) hitVoice.Stop();
    }
}
