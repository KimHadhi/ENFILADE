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

    // A hanging leg lies along the wall, and clips made for longer arms or shorter legs put a knee or a foot through it. Each
    // joint from the hip down is turned back out of the wall, as little as it needs, whatever the clip or the character.
    private void KeepLegsOffWall()
    {
        var ledge = player.Abilities.Traversal.Ledge;
        if (gripWeight <= 0f || ledge.Surface == null || (state != MovementState.LedgeHang && !HeavePressing)
            || leftLeg == null || rightLeg == null) return;
        Transform support = ledge.Surface.transform;
        Vector3 outward = Vector3.ProjectOnPlane(support.TransformDirection(ledge.LocalNormal), Vector3.up).normalized;
        Vector3 edge = support.TransformPoint(ledge.LocalEdge);
        OffWall(leftLeg);
        OffWall(rightLeg);

        void OffWall(Leg leg)
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
