using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A class throwable in flight (the knife flies on its own: ThrownKnife): its model, a thin trail, a clatter at each
/// bounce, and its fuse. What moves it is GrenadeFlight, tick by tick: the very function the aiming line ran ahead of
/// time (ThrowArc.Fly), so it bounces where the line said it would (the user, 2026-11-18: "튕겨 나오는 예상 바운스 궤적
/// 표시"). It is no physics body: the engine's bounces and rolls could not be drawn beforehand, differed with the frame
/// rate and differed between PCs; now every PC flies the same throw the same way from where it left the hand, how fast
/// and how long its fuse was. It bounces off the built world as its kind bounces (the flashbang most, to bank it off a
/// wall) and passes zombies, players and the dead, as it always did; having no collider, nothing meets it at all.
/// On the thrower's PC the fuse decides: it bursts there (GrenadeBursts) and says where (PlayerGrenades → every other
/// PC). A friend's copy bursts where the thrower's PC says, so the burst is the same spot everywhere; one that hears
/// nothing bursts on its own a moment after its fuse.
/// Checked without playing: the thrower's PC flies the whole throw once more as it leaves the hand and, at the burst,
/// writes that prediction beside where it really burst (Logs/grenade-flight.txt): the two must be one point.
/// </summary>
[DisallowMultipleComponent]
public sealed class ThrownGrenade : MonoBehaviour
{
    public const string RecordName = "grenade-flight";
    private const string RecordHeading = "each grenade this PC's player threw (ThrownGrenade): where the flight said at the throw it would burst, "
        + "where it burst, how far apart (m; 0 unless the world changed under it), its bounces and its fuse";
    // The quietest bounce that clatters (m/s into the face), the least time between two clatters (s), the most of them;
    // how long a copy waits past its fuse to be told where it burst (s), and how long anything lives (s).
    private const float BounceSpeed = 1.4f, BounceGap = .12f, CopyGrace = 1.5f, Life = 30f, TrailSeconds = .22f;
    private const int MostBounces = 6;
    // More ticks than this owed in one frame (a long hitch) are flown over the next frames, never all at once.
    private const int MostTicks = 12;

    private Throwable kind;
    private PlayerMovement thrower;
    private Action<ThrownGrenade> burstAt;
    private GrenadeFlight flight;
    private GrenadeFlight.Body body;
    private GrenadeFlight.Rules rules;
    // Where it was a tick ago (it is drawn between the two), the time not yet flown, and its tumble (degrees a second).
    private Vector3 before, spin, predicted;
    private float fuse, age, owed, lastBounce = -1f;
    private int clatters;
    private bool mine, done;

    internal ushort Id { get; private set; }
    internal Throwable Kind => kind;

