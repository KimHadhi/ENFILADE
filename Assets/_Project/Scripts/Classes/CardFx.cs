using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The class cards' and special moments' looks in the world (Docs/Features/ClassCards.md). Every PC draws them: the card's player
/// at once, the others as CardNet tells them, so the one who did it sees what the others see (the user, 2026-11-12:
/// "내가 보는 화면이 진실이라서 남이 보는 세상과 … 똑같았으면"). Short and bright, nothing left lying.
/// Since the special-moment picks of 2026-11-12, most of them are MomentFx's (the restrained language, MomentLook):
/// <list type="bullet">
/// <item>Claws, a Ghost's White Tiger: three curved claw wounds raked across the head, no flash (pick A).</item>
/// <item>Flash, the gauntlet's flash charge: a star where it struck and the cone it stuns on the ground. It no longer
/// whitens the screen; the range shows on the Knight's own screen too (pick C).</item>
/// <item>Shell, a Broken Arrow round: its target drawn on the ground, a bright streak falling onto it, the target bursting
/// with the fuel can's blast (pick C).</item>
/// <item>Cavalry, the phantom riders: a wedge of gold chevron plates sliding along the line at their pace (pick B).</item>
/// <item>Shadow, a Knight's shadow double coming out: the Knight's own character beside them, violet-black and see-through,
/// for the shadow's seconds (BodyDouble).</item>
/// <item>Ultimate: someone's ultimate, with its notice on every HUD and the class's look (MomentFx.Ultimate).</item>
/// <item>Reversal, Cover deflect and Kata draw: the reversal's throw, a cover's turned blow, a Gun Kata draw.</item>
/// <item>Kata aura: a Point's Gun Kata on, its gold rounds turning round them for its seconds (MomentFx.KataAura).</item>
/// </list>
/// Drawn here as ever:
/// <list type="bullet">
/// <item>Echo, the shadow double striking again: a violet crescent with a white inner edge cuts across the spot, and a
/// fainter one trails it.</item>
/// <item>Deflect, a Knight's 비껴 흘리기: a white arc runs sideways off the parry, the blow slipping past.</item>
/// <item>Blast, the gauntlet's shot: a cone of hot streaks flies out of the fist with an orange puff and a bang.</item>
/// </list>
/// </summary>
public sealed class CardFx : MonoBehaviour
{
    public enum Kind : byte { Claws, Echo, Deflect, Flash, Blast, Shell, Cavalry, Shadow, Ultimate, Reversal, CoverDeflect, KataDraw, KataAura }

    private const int SampleRate = 44100;
    /// <summary>Cavalry: how fast they ride (m/s), how long they fade in and out (s); ClassCards sweeps at the same pace.</summary>
    public const float CavalrySpeed = 26f, CavalryIn = .08f, CavalryOut = .3f;
    /// <summary>A Shell: how long its streak takes to come down (s); ClassCards lands its blast then.</summary>
    public const float ShellFall = .3f;
    private static readonly Color Violet = new Color(.55f, .18f, 1f), Hot = new Color(1f, .78f, .42f);
    private static AudioClip boom;

    private Kind kind;
    private Vector3 at, direction;
    private float value, age, life;
    private int seed;
    private Mesh glow, matter;
    private readonly List<Vector3> glowVertices = new List<Vector3>(), matterVertices = new List<Vector3>();
    private readonly List<Color32> glowColors = new List<Color32>(), matterColors = new List<Color32>();
    private readonly List<int> glowTriangles = new List<int>(), matterTriangles = new List<int>();
    private Light flare;
    private Vector3 right, up, forward;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => boom = null;

    /// <summary>
    /// Draws <paramref name="kind"/> at <paramref name="at"/>, going or facing <paramref name="direction"/>;
    /// <paramref name="value"/> is its size (Claws, Flash: m), its reach (Blast, Cavalry: m), its blast's reach (Shell: m),
    /// the double's seconds (Shadow) or the class's number (Ultimate, MomentLook.IndexOf). <paramref name="mine"/>: this
    /// PC's own player made it.
    /// </summary>
    public static void Play(Kind kind, Vector3 at, Vector3 direction, float value, bool mine)
    {
        var network = GameSession.Network;
        Play(kind, at, direction, value, mine, network != null ? network.LocalClientId : 0ul);
    }

