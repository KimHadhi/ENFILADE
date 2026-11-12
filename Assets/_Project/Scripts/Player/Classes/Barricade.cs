using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A Support's barricade (stage 4 of the classes; the user: Guts &amp; Blackpowder's sapper, "적들을 막는 임시 바리케이드
/// 같은 건축물"): planks nailed across two posts, waist high, so the team shoots over it and vaults it while zombies can't
/// pass (NavMeshObstacle, carving). A zombie's blow that meets it on its way to a player behind lands on the planks
/// instead (it takes hits: IDamageReceiver), and zombies pressing against it wear it down too; planks come off as it
/// weakens and it bursts apart when it breaks. Players' shots and blows stop on it and do nothing. On every PC: put up
/// where the placement rule allows it (Room: ground, slope, nothing in the way), by its Support's own PC when that is the
/// host or alone, else by the host when that PC asks (Judge checks the spot again against the host's world, the true
/// one) and PlayerNetworkCombat tells the others; worn down and broken by the host's or a lone PC's, whose health every
/// PC shows (a client's blow on its copy is sent there). Its look (Shape) is drawn see-through by BarricadeGhost while
/// the Support's class key is held. Numbers: Tables/ClassAbilities Support.barricade*.
/// </summary>
[DisallowMultipleComponent]
public sealed class Barricade : MonoBehaviour, IDamageReceiver
{
    private const string Row = "Support";
    /// <summary>Its size (m): along, up, through (SupportKit finds room for it by these).</summary>
    internal const float Width = 2.1f, Height = 1.15f, Depth = .3f;
    // The planks, how often the host looks at who presses on it (s), how often it tells the others its health while it
    // is being worn (s), and how long the planks fly when it breaks (s).
    private const float PressEvery = .5f, ShareEvery = .25f, FlySeconds = 2.4f;
    private const int Planks = 4;
    private static readonly Dictionary<ushort, Barricade> standing = new Dictionary<ushort, Barricade>();
    private static Material wood;
    private static readonly Collider[] pressed = new Collider[32];
    // What stands where one would go (Room); the builder's own body may take several of these.
    private static readonly Collider[] overlaps = new Collider[32];

    /// <summary>
    /// Whether a barricade can stand at a spot (the placement rule: SupportKit's preview and build, and the host's check),
    /// and if not, why: no ground under it, the ground too steep, a wall between the builder's eyes and it (their own PC
    /// only), something in its way (a wall, a prop, a zombie, a player, another barricade), too far from the builder
    /// (the host only, for a client's spot), or a supply bag too light for one (their own PC only).
    /// </summary>
    public enum Fit : byte { Ok, NoGround, Steep, Walled, Blocked, Far, Short }

    /// <summary>One box of its look, in its own frame (its feet at the origin, facing along z).</summary>
    internal readonly struct Part
    {
        public readonly string Name;
        public readonly Vector3 At, Size;
        public readonly Quaternion Turn;
        public readonly bool Plank;

        public Part(string name, Vector3 at, Vector3 size, Quaternion turn, bool plank)
        {
            Name = name;
            At = at;
            Size = size;
            Turn = turn;
            Plank = plank;
        }
    }

    private static Part[] shape;

    /// <summary>
    /// Its look, box by box: two posts, the planks across them at their own small tilts (the lowest first, the last to
    /// go), and a brace across their back, the sapper's quick work. The barricade and its preview (BarricadeGhost) alike.
    /// </summary>
    internal static IReadOnlyList<Part> Shape
    {
        get
        {
            if (shape != null) return shape;
            var parts = new List<Part>();
            for (int side = -1; side <= 1; side += 2)
                parts.Add(new Part("Post", new Vector3(side * (Width * .5f - .12f), Height * .5f, 0f), new Vector3(.13f, Height, .13f),
                    Quaternion.identity, false));
            for (int i = 0; i < Planks; i++)
            {
                float tilt = (i % 2 == 0 ? 1f : -1f) * (2f + i * 1.3f);
                parts.Add(new Part("Plank " + i, new Vector3(0f, .22f + i * (Height - .32f) / (Planks - 1), -.09f), new Vector3(Width, .2f, .05f),
                    Quaternion.Euler(0f, 0f, tilt), true));
            }
            parts.Add(new Part("Brace", new Vector3(0f, Height * .5f, .06f), new Vector3(Width * 1.02f, .14f, .045f), Quaternion.Euler(0f, 0f, 24f), false));
            return shape = parts.ToArray();
        }
    }

