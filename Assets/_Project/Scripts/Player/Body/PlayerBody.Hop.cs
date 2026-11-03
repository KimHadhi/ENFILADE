using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Jumping again the moment the feet touch: each touchdown still shows for a moment as the knees take the landing, and
/// every jump starts its clip again, in step with the flight. Both follow the Landed and Jumped events rather than the
/// ground flag: the feet are down for a frame or two, and a copy on another PC can miss that between two poses.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("The knees taking a light landing, shown when the next jump leaves at once.")]
    private string touchdown = "land";
    [SerializeField, Min(0f), Tooltip("How long a touchdown shows once the next jump has left the ground (s).")]
    private float touchdownSeconds = .12f;
    private float touchdownLeft;
    private bool jumpQueued, jumpPending;

    private void BindHops()
    {
        AddChannel(touchdown, 0f);
        touchdownLeft = 0f;
        jumpQueued = jumpPending = false;
        player.Landed -= OnHopLanded;
        player.Jumped -= OnHopJumped;
        player.Landed += OnHopLanded;
        player.Jumped += OnHopJumped;
    }

    private void UnbindHops()
    {
        if (player == null) return;
        player.Landed -= OnHopLanded;
        player.Jumped -= OnHopJumped;
    }

    private void OnHopLanded(float impact)
    {
        if (player.LastLandingTier != MovementLanding.Tier.Light) return;
        var channel = Find(touchdown);
        if (channel == null || channel.Synced) return;
        Restart(channel);
        touchdownLeft = touchdownSeconds;
    }

    private void OnHopJumped() => jumpQueued = true;

    /// <summary>In the air this soon after a touchdown, the body still shows it.</summary>
    private bool ShowsTouchdown => touchdownLeft > 0f && Find(touchdown) != null;

    // After Touch and airTime: a jump starts its clip once the touchdown has had its moment, at the time the flight
    // has reached, so a clip already playing never snaps back to its first frame at full weight.
    private void TickHops(float dt)
    {
        touchdownLeft = Mathf.Max(0f, touchdownLeft - dt);
        if (jumpQueued)
        {
            jumpQueued = false;
            jumped = jumpPending = true;
            airTime = 0f;
            jumpClip = JumpFor(HorizontalSpeed());
        }
        if (!jumpPending || ShowsTouchdown) return;
        jumpPending = false;
        var channel = Find(jumpClip);
        if (channel == null || channel.Synced) return;
        Restart(channel);
        channel.Playable.SetTime(Mathf.Min(airTime, channel.Length));
    }
}
