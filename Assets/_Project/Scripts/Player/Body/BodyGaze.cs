using UnityEngine;

/// <summary>
/// Where the body looks:
/// - the mouse leads the view at once; selected action clips (a vault, a climb; PlayerBody picks them) add a bounded
///   share of their head's turn on top, smoothed on its own, less the more the player aims (Update). The jumps, landings and
///   rolls go further: their head's turn about the body's own right (a somersault, a landing's nod) carries the view all
///   the way, so a roll turns the view over with the head (the user, 2026-11-21: the view held ahead through a dive roll
///   or a jump, "억지로 앞에 강제로 닭처럼 고정된 느낌"); their turn about up and ahead stays bounded, so the aim keeps its way
///   round and the horizon about level;
/// - in every state the head turns to look where the view looks, as it did up to 0.7.34: with the look bend only upright or
///   sliding, a vault, hang, roll or rung left the head where the clip had it, away from the aim in the F5 view and on
///   friends' screens (user picked it back, 2026-11-08). An action clip's head keeps its own nods within a cone round the
///   view and follows the view past it, no further than a neck bends from the chest (FaceHead). Q/E tilts it only as far as
///   the view rolls; the neck takes back the spine's tip. While FPS Animation Framework holds a gun the head is its (its
///   Look lines the sight up on the eyes, the gun hangs from the head), so this steps back (FaceHead).
/// The camera takes Rotation, with the camera effects' turn on top of Input (BodyCamera).
/// A plain class PlayerBody owns (stage 3 of the body restructure, 2026-11-09). The numbers stay PlayerBody's table-bound
/// fields (Player.csv Body.animationViewStrength, Body.animationViewLimits, Body.animationViewTime), handed over by Tune.
/// </summary>
internal sealed class BodyGaze
{
    private Transform root, lookPivot, model, head, neck, chest;
    // The head's gaze from its bone, and the way a head looks from the chest bone with the neck straight (both from the rest
    // pose).
    private Quaternion headToGaze = Quaternion.identity, chestToGaze = Quaternion.identity;
    private Quaternion rotation = Quaternion.identity, actionTurn = Quaternion.identity;
    private float actionWeight, faceWeight;
    // The neck guard's turn from the chest the head was let go to on the last frame and the view's reading then (degrees), and
    // whether there was one (FaceHead).
    private float guardTurn, guardWas;
    private bool guardKnown;
    // The guard goes over from one shoulder to the other at this (degrees a second) past what the view turned, and keeps a
    // shoulder while the view is within GuardSideSlack of the back on the other side.
    private const float GuardSwapRate = 720f, GuardSideSlack = 30f;
    // The carried head's turn over the body's right (degrees) and its turn on the last frame (Update).
    private float overTurn;
    private Quaternion overWas = Quaternion.identity;
    private bool overKnown;
    private float strength = .65f, follow = 1f, time = .1f, cone = 20f;
    // The neck's guard has a view to hold from LevelFrom of its ahead along the ground, fully by LevelBy (FaceHead).
    private const float LevelFrom = .1f, LevelBy = .3f;
    private Vector3 limits = new Vector3(18f, 12f, 10f);
    // The most the neck bends the head from the chest to follow the view: up (x), down (y), degrees.
    private Vector2 neckPitch = new Vector2(60f, 50f);

    /// <summary>Where the view looks this frame: the mouse's look with the action clips' share on top.</summary>
    internal Quaternion Rotation => rotation;

    /// <summary>Where the head looks now (its gaze, as the rest pose measured it).</summary>
    internal Quaternion HeadGaze => head != null ? head.rotation * headToGaze : rotation;

    /// <summary>Where the mouse alone has the view.</summary>
    internal Quaternion Input => lookPivot != null ? lookPivot.rotation : root.rotation;

