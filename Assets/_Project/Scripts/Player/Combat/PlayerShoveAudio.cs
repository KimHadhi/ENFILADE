using UnityEngine;

public enum ShoveImpactKind { None, Enemy, World }

/// <summary>One impact cue per shove, shared by armed and unarmed combat.</summary>
[DisallowMultipleComponent]
public sealed class PlayerShoveAudio : MonoBehaviour
{
    private AudioSource source;
    private AudioClip previousEnemyClip;
    public ShoveImpactKind LastImpact { get; private set; }
    public AudioClip LastClip { get; private set; }

    public void Play(ShoveImpactKind impact)
    {
        LastImpact = impact; LastClip = null;
        var settings = CombatFeedbackSettings.Instance;
        if (impact == ShoveImpactKind.None || settings == null || !isActiveAndEnabled) return;
        LastClip = impact == ShoveImpactKind.Enemy ? PickEnemyClip(settings.shoveEnemySounds) : settings.shoveWorldSound;
        if (LastClip == null || !Application.isPlaying) return;
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 0f;
            GameAudio.Bind(source, AudioChannel.Combat);
        }
        float volume = impact == ShoveImpactKind.Enemy ? settings.shoveEnemyVolume : settings.shoveWorldVolume;
        source.PlayOneShot(LastClip, Mathf.Clamp01(volume));
    }
    private AudioClip PickEnemyClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        int alternatives = 0; AudioClip fallback = null;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            fallback = clip;
            if (clip != previousEnemyClip) alternatives++;
        }
        if (alternatives == 0) return previousEnemyClip = fallback;
        int choice = Random.Range(0, alternatives);
        foreach (var clip in clips)
            if (clip != null && clip != previousEnemyClip && choice-- == 0) return previousEnemyClip = clip;
        return previousEnemyClip = fallback;
    }
    private void OnDisable() { if (source != null) source.Stop(); }
}
