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
/// A gun: the KINEMATION-era lean (0.7.35, the user's pick), whose camera rides the eyes, as the sight is lined up on them:
/// FPS Animation Framework's Look tips the spine just far enough that the eyes go the lean's distance on this body, the
/// angle found for how it shares the tip down the spine (RollFor). FPSAF tips it in the animation pass, before SpineLook
/// keeps the clip's head, so PlayerBody.FollowHead leaves that part out (ClipHeadOffset); followed, it walked the body
/// back under the camera's pivot and the peek came undone ("오른쪽으로 기울였는데 왼쪽으로 다시 움직여", 2026-11-08).
///
/// Holding something, the leaning hitboxes follow the eyes' actual sideways move. Every other PC's copy leans from the
/// player's pose and slot the same way.
///
/// A plain class PlayerBody owns (stage 3 of the body restructure, 2026-11-09): everything it reads from the body comes in
/// through Reset, Bind, Tune or the call itself, and all its state is here. The numbers stay PlayerBody's table-bound fields
/// (Player.csv Body.leanSpineBend, Body.leanHipShift, Body.leanHipReach), handed over each frame by Tune.
/// </summary>
internal sealed class BodyLean
{
    // The furthest the spine tips for a peek with a gun (degrees), whatever the body.
    private const float MostLeanRoll = 60f;
    // Under this share of a full lean the body counts as upright, and where the eyes sit sideways then is learnt.
    private const float LeanAtRest = .02f;

    private readonly List<(Transform bone, float share)> spineChain = new List<(Transform bone, float share)>();
    private readonly float[] heights = new float[8], shares = new float[8], reachX = new float[8], reachY = new float[8];
    private PlayerMovement player;
    private PlayerLeanHitboxes hitboxes;
    private SpineLook spineLook;
    private Transform root, model, head, leftUpperArm, rightUpperArm;
    private Vector3 eyeLocal;
    private float spineBend = 20f, hipShift = .08f, hipReach = .2f, blendTime = .15f;
    // How much of each way the lean goes now: nothing in hand, a gun; the fists are the rest.
    private float handsFree = 1f, gun;
    private float eyesSideAtRest, clipHeadSideAtRest;
    private bool hasEyesSide, hasClipHeadSide;

    /// <summary>The part of the peek the camera adds to the eyes: the rest of it with nothing in hand, none holding something.</summary>
    internal float CameraMakesUp => handsFree;

    private float FistsShare => Mathf.Clamp01(1f - handsFree - gun);

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
        spineChain.Clear();
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

