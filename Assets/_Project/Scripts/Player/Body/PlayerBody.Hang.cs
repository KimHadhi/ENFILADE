using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Beatrun's hangpose: hanging, a look past the ledge-jump angle turns the body after the view on one hand, so a jump from
/// there starts from the way it looks. The grid of poses comes from its model (hang_yaw, hang_pitch) and is steered the way
/// jumpanim.lua steers it. Looking left keeps the right hand and the other way round; PlayerBodyHands lets go of the other.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("Beginning of the hang look clips' names: hangpose_l90_mid is 90 degrees left, looking level.")]
    private string hangPose = "hangpose";
    [SerializeField, Range(0f, 179f), Tooltip("Hanging, look limit either side of the wall (degrees). Past 180 the turned pose flips to the other side.")]
    private float hangTurnLimit = 175f;
    [SerializeField, Min(0f), Tooltip("Hanging, a foot goes to the wall under the ledge while its toes are within this of it (m); "
        + "further in (a recess, an overhang) the leg hangs down instead, letting go of the wall from half this to this.")]
    private float hangFootReach = .35f;
    [SerializeField, Min(0f), Tooltip("Hanging or climbing up, how far toes, knees and ankles keep off the wall (m).")]
    private float hangWallGap = .015f;
    [SerializeField, Tooltip("How far the front of the knee and of the ankle stand in front of their joints, as shares of the shin's "
        + "length: the leg's own thickness, kept off the wall too.")]
    private Vector2 hangLegFront = new Vector2(.12f, .1f);
    [SerializeField, Tooltip("A leg hanging free, with no wall within reach: its ankle this share of the leg's length below the hip, "
        + "and this share in toward the wall.")]
    private Vector2 hangLegDangle = new Vector2(.96f, .06f);
    [SerializeField, Tooltip("Hanging, the legs meet the real wall under the ledge (HangWall). Off, as before 0.8.318: the plane "
        + "straight down from the edge, for comparing by the console's /hang.")]
    private bool hangRealWall = true;

    /// <summary>The legs meet the real wall under a ledge (Body.hangRealWall), for the /hang readout to say what it measured.</summary>
    internal bool HangRealWall => hangRealWall;

    private static readonly float[] HangYawKeys = { -179f, -89.5f, 0f, 89.5f, 179f }, HangPitchKeys = { -90f, 0f, 90f };
    private static readonly string[] HangYawNames = { "l180", "l90", null, "r90", "r180" }, HangPitchNames = { "up", "mid", "down" };
    private const float HangFollowRate = 15f;
    private float hangYaw;

    /// <summary>How far the body has turned from the wall (0-1).</summary>
    internal float HangTurn => state != MovementState.LedgeHang ? 0f
        : Mathf.Clamp01(Mathf.Abs(hangYaw) / Mathf.Max(1f, player.AbilitySettings.ledgeJumpLookAngle));

    /// <summary>How far this hand has come off the ledge (0-1).</summary>
    internal float HangLetGo(bool isLeft) => isLeft == hangYaw < 0f ? HangTurn : 0f;

    public float HangLookYawLimit => Mathf.Clamp(hangTurnLimit, 0f, 179f);

    private string HangClip(int yaw, int pitch) => hangPose + "_" + HangYawNames[yaw] + "_" + HangPitchNames[pitch];

    private void AddHangChannels()
    {
        for (int yaw = 0; yaw < HangYawKeys.Length; yaw++)
            for (int pitch = 0; pitch < HangPitchKeys.Length; pitch++)
                if (HangYawNames[yaw] != null) AddChannel(HangClip(yaw, pitch), 0f);
    }

    // Looking at the wall is the hang itself; the middle column of the grid hands over to it.
    private void HangLook(Vector3 inward, float dt)
    {
        float wall = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
        float look = Mathf.Clamp(Mathf.DeltaAngle(wall, transform.eulerAngles.y), -179f, 179f);
        if (Mathf.Abs(look) < player.AbilitySettings.ledgeJumpLookAngle) look = 0f;
        EaseHangYaw(look, dt);
        float pitch = lookPivot != null ? Mathf.DeltaAngle(0f, lookPivot.localEulerAngles.x) : 0f;
        int y = KeyBelow(HangYawKeys, hangYaw, out float towardY), p = KeyBelow(HangPitchKeys, pitch, out float towardP);
        Cell(y, p, (1f - towardY) * (1f - towardP));
        Cell(y + 1, p, towardY * (1f - towardP));
        Cell(y, p + 1, (1f - towardY) * towardP);
        Cell(y + 1, p + 1, towardY * towardP);

        void Cell(int yawKey, int pitchKey, float weight) => Mix(HangYawNames[yawKey] == null ? ledgeHang : HangClip(yawKey, pitchKey), weight);
    }

    /// <summary>
    /// The legs against the real wall under a ledge (HangWall), measured where each part of them is rather than taken to be
    /// the plane straight down from the edge, which a sill, a lip or a thicker storey under the ledge stepped out of (feet
    /// through it) and a recess or an overhang went into (feet on the air) (user, 2026-11-13). Hanging, each foot goes to
    /// the wall in front of it while that is within hangFootReach, its toes (the front of the shoe, measured on this body)
    /// resting hangWallGap off it; further, the leg hangs down from the hip instead (hangLegDangle). Then, hanging or climbing
    /// up, every joint from the hip down is turned back the least way that keeps its front out of the wall: the knee's and
    /// the ankle's skin (hangLegFront, a share of the shin) and the toes'. Any clip, any character, any shoes.
    /// </summary>
    private void KeepLegsOffWall()
    {
        var ledge = player.Abilities.Traversal.Ledge;
        if (gripWeight <= 0f || ledge.Surface == null || (state != MovementState.LedgeHang && !HeavePressing)
            || leftLeg == null || rightLeg == null) return;
        Transform support = ledge.Surface.transform;
        Vector3 inward = Vector3.ProjectOnPlane(-support.TransformDirection(ledge.LocalNormal), Vector3.up);
        if (inward.sqrMagnitude < 1e-6f) return;
        inward.Normalize();
        if (!hangRealWall)
        {
            KeepLegsOffPlane(support.TransformPoint(ledge.LocalEdge), -inward);
            return;
        }
        // Over the ledge's top there is no wall under it to keep off.
        float top = support.TransformPoint(ledge.LocalEdge).y;
        int layers = HangWall.Layers(player.groundLayers);
        Vector3 hipLine = rightLeg.Upper.position - leftLeg.Upper.position;
        bool hanging = state == MovementState.LedgeHang;
        Fit(leftLeg);
        Fit(rightLeg);

        void Fit(FootPlanting.Leg leg)
        {
            float shin = Vector3.Distance(leg.Lower.position, leg.Foot.position);
            if (hanging) Reach(leg, shin);
            Out(leg, leg.Upper, leg.Lower.position, shin * hangLegFront.x);
            Out(leg, leg.Lower, leg.Foot.position, shin * hangLegFront.y);
            Out(leg, leg.Foot, leg.Foot.TransformPoint(leg.Tip), 0f);
            Out(leg, leg.Foot, leg.Foot.TransformPoint(leg.TipSole), 0f);
        }

        // The foot to the wall in front of it, or the leg hanging down from the hip where there is none within reach.
        void Reach(FootPlanting.Leg leg, float shin)
        {
            Vector3 ankle = leg.Foot.position, hip = leg.Upper.position;
            Vector3 tip = leg.Foot.TransformPoint(leg.Tip), sole = leg.Foot.TransformPoint(leg.TipSole);
            Vector3 front = Vector3.Dot(tip, inward) >= Vector3.Dot(sole, inward) ? tip : sole;
            float gap = 0f;
            bool wall = front.y <= top && HangWall.Probe(front, inward, HangWall.BackFrom(front, hip, inward), layers, transform, out gap, out _);
            float onWall = wall ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hangFootReach * .5f, hangFootReach, gap)) : 0f;
            float length = Vector3.Distance(hip, leg.Lower.position) + shin;
            Vector3 hangsFree = hip + Vector3.down * (length * hangLegDangle.x) + inward * (length * hangLegDangle.y);
            Vector3 onIt = ankle + inward * ((wall ? gap : 0f) - hangWallGap);
            Vector3 target = Vector3.Lerp(ankle, Vector3.Lerp(hangsFree, onIt, onWall), gripWeight);
            // The knee bends about the clip's own axis, as the foot IK bends it (FootPlanting); the foot keeps its turn.
            Quaternion turn = leg.Foot.rotation;
            Vector3 thigh = leg.Lower.position - hip, below = ankle - leg.Lower.position;
            Vector3 own = Vector3.Cross(thigh, below), straight = Vector3.ProjectOnPlane(hipLine, thigh).normalized;
            float bent = Mathf.Clamp01(own.magnitude / Mathf.Max(1e-6f, thigh.magnitude * below.magnitude) / .26f);
            if (Vector3.Dot(own, straight) < 0f && bent < 1f) bent = 0f;
            TwoBoneIK.Reach(leg.Upper, leg.Lower, leg.Foot, target, Vector3.Lerp(straight, own.normalized, bent));
            leg.Foot.rotation = turn;
        }

        // Turns this joint the least way that brings the front of its far end (front toward the wall) out of the wall.
        void Out(FootPlanting.Leg leg, Transform joint, Vector3 end, float front)
        {
            if (end.y > top) return;
            Vector3 skin = end + inward * front;
            if (!HangWall.Probe(skin, inward, HangWall.BackFrom(skin, leg.Upper.position, inward), layers, transform, out float gap, out _)) return;
            float into = (hangWallGap - gap) * gripWeight;
            if (into <= 0f) return;
            Vector3 limb = end - joint.position;
            joint.rotation = Quaternion.FromToRotation(limb, limb - inward * into) * joint.rotation;
        }
    }

    // As before 0.8.318 (Body.hangRealWall off, to compare by /hang): the wall taken to be the plane straight down from the
    // edge, each joint from the hip down turned back out of it.
    private void KeepLegsOffPlane(Vector3 edge, Vector3 outward)
    {
        OffPlane(leftLeg);
        OffPlane(rightLeg);

        void OffPlane(FootPlanting.Leg leg)
        {
            Out(leg.Upper, leg.Lower.position, leg.Sole);
            Out(leg.Lower, leg.Foot.position, leg.Sole);
            Out(leg.Foot, leg.Foot.TransformPoint(leg.Tip), 0f);
        }

        void Out(Transform joint, Vector3 end, float margin)
        {
            float into = (Vector3.Dot(edge - end, outward) + margin) * gripWeight;
            if (into <= 0f || end.y > edge.y) return;
            Vector3 limb = end - joint.position;
            joint.rotation = Quaternion.FromToRotation(limb, limb + outward * into) * joint.rotation;
        }
    }

    /// <summary>
    /// For the /hang readout, after this frame's legs: how deep each part of this leg is in the wall under the ledge (m, 0
    /// where it is clear: the knee's and the ankle's skin, the toe tip, the sole under it and the heel), how far its toes
    /// stand off the wall (infinity with none within reach), and whether the leg is in a hang at all.
    /// </summary>
    internal bool HangLegReadout(bool isLeft, float[] depths, out float toesOff)
    {
        toesOff = float.PositiveInfinity;
        var leg = isLeft ? leftLeg : rightLeg;
        var ledge = player.Abilities.Traversal.Ledge;
        if (leg == null || ledge.Surface == null || depths == null || depths.Length < 5) return false;
        Transform support = ledge.Surface.transform;
        Vector3 inward = Vector3.ProjectOnPlane(-support.TransformDirection(ledge.LocalNormal), Vector3.up);
        if (inward.sqrMagnitude < 1e-6f) return false;
        inward.Normalize();
        float top = support.TransformPoint(ledge.LocalEdge).y, shin = Vector3.Distance(leg.Lower.position, leg.Foot.position);
        int layers = HangWall.Layers(player.groundLayers);
        Vector3 hip = leg.Upper.position;
        depths[0] = Depth(leg.Lower.position + inward * (shin * hangLegFront.x));
        depths[1] = Depth(leg.Foot.position + inward * (shin * hangLegFront.y));
        depths[2] = Depth(leg.Foot.TransformPoint(leg.Tip));
        depths[3] = Depth(leg.Foot.TransformPoint(leg.TipSole));
        depths[4] = Depth(leg.Foot.TransformPoint(leg.HeelSole));
        Vector3 tip = leg.Foot.TransformPoint(leg.Tip);
        if (tip.y <= top && HangWall.Probe(tip, inward, HangWall.BackFrom(tip, hip, inward), layers, transform, out float gap, out _))
            toesOff = gap;
        return true;

        float Depth(Vector3 point)
            => point.y <= top && HangWall.Probe(point, inward, HangWall.BackFrom(point, hip, inward), layers, transform, out float into, out _)
                ? Mathf.Max(0f, -into) : 0f;
    }

    // Shimmying takes both hands.
    private void FaceWall(float dt) => EaseHangYaw(0f, dt);

    private void EaseHangYaw(float look, float dt)
    {
        hangYaw = Mathf.Lerp(hangYaw, look, 1f - Mathf.Exp(-HangFollowRate * dt));
        // A lerp never arrives, and facing the wall has to count as facing it.
        if (look == 0f && Mathf.Abs(hangYaw) < .5f) hangYaw = 0f;
    }

    private static int KeyBelow(float[] keys, float value, out float toward)
    {
        int key = 0;
        while (key < keys.Length - 2 && value >= keys[key + 1]) key++;
        toward = Mathf.InverseLerp(keys[key], keys[key + 1], value);
        return key;
    }
}
