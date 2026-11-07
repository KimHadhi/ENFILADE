using UnityEngine;

/// <summary>
/// The quick vaults (Over, Kong) bring the body down onto a hand on the top, after PlayerBody.VaultHeight has already let
/// the clip carry the body instead of the capsule. What the arm is still short is made up here:
/// - A hand plants once it stops coming closer, across, to its place on the top, if the body can come down far enough for
///   that arm to reach it.
/// - While it is down, the body comes straight down by what that arm is short, so the view dips onto the hands as a body
///   does going over them. Only down: holding the body back along the arm, as the high moves do (EdgeHoldBack), would drag
///   the feet back through the top.
/// - Never further than edgePullDown arm lengths, nor into the feet's room (PlayerBody.FeetRoomNow and FeetRoomSoon: no
///   foot within reach of the floor under it or the top it is about to go over). It eases down and back up
///   (edgePullSmooth), so the view dips onto the hands instead of jolting.
/// - A hand's weight on the top follows an S-curve, reaching out over edgeEase and letting go over edgeQuickLetGo, so it
///   neither starts nor stops with a jerk.
/// - Once even a straight arm can't reach with what is left, the hand comes off, the push-off.
/// Nothing is per clip or per character: a clip swapped in later is brought down by whatever its arms are short.
/// </summary>
public sealed partial class PlayerBodyHands
{
    [SerializeField, Range(0f, 2f), Tooltip("In a quick vault, how far at most the body comes down for a hand on the top, in arm lengths.")]
    private float edgePullDown = 1f;
    [SerializeField, Tooltip("In a quick vault, how smoothly the body follows the hands down and lets them go (s): going down, coming back up.")]
    private Vector2 edgePullSmooth = new Vector2(.08f, .12f);
    [SerializeField, Min(.01f), Tooltip("In a quick vault, how long a hand takes to come off the top or give up reaching for it (s).")]
    private float edgeQuickLetGo = .2f;

    // How far the body is down for the hands now (m) and how fast that is changing, and how far it may be down this frame (m).
    private float pulled, pullSpeed, pullRoom;

    /// <summary>The hands have the body down for them, so its feet's room is still wanted (PlayerBody.VaultHeight).</summary>
    internal bool PullsBody => pulled > 0f;

    /// <summary>Temporary, for Logs/body-probe.txt: the pull, its room and each hand's state.</summary>
    internal string PullProbe => $"pull {pulled:0.000} room {pullRoom:0.000} held back {edgeHeld.magnitude:0.000} "
        + $"hands {edgeHands[0]}/{edgeHands[1]} fingers {edgeContacts[0]:0.00}/{edgeContacts[1]:0.00}";

    private void ResetPull() => pulled = pullSpeed = pullRoom = 0f;

    /// <summary>
    /// A hand in a quick vault (ClearsTop). It reaches out for its place on the top from the start of the move, as a
    /// vaulter's hands go out to the obstacle, and plants as soon as the arm, with what the body can come down, gets there;
    /// it comes off once even a straight arm no longer can, or the body has gone past its place without reaching it.
    /// Before, it waited for the clip's hand to come near: at Beatrun's speed the body was already over the edge, and the
    /// hand landed late and left at once (user, 2026-11-07: "vaulting은 빠른데 손이 늦게 잡으니"). Returns whether it is
    /// still reaching.
    /// </summary>
    private bool TrackHandOverTop(ArmIK arm, int hand, Vector3 wrist, float reach)
    {
        // The shoulder is already down by pulled this frame; this much more is left.
        float more = Mathf.Max(0f, pullRoom - pulled);
        Vector3 shoulder = arm.Upper.position;
        var was = edgeHands[hand];
        // edgeAway holds, for a hand reaching out, the closest its shoulder has come to its place on the top.
        float distance = Vector3.Distance(shoulder, wrist);
        if (was == EdgeHand.Waiting)
        {
            if (Drop(shoulder, wrist, reach * edgeArmUse) <= more) edgeHands[hand] = EdgeHand.Planted;
            // Past its place, or the place getting farther again without the arm ever reaching it: it gives up. A body
            // too high above a low top kept both arms out at full stretch all through the climb and flung them back as it
            // ended (user, 2026-11-07: "오른손이 끝날 때 즈음에 확 튀는데").
            else if (Vector3.Dot(shoulder - wrist, Onward()) > 0f || distance > edgeAway[hand] + RecedeSlack) edgeHands[hand] = EdgeHand.Gone;
        }
        else if (was == EdgeHand.Planted && Drop(shoulder, wrist, reach) > more + ReachSlack)
            edgeHands[hand] = EdgeHand.Gone;
        edgeAway[hand] = Mathf.Min(edgeAway[hand], distance);
        if (was != EdgeHand.Gone && edgeHands[hand] == EdgeHand.Gone) HoldDownAfterPress(arm, hand, wrist);
        return edgeHands[hand] == EdgeHand.Waiting;
    }

