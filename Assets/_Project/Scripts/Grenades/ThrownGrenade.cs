using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A class throwable in flight (the knife flies on its own: ThrownKnife): its model on a small ball that bounces as its
/// kind bounces (the flashbang most, to bank it off a wall), a thin trail, a clatter at each bounce, and its fuse. On the
/// thrower's PC the fuse decides: it bursts there (GrenadeBursts) and says where (PlayerGrenades → every other PC). A
/// friend's copy flies the same throw by its own physics and bursts where the thrower's PC says, so the burst is the
/// same spot everywhere; one that hears nothing bursts on its own a moment after its fuse. Like the stone it passes
/// zombies and players (layer 31) and lands where it was thrown; bullets go through it (Throwables.Passes).
/// </summary>
[DisallowMultipleComponent]
public sealed class ThrownGrenade : MonoBehaviour
{
    private const float BounceSpeed = 1.4f, BounceGap = .12f, CopyGrace = 1.5f, Life = 30f, TrailSeconds = .22f;
    private const int MostBounces = 6;
    private static readonly Dictionary<int, PhysicsMaterial> bounceMaterials = new Dictionary<int, PhysicsMaterial>();

    private Throwable kind;
    private PlayerMovement thrower;
    private Action<ThrownGrenade> burstAt;
    private SphereCollider ball;
    private Rigidbody body;
    private float fuse, age, lastBounce = -1f, extraGravity;
    private int bounces;
    private bool mine, done;

    internal ushort Id { get; private set; }
    internal Throwable Kind => kind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => bounceMaterials.Clear();

    /// <summary>
    /// Throws one: from where, how fast, with how long left on its fuse (s). mine: this PC's player threw it, and its fuse
    /// decides; burst is then told where it burst (for the other PCs).
    /// </summary>
    internal static ThrownGrenade Throw(Throwable kind, ushort id, Vector3 from, Vector3 velocity, float fuse, PlayerMovement thrower,
        bool mine, Action<ThrownGrenade> burst)
    {
        var info = Throwables.Of(kind);
        if (info == null) return null;
        var host = new GameObject("Thrown " + kind) { layer = GoreGibs.Layer };
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.World, "Debris");
        host.transform.SetPositionAndRotation(from, velocity.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(velocity) * Quaternion.Euler(80f, 0f, 0f) : UnityEngine.Random.rotation);
        Throwables.Dress(kind, host.transform);
        var ball = host.AddComponent<SphereCollider>();
        ball.radius = Mathf.Max(.02f, info.Size * .5f);
        // It flies through zombies and players and lands where it was thrown, as the stone does.
        ball.excludeLayers = LayerMask.GetMask("Enemy") | (1 << 31);
        ball.sharedMaterial = BounceOf(info.Bounce);
        Throwables.Mark(ball);
        var body = host.AddComponent<Rigidbody>();
        body.mass = .4f;
        body.linearDamping = .05f;
        body.angularDamping = .35f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = velocity;
        body.angularVelocity = Throwables.Spin(14f);
        foreach (var player in PlayerRoster.All)
        {
            if (player == null) continue;
            foreach (var part in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(ball, part, true);
        }
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
        grenade.ball = ball;
        grenade.body = body;
        grenade.extraGravity = info.Gravity - 1f;
        return grenade;
    }

    // One physics material per bounciness, made once.
    private static PhysicsMaterial BounceOf(float bounce)
    {
        int key = Mathf.RoundToInt(Mathf.Clamp01(bounce) * 100f);
        if (bounceMaterials.TryGetValue(key, out var made) && made != null) return made;
        made = new PhysicsMaterial("Throwable Bounce " + key)
        {
            bounciness = key / 100f, dynamicFriction = .45f, staticFriction = .6f,
            bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Average,
        };
        bounceMaterials[key] = made;
        return made;
    }

    private void FixedUpdate()
    {
        if (body != null && !body.isKinematic && Mathf.Abs(extraGravity) > 1e-3f) body.AddForce(Physics.gravity * extraGravity, ForceMode.Acceleration);
    }

    private void Update()
    {
        if (done) return;
        float dt = Time.deltaTime;
        age += dt;
        fuse -= dt;
        // The thrower's PC bursts it when its fuse runs out; a copy waits to hear where, a moment past its own fuse.
        if ((mine && fuse <= 0f) || (!mine && fuse <= -CopyGrace)) Burst(transform.position);
        else if (age >= Life) Destroy(gameObject);
    }

    /// <summary>The thrower's PC says it burst there: this copy bursts in that spot.</summary>
    internal void BurstAt(Vector3 at)
    {
        if (done) return;
        transform.position = at;
        Burst(at);
    }

    private void Burst(Vector3 at)
    {
        if (done) return;
        done = true;
        // A hand's width up from what it rests on, so its burst isn't buried in the floor.
        at += Vector3.up * .06f;
        GrenadeBursts.Play(kind, at, thrower, mine);
        if (mine) burstAt?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>Where it is now (the thrower's PC tells the others the spot it burst at).</summary>
    internal Vector3 Position => transform.position + Vector3.up * .06f;

    private void OnCollisionEnter(Collision hit)
    {
        float speed = hit.relativeVelocity.magnitude;
        if (speed < BounceSpeed || bounces >= MostBounces || age - lastBounce < BounceGap) return;
        bounces++;
        lastBounce = age;
        Vector3 point = hit.contactCount > 0 ? hit.GetContact(0).point : transform.position;
        float strength = Mathf.InverseLerp(BounceSpeed, 12f, speed);
        Throwables.Play("Bounce", point, Mathf.Lerp(.35f, 1f, strength), 1.5f, 30f, UnityEngine.Random.Range(.92f, 1.1f));
    }

    private void OnDestroy()
    {
        if (!ReferenceEquals(ball, null)) Throwables.Unmark(ball);
    }
}
