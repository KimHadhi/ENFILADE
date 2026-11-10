using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage C-16, the first of the elites (user, 2026-11-10: "a charger and an exploder for now, more later"): the
/// exploder. One of the pack, a little bloated, with a light burning in its chest that every PC sees from afar, so it
/// can be picked out as a runner is by its eyes. It doesn't claw: it walks into the players, and within fuseRange of
/// one its fuse runs, the light beating faster and its body flashing, until after fuseSeconds it bursts. It bursts
/// when it dies too, however it dies. Shot from afar it takes the pack round it along (zombies in reach are hurt by how
/// near they stand and knocked flat, the kills credited to whoever shot it), which is why it is worth picking out;
/// next to a player it hurts and throws them. The host runs the fuse and ends it; every PC sees the death, plays the
/// burst there (CanBlastEffect, with no fire left behind) and hurts and throws its own player, a player's health being
/// their own PC's. On every zombie, as ZombieRunner is; it does nothing for other kinds. Numbers: Tables/ZombieExploder.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieExploder : MonoBehaviour
{
    public const string TableName = "ZombieExploder", TableRow = "Default";
    private const int SampleRate = 44100;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly List<EnemyHealth> near = new List<EnemyHealth>();
    private static AudioClip boom;

    [Header("Fuse")]
    [SerializeField, Min(0f), Tooltip("A player this near sets its fuse running (m).")]
    private float fuseRange = 2.4f;
    [SerializeField, Min(.1f), Tooltip("How long the fuse runs before it bursts (s). It runs back at half the pace while nobody is near.")]
    private float fuseSeconds = 1f;
    [Header("Burst")]
    [SerializeField, Min(0f), Tooltip("How far the burst reaches (m).")]
    private float blastRadius = 4.5f;
    [SerializeField, Min(0f), Tooltip("What it does to a player at its middle.")]
    private float playerDamage = 45f;
    [SerializeField, Min(0f), Tooltip("And at its edge.")]
    private float playerEdgeDamage = 12f;
    [SerializeField, Min(0f), Tooltip("It throws a player this fast away from it at its middle (m/s), less toward the edge.")]
    private float knock = 8f;
    [SerializeField, Min(0f), Tooltip("And this fast up (m/s).")]
    private float knockUp = 4f;
    [SerializeField, Min(0f), Tooltip("What it does to a zombie at its middle.")]
    private float zombieDamage = 220f;
    [SerializeField, Min(0f), Tooltip("And at its edge.")]
    private float zombieEdgeDamage = 60f;
    [SerializeField, Min(0f), Tooltip("How long it knocks zombies down (s). One that stands firm (a brute) is only hurt.")]
    private float zombieKnockdown = 2.2f;
    [SerializeField, Min(0f), Tooltip("A view within this of the burst shakes, harder nearer (m).")]
    private float shakeReach = 16f;
    [Header("The light in its chest")]
    [SerializeField, Min(.05f), Tooltip("How big the light is (m).")]
    private float glowSize = .7f;
    [SerializeField, Tooltip("Its colour while it only walks.")]
    private Color glowColor = new Color(1f, .5f, .12f);
    [SerializeField, Min(0f), Tooltip("How often it beats while it only walks (a second).")]
    private float glowBeat = 1.4f;
    [SerializeField, Min(0f), Tooltip("How often at the end of the fuse (a second).")]
    private float fuseBeat = 11f;

    private EnemyHealth health;
    private EnemyNetwork network;
    private ZombieType type;
    private ZombieMind mind;
    private MeleeCombatTarget melee;
    private Animator animator;
    private Transform chest;
    private GameObject glow;
    private Renderer glowRenderer;
    private MaterialPropertyBlock block;
    private GameObject lastSource;
    private float fuse, beat, nextFlash, nextLook;
    private bool watching;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => boom = null;

    private bool IsExploder => type != null && type.Kind == ZombieKind.Exploder && !type.Crawls;
    private bool Runs => network == null || !network.IsCopy;

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        type = GetComponent<ZombieType>();
        mind = GetComponent<ZombieMind>();
        melee = GetComponent<MeleeCombatTarget>();
        GameTables.Bind(TableName, TableRow, this);
        if (health != null) health.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (health != null) health.Died -= OnDied;
        Watch(false);
        if (glow != null) Destroy(glow);
    }

    // Only an exploder listens for who hurts it: every zombie carries this component, and most are not one.
    private void Watch(bool on)
    {
        if (on == watching) return;
        watching = on;
        if (on) CombatDamageEvents.Damaged += OnDamaged;
        else CombatDamageEvents.Damaged -= OnDamaged;
    }

    private void OnDisable()
    {
        if (glow != null) glow.SetActive(false);
    }

    // Who last hurt it: the pack its burst takes along is theirs.
    private void OnDamaged(CombatDamageReport report)
    {
        if (report.Target == health && report.Hit.Source != null) lastSource = report.Hit.Source;
    }

    private void LateUpdate()
    {
        bool lit = IsExploder && health != null && health.IsAlive;
        Watch(lit);
        if (!lit)
        {
            if (glow != null && glow.activeSelf) glow.SetActive(false);
            fuse = 0f;
            return;
        }
        float dt = Time.deltaTime;
        Fuse(dt);
        ShowGlow(dt);
    }

    // Every PC counts the fuse from where everyone stands, so its copies flash as the host's does; only the host ends it.
    private void Fuse(float dt)
    {
        bool standing = (mind == null || mind.Ready) && (melee == null || !(melee.IsLying || melee.IsStunned));
        bool close = false;
        if (standing)
            foreach (var player in PlayerRoster.All)
                if (player != null && player.IsInitialized && !player.IsDead
                    && (player.transform.position - transform.position).sqrMagnitude <= fuseRange * fuseRange) { close = true; break; }
        fuse = Mathf.Clamp(fuse + (close ? dt : -dt * .5f), 0f, fuseSeconds);
        if (close && Time.time >= nextFlash)
        {
            // Faster as it runs out.
            nextFlash = Time.time + Mathf.Lerp(.28f, .07f, fuse / fuseSeconds);
            HitFlashRenderer.For(this)?.WhiteFlash(.05f);
        }
        if (fuse < fuseSeconds || !Runs) return;
        health.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), health.Health + 1f, DamageKind.Fall, transform.position + Vector3.up,
            Vector3.up, null));
    }

    // The light: a soft glow at the chest, turned to the camera and held a little toward it so the body doesn't hide it.
    private void ShowGlow(float dt)
    {
        var view = Camera.main;
        if (view == null) return;
        if (chest == null)
        {
            // Its body is put on after it appears, and may be changed: looked for again now and then until it is there.
            if (Time.time < nextLook) return;
            nextLook = Time.time + .4f;
            animator = null;
            foreach (var candidate in GetComponentsInChildren<Animator>())
                if (candidate.isHuman) { animator = candidate; break; }
            chest = animator != null ? animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine) : null;
            if (chest == null) { if (glow != null && glow.activeSelf) glow.SetActive(false); return; }
        }
        if (glow == null) BuildGlow();
        if (!glow.activeSelf) glow.SetActive(true);
        float share = fuse / fuseSeconds;
        beat += dt * Mathf.Lerp(glowBeat, fuseBeat, share) * Mathf.PI * 2f;
        float pulse = .5f + .5f * Mathf.Sin(beat);
        Vector3 at = chest.position, toward = view.transform.position - at;
        glow.transform.position = at + toward.normalized * Mathf.Min(.3f, toward.magnitude * .5f);
        glow.transform.rotation = view.transform.rotation;
        glow.transform.localScale = Vector3.one * (glowSize * Mathf.Lerp(.8f, 1.15f, pulse) * (1f + share * .5f));
        Color color = Color.Lerp(glowColor, Color.white, share * .8f);
        color.a = Mathf.Lerp(.55f, 1f, pulse);
        block.SetColor(ColorId, color);
        glowRenderer.SetPropertyBlock(block);
    }

    // Its own object, not a child of the body: the hit flash is laid over every renderer under the zombie.
    private void BuildGlow()
    {
        glow = new GameObject("Exploder Light");
        RuntimeSceneHierarchy.Group(glow, "EFFECTS", "World", gameObject);
        glow.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
        var renderer = glow.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        glowRenderer = renderer;
        block = new MaterialPropertyBlock();
    }

    // Every PC, as it dies: the burst where it stood, this PC's own player hurt and thrown; on the host, the zombies round it.
    private void OnDied(EnemyHealth died)
    {
        if (!IsExploder) return;
        Vector3 at = transform.position;
        if (glow != null) glow.SetActive(false);
        CanBlastEffect.Play(at, blastRadius, 0f);
        GameAudio.PlayAt(Boom(), at + Vector3.up, AudioChannel.Combat, 1f, 4f, 80f);
        var mine = PlayerRoster.Local;
        if (mine != null && mine.IsInitialized && !mine.IsDead)
        {
            Vector3 away = mine.transform.position - at;
            float far = away.magnitude;
            Vector3 flat = Vector3.ProjectOnPlane(away, Vector3.up);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : mine.transform.forward * -1f;
            if (far <= blastRadius)
            {
                float share = 1f - far / Mathf.Max(.01f, blastRadius);
                mine.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Lerp(playerEdgeDamage, playerDamage, share), DamageKind.Attack,
                    mine.transform.position + Vector3.up, flat, gameObject, origin: at + Vector3.up));
                mine.KnockBack(flat * (knock * Mathf.Lerp(.4f, 1f, share)) + Vector3.up * knockUp);
            }
            float shake = 1f - Mathf.Clamp01(far / Mathf.Max(.01f, shakeReach));
            var view = shake > 0f ? mine.GetComponentInChildren<PlayerCameraEffects>() : null;
            if (view != null) view.ApplyCameraImpulse(new Vector3(-8f * shake, 0f, (Random.value < .5f ? -4f : 4f) * shake), 4.5f * shake);
        }
        if (!Runs) return;
        // A copy of the list: the burst kills, and the killed leave it (another exploder among them bursts in its turn).
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy != health && enemy.IsAlive && (enemy.transform.position - at).sqrMagnitude <= blastRadius * blastRadius) near.Add(enemy);
        var struck = near.ToArray();
        foreach (var enemy in struck)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            float share = 1f - Mathf.Clamp01(away.magnitude / Mathf.Max(.01f, blastRadius));
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Lerp(zombieEdgeDamage, zombieDamage, share), DamageKind.Attack,
                enemy.transform.position + Vector3.up, away, lastSource, false, 0f, true, origin: at));
            if (enemy != null && enemy.IsAlive && enemy.TryGetComponent(out MeleeCombatTarget target)) target.KnockDown(away, zombieKnockdown);
        }
    }

    // A wet, heavy burst, made here once: a thump that drops, under a short rush of dulled noise.
    private static AudioClip Boom()
    {
        if (boom != null) return boom;
        var data = new float[(int)(SampleRate * .9f)];
        var random = new System.Random(3016);
        float held = 0f, turn = 0f, top = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            turn += 2f * Mathf.PI * Mathf.Lerp(120f, 38f, Mathf.Clamp01(t / .25f)) / SampleRate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * Mathf.Lerp(.5f, .06f, Mathf.Clamp01(t / .3f));
            data[i] = Mathf.Sin(turn) * Mathf.Exp(-t / .16f) * .9f + held * Mathf.Exp(-t / .22f) * .8f;
            top = Mathf.Max(top, Mathf.Abs(data[i]));
        }
        int fade = SampleRate / 50;
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] / Mathf.Max(.001f, top) * .9f * Mathf.Min(1f, (data.Length - 1 - i) / (float)fade);
        boom = AudioClip.Create("Exploder Burst", data.Length, 1, SampleRate, false);
        boom.SetData(data, 0);
        boom.hideFlags = HideFlags.HideAndDontSave;
        return boom;
    }
}