    /// <summary>As Play above; <paramref name="from"/> is the client whose player made it (CardNet).</summary>
    public static void Play(Kind kind, Vector3 at, Vector3 direction, float value, bool mine, ulong from)
    {
        if (!Application.isPlaying) return;
        Vector3 way = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        switch (kind)
        {
            case Kind.Claws:
                MomentFx.Play(MomentFx.Kind.Claws, at, way, value, Color.white);
                return;
            case Kind.Flash:
                MomentFx.Play(MomentFx.Kind.Flash, at, way, value, Color.white);
                return;
            case Kind.Shell:
                MomentFx.Play(MomentFx.Kind.Shell, at, way, value, Color.white);
                return;
            case Kind.Cavalry:
                MomentFx.Play(MomentFx.Kind.Wedge, at, way, value, MomentLook.ColorOf(PlayerClass.Assault));
                return;
            case Kind.Shadow:
            {
                var knight = PlayerOf(from, mine);
                if (knight != null) BodyDouble.Show(knight, value > 0f ? value : 6f);
                return;
            }
            case Kind.Ultimate:
            {
                var who = PlayerOf(from, mine);
                if (who != null) MomentFx.Ultimate(who, MomentLook.ClassAt(Mathf.RoundToInt(value)), mine);
                return;
            }
            case Kind.Reversal:
                MomentFx.Play(MomentFx.Kind.Reversal, at, way, 0f, KitStyle.Accent);
                MomentSound.Play("reversal", mine ? (Vector3?)null : at);
                return;
            case Kind.CoverDeflect:
                MomentFx.Play(MomentFx.Kind.CoverDeflect, at, way, 0f, MomentLook.ColorOf(PlayerClass.Knight));
                MomentSound.Play("cover", mine ? (Vector3?)null : at);
                return;
            case Kind.KataDraw:
                MomentFx.Play(MomentFx.Kind.KataDraw, at, way, 0f, MomentLook.ColorOf(PlayerClass.Point));
                MomentSound.Play("kata", mine ? (Vector3?)null : at, .55f);
                return;
            case Kind.KataAura:
            {
                var point = PlayerOf(from, mine);
                if (point != null) MomentFx.KataAura(point, value, mine);
                return;
            }
        }
        var host = FeelAssets.CreateRuntimeRoot("Card " + kind);
        var fx = host.AddComponent<CardFx>();
        fx.kind = kind;
        fx.at = at;
        fx.direction = way;
        fx.value = value;
        fx.seed = Mathf.Abs(Mathf.RoundToInt(at.x * 73f + at.y * 151f + at.z * 37f)) % 9973;
        fx.life = kind switch
        {
            Kind.Echo => .28f,
            Kind.Deflect => .32f,
            Kind.Blast => .24f,
            _ => .5f
        };
        fx.Begin();
    }

