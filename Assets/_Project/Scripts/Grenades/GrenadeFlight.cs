using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a thrown grenade flies: one function that the grenade itself is moved by (ThrownGrenade) and that the aiming
/// line runs ahead of time (ThrowArc.Fly), so the bounce that is drawn IS the bounce that happens. The user, 2026-11-18:
/// "투척 시 벽이나 오브젝트에 튕겨 나오는 예상 바운스 궤적 표시".
/// <para>
/// Until then there were two flights. The grenade was a physics body the engine bounced and rolled (its restitution, its
/// friction, its spin), and the line was an integrator of its own that stopped at the first thing it met: the line
/// ended where the grenade only began to bounce, and the burst ring was drawn where it never burst. No second model can
/// follow the engine's: the same throw lands differently with the frame rate, and differently on another PC. So the
/// grenade is no physics body any more. This is how shipped shooters do it (Source's grenades are moved by game code with
/// a fixed tick, a swept hull and an elasticity, and its trajectory preview runs the same ticks): the standard part. The
/// contact rule below is this game's own choice of it.
/// </para>
/// <para>
/// A tick (GrenadeRules flightTick, the same everywhere, whatever the frame rate or a slow motion does): gravity (the
/// kind's share of it) and a little air drag, then the move, swept as a ball of the kind's size against the built
/// world (WorldLayers.Built: it passes players, zombies, the dead and triggers, as it always did). Where it meets a
/// face, the speed it came in with decides: fast enough (flightBounceFrom) it bounces, keeping its kind's bounce
/// (Grenades.csv bounce) of the speed into the face, the face taking a share of the speed along it (flightGrip: a ball
/// that starts to roll); slower, it stays on the face and rolls along it, the roll wearing off (flightRoll), and on
/// ground level enough it comes to rest (flightRest).
/// </para>
/// Every PC flies the same throw from the same start (where, how fast, how long its fuse) through the same function
/// over the same map, so the copies fly the thrower's path; where it bursts is still the thrower's PC's word
/// (PlayerGrenades.Burst). Checked without playing: the thrower's PC writes the burst it predicted at the throw beside
/// the burst it flew to Logs/grenade-flight.txt (PlayRecord); they must be the same point.
/// </summary>
public struct GrenadeFlight
{
    /// <summary>What flies: a ball this big (m), under this share of gravity, keeping this much of an impact's speed.</summary>
    public readonly struct Body
    {
        public readonly float Radius, Gravity, Bounce;

        public Body(float radius, float gravity, float bounce)
        {
            Radius = Mathf.Max(.01f, radius);
            Gravity = Mathf.Max(0f, gravity);
            Bounce = Mathf.Clamp01(bounce);
        }
    }

    /// <summary>How anything thrown flies (Tables/GrenadeRules flight*), and the world it flies in.</summary>
    public readonly struct Rules
    {
        public readonly float Tick, Drag, Grip, Roll, BounceFrom, Rest;
        public readonly int World;

        public Rules(float tick, float drag, float grip, float roll, float bounceFrom, float rest, int world)
        {
            Tick = Mathf.Clamp(tick, .005f, .1f);
            Drag = Mathf.Max(0f, drag);
            Grip = Mathf.Clamp01(grip);
            Roll = Mathf.Max(0f, roll);
            BounceFrom = Mathf.Max(.05f, bounceFrom);
            Rest = Mathf.Max(0f, rest);
            World = world;
        }
    }

    // How near a face it stops (m): never touching, so the next sweep still sees the face it lies on.
    private const float Skin = .005f;
    // Ground level enough to lie on (the face's normal's height): steeper, it slides on.
    private const float Level = .6f;
    // The most faces it turns on in one tick (a corner): past them it waits for the next.
    private const int MostTurns = 4;

    /// <summary>Where it is and how fast it goes, after the ticks so far.</summary>
    public Vector3 Position, Velocity;
    /// <summary>How long it has flown (s, whole ticks).</summary>
    public float Age;
    /// <summary>How many times it has bounced.</summary>
    public int Bounces;
    /// <summary>It lies still: nothing moves it any more.</summary>
    public bool Resting;

    /// <summary>A flight about to start: out of the hand there, that fast.</summary>
    public static GrenadeFlight From(Vector3 from, Vector3 velocity) => new GrenadeFlight { Position = from, Velocity = velocity };

    /// <summary>A kind's body (Tables/Grenades size, gravity, bounce).</summary>
    public static Body BodyOf(ThrowableInfo info) => new Body(info.Size * .5f, info.Gravity, info.Bounce);

