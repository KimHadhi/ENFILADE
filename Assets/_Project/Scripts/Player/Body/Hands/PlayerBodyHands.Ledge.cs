using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Hands on a ledge. Hanging, each hand keeps its own hold on the edge, the top under its palm, and stays there while the
/// body moves (LedgeHold): still, the holds are a shoulder width apart (Hands.ledgeHandSpacing). A step along the ledge goes
/// hand over hand on the step's own clock (ParkourTraversal.LedgeStepShare), as the step's clip moves its hands: the hand on
/// the way the body goes lets go as the step begins and takes hold by LeadHolds of it, the other lets go at TrailLifts and
/// takes hold as the step ends, each at its place beside where the body will be as the step ends, so one hand always holds.
/// A hand on its way lifts over the top (ledgeHandLift) with its fingers open. A hold is only ever taken where a palm can
/// rest: a top within a hand's height of the edge's, with room over it, so at the ledge's end, a gap or an inside corner the
/// hand holds the furthest such place short of it instead of the air or a wall. Before, both hands slid along the edge with
/// the body (user, 2026-11-13); until 0.9.393 the hands went on a clock of their own, a hand every 0.22 s from the capsule's
/// speed, two and a half times the clip's (the user: "ledge hanging도 왼쪽 오른쪽 갈 때 … 팔은 버벅거리잖아").
/// Catching a ledge, the hands reach for their holds over the edge and land as the warp ends (PlayerBody.LedgeCatchShare).
/// Everything here comes from the capsule's place along the ledge, the ledge itself and the time in the hang, which every PC
/// has, so every PC shows the same hands, and they are the hands the player sees in first person.
/// </summary>
public sealed partial class PlayerBodyHands
{
    [SerializeField, Range(0f, .75f), Tooltip("How far inside the edge the palm centre rests, as a share of palm length.")]
    private float ledgePalmInset = .15f;
    private PlayerBodyProfile.Skin gripSkin;
    private readonly struct LedgeContact
    {
        public readonly Collider Surface;
        public readonly Vector3 Palm, Fingers, Normal, Side;
        public LedgeContact(Collider surface, Vector3 palm, Vector3 fingers, Vector3 normal, Vector3 side)
        { Surface = surface; Palm = palm; Fingers = fingers; Normal = normal; Side = side; }
    }

    // One hand's hold: the top under its palm and the way that top faces, in the space of the collider it is on (a moving
    // ledge carries it), and a move under way to it from the last one while Progress is below 1.
    private sealed class LedgeHold
    {
        public Collider Surface, FromSurface;
        public Vector3 Top, Up, FromTop, FromUp;
        public float Progress = 1f;
        public bool Moving => Progress < 1f;
        public bool Valid => Surface != null;
        public Vector3 TopNow => Surface.transform.TransformPoint(Top);
        public Vector3 UpNow => Surface.transform.TransformDirection(Up);
        public Vector3 FromTopNow => FromSurface != null ? FromSurface.transform.TransformPoint(FromTop) : TopNow;
        public Vector3 FromUpNow => FromSurface != null ? FromSurface.transform.TransformDirection(FromUp) : UpNow;

        public void Take(in RaycastHit top)
        {
            Surface = top.collider;
            Top = Surface.transform.InverseTransformPoint(top.point);
            Up = Surface.transform.InverseTransformDirection(top.normal);
            FromSurface = null;
            Progress = 1f;
        }

        // Lets go where it holds and takes this top instead, over Ability.ledgeHandStepTime.
        public void MoveTo(in RaycastHit top)
        {
            Collider fromSurface = Surface;
            Vector3 fromTop = Top, fromUp = Up;
            Take(top);
            FromSurface = fromSurface;
            FromTop = fromTop;
            FromUp = fromUp;
            Progress = 0f;
        }
    }

    private readonly LedgeHold[] ledgeHolds = { new LedgeHold(), new LedgeHold() };
    private int ledgeHoldsFrame = -1;
    private bool ledgeHoldsSet;
    private float ledgeHungFor, ledgeLastShare = 1f;
    // When in a step each hand moves (ParkourTraversal.LedgeStepShare), as Beatrun's hangstrafeleft/right move theirs (the clip
    // bench, 2026-11-24 17:06: of its 1.17 s the leading hand reaches out from 0 to 0.45, the other follows from 0.65 to its
    // end); a hand still on its way when a step stops short lands in FinishTime (s).
    private const float LeadHolds = .385f, TrailLifts = .556f, FinishTime = .15f;

