using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Hands on the edge of what the player vaults or climbs. Nothing here is per clip or per character: a hand plants where the
/// clip brings it closest to its place on the edge, then stays put while the body keeps within its arm's reach. The capsule
/// doesn't wait for it, so the further ahead it draws the less the body holds back (EdgeHoldBack), and once the arm runs out
/// the hand comes off for good and the clip carries on. The quick vaults instead bring the body down onto their hands
/// (PlayerBodyHands.Pull). A step up brings no hand near, so none plants; a one-handed vault
/// plants one. Climbing from a hang the hands carry the body instead (PlayerBody.Heave). Hanging, the look clips take one
/// hand away, and that hand lets go. Over a high wall a hand that has pushed off stays no higher than the top it left
/// until the clip has played out (KeepPushedHandsDown). The high moves reach for the edge from their start (ReachesAhead).
/// </summary>
public sealed partial class PlayerBodyHands
{
    [SerializeField, Range(0f, 2f), Tooltip("A climbing hand plants at its closest to its place on the edge, if that is within this share of the arm's length.")]
    private float edgeCatch = 1f;
    [SerializeField, Min(.01f), Tooltip("How long a hand takes to settle on the edge or come off it (s).")]
    private float edgeEase = .1f;
    [SerializeField, Min(.01f), Tooltip("As a high vault or a high climb begins, how long a hand takes to reach for its place on the edge (s), "
        + "instead of following its clip, whose hands start over the head. About the capsule's own way to the wall.")]
    private float edgeReach = .2f;
    [SerializeField, Range(.5f, 1f), Tooltip("A planted arm stretches to this share of its length; past that the body stays back for it.")]
    private float edgeArmUse = .95f;
    [SerializeField, Tooltip("How far behind the capsule the body stays for planted hands, in arm lengths: it starts giving up at the first and has given up at the second.")]
    private Vector2 edgeHoldBack = new Vector2(.6f, 1.8f);
    [SerializeField, Min(.1f), Tooltip("How quickly the body catches the capsule up once the hands have let go (arm lengths per second).")]
    private float edgeCatchUp = 3f;

    // Holding back has to keep up with the capsule, whose quickest moves cover several arm lengths a second.
    private const float HoldBackFollow = 20f;
    // Below this the body has begun giving up on the hands, so an arm at full stretch is one that has run out.
    private const float StillWaiting = .98f;
    // How quickly a held-down hand (KeepPushedHandsDown) is let back up once the move is over, in arm lengths per second.
    // At the rest lift's 6 it rose 3 m/s, a flick of its own.
    private const float PushLetGoSpeed = 2f;

    private enum EdgeHand { Waiting, Planted, Gone }
    private readonly EdgeHand[] edgeHands = new EdgeHand[2];
    private readonly float[] edgeShares = new float[2], edgeAway = new float[2];
    // Per hand: how far the fingers close on the edge. A hand still reaching for it keeps the clip's fingers.
    private readonly float[] edgeContacts = new float[2];
    // Per hand: the wrist height where it pushed off a high wall (infinite when it hasn't), and how far it is held down.
    private readonly float[] pushedFrom = { float.PositiveInfinity, float.PositiveInfinity }, pushDrops = new float[2];
    // Per hand, off a low top: how far under its shoulder it let go (m; infinite when it hasn't), the most it may rise to.
    private readonly float[] pushedUnderShoulder = { float.PositiveInfinity, float.PositiveInfinity };
    private MovementState edgeState;
    private Vector3 edgeHeld;
    private float edgeWaits = 1f, shoulderSpan;

