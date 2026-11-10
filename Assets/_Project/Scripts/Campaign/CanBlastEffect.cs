using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A shot fuel can's blast and the fire it leaves, drawn here as flat cut shapes instead of bought effect prefabs. The
/// user found those too much, "like a gun skill's effect", and asked for both in the game's own style (2026-11-10);
/// this is their pick of the second drafts (draft B: the restrained blast of the first round's C, shown the way its A
/// was). Everything moves in held frames, twelve a second: the screen goes white for one of them (less the farther
/// off); a jagged shape bursts out white and then yellow, a new outline each frame; a thin white ring runs over the
/// floor to the blast's reach; sparks fly up and out; six black puffs of smoke open round it and rise. Then the fire:
/// a low orange glow on the floor and blades of flame standing in it, each an orange blade with a yellow one inside,
/// re-cut ten times a second, until the can's fire goes out (PutOut). A light goes with both. Shapes face the camera;
/// the ring and the glow lie on the floor. CampaignCans makes one where a can blows up, on every PC.
/// </summary>
[DisallowMultipleComponent]
public sealed class CanBlastEffect : MonoBehaviour
{
    // Held frames a second for the blast and for the fire, and when the fire starts and how long it takes to come up
    // and to go out (s).
    private const float BlastRate = 12f, FireRate = 10f, FireFrom = .55f, FireIn = .4f, FireOut = .45f, BlastSeconds = 1.4f;
    // The blast (m): the can's middle over the floor, the burst's radius from first to last, the smoke's ring and rise
    // and its puffs from first to last, the sparks' start, flight, length and thickness, the ring's line.
    private const float Middle = .35f, BurstFirst = .42f, BurstLast = 1.6f, SmokeRing = 1.5f, SmokeRise = 1.15f, PuffFirst = .3f, PuffLast = .65f,
        SparkFrom = .55f, SparkFlight = 2.9f, SparkLength = .34f, SparkWidth = .06f, RingLine = .07f;
    // The white frame: how long (real s), its most, and how far off it is gone (m).
    private const float WhiteSeconds = .085f, WhiteMost = .8f, WhiteReach = 26f;
    // The fire (m): a blade's width and height, and how many stand in it.
    private const float BladeWidth = .6f, BladeHeight = 1.3f;
    private const int Blades = 12, Puffs = 6, Sparks = 10, BurstPoints = 9, RingSegments = 40, DiscSegments = 24;
    private static readonly Color32 Smoke = new Color32(11, 11, 13, 255), Hot = new Color32(255, 255, 255, 255), Warm = new Color32(255, 210, 122, 255),
        Spark = new Color32(255, 224, 122, 255), Flame = new Color32(255, 138, 61, 255), Core = new Color32(255, 224, 122, 255);

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Vector3> rim = new List<Vector3>();
    private Mesh mesh;
    private Light glow;
    private GameObject white;
    private Image whiteLid;
    private float age, whiteAge, blastReach = 5f, fireReach = 3f, whiteStrength, outAt = -1f;
    private int drawnStep = -1;

    /// <summary>
    /// A can blows up here: the blast out to blastRadius, then a fire fireRadius wide until PutOut. With no fireRadius
    /// there is only the blast, and the effect removes itself after it (an exploder's burst, ZombieExploder).
    /// </summary>
    public static CanBlastEffect Play(Vector3 at, float blastRadius, float fireRadius)
    {
        var host = new GameObject("Can Blast");
        host.transform.position = at;
        var effect = host.AddComponent<CanBlastEffect>();
        effect.blastReach = blastRadius;
        effect.fireReach = fireRadius;
        effect.Begin();
        return effect;
    }

    /// <summary>The fire dies down and the effect removes itself.</summary>
    public void PutOut()
    {
        if (outAt < 0f) outAt = age;
    }