    // A palm rests on a top within this of the edge's height (m), probed from this far above it (m), with a hand's room over
    // it (the ledge's own rule and number: LedgeFinder.HandRoom, Ability.ledgeHandRoom).
    private const float TopTolerance = .1f, TopProbe = .25f;
    // A hand moves to a new hold only if it gets at least this far (m), looking back from where it was going in these
    // steps (m).
    private const float LeastMove = .03f, MoveSearchStep = .03f;
    // Catching, the fingers close over the last quarter of the reach.
    private const float CatchCloses = .75f;

    private float LedgeHalfSpan => ledgeHandSpacing * shoulderSpan * (gripSkin?.LedgeHandSpacingScale ?? 1f) * .5f;
    private float LedgeInset(ArmIK arm) => arm.PalmLength * ledgePalmInset * (gripSkin?.LedgePalmInsetScale ?? 1f);

    // The ledge as the hands see it: a point on its edge, its outward side (level) and the way along it to the right as one
    // faces the wall.
    private static bool LedgeFrame(in LedgeTarget ledge, out Vector3 edge, out Vector3 outward, out Vector3 along)
    {
        edge = outward = along = Vector3.zero;
        if (ledge.Surface == null) return false;
        Transform support = ledge.Surface.transform;
        outward = Vector3.ProjectOnPlane(support.TransformDirection(ledge.LocalNormal), Vector3.up);
        if (outward.sqrMagnitude < 1e-6f) return false;
        outward.Normalize();
        along = Vector3.Cross(Vector3.up, -outward);
        edge = support.TransformPoint(ledge.LocalEdge);
        return true;
    }

    // The top a palm can rest on where the edge passes this point (on the edge's line), the palm inset as it rests: the
    // first thing under it within TopTolerance of the edge's height, facing up enough, with a hand's room over it (not
    // inside a wall that rises there, as at an inside corner). The player's own colliders and the zombies are no top.
    // The rule is the ledge's own (LedgeFinder.HandTop), the one the movement found the ledge by, so a hand never takes
    // hold where the movement saw no ledge: they were two rules until 2026-11-18, and the movement's knew nothing of the
    // room a hand needs. Only the palm's own numbers are the hands' (how far in it rests, how steep a top it lies on).
    private bool TopAt(Vector3 onEdge, Vector3 outward, float inset, float height, out RaycastHit top)
    {
        var detector = player.Abilities.Traversal.Detector;
        var body = detector.Body;
        body.Layers = HangWall.Layers(player.groundLayers);
        return LedgeFinder.HandTop(body, detector.LedgeRules, onEdge, outward, inset, height, TopTolerance, TopProbe, PalmLeastUp, out top);
    }

    // A palm lies on a top that faces up at least this much (the up part of its normal).
    private const float PalmLeastUp = .3f;

    // A hand's hold at shoulder width beside this point on the edge, or the nearest toward it that has a top.
    private bool NeutralTop(ArmIK arm, bool isLeft, Vector3 centre, Vector3 outward, Vector3 along, float height, out RaycastHit top)
    {
        Vector3 side = along * (isLeft ? -1f : 1f);
        float across = LedgeHalfSpan, inset = LedgeInset(arm);
        for (int attempt = 0; attempt < 5; attempt++, across *= .7f)
            if (TopAt(centre + side * across, outward, inset, height, out top)) return true;
        top = default;
        return false;
    }