    private static bool Climbing(MovementState state) => state == MovementState.Vault || state == MovementState.Mantle;
    // The quick vaults lift the feet over the top on their own arc, which holding the body back would drag through it; the
    // body comes straight down for their hands instead (PlayerBodyHands.Pull).
    private static bool ClearsTop(VaultKind kind) => kind.ClearsTop();
    // The quick moves onto or over a low top: the hands reach out from the start, press while the arm reaches and let go on
    // an S-curve, and never hold the body back (PlayerBodyHands.Pull). Climbing onto a low top (Onto) went the high moves'
    // way: both hands pulled to the edge in a tenth of a second, the body held back behind them up to 1.8 arm lengths and
    // let go at once, and each hand flung back to its clip in a tenth of a second, the vaultonto clip's right hand up to the
    // chest (user, 2026-11-07: "팔이 허우적거림").
    private static bool PressesQuickly(VaultKind kind) => kind.ClearsTop() || kind == VaultKind.Onto;
    // The high vault over a wall: the body is held back for planted hands, so they let go late, with the body already well
    // over the top, and its clip then throws the hand that pushed off up past the face, where a thin wall leaves nothing to
    // press. Measured on vaultoverhigh: the right hand leaves the top a third of the way in and rises to head height.
    private static bool PushesOver(MovementState state, VaultKind kind) => state == MovementState.Vault && !ClearsTop(kind);
    // The high moves first take the capsule to the wall (ObstacleTarget.Approach), and both their clips start with the hands
    // over the head and bring them down onto the top only later (measured on vaultoverhigh and vaultontohigh), so the hands
    // reached up into the air above and beyond the top before they met it. Here they reach for their place from the start.
    private static bool ReachesAhead(VaultKind kind) => kind == VaultKind.OverHigh || kind == VaultKind.OntoHigh;

    private void ResetEdge()
    {
        edgeState = player.CurrentState;
        edgeHeld = Vector3.zero;
        edgeWaits = 1f;
        ResetPull();
        for (int hand = 0; hand < 2; hand++)
        {
            edgeHands[hand] = EdgeHand.Waiting;
            edgeShares[hand] = edgeContacts[hand] = restLifts[hand] = pushDrops[hand] = 0f;
            pushedFrom[hand] = pushedUnderShoulder[hand] = float.PositiveInfinity;
        }
    }

    // Off a hang both hands are already there; any other move starts with them free. Leaving the edge any other way, a hand
    // keeps its share while Hold.Ledge fades, so it never swings back on the way out.
    private void TrackEdgeHands(MovementState state)
    {
        if (state == edgeState) return;
        // Up off a swing bar the hands are already on it too (ParkourTraversal.MantleFromBar).
        bool hung = state == MovementState.LedgeHang || edgeState == MovementState.LedgeHang
            || edgeState == MovementState.BarGrab || edgeState == MovementState.BarSwing;
        if (Climbing(edgeState) && !Climbing(state)) LetGoAfterQuickPress();
        edgeState = state;
        restLifts[0] = restLifts[1] = 0f;
        if (state != MovementState.LedgeHang && !Climbing(state)) return;
        for (int hand = 0; hand < 2; hand++)
        {
            edgeHands[hand] = hung ? EdgeHand.Planted : EdgeHand.Waiting;
            edgeAway[hand] = pushedFrom[hand] = pushedUnderShoulder[hand] = float.PositiveInfinity;
        }
    }

    private bool WantsEdge(MovementState state)
    {
        var traversal = player.Abilities.Traversal;
        if (traversal.Ledge.Surface == null) return false;
        if (state == MovementState.LedgeHang) return true;
        // A quick move's hands let go on their own curve (edgeQuickLetGo), during the move or after it, not at Hold.Ledge's.
        if (PressesQuickly(traversal.Obstacle.Kind) && (edgeShares[0] > 0f || edgeShares[1] > 0f)) return true;
        return Climbing(state) && traversal.Obstacle.Kind != VaultKind.None
            && (edgeHands[0] != EdgeHand.Gone || edgeHands[1] != EdgeHand.Gone);
    }

