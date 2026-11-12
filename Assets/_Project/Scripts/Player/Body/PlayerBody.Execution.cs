using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// A paired execution or takedown on the player's body (ExecutionPairPlayer, Tables/Executions.csv): the pair's player
/// clip on the top layer, the whole body (no mask), over the movement clips and every other layer. The body's root
/// leaves the capsule for the pair's place beside the zombie (the root the two clips share, put there on every PC),
/// moving onto it over the pair's align time, and comes back under the capsule as the clip eases out. While it plays the
/// clip carries the head and the feet: the head no longer follows the camera's pivot (FollowHead), the look bend and the
/// feet's planting step back, and the gaze takes the clip's head as an action clip's (BodyGaze). The clip's time is the
/// pair's clock (ExecutionPairPlayer sets it), so it stays with the zombie's.
/// </summary>
public sealed partial class PlayerBody
{
    private const int ExecutionLayer = 6;

    private AnimationClipPlayable pairedClip;
    private bool paired;
    private float pairedWeight, pairedPlace, pairedTime, pairedLength, pairedIn = .12f, pairedOut = .25f, pairedAlign = .2f, pairedAge;
    private Vector3 pairedRoot, pairedFromPosition;
    private Quaternion pairedTurn = Quaternion.identity, pairedFromRotation = Quaternion.identity;

    /// <summary>A pair's clip has (or still eases out of) this body.</summary>
    internal bool PlaysPaired => paired || pairedWeight > 0f;

    // From BuildLayers, once the layer mixer exists: the input waits empty until a pair plays.
    private void BuildExecution() => layers.SetInputWeight(ExecutionLayer, 0f);

    private void TeardownExecution()
    {
        pairedClip = default;
        paired = false;
        pairedWeight = pairedPlace = 0f;
    }

    /// <summary>
    /// The pair's player clip takes the body: over blendIn, its root moving from where the body stands now onto
    /// <paramref name="root"/>, turned <paramref name="turn"/>, over align. Its time is the pair's (SetPairedTime).
    /// False when the body can't play it (no graph yet, no clip).
    /// </summary>
    internal bool BeginPaired(AnimationClip clip, Vector3 root, Quaternion turn, float length, float blendIn, float blendOut, float align)
    {
        if (clip == null || body == null || !graph.IsValid() || !layers.IsValid() || layers.GetInputCount() <= ExecutionLayer) return false;
        if (pairedClip.IsValid())
        {
            graph.Disconnect(layers, ExecutionLayer);
            pairedClip.Destroy();
        }
        pairedClip = ClipPlayable(clip);
        pairedClip.SetSpeed(0d);
        graph.Connect(pairedClip, 0, layers, ExecutionLayer);
        paired = true;
        pairedRoot = root;
        pairedTurn = turn;
        pairedLength = Mathf.Max(.05f, Mathf.Min(length, clip.length));
        pairedIn = Mathf.Max(.01f, blendIn);
        pairedOut = Mathf.Max(.01f, blendOut);
        pairedAlign = Mathf.Max(.01f, align);
        pairedTime = pairedAge = 0f;
        pairedFromPosition = body.position;
        pairedFromRotation = body.rotation;
        return true;
    }

    /// <summary>The pair's clock, set each frame by ExecutionPairPlayer on every PC.</summary>
    internal void SetPairedTime(float time) => pairedTime = time;

    /// <summary>The pair is over or was cut short: the body eases back into its own clips and under its capsule.</summary>
    internal void EndPaired()
    {
        if (!paired) return;
        paired = false;
        // The body's own placing carries on from where the pair left it, so nothing jumps: its offset under the capsule
        // and its turn from the view, which FollowHead and the legs' turn then ease back as they always do.
        if (body == null) return;
        headAlignment = Vector3.ClampMagnitude(transform.InverseTransformPoint(body.position), 1.5f);
        yaw = Mathf.DeltaAngle(transform.eulerAngles.y, body.eulerAngles.y);
        yawVelocity = 0f;
        legYaw = body.eulerAngles.y;
    }

    // From BlendLayers: the layer's share eases in at the start and out at the end; the clip at the pair's time.
    private void BlendExecution(float dt)
    {
        if (!pairedClip.IsValid()) return;
        pairedAge += dt;
        pairedWeight = Mathf.MoveTowards(pairedWeight, paired ? 1f : 0f, dt / (paired ? pairedIn : pairedOut));
        layers.SetInputWeight(ExecutionLayer, pairedWeight);
        pairedClip.SetTime(Mathf.Clamp(pairedTime, 0f, pairedLength));
        if (paired || pairedWeight > 0f) return;
        graph.Disconnect(layers, ExecutionLayer);
        pairedClip.Destroy();
        pairedClip = default;
    }

    // From Update after PlaceBody: while the pair plays the body's root stands on the pair's place, moved onto it over
    // align; easing out it goes back under the capsule with the layer.
    private void PlacePaired()
    {
        if (body == null || !pairedClip.IsValid()) return;
        if (paired)
        {
            float t = Mathf.Clamp01(pairedAge / pairedAlign);
            pairedPlace = t * t * (3f - 2f * t);
            // The root stands as the capsule's does: its soles on the floor (StandOnSoles).
            Vector3 at = pairedRoot - transform.up * soleLift;
            body.SetPositionAndRotation(Vector3.Lerp(pairedFromPosition, at, pairedPlace),
                Quaternion.Slerp(pairedFromRotation, pairedTurn, pairedPlace));
            // The legs face the pair's way meanwhile, so a turn of the view moves nothing.
            legYaw = body.eulerAngles.y;
            yaw = targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, legYaw);
            yawVelocity = 0f;
        }
    }
}
