using UnityEngine;

/// <summary>
/// A Détente getting within its arm's reach of the friend it means to dose or bandage. The syringe reaches a friend
/// 3.5 m off and the treatment by hand one 2.8 m off, and an arm with a syringe in it reaches a spot about 0.6 m from
/// its shoulder (the editor's InjectBench measured it), so the hand worked in the air. Asked which way to close that
/// gap, the user chose the run (2026-11-18): "닿는 거리로 빠르게 이동하죠, 그 써지 기능처럼요. 물론 충돌 판정이나 벽을 뚫는
/// 그런 방지는 대비를 해야 할 것입니다. 훌륭한 OOP라면 적은 코드로 높은 유동성과 안정성을 마련할 수 있을 겁니다."
/// So nothing of a run is made here. The body's one surge carries it (GroundSurge, the vault's own: its pace, its camera
/// push, and everything that ends it), and whether the way is clear is asked of SurgeWay before the run is, with the
/// body's own capsule on the game's physics: no wall, no shut door, no drop, and the friend in the open at the arm's
/// height from where the run ends. What is not clear is not offered (Look, which DetenteKit asks before it offers a
/// treatment and when the class key is pressed).
/// What is here is only what a dose needs of a run.
/// - How near is near enough, measured where it matters: from this body's shoulder to the spot on the friend the needle
///   goes to (InjectArm.SpotOn, the very spot the arm will be put on), in this arm's own lengths (the motion's row of
///   Tables/InjectArms: within reach nothing runs; a run ends at near). Body to body it came out wrong on a broad body:
///   the bench's mannequin stood "in reach" 12 cm short of a friend's arm.
/// - A friend who does not stand still: every frame of the run the way to where they are now is walked again and the
///   surge turned there (Steer), within the time the furthest friend in range would have taken, so one who keeps running
///   off is not chased. In reach, the run ends and the body is put back at the pace it had (Land); the dose or the
///   treatment is DetenteKit's to give then. A run that does not get there costs nothing: nothing was spent before the
///   arm could reach.
/// This PC's own player only: the acting PC decides (memory actor-decides), the others see the body run as they see it
/// walk, and hear of the dose when it goes in.
/// </summary>
internal sealed class InjectApproach
{
    /// <summary>What stands between this body and a friend.</summary>
    public enum Way
    {
        /// <summary>The arm reaches them from here.</summary>
        There,
        /// <summary>A run gets within reach.</summary>
        Run,
        /// <summary>The way is not clear, they are too far, or the arm would not reach them from beside them either.</summary>
        Blocked,
        /// <summary>A run would get there, but the body cannot run as it is (crouched, lying, in the air).</summary>
        Stance,
    }

    public enum State { None, Running, Arrived, Failed }

    // Seconds over what the run should take (the vault's surge has the same); how near two bodies' middles a run may
    // bring them (m: a friend is run up to, not through); a run nearer its end than this has got there (m).
    private const float Spare = .1f, LeastApart = .3f, Arrive = .05f;
    // An arm nobody could measure (a body with no humanoid arm) is taken as this long (m).
    private const float ArmGuess = .5f;

    private static SurgeWay.Walker walker;

    private readonly PlayerMovement movement;
    private float range;

    public InjectApproach(PlayerMovement detente) => movement = detente;

    /// <summary>The friend being run to, and what for; null and None when no run is under way.</summary>
    public PlayerMovement Friend { get; private set; }
    public InjectArm.Kind Kind { get; private set; }
    public bool Running => Friend != null;