    private void Begin()
    {
        mesh = new Mesh { name = "Can Blast", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        // Flat colour from the vertices, blended over the world: matter, not light.
        renderer.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        glow = new GameObject("Light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = Vector3.up * 1f;
        glow.type = LightType.Point;
        glow.shadows = LightShadows.None;
        glow.range = blastReach * 3f;

        var view = Camera.main;
        float away = view != null ? Vector3.Distance(view.transform.position, transform.position) : WhiteReach;
        whiteStrength = WhiteMost * (1f - Mathf.Clamp01(away / WhiteReach));
        if (whiteStrength > .02f) BuildWhite();
        Draw();
    }

    private void BuildWhite()
    {
        white = new GameObject("Can Blast White", typeof(RectTransform), typeof(Canvas));
        var canvas = white.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var lid = FeelUI.Rect("White", white.transform);
        lid.anchorMin = Vector2.zero; lid.anchorMax = Vector2.one;
        lid.offsetMin = lid.offsetMax = Vector2.zero;
        whiteLid = lid.gameObject.AddComponent<Image>();
        whiteLid.raycastTarget = false;
        whiteLid.color = new Color(1f, 1f, 1f, whiteStrength);
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (white != null) Destroy(white);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (white != null)
        {
            // Real time: a white frame is a frame, however slow the game is running.
            whiteAge += Time.unscaledDeltaTime;
            if (whiteAge >= WhiteSeconds) { Destroy(white); white = null; }
            else whiteLid.color = new Color(1f, 1f, 1f, whiteStrength * (1f - whiteAge / WhiteSeconds));
        }
        if (outAt >= 0f && age - outAt >= FireOut) { Destroy(gameObject); return; }
        if (fireReach <= 0f && age >= BlastSeconds) { Destroy(gameObject); return; }
        Draw();
    }

    // Redrawn only when the held frame changes: the blast's while it lasts, then the fire's.
    private void Draw()
    {
        int step = age < BlastSeconds ? Mathf.FloorToInt(age * BlastRate) : 1000 + Mathf.FloorToInt(age * FireRate);
        float fire = fireReach <= 0f ? 0f
            : Mathf.Clamp01((age - FireFrom) / FireIn) * (outAt < 0f ? 1f : 1f - Mathf.Clamp01((age - outAt) / FireOut));
        Shine(fire, step);
        if (step == drawnStep) return;
        drawnStep = step;
        vertices.Clear(); colors.Clear(); triangles.Clear();
        var view = Camera.main;
        Vector3 right = view != null ? view.transform.right : Vector3.right, up = view != null ? view.transform.up : Vector3.up;
        Vector3 flat = Vector3.ProjectOnPlane(right, Vector3.up);
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.right;
        if (fire > 0f) Fire(fire, flat, Mathf.FloorToInt(age * FireRate));
        if (age < BlastSeconds) Blast(Mathf.Floor(age * BlastRate) / BlastRate, Mathf.FloorToInt(age * BlastRate), right, up);
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    // The blast at held time e: smoke behind, the burst over it, the ring on the floor, sparks last.
    private void Blast(float e, int seed, Vector3 right, Vector3 up)
    {
        Vector3 middle = Vector3.up * Middle;
        float spread = Part(e, .1f, 1f), smoke = (1f - Part(e, .6f, 1.3f)) * .9f;
        if (smoke > 0f)
            for (int i = 0; i < Puffs; i++)
            {
                float a = i / (float)Puffs * Mathf.PI * 2f + .5f, ring = spread * SmokeRing;
                Vector3 at = middle + right * (Mathf.Cos(a) * ring * .8f) + up * (SmokeRise * spread - Mathf.Sin(a) * ring * .42f);
                Disc(at, right, up, Mathf.Lerp(PuffFirst, PuffLast, Part(e, .1f, .6f)), 10, Tint(Smoke, smoke));
            }

        float burst = 1f - Part(e, .25f, .5f);
        if (burst > 0f)
        {
            float size = Mathf.Lerp(BurstFirst, BurstLast, Part(e, 0f, .34f));
            rim.Clear();
            for (int i = 0; i < BurstPoints; i++)
            {
                float a = i / (float)BurstPoints * Mathf.PI * 2f, r = size * (.78f + Noise(i + seed) * .4f);
                rim.Add(middle + right * (Mathf.Cos(a) * r) + up * (Mathf.Sin(a) * r * .82f));
            }
            Fan(middle, rim, Tint(e < .09f ? Hot : Warm, burst));
        }

        float wave = 1f - Part(e, 0f, .34f);
        if (wave > 0f) Ring(Mathf.Lerp(.5f, blastReach, EaseOut(Part(e, 0f, .3f))), RingLine, Tint(Hot, wave));

        float sparks = 1f - Part(e, .17f, .5f);
        if (sparks > 0f)
            for (int i = 0; i < Sparks; i++)
            {
                // Up and out, never down into the floor.
                float a = Mathf.PI * (.05f + .9f * Noise(i)), from = SparkFrom + Part(e, 0f, .45f) * SparkFlight * (.5f + Noise(i + 1));
                Vector3 way = right * Mathf.Cos(a) + up * Mathf.Sin(a), across = Vector3.Cross(way, Vector3.Cross(right, up)).normalized * (SparkWidth * .5f);
                Vector3 a0 = middle + way * from, a1 = middle + way * (from + SparkLength);
                Quad(a0 - across, a0 + across, a1 + across, a1 - across, Tint(Spark, sparks));
            }
    }

    // The fire: a glow on the floor and the blades standing in it, facing the camera round their own feet.
    private void Fire(float strength, Vector3 right, int seed)
    {
        rim.Clear();
        float glowReach = fireReach * .95f;
        for (int i = 0; i < DiscSegments; i++)
        {
            float a = i / (float)DiscSegments * Mathf.PI * 2f;
            rim.Add(new Vector3(Mathf.Cos(a) * glowReach, .04f, Mathf.Sin(a) * glowReach));
        }
        Fan(new Vector3(0f, .04f, 0f), rim, Tint(Flame, .24f * strength));

        for (int i = 0; i < Blades; i++)
        {
            // Where each stands never changes (a sunflower's seeds); how tall it is and where its tip leans does, each step.
            float turn = i * 2.39996f, off = fireReach * .8f * Mathf.Sqrt((i + .5f) / Blades);
            Vector3 foot = new Vector3(Mathf.Cos(turn) * off, .05f, Mathf.Sin(turn) * off);
            float height = BladeHeight * (.55f + Noise(seed + i * 13) * .6f), lean = (Noise(seed + i * 5) - .5f) * BladeWidth;
            Vector3 half = right * (BladeWidth * .5f), tip = foot + right * lean + Vector3.up * height;
            Triangle(foot - half, foot + half, tip, Tint(Flame, strength));
            Triangle(foot - half * .5f, foot + half * .5f, foot + right * (lean * .6f) + Vector3.up * (height * .55f), Tint(Core, strength));
        }
    }

    // The light they throw: white-hot at the burst and gone in a third of a second, then the fire's, wavering by the step.
    private void Shine(float fire, int step)
    {
        float burst = 1f - Mathf.Clamp01(age / .35f);
        float waver = .8f + .4f * Noise(step);
        glow.color = Color.Lerp(new Color(1f, .62f, .3f), Color.white, burst);
        glow.intensity = burst * 9f + fire * 2.4f * waver;
        glow.enabled = glow.intensity > .01f;
    }

    private static float Part(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
    // The same every time for the same number: a shape drawn from it can be held for a frame and drawn again.
    private static float Noise(int n) => Mathf.Repeat(Mathf.Sin(n * 127.1f + 311.7f) * 43758.5453f, 1f);

    private static Color32 Tint(Color32 color, float alpha)
    {
        color.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
        return color;
    }

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, Color32 color)
    {
        int first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        colors.Add(color); colors.Add(color); colors.Add(color);
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
    {
        int first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        for (int i = 0; i < 4; i++) colors.Add(color);
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
    }

    // A filled shape: its middle and the points round it, in order.
    private void Fan(Vector3 middle, List<Vector3> round, Color32 color)
    {
        int first = vertices.Count;
        vertices.Add(middle); colors.Add(color);
        foreach (var point in round) { vertices.Add(point); colors.Add(color); }
        for (int i = 0; i < round.Count; i++)
        {
            triangles.Add(first); triangles.Add(first + 1 + i); triangles.Add(first + 1 + (i + 1) % round.Count);
        }
    }

    private void Disc(Vector3 at, Vector3 right, Vector3 up, float radius, int sides, Color32 color)
    {
        rim.Clear();
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            rim.Add(at + right * (Mathf.Cos(a) * radius) + up * (Mathf.Sin(a) * radius));
        }
        Fan(at, rim, color);
    }

    // A thin band round the middle, lying on the floor.
    private void Ring(float radius, float line, Color32 color)
    {
        int first = vertices.Count;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i / (float)RingSegments * Mathf.PI * 2f;
            var away = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices.Add(away * (radius - line * .5f) + Vector3.up * .06f); colors.Add(color);
            vertices.Add(away * (radius + line * .5f) + Vector3.up * .06f); colors.Add(color);
        }
        for (int i = 0; i < RingSegments; i++)
        {
            int here = first + i * 2, next = first + (i + 1) % RingSegments * 2;
            triangles.Add(here); triangles.Add(here + 1); triangles.Add(next + 1);
            triangles.Add(here); triangles.Add(next + 1); triangles.Add(next);
        }
    }
}
