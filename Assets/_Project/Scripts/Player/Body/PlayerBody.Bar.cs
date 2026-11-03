using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// On a swing bar the body hangs from its hands and swings with the pendulum. Beatrun bends its swingstraight pose by the
/// swing's offset; our clip is that straight hang, so the whole body turns about its feet by the swing's angle (the bar's
/// hang length runs from the hands to the feet, so the feet stay where the capsule is), and PlayerBody.Grip moves it the
/// rest of the way for PlayerBodyHands to close each hand round the bar. It faces along the swing the way nearer the view,
/// as Beatrun's SwingbarCheck picks it, and the view turns within barTurnLimit of that (PlayerLook). Let go, it comes
/// upright about its feet over barReleaseTime.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Swing bar")]
    [SerializeField, Range(0f, 85f), Tooltip("On a swing bar, look limit either side of the way the body faces (degrees). Beatrun's swing: 45.")]
    private float barTurnLimit = 45f;
    [SerializeField, Min(.01f), Tooltip("Let go of a swing bar, how long the body takes to come upright again (s).")]
    private float barReleaseTime = .3f;

    private Vector3 barFacing = Vector3.forward, barSwingDirection = Vector3.forward;
    private float barAngle, barWeight;

    public float BarLookYawLimit => Mathf.Clamp(barTurnLimit, 0f, 85f);
    /// <summary>Up the body from its feet toward its hands, as it swings.</summary>
    internal Vector3 BarUp { get; private set; } = Vector3.up;
    /// <summary>The way the chest faces as the body swings.</summary>
    internal Vector3 BarForward { get; private set; } = Vector3.forward;

    private bool OnBar => !dead && (state == MovementState.BarGrab || state == MovementState.BarSwing)
        && player.Abilities.Traversal.Bar != null;

    /// <summary>Along the bar's swing, the way nearer the view.</summary>
    public static Vector3 BarFacingFor(SwingBar bar, Vector3 view)
    {
        Vector3 swing = bar.SwingDirection;
        return Vector3.Dot(view, swing) >= 0f ? swing : -swing;
    }

    private void BarPose(float dt)
    {
        var bar = player.Abilities.Traversal.Bar;
        if (bar != null) barFacing = BarFacingFor(bar, transform.forward);
        Want(barSwing);
        legYaw = Mathf.MoveTowardsAngle(legYaw, Mathf.Atan2(barFacing.x, barFacing.z) * Mathf.Rad2Deg, FacingTurnRate * dt);
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, legYaw);
        legsPlanted = true;
        turning = 0;
    }

    // Before the hands are measured (Grip): the body turns about its feet by the swing's angle.
    private void SwingWithBar(float dt)
    {
        bool on = OnBar;
        if (on)
        {
            var traversal = player.Abilities.Traversal;
            barAngle = traversal.Angle;
            barSwingDirection = traversal.Bar.SwingDirection;
        }
        barWeight = Mathf.MoveTowards(barWeight, on ? 1f : 0f, dt / (on ? blendTime : barReleaseTime));
        // The swing carries the feet along barSwingDirection by this angle (BarSwingState).
        float angle = barAngle * barWeight;
        Quaternion turn = Quaternion.FromToRotation(Vector3.down, Vector3.down * Mathf.Cos(angle) + barSwingDirection * Mathf.Sin(angle));
        BarUp = turn * Vector3.up;
        BarForward = turn * barFacing;
        if (barWeight > 0f) body.rotation = turn * body.rotation;
    }
}