    // Once a frame, before anything asks where a hand holds: the holds set as a hang begins, then kept, moved hand over
    // hand while hanging, and kept still while climbing up from the hang (PlayerBody.Heave); dropped otherwise.
    private void UpdateLedgeHolds()
    {
        if (ledgeHoldsFrame == Time.frameCount) return;
        ledgeHoldsFrame = Time.frameCount;
        var state = player.CurrentState;
        var traversal = player.Abilities.Traversal;
        bool hanging = state == MovementState.LedgeHang;
        bool heaving = state == MovementState.Mantle && traversal.Obstacle.Kind == VaultKind.Heave && traversal.Obstacle.FromBar == null;
        // A new hang (its time began again) takes its holds afresh, even if nothing asked for them in between.
        float hungFor = hanging ? body.StateTime : 0f;
        if (hungFor < ledgeHungFor) ledgeHoldsSet = false;
        ledgeHungFor = hungFor;
        if (left == null || right == null || body.IsDead || (!hanging && !(heaving && ledgeHoldsSet))
            || !LedgeFrame(traversal.Ledge, out Vector3 edge, out Vector3 outward, out Vector3 along))
        {
            ledgeHoldsSet = false;
            return;
        }
        float dt = Time.deltaTime;
        var settings = player.AbilitySettings;
        Vector3 centre = edge + along * Vector3.Dot(transform.position - edge, along);
        if (!ledgeHoldsSet)
        {
            // Caught: around the edge point the hands were caught at, where the warp brings the body; otherwise where it is.
            Vector3 middle = body.LedgeCatchShare < 1f ? edge : centre;
            if (!NeutralTop(left, true, middle, outward, along, edge.y, out RaycastHit leftTop)
                || !NeutralTop(right, false, middle, outward, along, edge.y, out RaycastHit rightTop)) return;
            ledgeHolds[0].Take(leftTop);
            ledgeHolds[1].Take(rightTop);
            ledgeHoldsSet = true;
            ledgeLastShare = 1f;
            return;
        }
        foreach (var hold in ledgeHolds)
            // The collider it held went away.
            if (!hold.Valid) { ledgeHoldsSet = false; return; }
        float share = traversal.LedgeStepShare, way = traversal.LedgeStepSide;
        // Climbing up, still catching, or no step under way: the hands stay where they hold, one still on its way landing.
        if (!hanging || body.LedgeCatchShare < 1f || share >= 1f || way == 0f)
        {
            foreach (var hold in ledgeHolds)
                if (hold.Moving) hold.Progress = Mathf.MoveTowards(hold.Progress, 1f, dt / FinishTime);
            ledgeLastShare = 1f;
            return;
        }
        float half = LedgeHalfSpan;
        int lead = way > 0f ? 1 : 0;
        // A step began this frame (the first, or the next on from the last).
        bool began = share < ledgeLastShare;
        for (int hand = 0; hand < 2; hand++)
        {
            bool leads = hand == lead;
            float lift = leads ? 0f : TrailLifts, holds = leads ? LeadHolds : 1f;
            if (leads ? began : share >= lift && (began || ledgeLastShare < lift))
                // To its place beside where the body will be as the step ends: the step's whole length on from where it began
                // (the table's, as every PC knows it; a step the ledge cut short finds the furthest top short of it).
                MoveHold(hand, Off(hand), way * settings.ledgeShimmyStep * (1f - ParkourTraversal.LedgeStepTravel(share)));
            var held = ledgeHolds[hand];
            if (held.Moving) held.Progress = Mathf.Max(held.Progress, Mathf.InverseLerp(lift, holds, share));
        }
        ledgeLastShare = share;

        float Place(int hand) => hand == 0 ? -half : half;
        // How far a hold is from its place at shoulder width beside the body, along the ledge to the right (m).
        float Off(int hand) => Vector3.Dot(ledgeHolds[hand].TopNow - centre, along) - Place(hand);

        // From off to as near to as has a top, looking back from it, if that gets the hand at least LeastMove.
        void MoveHold(int hand, float off, float to)
        {
            // Never nearer the other hand than a shoulder width (the user, 2026-11-22: "ledge hanging 후 왼쪽 오른쪽 이동할 때 손이
            // 겹친다").
            float other = Off(1 - hand);
            to = hand == 0 ? Mathf.Min(to, other) : Mathf.Max(to, other);
            if (Mathf.Abs(to - off) < LeastMove) return;
            float direction = Mathf.Sign(to - off);
            var arm = hand == 0 ? left : right;
            float inset = LedgeInset(arm);
            for (float at = to; (at - off) * direction >= LeastMove; at -= direction * MoveSearchStep)
            {
                if (!TopAt(centre + along * (Place(hand) + at), outward, inset, edge.y, out RaycastHit top)) continue;
                ledgeHolds[hand].MoveTo(top);
                return;
            }
        }
    }

