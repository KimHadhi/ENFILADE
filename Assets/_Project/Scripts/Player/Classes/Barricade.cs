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
/// <para>
/// It comes in kinds now (the user, 2026-11-12: "서포트 다른 건축물 아이디어 지어도 괜찮을 듯"), each from its own Support
/// card, chosen with F while the class key is held (SupportKit), and carried in its id (two bits), so every PC builds
/// the same one from the same word: the planks (above); barbed wire, low and walked through, which slows the zombies in
/// it, cuts them a little and wears as they push through (Tangle; the host's zombies); an ammo crate, which anyone takes
/// a resupply from with F, once in a while each, its health its stock (Take); and a searchlight, which lights the ground
/// and keeps the zombies from coming up anywhere within its reach, the safe spot the user asked for ("탐조등이 있는 위치는
/// 좀비 스폰이 안 되게 막는 거지": Shelters, asked by the wave and the campaign's spawners). Numbers: Support.wire*,
/// crate*, light*.
/// </para>
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

    /// <summary>What it is: planks across posts, barbed wire, an ammo crate, a searchlight.</summary>
    public enum Kind : byte { Planks, Wire, Crate, Light }

    /// <summary>The kind an id carries (bits 6 and 7: SupportKit makes them so).</summary>
    public static Kind KindOf(ushort id) => (Kind)((id >> 6) & 3);

    /// <summary>Its size (m): along, up, through.</summary>
    internal static Vector3 SizeOf(Kind kind) => kind switch
    {
        Kind.Wire => new Vector3(2.6f, .6f, .7f),
        Kind.Crate => new Vector3(.9f, .55f, .55f),
        Kind.Light => new Vector3(.8f, 1.9f, .8f),
        _ => new Vector3(Width, Height, Depth),
    };

    /// <summary>One box of its look, in its own frame (its feet at the origin, facing along z).</summary>
    internal readonly struct Part
    {
        public readonly string Name;
        public readonly Vector3 At, Size;
        public readonly Quaternion Turn;
        public readonly bool Plank;
        /// <summary>What it is made of: 0 wood, 1 steel, 2 olive drab, 3 dark metal, 4 the lamp's glass.</summary>
        public readonly byte Paint;

        public Part(string name, Vector3 at, Vector3 size, Quaternion turn, bool plank, byte paint = 0)
        {
            Name = name;
            At = at;
            Size = size;
            Turn = turn;
            Plank = plank;
            Paint = paint;
        }
    }

    private static Part[] shape;
    private static readonly Part[][] shapes = new Part[4][];

    /// <summary>A kind's look, box by box (the planks' is Shape), the built one and its preview alike.</summary>
    internal static IReadOnlyList<Part> ShapeOf(Kind kind)
    {
        if (kind == Kind.Planks) return Shape;
        if (shapes[(int)kind] != null) return shapes[(int)kind];
        var parts = new List<Part>();
        switch (kind)
        {
            case Kind.Wire:
                // Stakes along it and wire crossed between them (the coils themselves are drawn on the built one).
                for (int i = -1; i <= 1; i++)
                    parts.Add(new Part("Stake", new Vector3(i * 1.2f, .35f, 0f), new Vector3(.05f, .7f, .05f), Quaternion.identity, false, 1));
                for (int i = -1; i <= 1; i += 2)
                for (int s = -1; s <= 1; s += 2)
                    parts.Add(new Part("Wire", new Vector3(i * .6f, .32f, 0f), new Vector3(1.32f, .015f, .015f), Quaternion.Euler(0f, 0f, s * 22f), false, 1));
                break;
            case Kind.Crate:
                parts.Add(new Part("Box", new Vector3(0f, .25f, 0f), new Vector3(.9f, .5f, .55f), Quaternion.identity, false, 2));
                parts.Add(new Part("Lid", new Vector3(0f, .52f, 0f), new Vector3(.93f, .05f, .58f), Quaternion.identity, false, 3));
                for (int side = -1; side <= 1; side += 2)
                    parts.Add(new Part("Handle", new Vector3(side * .47f, .33f, 0f), new Vector3(.03f, .05f, .22f), Quaternion.identity, false, 1));
                break;
            case Kind.Light:
                for (int leg = 0; leg < 3; leg++)
                {
                    var turn = Quaternion.Euler(0f, leg * 120f, 0f);
                    parts.Add(new Part("Leg", turn * new Vector3(0f, .45f, .22f), new Vector3(.04f, .95f, .04f), turn * Quaternion.Euler(-25f, 0f, 0f), false, 3));
                }
                parts.Add(new Part("Mast", new Vector3(0f, 1.25f, 0f), new Vector3(.06f, .9f, .06f), Quaternion.identity, false, 3));
                parts.Add(new Part("Head", new Vector3(0f, 1.75f, 0f), new Vector3(.34f, .26f, .32f), Quaternion.Euler(28f, 0f, 0f), false, 3));
                parts.Add(new Part("Glass", new Vector3(0f, 1.71f, .17f), new Vector3(.28f, .2f, .02f), Quaternion.Euler(28f, 0f, 0f), false, 4));
                break;
        }
        return shapes[(int)kind] = parts.ToArray();
    }

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
    /// <summary>Its name on every PC (the network's word for it), and what it is.</summary>
    public ushort Id => id;
    public Kind Type => KindOf(id);
    /// <summary>An ammo crate's takes left (its health).</summary>
    public int Stock => Mathf.Max(0, Mathf.CeilToInt(health - .01f));
    /// <summary>Standing and short of its health: a Support's hammer can mend it (SupportKit).</summary>
    public bool Worn => !broken && health < most - .5f && Type != Kind.Crate;

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

    /// <summary>How many of this owner's of this kind stand now, oldest first.</summary>
    public static List<ushort> StandingOf(PlayerMovement owner, Kind kind)
    {
        var ids = StandingOf(owner);
        ids.RemoveAll(id => KindOf(id) != kind);
        return ids;
    }

    /// <summary>How many of this owner's of this kind stand now (the HUD asks every frame).</summary>
    public static int CountOf(PlayerMovement owner, Kind kind = Kind.Planks)
    {
        int count = 0;
        foreach (var pair in standing)
            if (pair.Value != null && pair.Value.Owner == owner && !pair.Value.broken && KindOf(pair.Key) == kind) count++;
        return count;
    }

    /// <summary>This owner's oldest standing of this kind, the one a new one past their most takes down; or null.</summary>
    public static Barricade OldestOf(PlayerMovement owner, Kind kind = Kind.Planks)
    {
        Barricade oldest = null;
        foreach (var pair in standing)
        {
            var barricade = pair.Value;
            if (barricade != null && barricade.Owner == owner && !barricade.broken && KindOf(pair.Key) == kind
                && (oldest == null || barricade.placedAt < oldest.placedAt))
                oldest = barricade;
        }
        return oldest;
    }

    /// <summary>
    /// Whether a zombie may not come up at <paramref name="point"/>: a searchlight stands within its reach there
    /// (Support.lightRadius). The wave and the campaign's spawners ask, on the host.
    /// </summary>
    public static bool Shelters(Vector3 point)
    {
        if (standing.Count == 0) return false;
        float reach = Mathf.Max(0f, PlayerClass.Ability(Row, "lightRadius", 25f));
        foreach (var pair in standing)
        {
            var light = pair.Value;
            if (light != null && !light.broken && KindOf(pair.Key) == Kind.Light && (light.transform.position - point).sqrMagnitude <= reach * reach)
                return true;
        }
        return false;
    }

    /// <summary>The ammo crate under <paramref name="who"/>'s crosshair within reach (anyone's), or null.</summary>
    public static Barricade CrateFor(PlayerMovement who)
    {
        if (standing.Count == 0 || who == null) return null;
        var eyes = PlayerView.EyesOf(who);
        if (eyes == null) return null;
        float reach = Mathf.Max(.5f, PlayerClass.Ability(Row, "crateReach", 2.5f));
        if (!Physics.Raycast(eyes.position, eyes.forward, out RaycastHit hit, reach, ~(1 << who.gameObject.layer), QueryTriggerInteraction.Ignore))
            return null;
        var crate = hit.collider.GetComponentInParent<Barricade>();
        return crate != null && !crate.broken && crate.Type == Kind.Crate ? crate : null;
    }

    /// <summary>The host or a lone PC: one take from ammo crate <paramref name="id"/> (a friend's PC asks: PlayerNetworkCombat).</summary>
    public static void Take(ushort id)
    {
        if (Authority && standing.TryGetValue(id, out var crate) && crate != null && crate.Type == Kind.Crate) crate.Wear(1f);
    }

    /// <summary>
    /// The placement rule's part about the spot itself, on any PC: walkable ground right under its middle (not a zombie,
    /// a player or another barricade), no steeper than Support.barricadeSlope, and nothing where it would stand, facing
    /// along <paramref name="yaw"/> with its feet at <paramref name="at"/>. The <paramref name="builder"/>'s own body
    /// doesn't count (the nearest reach keeps it clear of them anyway).
    /// </summary>
    internal static Fit Room(Vector3 at, float yaw, Transform builder, Kind kind = Kind.Planks)
    {
        if (!Physics.Raycast(at + Vector3.up * .6f, Vector3.down, out RaycastHit ground, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            return Fit.NoGround;
        float slope = Mathf.Clamp(PlayerClass.Ability(Row, "barricadeSlope", 25f), 0f, 89f);
        if (ground.normal.y < Mathf.Cos(slope * Mathf.Deg2Rad)) return Fit.Steep;
        var under = ground.collider;
        if ((builder != null && under.transform.IsChildOf(builder)) || under.GetComponentInParent<EnemyHealth>() != null
            || under.GetComponentInParent<PlayerMovement>() != null || under.GetComponentInParent<Barricade>() != null)
            return Fit.Blocked;
        Vector3 size = SizeOf(kind), middle = at + Vector3.up * (size.y * .5f + .1f);
        var half = new Vector3(size.x * .5f, size.y * .5f - .05f, size.z * .5f);
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
    internal static Fit Judge(Vector3 at, float yaw, PlayerMovement owner, Kind kind = Kind.Planks)
    {
        if (owner == null) return Fit.Blocked;
        float reach = Mathf.Max(PlayerClass.Ability(Row, "barricadeAhead", 1.8f), PlayerClass.Ability(Row, "barricadeReach", 3.5f))
            + Mathf.Max(0f, PlayerClass.Ability(Row, "barricadeLeeway", 2.5f));
        Vector3 off = at - owner.transform.position;
        if (Mathf.Abs(off.y) > 3f) return Fit.Far;
        off.y = 0f;
        if (off.sqrMagnitude > reach * reach) return Fit.Far;
        return Room(at, yaw, owner.transform, kind);
    }

    /// <summary>
    /// On the host or a lone PC, once the spot is allowed: barricade <paramref name="id"/> goes up for its Support, after
    /// their oldest come down past <paramref name="most"/> (their own count, which a card may raise on their own PC). The
    /// ids taken down are added to <paramref name="takenDown"/>, for the others to be told.
    /// </summary>
    internal static Barricade Raise(ushort id, Vector3 at, float yaw, PlayerMovement owner, int most, List<ushort> takenDown)
    {
        var mine = StandingOf(owner, KindOf(id));
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
        var kind = KindOf(id);
        barricade.most = barricade.health = Mathf.Max(1f, kind == Kind.Wire ? PlayerClass.Ability(Row, "wireHealth", 300f)
            : kind == Kind.Crate ? Mathf.Round(PlayerClass.Ability(Row, "crateStock", 6f))
            : kind == Kind.Light ? PlayerClass.Ability(Row, "lightHealth", 300f) : PlayerClass.Ability(Row, "barricadeHealth", 400f));
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
        bool mended = health > barricade.health + .01f;
        barricade.health = Mathf.Clamp(health, 0f, barricade.most);
        if (mended) barricade.Replanked();
        else barricade.Planked();
        if (barricade.health <= 0f) barricade.Break();
    }

    /// <summary>
    /// A Support's hammer put <paramref name="amount"/> health back into barricade <paramref name="id"/> (SupportKit; the user,
    /// 2026-11-12: "바리케이드가 설치만 하면 끝인 게 아니라 G&amp;B 공병처럼 수리하고 유지 보수할 수 있으면 재밌을 듯"): up to its
    /// most, its planks nailed back on. The host's or a lone PC's is the true one and tells the others as wear does; a
    /// client's Support's PC asks it (PlayerNetworkCombat) and sees the mend come back with the host's word.
    /// </summary>
    public static void Mend(ushort id, float amount)
    {
        if (!Authority || amount <= 0f || !standing.TryGetValue(id, out var barricade) || barricade == null || barricade.broken) return;
        barricade.health = Mathf.Min(barricade.most, barricade.health + amount);
        barricade.dirty = true;
        barricade.Replanked();
    }

    private void Build()
    {
        var kind = Type;
        look = new GameObject(kind.ToString()).transform;
        look.SetParent(transform, false);
        // Its look box by box (ShapeOf, which its preview draws too); the planks come off one by one as it weakens.
        foreach (var part in ShapeOf(kind))
        {
            var box = Box(part.Name, part.At, part.Size, part.Turn, Paint(part.Paint));
            if (part.Plank) planks.Add(box);
        }
        Vector3 size = SizeOf(kind);
        body = gameObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, size.y * .5f, 0f);
        body.size = kind == Kind.Light ? new Vector3(.45f, size.y, .45f) : size;
        // Wire is walked through (a zombie is tangled in it, a player steps over it); the rest stand in the way.
        if (kind == Kind.Wire)
        {
            body.isTrigger = true;
            Coils(size);
            return;
        }
        obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = body.center;
        obstacle.size = new Vector3(body.size.x + .1f, size.y, body.size.z + .1f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        if (kind == Kind.Light) Lamp();
    }

    // Barbed wire: two coils along it, wound round, steel grey.
    private void Coils(Vector3 size)
    {
        for (int coil = 0; coil < 2; coil++)
        {
            var line = new GameObject("Coil").AddComponent<LineRenderer>();
            line.transform.SetParent(look, false);
            line.useWorldSpace = false;
            const int Points = 120;
            line.positionCount = Points;
            line.widthMultiplier = .012f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = Paint(1);
            float radius = .2f + coil * .06f, z = (coil == 0 ? -.12f : .12f);
            for (int i = 0; i < Points; i++)
            {
                float t = i / (Points - 1f), turn = t * 2f * Mathf.PI * 14f;
                line.SetPosition(i, new Vector3(Mathf.Lerp(-size.x * .5f, size.x * .5f, t), radius + Mathf.Sin(turn) * radius, z + Mathf.Cos(turn) * radius * .8f));
            }
        }
    }

    // The searchlight's lamp, its beam over the ground, and a faint ring where it keeps the zombies from coming up.
    private void Lamp()
    {
        var lamp = new GameObject("Lamp").AddComponent<Light>();
        lamp.transform.SetParent(transform, false);
        lamp.transform.localPosition = new Vector3(0f, 1.75f, .2f);
        lamp.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
        lamp.type = LightType.Spot;
        lamp.spotAngle = 75f;
        lamp.range = 32f;
        lamp.intensity = 3.5f;
        lamp.color = new Color(1f, .96f, .86f);
        lamp.shadows = LightShadows.None;
        float reach = Mathf.Max(1f, PlayerClass.Ability(Row, "lightRadius", 25f));
        var ring = new GameObject("Safe Ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.useWorldSpace = true;
        ring.loop = true;
        const int RingPoints = 96;
        ring.positionCount = RingPoints;
        ring.widthMultiplier = .08f;
        ring.alignment = LineAlignment.TransformZ;
        ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        ring.startColor = ring.endColor = new Color(1f, .97f, .85f, .2f);
        Vector3 middle = transform.position + Vector3.up * .06f;
        for (int i = 0; i < RingPoints; i++)
        {
            float angle = 2f * Mathf.PI * i / RingPoints;
            ring.SetPosition(i, middle + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach));
        }
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
        var kind = Type;
        if (kind == Kind.Wire) Tangle();
        else if (kind != Kind.Crate && Time.time >= nextPress)
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

    // Barbed wire, on the host: the zombies in it slowed every frame, cut a little twice a second, and wearing it as they
    // push through. Its cuts are nobody's (no money, no gauge): the wire's own.
    private readonly List<NavMeshAgent> tangled = new List<NavMeshAgent>();
    private readonly List<EnemyHealth> caught = new List<EnemyHealth>();
    private float nextTangle, nextSting;

    private void Tangle()
    {
        if (Time.time >= nextTangle)
        {
            nextTangle = Time.time + .1f;
            tangled.Clear();
            caught.Clear();
            int mask = LayerMask.GetMask("Enemy");
            if (mask == 0) mask = ~0;
            Vector3 size = SizeOf(Kind.Wire);
            int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(new Vector3(0f, size.y * .5f, 0f)),
                new Vector3(size.x * .5f, size.y * .5f + .4f, size.z * .5f + .15f), pressed, transform.rotation, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var enemy = pressed[i] != null ? pressed[i].GetComponentInParent<EnemyHealth>() : null;
                if (enemy == null || !enemy.IsAlive || caught.Contains(enemy)) continue;
                caught.Add(enemy);
                if (enemy.TryGetComponent(out NavMeshAgent agent)) tangled.Add(agent);
            }
        }
        float slow = Mathf.Clamp01(PlayerClass.Ability(Row, "wireSlow", .4f));
        foreach (var agent in tangled)
        {
            if (agent == null || !agent.isActiveAndEnabled) continue;
            float most = agent.speed * slow;
            if (agent.velocity.sqrMagnitude > most * most) agent.velocity = agent.velocity.normalized * most;
        }
        if (caught.Count == 0 || Time.time < nextSting) return;
        nextSting = Time.time + .5f;
        float sting = Mathf.Max(0f, PlayerClass.Ability(Row, "wireDamage", 6f)) * .5f;
        foreach (var enemy in caught)
            if (enemy != null && enemy.IsAlive && sting > 0f)
                enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), sting, DamageKind.Attack, enemy.transform.position + Vector3.up,
                    transform.forward, gameObject, false, 0f, true, origin: transform.position));
        Wear(caught.Count * Mathf.Max(0f, PlayerClass.Ability(Row, "wireWear", 3f)) * .5f);
        if (Random.value < .5f) Shake();
    }

    /// <summary>
    /// A blow: only a zombie's wears it (a share of it: Support.barricadeBlow); a player's shot, shove or punch stops on
    /// the planks and does nothing. A client's blow is sent to the host, whose barricade is the true one. An ammo crate
    /// stops a blow without wear (its health is its stock); wire is walked through and never met.
    /// </summary>
    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (broken || hit.Amount <= 0f || hit.Id <= 0 || Type == Kind.Wire) return DamageResult.Ignored;
        bool zombie = hit.Source != null && hit.Source.GetComponentInParent<EnemyHealth>() != null;
        if (!zombie) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        if (Type == Kind.Crate) return DamageResult.Blocked;
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

    // Mended: the planks come back on from the bottom, the way they came off from the top (Planked), so the next wear
    // takes them off again.
    private void Replanked()
    {
        float share = most > 0f ? health / most : 0f;
        shownShare = share;
        for (int i = 0; i < planks.Count; i++)
        {
            var plank = planks[i];
            if (plank != null && !plank.gameObject.activeSelf && share > (float)i / Planks) plank.gameObject.SetActive(true);
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

    // What a part is made of: wood (the planks'), steel, olive drab (the crate), dark metal, the lamp's glowing glass.
    private static readonly Material[] paints = new Material[5];
    private static Material Paint(byte paint)
    {
        if (paint == 0) return Wood;
        if (paints[paint] != null) return paints[paint];
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Color color = paint == 1 ? new Color(.6f, .62f, .64f) : paint == 2 ? new Color(.29f, .33f, .2f) : paint == 3 ? new Color(.16f, .17f, .18f)
            : new Color(1f, .97f, .86f);
        var made = new Material(shader) { name = "Structure Paint (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", color);
        if (made.HasProperty("_Color")) made.SetColor("_Color", color);
        if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", paint == 2 ? .15f : .6f);
        if (paint == 4 && made.HasProperty("_EmissionColor"))
        {
            made.EnableKeyword("_EMISSION");
            made.SetColor("_EmissionColor", color * 2.2f);
        }
        return paints[paint] = made;
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
