using UnityEngine;

public enum ShoveImpactKind { None, Enemy, World }

/// <summary>One impact cue per shove, shared by armed and unarmed combat.</summary>
[DisallowMultipleComponent]
public sealed class PlayerShoveAudio : MonoBehaviour
{
    private const float OthersReach = 25f;
    private AudioSource source;
    private AudioClip previousEnemyClip;
    public ShoveImpactKind LastImpact { get; private set; }
    public AudioClip LastClip { get; private set; }
    /// <summary>A shove's impact sounded on this player, for the other PCs to play too.</summary>
    public event System.Action<ShoveImpactKind> Played;

    public void Play(ShoveImpactKind impact)
    {
        LastImpact = impact; LastClip = null;
        // None comes as a shove starts (ShoveSweep): the swing itself, SoundCues ShoveSwing (a COD MWII swipe), from the body.
        if (impact == ShoveImpactKind.None && isActiveAndEnabled && Application.isPlaying)
            GetComponent<CharacterSounds>()?.Play("ShoveSwing", transform.position + Vector3.up * 1.3f);
        var settings = CombatFeedbackSettings.Instance;
        if (impact == ShoveImpactKind.None || settings == null || !isActiveAndEnabled) return;
        LastClip = impact == ShoveImpactKind.Enemy ? PickEnemyClip(settings.shoveEnemySounds) : settings.shoveWorldSound;
        if (LastClip == null || !Application.isPlaying) return;
        Played?.Invoke(impact);
        // A friend's bash heard here (their copy, through the network) shows its shock too, a little ahead of their eyes;
        // this PC's own player's bashes show where they met (BashImpact).
        if (impact == ShoveImpactKind.Enemy && TryGetComponent(out PlayerMovement mover) && !mover.IsLocal) ShowFriendBash(mover);
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
            // One's own shove sounds flat; someone else's comes from where they stand.
            bool someoneElse = TryGetComponent(out PlayerMovement player) && !player.IsLocal;
            source.spatialBlend = someoneElse ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 2f; source.maxDistance = OthersReach;
            GameAudio.Bind(source, AudioChannel.Combat);
        }
        float volume = impact == ShoveImpactKind.Enemy ? settings.shoveEnemyVolume : settings.shoveWorldVolume;
        source.pitch = 1f + Random.Range(-settings.hitPitchVariance, settings.hitPitchVariance);
        source.PlayOneShot(LastClip, Mathf.Clamp01(volume));
    }
    private static void ShowFriendBash(PlayerMovement friend)
    {
        Transform eyes = PlayerView.EyesOf(friend);
        Vector3 forward = Vector3.ProjectOnPlane(eyes != null ? eyes.forward : friend.transform.forward, Vector3.up).normalized;
        // A copy without a view gives its root, at the feet: from there, about where eyes would be.
        Vector3 from = eyes != null && eyes != friend.transform ? eyes.position : friend.transform.position + Vector3.up * 1.35f;
        BashImpact.Play(from + forward * .9f - Vector3.up * .15f, forward, .8f);
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
