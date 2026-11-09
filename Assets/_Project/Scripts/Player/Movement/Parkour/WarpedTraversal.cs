using UnityEngine;

/// <summary>
/// A vault carried over the obstacle on its own clip's hands, the way KINEMATION Motion Warping warps a move onto its
/// target points, in three phases:
/// - Approach: the feet go from where the move starts to where the clip's hands, at the moment they plant, rest on the top
///   just past the near edge (Ability.vaultPalmInset), at the run's own speed; the clip's rate is fitted to that, within
///   Ability.vaultWarpRates, as Motion Warping fits a phase's play rate to its distance.
/// - Support: the hands stay on that spot and the body goes over them as the clip carries it; the feet are worked out
///   from the hands each frame, so the hands neither slide nor float.
/// - Exit: from where the hands leave the top to Beatrun's end point beyond the obstacle, at Beatrun's exit speed.
/// The clip plays at the same time (ClipTime, PlayerBody drives it), so clip, hands and capsule stay together. Moved by
/// position as StraightLineTraversal is, through the vaulted obstacle only, so the motor's collisions can't snag it.
/// Not Beatrun's straight line: its quick vaults lift the body too high for our characters' arms to reach the top, and
/// the hands waved in the air (user, 2026-11-07: "Motion Warping으로"). Any clip that puts its hands down works the same,
/// since the body measures them from the clip (IObstacleMotion).
/// </summary>
public sealed class WarpedTraversal
{
    private readonly PlayerMovement ctx;
    private readonly ObstacleTarget target;
    private readonly ParkourDetector detector;
    private readonly IObstacleMotion motion;
    private readonly Vector3 start, contact;
    private readonly float rate, exitSpeed, estimatedTime, lift, floor;
    private Vector3 releaseFeet, lastClear;
    private float clipTime, elapsed, exitClock, exitTime;
    private bool exiting;

    /// <summary>The move is over: the capsule has reached Beatrun's end point.</summary>
    public bool Complete { get; private set; }
    /// <summary>The clip's time (s) the body plays at.</summary>
    public float ClipTime => clipTime;
    /// <summary>Share of the move done (0-1).</summary>
    public float Progress => Complete ? 1f : Mathf.Clamp01(elapsed / Mathf.Max(.01f, estimatedTime));

    private WarpedTraversal(PlayerMovement ctx, in ObstacleTarget target, IObstacleMotion motion, Vector3 contact, float rate,
        float exitSpeed, float estimatedTime, float lift, float floor)
    {
        this.ctx = ctx;
        this.target = target;
        this.motion = motion;
        this.contact = contact;
        this.rate = rate;
        this.exitSpeed = exitSpeed;
        this.estimatedTime = estimatedTime;
        this.lift = lift;
        this.floor = floor;
        detector = ctx.Abilities.Traversal.Detector;
        start = lastClear = ctx.transform.position;
    }

    /// <summary>
    /// A warp for <paramref name="target"/> on <paramref name="motion"/>'s hands, or null when the hands' place on the top
    /// can't be found or the move would make no sense (behind the player, too far, too fast); the straight move takes over.
    /// </summary>
    internal static WarpedTraversal TryStart(PlayerMovement ctx, in ObstacleTarget target, IObstacleMotion motion, float entrySpeed)
        => TryStart(ctx, target, motion, entrySpeed, 0f, null);