    /// <summary>The body's lean numbers (degrees, m, m) and its blend time (s).</summary>
    internal void Tune(float spineBend, float hipShift, float hipReach, float blendTime)
    {
        this.spineBend = spineBend;
        this.hipShift = hipShift;
        this.hipReach = hipReach;
        this.blendTime = blendTime;
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

    // The look bend tips the spine bones it bends evenly (SpineLook: each its equal share of the roll).
    private IReadOnlyList<(Transform bone, float share)> SpineChain
    {
        get
        {
            if (spineChain.Count == 0 && spineLook != null)
            {
                var bones = spineLook.Spine;
                foreach (var bone in bones) spineChain.Add((bone, 1f / Mathf.Max(1, bones.Count)));
            }
            return spineChain;
        }
    }

    /// <summary>
    /// The roll (degrees, positive) that carries the eyes <paramref name="offset"/> (m) sideways when the spine tips as
    /// <paramref name="chain"/> shares it, from the lowest bone up: each bone turns its share about itself, carrying the
    /// ones above it. Heights are the bones' as the body stands now.
    /// </summary>
    internal float RollFor(float offset, IReadOnlyList<(Transform bone, float share)> chain)
    {
        if (offset <= .001f || chain == null || chain.Count == 0 || head == null || root == null) return 0f;
        int count = Mathf.Min(chain.Count, heights.Length - 1);
        Vector3 up = root.up;
        for (int i = 0; i < count; i++)
        {
            heights[i] = chain[i].bone != null ? Vector3.Dot(chain[i].bone.position, up) : 0f;
            shares[i] = chain[i].bone != null ? chain[i].share : 0f;
        }
        heights[count] = Vector3.Dot(head.TransformPoint(eyeLocal), up);
        if (Reach(MostLeanRoll) <= offset) return MostLeanRoll;
        float low = 0f, high = MostLeanRoll;
        for (int step = 0; step < 14; step++)
        {
            float middle = (low + high) * .5f;
            if (Reach(middle) < offset) low = middle; else high = middle;
        }
        return (low + high) * .5f;

        // How far sideways the eyes go for a roll, all the points starting on one upright line.
        float Reach(float roll)
        {
            for (int i = 0; i <= count; i++) { reachX[i] = 0f; reachY[i] = heights[i]; }
            for (int i = 0; i < count; i++)
            {
                float angle = roll * shares[i] * Mathf.Deg2Rad, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                for (int j = i + 1; j <= count; j++)
                {
                    float dx = reachX[j] - reachX[i], dy = reachY[j] - reachY[i];
                    reachX[j] = reachX[i] + dx * cos + dy * sin;
                    reachY[j] = reachY[i] - dx * sin + dy * cos;
                }
            }
            return reachX[count];
        }
    }

    /// <summary>
    /// The look bend's tip this frame (positive tips the top to the left, as the camera's own roll does): the old bend with
    /// nothing in hand or the fists, by <paramref name="bendWeight"/> (how far the body bends with the view). With a gun
    /// FPSAF has the upper body; where the look bend has it anyway (a slide with the gun), as far as takes the eyes the
    /// whole peek, as in 0.7.35.
    /// </summary>
    internal float SpineRoll(float bendWeight)
    {
        float share = RawShare;
        float old = -share * bendWeight * spineBend;
        if (gun <= 0f) return old;
        float reach = Mathf.Abs(share) < .001f ? 0f : -Mathf.Sign(share) * RollFor(Mathf.Abs(player.LeanOffset), SpineChain);
        return Mathf.Lerp(old, reach, gun);
    }

    /// <summary>
    /// Before the feet are planted: the weight goes onto the leaning side, and PlantFeet keeps the feet where they stood. The
    /// whole body above moves as one. Nothing in hand, a set hip shift (the camera makes up the rest); the fists, as far as
    /// the spine's bend leaves of the peek, up to the hip reach, so the eyes reach it; a gun, none (FPSAF's tip reaches it).
    /// </summary>
    internal void MoveHips(Transform hips, float bendWeight, bool dead)
    {
        float share = RawShare * bendWeight;
        if (hips == null || head == null || root == null || dead || Mathf.Abs(share) < .001f) return;
        float shift = share * hipShift * handsFree;
        float fists = FistsShare;
        if (fists > 0f)
        {
            float peek = player.LeanOffset * bendWeight, toward = Mathf.Sign(share);
            float bent = hasEyesSide ? Vector3.Dot(head.TransformPoint(eyeLocal) - root.position, root.right) - eyesSideAtRest : 0f;
            shift += toward * Mathf.Clamp((peek - bent) * toward, 0f, hipReach * Mathf.Abs(share)) * fists;
        }
        hips.position += root.right * shift;
    }

    /// <summary>
    /// The sideways part of the clip's head (SpineLook.ClipHeadPosition) that the lean put there, for FollowHead to leave
    /// out. FPS Animation Framework tips the spine before the clip's head is kept, so with a gun the whole peek is in it;
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
        float drop = Mathf.Abs(side - eyesSideAtRest) * Mathf.Tan(Mathf.Abs(RawShare * bendWeight * spineBend) * .5f * Mathf.Deg2Rad);
        return carried - root.up * drop;
    }
}
