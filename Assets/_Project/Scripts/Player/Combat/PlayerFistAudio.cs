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
    private const float SwingUnderHit = .3f, OthersReach = 25f;
    // Two swings and four hits at most, so an autoclicker can't use up the real voices (VoiceRing).
    private const int SwingVoices = 2, HitVoices = 4;
    private VoiceRing swingVoices, hitVoices;
    private AudioClip previousSwing, previousHit, previousHeavy, previousWall;
    public FistSound LastSound { get; private set; }
    public AudioClip LastClip { get; private set; }
    public int PlayCount { get; private set; }
    /// <summary>A fist sound started on this player, for the other PCs to play too.</summary>
    public event System.Action<FistSound> Played;

    public void Play(FistSound sound)
    {
        switch (sound)
        {
            case FistSound.Swing: PlaySwing(); break;
            case FistSound.Hit: PlayHit(); break;
            case FistSound.HeavyHit: PlayHeavyHit(); break;
            case FistSound.WallHit: PlayWallHit(); break;
        }
    }

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
        Played?.Invoke(sound);
        if (sound == FistSound.Swing)
        {
            if (swingVoices == null) swingVoices = NewRing(SwingVoices);
            var swing = swingVoices.Take();
            GameAudio.SetGain(swing, 1f);
            swing.PlayOneShot(clip, Mathf.Clamp01(volume));
            return;
        }
        if (hitVoices == null) hitVoices = NewRing(HitVoices);
        swingVoices?.ForEach(swing => GameAudio.SetGain(swing, SwingUnderHit));
        float variance = CombatFeedbackSettings.InstanceOrDefaults.hitPitchVariance;
        var hit = hitVoices.Take();
        hit.pitch = 1f + Random.Range(-variance, variance);
        hit.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
    private VoiceRing NewRing(int count)
    {
        var sources = new AudioSource[count];
        for (int i = 0; i < count; i++) sources[i] = NewVoice();
        return new VoiceRing(sources);
    }
    private AudioSource NewVoice()
    {
        var source = gameObject.AddComponent<AudioSource>();
        // One's own fists sound flat; someone else's come from where they stand.
        bool someoneElse = TryGetComponent(out PlayerMovement player) && !player.IsLocal;
        source.playOnAwake = false; source.spatialBlend = someoneElse ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 2f; source.maxDistance = OthersReach;
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
        swingVoices?.Stop();
        hitVoices?.Stop();
    }
}