    // The palm on a hold: on its top, fingers over it away from the wall. On its way the hand goes from the last top to the
    // next, lifting off and coming back down a little out from the wall, so it never drags along the top or cuts the edge.
    private LedgeContact PalmOnHold(ArmIK arm, LedgeHold hold, Vector3 outward, Vector3 side, bool lifted)
    {
        Vector3 top = hold.TopNow, up = hold.UpNow;
        if (hold.Moving)
        {
            float progress = Mathf.SmoothStep(0f, 1f, hold.Progress);
            top = Vector3.Lerp(hold.FromTopNow, top, progress);
            up = Vector3.Slerp(hold.FromUpNow, up, progress);
        }
        Vector3 palm = top + up * (arm.PalmDepth + fingerSurfaceClearance);
        if (lifted && hold.Moving)
            palm += (Vector3.up + outward * .5f) * (player.AbilitySettings.ledgeHandLift * Mathf.Sin(Mathf.PI * hold.Progress));
        Vector3 fingers = Vector3.ProjectOnPlane(-outward, up).normalized;
        return new LedgeContact(hold.Surface, palm, fingers, up, side);
    }

    private bool FindLedgeContact(ArmIK arm, bool isLeft, out LedgeContact contact, bool lifted = true)
    {
        contact = default;
        var ledge = player.Abilities.Traversal.Ledge;
        if (ledge.Surface == null || arm == null) return false;
        UpdateLedgeHolds();
        if (ledgeHoldsSet && ledgeHolds[isLeft ? 0 : 1].Valid && LedgeFrame(ledge, out _, out Vector3 holdOutward, out Vector3 holdAlong))
        {
            contact = PalmOnHold(arm, ledgeHolds[isLeft ? 0 : 1], holdOutward, holdAlong * (isLeft ? -1f : 1f), lifted);
            return true;
        }
        Transform support = ledge.Surface.transform;
        Vector3 outward = Vector3.ProjectOnPlane(support.TransformDirection(ledge.LocalNormal), Vector3.up).normalized;
        Vector3 along = Vector3.Cross(Vector3.up, -outward), edge = support.TransformPoint(ledge.LocalEdge);
        // Climbing, the edge point is already the one in front of the body, and the hands must not slide with it.
        Vector3 centre = player.CurrentState != MovementState.LedgeHang ? edge
            : edge + along * Vector3.Dot(transform.position - edge, along);
        Vector3 side = along * (isLeft ? -1f : 1f);
        float across = LedgeHalfSpan;
        // The palm rests just inside the edge; the wrist is placed from the measured palm centre, not guessed.
        float inset = LedgeInset(arm);
        for (int attempt = 0; attempt < 5; attempt++, across *= .7f)
        {
            Vector3 probe = centre + side * across - outward * inset + Vector3.up * .25f;
            if (!ledge.Surface.Raycast(new Ray(probe, Vector3.down), out RaycastHit top, .5f) || top.normal.y < .3f) continue;
            Vector3 fingers = Vector3.ProjectOnPlane(-outward, top.normal).normalized;
            contact = new LedgeContact(ledge.Surface, top.point + top.normal * (arm.PalmDepth + fingerSurfaceClearance),
                fingers, top.normal, side);
            return true;
        }
        return false;
    }