    /// <summary>
    /// As above; with <paramref name="clipDuration"/> above 0 the clip plays over that long whatever the run's speed, and
    /// the exit goes at the pace that reaches the end as the clip does. For a climb that starts standing still (climbing
    /// off a ladder's top), where there is no run to fit the clip's rate to. With <paramref name="grip"/>, the hands (their
    /// middle, the wrists) hold there instead of on the obstacle's top past its near edge (a ladder's rails).
    /// </summary>
    internal static WarpedTraversal TryStart(PlayerMovement ctx, in ObstacleTarget target, IObstacleMotion motion, float entrySpeed,
        float clipDuration, Vector3? grip)
    {
        // No clip hands or obstacle, or the obstacle's near edge wasn't found.
        if (motion == null || target.Surface == null) return null;
        if (grip == null && target.EdgeNormal == Vector3.zero) return null;
        var settings = ctx.AbilitySettings;
        float plant = motion.PlantTime, release = motion.ReleaseTime;
        // The plant and the release make no support.
        if (plant <= 0f || release <= plant) return null;

        Vector3 hands = motion.HandsFromFeet(plant), feet = ctx.transform.position;
        Vector3 spot;
        if (grip.HasValue) spot = grip.Value;
        else
        {
            // Where the hands go: on the top just past the near edge, as far along it as the clip has them from the body's middle.
            Vector3 inward = Vector3.ProjectOnPlane(-target.EdgeNormal, Vector3.up).normalized;
            Vector3 along = Vector3.Cross(Vector3.up, inward);
            spot = target.Edge + along * Vector3.Dot(hands, along) + inward * settings.vaultPalmInset;
            // Already closer than the clip reaches ahead: the hands go further onto the top instead of the feet going back.
            float short_ = Vector3.Dot(hands, inward) + LeastApproach - Vector3.Dot(spot - feet, inward);
            if (short_ > 0f) spot += inward * short_;
            // No top under the hands' place.
            if (!target.Surface.Raycast(new Ray(spot + Vector3.up * .5f, Vector3.down), out RaycastHit top, 1f) || top.point.y < target.Edge.y - TopTolerance)
                return null;
            // The hand bone is the wrist: it rests a palm's depth above the top, as PlayerBodyHands puts a hand down.
            spot.y = top.point.y + motion.WristAboveTop;
        }

        // The feet never go below the floor they leave from: what the clip's hands would reach under that is left to the
        // hand IK (PlayerBodyHands), which is then within reach. Below the floor the move hit it and stopped at once.
        float floor = FloorUnder(ctx, feet);
        float lift = Mathf.Max(0f, floor - (spot.y - hands.y));
        Vector3 plantFeet = spot - hands + Vector3.up * lift;
        Vector3 approach = plantFeet - feet;
        if (approach.magnitude > settings.vaultWarpReach) return null;

        // Motion Warping's rate for the phase: the clip's time to the plant over the time the run takes to get there.
        float speed = Mathf.Max(entrySpeed, settings.vaultWarpLeastSpeed);
        float rate = Mathf.Clamp(plant * speed / Mathf.Max(.01f, approach.magnitude), settings.vaultWarpRates.x, settings.vaultWarpRates.y);
        float unit = ctx.HeightUnit, exit = VaultMotion.ExitSpeed(target.Kind, entrySpeed / unit) * unit;
        if (exit <= .1f) exit = speed;
        Vector3 releaseGuess = spot - motion.HandsFromFeet(release) + Vector3.up * lift;
        if (clipDuration > 0f)
        {
            // At the clip's own pace: all of it over clipDuration, and the feet reach the end as the clip ends.
            rate = motion.Length / clipDuration;
            float afterRelease = Mathf.Max(.05f, (motion.Length - release) / rate);
            exit = Mathf.Max(.1f, Vector3.Distance(releaseGuess, target.End) / afterRelease);
        }
        float estimated = release / rate + Vector3.Distance(releaseGuess, target.End) / exit;
        return new WarpedTraversal(ctx, target, motion, spot, rate, exit, estimated, lift, floor);
    }

    // The feet's least way to the plant (m), and how far below the edge a found top may be (m).
    private const float LeastApproach = .05f, TopTolerance = .08f;