    /// <summary>
    /// Throws one: from where, how fast, with how long left on its fuse (s). mine: this PC's player threw it, and its fuse
    /// decides; burst is then told where it burst (for the other PCs).
    /// </summary>
    internal static ThrownGrenade Throw(Throwable kind, ushort id, Vector3 from, Vector3 velocity, float fuse, PlayerMovement thrower,
        bool mine, Action<ThrownGrenade> burst)
    {
        var info = Throwables.Of(kind);
        if (info == null) return null;
        var host = new GameObject("Thrown " + kind);
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.World, "Debris");
        host.transform.SetPositionAndRotation(from, velocity.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(velocity) * Quaternion.Euler(80f, 0f, 0f) : UnityEngine.Random.rotation);
        Throwables.Dress(kind, host.transform);
        var trail = host.AddComponent<TrailRenderer>();
        trail.time = TrailSeconds;
        trail.startWidth = .035f;
        trail.endWidth = 0f;
        trail.minVertexDistance = .05f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, true);
        trail.startColor = KitStyle.With(info.Tint, .55f);
        trail.endColor = KitStyle.With(info.Tint, 0f);
        var grenade = host.AddComponent<ThrownGrenade>();
        grenade.kind = kind;
        grenade.Id = id;
        grenade.fuse = Mathf.Max(0f, fuse);
        grenade.thrower = thrower;
        grenade.mine = mine;
        grenade.burstAt = burst;
        grenade.body = GrenadeFlight.BodyOf(info);
        grenade.rules = GrenadeFlight.RulesNow;
        grenade.flight = GrenadeFlight.From(from, velocity);
        grenade.before = from;
        // Its tumble is only seen (each PC's own): about as fast as the body the engine used to spin.
        grenade.spin = Throwables.Spin(14f) * Mathf.Rad2Deg;
        // What the flight says now of where it will burst, kept to be set beside where it does (the record).
        if (mine && PlayRecord.On) grenade.predicted = GrenadeFlight.Fly(from, velocity, grenade.fuse, grenade.body, grenade.rules).Position;
        // The indicators round the crosshair show every live one (made with the first throw on this PC).
        GrenadeIndicators.Prepare();
        return grenade;
    }

    private void Update()
    {
        if (done) return;
        float dt = Time.deltaTime;
        age += dt;
        owed += dt;
        // The thrower's PC bursts it when its fuse runs out, at the tick the line counted to; a copy waits to hear where,
        // a moment past its own fuse.
        for (int ticks = 0; owed >= rules.Tick && ticks < MostTicks; ticks++)
        {
            if (mine && flight.Over(fuse)) break;
            owed -= rules.Tick;
            before = flight.Position;
            bool wasResting = flight.Resting;
            if (flight.Step(body, rules, out Vector3 struck, out float speed))
            {
                Clatter(struck, speed);
                if (mine) Turned?.Invoke(this);
            }
            else if (mine && flight.Resting && !wasResting) Turned?.Invoke(this);
        }
        if (mine && flight.Over(fuse))
        {
            Burst(flight.Position);
            return;
        }
        // Drawn between the last tick and this one, so it moves every frame however long a tick is.
        transform.position = Vector3.Lerp(before, flight.Position, Mathf.Clamp01(owed / rules.Tick));
        if (!flight.Resting) transform.Rotate(spin * dt, Space.World);
        if (!mine && age >= fuse + CopyGrace) Burst(flight.Position);
        else if (age >= Life) Destroy(gameObject);
    }

    /// <summary>
    /// On the thrower's PC: one of its own bounced, or came to rest. Its flight goes to the copies then (Flight, Follow), so
    /// a copy that bounced off something a little different on its PC (a door mid-swing, a set piece, a barricade) is put
    /// back on the thrower's path at once rather than jumping to where it bursts (SyncAudit.md, item 18).
    /// </summary>
    internal static event Action<ThrownGrenade> Turned;

    /// <summary>
    /// Every thrown grenade on this PC, flying or lying, this PC's player's own and the copies of friends': what the
    /// grenade indicators round the crosshair show (GrenadeIndicators), as the addons they come from look at every
    /// grenade entity there is. Each is in it from its throw until its object is gone; one that burst this frame is
    /// still in it until the end of the frame, marked Done.
    /// </summary>
    internal static readonly List<ThrownGrenade> Live = new List<ThrownGrenade>();

    /// <summary>It burst (its own fuse, or the thrower's PC said where): gone from the world at the end of this frame.</summary>
    internal bool Done => done;

    private void OnEnable() => Live.Add(this);
    private void OnDisable() => Live.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Turned = null;
        Live.Clear();
    }

    /// <summary>Its flight as it is now (the thrower's PC sends it as it turns).</summary>
    internal GrenadeFlight Flight => flight;
    internal PlayerMovement Thrower => thrower;

    // A copy further than this from the thrower's flight, at the same age, takes it (m).
    private const float FollowSlack = .05f;

    /// <summary>
    /// A friend's copy: the thrower's flight as it bounced or came to rest. Flown on here to this copy's age; where this
    /// copy has gone another way, it goes on from there.
    /// </summary>
    internal void Follow(GrenadeFlight theirs)
    {
        if (done || mine) return;
        for (int ticks = 0; theirs.Age < flight.Age - 1e-4f && ticks < 600; ticks++) theirs.Step(body, rules, out _, out _);
        if ((theirs.Position - flight.Position).sqrMagnitude <= FollowSlack * FollowSlack && theirs.Resting == flight.Resting) return;
        flight = theirs;
        before = flight.Position;
    }

    /// <summary>The thrower's PC says it burst there: this copy bursts in that very spot.</summary>
    internal void BurstAt(Vector3 at)
    {
        if (done) return;
        done = true;
        transform.position = at;
        GrenadeBursts.Play(kind, at, thrower, false);
        Destroy(gameObject);
    }

    // Its own burst (the thrower's, or a copy nobody told): a hand's width up from what it rests on, so the burst isn't
    // buried in the floor; the thrower's PC tells the others that spot (Position).
    private void Burst(Vector3 at)
    {
        if (done) return;
        done = true;
        transform.position = at;
        GrenadeBursts.Play(kind, Position, thrower, mine);
        if (mine)
        {
            if (PlayRecord.On)
                PlayRecord.Line(RecordName, RecordHeading, $"{kind} #{Id} predicted {PlayRecord.At(predicted)} flown {PlayRecord.At(at)}"
                    + $" off {Vector3.Distance(predicted, at):0.000} | bounces {flight.Bounces} | fuse {fuse:0.00} tick {rules.Tick:0.000}");
            burstAt?.Invoke(this);
        }
        Destroy(gameObject);
    }

    /// <summary>Where it bursts (the thrower's PC tells the others this spot).</summary>
    internal Vector3 Position => transform.position + Vector3.up * .06f;

    // A bounce heard: louder the harder it struck, a few times at most and never two at once.
    private void Clatter(Vector3 at, float speed)
    {
        // Each bounce turns its tumble another way.
        spin = Quaternion.AngleAxis(97f, Vector3.up + Vector3.right * .4f) * spin * .7f;
        if (speed < BounceSpeed || clatters >= MostBounces || age - lastBounce < BounceGap) return;
        clatters++;
        lastBounce = age;
        float strength = Mathf.InverseLerp(BounceSpeed, 12f, speed);
        Throwables.Play("Bounce", at, Mathf.Lerp(.35f, 1f, strength), 1.5f, 30f, UnityEngine.Random.Range(.92f, 1.1f));
    }
}
