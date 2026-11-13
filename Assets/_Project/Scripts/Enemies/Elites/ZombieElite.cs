using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What the memo's elites (2026-11-11: ZombieNinja, ZombieCandyman, ZombieBreadbag) share. ZombieType puts the one its
/// kind needs on a zombie when it becomes that kind; it does nothing while the kind is another (a copy's first roll). It
/// tells the chase (EnemyChaseAI) when it moves the body itself (Driving), chooses the attack its kind makes and hears how
/// that attack went (EnemyCombatAI: Choose, Struck, Parried, Blocked, GuardableBy), carries its light (EliteLight) and
/// what it holds (EliteLooks' props, or a few boxes standing in), and the first time this PC's player comes near one of
/// its kind, says on the alert band what it does and how to beat it (the user's rule: an effect comes with its reason).
/// </summary>
public abstract class ZombieElite : MonoBehaviour
{
    // How near this PC's player has to come before the first sight of a kind is told (m), and how long its mark stays (s).
    private const float TellWithin = 32f, TellMark = 6f;
    private static readonly HashSet<ZombieKind> told = new HashSet<ZombieKind>();
    private static readonly Dictionary<Color, Material> paints = new Dictionary<Color, Material>();
    private static Mesh cube;

    protected NavMeshAgent agent;
    protected EnemyHealth health;
    protected EnemyNetwork network;
    protected EnemyTargeting targeting;
    protected MeleeCombatTarget melee;
    protected ZombieMind mind;
    protected ZombieType type;
    protected EnemyCombatAI combat;
    protected ZombieGrip grip;
    protected ZombieBody body;
    private EliteLight marker;
    private bool dressed;
    private int sightMask;