    // How far a reaching hand's place may draw away again before it gives up (m): the body's own sway.
    private const float RecedeSlack = .03f;

    // Off a low top a hand that lets go rises no higher under its shoulder than it is now (KeepPushedHandsDown). Where it
    // is: the clip's hand (arm.Hand, not yet placed this frame) as far toward its place on the top as it had gone.
    private void HoldDownAfterPress(ArmIK arm, int hand, Vector3 wrist)
    {
        if (player.Abilities.Traversal.Obstacle.Kind != VaultKind.Onto) return;
        pushedUnderShoulder[hand] = Mathf.Lerp(arm.Hand.position.y, wrist.y, edgeShares[hand]) - arm.Upper.position.y;
    }

    /// <summary>
    /// The move is over with a hand still reaching out or pressing: it lets go now, held down as if it had pushed off,
    /// instead of keeping its reach until Hold.Ledge fades.
    /// </summary>
    private void LetGoAfterQuickPress()
    {
        if (!PressesQuickly(player.Abilities.Traversal.Obstacle.Kind) || left == null || right == null) return;
        LetGo(left, 0, true);
        LetGo(right, 1, false);

        void LetGo(ArmIK arm, int hand, bool isLeft)
        {
            if (edgeHands[hand] == EdgeHand.Gone) return;
            edgeHands[hand] = EdgeHand.Gone;
            if (LedgeHandTarget(arm, isLeft, out Vector3 wrist, out _, out _)) HoldDownAfterPress(arm, hand, wrist);
        }
    }

    // The way over the obstacle, level: into its near face.
    private Vector3 Onward()
    {
        Vector3 inward = Vector3.ProjectOnPlane(-player.Abilities.Traversal.Obstacle.EdgeNormal, Vector3.up);
        return inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
    }

    // Room for a straight arm's last millimetres, so a hand at full stretch doesn't come off for rounding; and how close an
    // eased pull counts as gone (m).
    private const float ReachSlack = .005f, Settled = .0005f;

    /// <summary>
    /// How far the body is down for the hands this frame (m). Called from EdgeHoldBack, after PlayerBody.VaultHeight and
    /// before the eye camera and the hands are placed.
    /// </summary>
    private float PullOverTop(float arm, float dt)
    {
        var obstacle = player.Abilities.Traversal.Obstacle;
        bool over = Climbing(player.CurrentState) && ClearsTop(obstacle.Kind) && !body.IsDead && left != null && right != null
            && obstacle.Surface != null && body.InQuickVault;
        if (!over && pulled <= 0f)
        {
            pullRoom = 0f;
            return 0f;
        }
        float soon = Mathf.Min(body.FeetRoomNow, body.FeetRoomSoon);
        pullRoom = over ? Mathf.Min(edgePullDown * arm, soon) : 0f;
        float want = over ? Mathf.Min(Mathf.Max(Want(left, true), Want(right, false)), pullRoom) : 0f;
        // Eased both ways (edgePullSmooth), the view on the eyes with it. It used to follow at once: a hand on the top for
        // 50 ms took the view 0.25-0.35 m down and straight back up, and the feet's room closing snapped it up (2026-11-07).
        // A foot may graze what is under it for that moment.
        float smooth = Mathf.Max(.001f, want > pulled ? edgePullSmooth.x : edgePullSmooth.y);
        pulled = Mathf.SmoothDamp(pulled, want, ref pullSpeed, smooth, Mathf.Infinity, dt);
        if (pulled <= Settled && want <= 0f) pulled = pullSpeed = 0f;
        return pulled;

        // The drop this hand asks for, as far as it is on the top. Only a hand on it: one still on its way, or one gone,
        // asks for none. A gone hand that had never got there pulled the body down as it let go, the camera with it
        // (user, 2026-11-07: one-hand vault "카메라가 갑자기 쑥 하고 낮아지는").
        float Want(ArmIK limb, bool isLeft)
        {
            int hand = isLeft ? 0 : 1;
            float share = edgeShares[hand];
            if (share <= 0f || edgeHands[hand] != EdgeHand.Planted || !LedgeHandTarget(limb, isLeft, out Vector3 wrist, out _, out _))
                return 0f;
            float drop = Drop(limb.Upper.position, wrist, limb.Reach * edgeArmUse);
            return float.IsPositiveInfinity(drop) ? 0f : drop * share;
        }
    }

    // How far the shoulder must come straight down for the wrist to be within limit of it (m): none when it already is,
    // infinite when no drop would do (the wrist further than limit away across, or too far above).
    private static float Drop(Vector3 shoulder, Vector3 wrist, float limit)
    {
        Vector3 toWrist = wrist - shoulder;
        float square = limit * limit - (toWrist.x * toWrist.x + toWrist.z * toWrist.z);
        if (square < 0f) return float.PositiveInfinity;
        float upDown = Mathf.Sqrt(square);
        if (toWrist.y > upDown) return float.PositiveInfinity;
        return Mathf.Max(0f, -toWrist.y - upDown);
    }
}
