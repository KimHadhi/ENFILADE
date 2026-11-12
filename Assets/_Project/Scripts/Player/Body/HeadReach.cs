using UnityEngine;

/// <summary>
/// Brings the head to the camera, so the camera never has to leave the head (the user, 2026-11-12: "캐릭터의 머리와
/// 카메라가 일치해서 True FPS의 몰입감은 있으면서 너무 멀미나거나 게임 플레이에 방해가 되지 않게"). Where the camera
/// steadies off the eyes (a punch, a slip's dip, the share of a Q/E peek the camera makes up with nothing in hand, a wall
/// keeping it back, less body sway in the settings), the spine, chest and neck bend so the eyes come to it and the head
/// keeps its own turn, as people hold their head steady while the body moves under it. Seen from outside (F5, a mirror, a
/// friend's screen) the head is then where its view is.
/// - Each bone takes a share of the move, from the spine up, and turns at most its own angle; the eyes move at most
///   maxMove. Past that the camera keeps the rest off the eyes, as before, so a neck never looks broken.
/// - A turn swings the head round a bone but can't stretch the neck, so an upright body can't raise its head this way. On
///   a ladder, where the camera stays at its height in the capsule, the hands move the whole body to it (PlayerBody.Grip).
/// - While FPS Animation Framework holds a gun, only the neck bends: the arms hang from the chest, and turning it would
///   take the hands off the gun FPSAF has already posed.
/// A plain class PlayerBody owns, as SpineLook is. It runs right after the camera is placed (PlayerBody.Look), the last
/// of the pose work, so the hands (PlayerBodyHands) take their grips from the bent body. Numbers: Player.csv, row Body.
/// </summary>
internal sealed class HeadReach
{
    // The bones that bend, from the hips up, and each one's share of the move; a missing bone's share goes to the others.
    // The neck takes little: it is short on these characters (6 cm on Mayo, 5 cm on Ichigo as the game draws them).
    private static readonly HumanBodyBones[] Chain = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck };
    private static readonly float[] Shares = { .3f, .3f, .2f, .2f };
    private const int NeckIndex = 3;

    private readonly Transform[] bones = new Transform[Chain.Length];
    private Transform head;

    /// <summary>Takes the spine, neck and head of the body's humanoid avatar.</summary>
    internal void Bind(Animator animator)
    {
        Clear();
        if (animator == null || !animator.isHuman) return;
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        for (int i = 0; i < Chain.Length; i++) bones[i] = animator.GetBoneTransform(Chain[i]);
    }

    internal void Clear()
    {
        head = null;
        for (int i = 0; i < bones.Length; i++) bones[i] = null;
    }

    /// <summary>
    /// Bends the body so the eyes (<paramref name="eyeLocal"/>, in the head's frame) come to <paramref name="goal"/>, by at
    /// most <paramref name="maxMove"/> (m). <paramref name="limits"/>: the most each spine bone (x) and the neck (y) turns
    /// (degrees). Only the neck bends when <paramref name="neckOnly"/>.
    /// </summary>
    internal void Meet(Vector3 eyeLocal, Vector3 goal, float maxMove, Vector2 limits, bool neckOnly)
    {
        if (head == null || maxMove <= 0f) return;
        Vector3 move = Vector3.ClampMagnitude(goal - head.TransformPoint(eyeLocal), maxMove);
        // Under a millimetre: the camera is on the eyes, as it is most of the time.
        if (move.sqrMagnitude < 1e-6f) return;
        Quaternion turn = head.rotation;
        // The head's turn is put back at the end, so the eyes go wherever the head bone goes.
        Vector3 want = head.position + move;
        float left = 0f;
        for (int i = 0; i < bones.Length; i++) if (Bends(i, neckOnly)) left += Shares[i];
        for (int i = 0; i < bones.Length && left > 0f; i++)
        {
            if (!Bends(i, neckOnly)) continue;
            // Each bone its share of what is still to go, so a bone at its limit leaves the rest to the ones above it.
            float share = Shares[i] / left;
            left -= Shares[i];
            Swing(bones[i], (want - head.position) * share, i == NeckIndex ? limits.y : limits.x);
        }
        // The head keeps its own turn (the gaze, or FPSAF's), so the view's direction and the face stay as they were: only
        // where the head is changes.
        head.rotation = turn;
    }

    private bool Bends(int i, bool neckOnly) => bones[i] != null && (!neckOnly || i == NeckIndex);

    // Turns the bone so the head goes `part` further, as far as a turn round the bone takes it, at most `limit` degrees.
    private void Swing(Transform bone, Vector3 part, float limit)
    {
        Vector3 lever = head.position - bone.position;
        if (limit <= 0f || lever.sqrMagnitude < 1e-6f || part.sqrMagnitude < 1e-8f) return;
        Quaternion.FromToRotation(lever, lever + part).ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (Mathf.Abs(angle) < 1e-3f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;
        bone.rotation = Quaternion.AngleAxis(Mathf.Clamp(angle, -limit, limit), axis) * bone.rotation;
    }
}