    private readonly List<Transform> planks = new List<Transform>();
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly HashSet<EnemyHealth> pressing = new HashSet<EnemyHealth>();
    private Transform look;
    private NavMeshObstacle obstacle;
    private BoxCollider body;
    private ushort id;
    private float health, most, nextPress, nextShare, shownShare = 1f, shakeLeft;
    private bool broken, dirty;

    /// <summary>Who built it (their own PC placed it).</summary>
    public PlayerMovement Owner { get; private set; }
    public float Health => health;
    public float MostHealth => most;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => standing.Clear();

    // The host or a lone PC: its zombies are the real ones, so it wears the barricades down, and says where they go up
    // (SupportKit puts its own up straight away there; a client's PC asks it).
    internal static bool Authority => !GameSession.IsOnline || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);

    // This PC's own player's network part, through which the barricades' news goes.
    private static PlayerNetworkCombat Network
    {
        get
        {
            var mine = PlayerRoster.Local;
            return mine != null && mine.TryGetComponent(out PlayerNetworkCombat network) && network.IsSpawned ? network : null;
        }
    }

    /// <summary>How many of this owner's stand now, oldest first.</summary>
    public static List<ushort> StandingOf(PlayerMovement owner)
    {
        var ids = new List<ushort>();
        foreach (var pair in standing) if (pair.Value != null && pair.Value.Owner == owner && !pair.Value.broken) ids.Add(pair.Key);
        ids.Sort((a, b) => standing[a].placedAt.CompareTo(standing[b].placedAt));
        return ids;
    }

    private float placedAt;

    /// <summary>How many of this owner's stand now (StandingOf without its list: the HUD asks every frame).</summary>
    public static int CountOf(PlayerMovement owner)
    {
        int count = 0;
        foreach (var pair in standing) if (pair.Value != null && pair.Value.Owner == owner && !pair.Value.broken) count++;
        return count;
    }

    /// <summary>This owner's oldest standing, the one a new one past their most takes down; or null.</summary>
    public static Barricade OldestOf(PlayerMovement owner)
    {
        Barricade oldest = null;
        foreach (var pair in standing)
        {
            var barricade = pair.Value;
            if (barricade != null && barricade.Owner == owner && !barricade.broken && (oldest == null || barricade.placedAt < oldest.placedAt))
                oldest = barricade;
        }
        return oldest;
    }

    /// <summary>
    /// The placement rule's part about the spot itself, on any PC: walkable ground right under its middle (not a zombie,
    /// a player or another barricade), no steeper than Support.barricadeSlope, and nothing where it would stand, facing
    /// along <paramref name="yaw"/> with its feet at <paramref name="at"/>. The <paramref name="builder"/>'s own body
    /// doesn't count (the nearest reach keeps it clear of them anyway).
    /// </summary>
    internal static Fit Room(Vector3 at, float yaw, Transform builder)
    {
        if (!Physics.Raycast(at + Vector3.up * .6f, Vector3.down, out RaycastHit ground, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            return Fit.NoGround;
        float slope = Mathf.Clamp(PlayerClass.Ability(Row, "barricadeSlope", 25f), 0f, 89f);
        if (ground.normal.y < Mathf.Cos(slope * Mathf.Deg2Rad)) return Fit.Steep;
        var under = ground.collider;
        if ((builder != null && under.transform.IsChildOf(builder)) || under.GetComponentInParent<EnemyHealth>() != null
            || under.GetComponentInParent<PlayerMovement>() != null || under.GetComponentInParent<Barricade>() != null)
            return Fit.Blocked;
        Vector3 middle = at + Vector3.up * (Height * .5f + .1f);
        var half = new Vector3(Width * .5f, Height * .5f - .05f, Depth * .5f);
        int count = Physics.OverlapBoxNonAlloc(middle, half, overlaps, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (overlaps[i] != null && (builder == null || !overlaps[i].transform.IsChildOf(builder))) return Fit.Blocked;
        return Fit.Ok;
    }

    /// <summary>
    /// On the host: a client's Support asks for a barricade here (PlayerNetworkCombat). The host's world is the true one
    /// (its zombies stand where they really are), so the spot must hold one there too (Room), and lie within the
    /// farthest reach of that player as the host sees them, with Support.barricadeLeeway more for their copy lagging
    /// behind. The builder's own view (no wall between) and their bag are theirs to judge, on their own PC.
    /// </summary>
    internal static Fit Judge(Vector3 at, float yaw, PlayerMovement owner)
    {
        if (owner == null) return Fit.Blocked;
        float reach = Mathf.Max(PlayerClass.Ability(Row, "barricadeAhead", 1.8f), PlayerClass.Ability(Row, "barricadeReach", 3.5f))
            + Mathf.Max(0f, PlayerClass.Ability(Row, "barricadeLeeway", 2.5f));
        Vector3 off = at - owner.transform.position;
        if (Mathf.Abs(off.y) > 3f) return Fit.Far;
        off.y = 0f;
        if (off.sqrMagnitude > reach * reach) return Fit.Far;
        return Room(at, yaw, owner.transform);
    }

    /// <summary>
    /// On the host or a lone PC, once the spot is allowed: barricade <paramref name="id"/> goes up for its Support, after
    /// their oldest come down past <paramref name="most"/> (their own count, which a card may raise on their own PC). The
    /// ids taken down are added to <paramref name="takenDown"/>, for the others to be told.
    /// </summary>
    internal static Barricade Raise(ushort id, Vector3 at, float yaw, PlayerMovement owner, int most, List<ushort> takenDown)
    {
        var mine = StandingOf(owner);
        while (mine.Count > 0 && mine.Count >= Mathf.Max(1, most))
        {
            ushort oldest = mine[0];
            mine.RemoveAt(0);
            Collapse(oldest);
            takenDown?.Add(oldest);
        }
        return Place(id, at, yaw, owner);
    }

    /// <summary>
    /// Puts barricade <paramref name="id"/> up at <paramref name="at"/> (its feet), facing along <paramref name="yaw"/>, its
    /// Support's. A second word of the same one is ignored.
    /// </summary>
    public static Barricade Place(ushort id, Vector3 at, float yaw, PlayerMovement owner)
    {
        if (!Application.isPlaying) return null;
        if (standing.TryGetValue(id, out var already) && already != null) return already;
        var host = FeelAssets.CreateRuntimeRoot("Barricade " + id);
        host.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        var barricade = host.AddComponent<Barricade>();
        barricade.id = id;
        barricade.Owner = owner;
        barricade.placedAt = Time.time;
        barricade.most = barricade.health = Mathf.Max(1f, PlayerClass.Ability(Row, "barricadeHealth", 400f));
        barricade.Build();
        standing[id] = barricade;
        return barricade;
    }

    /// <summary>Barricade <paramref name="id"/> comes down now (broken, or taken down by its Support for a new one).</summary>
    public static void Collapse(ushort id)
    {
        if (standing.TryGetValue(id, out var barricade) && barricade != null) barricade.Break();
    }

    /// <summary>On the host: a client's zombie blow landed on its copy of barricade <paramref name="id"/>.</summary>
    public static void Hurt(ushort id, float damage)
    {
        if (Authority && standing.TryGetValue(id, out var barricade) && barricade != null) barricade.Wear(damage);
    }

    /// <summary>On a client: barricade <paramref name="id"/>'s health on the host.</summary>
    public static void Show(ushort id, float health)
    {
        if (!standing.TryGetValue(id, out var barricade) || barricade == null || barricade.broken) return;
        if (health < barricade.health - .01f) barricade.Shake();
        barricade.health = Mathf.Clamp(health, 0f, barricade.most);
        barricade.Planked();
        if (barricade.health <= 0f) barricade.Break();
    }

    private void Build()
    {
        look = new GameObject("Planks").transform;
        look.SetParent(transform, false);
        var material = Wood;
        // Its look box by box (Shape, which its preview draws too); the planks come off one by one as it weakens.
        foreach (var part in Shape)
        {
            var box = Box(part.Name, part.At, part.Size, part.Turn, material);
            if (part.Plank) planks.Add(box);
        }

        body = gameObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, Height * .5f, 0f);
        body.size = new Vector3(Width, Height, Depth);
        obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = body.center;
        obstacle.size = new Vector3(Width + .1f, Height, Depth + .1f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    private Transform Box(string name, Vector3 at, Vector3 size, Quaternion turn, Material material)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(look, false);
        part.transform.localPosition = at;
        part.transform.localRotation = turn;
        part.transform.localScale = size;
        part.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return part.transform;
    }

    private void Update()
    {
        if (broken) return;
        if (shakeLeft > 0f)
        {
            shakeLeft -= Time.deltaTime;
            look.localPosition = shakeLeft > 0f ? Random.insideUnitSphere * .035f : Vector3.zero;
        }
        if (!Authority) return;
        if (Time.time >= nextPress)
        {
            nextPress = Time.time + PressEvery;
            Press();
        }
        // The others hear how worn it is now and then while it is worn, and at once when it breaks (Break).
        if (dirty && Time.time >= nextShare)
        {
            nextShare = Time.time + ShareEvery;
            dirty = false;
            Network?.ShareBarricadeHealth(id, health);
        }
    }

    // Zombies awake and pressing against it wear it down, each its share a second.
    private void Press()
    {
        int mask = LayerMask.GetMask("Enemy");
        if (mask == 0) mask = ~0;
        Vector3 half = new Vector3(Width * .5f + .5f, Height * .5f + .3f, Depth * .5f + .7f);
        int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(body.center), half, pressed, transform.rotation, mask,
            QueryTriggerInteraction.Ignore);
        pressing.Clear();
        for (int i = 0; i < count; i++)
        {
            var enemy = pressed[i] != null ? pressed[i].GetComponentInParent<EnemyHealth>() : null;
            if (enemy == null || !enemy.IsAlive) continue;
            if (enemy.TryGetComponent(out ZombieMind mind) && !mind.IsAwake) continue;
            pressing.Add(enemy);
        }
        if (pressing.Count == 0) return;
        Wear(pressing.Count * Mathf.Max(0f, PlayerClass.Ability(Row, "barricadePressure", 4f)) * PressEvery);
        if (Random.value < .5f) Shake();
    }

    /// <summary>
    /// A blow: only a zombie's wears it (a share of it: Support.barricadeBlow); a player's shot, shove or punch stops on
    /// the planks and does nothing. A client's blow is sent to the host, whose barricade is the true one.
    /// </summary>
    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (broken || hit.Amount <= 0f || hit.Id <= 0) return DamageResult.Ignored;
        bool zombie = hit.Source != null && hit.Source.GetComponentInParent<EnemyHealth>() != null;
        if (!zombie) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        float damage = hit.Amount * Mathf.Max(0f, PlayerClass.Ability(Row, "barricadeBlow", 1f));
        Shake();
        KitSound.Play(KitSound.Cue.Splinter, Near(.5f));
        if (Authority) Wear(damage);
        else Network?.SendBarricadeHit(id, damage);
        return DamageResult.Blocked;
    }

    private void Wear(float damage)
    {
        if (broken || damage <= 0f) return;
        health = Mathf.Max(0f, health - damage);
        dirty = true;
        Planked();
        if (health > 0f) return;
        Network?.ShareBarricadeHealth(id, 0f);
        Break();
    }

    // The planks come off from the top as it weakens: none left on the last quarter.
    private void Planked()
    {
        float share = most > 0f ? health / most : 0f;
        if (share >= shownShare - .001f && share > 0f) return;
        shownShare = share;
        for (int i = 0; i < planks.Count; i++)
        {
            var plank = planks[i];
            if (plank == null || !plank.gameObject.activeSelf) continue;
            if (share <= (float)i / Planks) Throw(plank, .6f);
        }
    }

    private void Shake() => shakeLeft = .15f;

    // Down: its planks and posts fly apart, the way is open again at once, and the pieces are gone in a few seconds.
    private void Break()
    {
        if (broken) return;
        broken = true;
        standing.Remove(id);
        if (obstacle != null) obstacle.enabled = false;
        if (body != null) body.enabled = false;
        foreach (Transform part in look) Throw(part, 1f);
        KitSound.Play(KitSound.Cue.Collapse, Near(.8f));
        Destroy(gameObject, FlySeconds);
    }

    // A piece let go: it flies off away from the middle, tumbling, and is gone with the barricade.
    private void Throw(Transform part, float strength)
    {
        if (part == null || !part.gameObject.activeSelf) return;
        var copy = Instantiate(part.gameObject, part.position, part.rotation);
        copy.transform.localScale = part.lossyScale;
        part.gameObject.SetActive(false);
        var collider = copy.AddComponent<BoxCollider>();
        collider.size = Vector3.one;
        var rigid = copy.AddComponent<Rigidbody>();
        rigid.mass = 4f;
        Vector3 away = (part.position - transform.position).normalized + Random.insideUnitSphere * .5f + Vector3.up * .8f;
        rigid.AddForce(away * Random.Range(2.5f, 5f) * strength, ForceMode.VelocityChange);
        rigid.AddTorque(Random.insideUnitSphere * 6f * strength, ForceMode.VelocityChange);
        Destroy(copy, FlySeconds);
    }

    // Louder the nearer this PC's player is to it.
    private float Near(float most)
    {
        var mine = PlayerRoster.Local;
        float distance = mine != null ? Vector3.Distance(mine.transform.position, transform.position) : 0f;
        return most * Mathf.Clamp01(1f - distance / 35f);
    }

    private void OnDestroy()
    {
        if (standing.TryGetValue(id, out var self) && self == this) standing.Remove(id);
    }

    // Weathered planks: a warm brown with long grain, made once.
    private static Material Wood
    {
        get
        {
            if (wood != null) return wood;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            const int Across = 32, Along = 256;
            var grain = new Texture2D(Across, Along, TextureFormat.RGBA32, true)
                { name = "Barricade Grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[Across * Along];
            for (int y = 0; y < Along; y++)
            for (int x = 0; x < Across; x++)
            {
                float streak = Mathf.PerlinNoise(x * .45f, y * .025f) * .6f + Mathf.PerlinNoise(x * 1.7f, y * .1f) * .25f;
                float knot = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y * .25f), new Vector2(Across * .6f, Along * .12f)) / 4f) * .35f;
                float shade = Mathf.Clamp01(.55f + streak * .5f - knot);
                pixels[y * Across + x] = new Color32((byte)(shade * 255f), (byte)(shade * 255f), (byte)(shade * 255f), 255);
            }
            grain.SetPixels32(pixels);
            grain.Apply(true, true);
            wood = new Material(shader) { name = "Barricade Wood (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            var brown = new Color(.47f, .33f, .21f, 1f);
            if (wood.HasProperty("_BaseMap")) wood.SetTexture("_BaseMap", grain);
            if (wood.HasProperty("_MainTex")) wood.SetTexture("_MainTex", grain);
            if (wood.HasProperty("_BaseColor")) wood.SetColor("_BaseColor", brown);
            if (wood.HasProperty("_Color")) wood.SetColor("_Color", brown);
            if (wood.HasProperty("_Smoothness")) wood.SetFloat("_Smoothness", .12f);
            return wood;
        }
    }
}