    /// <summary>The rules as the table has them now (Tables/GrenadeRules flight*).</summary>
    public static Rules RulesNow => new Rules(Throwables.Rule("flightTick", .02f), Throwables.Rule("flightDrag", .05f),
        Throwables.Rule("flightGrip", .29f), Throwables.Rule("flightRoll", 3f), Throwables.Rule("flightBounceFrom", 1.4f),
        Throwables.Rule("flightRest", .3f), WorldLayers.Built);

    /// <summary>
    /// One tick on. True when it struck a face hard enough to bounce: <paramref name="struck"/> is where, and
    /// <paramref name="speed"/> how fast into the face (the clatter's loudness, the line's mark).
    /// </summary>
    public bool Step(in Body body, in Rules rules, out Vector3 struck, out float speed)
    {
        struck = Position;
        speed = 0f;
        float dt = rules.Tick;
        Age += dt;
        if (Resting) return false;
        Velocity += Physics.gravity * (body.Gravity * dt);
        Velocity *= Mathf.Max(0f, 1f - rules.Drag * dt);
        float left = dt;
        bool bounced = false, rolled = false;
        for (int turn = 0; turn < MostTurns && left > 1e-6f; turn++)
        {
            Vector3 move = Velocity * left;
            float length = move.magnitude;
            if (length < 1e-6f) break;
            Vector3 way = move / length;
            if (!Physics.SphereCast(Position, body.Radius, way, out RaycastHit hit, length + Skin, rules.World, QueryTriggerInteraction.Ignore))
            {
                Position += move;
                break;
            }
            float travel = Mathf.Clamp(hit.distance - Skin, 0f, length);
            Position += way * travel;
            left *= 1f - travel / length;
            Vector3 normal = hit.normal;
            Turn(ref Velocity, normal, body.Bounce, rules, rolled ? 0f : dt, out float into, out bool bounce);
            if (bounce)
            {
                if (into > speed)
                {
                    struck = hit.point;
                    speed = into;
                }
                bounced = true;
                Bounces++;
                continue;
            }
            rolled = true;
            if (normal.y >= Level && Velocity.magnitude <= rules.Rest)
            {
                Velocity = Vector3.zero;
                Resting = true;
                break;
            }
        }
        return bounced;
    }

    /// <summary>
    /// What a face does to the speed it is met with (the contact rule, a pure function: checked on its own, without the
    /// game). Into the face at <paramref name="bounceFrom"/> or faster: it bounces, keeping <paramref name="bounce"/> of
    /// the speed into the face, and the face takes <paramref name="grip"/> of the speed along it. Slower: it stays on the
    /// face, the speed into it gone, and what is left along the face wears off by <paramref name="roll"/> a second over
    /// <paramref name="seconds"/>.
    /// </summary>
    public static Vector3 Turn(Vector3 velocity, Vector3 normal, float bounce, float grip, float roll, float bounceFrom, float seconds,
        out float into, out bool bounces)
    {
        float along = Vector3.Dot(velocity, normal);
        into = Mathf.Max(0f, -along);
        bounces = into >= bounceFrom;
        // Already leaving the face (a graze the sweep still caught): nothing to turn.
        if (along >= 0f) return velocity;
        Vector3 slide = velocity - normal * along;
        if (bounces) return slide * (1f - grip) + normal * (into * bounce);
        float fast = slide.magnitude;
        float slower = Mathf.Max(0f, fast - roll * seconds);
        return fast > 1e-5f ? slide * (slower / fast) : Vector3.zero;
    }

    private static void Turn(ref Vector3 velocity, Vector3 normal, float bounce, in Rules rules, float seconds, out float into, out bool bounces)
        => velocity = Turn(velocity, normal, bounce, rules.Grip, rules.Roll, rules.BounceFrom, seconds, out into, out bounces);

    /// <summary>
    /// Flies a throw for <paramref name="seconds"/> (its fuse), in whole ticks as the thrown one itself does. Where it is
    /// after each tick goes into <paramref name="path"/> and where it bounced into <paramref name="bounces"/> (either
    /// may be null). Returns it as it is when the time is up: where it bursts.
    /// </summary>
    public static GrenadeFlight Fly(Vector3 from, Vector3 velocity, float seconds, in Body body, in Rules rules,
        List<Vector3> path = null, List<Vector3> bounces = null)
    {
        var flight = From(from, velocity);
        path?.Add(from);
        while (!flight.Over(seconds))
        {
            if (flight.Step(body, rules, out Vector3 struck, out _)) bounces?.Add(struck);
            // Lying still, it stays where it is until the time is up: no more points to draw.
            if (path != null && !(flight.Resting && path.Count > 0 && path[path.Count - 1] == flight.Position)) path.Add(flight.Position);
            if (flight.Resting) break;
        }
        return flight;
    }

    /// <summary>Its fuse of <paramref name="seconds"/> has run out (the same test for the thrown one and the line: whole ticks).</summary>
    public bool Over(float seconds) => Age >= seconds - 1e-4f;
}