    // The floor under the feet as the move starts; far below or none, nothing holds them up.
    private static float FloorUnder(PlayerMovement ctx, Vector3 feet)
        => Physics.Raycast(feet + Vector3.up * .1f, Vector3.down, out RaycastHit floor, 3f, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            ? floor.point.y : float.NegativeInfinity;

    // Where the feet are when the hands are on the spot at this clip time, never below the floor they left.
    private Vector3 FeetFor(float time)
    {
        Vector3 feet = contact - motion.HandsFromFeet(time) + Vector3.up * lift;
        feet.y = Mathf.Max(feet.y, floor);
        return feet;
    }

    // Climbing off a ladder's top the feet start further from the floor's edge than the clip's hands reach ahead of them
    // (measured 0.46 m against 0.23 m), so landing the hands on the edge pulled the feet toward the wall while still below
    // the floor, and the body went into it (user, 2026-11-08). Below the floor's height the feet keep no closer to the
    // wall than where they started; the hands keep hold of the ladder's rails (PlayerBodyHands.ExitRailPoint).
    private float Inward(Vector3 point) => Vector3.Dot(point, -target.ExitLadder.Outward);

    /// <summary>Climbing off a ladder: how far short of the floor's edge the clip's hands are kept, along the way in (m).</summary>
    internal float EdgeShortfall { get; private set; }

    // As close to the wall as the capsule can stand while below the floor: its side just off the floor's edge, which
    // (the ladder standing off the wall) is further in than where the climb began. Keeping the feet back at the start
    // held the body back while the clip went forward, and the arms trailed behind (user, 2026-11-08).
    private float WallLimit => Mathf.Max(Inward(start), Inward(target.Edge) - ctx.Motor.controller.radius - WallGap);
    private const float WallGap = .02f;

    private Vector3 OutOfWall(Vector3 feet)
    {
        float most = WallLimit, now = Inward(feet);
        EdgeShortfall = 0f;
        if (feet.y >= target.Edge.y - .02f || now <= most) return feet;
        EdgeShortfall = now - most;
        return feet + target.ExitLadder.Outward * EdgeShortfall;
    }

    // After the hands let go: up and in together, the rise early (easing out) and the way in eased through, kept off
    // the wall while the feet are still below the floor, so it rounds the edge's corner instead of cutting it or going
    // straight up first while the clip already steps on.
    private Vector3 UpThenIn(float share)
    {
        Vector3 from = releaseFeet, to = target.End;
        float height = to.y - from.y;
        // The share at which the rise (easing out) brings the feet up to the edge; until then the way in stops at the wall.
        float atEdge = height > 1e-4f ? Mathf.Clamp01((target.Edge.y - .02f - from.y) / height) : 0f;
        float edgeShare = 1f - Mathf.Sqrt(1f - atEdge);
        Vector3 at = share <= edgeShare ? HeldOff(Vector3.Lerp(from, to, Smooth(share)))
            // Over the edge: on in from where the wall held it, to the end.
            : Vector3.Lerp(HeldOff(Vector3.Lerp(from, to, Smooth(edgeShare))), to, Smooth((share - edgeShare) / Mathf.Max(1e-4f, 1f - edgeShare)));
        at.y = from.y + height * (1f - (1f - share) * (1f - share));
        return at;

        static float Smooth(float t) => t * t * (3f - 2f * t);
        Vector3 HeldOff(Vector3 point)
        {
            float over = Inward(point) - WallLimit;
            return over > 0f ? point + target.ExitLadder.Outward * over : point;
        }
    }

    /// <summary>The clip time span the hands are down (support), for the hands' reach onto the edge.</summary>
    internal float PlantTime => motion.PlantTime;
    internal float ReleaseTime => motion.ReleaseTime;

    internal bool Advance(float dt)
    {
        // The obstacle went.
        if (target.Surface == null || !target.Surface.enabled || !target.Surface.gameObject.activeInHierarchy) return false;
        dt = Mathf.Max(0f, dt);
        elapsed += dt;
        Vector3 position;
        if (!exiting)
        {
            clipTime = Mathf.Min(clipTime + dt * rate, motion.ReleaseTime);
            float plant = motion.PlantTime;
            // The plant's feet follow the body as it turns with the view (PlayerBody.VaultLook), so they are found each frame.
            position = clipTime < plant ? Vector3.Lerp(start, FeetFor(plant), clipTime / plant) : FeetFor(clipTime);
            if (target.ExitLadder != null) position = OutOfWall(position);
            if (clipTime >= motion.ReleaseTime)
            {
                exiting = true;
                releaseFeet = position;
                exitTime = Mathf.Max(.02f, Vector3.Distance(releaseFeet, target.End) / exitSpeed);
            }
        }
        else
        {
            exitClock += dt;
            clipTime = Mathf.Min(clipTime + dt * rate, motion.Length);
            float share = Mathf.Clamp01(exitClock / exitTime);
            position = target.ExitLadder != null ? UpThenIn(share) : Vector3.Lerp(releaseFeet, target.End, share);
            Complete = exitClock >= exitTime;
            // Let go: up first, then in (UpThenIn), so nothing is kept short any more.
            EdgeShortfall = 0f;
        }

        // Blocked on the way, or the end isn't clear.
        if (!detector.ClearStraightPath(ctx.transform.position, position, target.Surface)) return false;
        if (Complete && !detector.ClearStraightExit(position)) return false;
        if (detector.ClearStraightExit(position)) lastClear = position;
        Vector3 velocity = (position - ctx.transform.position) / Mathf.Max(dt, .001f);
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        ctx.VerticalVelocity = velocity.y;
        ctx.Motor.RequestTraversalPosition(position);
        return true;
    }

    internal void Exit()
    {
        // Cut short inside the obstacle (Ctrl, death, a new obstruction): back to the last free place first.
        if (!detector.ClearStraightExit(ctx.transform.position))
            ctx.Motor.RecoverTraversalPosition(lastClear);
    }
}