    /// <summary>A new body: the head starts on its clip. <paramref name="root"/> is the player, <paramref name="lookPivot"/>
    /// what the mouse turns (null: the player itself).</summary>
    internal void Reset(Transform root, Transform lookPivot)
    {
        this.root = root;
        this.lookPivot = lookPivot;
        faceWeight = 0f;
        guardKnown = false;
    }

    /// <summary>
    /// The body's head and neck, measured in its rest pose. Imported heads have arbitrary bone axes, so the anatomical gaze
    /// is <paramref name="restForward"/> (the way the body faces at rest) about the model's up.
    /// </summary>
    internal void SetUp(Transform model, Transform head, Transform neck, Vector3 restForward)
    {
        this.model = model;
        this.head = head;
        this.neck = neck;
        headToGaze = Quaternion.Inverse(head.rotation) * Quaternion.LookRotation(restForward, model.up);
        chest = neck != null ? neck.parent : head.parent;
        chestToGaze = chest != null ? Quaternion.Inverse(chest.rotation) * Quaternion.LookRotation(restForward, model.up) : Quaternion.identity;
        actionTurn = Quaternion.identity;
        actionWeight = 0f;
        rotation = Input;
    }

    /// <summary>How much the action clips lead the view (0-1), how much of their head's turn over the body's right the
    /// carrying clips give it (0-1), the most the leading clips turn it (degrees: pitch, yaw, roll), how quickly their
    /// turn follows (s), how far from the view an action clip's head keeps its own turn (degrees), and the most the neck
    /// bends the head from the chest to follow the view past that (degrees: up, down).</summary>
    internal void Tune(float strength, float follow, Vector3 limits, float time, float cone, Vector2 neckPitch)
    {
        this.strength = strength;
        this.follow = follow;
        this.limits = limits;
        this.time = time;
        this.cone = Mathf.Max(0f, cone);
        this.neckPitch = new Vector2(Mathf.Clamp(neckPitch.x, 0f, 90f), Mathf.Clamp(neckPitch.y, 0f, 90f));
    }

