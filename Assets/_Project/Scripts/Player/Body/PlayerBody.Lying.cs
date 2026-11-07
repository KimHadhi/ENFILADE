using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Lying on the ground or diving (a front dive, the belly slide after it, prone, and lying on the back) the body keeps
/// its own heading in the world instead of facing the view. It used to face the view, so a dive or slide going any other
/// way had the body swim through the air sideways, and anything that turned it away (the side dive, backlog 135) had to
/// turn it back once down.
/// lyingYaw is the way the head points. Face down the body faces that way; on the back it faces its feet, the other way.
/// - Diving, the head leads the dive (MovementDive set DiveBodyForward).
/// - Belly sliding, it leads the slide (FaceSlide does this for the feet-first slide).
/// - Prone, it stays put while the player lies still and looks around; crawling turns it toward the crawl (backwards
///   crawling keeps it, feet first).
/// - Lying prone or on the back, the view stops lyingLookLimit either side of the way the body faces (PlayerLook), and
///   the mouse pushed on past that rolls the player over (PlayerMovement.TryRollOver), the head staying where it is.
/// Starting to lie from upright, it starts from where the body faced, so nothing snaps.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Range(30f, 180f), Tooltip("Lying down, how far the view turns either side of the way the body faces (degrees). Pushing on past it rolls the player over.")]
    private float lyingLookLimit = 110f;
    [SerializeField, Min(10f), Tooltip("Crawling, how fast the body turns toward the crawl (degrees/s).")]
    private float crawlTurnRate = 240f;
    private float lyingYaw;

    /// <summary>Lying prone or on the back: the view's limit either side of the way the body faces.</summary>
    public float LyingLookLimit => lyingLookLimit;
    /// <summary>The body lies prone or on its back this frame, so LyingFacingYaw holds (it may lag the movement a frame).</summary>
    public bool LyingStill => !dead && (state == MovementState.Proning || state == MovementState.Supine);
    /// <summary>The way the lying body faces: where the head points face down, where the feet point on the back.</summary>
    public float LyingFacingYaw => state == MovementState.Supine ? lyingYaw + 180f : lyingYaw;

    private static bool LiesFaceDown(MovementState s) =>
        s == MovementState.Diving || s == MovementState.HeadFirstSliding || s == MovementState.Proning;
    private static bool Lies(MovementState s) => LiesFaceDown(s) || s == MovementState.Supine;

    private void StartLying(MovementState from, MovementState to)
    {
        // Already lying: it carries on from its own heading (rolling over keeps the head where it is). From anything
        // else, from where the body faced: face down the head points that way, on the back the feet do.
        if (!Lies(to) || Lies(from)) return;
        lyingYaw = to == MovementState.Supine ? legYaw + 180f : legYaw;
    }

    private void FaceLying(float dt)
    {
        if (dead) return;
        Vector3 want = Vector3.zero;
        float rate = FacingTurnRate;
        if (state == MovementState.Diving) want = player.DiveBodyForward;
        else if (state == MovementState.HeadFirstSliding) want = player.SlideDirection;
        else if (state == MovementState.Proning)
        {
            Vector3 crawl = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
            if (player.CurrentInput.sqrMagnitude > .01f && crawl.sqrMagnitude > .04f)
            {
                Vector3 heading = Quaternion.Euler(0f, lyingYaw, 0f) * Vector3.forward;
                // Crawling back the way the feet point keeps the heading; any other way turns the head toward it.
                want = Vector3.Dot(crawl.normalized, heading) < -.5f ? -crawl : crawl;
                rate = crawlTurnRate;
            }
        }
        want = Vector3.ProjectOnPlane(want, Vector3.up);
        if (want.sqrMagnitude > .0001f)
            lyingYaw = Mathf.MoveTowardsAngle(lyingYaw, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg, rate * dt);
        float view = transform.eulerAngles.y, facing = LyingFacingYaw, off = Mathf.DeltaAngle(facing, view);
        // PlayerLook keeps the view within the limit lying down; in a dive or slide, or after a blow turned the view
        // (CombatHeadMotion), the view drags the body round rather than twist the neck past it.
        if (Mathf.Abs(off) > lyingLookLimit + 1f)
        {
            facing = view - Mathf.Sign(off) * lyingLookLimit;
            lyingYaw = state == MovementState.Supine ? facing - 180f : facing;
        }
        legYaw = facing;
        targetYaw = Mathf.DeltaAngle(view, facing);
        legsPlanted = true;
        turning = 0;
    }
}
