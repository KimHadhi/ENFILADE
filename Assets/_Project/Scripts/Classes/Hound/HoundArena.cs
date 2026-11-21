using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// The Schießhund's showdown arena (its ultimate; the user, 2026-11-14: "아이소 궁극기처럼 선택한 대상을 납치해서 시순트와 그
/// 둘만의 결투장으로 데려갈 수 있나?"): a round floor walled and roofed in, far below the map where nobody else can reach it, lit
/// hard from overhead like a ring at night, a teal line round its foot (the bounty's colour) and a gold one higher up (the
/// Desperado's). Nothing else is in it. Every PC that hears of a showdown makes it (ArenaNet), so the hound has its floor
/// and walls and a body that falls there lands, whichever PC draws it. Each seat of the room has its own, the next one a
/// little deeper, so two hounds' showdowns never meet. The PC that runs the zombie (the host, or a lone PC) also lays a
/// walk-mesh over the floor before the zombie is moved in (Walkable, through ArenaPen; NavMeshBuilder, the runtime bake
/// a NavMeshSurface does, once for each kind of agent), so the zombie fights there on its own legs and with its own
/// brain, as on any map. Made once a scene, then moved under each showdown.
/// Its roof casts a shadow both ways, so the sun overhead never reaches in. Numbers: Tables/ClassAbilities
/// Schiesshund.arena* (the table's own on every PC, never a card's, so all of them build the same arena).
/// </summary>
[DisallowMultipleComponent]
public sealed class HoundArena : MonoBehaviour
{
    private const string Row = PlayerClass.HoundRow;
    // Sides of the round floor, wall and roof, and solid panels round the wall.
    private const int Sides = 64, Panels = 24;
    private static readonly Color Gold = new Color(1f, .59f, .16f);

    private static readonly Dictionary<int, HoundArena> arenas = new Dictionary<int, HoundArena>();
    // The players in an arena now, on every PC (ArenaNet says when they go and when they come back).
    private static readonly HashSet<PlayerMovement> away = new HashSet<PlayerMovement>();
    // The floor's walk-mesh for each kind of agent, built once in the arena's own space and laid wherever it stands.
    private static readonly Dictionary<int, NavMeshData> walks = new Dictionary<int, NavMeshData>();
    private static Material floorPaint, wallPaint, roofPaint;