    /// <summary>
    /// This frame's gaze. <paramref name="ledWeight"/> is how much of the pose the gaze-leading action clips have and
    /// <paramref name="carriedWeight"/> the clips whose head carries the view (their actual crossfade, an action playing out
    /// after its move included); <paramref name="clipHead"/> the head as the clips posed it, before the look bent it
    /// (SpineLook keeps it), so neither last frame's camera nor its effects feed back.
    /// </summary>
    internal void Update(float ledWeight, float carriedWeight, float aimBlend, bool dead, Quaternion clipHead, float dt)
    {
        Quaternion input = Input;
        actionWeight = Mathf.Clamp01(ledWeight + carriedWeight);
        float steady = dead ? 0f : 1f - aimBlend;
        float led = Mathf.Clamp01(ledWeight) * Mathf.Clamp01(strength) * steady;
        float carried = Mathf.Clamp01(carriedWeight) * Mathf.Clamp01(follow) * steady;

        // Body yaw belongs to the hips, not aim.
        Quaternion authored = Quaternion.Inverse(model.rotation) * (clipHead * headToGaze);
        // Fold inverted motion into small view offsets. Euler decomposition turns a pure somersault past 90 degrees into
        // spurious yaw/roll of 180 degrees, causing an unrelated sideways camera kick.
        Vector3 forward = authored * Vector3.forward, right = authored * Vector3.right, up = authored * Vector3.up;
        Vector3 angles = new Vector3(
            Mathf.Atan2(-forward.y, new Vector2(forward.x, forward.z).magnitude),
            Mathf.Atan2(forward.x, Mathf.Abs(forward.z)),
            Mathf.Atan2(right.y, Mathf.Abs(up.y))) * Mathf.Rad2Deg;
        angles = new Vector3(Limit(angles.x, limits.x), Limit(angles.y, limits.y), Limit(angles.z, limits.z));
        // The carrying clips' turn over the body's right, all of it: the part of the head's turn about that axis (its twist),
        // one formula that goes on round past upside down without a jump. Until 0.9.286 it was folded into the bounded pitch,
        // and a roll moved the view by 12 degrees while the head went over; until 0.9.398 it was read from whichever of the
        // head's ahead and up lay more in that plane, two readings that differ once the head turns or tilts and swapped
        // wherever the two came level, as an upright head's do.
        // While a clip carries the view it is summed from each frame's step, so a roll whose head goes over another way (to the
        // side, on a diagonal) can't send it round past 180 in a frame where the whole turn's reading is ill-defined (the audit
        // of 2026-11-24); with nothing carrying it is the whole turn's, an upright head's near nothing. The sum is kept within
        // +-180, as the whole turn's reading is: the view takes `carried` of it, and as a roll's clip hands over that share
        // must come back to nothing the short way. 0.9.400 summed it unbounded, a whole roll came to 360, and as the roll
        // handed over to the stand or the run the view, and the head on it, turned over through upside down with the share
        // (head and neck 48 to 78 degrees in a frame: the pops record of 2026-11-24 20:3x).
        if (carried > 0f && overKnown)
        {
            Quaternion step = authored * Quaternion.Inverse(overWas);
            overTurn = Mathf.DeltaAngle(0f, overTurn + Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(step.x, step.w) * Mathf.Rad2Deg));
        }
        else overTurn = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(authored.x, authored.w) * Mathf.Rad2Deg);
        overWas = authored;
        overKnown = true;
        float over = overTurn;
        float sideways = Mathf.Clamp01(led + carried);
        // About the body's own right, not the view's: a dive roll out of a slide goes the slide's way with the view looking aside.
        Quaternion target = Quaternion.AngleAxis(over * carried, Quaternion.Inverse(input) * (model.rotation * Vector3.right))
            * Quaternion.Euler(angles.x * led, angles.y * sideways, angles.z * sideways);
        actionTurn = Quaternion.Slerp(actionTurn, target, 1f - Mathf.Exp(-dt / Mathf.Max(.01f, time)));
        rotation = input * actionTurn;

        static float Limit(float angle, float limit) => Mathf.Clamp(Mathf.DeltaAngle(0f, angle), -Mathf.Abs(limit), Mathf.Abs(limit));
    }

    /// <summary>
    /// Turns the head (and a little of the neck) to the gaze, tilted by <paramref name="leanRoll"/> (degrees, the view's
    /// Q/E roll); an action clip's head only as far as it is off the view past the cone. <paramref name="off"/>: the head
    /// isn't this one's (dead, or FPSAF holds a gun); it hands over and takes it back over <paramref name="blendTime"/> (s).
    /// With <paramref name="neckLimit"/> above 0 the head turns no further than that (degrees) from
    /// <paramref name="chestAhead"/>, the way the chest faces, by <paramref name="upright"/> (0-1: how upright the body is to
    /// have a facing); true when it had to stop there or at the neck's bend (PlayerBody.Neck).
    /// </summary>
    internal bool FaceHead(bool off, float leanRoll, float blendTime, float dt, Vector3 chestAhead = default, float neckLimit = 0f,
        float upright = 0f)
    {
        faceWeight = Mathf.MoveTowards(faceWeight, off ? 0f : 1f, dt / blendTime);
        if (faceWeight <= 0f || head == null) return false;
        Quaternion gaze = rotation * Quaternion.Euler(0f, 0f, leanRoll);
        bool held = false;
        // The view is held where the head can turn (PlayerLook, PlayerBody.HeadReachArc); this catches a frame a clip turned
        // the chest away faster than that, so the head never turns into the shoulder. It is a turn about the world's up, so
        // it holds by how much it has to go on, each eased in: a body upright enough to have a facing, a view looking more
        // along the ground than up or down, and no action clip, whose head the neck's own bend from the chest holds instead
        // (WithinNeck, below). Until 0.9.401 each was yes or no: as a roll's body came up past upright (RollToRun_F at 0.27 s,
        // the hips-to-head line from 149 degrees over to 64) the guard came on all at once against a view still turned over
        // with the head, and turned the head and its hair 23 degrees in a frame in every play (the pops record of 2026-11-24
        // 20:3x; the user: "랜딩 롤은 여전히 튀는데").
        Vector3 ahead = Vector3.ProjectOnPlane(gaze * Vector3.forward, Vector3.up);
        Vector3 chest = Vector3.ProjectOnPlane(chestAhead, Vector3.up);
        float guard = Mathf.Clamp01(upright) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LevelFrom, LevelBy, ahead.magnitude))
            * (1f - actionWeight);
        // With the view behind the chest the head keeps the shoulder it is held over, and goes over to the other through the
        // front at GuardSwapRate once the view is well round to it. Until 0.9.421 it went to whichever shoulder the view read
        // nearer each frame: as the view passed behind, the head swung from one shoulder to the other in a frame, 170 degrees,
        // the neck 51 and the eyes 15 cm (the pops record of 2026-11-25 05:10, the animation expert's review).
        if (neckLimit > 0f && guard > 0f && chest.sqrMagnitude > 1e-6f)
        {
            float turn = Vector3.SignedAngle(chest, ahead, Vector3.up);
            float allowed = Mathf.Clamp(turn, -neckLimit, neckLimit);
            if (guardKnown)
            {
                if (Mathf.Abs(guardTurn) >= neckLimit - 1f && guardTurn * turn < 0f && Mathf.Abs(turn) > 180f - GuardSideSlack)
                    allowed = guardTurn;
                allowed = Mathf.MoveTowards(guardTurn, allowed, Mathf.Abs(Mathf.DeltaAngle(guardWas, turn)) + GuardSwapRate * dt);
            }
            guardTurn = allowed;
            guardWas = turn;
            guardKnown = true;
            float fix = Mathf.DeltaAngle(turn, allowed);
            if (Mathf.Abs(fix) > .01f)
            {
                gaze = Quaternion.AngleAxis(fix * guard, Vector3.up) * gaze;
                held = true;
            }
        }
        else guardKnown = false;
        Quaternion target = gaze * Quaternion.Inverse(headToGaze);
        if (actionWeight > 0f)
        {
            // An action clip's head (a climb's, a vault's, a landing's) keeps its own nods and turns as far as they stay within
            // a cone round the view (Body.actionHeadCone), and past it follows the view, no further than a neck bends from the
            // chest (Body.neckPitchLimits, Body.neckTurnLimit). Until 0.9.380 the clip kept the head whatever the view did:
            // looking up from a mantle or a wall climb, the camera on the eyes turned 60 to 90 degrees up inside a head still
            // looking at the wall, and saw the inside of its own hat and ears (the user, 2026-11-24: "벽 mantling 하거나 오를
            // 때 위에 보면 모자랑 동물 귀까지 뚫려 보이는데").
            Quaternion clip = head.rotation;
            Quaternion follows = Toward(clip, target, cone * actionWeight, 1f - actionWeight);
            Quaternion bent = WithinNeck(follows, clip, neckLimit, out bool stopped);
            target = Quaternion.Slerp(follows, bent, actionWeight);
            held |= stopped;
        }
        target = Quaternion.Slerp(head.rotation, target, faceWeight);
        if (neck != null)
            neck.rotation = Quaternion.Slerp(Quaternion.identity, target * Quaternion.Inverse(head.rotation), .3f) * neck.rotation;
        head.rotation = target;
        return held;
    }

    // The clip's head turned toward the target's: its look to within `allowed` degrees of the target's look, then its tilt
    // about that look by `tiltShare` of what lies between them, each by its own angle, so a head tumbling far round from the
    // view turns toward it the same way frame after frame. Until 0.9.398 the whole rotation went the shortest way from the
    // clip's to the target's, and that way turned round to the other side as the two passed 180 degrees apart: as a roll came
    // up (RollToRun_F at 0.86 s, the head 71 degrees down, 101 round and tilted 80), the head and its hair swung 30 degrees
    // for two frames and back in every play (the gait record of 2026-11-24 19:5x; the user: "랜딩 롤 이후 … 부자연스럽게 터지는
    // 느낌").
    private Quaternion Toward(Quaternion clip, Quaternion target, float allowed, float tiltShare)
    {
        Quaternion from = clip * headToGaze, to = target * headToGaze;
        Vector3 look = from * Vector3.forward, want = to * Vector3.forward;
        float apart = Vector3.Angle(look, want);
        Vector3 now = apart > allowed ? Vector3.RotateTowards(look, want, (apart - allowed) * Mathf.Deg2Rad, 0f) : look;
        Quaternion gaze = Quaternion.FromToRotation(look, now) * from;
        if (tiltShare > 0f)
        {
            Vector3 up = Vector3.ProjectOnPlane(gaze * Vector3.up, now), wantUp = Vector3.ProjectOnPlane(to * Vector3.up, now);
            if (up.sqrMagnitude > 1e-6f && wantUp.sqrMagnitude > 1e-6f)
                gaze = Quaternion.AngleAxis(Vector3.SignedAngle(up, wantUp, now) * tiltShare, now) * gaze;
        }
        return gaze * Quaternion.Inverse(headToGaze);
    }

    // The head's rotation held within the neck's bend from the chest (pitch neckPitch, sideways yawLimit, from the way a head
    // looks off the chest with the neck straight), or as far as the clip itself bends it, if further: the limit stops the
    // following of the view, never the clip's own pose. True when it had to stop.
    // The pitch is read all the way round over the chest's right and the sideways turn off that plane, so a view up past
    // the top (a ledge climb's chest bent over the edge, the view straight up: 100 degrees and more off the chest) has a
    // steady side. Read as a turn round the chest's up and then a pitch, as in 0.9.380, the turn of a way pointing up and
    // back went from +180 to -180 with every hair of the mouse, the limit put it at +85 or -85 in turn, and the head and
    // its hat swung left and right (the user, 2026-11-24: "맨 위를 쳐다보고 있으면 모자가 왼쪽 오른쪽 왼쪽 오른쪽 움직여요").
    private Quaternion WithinNeck(Quaternion rotation, Quaternion clip, float yawLimit, out bool stopped)
    {
        stopped = false;
        if (chest == null) return rotation;
        Quaternion frame = chest.rotation * chestToGaze, back = Quaternion.Inverse(frame);
        Vector3 ahead = back * (rotation * headToGaze * Vector3.forward), own = back * (clip * headToGaze * Vector3.forward);
        Vector2 at = Angles(ahead), clipAt = Angles(own);
        Vector2 held = new Vector2(
            Mathf.Clamp(at.x, Mathf.Min(-neckPitch.x, clipAt.x), Mathf.Max(neckPitch.y, clipAt.x)),
            yawLimit > 0f ? Mathf.Clamp(at.y, Mathf.Min(-yawLimit, clipAt.y), Mathf.Max(yawLimit, clipAt.y)) : at.y);
        if ((held - at).sqrMagnitude < 1e-4f) return rotation;
        stopped = true;
        float pitch = held.x * Mathf.Deg2Rad, side = held.y * Mathf.Deg2Rad;
        Vector3 to = new Vector3(Mathf.Sin(side), -Mathf.Cos(side) * Mathf.Sin(pitch), Mathf.Cos(side) * Mathf.Cos(pitch));
        return frame * Quaternion.FromToRotation(ahead, to) * back * rotation;

        // A direction's pitch over the right (down positive, all the way round) and how far it leans off that to the right,
        // degrees.
        static Vector2 Angles(Vector3 way) =>
            new Vector2(Mathf.Atan2(-way.y, way.z), Mathf.Asin(Mathf.Clamp(way.x / Mathf.Max(1e-6f, way.magnitude), -1f, 1f))) * Mathf.Rad2Deg;
    }
}