    /// <summary>
    /// What stands between this body and <paramref name="friend"/>, no further off than <paramref name="within"/> (m),
    /// for a motion. <paramref name="spot"/> is where a run would end; <paramref name="gap"/> how far the spot on the
    /// friend is from this shoulder now, in arm lengths.
    /// </summary>
    public static Way Look(PlayerMovement detente, PlayerMovement friend, InjectArm.Kind kind, float within, out Vector3 spot, out float gap)
    {
        Vector3 feet = detente.transform.position, goal = friend.transform.position;
        spot = feet;
        gap = float.MaxValue;
        var move = InjectArm.Move.Of(kind);
        if (move == null) return Way.Blocked;
        // From this shoulder to the spot on them, and the arm it is measured in.
        Vector3 shoulder = feet + Vector3.up * move.ReachAt, onThem = goal + Vector3.up * move.ReachAt;
        float arm = ArmGuess;
        var own = InjectArm.Of(detente);
        if (own != null && own.TryShoulder(out Vector3 at, out float length) && length > 0f)
        {
            shoulder = at;
            arm = length;
            var theirs = friend.TryGetComponent(out PlayerBody body) ? body.BodyAnimator : null;
            if (!InjectArm.SpotOn(move, theirs, shoulder, null, null, out onThem, out _)) onThem = goal + Vector3.up * move.ReachAt;
        }
        Vector3 to = onThem - shoulder, flat = Vector3.ProjectOnPlane(goal - feet, Vector3.up);
        float apart = flat.magnitude;
        gap = to.magnitude / arm;
        walker ??= new SurgeWay.Walker();
        walker.Read(detente);
        var rules = SurgeWay.Rules.Read();
        // From here already: the walk with no run in it still says whether they are on this level and in the open.
        if (gap <= move.Reach)
            return SurgeWay.To(walker, feet, goal, apart + 1f, within, move.ReachAt, rules).Answer == SurgeWay.Answer.There ? Way.There : Way.Blocked;
        if (apart <= LeastApart) return Way.Blocked;
        // Else a run straight at them, as long as it takes to bring the spot to where the arm reaches with ease, or as
        // near as that line passes it; never up to their middle.
        Vector3 way = flat / apart;
        float along = Vector3.Dot(to, way), off = to.sqrMagnitude - along * along, near = move.Near * arm;
        float run = Mathf.Min(off < near * near ? along - Mathf.Sqrt(near * near - off) : along, apart - LeastApart);
        if (run <= 0f || (to - way * run).magnitude > move.Reach * arm) return Way.Blocked;
        var plan = SurgeWay.To(walker, feet, goal, apart - run, within, move.ReachAt, rules);
        if (plan.Answer != SurgeWay.Answer.Clear) return Way.Blocked;
        spot = plan.Spot;
        return detente.Abilities != null && detente.Abilities.Surge.Possible ? Way.Run : Way.Stance;
    }

    /// <summary>What stands between this body and a friend, for who only asks whether there is a way.</summary>
    public static Way Look(PlayerMovement detente, PlayerMovement friend, InjectArm.Kind kind, float within, out Vector3 spot)
        => Look(detente, friend, kind, within, out spot, out _);

    /// <summary>
    /// For <paramref name="to"/>: in reach already (There, nothing begun), or the run begins (Run), or why there is none.
    /// </summary>
    public Way Begin(PlayerMovement to, InjectArm.Kind what, float within)
    {
        Stop();
        var way = Look(movement, to, what, within, out Vector3 spot, out _);
        if (way != Way.Run) return way;
        var surge = movement.Abilities.Surge;
        // At the surge's pace, or the body's own if that is faster, as a vault's.
        float pace = Mathf.Max(movement.AbilitySettings.vaultSurgeSpeed, Vector3.ProjectOnPlane(movement.HorizontalVelocity, Vector3.up).magnitude);
        float gap = Vector3.ProjectOnPlane(spot - movement.transform.position, Vector3.up).magnitude;
        // Time for the furthest friend in range, whatever this one's distance: what is over is for one who walks on.
        if (!surge.Begin(this, spot, pace, Mathf.Max(0f, within - gap) / pace + Spare)) return Way.Blocked;
        Friend = to;
        Kind = what;
        range = within;
        return Way.Run;
    }

    /// <summary>
    /// A frame of the run, looked after: Arrived once the arm reaches the friend with ease, or reaches them at all where
    /// the run ends (the run is over, the body at its own pace again); Failed when it will not (they went down or away,
    /// something came between, the time ran out, the body left the ground or went into another move); Running else.
    /// </summary>
    public State Tick(float dt)
    {
        if (Friend == null) return State.None;
        var friend = Friend;
        var surge = movement.Abilities.Surge;
        var life = friend.DamageReceiver;
        bool treatable = friend.isActiveAndEnabled && life != null && life.IsAlive && !life.IsDowned;
        // Where they are now: a friend who walked on is run after, a friend met early is not run through.
        Vector3 spot = movement.transform.position;
        float gap = float.MaxValue;
        var way = treatable ? Look(movement, friend, Kind, range, out spot, out gap) : Way.Blocked;
        var move = InjectArm.Move.Of(Kind);
        bool running = surge.RunsFor(this) && surge.Spend(this, dt) > 0f;
        if (way == Way.There)
        {
            // In reach: go on to where the arm reaches with ease, unless the run is at its end.
            if (running && move != null && gap > move.Near && surge.ShortOf(movement.transform.position) > Arrive) return State.Running;
            End();
            return State.Arrived;
        }
        if (way != Way.Run || !running)
        {
            End();
            return State.Failed;
        }
        surge.Steer(this, spot);
        return State.Running;
    }

    /// <summary>The run is called off (the class changed, the body is gone).</summary>
    public void Stop()
    {
        if (Friend != null) End();
    }

    private void End()
    {
        Friend = null;
        Kind = InjectArm.Kind.None;
        if (movement != null && movement.Abilities != null) movement.Abilities.Surge.Land(this);
    }
}