    /// <summary>The kind this part plays.</summary>
    protected abstract ZombieKind Kind { get; }
    /// <summary>It moves its body itself just now: the chase waits (EnemyChaseAI) and so does the swing (EnemyCombatAI).</summary>
    public virtual bool Driving => false;
    /// <summary>
    /// It starts no attack of the usual kind, though it chases on (a ninja strikes only out of its smoke, ZombieNinja): the
    /// swing waits (EnemyCombatAI) while the chase goes on. An attack it begins itself (BeginSpecial) still runs.
    /// </summary>
    public virtual bool HoldsSwing => false;
    /// <summary>It is this kind (and not crawling on a lost leg): otherwise it does nothing.</summary>
    protected bool Mine => type != null && type.Kind == Kind && !type.Crawls;
    /// <summary>This PC runs the zombie (the host, or alone); a copy only shows it.</summary>
    protected bool Runs => network == null || !network.IsCopy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        told.Clear();
        paints.Clear();
    }

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        targeting = GetComponent<EnemyTargeting>();
        melee = GetComponent<MeleeCombatTarget>();
        mind = GetComponent<ZombieMind>();
        type = GetComponent<ZombieType>();
        combat = GetComponent<EnemyCombatAI>();
        grip = GetComponent<ZombieGrip>();
        body = GetComponent<ZombieBody>();
        int enemy = LayerMask.NameToLayer("Enemy");
        sightMask = ~((enemy >= 0 ? 1 << enemy : 0) | 1 << 31);
        if (TryGetComponent(out EnemyChaseAI chase)) chase.Elite = this;
        if (combat != null) combat.Elite = this;
    }

    protected virtual void OnDestroy() => marker?.Remove();

    protected virtual void OnDisable() => marker?.Hide();

    // ---- What EnemyCombatAI asks and tells ----

    /// <summary>The attack it makes at this player now (null: the usual choice). Level: on its floor, so a grab can reach.</summary>
    internal virtual AttackMove? Choose(PlayerMovement target, bool level) => null;
    /// <summary>Its windup came to its end on this PC (every PC alike: the host's own, a copy's timed from the host's word).</summary>
    internal virtual void Struck(AttackMove move) { }
    /// <summary>This PC's player parried its blow.</summary>
    internal virtual void Parried(PlayerMovement by) { }
    /// <summary>This PC's player guarded against its blow without a parry.</summary>
    internal virtual void Blocked(PlayerMovement by) { }
    /// <summary>Whether this blow can be guarded or parried by the player it lands on, judged on their PC.</summary>
    internal virtual bool GuardableBy(PlayerMovement victim, AttackMove move) => true;
    /// <summary>The windup of a move only its kind makes (s); 0 for the usual.</summary>
    internal virtual float WindupOf(AttackMove move) => 0f;
    /// <summary>Its attack was called off (stunned, the host's word to a copy): what runs on after a windup stops too.</summary>
    internal virtual void Cancelled() { }

    // ---- Shared ----

    /// <summary>On its feet, alive, free, awake, and not held for an execution: it may start what its kind does.</summary>
    protected bool CanAct()
    {
        if (health == null || !health.IsAlive || health.WavePaused) return false;
        // Awake (rousing counts: a windup may rouse it) but not while its calm pose still shows: it gets up first.
        if (mind != null && (!mind.IsAwake || mind.GettingUp)) return false;
        if (melee != null && (melee.IsStunned || melee.IsLying)) return false;
        if (PlayerExecution.Suppresses(this)) return false;
        return agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
    }

    protected Vector3 Feet => agent != null ? transform.position - Vector3.up * agent.baseOffset : transform.position;
    internal static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);

    /// <summary>Nothing solid between its eyes and the player's chest.</summary>
    protected bool Sees(PlayerMovement player)
        => player != null && !Physics.Linecast(Feet + Vector3.up * 1.6f, player.transform.position + Vector3.up, sightMask, QueryTriggerInteraction.Ignore);

    protected int WorldMask => sightMask;

    protected void Halt()
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
    }

    /// <summary>
    /// Every frame on every PC (LateUpdate of the kind): its light, what it holds once its body is on, and the first
    /// sight told. Returns false when it isn't this kind or is dead (then nothing of it shows).
    /// </summary>
    protected bool ShowElite(Color glow, float size, HumanBodyBones bone, HumanBodyBones otherwise)
    {
        bool alive = Mine && health != null && health.IsAlive;
        if (!alive) { marker?.Hide(); return false; }
        if (marker == null) marker = new EliteLight(this, bone, otherwise);
        marker.Show(glow, size);
        if (!dressed && body != null && body.Model != null) { dressed = true; Dress(body.Model); }
        Tell();
        return true;
    }

    protected void HideLight() => marker?.Hide();

    // Its props on its bones: EliteLooks' own, else the kind's placeholder (BuildHand, BuildHead).
    private void Dress(GameObject model)
    {
        var animator = model.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        var look = EliteLooks.For(Kind);
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        Quaternion facing = Quaternion.LookRotation(Flat(model.transform.forward).normalized, Vector3.up);
        if (hand != null)
        {
            var holder = Holder("Elite Hand", hand, hand.position, facing);
            if (look.handProp != null) Instantiate(look.handProp, holder, false);
            else BuildHand(holder);
            Strip(holder.gameObject, model.layer);
        }
        if (head != null)
        {
            var holder = Holder("Elite Head", head, head.position, facing);
            if (look.headProp != null) Instantiate(look.headProp, holder, false);
            // A body of its own is its look already (the reaper's firefighter): no box hood over its helmet.
            else if (look.character == null) BuildHead(holder);
            Strip(holder.gameObject, model.layer);
        }
    }

    /// <summary>The placeholder for its right hand, built along +Z from the grip (holder's space, m).</summary>
    protected virtual void BuildHand(Transform holder) { }
    /// <summary>The placeholder for its head, built round the head bone (holder's space, m).</summary>
    protected virtual void BuildHead(Transform holder) { }

    // An empty to hang a prop on, on the bone, facing the body's way as it stands; sized in metres whatever the rig's scale.
    private static Transform Holder(string name, Transform bone, Vector3 at, Quaternion facing)
    {
        var holder = new GameObject(name).transform;
        holder.SetParent(bone, false);
        holder.SetPositionAndRotation(at, facing);
        Vector3 scale = bone.lossyScale;
        holder.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(scale.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(scale.y)),
            1f / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
        return holder;
    }

    // Props never block or take anything: no colliders, the body's layer, no shadows from the small bits.
    private static void Strip(GameObject prop, int layer)
    {
        foreach (var collider in prop.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        foreach (var part in prop.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
    }

    /// <summary>A box of a placeholder: centre and size in the holder's space (m), turned by euler, painted plain.</summary>
    protected static GameObject Box(Transform holder, Vector3 center, Vector3 size, Color color, Vector3 euler = default, float shine = .2f)
    {
        var box = new GameObject("Box");
        box.transform.SetParent(holder, false);
        box.transform.localPosition = center;
        box.transform.localRotation = Quaternion.Euler(euler);
        box.transform.localScale = size;
        box.AddComponent<MeshFilter>().sharedMesh = Cube();
        var renderer = box.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Paint(color, shine);
        renderer.shadowCastingMode = size.x * size.y * size.z < .001f ? UnityEngine.Rendering.ShadowCastingMode.Off
            : UnityEngine.Rendering.ShadowCastingMode.On;
        return box;
    }

    private static Mesh Cube()
    {
        if (cube != null) return cube;
        cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (cube == null)
        {
            var made = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = made.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(made);
        }
        return cube;
    }

    private static Material Paint(Color color, float shine)
    {
        var key = new Color(color.r, color.g, color.b, shine);
        if (paints.TryGetValue(key, out var made) && made != null) return made;
        var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        made = new Material(lit) { name = "Elite Prop", hideFlags = HideFlags.DontSave };
        if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", color);
        if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", shine);
        if (made.HasProperty("_Metallic")) made.SetFloat("_Metallic", shine > .5f ? .8f : 0f);
        paints[key] = made;
        return made;
    }

    // The first time this PC's player comes near one of its kind this session: its name and what it does, on the band.
    private void Tell()
    {
        if (told.Contains(Kind)) return;
        var mine = PlayerRoster.Local;
        if (mine == null || (mine.transform.position - transform.position).sqrMagnitude > TellWithin * TellWithin) return;
        told.Add(Kind);
        AlertHUD.Danger(Texts.Get("hud.alert." + Kind.ToString().ToLowerInvariant()), transform, TellMark,
            () => health == null || !health.IsAlive);
        // The player shouts its name for the team too (PlayerCallouts; Settings → 저절로 외치기).
        PlayerCallouts.Spotted(health);
    }

    /// <summary>The clip, or the fallback when it is none (or a reference gone missing).</summary>
    protected static AudioClip Or(AudioClip clip, AudioClip fallback) => clip != null ? clip : fallback;

    /// <summary>A sound where it stands, heard by everyone near, through the Combat volume.</summary>
    protected static void PlayAt(AudioClip clip, Vector3 at, float volume, float near = 2f, float far = 40f)
    {
        if (clip != null) GameAudio.PlayAt(clip, at, AudioChannel.Combat, volume, near, far);
    }

    /// <summary>A looping sound that rides on it (an engine, a swarm), started silent; its volume is set each frame.</summary>
    protected AudioSource Loop(AudioClip clip, float near, float far)
    {
        if (clip == null) return null;
        var host = new GameObject("Elite Loop");
        host.transform.SetParent(transform, false);
        host.transform.localPosition = Vector3.up * 1.2f;
        var source = host.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = near;
        source.maxDistance = far;
        source.dopplerLevel = 0f;
        source.volume = 0f;
        GameAudio.Bind(source, AudioChannel.Combat);
        source.Play();
        return source;
    }

    /// <summary>A puff of smoke: soft grey blobs bursting out and fading (the ninja's vanish and arrival).</summary>
    protected static void Puff(Vector3 at, Color color, float size, int count)
    {
        var host = new GameObject("Elite Smoke");
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Effects, "World");
        host.transform.position = at;
        var system = host.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.duration = .1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.55f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.gravityModifier = -.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * .35f;
        var growth = system.sizeOverLifetime;
        growth.enabled = true;
        growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 1.6f));
        var fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.8f, .25f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var slow = system.limitVelocityOverLifetime;
        slow.enabled = true;
        slow.dampen = .35f;
        slow.limit = .2f;
        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, false);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        system.Play();
    }
}
