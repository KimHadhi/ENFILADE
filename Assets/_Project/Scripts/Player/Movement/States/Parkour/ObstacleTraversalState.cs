using UnityEngine;
using System.Collections.Generic;

public sealed class ObstacleTraversalState : MovementBaseState
{
    private readonly bool vault;
    private float blockedTime;
    private float progress, elapsed, duration;
    private bool finished;
    private Vector3 finishPosition;
    private TraversalPath path;
    private Vector3 entryVelocity;
    private StraightLineTraversal straightLine;
    // A vault carried by its clip's hands (KINEMATION Motion Warping style), where the kind is switched on and the clip puts
    // its hands down; otherwise the straight move.
    private WarpedTraversal warp;
    public bool MovesInStraightLine => straightLine != null || warp != null;
    public bool IsVault => vault;
    public int EntrySerial { get; private set; }
    public float EntrySpeed => entryVelocity.magnitude;
    private readonly List<Vector3> framePath = new List<Vector3>();
    // A vault ends in the air, chest high beyond the obstacle (Beatrun's vaultpos). It stays a vault until the feet are
    // down: handed to the fall at once, the move ended abruptly in mid-air, which the user found rushed and choppy
    // (2026-11-06). Ground further below than LandingReach (units) is left to the fall, with its landing and roll, as
    // before; LandingTime (s) bounds the wait.
    private bool landing;
    private float landingLeft;
    private const float LandingReach = 60f, LandingTime = .6f;
    public ObstacleTraversalState(PlayerMovement ctx, MovementBaseState parent, bool vault) : base(ctx, parent) { this.vault = vault; }
    public override string Name => vault ? "Vault" : "Mantle";
    public override MovementPosture Posture => MovesInStraightLine ? MovementPosture.Standing : MovementPosture.Crouching;
    public override bool CanApplyGravity => landing || base.CanApplyGravity;
    /// <summary>
    /// Coming down after the vault. Still a move to the motor, which leaves its ground alone, so the capsule's edge on the
    /// obstacle behind can't pass for a landing; the feet find the floor (FeetDown). MovementLanding measures the fall, so
    /// the landing is heard and felt as any other.
    /// </summary>
    public bool IsLanding => landing;
    public override void Enter()
    {
        blockedTime = progress = elapsed = 0f; finished = landing = false;
        entryVelocity = ctx.HorizontalVelocity;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Abilities.Traversal.Obstacle.Start = ctx.transform.position;
        var target = ctx.Abilities.Traversal.Obstacle;
        ctx.Abilities.Traversal.RecordObstacle();
        ctx.Abilities.Traversal.ObstacleProgress = 0f;
        ctx.Abilities.Traversal.ObstacleClipTime = -1f;
        ctx.Abilities.Traversal.ObstacleSupport = Vector2.zero;
        ctx.Abilities.Traversal.EdgeShortfall = 0f;
        warp =target.ExitLadder != null ? StartLadderWarp(target) : StartWarp(target);
        straightLine = warp == null && target.StraightLine ? new StraightLineTraversal(ctx, target, entryVelocity.magnitude) : null;
        path = target.StraightLine ? null : new TraversalPath(target, ctx.Abilities.Traversal.Detector, ctx.CrouchColliderHeight);
        duration = Mathf.Max(0.05f, TraversalTiming.Duration(ctx.AbilitySettings, target));
        EntrySerial++;
    }
    public override void ProcessInput(Vector2 input)
    {
        if (!ctx.InputAvailable || !ctx.Input.CrouchPressed) return;
        // Coming down after the vault, Ctrl hands over to the fall as it is, for a landing slide or roll.
        if (landing) ctx.Abilities.ReturnToLocomotion();
        else Abort();
    }
    public override void UpdateLogic(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        var target = traversal.Obstacle;
        if (landing)
        {
            landingLeft -= dt;
            if (!FeetDown() && landingLeft > 0f && !RanIntoSomething()) return;
            ctx.Abilities.ReturnToLocomotion();
            return;
        }
        if (target.Surface == null) { Abort(); return; }
        if (straightLine != null || warp != null)
        {
            if (!(straightLine?.Complete ?? warp.Complete)) return;
            // The clip runs on by itself from here, through the landing.
            traversal.ObstacleClipTime = -1f;
            ctx.Abilities.Landing.SupportedByTraversal();
            if (vault && !SpringsOff(target.Kind) && LandsWithin(LandingReach * ctx.HeightUnit))
            {
                Leave(target.Kind);
                landing = true;
                landingLeft = LandingTime;
                return;
            }
            ctx.Abilities.ReturnToLocomotion();
            Leave(target.Kind);
            return;
        }
        if (finished)
        {
            if (Vector3.Distance(ctx.transform.position, finishPosition) > 0.15f) { Abort(); return; }
            ctx.Abilities.Landing.SupportedByTraversal();
            ctx.Abilities.ReturnToLocomotion();
            ctx.HorizontalVelocity = entryVelocity;
            ctx.VerticalVelocity = -2f;
        }
    }
    // Forward is the way the player looks now, so looking round in a vault steers what comes after it (Beatrun). Looking
    // further back than sideways, the vault's own way carries on instead: Beatrun's rule sent the player back into the
    // obstacle just vaulted, which caught on its edge (user, 2026-11-07: free look, no buggy push back).
    private void Leave(VaultKind kind)
    {
        float unit = ctx.HeightUnit, entry = entryVelocity.magnitude / unit;
        Vector3 forward = ctx.transform.forward;
        var target = ctx.Abilities.Traversal.Obstacle;
        Vector3 travel = Vector3.ProjectOnPlane(target.End - target.Start, Vector3.up);
        if (travel.sqrMagnitude > .0001f && Vector3.Dot(forward, travel) < 0f) forward = travel.normalized;
        ctx.HorizontalVelocity = forward * (VaultMotion.ExitSpeed(kind, entry) * unit);
        ctx.VerticalVelocity = 0f;
        if (!ctx.Input.JumpHeld || !VaultMotion.CanSpring(kind)) return;
        ctx.Actions.Jump(VaultMotion.SpringHeight * unit);
        ctx.HorizontalVelocity = forward * (VaultMotion.SpringSpeed(entry) * unit);
    }
    // Holding jump as the move ends springs off it (Leave), a jump of its own that the fall carries.
    private bool SpringsOff(VaultKind kind) => ctx.Input.JumpHeld && VaultMotion.CanSpring(kind);
    // Ground under the feet, further down than a step and within reach: the vault comes down onto it before handing over.
    private bool LandsWithin(float reach)
        => Physics.Raycast(ctx.transform.position + Vector3.up * .05f, Vector3.down, out RaycastHit ground, reach + .05f,
               ctx.groundLayers, QueryTriggerInteraction.Ignore) && ground.distance > .1f;
    // Falling, with walkable ground right under the middle of the feet: an edge the capsule's side touches doesn't count.
    private bool FeetDown()
        => ctx.VerticalVelocity <= 0f && Physics.Raycast(ctx.transform.position + Vector3.up * .05f, Vector3.down,
               out RaycastHit ground, .05f + Mathf.Max(.06f, ctx.Motor.controller.skinWidth * 2f), ctx.groundLayers,
               QueryTriggerInteraction.Ignore) && ground.normal.y >= Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad);
    // The way down met something other than the floor under the feet. Turned round mid-vault, the exit speed (Leave, the
    // way the player looks, as Beatrun's) carries the player back at the obstacle just vaulted, and the capsule's round
    // bottom caught on its top edge: held on as a vault, it hung there in the air until LandingTime ran out (user,
    // 2026-11-07). The fall takes over at once instead, as Beatrun's walking movement does the moment its vault ends.
    // Last frame's move (the motor moves after UpdateLogic).
    private bool RanIntoSomething() => (ctx.Motor.lastMoveFlags & (CollisionFlags.Sides | CollisionFlags.Below)) != 0;
    public override void UpdatePhysics(float dt)
    {
        if (warp != null)
        {
            if (landing) return;
            if (!warp.Advance(dt))
            {
                Abort();
                return;
            }
            ctx.Abilities.Traversal.ObstacleProgress = warp.Progress;
            ctx.Abilities.Traversal.ObstacleClipTime = warp.Complete ? -1f : warp.ClipTime;
            ctx.Abilities.Traversal.ObstacleSupport = new Vector2(warp.PlantTime, warp.ReleaseTime);
            ctx.Abilities.Traversal.EdgeShortfall = warp.EdgeShortfall;
            return;
        }
        if (straightLine != null)
        {
            // Coming down: gravity and the motor carry the exit speed Leave set, with collisions, to the ground.
            if (landing) return;
            if (!straightLine.Advance(dt)) Abort();
            else ctx.Abilities.Traversal.ObstacleProgress = straightLine.Progress;
            return;
        }
        float nextElapsed = elapsed + dt;
        float to = TraversalTiming.Progress(nextElapsed / duration);
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        float from = progress;
        var target = ctx.Abilities.Traversal.Obstacle;
        Vector3 candidate = path.Evaluate(to);
        // Original exit conditions. Keep checking the body before handing control
        // back; unlike MOVETYPE_NOCLIP, this controller must not end inside a wall.
        // Climbing off a ladder's top goes all the way: the body plays the climb off by this move's time, and ending it at
        // EndProgress (about two thirds of the time) stood the player up on the floor while the clip still climbed the
        // ladder on top of them (user, 2026-11-08). It ends with the clip, at the end of the route.
        bool wantsExit = target.ExitLadder == null && (progress >= TraversalTiming.EndProgress
            || (candidate - target.End).sqrMagnitude < TraversalTiming.EndDistanceSquared);
        bool safeExit = Mathf.Abs(candidate.y - target.End.y) < 0.08f
            && ctx.Abilities.Traversal.Detector.ClearCapsule(candidate, ctx.StandingColliderHeight)
            && ctx.Abilities.Traversal.Detector.HasTread(candidate, target.Surface);
        bool complete = wantsExit && safeExit;
        if (to > 0.995f) { to = 1f; complete = true; }
        int slices = Mathf.Max(1, Mathf.CeilToInt((to - from) * path.Length / 0.06f));
        framePath.Clear();
        Vector3 destination = ctx.transform.position;
        for (int i = 1; i <= slices; i++)
        {
            Vector3 next = path.Evaluate(Mathf.Lerp(from, to, i / (float)slices));
            if (!ctx.Abilities.Traversal.Detector.ClearPath(destination, next, ctx.CrouchColliderHeight))
            { blockedTime += dt; if (blockedTime > 0.08f) Abort(); return; }
            framePath.Add(next); destination = next;
        }
        blockedTime = 0f;
        progress = to;
        elapsed = nextElapsed;
        ctx.Abilities.Traversal.ObstacleProgress = Mathf.Clamp01(elapsed / duration);
        finished = complete;
        finishPosition = destination;
        Vector3 velocity = (destination - ctx.transform.position) / Mathf.Max(dt, 0.001f);
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        ctx.VerticalVelocity = velocity.y;
        ctx.Motor.RequestPath(framePath);
    }
    private void Abort()
    {
        // Held W must not immediately turn a blocked vault into a wall action
        // or another automatic grab before the body has recovered.
        if (straightLine != null || warp != null) ctx.Abilities.Traversal.LockLedge();
        ctx.Abilities.ReturnToLocomotion();
        ctx.HorizontalVelocity = entryVelocity;
        ctx.VerticalVelocity = Mathf.Min(ctx.VerticalVelocity, 0f);
    }
    public override void Exit()
    {
        straightLine?.Exit();
        warp?.Exit();
        ctx.Abilities.Traversal.ObstacleClipTime = -1f;
    }
    // A vault carried by its clip's hands, or null for the straight move.
    // Climbing off a ladder's top on its clip's hands (Motion Warping), as the vaults are, at the clip's own pace over
    // ladderExitDuration (user, 2026-11-08: "warp it"). Beatrun's clip grabs high and pushes the body up over its hands
    // (measured: from 1.5 m over the feet at 0.3 s down to 0.8 m by about 1.1 s). Its ladders' rails stand up past the
    // roof for that; ours end at it, and poles added for it looked ugly, so the hands go on the floor's near edge up
    // there (user's pick, 2026-11-08): the clip's push is shorter than the climb, and the body is carried the rest of
    // the way up onto the floor after the hands let go. Without a measure, the route.
    private WarpedTraversal StartLadderWarp(in ObstacleTarget target)
    {
        // No body to ask, or the climb-off clip puts no hands down: the route.
        if (ctx.ObstacleMotions == null || !ctx.ObstacleMotions.TryLadderExitMotion(ctx.Abilities.Fixtures.LadderLeftHand, out var motion))
            return null;
        // The clip's hands come down on the tops of the ladder's rails (user, 2026-11-08: the hands on the bar, not the
        // roof): across the ladder's middle in the plane of its rails, at their top; PlayerBodyHands then closes each hand
        // on its own rail.
        var ladder = target.ExitLadder;
        Vector3 feet = ctx.transform.position;
        Vector3 middle = feet - ladder.Outward * Vector3.Dot(feet - ladder.transform.position, ladder.Outward);
        middle += ladder.transform.right * Vector3.Dot(ladder.transform.position - middle, ladder.transform.right);
        middle.y = (ladder.transform.position + ladder.transform.up * (ladder.Height - Ladder.BarThickness)).y;
        Vector3 grip = middle + ladder.Outward * (Ladder.BarThickness * .5f + motion.WristAboveTop);
        return WarpedTraversal.TryStart(ctx, target, motion, 0f, Mathf.Max(.1f, target.Duration), grip);
    }

    private WarpedTraversal StartWarp(in ObstacleTarget target)
    {
        if (!target.StraightLine || !Warps(target.Kind)) return null;
        // No body to ask, or the clip puts no hands down: the straight move.
        if (ctx.ObstacleMotions == null || !ctx.ObstacleMotions.TryObstacleMotion(target.Kind, out var motion)) return null;
        return WarpedTraversal.TryStart(ctx, target, motion, entryVelocity.magnitude);
    }

    // The kinds carried by their clip's hands (Ability.warpKong, warpOver, warpOverHigh).
    private bool Warps(VaultKind kind)
    {
        var settings = ctx.AbilitySettings;
        return kind == VaultKind.Kong ? settings.warpKong : kind == VaultKind.Over ? settings.warpOver
            : kind == VaultKind.OverHigh && settings.warpOverHigh;
    }
}
