using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The candyman, an elite from the user's memo of 2026-11-11 (item 25: "The Candyman: 아군 납치 기믹"). A tall zombie with a
/// hook for a hand and bees about it (heard before it is seen). Its reach is a hook (AttackMove.Grab every time it can;
/// a swipe of the hook when it can't): hooked, a player is dragged off behind it at dragSpeed, away from their friends
/// (PlayerHold's Drag: their own PC pulls them along, the hook in their flesh hurting hookDamage a second). Friends
/// chase it and shoot it (breakDamage from anyone makes it drop them) or knock it down or shove it (any stun does);
/// alone, mashing the shove key tears free (struggleStep a press), and either way it reels a moment. Going down ends it
/// too. It hooks again only after regrabAfter. The hold is the same one shamblers use (ZombieGrip, HoldKind.Drag), so
/// the other PCs see it holding, through walls (HoldReveal), with the hook's chain drawn to whom it drags.
/// Numbers: Tables/ZombieCandyman; its kind's row in ZombieKinds.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieCandyman : ZombieElite
{
    public const string TableName = "ZombieCandyman", TableRow = "Default";
    private const int Rate = 44100;
    private static AudioClip buzz;

    [Header("Drag")]
    [SerializeField, Min(.3f), Tooltip("How fast it drags someone off (m/s): slower than a run, so friends catch it.")]
    private float dragSpeed = 2.4f;
    [SerializeField, Min(2f), Tooltip("It makes for a place this far off (m), away from the others.")]
    private float dragFar = 22f;
    [SerializeField, Min(0f), Tooltip("After a drag ends, how long before it hooks again (s); meanwhile it swipes.")]
    private float regrabAfter = 6f;
    [SerializeField, Min(.3f), Tooltip("How far behind it the one it drags is pulled along (m).")]
    private float gap = 1.05f;
    [SerializeField, Min(.5f), Tooltip("How fast they may be pulled after it (m/s): faster than it walks, so they keep up.")]
    private float follow = 6.5f;
    [Header("Breaking free")]
    [SerializeField, Min(0f), Tooltip("Damage a second the hook does while it drags someone (the hook in their flesh).")]
    private float hookDamage = 6f;
    [SerializeField, Range(.02f, 1f), Tooltip("Each press of the shove key while dragged frees them this much (1 = free).")]
    private float struggleStep = .2f;
    [SerializeField, Min(0f), Tooltip("Damage from anyone during a drag that makes it drop them.")]
    private float breakDamage = 140f;
    [SerializeField, Min(0f), Tooltip("Torn free or made to drop them, it reels this long (s).")]
    private float breakStun = 1.4f;
    [Header("Look")]
    [SerializeField] private Color glowColor = new Color(1f, .36f, .62f);
    [SerializeField, Min(.05f)] private float glowSize = .36f;
    [SerializeField, Range(0f, 1f), Tooltip("The bees' volume about it.")]
    private float buzzVolume = .4f;
    [SerializeField] private Color chainColor = new Color(.22f, .2f, .2f);

    private float grabReadyAt, steerAt, walkSpeed = -1f;
    private bool wasDragging;
    private AudioSource bees;
    private LineRenderer chain;
    private Transform hookTip;

    protected override ZombieKind Kind => ZombieKind.Candyman;
    /// <summary>Dragging someone off, it walks itself (the chase waits).</summary>
    public override bool Driving => Mine && grip != null && grip.IsHolding && grip.Dragging;

    /// <summary>Hook damage a second (PlayerHold, on the dragged player's PC).</summary>
    public float HookDamage => hookDamage;
    /// <summary>A shove-key press's share of breaking free.</summary>
    public float StruggleStep => struggleStep;
    /// <summary>Damage from anyone that makes it drop them.</summary>
    public float BreakDamage => breakDamage;
    /// <summary>How long it reels once they are free (s).</summary>
    public float BreakStun => breakStun;
    /// <summary>How far behind it they are pulled (m), and how fast at most (m/s).</summary>
    public float Gap => gap;
    public float Follow => follow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => buzz = null;

    protected override void Awake()
    {
        base.Awake();
        GameTables.Bind(TableName, TableRow, this);
        if (grip == null) grip = GetComponent<ZombieGrip>();
    }

    // Its hook every time a hook can take them (on its floor, not climbing), but not again too soon after a drag.
    internal override AttackMove? Choose(PlayerMovement target, bool level)
    {
        if (target == null || target.IsDowned) return null;
        return level && !target.IsParkour && Time.time >= grabReadyAt ? AttackMove.Grab : AttackMove.Swing;
    }

    // ---- The host: dragging off ----

    private void Update()
    {
        if (grip == null) grip = GetComponent<ZombieGrip>();
        if (!Mine || !Runs || grip == null) return;
        bool dragging = grip.IsHolding && grip.Dragging && health != null && health.IsAlive;
        if (dragging && !wasDragging)
        {
            if (walkSpeed < 0f && agent != null) walkSpeed = agent.speed;
            steerAt = 0f;
        }
        if (!dragging && wasDragging)
        {
            grabReadyAt = Time.time + regrabAfter;
            if (agent != null && walkSpeed > 0f) agent.speed = walkSpeed;
            walkSpeed = -1f;
        }
        wasDragging = dragging;
        if (!dragging || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (melee != null && (melee.IsStunned || melee.IsLying)) return;
        agent.speed = dragSpeed;
        agent.isStopped = false;
        if (Time.time < steerAt) return;
        steerAt = Time.time + .8f;
        if (TryAway(out Vector3 away)) agent.SetDestination(away);
    }

    // A place dragFar off, away from the dragged player's friends (or on along its way with none about).
    private bool TryAway(out Vector3 place)
    {
        place = default;
        Vector3 from = transform.position, sum = Vector3.zero;
        int count = 0;
        foreach (var player in PlayerRoster.All)
        {
            if (player == null || player.IsDead || player == grip.Held) continue;
            sum += player.transform.position;
            count++;
        }
        Vector3 way = count > 0 ? Flat(from - sum / count) : Flat(transform.forward);
        if (way.sqrMagnitude < .01f) way = Flat(transform.forward);
        way.Normalize();
        foreach (float turn in new[] { 0f, 30f, -30f, 60f, -60f, 100f, -100f })
        {
            Vector3 spot = from + Quaternion.Euler(0f, turn, 0f) * way * dragFar;
            if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 4f, NavMesh.AllAreas)) { place = hit.position; return true; }
        }
        return false;
    }

    // ---- Every PC: its light, the bees, the hook's chain ----

    private void LateUpdate()
    {
        if (!ShowElite(glowColor, glowSize, HumanBodyBones.Head, HumanBodyBones.Neck))
        {
            if (bees != null) bees.volume = 0f;
            if (chain != null) chain.enabled = false;
            return;
        }
        if (bees == null) bees = Loop(Buzz(), 2f, 30f);
        bool dragging = grip != null && grip.IsHolding && grip.Dragging;
        if (bees != null) bees.volume = Mathf.MoveTowards(bees.volume, buzzVolume * (dragging ? 1.4f : 1f), Time.deltaTime * 2f);
        Chain(dragging);
    }

    // The chain from the hook to whom it drags: the one it holds where that is known, else the player in a zombie's grip
    // nearest it (a copy knows only that it holds someone).
    private void Chain(bool dragging)
    {
        PlayerMovement victim = null;
        if (dragging)
        {
            victim = grip.Held;
            if (victim == null)
            {
                float best = 16f;
                foreach (var player in PlayerRoster.All)
                {
                    if (player == null || !player.InZombieGrip) continue;
                    float distance = (player.transform.position - transform.position).sqrMagnitude;
                    if (distance < best) { best = distance; victim = player; }
                }
            }
        }
        if (victim == null) { if (chain != null) chain.enabled = false; return; }
        if (chain == null)
        {
            var host = new GameObject("Candyman Chain");
            host.transform.SetParent(transform, false);
            chain = host.AddComponent<LineRenderer>();
            chain.useWorldSpace = true;
            chain.positionCount = 2;
            chain.widthMultiplier = .025f;
            chain.numCapVertices = 2;
            chain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            chain.receiveShadows = false;
            chain.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
            chain.startColor = chain.endColor = chainColor;
        }
        chain.enabled = true;
        Vector3 from = hookTip != null ? hookTip.position : transform.position + Vector3.up * 1.2f;
        chain.SetPosition(0, from);
        chain.SetPosition(1, victim.transform.position + Vector3.up * 1.1f);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (chain != null) Destroy(chain.gameObject);
    }

    protected override void BuildHand(Transform holder)
    {
        // A meat hook: a wooden grip, an iron shaft along +Z and the hook curling down at its end.
        var iron = new Color(.2f, .19f, .19f);
        Box(holder, new Vector3(0f, 0f, -.02f), new Vector3(.04f, .04f, .13f), new Color(.3f, .2f, .12f));
        Box(holder, new Vector3(0f, 0f, .16f), new Vector3(.024f, .024f, .26f), iron, default, .6f);
        Box(holder, new Vector3(0f, -.05f, .3f), new Vector3(.024f, .11f, .024f), iron, default, .6f);
        hookTip = Box(holder, new Vector3(0f, -.1f, .26f), new Vector3(.022f, .022f, .09f), iron, new Vector3(-30f, 0f, 0f), .6f).transform;
    }

    // A swarm of bees: a handful of buzzing voices, each wavering on its own, looped seamlessly (every rate a whole
    // number of cycles over the loop).
    private static AudioClip Buzz()
    {
        if (buzz != null) return buzz;
        const float Length = 2f;
        var looks = EliteLooks.Instance;
        if (looks != null && looks.candymanBuzz != null) return buzz = looks.candymanBuzz;
        var data = new float[(int)(Rate * Length)];
        float[] pitches = { 196f, 211.5f, 224f, 236.5f, 248f, 259.5f };
        float[] wobbles = { 3.5f, 5f, 4f, 6.5f, 3f, 5.5f };
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float sample = 0f;
            for (int b = 0; b < pitches.Length; b++)
            {
                float phase = pitches[b] * t + .8f * Mathf.Sin(2f * Mathf.PI * wobbles[b] * t + b);
                float saw = 2f * (phase - Mathf.Floor(phase)) - 1f;
                float swell = .55f + .45f * Mathf.Sin(2f * Mathf.PI * ((b % 3 + 1) * .5f) * t + b * 1.7f);
                sample += saw * swell;
            }
            data[i] = sample;
        }
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        if (top > 0f) for (int i = 0; i < data.Length; i++) data[i] *= .5f / top;
        buzz = AudioClip.Create("Candyman Bees", data.Length, 1, Rate, false);
        buzz.SetData(data, 0);
        buzz.hideFlags = HideFlags.HideAndDontSave;
        return buzz;
    }
}