    // The player whose PC sent it: this PC's own for its own, else the one that client owns.
    private static PlayerMovement PlayerOf(ulong client, bool mine)
    {
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null) continue;
            if (mine) { if (someone.IsLocal && !someone.IsNetworkDriven) return someone; continue; }
            if (someone.TryGetComponent(out NetworkObject owner) && owner.IsSpawned && owner.OwnerClientId == client) return someone;
        }
        return mine ? (ClassKit.Local != null ? ClassKit.Local.Movement : null) : null;
    }

    private void Begin()
    {
        glow = Layer("Glow", true);
        matter = Layer("Matter", false);
        if (kind == Kind.Blast)
        {
            flare = new GameObject("Light").AddComponent<Light>();
            flare.transform.SetParent(transform, false);
            flare.transform.position = at;
            flare.type = LightType.Point;
            flare.shadows = LightShadows.None;
            flare.color = Hot;
            flare.range = 5f;
            flare.intensity = 0f;
            GameAudio.PlayAt(Boom(), at, AudioChannel.Combat, .55f, 3f, 50f);
        }
        Draw();
    }

    private Mesh Layer(string name, bool additive)
    {
        var host = new GameObject(name);
        host.transform.SetParent(transform, false);
        var mesh = new Mesh { name = "Card " + kind + " " + name, hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, additive);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return mesh;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy()
    {
        if (glow != null) Destroy(glow);
        if (matter != null) Destroy(matter);
    }

    private void Draw()
    {
        var view = Camera.main;
        if (view == null) return;
        right = view.transform.right;
        up = view.transform.up;
        forward = view.transform.forward;
        glowVertices.Clear(); glowColors.Clear(); glowTriangles.Clear();
        matterVertices.Clear(); matterColors.Clear(); matterTriangles.Clear();
        switch (kind)
        {
            case Kind.Echo: Echo(view); break;
            case Kind.Deflect: Deflect(view); break;
            case Kind.Blast: Blast(); break;
        }
        Apply(glow, glowVertices, glowColors, glowTriangles);
        Apply(matter, matterVertices, matterColors, matterTriangles);
    }

    private void Apply(Mesh mesh, List<Vector3> vertices, List<Color32> colors, List<int> triangles)
    {
        mesh.Clear();
        if (vertices.Count == 0) return;
        for (int i = 0; i < vertices.Count; i++) vertices[i] = transform.InverseTransformPoint(vertices[i]);
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    // How big a thing must be drawn to read at its distance (m): a shape a tenth of the distance across, at least so much.
    private float Readable(Camera view, float least, float share)
        => Mathf.Max(least, Vector3.Distance(view.transform.position, at) * share);

    // ---- The looks ----

    // A violet crescent with a white inner edge, and a fainter one a step behind.
    private void Echo(Camera view)
    {
        float size = Readable(view, .7f, .06f), k = Mathf.Clamp01(age / .1f), fade = 1f - Mathf.Clamp01((age - .1f) / .18f);
        Vector3 swing = Vector3.ProjectOnPlane(direction, forward);
        swing = swing.sqrMagnitude > 1e-4f ? swing.normalized : right;
        Vector3 bend = Vector3.Cross(forward, swing).normalized;
        for (int copy = 0; copy < 2; copy++)
        {
            float lag = copy * .3f, reach = Mathf.Clamp01(k - lag) / (1f - lag);
            if (reach <= 0f) continue;
            float strength = fade * (copy == 0 ? 1f : .45f);
            Vector3 offset = -swing * size * .18f * copy;
            Vector3 from = at + offset - swing * size * .6f - bend * size * .1f, to = at + offset + swing * size * .6f - bend * size * .1f;
            Vector3 end = Vector3.Lerp(from, to, reach);
            Blade(matterVertices, matterColors, matterTriangles, from, end, bend * size * .35f, size * .12f, With(new Color(.12f, .02f, .2f), .7f * strength), 14);
            Blade(glowVertices, glowColors, glowTriangles, from, end, bend * size * .35f, size * .09f, With(Violet, .9f * strength), 14);
            Blade(glowVertices, glowColors, glowTriangles, from, end, bend * size * .3f, size * .025f, With(Color.white, strength), 14);
        }
    }

    // A white arc running off sideways from the parry.
    private void Deflect(Camera view)
    {
        float size = Readable(view, .6f, .05f), k = Mathf.Clamp01(age / .12f), fade = 1f - Mathf.Clamp01((age - .1f) / .22f);
        Vector3 away = Vector3.ProjectOnPlane(direction, forward);
        away = away.sqrMagnitude > 1e-4f ? away.normalized : right;
        Vector3 bend = Vector3.Cross(forward, away).normalized;
        Vector3 from = at - bend * size * .25f, to = at + away * size * 1.1f + bend * size * .2f;
        Blade(glowVertices, glowColors, glowTriangles, from, Vector3.Lerp(from, to, k), bend * size * .3f, size * .08f, With(Color.white, .9f * fade), 12);
        Blade(glowVertices, glowColors, glowTriangles, from, Vector3.Lerp(from, to, k), bend * size * .3f, size * .16f, With(KitStyle.Accent, .35f * fade), 12);
    }

    // Hot streaks flying out of the fist in a cone, and a puff at it.
    private void Blast()
    {
        float reach = Mathf.Max(1f, value), k = Mathf.Clamp01(age / .09f), fade = 1f - Mathf.Clamp01((age - .06f) / .18f);
        var random = new System.Random(seed);
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        if (side.sqrMagnitude < .5f) side = right;
        Vector3 lift = Vector3.Cross(direction, side).normalized;
        for (int i = 0; i < 12; i++)
        {
            float a = ((float)random.NextDouble() - .5f) * .7f, b = ((float)random.NextDouble() - .5f) * .45f;
            Vector3 way = (direction + side * a + lift * b).normalized;
            float far = reach * (.7f + .3f * (float)random.NextDouble());
            Vector3 head = at + way * far * k, tail = at + way * Mathf.Max(0f, far * k - .9f);
            Blade(glowVertices, glowColors, glowTriangles, tail, head, Vector3.zero, .025f, With(Hot, fade), 2);
        }
        float puff = Mathf.Clamp01(1f - age / .12f);
        if (puff > 0f) Fan(glowVertices, glowColors, glowTriangles, at + direction * .3f, .5f + .6f * (1f - puff), right, up, 16,
            With(Color.white, .9f * puff), With(Hot, 0f));
        if (flare != null) flare.intensity = 5f * fade;
    }

    // ---- Shapes ----

    // A blade from a to b, bowed by `bow` at its middle, `width` across at its widest and tapering to points.
    private static void Blade(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 a, Vector3 b, Vector3 bow,
        float width, Color color, int steps)
    {
        Vector3 along = b - a;
        if (along.sqrMagnitude < 1e-6f || width <= 0f || color.a <= .004f) return;
        var view = Camera.main;
        Vector3 eye = view != null ? view.transform.forward : Vector3.forward;
        Vector3 side = Vector3.Cross(eye, along).normalized;
        if (side.sqrMagnitude < .5f) side = Vector3.Cross(Vector3.up, along).normalized;
        int first = vertices.Count;
        Color32 c = color;
        steps = Mathf.Max(1, steps);
        for (int i = 0; i <= steps; i++)
        {
            float s = i / (float)steps;
            Vector3 point = a + along * s + bow * (4f * s * (1f - s));
            // Long blades taper to points; a few-step one (a limb, a lance, a streak) keeps its width.
            float half = width * .5f * (steps < 6 ? 1f : Mathf.Sin(Mathf.PI * Mathf.Clamp(s, .04f, .96f)));
            vertices.Add(point + side * half);
            vertices.Add(point - side * half);
            colors.Add(c); colors.Add(c);
        }
        for (int i = 0; i < steps; i++)
        {
            int v = first + i * 2;
            triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 1);
            triangles.Add(v + 1); triangles.Add(v + 2); triangles.Add(v + 3);
        }
    }

    // A round flat shape facing along right × up, bright in the middle and fading to its rim.
    private static void Fan(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 middle, float radius,
        Vector3 right, Vector3 up, int segments, Color inner, Color rim)
    {
        if (inner.a <= .004f && rim.a <= .004f) return;
        int first = vertices.Count;
        vertices.Add(middle);
        colors.Add(inner);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices.Add(middle + right * radius * Mathf.Cos(angle) + up * radius * Mathf.Sin(angle));
            colors.Add(rim);
        }
        for (int i = 0; i < segments; i++)
        {
            triangles.Add(first);
            triangles.Add(first + 1 + (i + 1) % segments);
            triangles.Add(first + 1 + i);
        }
    }

    private static Color With(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }

    // ---- The sound ----

    /// <summary>A shell's or the gauntlet's bang, made here once: a falling thump under a short rush of dulled noise.</summary>
    public static AudioClip Boom()
    {
        if (boom != null) return boom;
        var data = new float[(int)(SampleRate * 1.1f)];
        var random = new System.Random(5113);
        float held = 0f, turn = 0f, top = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            turn += 2f * Mathf.PI * Mathf.Lerp(95f, 32f, Mathf.Clamp01(t / .3f)) / SampleRate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * Mathf.Lerp(.65f, .05f, Mathf.Clamp01(t / .35f));
            float crack = t < .012f ? ((float)random.NextDouble() * 2f - 1f) * (1f - t / .012f) : 0f;
            data[i] = Mathf.Sin(turn) * Mathf.Exp(-t / .2f) * .95f + held * Mathf.Exp(-t / .3f) * .85f + crack * .7f;
            top = Mathf.Max(top, Mathf.Abs(data[i]));
        }
        int fade = SampleRate / 40;
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] / Mathf.Max(.001f, top) * .9f * Mathf.Min(1f, (data.Length - 1 - i) / (float)fade);
        boom = AudioClip.Create("Card Boom", data.Length, 1, SampleRate, false);
        boom.SetData(data, 0);
        boom.hideFlags = HideFlags.HideAndDontSave;
        return boom;
    }
}
