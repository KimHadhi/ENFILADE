using UnityEngine;

public enum ShoveImpactKind { None, Enemy, World, Door }

/// <summary>
/// One impact cue per shove, shared by armed and unarmed combat. A kick on a door (ShoveMoves) is Beatrun's door barge,
/// the SoundCues row DoorKick, from the body's own voices (CharacterSounds), so it carries to the zombies on whichever PC
/// plays it; without that row it is the shove's world sound.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerShoveAudio : MonoBehaviour
{
    private const float OthersReach = 25f;
    private const string DoorCue = "DoorKick";
    private AudioSource source;
    private AudioClip previousEnemyClip;
    public ShoveImpactKind LastImpact { get; private set; }
    public AudioClip LastClip { get; private set; }
    /// <summary>A shove's impact sounded on this player, for the other PCs to play too.</summary>
    public event System.Action<ShoveImpactKind> Played;

    public void Play(ShoveImpactKind impact)
    {
        LastImpact = impact; LastClip = null;
        if (impact == ShoveImpactKind.Door && PlayDoor()) return;
        var settings = CombatFeedbackSettings.Instance;
        if (impact == ShoveImpactKind.None || settings == null || !isActiveAndEnabled) return;
        LastClip = impact == ShoveImpactKind.Enemy ? PickEnemyClip(settings.shoveEnemySounds) : settings.shoveWorldSound;
        if (LastClip == null || !Application.isPlaying) return;
        Played?.Invoke(impact);
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
    // The door barge, a step ahead of the body, where the foot met the door.
    private bool PlayDoor()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !SoundCues.TryFind(DoorCue, out _)
            || !TryGetComponent(out CharacterSounds sounds)) return false;
        Played?.Invoke(ShoveImpactKind.Door);
        sounds.Play(DoorCue, transform.position + Vector3.up + transform.forward * .9f);
        return true;
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