    // The share of this hand's weight the edge may take this frame. arm.Hand still sits where the clip put it.
    private float EdgeShare(ArmIK arm, bool isLeft, Vector3 wrist)
    {
        int hand = isLeft ? 0 : 1;
        var state = player.CurrentState;
        float wanted = edgeShares[hand], ease = edgeEase;
        bool reaching = false, overTop = false;
        if (state == MovementState.LedgeHang) wanted = 1f - body.HangLetGo(isLeft);
        else if (!Climbing(state) && PressesQuickly(player.Abilities.Traversal.Obstacle.Kind))
        {
            // After a quick move: whatever still has a share lets go (LetGoAfterQuickPress), on the same curve.
            overTop = true;
            wanted = 0f;
            ease = edgeQuickLetGo;
        }
        else if (Climbing(state))
        {
            var kind = player.Abilities.Traversal.Obstacle.Kind;
            float reach = Mathf.Max(.01f, arm.Reach);
            // A quick vault or a climb onto a low top reaches out from its start, and plants and lets go by what the body can
            // come down for the hand (PlayerBodyHands.Pull; nothing, climbing onto one); the move is over quickly, so the
            // reach takes edgeEase, not edgeReach.
            if (PressesQuickly(kind))
            {
                overTop = true;
                reaching = TrackHandOverTop(arm, hand, wrist, reach);
                if (edgeHands[hand] == EdgeHand.Gone) ease = edgeQuickLetGo;
            }
            else
            {
                float away = Vector3.Distance(arm.Hand.position, wrist), stretch = Vector3.Distance(arm.Upper.position, wrist) / reach;
                // Within what the body can make up by holding back, the hand goes down as soon as it stops getting closer.
                float spare = edgeArmUse + edgeHoldBack.x;
                if (edgeHands[hand] == EdgeHand.Waiting && stretch <= spare && away <= edgeCatch * reach && away >= edgeAway[hand])
                    edgeHands[hand] = EdgeHand.Planted;
                // Reaching ahead (ReachesAhead), the hand is on its way already, and it holds once it is there and the arm can take it.
                reaching = edgeHands[hand] == EdgeHand.Waiting && ReachesAhead(kind);
                if (reaching && edgeShares[hand] >= 1f && stretch <= 1f)
                {
                    edgeHands[hand] = EdgeHand.Planted;
                    reaching = false;
                }
                if (reaching) ease = edgeReach;
                edgeAway[hand] = away;
                bool ranOut = kind == VaultKind.Heave ? body.HeaveHandsFree : stretch >= 1f && edgeWaits < StillWaiting;
                if (edgeHands[hand] == EdgeHand.Planted && ranOut)
                {
                    edgeHands[hand] = EdgeHand.Gone;
                    if (PushesOver(state, kind)) pushedFrom[hand] = wrist.y;
                }
            }
            wanted = edgeHands[hand] == EdgeHand.Planted || reaching ? 1f : 0f;
        }
        float share = edgeShares[hand] = Mathf.MoveTowards(edgeShares[hand], wanted, Time.deltaTime / ease);
        // The fingers close on the edge once the hand is there; reaching for it, they stay as the clip has them.
        bool onEdge = !Climbing(state) || edgeHands[hand] != EdgeHand.Waiting;
        edgeContacts[hand] = Mathf.MoveTowards(edgeContacts[hand], onEdge ? share : 0f, Time.deltaTime / edgeEase);
        // A quick vault's hand goes out and comes back on an S-curve, so it neither starts nor stops with a jerk.
        return overTop ? Mathf.SmoothStep(0f, 1f, share) : share;
    }

