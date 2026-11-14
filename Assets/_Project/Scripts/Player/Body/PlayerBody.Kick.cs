using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The reload kick. When a kick leaves (ShoveMoves.Kicked: at the press on this player's own PC, through
/// PlayerNetworkCombat on everyone else's copy), its clip plays once on both legs and on the body over them, the pelvis
/// and the spine (the Body part), as it was made in the Blender shove workspace: a pelvis moved there, leaning back into
/// the kick or dropping onto the planted leg, moves here too, and the eyes, which carry the camera, go with it (the user,
/// 2026-11-14, asked whether to send the kick's hips at the cost of the view moving with them: "골반도 보내"). Before,
/// only the legs played, so a pelvis posed there was dropped and its legs stood under a pelvis that hadn't moved. The arms
/// carry on as they were, so the hands stay on the reload; the dodge and the turn on the spot keep the legs alone. The foot
/// IK keeps the clip's own lift (PlayerBody.Feet), so the kicking foot rises while the other stays planted. Standing
/// still, legs left facing away from the view turn to it with the kick, so the foot goes out where the eyes look, which
/// is where ShoveMoves sends it. Names and numbers come from Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Reload kick")]
    [SerializeField, Tooltip("Played once on the legs when a kick leaves (ShoveMoves.Kicked).")]
    private string kick = "kick";
    [SerializeField, Min(.1f), Tooltip("Playback speed of the kick clip.")]
    private float kickClipSpeed = 1f;
    [SerializeField, Min(.01f), Tooltip("Time for the kick clip to take the legs, and to give them back before it ends (s).")]
    private float kickBlendIn = .06f, kickBlendOut = .2f;
    [SerializeField, Min(0f), Tooltip("Standing still, how fast legs left facing away from the view turn to it with a kick (degrees/s).")]
    private float kickTurnSpeed = 360f;

    private AnimationClipPlayable kickClip;
    // The legs alone (the dodge's and the turn's layers use it too), and the kick's own: the legs and the body over them.
    private AvatarMask legMask, kickMask;
    private ShoveMoves kicks;
    private float kickLength, kickWeight;
    private bool kicking;

    // From BuildLayers, once the layer mixer exists.
    private void BuildKick()
    {
        legMask = Mask(AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        kickMask = Mask(AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        layers.SetLayerMaskFromAvatarMask(KickLayer, kickMask);
        if (string.IsNullOrEmpty(kick)) return;
        var clip = profile.Find(kick);
        if (clip == null)
        {
            Report($"clip '{kick}' is not in the PlayerBody profile.");
            return;
        }
        kickClip = ClipPlayable(clip);
        // Held at its start until a kick leaves.
        kickClip.SetSpeed(0);
        kickLength = Mathf.Max(.01f, clip.length);
        graph.Connect(kickClip, 0, layers, KickLayer);
        kicks = ShoveMoves.For(player);
        if (kicks != null) kicks.Kicked += OnKicked;
    }

    private void TeardownKick()
    {
        if (kicks != null) kicks.Kicked -= OnKicked;
        kicks = null;
        kickClip = default;
        kickWeight = 0f;
        kicking = false;
        if (legMask != null) Destroy(legMask);
        if (kickMask != null) Destroy(kickMask);
    }

    private void OnKicked()
    {
        // Crouched, the kick is the leg sweep, posed by IK (PlayerBody.Sweep).
        if (state == MovementState.Crouching) { BeginSweep(); return; }
        if (!kickClip.IsValid()) return;
        kickClip.SetTime(0);
        kickClip.SetSpeed(kickClipSpeed);
        kickClip.SetDone(false);
        kicking = true;
    }

    // From BlendLayers, after ChoosePose has set where the legs face.
    private void BlendKick(float dt)
    {
        if (!kickClip.IsValid()) return;
        // Counted on the clip's own clock, so a slowed copy (SetPace) kicks as slowly as its legs play.
        float left = (kickLength - (float)kickClip.GetTime()) / kickClipSpeed;
        // Any other move takes the legs back: a vault, a slide or a climb has legs of its own.
        if (dead || left <= 0f || (state != MovementState.Standing && state != MovementState.Crouching)) kicking = false;
        bool on = kicking && left > kickBlendOut;
        kickWeight = Mathf.MoveTowards(kickWeight, on ? 1f : 0f, dt / (on ? kickBlendIn : kickBlendOut));
        layers.SetInputWeight(KickLayer, kickWeight);
        // Standing still the legs may face up to turnStartAngle away from the view (TurnOnTheSpot).
        if (!kicking || !legsPlanted || state != MovementState.Standing) return;
        float look = transform.eulerAngles.y;
        float offset = Mathf.MoveTowards(Mathf.DeltaAngle(look, legYaw), 0f, kickTurnSpeed * dt);
        legYaw = look + offset;
        targetYaw = offset;
    }
}
