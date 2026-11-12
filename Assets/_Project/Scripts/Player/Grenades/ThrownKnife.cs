using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Knight's throwing knife (Throwable.Knife; the user, 2026-11-12: "나이트: throwing knife"). It leaves the hand fast
/// and flies nearly straight (a share of gravity), turning end over end, and strikes the first thing on its line as a
/// bullet does (AttackTrace: the zombie's bone hitboxes, its head for a headshot, which ends a normal zombie at once; the
/// part it meets is gore's to cut). It goes in point first and stays there: in a zombie it rides the bone it hit, and
/// drops to the floor when that zombie falls; in a wall it stays in the wall. The Knight takes it back by walking up to it
/// (GrenadeRules pickupReach): one more in the belt, as Horde's and MW's thrown knives come back. On the thrower's PC it
/// deals the hit; a friend's copy flies the same line, sticks where it meets something and goes when the thrower takes
/// theirs back (PlayerGrenades). A knife left lies for knifeLife seconds, then is gone.
/// </summary>
[DisallowMultipleComponent]
public sealed class ThrownKnife : MonoBehaviour
{
    // The longest flight (s), how deep its point goes in (m: its middle stops this far short of the hit, the point being
    // 0.12 m ahead of its middle), and its glint. How fast it turns and how long its streak is are GrenadeRules
    // knifeSpin and knifeTrail (as GMod's [MW] Quick Throwing Knife: 3000 degrees a second, a white streak of 0.3 s).
    private const float Flight = 3f, Bury = .06f, TipAhead = .12f, GlintSize = .16f, GlintEvery = 1.4f;
    private static readonly List<ThrownKnife> knives = new List<ThrownKnife>();
    private enum State { Flying, Stuck, Lying }

    private State state;
    private PlayerMovement thrower;
    private bool mine;
    private Vector3 velocity, origin;
    private float gravityShare, damage, headShare, age, life, sinceLanded, spin;
    private Transform look, glint;
    private TrailRenderer streak;
    // Riding a zombie's bone: the bone, the zombie, and where on the bone.
    private Transform bone;
    private EnemyHealth stuckIn;
    private Vector3 onBone;
    private Quaternion turnOnBone;

    internal ushort Id { get; private set; }
    internal bool Mine => mine;
    /// <summary>
    /// How hard a knife's hit jolts its zombie, as a gun's flinch multiplier does a shot's (EnemyHitReaction: the shove
    /// back, the slow, the stagger of a headshot): GrenadeRules knifeFlinch.
    /// </summary>
    internal static float Flinch => Mathf.Max(0f, Throwables.Rule("knifeFlinch", 1.5f));
    /// <summary>It has come to rest (in a body, a wall or on the floor) and can be taken back.</summary>
    internal bool Landed => state != State.Flying;
    internal static IReadOnlyList<ThrownKnife> All => knives;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => knives.Clear();

    /// <summary>Throws one from the hand at this velocity. mine: this PC's player threw it, and its hit is theirs to land.</summary>
    internal static ThrownKnife Throw(ushort id, Vector3 from, Vector3 velocity, PlayerMovement thrower, bool mine)
    {
        var info = Throwables.Of(Throwable.Knife);
        var host = new GameObject("Thrown Knife") { layer = GoreGibs.Layer };
        host.transform.SetPositionAndRotation(from, Quaternion.LookRotation(velocity.sqrMagnitude > 1e-4f ? velocity : Vector3.forward));
        var knife = host.AddComponent<ThrownKnife>();
        knife.Id = id;
        knife.thrower = thrower;
        knife.mine = mine;
        knife.velocity = velocity;
        knife.origin = from;
        knife.gravityShare = info != null ? info.Gravity : .2f;
        knife.damage = info != null ? Mathf.Max(1f, info.Damage) : 80f;
        knife.headShare = info != null && info.Damage > 0f ? Mathf.Max(1f, info.Head / info.Damage) : 4f;
        knife.life = Mathf.Max(5f, Throwables.Rule("knifeLife", 150f));
        knife.spin = Throwables.Rule("knifeSpin", 3000f);
        var spinner = new GameObject("Spin").transform;
        spinner.SetParent(host.transform, false);
        knife.look = spinner;
        Throwables.Dress(Throwable.Knife, spinner);
        knife.Streak();
        knives.Add(knife);
        return knife;
    }

