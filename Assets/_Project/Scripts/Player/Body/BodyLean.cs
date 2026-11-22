using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Q/E lean of the visible body: one lean, whatever is in the hands and whether the sights are up (the user, 2026-11-22:
/// "주먹이든 줌이든 기울이기 하나로 통일하자", "왜 25cm가 고정으로 가야 돼? 그리고 걍 자연스럽게 하면 안 돼?"). The weight goes
/// over onto that foot, the legs leaning from the planted feet (MoveHips), the pelvis tips after them and the spine bends on
/// from it, a few degrees each, and the eyes go as far as that carries them on this body. The camera stays on them, and
/// what has to know how far they go (the wall a lean stops at, the leaning hitboxes) reads it off the body (Reach, Carry).
/// The head tilts only as far as the view rolls (Ability.leanAngle, BodyGaze.FaceHead), the neck taking back the difference.
/// At the hip with a gun FPS Animation Framework has the upper body, so its Look bends the spine the same angle (GunRoll)
/// and the head and the gun go with the chest as one piece. FPSAF tips the spine in the animation pass, before SpineLook
/// keeps the clip's head, so PlayerBody.FollowHead leaves that part out (ClipHeadOffset); followed, it walked the body back
/// under the camera's pivot and the lean came undone ("오른쪽으로 기울였는데 왼쪽으로 다시 움직여", 2026-11-08).
/// Until 0.9.343 the eyes had to go a set distance (0.35 m, then 0.25), and how a body got them there was a lean of its own
/// for nothing in hand, the fists, a gun at the hip (its spine alone, 38 degrees on Ichigo) and one down the sights.
/// Every other PC's copy leans from the player's pose the same way.
///
/// A plain class PlayerBody owns (stage 3 of the body restructure, 2026-11-09): everything it reads from the body comes in
/// through Reset, Bind, Tune or the call itself, and all its state is here. The numbers stay PlayerBody's table-bound fields
/// (Player.csv Body.lean*), handed over each frame by Tune.
/// </summary>
internal sealed class BodyLean
{
    // Under this share of a full lean the body counts as upright, and where the eyes sit sideways then is learnt.
    private const float LeanAtRest = .02f;

    private PlayerMovement player;
    private PlayerLeanHitboxes hitboxes;
    private SpineLook spineLook;
    private Transform root, model, head, leftUpperArm, rightUpperArm;
    private Vector3 eyeLocal;
    private float legs = 3f, pelvis = 4f, spine = 11f, blendTime = .15f;
    // How far nothing is in the hands now (HangArm).
    private float handsFree = 1f;
    private float eyesSideAtRest, clipHeadSideAtRest;
    private bool hasEyesSide, hasClipHeadSide;

    // The fists are the chosen weapon (a slot with no gun in it), on this PC or as the other PC's slot shows on the copy.
    private bool HoldsFists => player != null && player.MeleeCombat != null && player.MeleeCombat.IsActive;

    // How far the Q/E lean has gone, -1 (left) to 1 (right). Every other PC knows it from the player's pose.
    private float RawShare => player != null && player.Abilities != null ? player.Abilities.Lean.Share : 0f;

    private bool Leaning(bool dead) => !dead && Mathf.Abs(RawShare) >= LeanAtRest;

    /// <summary>A new body: forgets what it learnt. <paramref name="root"/> is the player (PlayerBody's transform),
    /// <paramref name="model"/> the body's model under it.</summary>
    internal void Reset(PlayerMovement player, Transform root, Transform model, Transform head, SpineLook spineLook, bool holdsGun)
    {
        this.player = player;
        this.root = root;
        this.model = model;
        this.head = head;
        this.spineLook = spineLook;
        leftUpperArm = rightUpperArm = null;
        eyeLocal = Vector3.zero;
        hasEyesSide = hasClipHeadSide = false;
        handsFree = holdsGun || HoldsFists ? 0f : 1f;
        hitboxes = player != null ? player.GetComponent<PlayerLeanHitboxes>() : null;
    }

    /// <summary>Where the eyes sit in the head's frame, and the upper arms, once the body has measured them.</summary>
    internal void Bind(Vector3 eyeLocal, Transform leftUpperArm, Transform rightUpperArm)
    {
        this.eyeLocal = eyeLocal;
        this.leftUpperArm = leftUpperArm;
        this.rightUpperArm = rightUpperArm;
    }

    /// <summary>At a full lean, how far the legs lean over onto the foot, the pelvis tips and the spine bends on from it
    /// (degrees), and the body's blend time (s).</summary>
    internal void Tune(float legs, float pelvis, float spine, float blendTime)
    {
        this.legs = legs;
        this.pelvis = pelvis;
        this.spine = spine;
        this.blendTime = blendTime;
    }

    /// <summary>Toward what is in the hands, so changing slots mid-lean doesn't pop. In Update, with the look's inputs.</summary>
    internal void BlendHands(bool holdsGun, float dt)
        => handsFree = Mathf.MoveTowards(handsFree, holdsGun || HoldsFists ? 0f : 1f, dt > 0f ? dt / Mathf.Max(.01f, blendTime) : 1f);

    /// <summary>
    /// The roll (degrees, positive) FPS Animation Framework's Look takes at a full lean to bend the body this lean, as
    /// <paramref name="chain"/> shares it down this body's spine: a body with fewer spine bones takes more of it in each.
    /// </summary>
    internal float GunRoll(IReadOnlyList<(Transform bone, float share)> chain)
    {
        float sum = 0f;
        if (chain != null)
            foreach (var (bone, share) in chain)
                if (bone != null) sum += share;
        return sum > .01f ? Mathf.Min(spine / sum, 90f) : 0f;
    }

    /// <summary>
    /// The look bend's tip this frame (positive tips the top to the left, as the camera's own roll does), by
    /// <paramref name="bendWeight"/> (how far the body bends with the view). At the hip FPSAF has the upper body and bends it
    /// itself (GunRoll).
    /// </summary>
    internal float SpineRoll(float bendWeight) => -RawShare * bendWeight * spine;

    /// <summary>
    /// Before the feet are planted: the weight goes onto the leaning side, the legs leaning over from the feet, and the
    /// pelvis tips after them. Everything above the hips moves as one.
    /// Returns how far it moved the hips (world): the legs hang from them, so the feet went that far too, and the feet's
    /// owner takes it back out (FootPlanting.Hold.Carried). This comment used to say "PlantFeet keeps the feet where they
    /// stood", which nothing did: the feet's height was the ground's and their place wherever the hips had gone, so the
    /// lean slid both feet sideways with the pelvis.
    /// </summary>
    internal Vector3 MoveHips(Transform hips, float bendWeight, bool dead, Transform leftThigh, Transform rightThigh)
    {
        if (hips == null || head == null || root == null || dead) return Vector3.zero;
        float height = Vector3.Dot(hips.position - root.position, root.up), over = height * Mathf.Sin(legs * Mathf.Deg2Rad);
        if (!Leaning(false)) MeasureReach(height, over);
        float share = RawShare * bendWeight;
        if (Mathf.Abs(share) < .001f) return Vector3.zero;
        // The pelvis tips a little the way of the lean (the user, 2026-11-18, asked whether to: "그 편이 더 자연스럽다면
        // 그렇게"): with the weight gone over one planted foot a pelvis does not stay level, and a spine bent from a level
        // pelvis kinks at the waist. The thighs are turned back as they were, so the legs stay the clip's (the hip joints
        // move a few millimetres) and only what is above the pelvis goes with it.
        float tilt = -share * pelvis;
        if (Mathf.Abs(tilt) > .01f)
        {
            Quaternion left = leftThigh != null ? leftThigh.rotation : Quaternion.identity;
            Quaternion right = rightThigh != null ? rightThigh.rotation : Quaternion.identity;
            hips.rotation = Quaternion.AngleAxis(tilt, root.forward) * hips.rotation;
            if (leftThigh != null) leftThigh.rotation = left;
            if (rightThigh != null) rightThigh.rotation = right;
        }
        Vector3 moved = root.right * (share * over);
        hips.position += moved;
        return moved;
    }

    // How far a full lean takes this body's eyes sideways as it stands now (m): the hips over the foot, and each joint's turn
    // by how far the eyes are above it. The movement asks it for the wall a lean stops at (MovementLean).
    private void MeasureReach(float hipsHeight, float over)
    {
        if (player == null || player.Abilities == null || spineLook == null) return;
        Vector3 up = root.up;
        float eyes = Vector3.Dot(head.TransformPoint(eyeLocal) - root.position, up);
        float reach = over + (eyes - hipsHeight) * Mathf.Sin(pelvis * Mathf.Deg2Rad);
        var bones = spineLook.Spine;
        float each = Mathf.Sin(spine / Mathf.Max(1, bones.Count) * Mathf.Deg2Rad);
        foreach (var bone in bones)
            if (bone != null) reach += (eyes - Vector3.Dot(bone.position - root.position, up)) * each;
        player.Abilities.Lean.Reach = Mathf.Max(.02f, reach);
    }

    /// <summary>
    /// The sideways part of the clip's head (SpineLook.ClipHeadPosition) that the lean put there, for FollowHead to leave
    /// out. FPS Animation Framework tips the spine before the clip's head is kept, so with a gun at the hip its tip is in it;
    /// the look bend's own tip comes after, so otherwise it stays about zero. Measured against where it sits upright in the
    /// model's own frame, so the body's following doesn't feed back into it.
    /// </summary>
    internal Vector3 ClipHeadOffset(bool dead, float dt)
    {
        if (model == null || head == null || root == null || spineLook == null) return Vector3.zero;
        float side = Vector3.Dot(spineLook.ClipHeadPosition - model.position, root.right);
        bool leaning = Leaning(dead);
        if (!hasClipHeadSide) { clipHeadSideAtRest = side; hasClipHeadSide = true; }
        else if (!leaning) clipHeadSideAtRest = Mathf.Lerp(clipHeadSideAtRest, side, 1f - Mathf.Exp(-dt / .2f));
        return leaning ? root.right * (side - clipHeadSideAtRest) : Vector3.zero;
    }

    /// <summary>
    /// The arm on the leaning side keeps hanging the way the clip had it, with nothing in hand. Turned with the spine it swung
    /// in through the hip the weight had gone onto. The other arm stays with the spine: that side tips away, and the arm
    /// rests along it. <paramref name="spineRoll"/> is the spine's tip this frame, as much of it as the look bend holds.
    /// With a gun or the fists both arms follow (the user, 2026-11-08).
    /// </summary>
    internal void HangArm(float spineRoll, bool dead, bool holdsGun)
    {
        float roll = spineRoll * handsFree;
        if (Mathf.Abs(roll) < .01f || dead || holdsGun || HoldsFists || root == null) return;
        var arm = roll < 0f ? rightUpperArm : leftUpperArm;
        if (arm != null) arm.rotation = Quaternion.AngleAxis(-roll, root.forward) * arm.rotation;
    }

    /// <summary>After the pose, on every PC: the leaning hitboxes go as far as the eyes have actually gone sideways from
    /// where they sit upright.</summary>
    internal void Carry(Vector3 eye, bool dead, float dt)
    {
        if (root == null) return;
        float side = Vector3.Dot(eye - root.position, root.right);
        bool leaning = Leaning(dead);
        if (!hasEyesSide) { eyesSideAtRest = side; hasEyesSide = true; }
        else if (!leaning) eyesSideAtRest = Mathf.Lerp(eyesSideAtRest, side, 1f - Mathf.Exp(-dt / .2f));
        if (hitboxes != null) hitboxes.FollowEyes(leaning ? root.right * (side - eyesSideAtRest) : Vector3.zero);
    }
}
