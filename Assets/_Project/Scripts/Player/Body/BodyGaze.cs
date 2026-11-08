using UnityEngine;

/// <summary>
/// Where the body looks:
/// - the mouse leads the view at once; selected action clips (a vault, a climb, a roll; PlayerBody picks them) add a
///   bounded share of their head's turn on top, smoothed on its own, less the more the player aims (Update);
/// - in every state the head turns to look where the view looks, as far as a committed action's clip doesn't carry it, as
///   it did up to 0.7.34: with the look bend only upright or sliding, a vault, hang, roll or rung left the head where the
///   clip had it, away from the aim in the F5 view and on friends' screens (user picked it back, 2026-11-08). Q/E tilts it
///   only as far as the view rolls; the neck takes back the spine's tip. While FPS Animation Framework holds a gun the
///   head is its (its Look lines the sight up on the eyes, the gun hangs from the head), so this steps back (FaceHead).
/// The camera takes Rotation, with the camera effects' turn on top of Input (BodyCamera).
/// A plain class PlayerBody owns (stage 3 of the body restructure, 2026-11-09). The numbers stay PlayerBody's table-bound
/// fields (Player.csv Body.animationViewStrength, Body.animationViewLimits, Body.animationViewTime), handed over by Tune.
/// </summary>
internal sealed class BodyGaze
{
    private Transform root, lookPivot, model, head, neck;
    private Quaternion headToGaze = Quaternion.identity, rotation = Quaternion.identity, actionTurn = Quaternion.identity;
    private float actionWeight, faceWeight;
    private float strength = .65f, time = .1f;
    private Vector3 limits = new Vector3(18f, 12f, 10f);

    /// <summary>Where the view looks this frame: the mouse's look with the action clips' share on top.</summary>
    internal Quaternion Rotation => rotation;

    /// <summary>Where the mouse alone has the view.</summary>
    internal Quaternion Input => lookPivot != null ? lookPivot.rotation : root.rotation;

    /// <summary>A new body: the head starts on its clip. <paramref name="root"/> is the player, <paramref name="lookPivot"/>
    /// what the mouse turns (null: the player itself).</summary>
    internal void Reset(Transform root, Transform lookPivot)
    {
        this.root = root;
        this.lookPivot = lookPivot;
        faceWeight = 0f;
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
        actionTurn = Quaternion.identity;
        actionWeight = 0f;
        rotation = Input;
    }

    /// <summary>How much the action clips lead the view (0-1), the most they turn it before that (degrees: pitch, yaw, roll),
    /// and how quickly their turn follows (s).</summary>
    internal void Tune(float strength, Vector3 limits, float time)
    {
        this.strength = strength;
        this.limits = limits;
        this.time = time;
    }

    /// <summary>
    /// This frame's gaze. <paramref name="clipWeight"/> is how much of the pose the gaze-leading action clips have (their
    /// actual crossfade, an action playing out after its move included); <paramref name="clipHead"/> the head as the clips
    /// posed it, before the look bent it (SpineLook keeps it), so neither last frame's camera nor its effects feed back.
    /// </summary>
    internal void Update(float clipWeight, float aimBlend, bool dead, Quaternion clipHead, float dt)
    {
        Quaternion input = Input;
        actionWeight = Mathf.Clamp01(clipWeight);
        float weight = dead ? 0f : actionWeight * Mathf.Clamp01(strength) * (1f - aimBlend);

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
        Quaternion target = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(angles), weight);
        actionTurn = Quaternion.Slerp(actionTurn, target, 1f - Mathf.Exp(-dt / Mathf.Max(.01f, time)));
        rotation = input * actionTurn;

        static float Limit(float angle, float limit) => Mathf.Clamp(Mathf.DeltaAngle(0f, angle), -Mathf.Abs(limit), Mathf.Abs(limit));
    }

    /// <summary>
    /// Turns the head (and a little of the neck) to the gaze, tilted by <paramref name="leanRoll"/> (degrees, the view's
    /// Q/E roll), as far as the action clips don't carry it. <paramref name="off"/>: the head isn't this one's (dead, or
    /// FPSAF holds a gun); it hands over and takes it back over <paramref name="blendTime"/> (s).
    /// </summary>
    internal void FaceHead(bool off, float leanRoll, float blendTime, float dt)
    {
        faceWeight = Mathf.MoveTowards(faceWeight, off ? 0f : 1f, dt / blendTime);
        if (faceWeight <= 0f || head == null) return;
        Quaternion gaze = rotation * Quaternion.Euler(0f, 0f, leanRoll);
        Quaternion target = Quaternion.Slerp(gaze * Quaternion.Inverse(headToGaze), head.rotation, actionWeight);
        target = Quaternion.Slerp(head.rotation, target, faceWeight);
        if (neck != null)
            neck.rotation = Quaternion.Slerp(Quaternion.identity, target * Quaternion.Inverse(head.rotation), .3f) * neck.rotation;
        head.rotation = target;
    }
}
