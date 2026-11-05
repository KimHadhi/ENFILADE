using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The reload kick on the legs. When a kick leaves (ShoveMoves.Kicked: at the press on this player's own PC, through
/// PlayerNetworkCombat on everyone else's copy), its clip plays once on both legs. The hips, the spine and the arms carry
/// on as they were, so the hands stay on the reload and the eyes, which carry the camera, stay where they are. The foot
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
    private AvatarMask legMask;
    private ShoveMoves kicks;
    private float kickLength, kickWeight;
    private bool kicking;

    // From BuildLayers, once the layer mixer exists.
    private void BuildKick()
    {
        legMask = Mask(AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        layers.SetLayerMaskFromAvatarMask(KickLayer, legMask);
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
    }

    private void OnKicked()
    {
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
