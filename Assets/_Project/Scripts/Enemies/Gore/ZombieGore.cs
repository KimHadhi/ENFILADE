using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Zombie gore (the user's picks, 2026-11-09, from side-view mockups): a headshot kill bursts the head; arms and legs come
/// off, a living zombie's too, and a zombie that has lost a leg crawls. It listens to CombatDamageEvents.Damaged, which
/// every PC raises once per hit (the attacker's prediction, the host, the others as the host tells them), and every hit
/// carries the part it met (DamageHit.Part, from the bone hitbox the bullet hit), so every PC makes the same cut from the
/// same hits without any state of its own on the network.
/// A limb comes off when one blow puts Blow Cut of the zombie's health into it (a shotgun's pellets in one limb count as
/// one blow), when it has taken Wear Cut in all, or on the killing blow when that puts Kill Cut into it. The part is hidden
/// on the skin only (GoreBody), cut from the character's own skin as a flying piece (GoreGibs), and bleeds (GoreFx).
/// Until zombies can crawl (LegsCanCrawl, set by whatever moves them), a leg only comes off on the killing blow.
/// It finds zombies by itself once dressed (ZombieBody.Model) and needs nothing from them. Numbers in Tables/Gore.csv.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieGore : MonoBehaviour
{
    public const string TableName = "Gore", TableRow = "Default";
    private static ZombieGore instance;
    // Every followed zombie's bones by part: what AttackTrace reads the part a bullet met from.
    private static readonly Dictionary<Transform, BodyPart> boneParts = new Dictionary<Transform, BodyPart>();

    /// <summary>
    /// Zombies can crawl: a leg may come off a living one. Whatever moves zombies sets it once it handles Crippled.
    /// Until then a leg only comes off on the killing blow.
    /// </summary>
    public static bool LegsCanCrawl { get; set; }
    /// <summary>A part came off a living zombie, on every PC alike. Legs lost: it should crawl.</summary>
    public static event Action<EnemyHealth, BodyPart> Crippled;

    [SerializeField, Range(0f, 2f), Tooltip("Share of the zombie's health one blow must put into a limb to take it off a living zombie.")]
    private float blowCut = .45f;
    [SerializeField, Range(0f, 2f), Tooltip("Share of the zombie's health a limb can take in all before it comes off.")]
    private float wearCut = .6f;
    [SerializeField, Range(0f, 2f), Tooltip("Share of the zombie's health the killing blow must put into a limb to take it off.")]
    private float killCut = .15f;
    [SerializeField, Min(0f), Tooltip("How fast a burst head's chunks fly (m/s).")]
    private float burstSpeed = 6f;
    [SerializeField, Min(0), Tooltip("Gut chunks thrown from a burst head.")]
    private int gutChunks = 5;
    [SerializeField, Min(0f), Tooltip("How fast a severed arm or leg is thrown along the shot (m/s).")]
    private float gibSpeed = 3.5f;
    [SerializeField, Min(0f), Tooltip("How long a neck pumps blood after a burst (s); a limb's stump pumps 70% as long.")]
    private float spurtSeconds = 1.1f;
    [SerializeField, Min(0f), Tooltip("Drops a stump pumps per second at the first beat.")]
    private float spurtRate = 220f;
    [SerializeField, Min(0f), Tooltip("Size of the splat on the wall behind a burst head (m).")]
    private float wallSplat = 1.3f;
    [SerializeField, Min(0f), Tooltip("Size a pool under a stump spreads to (m).")]
    private float poolSize = .9f;
    [SerializeField, Min(0f), Tooltip("How long a piece lies before it sinks (s).")]
    private float gibLie = 20f;
    [SerializeField, Min(.05f), Tooltip("How long a piece takes to sink (s).")]
    private float gibSink = 2f;
    [SerializeField, Min(1), Tooltip("Pieces allowed about at once; past it the oldest sinks.")]
    private int mostGibs = 40;
    [SerializeField, Min(1), Tooltip("Blood decals allowed at once; past it the oldest is reused.")]
    private int mostSplats = 32;
    [SerializeField, Min(1f), Tooltip("How long a blood decal stays before it fades (s).")]
    private float splatSeconds = 25f;

    internal GoreFx Fx { get; private set; }
    internal GoreGibs Gibs { get; private set; }
    internal int MostGibs => mostGibs;
    internal int MostSplats => mostSplats;
    internal float GibLie => gibLie;
    internal float GibSink => gibSink;
    internal float SpurtSeconds => spurtSeconds;
    internal float SpurtRate => spurtRate;
    internal float WallSplat => wallSplat;
    internal float PoolSize => poolSize;
    internal float SplatSeconds => splatSeconds;

    private readonly List<GoreBody> bodies = new List<GoreBody>();
    private readonly Dictionary<EnemyHealth, GoreBody> byEnemy = new Dictionary<EnemyHealth, GoreBody>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        instance = null;
        boneParts.Clear();
        LegsCanCrawl = false;
        Crippled = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    private static void Ensure()
    {
        if (instance != null || !Application.isPlaying) return;
        FeelAssets.CreateRuntimeRoot("Zombie Gore").AddComponent<ZombieGore>();
    }

    /// <summary>The part of a zombie a collider sits on (its bone hitboxes), or None.</summary>
    public static BodyPart PartOf(Collider collider)
    {
        if (collider == null || boneParts.Count == 0) return BodyPart.None;
        var bone = collider.transform.parent;
        return bone != null && boneParts.TryGetValue(bone, out var part) ? part : BodyPart.None;
    }

    /// <summary>What a zombie has lost: legs and arms (0 to 2 each). False if it isn't followed.</summary>
    public static bool Losses(EnemyHealth enemy, out int legs, out int arms)
    {
        legs = arms = 0;
        if (instance == null || enemy == null || !instance.byEnemy.TryGetValue(enemy, out var body) || body == null) return false;
        legs = body.LegsLost;
        arms = body.ArmsLost;
        return true;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        GameTables.Bind(TableName, TableRow, this);
        Fx = new GoreFx(this, transform);
        Gibs = new GoreGibs(this);
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        foreach (var body in bodies) body.Unregister(boneParts);
        instance = null;
    }

    private void Update()
    {
        // Zombies once dressed; a failed one is remembered as null so it isn't tried again.
        var living = EnemyHealth.Living;
        for (int i = 0; i < living.Count; i++)
        {
            var enemy = living[i];
            if (enemy == null || byEnemy.ContainsKey(enemy) || !enemy.TryGetComponent(out ZombieBody zombie)) continue;
            if (zombie.Model == null) { if (zombie.Wears) byEnemy[enemy] = null; continue; }
            var body = GoreBody.Follow(enemy, zombie.Model);
            byEnemy[enemy] = body;
            if (body == null) continue;
            bodies.Add(body);
            body.Register(boneParts);
            Gibs.Warm(body);
        }
        // A body whose corpse has gone is let go.
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var body = bodies[i];
            if (body.Model != null) continue;
            body.Unregister(boneParts);
            byEnemy.Remove(body.Enemy);
            bodies.RemoveAt(i);
        }
        if (byEnemy.Count > bodies.Count + 64) Prune();
        Gibs.Tick();
        Fx.Tick(Time.deltaTime);
    }

    // Enemies that were never dressed and are gone.
    private void Prune()
    {
        var gone = new List<EnemyHealth>();
        foreach (var pair in byEnemy) if (pair.Value == null && pair.Key == null) gone.Add(pair.Key);
        foreach (var enemy in gone) byEnemy.Remove(enemy);
    }

    private static void OnDamaged(CombatDamageReport report)
    {
        if (instance == null || !(report.Target is EnemyHealth enemy)) return;
        if (!instance.byEnemy.TryGetValue(enemy, out var body) || body == null || body.Model == null) return;
        instance.Judge(body, report);
    }

    private void Judge(GoreBody body, in CombatDamageReport report)
    {
        var hit = report.Hit;
        bool kill = report.IsKillingBlow;
        var part = hit.Part;
        if (kill && (part == BodyPart.Head || (hit.IsCritical && hit.Method == DamageMethod.Bullet)))
        {
            if (body.Has(BodyPart.Head)) Burst(body, hit);
            return;
        }
        if (!BodyParts.IsLimb(part) || !body.Has(part)) return;
        float health = Mathf.Max(1f, report.MaximumHealth);
        float blow = body.Blow(part, hit.Amount), worn = body.Wear(part, hit.Amount);
        bool off = kill ? blow >= killCut * health : blow >= blowCut * health || worn >= wearCut * health;
        if (!off || (!kill && BodyParts.IsLeg(part) && !LegsCanCrawl)) return;
        Cut(body, part, hit);
        if (!kill) Crippled?.Invoke(body.Enemy, part);
    }

    private static Vector3 Along(in DamageHit hit, GoreBody body)
    {
        Vector3 direction = hit.Direction;
        if (direction.sqrMagnitude > 1e-4f) return direction.normalized;
        return -body.Model.transform.forward;
    }

    // The head bursts and is gone: blood and guts fly along the shot, the neck is left a stump that pumps. Its skin is not
    // thrown as chunks: an open shell of skin showed its missing inside (the user, 2026-11-09: "걍 터뜨리고 없애도").
    private void Burst(GoreBody body, in DamageHit hit)
    {
        var head = body.Bone(HumanBodyBones.Head);
        if (head == null) return;
        Vector3 direction = Along(hit, body);
        // The head's middle: its hitbox sits this far up from the head bone, at the top of the neck (ZombieBody).
        Vector3 center = head.position + body.Model.transform.up * .09f;
        var root = body.Hide(BodyPart.Head);
        Fx.Wound(center, direction, body.Enemy, true);
        Fx.Burst(center, direction, body.Model.transform, burstSpeed, gutChunks);
        if (root != null) Fx.Stump(root, BodyPart.Head);
    }

    // An arm or leg comes off whole at its joint and flies along the shot.
    private void Cut(GoreBody body, BodyPart part, in DamageHit hit)
    {
        var root = body.Bone(BodyParts.Root(part));
        if (root == null) return;
        Vector3 direction = Along(hit, body), at = root.position;
        Gibs.Sever(body, part, direction, gibSpeed, Vector3.zero);
        if (body.Hide(part) == null) return;
        Fx.Wound(at, direction, body.Enemy, false);
        Fx.Stump(root, part);
    }
}
