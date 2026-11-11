using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One thrown knife in the world (PlayerKnives throws it, ThrowingKnives keeps them). It flies the way it was thrown,
/// end over end, falling a little, on every PC. Only the thrower's PC judges what it meets; every other PC flies a copy
/// that holds still where it meets something until the thrower's word places it (ThrowingKnives' notes):
/// - a body (a zombie's bone hitbox, anything else that takes damage) at speed ([MW] Quick Throwing Knife: 300 units/s):
///   the knife's damage goes in by the usual way (EnemyHealth, whose copy on a guest's PC passes it to the host), it
///   staggers what it lands in, and it stays in that bone, riding the body and then the corpse;
/// - a wall fast and square enough (TFA Base's thrown blade: over 250 units/s, and dot(normal, way) under
///   -1 + speed/1000 × 0.3 with the speed in units/s): it sticks in, its tip a few centimetres deep;
/// - anything else: it glances off (off a body it drops back at an eighth of its speed, as TFA's does), tumbles down
///   under physics and lies there.
/// Stuck or lying, it waits to be taken back with F, then goes after its time (TFA: 30 s lying, 60 s stuck). A knife in
/// a body goes with that body when its corpse sinks. It lies on the players' own layer like a dropped magazine: players
/// walk through it, and bullets and throws pass it (AttackTrace, ThrownSweep).
/// </summary>
[DisallowMultipleComponent]
public sealed class ThrownKnife : MonoBehaviour
{
    internal enum State : byte { Flying, Stuck, Loose, Lying }

    /// <summary>The bone index a knife in no humanoid bone is sent with.</summary>
    internal const byte NoBone = 255;
    // The longest it flies before it is let go (s); how long a copy that met something waits for the thrower's word (s);
    // how long it takes to shrink away when its time is up (s).
    private const float LongestFlight = 6f, WordWait = 1.5f, GoSeconds = .25f;
    // Tumbling, it lies still once slower than these (m/s, rad/s) for this long (s), or after this long at most (s).
    private const float StillSpeed = .08f, StillSpin = .5f, StillFor = .35f, LongestTumble = 4f;
    // How far off a surface a glancing knife starts its tumble (m); its box's thickness and width (m).
    private const float Clearance = .02f, BoxThickness = .02f, BoxWidth = .04f;
    // Its clatter as it tumbles: how hard a knock (m/s), how far apart (s), and how many at most.
    private const float ClatterSpeed = 1.5f, ClatterGap = .12f;
    private const int MostClatters = 3;
    // Below this the world is gone (m): a knife that fell out of the map.
    private const float Bottom = -500f;

    private static readonly HashSet<Collider> blades = new HashSet<Collider>();

    /// <summary>A knife's box: bullets and throws pass it.</summary>
    public static bool IsKnife(Collider collider) => collider != null && blades.Contains(collider);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => blades.Clear();

    public uint Id { get; private set; }
    internal State Now { get; private set; }
    /// <summary>Who threw it (null once they have left).</summary>
    internal PlayerMovement Thrower { get; private set; }
    /// <summary>The thrower's own PC: it judges what the knife meets and tells the others.</summary>
    internal bool Judges { get; private set; }
    /// <summary>When it came to rest, stuck or let loose (game time); its time counts from here.</summary>
    internal float LandedAt { get; private set; } = float.PositiveInfinity;
    /// <summary>Shrinking away: its time is up.</summary>
    internal bool IsGoing => goneAt >= 0f;
    /// <summary>
    /// It may be taken back now: stuck in a wall or a dead body, or lying (not while it flies, waits for its thrower's word
    /// or rides a body still alive).
    /// </summary>
    internal bool CanBeTaken => goneAt < 0f && !waiting && Now != State.Flying && (!inBody || stuckIn == null || !stuckIn.IsAlive);
    /// <summary>Stuck in something rather than lying loose: taking it back pulls it out.</summary>
    internal bool IsStuck => Now == State.Stuck;
    /// <summary>Where a hand takes it: its handle.</summary>
    internal Vector3 GripPoint => transform.position - transform.forward * (home != null ? home.HalfLength * .5f : .07f);

    private ThrowingKnives home;
    private EnemyHealth stuckIn;
    private bool inBody, waiting, fresh = true;
    private Vector3 velocity, spinAxis, origin, sweepFrom, goneScale = Vector3.one;
    private float flown, tumbled, stillFor, waitUntil, lastClatter = -1f, goneAt = -1f;
    private int clatters;
    private RaycastHit waitHit;
    private Vector3 waitVelocity;
    private Rigidbody body;
    private BoxCollider box;

