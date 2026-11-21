using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// A blow that lands jolts the body (the user, 2026-11-22, choosing where BIPER Combat Girls' hits go: "맞으면 몸만
/// 움찔", with the view, the gun and the controls as they were). The spine takes BIPER's hit clip on a layer of its own
/// (FlinchLayer, the spine and chest only, over the moves and the fists' clench, under a punch, a kick, a dodge and an
/// execution): a light blow R_Hit_Upper (thrown back), one that takes flinchHardDamage or more R_Hit_Low (doubled over).
/// The hips and legs carry on with the moves and FPS Animation Framework keeps the arms on the gun. The camera keeps to
/// where the eyes were when the blow came (BodyCamera.Place's hold) and the head bends back under it (HeadReach), so in
/// first person the flinch shows only in the chest under the view; the hit's own camera punch is the camera effects'.
/// Friends see it on their PCs from the hurt they are told of: PlayerNetworkCombat's HurtTakenRpc carries what the blow
/// took, so they pick the same clip. Only standing: BIPER has no crouched hit, and the standing clip's upright spine over
/// crouched legs straightened the body up; a fall's hurt is the landing's, and a hold's grab takes no flinch. A body
/// already reeling takes the next blow in its stride until it is coming back up (half the clip); a harder one takes over,
/// the two clips crossing in flinchBlendIn.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Flinch")]
    [SerializeField, Tooltip("A blow that lands: the spine thrown back (light), and doubled over (flinchHardDamage or more).")]
    private string flinchLight = "R_Hit_Upper", flinchHard = "R_Hit_Low";
    [SerializeField, Min(0f), Tooltip("A blow that takes this much or more (shield and health together) doubles the body over.")]
    private float flinchHardDamage = 20f;
    [SerializeField, Min(.01f), Tooltip("How quickly a flinch takes the spine (s), and gives it back at its clip's end.")]
    private float flinchBlendIn = .05f, flinchBlendOut = .25f;

    private AnimationMixerPlayable flinchMixer;
    private AvatarMask spineMask;
    private AnimationClipPlayable flinchLightClip, flinchHardClip;
    private float flinchWeight, flinchLeft, flinchLength, flinchHardShare;
    private bool flinchIsHard;
    private PlayerDamageReceiver flinchReceiver;

    /// <summary>How far a flinch has the spine now (0-1): the camera holds where the eyes were by as much (PlayerBody.Look).</summary>
    private float FlinchHold => flinchWeight;

    // From BuildLayers.
    private void BuildFlinch()
    {
        spineMask = Mask(AvatarMaskBodyPart.Body);
        layers.SetLayerMaskFromAvatarMask(FlinchLayer, spineMask);
        flinchMixer = AnimationMixerPlayable.Create(graph, 2);
        graph.Connect(flinchMixer, 0, layers, FlinchLayer);
        flinchLightClip = FlinchClip(flinchLight, 0);
        flinchHardClip = FlinchClip(flinchHard, 1);
        flinchWeight = flinchLeft = 0f;
        layers.SetInputWeight(FlinchLayer, 0f);
        ListenForBlows();
    }

    private AnimationClipPlayable FlinchClip(string clipName, int input)
    {
        var clip = string.IsNullOrEmpty(clipName) ? null : profile.Find(clipName);
        if (clip == null)
        {
            if (!string.IsNullOrEmpty(clipName)) Report($"clip '{clipName}' is not in the PlayerBody profile.");
            return default;
        }
        var playable = ClipPlayable(clip);
        playable.SetSpeed(0);
        graph.Connect(playable, 0, flinchMixer, input);
        return playable;
    }

    private void TeardownFlinch()
    {
        if (flinchReceiver != null) flinchReceiver.HitResolved -= OnBlow;
        flinchReceiver = null;
        flinchMixer = default;
        flinchLightClip = flinchHardClip = default;
        flinchWeight = flinchLeft = 0f;
        if (spineMask != null) Destroy(spineMask);
    }

    // This PC's own player's blows are judged here (PlayerDamageReceiver: a copy's never are); a copy's come from its PC
    // (PlayerNetworkCombat → Flinch).
    private void ListenForBlows()
    {
        var receiver = player != null ? player.DamageReceiver : null;
        if (receiver == flinchReceiver) return;
        if (flinchReceiver != null) flinchReceiver.HitResolved -= OnBlow;
        flinchReceiver = receiver;
        if (flinchReceiver != null) flinchReceiver.HitResolved += OnBlow;
    }

    private void OnBlow(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || hit.TakesHold || hit.Kind == DamageKind.Fall || flinchReceiver == null) return;
        Flinch(flinchReceiver.LastShieldTaken + flinchReceiver.LastHealthTaken);
    }

    /// <summary>A blow took <paramref name="taken"/> (shield and health) off this player: the body flinches, on every PC alike.</summary>
    internal void Flinch(float taken)
    {
        if (!flinchMixer.IsValid() || dead || taken <= 0f || !OnFeetForFlinch) return;
        bool hard = taken >= flinchHardDamage;
        var clip = hard ? flinchHardClip : flinchLightClip;
        if (!clip.IsValid()) return;
        // Reeling from the last blow, the body takes this one in its stride until it comes back up; a harder one takes over.
        if (flinchLeft > flinchLength * .5f && (flinchIsHard || !hard)) return;
        clip.SetTime(0);
        clip.SetSpeed(1);
        clip.SetDone(false);
        // From no flinch the clip takes the layer whole; from the other one the two cross (BlendFlinch).
        if (flinchWeight <= 0f) flinchHardShare = hard ? 1f : 0f;
        flinchIsHard = hard;
        flinchLength = flinchLeft = clip.GetAnimationClip().length;
    }

    private bool OnFeetForFlinch => state == MovementState.Standing;

    // From BlendLayers: the flinch plays out, and goes at once off the feet, into a crouch or into an execution.
    private void BlendFlinch(float dt)
    {
        if (!flinchMixer.IsValid()) return;
        // The receiver may come after the body is made (PlayerMovement sets it up): a reference check a frame.
        ListenForBlows();
        flinchLeft = dead || !OnFeetForFlinch || PlaysPaired ? 0f : Mathf.Max(0f, flinchLeft - dt);
        bool on = flinchLeft > flinchBlendOut;
        flinchWeight = Mathf.MoveTowards(flinchWeight, on ? 1f : 0f, dt / (on ? flinchBlendIn : flinchBlendOut));
        flinchHardShare = Mathf.MoveTowards(flinchHardShare, flinchIsHard ? 1f : 0f, dt / flinchBlendIn);
        flinchMixer.SetInputWeight(0, 1f - flinchHardShare);
        flinchMixer.SetInputWeight(1, flinchHardShare);
        layers.SetInputWeight(FlinchLayer, flinchWeight);
    }
}
