using UnityEngine;

/// <summary>
/// How far a body can come down before a foot or its toes comes within a clearance of what is under it: now, and over the
/// next moment along the way the foot is going across. What is under a foot is found straight down from above it, and from
/// above the obstacle's top while one is being crossed, so a foot already sunk into the obstacle still finds its top, and a
/// foot past its far side finds the floor there. Only the way across is read ahead: a foot coming down to land is meant to
/// reach the floor (reading its fall ahead lifted the body before every landing and dropped it on touchdown, 2026-11-07).
/// The feet are read before the body is moved this frame, as the clip and the capsule put them. Any humanoid; toes may be
/// missing.
/// </summary>
internal sealed class FeetClearance
{
    // From this far above each foot, or above the top being crossed (m), down to this far below the foot, what is under it
    // is looked for.
    private const float Above = .5f, Below = 2.5f;
    // Points read along the way ahead, the last at the full time ahead.
    private const int AheadSteps = 2;
    // Shared, since the bodies are placed one at a time on the main thread.
    private static readonly RaycastHit[] Hits = new RaycastHit[8];
    private readonly Transform owner;
    private readonly Transform[] feet = new Transform[4];
    private readonly Vector3[] read = new Vector3[4];
    private int readFrame = -1000;

    internal FeetClearance(Animator animator, Transform owner)
    {
        this.owner = owner;
        if (animator == null || !animator.isHuman) return;
        feet[0] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        feet[1] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        feet[2] = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        feet[3] = animator.GetBoneTransform(HumanBodyBones.RightToes);
    }

    /// <summary>
    /// The room (m, never negative, infinite with nothing under the feet) before any foot is within
    /// <paramref name="clear"/> of what is under it: <paramref name="now"/>, and at the least over the next
    /// <paramref name="ahead"/> seconds as the feet go on across (<paramref name="soon"/>, never more than now).
    /// <paramref name="top"/> is the height of an obstacle being crossed (negative infinity for none).
    /// </summary>
    internal void Measure(LayerMask ground, Vector3 fallback, float top, float clear, float ahead, float dt, out float now, out float soon)
    {
        now = soon = float.PositiveInfinity;
        bool known = readFrame == Time.frameCount - 1 && dt > 0f;
        for (int i = 0; i < feet.Length; i++)
        {
            var foot = feet[i];
            if (foot == null) continue;
            Vector3 at = foot.position, moving = known ? (at - read[i]) / dt : fallback;
            read[i] = at;
            Vector3 across = Vector3.ProjectOnPlane(moving, Vector3.up) * ahead;
            float room = Room(at);
            now = Mathf.Min(now, room);
            for (int step = 1; step <= AheadSteps; step++) room = Mathf.Min(room, Room(at + across * step / AheadSteps));
            soon = Mathf.Min(soon, room);
        }
        readFrame = Time.frameCount;

        float Room(Vector3 point) => Mathf.Max(0f, point.y - Under(point) - clear);

        // The highest thing straight under a point that isn't the body's own, negative infinity for nothing.
        float Under(Vector3 point)
        {
            float from = Mathf.Max(point.y, top) + Above;
            int count = Physics.RaycastNonAlloc(new Vector3(point.x, from, point.z), Vector3.down, Hits, from - (point.y - Below), ground,
                QueryTriggerInteraction.Ignore);
            float under = float.NegativeInfinity;
            for (int h = 0; h < count; h++)
                if (owner == null || !Hits[h].collider.transform.IsChildOf(owner)) under = Mathf.Max(under, Hits[h].point.y);
            return under;
        }
    }
}