    private readonly Dictionary<int, NavMeshDataInstance> laid = new Dictionary<int, NavMeshDataInstance>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Light> lamps = new List<Light>();
    private Mesh floor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        arenas.Clear();
        away.Clear();
        walks.Clear();
        floorPaint = wallPaint = roofPaint = null;
    }

    /// <summary>One of the arena's numbers: the table's, the same on every PC.</summary>
    internal static float Number(string name, float fallback) => GameTables.Number(PlayerClass.AbilityTable, Row, name, fallback);

    /// <summary>The floor's radius and the height of its wall and roof (m).</summary>
    public static float Radius => Mathf.Clamp(Number("arenaRadius", 8f), 3f, 40f);
    public static float Height => Mathf.Clamp(Number("arenaHeight", 12f), 4f, 60f);

    /// <summary>
    /// Where the arena of <paramref name="seat"/> stands for a showdown left from <paramref name="from"/>: straight under it,
    /// far down (arenaDepth), deeper by arenaStep for each seat after the first, so two arenas' walls and lights never meet.
    /// </summary>
    public static Vector3 CenterUnder(Vector3 from, int seat)
    {
        float step = Mathf.Max(Height + 10f, Number("arenaStep", 50f));
        return new Vector3(from.x, from.y - Mathf.Max(100f, Number("arenaDepth", 600f)) - Mathf.Clamp(seat, 0, 31) * step, from.z);
    }

    /// <summary>ArenaNet: <paramref name="player"/> went into an arena, or came back.</summary>
    internal static void SetAway(PlayerMovement player, bool gone)
    {
        if (player == null) return;
        if (gone) away.Add(player);
        else away.Remove(player);
    }

    /// <summary>The arena of <paramref name="seat"/>, made if it isn't yet, standing with its floor's middle at <paramref name="center"/>.</summary>
    public static HoundArena At(int seat, Vector3 center)
    {
        if (!Application.isPlaying) return null;
        if (!arenas.TryGetValue(seat, out var arena) || arena == null)
        {
            var host = new GameObject("Showdown Arena " + seat);
            host.transform.SetPositionAndRotation(center, Quaternion.identity);
            RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.World, "Showdown");
            arena = host.AddComponent<HoundArena>();
            arena.Build();
            arenas[seat] = arena;
        }
        arena.Stand(center);
        return arena;
    }

    /// <summary>The showdown in the arena of <paramref name="seat"/> is over: its lights go out until the next one.</summary>
    public static void Rest(int seat)
    {
        if (arenas.TryGetValue(seat, out var arena) && arena != null) arena.Lamps(false);
    }

    /// <summary>
    /// Lays the floor's walk-mesh here for this kind of agent (on the PC that runs the zombie), so the zombie stands and
    /// walks on it as on any map. Built the first time, then only laid where the arena stands.
    /// </summary>
    public void Walkable(int agentTypeID)
    {
        if (laid.TryGetValue(agentTypeID, out var instance) && instance.valid) return;
        if (!walks.TryGetValue(agentTypeID, out var data) || data == null)
        {
            data = BuildWalk(agentTypeID);
            if (data == null) return;
            walks[agentTypeID] = data;
        }
        laid[agentTypeID] = NavMesh.AddNavMeshData(data, transform.position, transform.rotation);
    }

    private void OnDestroy()
    {
        Lift();
        foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
        meshes.Clear();
    }

    // Moved under a new showdown: the walk-mesh laid at the old place goes, to be laid again here when wanted.
    private void Stand(Vector3 center)
    {
        if ((transform.position - center).sqrMagnitude > .0001f)
        {
            Lift();
            transform.SetPositionAndRotation(center, Quaternion.identity);
        }
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        Lamps(true);
    }

    // Lit only while a showdown is on: out of use, nothing of it is drawn into a light's shadow or counted for a light.
    private void Lamps(bool on)
    {
        foreach (var lamp in lamps) if (lamp != null) lamp.enabled = on && lamp.intensity > 0f;
    }

    private void Lift()
    {
        foreach (var instance in laid.Values) if (instance.valid) instance.Remove();
        laid.Clear();
    }

    // The floor as the agent's walkable ground, nothing else: its edge, an agent's radius in from the wall, is as far as
    // a zombie walks, so none ever stands in the wall.
    private NavMeshData BuildWalk(int agentTypeID)
    {
        if (floor == null) return null;
        var settings = NavMesh.GetSettingsByID(agentTypeID);
        if (settings.agentTypeID != agentTypeID) settings = NavMesh.GetSettingsByIndex(0);
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = floor, transform = Matrix4x4.identity, area = 0 }
        };
        float across = Radius * 2f + 2f;
        var bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(across, 4f, across));
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data != null) data.name = "Showdown Arena Walk";
        return data;
    }

    // ---- Building ----

    private void Build()
    {
        float radius = Radius, height = Height;
        floor = Disc(radius, 0f, true, "Showdown Floor");
        Part("Floor", floor, FloorPaint, ShadowCastingMode.Off);
        Solid("Floor Solid", new Vector3(0f, -.5f, 0f), new Vector3(radius * 2f + 4f, 1f, radius * 2f + 4f), Quaternion.identity);
        // The wall and roof hold the light in and the sun out: they cast both ways.
        Part("Wall", Drum(radius, 0f, height, null, "Showdown Wall"), WallPaint, ShadowCastingMode.TwoSided);
        Part("Roof", Disc(radius + .2f, height, false, "Showdown Roof"), RoofPaint, ShadowCastingMode.TwoSided);
        Solid("Roof Solid", new Vector3(0f, height + .5f, 0f), new Vector3(radius * 2f + 4f, 1f, radius * 2f + 4f), Quaternion.identity);
        float panel = 2f * Mathf.PI * (radius + .5f) / Panels * 1.15f;
        for (int i = 0; i < Panels; i++)
        {
            float angle = i * Mathf.PI * 2f / Panels;
            var way = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Solid("Wall Solid", way * (radius + .5f) + Vector3.up * (height * .5f), new Vector3(panel, height + 2f, 1f), Quaternion.LookRotation(way));
        }
        // Lit lines round the wall: teal along its foot, gold higher up.
        var glow = FeelAssets.Particle(Texture2D.whiteTexture, true);
        Part("Foot Line", Drum(radius - .03f, .06f, .2f, FxShapes.With(PingMarks.BountyColor, .9f), "Showdown Foot Line"), glow, ShadowCastingMode.Off);
        Part("Gold Line", Drum(radius - .03f, height * .32f, height * .32f + .07f, FxShapes.With(Gold, .75f), "Showdown Gold Line"), glow, ShadowCastingMode.Off);
        Lights(radius, height);
    }

    // One hard light straight down on the floor's middle, and two rims from up the wall, teal and gold, crossing at it.
    private void Lights(float radius, float height)
    {
        float key = Mathf.Max(0f, Number("arenaKey", 120f)), rim = Mathf.Max(0f, Number("arenaRim", 25f));
        Lamp("Key Light", new Vector3(0f, height - .3f, 0f), Vector3.zero, new Color(1f, .95f, .88f), key, 108f, height + 6f, true);
        Vector3 middle = new Vector3(0f, 1.2f, 0f);
        Lamp("Teal Rim", new Vector3(radius * .85f, height * .7f, 0f), middle, PingMarks.BountyColor, rim, 70f, radius * 3f, false);
        Lamp("Gold Rim", new Vector3(-radius * .85f, height * .7f, 0f), middle, Gold, rim, 70f, radius * 3f, false);
    }

    private void Lamp(string name, Vector3 at, Vector3 lookAt, Color color, float intensity, float angle, float range, bool shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(transform, false);
        Vector3 way = (lookAt - at).normalized;
        light.transform.localPosition = at;
        light.transform.localRotation = Quaternion.LookRotation(way, Mathf.Abs(way.y) > .99f ? Vector3.forward : Vector3.up);
        light.type = LightType.Spot;
        light.spotAngle = angle;
        light.innerSpotAngle = angle * .45f;
        light.range = range;
        light.intensity = intensity;
        light.color = color;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.enabled = intensity > 0f;
        lamps.Add(light);
    }

    private void Part(string name, Mesh mesh, Material paint, ShadowCastingMode shadows)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(transform, false);
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = paint;
        renderer.shadowCastingMode = shadows;
        renderer.receiveShadows = paint != null && paint.renderQueue < 3000;
        meshes.Add(mesh);
    }

    private void Solid(string name, Vector3 center, Vector3 size, Quaternion turn)
    {
        var solid = new GameObject(name, typeof(BoxCollider));
        solid.transform.SetParent(transform, false);
        solid.transform.localPosition = center;
        solid.transform.localRotation = turn;
        solid.GetComponent<BoxCollider>().size = size;
    }

    // A flat round plate at height y, facing up (the floor, its UVs across it for the markings) or down (the roof).
    private static Mesh Disc(float radius, float y, bool up, string name)
    {
        var vertices = new Vector3[Sides + 1];
        var uvs = new Vector2[Sides + 1];
        var normals = new Vector3[Sides + 1];
        var triangles = new int[Sides * 3];
        vertices[0] = new Vector3(0f, y, 0f);
        uvs[0] = new Vector2(.5f, .5f);
        for (int i = 0; i < Sides; i++)
        {
            float angle = i * Mathf.PI * 2f / Sides, x = Mathf.Cos(angle), z = Mathf.Sin(angle);
            vertices[i + 1] = new Vector3(x * radius, y, z * radius);
            uvs[i + 1] = new Vector2(.5f + x * .5f, .5f + z * .5f);
        }
        for (int i = 0; i < normals.Length; i++) normals[i] = up ? Vector3.up : Vector3.down;
        for (int i = 0; i < Sides; i++)
        {
            int a = i + 1, b = (i + 1) % Sides + 1;
            // Clockwise as seen from the side it faces (Unity's front).
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = up ? b : a;
            triangles[i * 3 + 2] = up ? a : b;
        }
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A band of wall round the floor from height `from` to `to`, facing in: the wall itself, or a lit line on it (a
    // colour for its vertices, drawn in the additive glow).
    private static Mesh Drum(float radius, float from, float to, Color? tint, string name)
    {
        var vertices = new List<Vector3>((Sides + 1) * 2);
        var normals = new List<Vector3>((Sides + 1) * 2);
        var uvs = new List<Vector2>((Sides + 1) * 2);
        var colors = new List<Color32>((Sides + 1) * 2);
        var triangles = new List<int>(Sides * 6);
        Color32 shade = tint ?? Color.white;
        for (int i = 0; i <= Sides; i++)
        {
            float angle = i * Mathf.PI * 2f / Sides;
            var way = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(way * radius + Vector3.up * from);
            vertices.Add(way * radius + Vector3.up * to);
            normals.Add(-way);
            normals.Add(-way);
            float u = i / (float)Sides * 12f;
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
            colors.Add(shade);
            colors.Add(shade);
        }
        for (int i = 0; i < Sides; i++)
        {
            // Seen from the middle, side i+1 is to the left of side i: clockwise there is (i+1 low, i+1 high, i high).
            int v = i * 2;
            triangles.Add(v + 2); triangles.Add(v + 3); triangles.Add(v + 1);
            triangles.Add(v + 2); triangles.Add(v + 1); triangles.Add(v);
        }
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---- Paint ----

    // Dark concrete with a ring at the edge of the fight, a quieter one halfway in, a clock of ticks round the outer ring
    // and the bounty's diamond in the middle, made once.
    private static Material FloorPaint
    {
        get
        {
            if (floorPaint != null) return floorPaint;
            const int Size = 512;
            var marks = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "Showdown Floor Marks", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[Size * Size];
            Color teal = PingMarks.BountyColor;
            float pixel = 2f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + .5f) / Size * 2f - 1f, v = (y + .5f) / Size * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                float grain = Mathf.PerlinNoise(x * .04f, y * .04f) * .05f + Mathf.PerlinNoise(x * .31f + 17f, y * .31f) * .025f;
                float shade = .2f + grain - .06f * Mathf.Clamp01((r - .7f) / .3f);
                var color = new Color(shade, shade * .98f, shade * .95f, 1f);
                color = Color.Lerp(color, new Color(.42f, .42f, .41f), Stroke(Mathf.Abs(r - .86f), pixel * 1.4f, pixel));
                color = Color.Lerp(color, new Color(.3f, .3f, .3f), Stroke(Mathf.Abs(r - .5f), pixel * .8f, pixel));
                // Ticks round the outer ring, every fourth a long one.
                float turn = Mathf.Repeat(Mathf.Atan2(v, u) / (Mathf.PI * 2f) * 48f, 1f);
                bool major = Mathf.RoundToInt(Mathf.Atan2(v, u) / (Mathf.PI * 2f) * 48f) % 4 == 0;
                float tick = Stroke(Mathf.Min(turn, 1f - turn) * Mathf.PI * 2f * r / 48f, pixel * .9f, pixel)
                    * (r > .86f && r < (major ? .97f : .92f) ? 1f : 0f);
                color = Color.Lerp(color, new Color(.38f, .38f, .37f), tick);
                // The bounty's diamond.
                float diamond = Stroke(Mathf.Abs(Mathf.Abs(u) + Mathf.Abs(v) - .2f) * .7071f, pixel * 1.6f, pixel);
                color = Color.Lerp(color, Color.Lerp(color, teal, .7f), diamond);
                pixels[y * Size + x] = color;
            }
            marks.SetPixels32(pixels);
            marks.Apply(true, true);
            return floorPaint = Lit("Showdown Floor (Runtime)", Color.white, .16f, marks);
        }
    }

    private static Material WallPaint => wallPaint != null ? wallPaint : wallPaint = Lit("Showdown Wall (Runtime)", new Color(.07f, .075f, .085f), .22f);
    private static Material RoofPaint => roofPaint != null ? roofPaint : roofPaint = Lit("Showdown Roof (Runtime)", new Color(.02f, .02f, .024f), 0f);

    // A line `half` wide at `distance` from it, soft over one pixel.
    private static float Stroke(float distance, float half, float pixel) => Mathf.Clamp01(1f - (distance - half) / pixel);

    private static Material Lit(string name, Color color, float smoothness, Texture texture = null)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var made = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        if (texture != null)
        {
            if (made.HasProperty("_BaseMap")) made.SetTexture("_BaseMap", texture);
            if (made.HasProperty("_MainTex")) made.SetTexture("_MainTex", texture);
        }
        if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", color);
        if (made.HasProperty("_Color")) made.SetColor("_Color", color);
        if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", smoothness);
        return made;
    }
}
