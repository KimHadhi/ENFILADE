using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// A surge: a short run along the ground at a pace of its own to a spot, for no longer than getting there should take.
/// A body has one. It began as the vault's (the user, 2026-11-06: a jump pressed early at a low obstacle should get
/// there with speed rather than a teleport); since 2026-11-18 anything may ask for it, because the user asked for the
/// same run to carry a Détente within its arm's reach of the friend it doses ("닿는 거리로 빠르게 이동하죠, 그 써지
/// 기능처럼요. 물론 충돌 판정이나 벽을 뚫는 그런 방지는 대비를 해야 할 것입니다"). One mover for both, not a second one
/// beside the first.
/// What it is: a ground velocity that LocomotionState keeps in place of the keys' while it runs (Velocity), and the time
/// it may still take. The capsule's own move carries it, so whatever stops a walking body stops it: a wall, a shut
/// door, a zombie. It is over the moment the body is off the ground or in any move but standing (Tick), so it carries
/// nobody on through the air past an edge, nor through a dive or a climb. It is a run and nothing else: it starts no
/// move and ends in none.
/// Who asked for it looks after it every frame it runs: spends its time (Spend), sees how far it still is from its spot
/// (ShortOf), turns it if the spot moved (Steer), and does what the run was for when it gets there (the vault begins,
/// the needle goes in). A surge nobody looks after any more ends by itself.
/// Whether the way to a spot is clear at all is asked before, of SurgeWay.
/// </summary>
public sealed class GroundSurge
{
    // A surge nobody has spent for this long (s) is over: who asked for it is gone.
    private const float Unattended = .5f;

    private readonly PlayerMovement ctx;
    private object asker;
    private Vector3 velocity, spot;
    private float left, from, idle;

    internal GroundSurge(PlayerMovement context) => ctx = context;

    /// <summary>The ground velocity of the surge under way; zero when there is none.</summary>
    public Vector3 Velocity => left > 0f ? velocity : Vector3.zero;
    public bool Running => left > 0f;
    /// <summary>Whether a surge is under way that <paramref name="who"/> asked for.</summary>
    public bool RunsFor(object who) => left > 0f && ReferenceEquals(asker, who);
    /// <summary>How fast the body ran when the surge began (m/s): a move begun at its end goes on at that, not at the surge's pace.</summary>
    public float From => from;
    /// <summary>The body can surge as it is: standing, on the ground.</summary>
    public bool Possible => ctx.IsGrounded && ctx.CurrentState == State.Standing;

    /// <summary>
    /// The run begins: from where the body stands toward <paramref name="to"/> at <paramref name="pace"/> (m/s), with
    /// <paramref name="spare"/> seconds over what the way takes, and the camera's push that sells the burst (the view
    /// dips forward a little and widens). A surge someone else asked for gives way to it. False, and nothing begun,
    /// when the spot is where the body stands.
    /// </summary>
    internal bool Begin(object who, Vector3 to, float pace, float spare)
    {
        Vector3 flat = Vector3.ProjectOnPlane(to - ctx.transform.position, Vector3.up);
        float gap = flat.magnitude;
        if (who == null || gap <= 1e-6f || pace <= 0f) return false;
        asker = who;
        from = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up).magnitude;
        velocity = flat / gap * pace;
        spot = to;
        left = gap / pace + spare;
        idle = 0f;
        var kick = ctx.AbilitySettings.vaultSurgeKick;
        ctx.GetComponentInChildren<PlayerCameraEffects>().OrNull()?.ApplyCameraImpulse(new Vector3(kick.x, 0f, 0f), kick.y);
        return true;
    }

    /// <summary>
    /// The surge <paramref name="who"/> asked for goes on toward another spot (a friend who walked on) at the pace it
    /// has; its time stays as it was, so a spot that keeps running away is not chased for long. False when no surge of
    /// theirs is under way.
    /// </summary>
    internal bool Steer(object who, Vector3 to)
    {
        if (!RunsFor(who)) return false;
        Vector3 flat = Vector3.ProjectOnPlane(to - ctx.transform.position, Vector3.up);
        spot = to;
        if (flat.sqrMagnitude > 1e-6f) velocity = flat.normalized * velocity.magnitude;
        return true;
    }

    /// <summary>
    /// A frame of the surge, taken off its time by who looks after it. What is left (s): at 0 or less it has run out and
    /// is over, and what to make of where the body got to is theirs to say in this same frame.
    /// </summary>
    internal float Spend(object who, float dt)
    {
        if (!RunsFor(who)) return 0f;
        idle = 0f;
        left -= dt;
        return left;
    }

    /// <summary>How far the body still is from the surge's spot along its way (m); below 0 once past it.</summary>
    public float ShortOf(Vector3 feet) => Vector3.Dot(spot - feet, velocity.normalized);

    /// <summary>Ends the surge <paramref name="who"/> asked for; another's is left alone.</summary>
    internal void Stop(object who)
    {
        if (ReferenceEquals(asker, who)) left = 0f;
    }

    /// <summary>
    /// Ends the surge <paramref name="who"/> asked for where the body is, and puts the body back at the pace it ran at
    /// before (standing still, if it stood): left at the surge's pace it would slide on past its spot. For a surge that
    /// ends in standing somewhere; a move begun at a surge's end takes the body over by itself.
    /// </summary>
    internal void Land(object who)
    {
        if (!ReferenceEquals(asker, who)) return;
        if (left > 0f || ctx.HorizontalVelocity.sqrMagnitude > from * from) ctx.HorizontalVelocity = velocity.normalized * from;
        left = 0f;
    }

    /// <summary>Ends whatever surge is under way (the body is put somewhere else, or made anew).</summary>
    internal void Stop() => left = 0f;

    /// <summary>
    /// Every frame, before anything of the frame moves: a surge is a run on the ground, so off the ground, or in any
    /// move but standing, it is over; and so is one nobody looks after.
    /// </summary>
    internal void Tick(float dt)
    {
        if (left <= 0f) return;
        idle += dt;
        if (!ctx.IsGrounded || ctx.CurrentState != State.Standing || idle > Unattended) left = 0f;
    }
}