    // A thin white streak behind it in flight, fading as it goes (the addon's sprite trail), gone once it lands.
    private void Streak()
    {
        float seconds = Throwables.Rule("knifeTrail", .3f);
        if (seconds <= 0f) return;
        streak = gameObject.AddComponent<TrailRenderer>();
        streak.time = seconds;
        streak.startWidth = .02f;
        streak.endWidth = 0f;
        streak.minVertexDistance = .05f;
        streak.numCapVertices = 2;
        streak.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        streak.receiveShadows = false;
        streak.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, true);
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(.55f, 0f), new GradientAlphaKey(0f, 1f) });
        streak.colorGradient = fade;
    }

    private void StopStreak()
    {
        if (streak != null) streak.emitting = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        switch (state)
        {
            case State.Flying: Fly(dt); break;
            case State.Stuck: Ride(); break;
            case State.Lying: break;
        }
        if (state != State.Flying)
        {
            sinceLanded += dt;
            Glint();
            if (sinceLanded >= life) Destroy(gameObject);
        }
    }

    private void Fly(float dt)
    {
        Vector3 gravity = Physics.gravity * gravityShare;
        Vector3 step = velocity * dt + .5f * dt * dt * gravity;
        float length = step.magnitude;
        if (length > 1e-5f)
        {
            Vector3 way = step / length;
            var source = thrower != null ? thrower.gameObject : null;
            // The thrower's PC lands the hit, as a bullet: headshots by the head hitbox (head damage over body damage).
            if (mine)
            {
                var attack = new DamageHit(DamageHit.NewAttackId(), damage, DamageKind.Attack, transform.position, way, source,
                    method: DamageMethod.Thrown, origin: origin);
                var struck = AttackTrace.Trace(attack, transform.position, way, length, ~0, headShare);
                if (struck.HasHit) { Stick(struck.Hit, way, struck.IsHeadshot); return; }
            }
            else if (AttackTrace.FirstExternalHit(source, transform.position, way, length, ~0, out RaycastHit met))
            {
                Stick(met, way, false);
                return;
            }
            transform.position += step;
            transform.rotation = Quaternion.LookRotation(way);
        }
        velocity += gravity * dt;
        // End over end about its flat side, the point forward when it lands (Stick turns it so).
        if (look != null) look.localRotation = Quaternion.Euler(age * spin, 0f, 0f);
        if (age >= Flight) Drop();
    }

    // In point first where it met something: a zombie's bone carries it, a wall keeps it; a player it met only falls at
    // their feet (a knife stuck in a friend reads wrong).
    private void Stick(RaycastHit hit, Vector3 way, bool headshot)
    {
        state = State.Stuck;
        StopStreak();
        if (look != null) look.localRotation = Quaternion.identity;
        transform.rotation = Quaternion.LookRotation(way);
        transform.position = hit.point - way * (TipAhead - Bury);
        var collider = hit.collider;
        var enemy = collider != null ? collider.GetComponentInParent<EnemyHealth>() : null;
        if (collider != null && collider.GetComponentInParent<PlayerMovement>() != null)
        {
            Throwables.Play("KnifeWall", hit.point, .7f, 1.5f, 25f);
            Drop();
            return;
        }
        if (enemy != null)
        {
            bone = collider.transform;
            stuckIn = enemy;
            onBone = bone.InverseTransformPoint(transform.position);
            turnOnBone = Quaternion.Inverse(bone.rotation) * transform.rotation;
            Throwables.Play("KnifeFlesh", hit.point, headshot ? 1f : .85f, 1.5f, 30f);
            return;
        }
        Throwables.Play("KnifeWall", hit.point, .85f, 1.5f, 35f);
    }

    // Riding its bone; the zombie gone or its part cut away, it drops to the floor below.
    private void Ride()
    {
        if (bone == null || stuckIn == null || !stuckIn.IsAlive || !bone.gameObject.activeInHierarchy)
        {
            if (bone != null || stuckIn != null) Drop();
            return;
        }
        transform.SetPositionAndRotation(bone.TransformPoint(onBone), bone.rotation * turnOnBone);
    }

    // Down to the floor under it, lying flat on its side.
    private void Drop()
    {
        state = State.Lying;
        StopStreak();
        bone = null;
        stuckIn = null;
        if (look != null) look.localRotation = Quaternion.identity;
        Vector3 from = transform.position + Vector3.up * .3f;
        Vector3 floor = Physics.Raycast(from, Vector3.down, out RaycastHit ground, 6f, Throwables.WorldMask, QueryTriggerInteraction.Ignore)
            ? ground.point + Vector3.up * .025f : transform.position;
        Vector3 along = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (along.sqrMagnitude < 1e-4f) along = Vector3.forward;
        // Flat on the floor: the point along it, a flat face up (the knife's flat faces are its ±X).
        transform.SetPositionAndRotation(floor, Quaternion.LookRotation(along.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, 90f));
    }

    // A small glint now and then on the thrower's own screen, so a Knight finds their knives again.
    private void Glint()
    {
        if (!mine) return;
        if (glint == null)
        {
            var quad = new GameObject("Glint");
            quad.transform.SetParent(transform, false);
            quad.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.Glint, true);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            glint = quad.transform;
        }
        float phase = Mathf.Repeat(sinceLanded, GlintEvery) / GlintEvery;
        float pulse = phase < .25f ? Mathf.Sin(phase / .25f * Mathf.PI) : 0f;
        glint.gameObject.SetActive(pulse > .01f);
        if (pulse <= .01f) return;
        var eye = Camera.main;
        if (eye != null) glint.rotation = eye.transform.rotation;
        glint.position = transform.position + Vector3.up * .02f;
        glint.localScale = Vector3.one * GlintSize * pulse;
    }

    /// <summary>Taken back (by its thrower) or gone: it leaves the world.</summary>
    internal void Take() => Destroy(gameObject);

    private void OnDestroy() => knives.Remove(this);
}
