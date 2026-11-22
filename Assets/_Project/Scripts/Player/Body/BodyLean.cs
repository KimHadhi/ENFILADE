using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Q/E lean of the visible body. It peeks three ways, by what is in the hands (the user's split, 2026-11-08), blended
/// over the body's blendTime when that changes. The head tilts only as far as the view rolls (Ability.leanAngle,
/// BodyGaze.FaceHead), the neck taking back the difference.
///
/// Nothing in hand (no slot chosen): as up to 0.7.34, which the user liked —
/// - the hips go over onto the leaning side's foot while the feet stay put (hip shift, MoveHips before PlantFeet);
/// - the spine bends sideways from the waist, the spine bend at a full lean (SpineRoll, done by SpineLook);
/// - the arm on the leaning side is left hanging the way the clip had it (HangArm), more natural than letting it swing in
///   with the spine;
/// - the camera makes up the rest of the peek (Carry: what the body carried, measured on the eyes), so it still goes
///   Ability.leanOffset sideways, and the leaning hitboxes go out by that distance.
///
/// The fists: the same spine bend, and the hips go over as far as the bend leaves of the peek (up to the hip reach), so the
/// eyes reach it and the camera stays on them: the guard is placed from the camera, and a camera off the eyes stretched the
/// guard arm straight out (user's screenshot). The KINEMATION lean tipped the spine 40-60° for the whole peek, which looked
/// grotesque with the fists up (user's screenshot). Both arms follow.
///
/// A gun, at the hip and down the sights alike (0.9.342): as the fists, each part a little. The spine bends leanGunBend (more
/// on the move, where a body carried far over its feet would walk askew), the pelvis tips leanGunTilt, and the hips go over
/// as far as that leaves of the peek, so the eyes reach it and the camera stays on them. At the hip FPS Animation
/// Framework has the spine and its Look bends it that far (GunRoll); it carries the head and the gun with it, so they lean
/// as one piece with the chest. Down the sights the look bend bends it (SpineRoll), the head keeps to the view and the gun
/// is held on the camera. The trunk and the hips are in the same place either way, so the sights come up in a lean without
/// the body moving. Both ways came from a lean of their own before: at the hip the spine alone went the whole peek, 38
/// degrees on Ichigo, over hips that stayed where they were (KINEMATION's, 0.7.35), and down the sights the fists' 14 with
/// the hips 13 cm over (0.9.321). The user, 2026-11-22: "그냥 기울이기는 너무 기울이고 너무 꺾여", "좋은 점만 서로 딸 순 없나?".
/// FPSAF tips the spine in the animation pass, before SpineLook keeps the clip's head, so PlayerBody.FollowHead leaves that
/// part out (ClipHeadOffset); followed, it walked the body back under the camera's pivot and the peek came undone ("오른쪽으로
/// 기울였는데 왼쪽으로 다시 움직여", 2026-11-08).
///
/// Holding something, the leaning hitboxes follow the eyes' actual sideways move. Every other PC's copy leans from the
/// player's pose and slot the same way.
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
    private float spineBend = 20f, hipShift = .08f, hipReach = .2f, pelvisTilt, gunBend = 18f, gunTilt = 4f, blendTime = .15f;
    // How much of each way the lean goes now: nothing in hand, a gun; the fists are the rest.
    private float handsFree = 1f, gun;
    private float eyesSideAtRest, clipHeadSideAtRest;
    private bool hasEyesSide, hasClipHeadSide;

    /// <summary>The part of the peek the camera adds to the eyes: the rest of it with nothing in hand, none holding something.</summary>
    internal float CameraMakesUp => handsFree;

    // The spine's bend and the pelvis's tip at a full lean, for what is in the hands.
    private float Bend => Mathf.Lerp(spineBend, gunBend, gun);
    private float Tilt => Mathf.Lerp(pelvisTilt, gunTilt, gun);

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
        gun = holdsGun ? 1f : 0f;
        hitboxes = player != null ? player.GetComponent<PlayerLeanHitboxes>() : null;
    }

    /// <summary>Where the eyes sit in the head's frame, and the upper arms, once the body has measured them.</summary>
    internal void Bind(Vector3 eyeLocal, Transform leftUpperArm, Transform rightUpperArm)
    {
        this.eyeLocal = eyeLocal;
        this.leftUpperArm = leftUpperArm;
        this.rightUpperArm = rightUpperArm;
    }

    /// <summary>The body's lean numbers (degrees, m, m), its blend time (s) and the pelvis's tip (degrees); with a gun out
    /// the spine's bend and the pelvis's tip instead.</summary>
    internal void Tune(float spineBend, float hipShift, float hipReach, float blendTime, float pelvisTilt, float gunBend, float gunTilt)
    {
        this.gunBend = gunBend;
        this.gunTilt = gunTilt;
        this.spineBend = spineBend;
        this.hipShift = hipShift;
        this.hipReach = hipReach;
        this.blendTime = blendTime;
        this.pelvisTilt = pelvisTilt;
    }

    /// <summary>
    /// Toward the way for what is in the hands, so changing slots mid-lean doesn't pop. In Update, with the look's inputs.
    /// Both move at one rate, so the fists' share (the rest) only ever stands between them.
    /// </summary>
    internal void BlendHands(bool holdsGun, float dt)
    {
        float free = holdsGun || HoldsFists ? 0f : 1f, armed = holdsGun ? 1f : 0f;
        float step = dt > 0f ? dt / Mathf.Max(.01f, blendTime) : 1f;
        handsFree = Mathf.MoveTowards(handsFree, free, step);
        gun = Mathf.MoveTowards(gun, armed, step);
    }

    /// <summary>
    /// The roll (degrees, positive) FPS Animation Framework's Look takes at a full lean to bend the body the gun's lean, as
    /// <paramref name="chain"/> shares it down this body's spine: a body with fewer spine bones takes more of it in each.
    /// </summary>
    internal float GunRoll(IReadOnlyList<(Transform bone, float share)> chain)
    {
        float sum = 0f;
        if (chain != null)
            foreach (var (bone, share) in chain)
                if (bone != null) sum += share;
        return sum > .01f ? Mathf.Min(gunBend / sum, 90f) : 0f;
    }

    /// <summary>
    /// The look bend's tip this frame (positive tips the top to the left, as the camera's own roll does), by
    /// <paramref name="bendWeight"/> (how far the body bends with the view). At the hip FPSAF has the upper body and bends it
    /// itself (GunRoll).
    /// </summary>
    internal float SpineRoll(float bendWeight) => -RawShare * bendWeight * Bend;

    /// <summary>
    /// Before the feet are planted: the weight goes onto the leaning side. The whole body above moves as one. Nothing in
    /// hand, a set hip shift (the camera makes up the rest); the fists or a gun, as far as the spine's bend leaves of the peek,
    /// up to the hip reach, so the eyes reach it.
    /// Returns how far it moved the hips (world): the legs hang from them, so the feet went that far too, and the feet's
    /// owner takes it back out (FootPlanting.Hold.Carried). This comment used to say "PlantFeet keeps the feet where they
    /// stood", which nothing did: the feet's height was the ground's and their place wherever the hips had gone, so the
    /// lean slid both feet sideways with the pelvis.
    /// </summary>
    internal Vector3 MoveHips(Transform hips, float bendWeight, bool dead, Transform leftThigh, Transform rightThigh)
    {
        float share = RawShare * bendWeight;
        if (hips == null || head == null || root == null || dead || Mathf.Abs(share) < .001f) return Vector3.zero;
        // The pelvis tips a little the way of the lean (the user, 2026-11-18, asked whether to: "그 편이 더 자연스럽다면
        // 그렇게"): with the weight gone over one planted foot a pelvis does not stay level, and a spine bent from a level
        // pelvis kinks at the waist. The thighs are turned back as they were, so the legs stay the clip's (the hip joints
        // move a few millimetres) and only what is above the pelvis goes with it: the eyes go further, so the hips, just
        // below, have less far to go for the same peek.
        float tilt = -share * Tilt;
        if (Mathf.Abs(tilt) > .01f)
        {
            Quaternion left = leftThigh != null ? leftThigh.rotation : Quaternion.identity;
            Quaternion right = rightThigh != null ? rightThigh.rotation : Quaternion.identity;
            hips.rotation = Quaternion.AngleAxis(tilt, root.forward) * hips.rotation;
            if (leftThigh != null) leftThigh.rotation = left;
            if (rightThigh != null) rightThigh.rotation = right;
        }
        float shift = share * hipShift * handsFree;
        float holding = Mathf.Clamp01(1f - handsFree);
        if (holding > 0f)
        {
            float peek = player.LeanOffset * bendWeight, toward = Mathf.Sign(share);
            float bent = hasEyesSide ? Vector3.Dot(head.TransformPoint(eyeLocal) - root.position, root.right) - eyesSideAtRest : 0f;
            shift += toward * Mathf.Clamp((peek - bent) * toward, 0f, hipReach * Mathf.Abs(share)) * holding;
        }
        Vector3 moved = root.right * shift;
        hips.position += moved;
        return moved;
    }

    /// <summary>
    /// The sideways part of the clip's head (SpineLook.ClipHeadPosition) that the lean put there, for FollowHead to leave
    /// out. FPS Animation Framework tips the spine before the clip's head is kept, so with a gun at the hip its tip is in it;
    /// the look bend's own tip comes after, so with the fists or nothing in hand it stays about zero. Measured against
    /// where it sits upright in the model's own frame, so the body's following doesn't feed back into it.
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

    /// <summary>
    /// After the pose, on every PC: how far the eyes have actually gone sideways from where they sit upright. The leaning
    /// hitboxes go with them holding something, by the whole lean's distance with nothing in hand (where the camera makes up
    /// the rest). Returns that sideways move with the small drop the bend brings, which the camera keeps out.
    /// </summary>
    internal Vector3 Carry(Vector3 eye, float bendWeight, bool dead, float dt)
    {
        if (root == null) return Vector3.zero;
        float side = Vector3.Dot(eye - root.position, root.right);
        bool leaning = Leaning(dead);
        if (!hasEyesSide) { eyesSideAtRest = side; hasEyesSide = true; }
        else if (!leaning) eyesSideAtRest = Mathf.Lerp(eyesSideAtRest, side, 1f - Mathf.Exp(-dt / .2f));
        Vector3 carried = leaning ? root.right * (side - eyesSideAtRest) : Vector3.zero;
        if (hitboxes != null)
            hitboxes.FollowEyes(Vector3.Lerp(carried, leaning ? root.right * player.LeanOffset : Vector3.zero, handsFree));
        if (!leaning) return Vector3.zero;
        float drop = Mathf.Abs(side - eyesSideAtRest) * Mathf.Tan(Mathf.Abs(RawShare * bendWeight * Bend) * .5f * Mathf.Deg2Rad);
        return carried - root.up * drop;
    }
}