    private bool LedgeHandTarget(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint, bool lifted = true)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        if (!FindLedgeContact(arm, isLeft, out var contact, lifted)) return false;
        rotation = arm.HandRotation(contact.Fingers, -contact.Normal);
        Vector3 palm = contact.Palm;
        // Catching, the hand reaches for its hold over the edge rather than through the wall's face under it: up and out
        // first, landing as the catch ends (PlayerBody.LedgeCatchShare).
        float catching = body.LedgeCatchShare;
        if (lifted && catching < 1f)
            palm += (Vector3.up - contact.Fingers * .5f) * (player.AbilitySettings.ledgeHandLift * Mathf.Sin(Mathf.PI * catching));
        position = arm.WristAtPalm(palm, rotation);
        // The clip's own elbow: the step's clip bends it as its hand reaches and holds, and looking away the look clip has it
        // somewhere an arm can go from that way round. Until 0.9.393 the bend was turned toward a point beside the shoulder
        // whatever the clip did, and a nearly straight arm flipped about it.
        hint = arm.Elbow;
        return true;
    }

    // The body's ledge alignment uses the same contacts as the final hand IK, before the eye camera is placed. A hand that
    // has let go no longer counts: the body hangs from the other. centre is where the wrists belong, held where they are.
    // A hand on its way to another hold counts where it goes, not lifted, so the body doesn't bob with it.
    internal bool TryLedgeWrists(out Vector3 centre, out Vector3 held)
    {
        centre = held = Vector3.zero;
        if (left == null || right == null
            || !LedgeHandTarget(left, true, out Vector3 l, out _, out _, false)
            || !LedgeHandTarget(right, false, out Vector3 r, out _, out _, false)) return false;
        float leftShare = Mathf.Max(edgeShares[0], .001f), rightShare = Mathf.Max(edgeShares[1], .001f), total = leftShare + rightShare;
        centre = (l * leftShare + r * rightShare) / total;
        held = (left.Hand.position * leftShare + right.Hand.position * rightShare) / total;
        return true;
    }

    /// <summary>
    /// Where the wrists' middle will be once the hands hold the ledge just caught: at shoulder width either side of its
    /// edge point, as the holds begin (UpdateLedgeHolds). The catch's warp brings the catch clip's wrists there
    /// (PlayerBody.TryLedgeContact). Nothing of the holds changes.
    /// </summary>
    internal bool TryLedgeCatchWrists(out Vector3 centre)
    {
        centre = Vector3.zero;
        if (left == null || right == null || !LedgeFrame(player.Abilities.Traversal.Ledge, out Vector3 edge, out Vector3 outward, out Vector3 along)
            || !Wrist(left, true, out Vector3 l) || !Wrist(right, false, out Vector3 r)) return false;
        centre = (l + r) * .5f;
        return true;

        bool Wrist(ArmIK arm, bool isLeft, out Vector3 wrist)
        {
            wrist = Vector3.zero;
            if (!NeutralTop(arm, isLeft, edge, outward, along, edge.y, out RaycastHit top)) return false;
            Vector3 fingers = Vector3.ProjectOnPlane(-outward, top.normal).normalized;
            Quaternion rotation = arm.HandRotation(fingers, -top.normal);
            wrist = arm.WristAtPalm(top.point + top.normal * (arm.PalmDepth + fingerSurfaceClearance), rotation);
            return true;
        }
    }

    /// <summary>
    /// For the /hang readout, after this frame's hand IK: where this hand's palm is, where its hold puts it, how much of the
    /// hand the ledge holds (0-1) and whether it is on its way to another hold. False when the ledge doesn't hold it.
    /// </summary>
    internal bool LedgeReadout(bool isLeft, out Vector3 palm, out Vector3 wanted, out float hold, out bool moving)
    {
        var arm = isLeft ? left : right;
        palm = wanted = Vector3.zero;
        hold = 0f;
        moving = false;
        if (arm == null || !FindLedgeContact(arm, isLeft, out var contact)) return false;
        palm = arm.PalmPosition;
        wanted = contact.Palm;
        hold = weights[(int)Hold.Ledge] * edgeShares[isLeft ? 0 : 1];
        moving = ledgeHoldsSet && ledgeHolds[isLeft ? 0 : 1].Moving;
        return true;
    }

    // A hand on its way between holds opens off the top and closes again as it lands.
    private float HoldGrip(bool isLeft)
    {
        if (!ledgeHoldsSet) return 1f;
        var hold = ledgeHolds[isLeft ? 0 : 1];
        return hold.Moving ? 1f - Mathf.Sin(Mathf.PI * hold.Progress) : 1f;
    }

    private void CurlLedgeFingers(ArmIK arm, bool isLeft)
    {
        float weight = weights[(int)Hold.Ledge] * edgeContacts[isLeft ? 0 : 1] * HoldGrip(isLeft);
        if (weight <= 0f || !FindLedgeContact(arm, isLeft, out var contact)) return;
        arm.GripLedge(contact.Surface, contact.Fingers, -contact.Normal, contact.Palm, fingerSurfaceClearance, weight);
    }
}