    /// <summary>
    /// A knife leaving the hand at <paramref name="from"/> at <paramref name="velocity"/>. Its first stretch is checked from
    /// <paramref name="sweepFrom"/> (the thrower's eyes), so a zombie right in their face is met too.
    /// </summary>
    internal static ThrownKnife Make(ThrowingKnives home, uint id, PlayerMovement thrower, bool judges, Vector3 sweepFrom, Vector3 from,
        Vector3 velocity)
    {
        var host = new GameObject("Thrown Knife " + id) { layer = GoreGibs.Layer };
        host.transform.SetParent(home.transform, false);
        host.transform.SetPositionAndRotation(from, ThrowingKnives.LaunchTurn(velocity));
        home.Dress(host.transform);
        var knife = host.AddComponent<ThrownKnife>();
        knife.home = home;
        knife.Id = id;
        knife.Thrower = thrower;
        knife.Judges = judges;
        knife.origin = from;
        knife.sweepFrom = sweepFrom;
        knife.velocity = velocity;
        // End over end, about the throw's own sideways line.
        Vector3 across = Vector3.Cross(Vector3.up, velocity);
        knife.spinAxis = across.sqrMagnitude > 1e-6f ? across.normalized : host.transform.right;
        knife.Now = State.Flying;
        return knife;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || home == null) return;
        if (goneAt >= 0f) { Shrink(); return; }
        if (Now == State.Flying) Fly(dt);
        else if (Now == State.Loose) Tumble(dt);
        if (Now != State.Flying && Time.time - LandedAt > (Now == State.Stuck ? home.stuckLife : home.looseLife)) Go();
    }

    // ---- In the air ----

    private void Fly(float dt)
    {
        if (waiting)
        {
            // A copy waits where it met something for its thrower's word; with none (they left), it settles by itself.
            if (Time.time >= waitUntil) { waiting = false; Settle(waitHit, waitVelocity, false); }
            return;
        }
        Vector3 gravity = Vector3.down * home.gravity;
        Vector3 at = transform.position;
        Vector3 next = at + velocity * dt + .5f * dt * dt * gravity;
        Vector3 start = fresh ? sweepFrom : at;
        fresh = false;
        ThrownSweep.SyncBodies();
        if (ThrownSweep.First(Thrower != null ? Thrower.gameObject : null, start, next, home.hitRadius, ThrowingKnives.Mask, out RaycastHit hit))
        {
            float length = (next - start).magnitude;
            Meet(hit, velocity + gravity * (dt * (length > 1e-5f ? hit.distance / length : 0f)));
            return;
        }
        transform.position = next;
        velocity += gravity * dt;
        transform.rotation = Quaternion.AngleAxis(home.spin * dt, spinAxis) * transform.rotation;
        flown += dt;
        if (flown > LongestFlight || next.y < Bottom) Go();
    }

    private void Meet(in RaycastHit hit, Vector3 hitVelocity)
    {
        if (Judges) { Settle(hit, hitVelocity, true); return; }
        // A copy: it stops there, a little short, still and waiting.
        Vector3 way = hitVelocity.sqrMagnitude > 1e-6f ? hitVelocity.normalized : transform.forward;
        transform.position = hit.point - way * home.HalfLength;
        waiting = true;
        waitUntil = Time.time + WordWait;
        waitHit = hit;
        waitVelocity = hitVelocity;
    }

    // What becomes of it, there. Judged: this is the thrower's PC, which deals the blow and tells the others.
    private void Settle(in RaycastHit hit, Vector3 hitVelocity, bool judged)
    {
        var collider = hit.collider;
        float speed = hitVelocity.magnitude;
        Vector3 way = speed > 1e-4f ? hitVelocity / speed : transform.forward;
        if (collider == null)
        {
            LetGo(transform.position, transform.rotation, Vector3.zero, Vector3.zero);
            if (judged) home.ShareBounce(this, Vector3.zero, Vector3.zero);
            return;
        }
        var receiver = collider.GetComponentInParent<IDamageReceiver>();
        bool flesh = ThrownSweep.IsBody(collider);
        if (receiver != null || flesh)
        {
            // Which bone it goes into, read before the blow: a killing one hands the body over to its corpse.
            Transform bone = ZombieBody.IsHitbox(collider) && collider.transform.parent != null ? collider.transform.parent : collider.transform;
            byte boneIndex = BoneIndex(bone);
            ulong target = receiver is Component owner && owner.TryGetComponent(out NetworkObject network) && network.IsSpawned
                ? network.NetworkObjectId : 0UL;
            var enemy = collider.GetComponentInParent<EnemyHealth>();
            bool fast = speed >= home.minHitSpeed;
            bool landed = fast && (judged ? Strike(hit, receiver, enemy, way) : flesh);
            if (landed)
            {
                Vector3 position = hit.point + way * (home.fleshDepth - home.HalfLength);
                PutInBody(bone, position, Along(way), enemy);
                if (judged) home.ShareBody(this, bone, boneIndex, target);
                return;
            }
            if (flesh)
            {
                Vector3 back = -hitVelocity * .125f;
                Vector3 spin = Random.insideUnitSphere * 15f;
                LetGo(hit.point + hit.normal * Clearance, transform.rotation, back, spin);
                if (judged) home.ShareBounce(this, back, spin);
                return;
            }
            // Something that takes no blow from a player (a barricade): a surface like any other.
        }
        if (Sticks(hit.normal, hitVelocity))
        {
            PutInWall(hit.point + way * (home.wallDepth - home.HalfLength), Along(way), hit.rigidbody != null ? collider.transform : null);
            if (judged) home.ShareWorld(this);
            return;
        }
        Vector3 off = Vector3.Reflect(hitVelocity, hit.normal) * home.bounce;
        Vector3 tumble = Random.insideUnitSphere * 25f;
        LetGo(hit.point + hit.normal * Clearance, transform.rotation, off, tumble);
        if (judged) home.ShareBounce(this, off, tumble);
    }

    // TFA Base's thrown blade: fast enough, and the faster the more slanted it may come in. Its numbers are in units/s,
    // ours in m/s (Tables/ThrowingKnife stickSpeed, stickSlope).
    private bool Sticks(Vector3 normal, Vector3 hitVelocity)
    {
        float speed = hitVelocity.magnitude;
        if (speed <= home.stickSpeed || speed <= 1e-4f) return false;
        return Vector3.Dot(normal, hitVelocity / speed) < -1f + speed * home.stickSlope;
    }

    // The knife's blow, on the thrower's PC: the usual way into whatever takes it. False if it didn't take.
    private bool Strike(in RaycastHit hit, IDamageReceiver receiver, EnemyHealth enemy, Vector3 way)
    {
        if (receiver == null || Thrower == null || (enemy != null && !enemy.IsAlive)) return false;
        bool head = WeaponHitRegion.IsHead(hit);
        float amount = home.damage * (head ? home.headFactor : 1f);
        // A zombie that never knew they were there (calm, alert or searching): a knife in it hits as hard as unawareFactor
        // says, which ends a common one wherever it lands, as a takedown would (Stealth; its kill pays the 암살 bonus).
        if (enemy != null && enemy.TryGetComponent(out ZombieMind mind) && mind.Unaware) amount *= home.unawareFactor;
        var blow = new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, hit.point, way, Thrower.gameObject,
            postureDamage: home.postureDamage, isCritical: head, method: DamageMethod.Thrown, origin: origin);
        if (receiver.ReceiveHit(blow) != DamageResult.Damaged) return false;
        // It staggers what it lands in, cutting off a blow on its way (a copy passes it to the host).
        if (enemy != null && enemy.IsAlive && enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable)
            target.ApplyPostureDamage(home.postureDamage, home.stagger, true);
        return true;
    }

    // The human bone this transform is, on its body's Animator; NoBone for anything else.
    private static byte BoneIndex(Transform bone)
    {
        var animator = bone != null ? bone.GetComponentInParent<Animator>() : null;
        if (animator == null || !animator.isHuman) return NoBone;
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            if (animator.GetBoneTransform((HumanBodyBones)i) == bone) return (byte)i;
        return NoBone;
    }

    // Tip first along the way it came, turned a little about it so no two sit alike.
    private Quaternion Along(Vector3 way)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(way, Vector3.up)) > .98f ? transform.up : Vector3.up;
        return Quaternion.LookRotation(way, up) * Quaternion.Euler(0f, 0f, Random.Range(-35f, 35f));
    }

    // ---- Coming to rest (judged here, or the thrower's word on another PC) ----

    /// <summary>It goes into a body here, riding that bone (none: it stays where it is).</summary>
    internal void PutInBody(Transform bone, Vector3 position, Quaternion rotation, EnemyHealth enemy)
    {
        Still();
        transform.SetParent(home.transform, true);
        transform.SetPositionAndRotation(position, rotation);
        if (bone != null) transform.SetParent(bone, true);
        stuckIn = enemy;
        inBody = true;
        Rest(State.Stuck);
        home.Play(home.Sounds != null ? home.Sounds.hitFlesh : null, position + transform.forward * home.HalfLength, .9f);
    }

    /// <summary>It sticks into a wall here, riding <paramref name="moving"/> if that moves (a door, a prop).</summary>
    internal void PutInWall(Vector3 position, Quaternion rotation, Transform moving = null)
    {
        Still();
        transform.SetParent(moving != null ? moving : home.transform, true);
        transform.SetPositionAndRotation(position, rotation);
        stuckIn = null;
        inBody = false;
        Rest(State.Stuck);
        Vector3 tip = position + transform.forward * home.HalfLength;
        home.Play(home.Sounds != null ? home.Sounds.hitWorld : null, tip, .85f);
        // A thunk in the wall: little enough noise, but zombies near hear it (and look there).
        Noise.Make(tip, home.wallNoise, Thrower != null ? Thrower.gameObject : null);
    }

    /// <summary>It glanced off here: it tumbles from this pose at this speed and spin, then lies.</summary>
    internal void LetGo(Vector3 position, Quaternion rotation, Vector3 linear, Vector3 angular)
    {
        waiting = false;
        stuckIn = null;
        inBody = false;
        transform.SetParent(home.transform, true);
        transform.SetPositionAndRotation(position, rotation);
        Loosen(linear, angular);
        bool knocked = linear.sqrMagnitude > ClatterSpeed * ClatterSpeed;
        if (!knocked) return;
        home.Play(home.Sounds != null ? home.Sounds.hitWorld : null, position, .55f, Random.Range(1.05f, 1.2f));
        Noise.Make(position, home.bounceNoise, Thrower != null ? Thrower.gameObject : null);
    }

    /// <summary>The thrower's word: it lies still here.</summary>
    internal void LayDown(Vector3 position, Quaternion rotation)
    {
        waiting = false;
        if (Now != State.Loose && Now != State.Lying) { stuckIn = null; inBody = false; }
        transform.SetParent(home.transform, true);
        if (body != null) { body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; body.isKinematic = true; }
        transform.SetPositionAndRotation(position, rotation);
        if (Now != State.Loose && Now != State.Lying) LandedAt = Time.time;
        Now = State.Lying;
        home.Landed(this);
    }

    private void Rest(State state)
    {
        Now = state;
        LandedAt = Time.time;
        home.Landed(this);
    }

    // Out of the air and off physics.
    private void Still()
    {
        waiting = false;
        if (body != null) { body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; body.isKinematic = true; }
        if (box != null) box.enabled = false;
    }

    // Under physics: a small box that players pass through, as a dropped magazine's.
    private void Loosen(Vector3 linear, Vector3 angular)
    {
        if (box == null)
        {
            box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(BoxThickness, BoxWidth, Mathf.Max(.05f, home.length));
            blades.Add(box);
            foreach (var player in PlayerRoster.All)
            {
                if (player == null) continue;
                foreach (var part in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(box, part, true);
            }
        }
        box.enabled = true;
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = .25f;
            body.linearDamping = .05f;
            body.angularDamping = .5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = linear;
        body.angularVelocity = angular;
        tumbled = stillFor = 0f;
        clatters = 0;
        Now = State.Loose;
        LandedAt = Time.time;
        home.Landed(this);
    }

    private void Tumble(float dt)
    {
        if (body == null) { Lie(); return; }
        tumbled += dt;
        if (transform.position.y < Bottom) { Go(); return; }
        bool still = body.linearVelocity.magnitude < StillSpeed && body.angularVelocity.magnitude < StillSpin;
        stillFor = still ? stillFor + dt : 0f;
        if (stillFor < StillFor && tumbled < LongestTumble && !body.IsSleeping()) return;
        Lie();
        // The others lay their copy down where this one lies.
        if (Judges) home.ShareRest(this);
    }

    private void Lie()
    {
        if (body != null) { body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; body.isKinematic = true; }
        Now = State.Lying;
    }

    private void OnCollisionEnter(Collision hit)
    {
        if (Now != State.Loose || clatters >= MostClatters || Time.time - lastClatter < ClatterGap || home == null) return;
        float speed = hit.relativeVelocity.magnitude;
        if (speed < ClatterSpeed) return;
        clatters++;
        lastClatter = Time.time;
        Vector3 point = hit.contactCount > 0 ? hit.GetContact(0).point : transform.position;
        home.Play(home.Sounds != null ? home.Sounds.hitWorld : null, point, Mathf.Lerp(.15f, .45f, Mathf.InverseLerp(ClatterSpeed, 8f, speed)),
            Random.Range(1.1f, 1.3f));
    }

    // ---- Going ----

    /// <summary>Its time is up (or too many lie about): it shrinks away.</summary>
    internal void Go()
    {
        if (goneAt >= 0f) return;
        goneAt = Time.time;
        // As it is now: under a bone its local scale makes up for the bone's.
        goneScale = transform.localScale;
        Still();
    }

    /// <summary>Someone took it back: gone at once.</summary>
    internal void Taken() => Destroy(gameObject);

    private void Shrink()
    {
        float share = Mathf.Clamp01((Time.time - goneAt) / GoSeconds);
        transform.localScale = goneScale * (1f - share);
        if (share >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (!ReferenceEquals(box, null)) blades.Remove(box);
        if (home != null) home.Forget(this);
    }
}