    /// <summary>
    /// A hand that pushed off a high wall (PushesOver) stays no higher than the top it left while the vault and its clip's
    /// play-out last, instead of rising into the air ahead of the face. It is held down just as far as the clip lifts it, so
    /// nothing jumps, and let go eased afterwards. A hand on the gun, the fists, a rung or a bar is left alone.
    /// Not in Beatrun, whose clip lifts that hand the same way; the user saw it press on air (2026-11-06).
    /// Climbing onto a low top, a hand that has let go stays no higher than it was under its shoulder then, so it goes up
    /// with the body standing and no further: vaultonto's clip swings the right hand from the hip up to the chest in a
    /// quarter of a second near the top, which flicked into the air once the hands let go early (user, 2026-11-07:
    /// "오른손이 갑자기 허공으로 휙 튀는데요").
    /// </summary>
    private void KeepPushedHandsDown(float dt)
    {
        var state = player.CurrentState;
        bool over = !body.IsDead && (state == MovementState.Vault || body.PlaysOutObstacle
            || (state == MovementState.Mantle && player.Abilities.Traversal.Obstacle.Kind == VaultKind.Onto));
        Lower(left, true);
        Lower(right, false);

        void Lower(ArmIK arm, bool isLeft)
        {
            int hand = isLeft ? 0 : 1;
            if (!over) pushedFrom[hand] = pushedUnderShoulder[hand] = float.PositiveInfinity;
            bool held = weights[(int)Hold.Gun] * (isLeft ? 1f - leftOffGun : 1f) > .001f || weights[(int)Hold.Fists] > .001f
                || weights[(int)Hold.Ladder] > .001f || weights[(int)Hold.Bar] > .001f;
            // Over a high wall no higher than the top it left; off a low top no higher, under its shoulder, than it let go.
            float cap = Mathf.Min(pushedFrom[hand], arm.Upper.position.y + pushedUnderShoulder[hand]);
            pushDrops[hand] = !held && !float.IsPositiveInfinity(cap)
                ? Mathf.Max(0f, arm.Hand.position.y - cap)
                : Mathf.MoveTowards(pushDrops[hand], 0f, PushLetGoSpeed * arm.Reach * dt);
            if (pushDrops[hand] > 0f)
                arm.Solve(arm.Hand.position + Vector3.down * pushDrops[hand], arm.Hand.rotation, arm.Elbow, 1f, 0f, shoulderReach);
        }
    }

    /// <summary>How far the clip has straightened the arms over the hands, as a share of their length; none with the shoulders below them.</summary>
    internal float ArmPress()
    {
        if (left == null || right == null) return 0f;
        if (left.Upper.position.y + right.Upper.position.y <= left.Hand.position.y + right.Hand.position.y) return 0f;
        return (Vector3.Distance(left.Upper.position, left.Hand.position) / Mathf.Max(.01f, left.Reach)
            + Vector3.Distance(right.Upper.position, right.Hand.position) / Mathf.Max(.01f, right.Reach)) * .5f;
    }

    /// <summary>
    /// How far the body stays behind where the clip and capsule put it, so that arms on the edge still reach: the least move
    /// that brings each planted shoulder within its arm's length. Called before the eye camera is placed. A hang looking away
    /// keeps its place at the wall (PlayerBody.Grip) and takes only this.
    /// </summary>
    internal Vector3 EdgeHoldBack(float dt)
    {
        var kind = player.Abilities.Traversal.Obstacle.Kind;
        bool climbing = Climbing(player.CurrentState) && kind != VaultKind.None && kind != VaultKind.Heave && !PressesQuickly(kind);
        float turn = body.HangTurn, arm = left != null ? left.Reach : 0f, total = 0f, most = 0f;
        Vector3 need = Vector3.zero;
        edgeWaits = 1f;
        if (left != null && right != null && !body.IsDead && (climbing || turn > 0f))
        {
            Add(left, true);
            Add(right, false);
            // Takes its full size only as the hands finish settling, so a plant doesn't jerk the body and the view.
            if (total > 0f) need *= most / total;
            if (climbing) edgeWaits = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edgeHoldBack.x * arm, edgeHoldBack.y * arm, need.magnitude));
            need *= climbing ? edgeWaits : turn;
        }
        float speed = need.sqrMagnitude > edgeHeld.sqrMagnitude ? HoldBackFollow : edgeCatchUp;
        edgeHeld = Vector3.MoveTowards(edgeHeld, need, speed * arm * dt);
        // The quick vaults come down for their hands instead (PlayerBodyHands.Pull).
        return edgeHeld + Vector3.down * PullOverTop(arm, dt);

        void Add(ArmIK limb, bool isLeft)
        {
            // A hand still reaching for the edge holds nothing back yet: the body goes on to the wall.
            float share = edgeShares[isLeft ? 0 : 1];
            if (share <= 0f || edgeHands[isLeft ? 0 : 1] == EdgeHand.Waiting
                || !LedgeHandTarget(limb, isLeft, out Vector3 wrist, out _, out _)) return;
            Vector3 reach = wrist - limb.Upper.position;
            float distance = reach.magnitude, limit = limb.Reach * edgeArmUse;
            if (distance > limit) need += reach * ((distance - limit) / distance * share);
            total += share;
            most = Mathf.Max(most, share);
        }
    }
}
