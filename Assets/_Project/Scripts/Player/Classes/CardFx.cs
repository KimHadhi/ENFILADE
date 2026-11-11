using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The class cards' looks in the world (Docs/ClassCards.md), drawn here as flat cut shapes facing the camera, as the
/// game's other effects are (CanBlastEffect, TauntRing): short and bright, nothing left lying (the user's rule for hit
/// and confirm effects). Every PC draws them, the card's player at once and the others as CardNet tells them.
/// <list type="bullet">
/// <item>Claws, a Ghost's White Tiger: three white blades rake across the head one after another, pale blue at their
/// edges, over a white flash.</item>
/// <item>Echo, a Knight's shadow double striking again: a violet crescent with a white inner edge cuts across the
/// spot, and a fainter one trails it.</item>
/// <item>Deflect, a Knight's 비껴 흘리기: a white arc runs sideways off the parry, the blow slipping past.</item>
/// <item>Flash, the gauntlet's flash charge: a white star bursts and a light flares; near enough, the screen blanches
/// for a moment.</item>
/// <item>Blast, the gauntlet's shot: a cone of hot streaks flies out of the fist with an orange puff and a bang.</item>
/// <item>Shell, a Broken Arrow round: a white streak drops out of the sky and the can's blast goes off where it
/// lands, with a boom.</item>
/// <item>Cavalry, an Assault's phantom riders: a wedge of five pink horsemen with white lances gallops along the line,
/// seen side on from wherever they are watched (as the old shooters drew their sprites), leaving two lines of light on
/// the ground.</item>
/// <item>Shadow, a Knight's shadow double coming out: violet wisps leap up round them over a dark ring.</item>
/// </list>
/// </summary>
public sealed class CardFx : MonoBehaviour
{
    public enum Kind : byte { Claws, Echo, Deflect, Flash, Blast, Shell, Cavalry, Shadow }

    private const int SampleRate = 44100, Riders = 5;
    /// <summary>Cavalry: how fast they ride (m/s), how long they fade in and out (s); ClassCards sweeps at the same pace.</summary>
    public const float CavalrySpeed = 26f, CavalryIn = .08f, CavalryOut = .3f;
    /// <summary>A Shell: how long its streak takes to come down (s); ClassCards lands its blast then.</summary>
    public const float ShellFall = .3f;
    private static readonly Color Violet = new Color(.55f, .18f, 1f), Blue = new Color(.62f, .86f, 1f), Hot = new Color(1f, .78f, .42f);
    private static AudioClip boom;

    private Kind kind;
    private Vector3 at, direction;
    private float value, age, life;
    private bool mine, blasted;
    private int seed;
    private Mesh glow, matter;
    private readonly List<Vector3> glowVertices = new List<Vector3>(), matterVertices = new List<Vector3>();
    private readonly List<Color32> glowColors = new List<Color32>(), matterColors = new List<Color32>();
    private readonly List<int> glowTriangles = new List<int>(), matterTriangles = new List<int>();
    private Light flare;
    private Vector3 right, up, forward;
    // The flash charge's blanched screen (this PC's own flash only), fading over its first moments.
    private GameObject blanch;
    private UnityEngine.UI.Image blanchLid;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => boom = null;

    /// <summary>
    /// Draws <paramref name="kind"/> at <paramref name="at"/>, going or facing <paramref name="direction"/>;
    /// <paramref name="value"/> is its size (Claws, Flash: m), its reach (Blast, Cavalry: m) or its blast's reach
    /// (Shell: m). <paramref name="mine"/>: this PC's own player made it (the flash blanches only their screen).
    /// </summary>
    public static void Play(Kind kind, Vector3 at, Vector3 direction, float value, bool mine)
    {
        if (!Application.isPlaying) return;
        var host = FeelAssets.CreateRuntimeRoot("Card " + kind);
        var fx = host.AddComponent<CardFx>();
        fx.kind = kind;
        fx.at = at;
        fx.direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        fx.value = value;
        fx.mine = mine;
        fx.seed = Mathf.Abs(Mathf.RoundToInt(at.x * 73f + at.y * 151f + at.z * 37f)) % 9973;
        fx.life = kind switch
        {
            Kind.Claws => .5f,
            Kind.Echo => .28f,
            Kind.Deflect => .32f,
            Kind.Flash => .26f,
            Kind.Blast => .24f,
            Kind.Shell => ShellFall + .1f,
            Kind.Cavalry => Mathf.Max(.2f, value) / CavalrySpeed + CavalryIn + CavalryOut,
            _ => .5f
        };
        fx.Begin();
    }

    private void Begin()
    {
        glow = Layer("Glow", true);
        matter = Layer("Matter", false);
        if (kind == Kind.Flash || kind == Kind.Blast || kind == Kind.Claws)
        {
            flare = new GameObject("Light").AddComponent<Light>();
            flare.transform.SetParent(transform, false);
            flare.transform.position = at;
            flare.type = LightType.Point;
            flare.shadows = LightShadows.None;
            flare.color = kind == Kind.Blast ? Hot : kind == Kind.Claws ? Blue : Color.white;
            flare.range = kind == Kind.Flash ? 9f : 5f;
            flare.intensity = 0f;
        }
        if (kind == Kind.Blast) GameAudio.PlayAt(Boom(), at, AudioChannel.Combat, .55f, 3f, 50f);
        if (kind == Kind.Flash && mine) Blanch();
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
        if (blanchLid != null) blanchLid.color = new Color(1f, 1f, 1f, .45f * (1f - Mathf.Clamp01(age / .16f)));
        if (kind == Kind.Shell && !blasted && age >= ShellFall)
        {
            blasted = true;
            CanBlastEffect.Play(at, Mathf.Max(1f, value), 0f);
            GameAudio.PlayAt(Boom(), at, AudioChannel.Combat, 1f, 5f, 90f);
        }
        if (age >= life) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy()
    {
        if (glow != null) Destroy(glow);
        if (matter != null) Destroy(matter);
        if (blanch != null) Destroy(blanch);
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
        float t = age / Mathf.Max(.01f, life);
        switch (kind)
        {
            case Kind.Claws: Claws(view); break;
            case Kind.Echo: Echo(view); break;
            case Kind.Deflect: Deflect(view); break;
            case Kind.Flash: Flash(view, t); break;
            case Kind.Blast: Blast(view); break;
            case Kind.Shell: Shell(); break;
            case Kind.Cavalry: Cavalry(view); break;
            case Kind.Shadow: Shadow(t); break;
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

    // Three blades raked across the head, top right to bottom left, one after another.
    private void Claws(Camera view)
    {
        float size = Readable(view, 1.1f, .09f);
        Vector3 across = (right * -.55f - up * 1f).normalized, side = Vector3.Cross(forward, across).normalized;
        float flash = Mathf.Clamp01(1f - age / .14f);
        if (flash > 0f) Fan(glowVertices, glowColors, glowTriangles, at, size * .9f * (1f + .3f * (1f - flash)), right, up, 20,
            With(Color.white, .8f * flash), With(Blue, 0f));
        if (flare != null) flare.intensity = 6f * flash;
        for (int i = 0; i < 3; i++)
        {
            float start = i * .045f, k = Mathf.Clamp01((age - start) / .07f);
            if (k <= 0f) continue;
            float fade = 1f - Mathf.Clamp01((age - .2f - start) / .25f);
            if (fade <= 0f) continue;
            Vector3 middle = at + side * (i - 1) * size * .28f;
            Vector3 from = middle - across * size * .55f, to = middle + across * size * .55f;
            // The tip runs from the top down to the end; the blade bows a little toward the next one.
            Blade(glowVertices, glowColors, glowTriangles, from, Vector3.Lerp(from, to, k), side * size * .06f, size * .07f,
                With(Blue, .55f * fade), 10);
            Blade(glowVertices, glowColors, glowTriangles, from, Vector3.Lerp(from, to, k), side * size * .06f, size * .03f,
                With(Color.white, fade), 10);
        }
    }

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

    // A white star and a flare of light.
    private void Flash(Camera view, float t)
    {
        float size = Mathf.Max(1f, value) * (.6f + .6f * Mathf.Sqrt(t)), fade = 1f - t;
        Fan(glowVertices, glowColors, glowTriangles, at, size, right, up, 24, With(Color.white, fade), With(Color.white, 0f));
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f + seed * .1f;
            Vector3 ray = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            Blade(glowVertices, glowColors, glowTriangles, at, at + ray * size * (i % 2 == 0 ? 1.7f : 1.1f), Vector3.zero, size * .06f,
                With(Color.white, .9f * fade), 4);
        }
        if (flare != null) flare.intensity = 9f * fade;
    }

    // Hot streaks flying out of the fist in a cone, and a puff at it.
    private void Blast(Camera view)
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

    // A white streak dropping out of the sky onto the spot (the blast then is CanBlastEffect's).
    private void Shell()
    {
        float k = Mathf.Clamp01(age / ShellFall);
        Vector3 sky = at + Vector3.up * 45f + direction * 6f;
        Vector3 head = Vector3.Lerp(sky, at, k * k), tail = Vector3.Lerp(sky, at, Mathf.Max(0f, k * k - .25f));
        Blade(glowVertices, glowColors, glowTriangles, tail, head, Vector3.zero, .18f, With(Color.white, .95f), 2);
        Blade(glowVertices, glowColors, glowTriangles, tail, head, Vector3.zero, .45f, With(KitStyle.Accent, .35f), 2);
    }

    // Violet wisps leaping up round the Knight over a dark ring.
    private void Shadow(float t)
    {
        float fade = 1f - t, reach = 1.1f + .6f * t;
        Ring(matterVertices, matterColors, matterTriangles, at + Vector3.up * .05f, reach, .25f, With(new Color(.08f, .02f, .14f), .6f * fade), 32);
        var random = new System.Random(seed);
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f + (float)random.NextDouble() * .4f;
            Vector3 foot = at + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * reach * .8f;
            float height = (1.2f + (float)random.NextDouble() * 1f) * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.4f));
            Blade(glowVertices, glowColors, glowTriangles, foot, foot + Vector3.up * height, right * .15f * Mathf.Sin(angle * 3f + age * 9f), .12f,
                With(Violet, .85f * fade), 6);
        }
    }

    // ---- The phantom cavalry ----

    // Five horsemen in a wedge, galloping along the line; each drawn side on to the eye, its head the way it rides on
    // screen, with a white lance and a pink pennant; two lines of light left on the ground behind them.
    private void Cavalry(Camera view)
    {
        Vector3 travel = Vector3.ProjectOnPlane(direction, Vector3.up);
        travel = travel.sqrMagnitude > 1e-4f ? travel.normalized : Vector3.forward;
        Vector3 aside = Vector3.Cross(Vector3.up, travel).normalized;
        float reach = Mathf.Max(1f, value), ride = Mathf.Max(0f, age - CavalryIn) * CavalrySpeed;
        float shown = Mathf.Clamp01(age / CavalryIn) * (1f - Mathf.Clamp01((age - (life - CavalryOut)) / CavalryOut));
        // Side on to the eye, the head toward where they go on screen.
        float facing = Vector3.Dot(travel, right) >= 0f ? 1f : -1f;
        Vector3 ahead = right * facing, high = Vector3.ProjectOnPlane(Vector3.up, forward).normalized;
        if (high.sqrMagnitude < .5f) high = up;
        for (int i = 0; i < Riders; i++)
        {
            int rank = (i + 1) / 2;
            float lateral = rank * 1.25f * (i % 2 == 0 ? 1f : -1f), back = rank * 1.3f;
            float along = Mathf.Min(reach, ride - back);
            if (along < -2f) continue;
            Vector3 ground = at + travel * along + aside * lateral;
            float gait = age * 11f + i * 1.7f;
            Horseman(ground, ahead, high, gait, shown);
            // The light left on the ground, behind this rider, fading toward the start.
            Vector3 start = at + aside * lateral + Vector3.up * .04f, now = ground + Vector3.up * .04f;
            if (along > 0f) Track(start, now, aside, With(KitStyle.Accent, .55f * shown));
        }
    }

    // One horseman, side on: body, neck and head, four legs galloping, tail, the rider leaning into the lance.
    private void Horseman(Vector3 feet, Vector3 ahead, Vector3 high, float gait, float shown)
    {
        Color body = With(KitStyle.Accent, .5f * shown), edge = With(Color.white, .9f * shown);
        Vector3 P(float x, float y) => feet + ahead * x + high * y;
        Ellipse(glowVertices, glowColors, glowTriangles, P(0f, 1.3f), ahead * .95f, high * .38f, 18, body, With(KitStyle.Accent, .2f * shown));
        Blade(glowVertices, glowColors, glowTriangles, P(.7f, 1.45f), P(1.2f, 2.08f), Vector3.zero, .17f, body, 3);
        Blade(glowVertices, glowColors, glowTriangles, P(1.12f, 2.1f), P(1.6f, 1.78f), Vector3.zero, .12f, body, 3);
        Blade(glowVertices, glowColors, glowTriangles, P(1.1f, 2.12f), P(1.05f, 2.35f), Vector3.zero, .05f, body, 2);
        Blade(glowVertices, glowColors, glowTriangles, P(-.9f, 1.45f), P(-1.3f, 1.05f + .08f * Mathf.Sin(gait)), Vector3.zero, .08f, body, 3);
        // Legs: shoulders and hips, a thigh and a shin each, swinging in a gallop's order.
        float[] hipX = { .62f, .5f, -.6f, -.72f }, phase = { 0f, .6f, 2.4f, 3f };
        for (int leg = 0; leg < 4; leg++)
        {
            float swing = Mathf.Sin(gait + phase[leg]);
            Vector3 hip = P(hipX[leg], 1.05f);
            Vector3 knee = hip + (-high * Mathf.Cos(swing * .6f) + ahead * Mathf.Sin(swing * .6f)) * .48f;
            Vector3 hoof = knee + (-high * Mathf.Cos(swing * .9f - .3f) + ahead * Mathf.Sin(swing * .9f - .3f)) * .5f;
            Blade(glowVertices, glowColors, glowTriangles, hip, knee, Vector3.zero, .07f, body, 2);
            Blade(glowVertices, glowColors, glowTriangles, knee, hoof, Vector3.zero, .05f, body, 2);
        }
        // The rider, leaning in, and the lance couched under the arm, its pennant flying.
        Blade(glowVertices, glowColors, glowTriangles, P(.02f, 1.62f), P(.24f, 2.32f), Vector3.zero, .15f, body, 3);
        Fan(glowVertices, glowColors, glowTriangles, P(.3f, 2.5f), .14f, ahead, high, 10, body, With(KitStyle.Accent, .2f * shown));
        Blade(glowVertices, glowColors, glowTriangles, P(-.4f, 2f), P(2.5f, 1.86f), Vector3.zero, .035f, edge, 2);
        float flutter = .06f * Mathf.Sin(gait * 1.6f);
        Triangle(glowVertices, glowColors, glowTriangles, P(1.95f, 1.9f), P(1.55f, 2.02f + flutter), P(1.6f, 1.74f - flutter),
            With(KitStyle.Accent, .9f * shown));
    }

    // Two thin lines of light along the ground from where they set off to where a rider is.
    private void Track(Vector3 from, Vector3 to, Vector3 aside, Color color)
    {
        for (int line = -1; line <= 1; line += 2)
        {
            Vector3 offset = aside * .32f * line;
            Flat(glowVertices, glowColors, glowTriangles, from + offset, to + offset, aside * .04f, With(color, 0f), color);
        }
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
        => Ellipse(vertices, colors, triangles, middle, right * radius, up * radius, segments, inner, rim);

    private static void Ellipse(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 middle, Vector3 across,
        Vector3 tall, int segments, Color inner, Color rim)
    {
        if (inner.a <= .004f && rim.a <= .004f) return;
        int first = vertices.Count;
        vertices.Add(middle);
        colors.Add(inner);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices.Add(middle + across * Mathf.Cos(angle) + tall * Mathf.Sin(angle));
            colors.Add(rim);
        }
        for (int i = 0; i < segments; i++)
        {
            triangles.Add(first);
            triangles.Add(first + 1 + (i + 1) % segments);
            triangles.Add(first + 1 + i);
        }
    }

    // A flat ring lying on the ground.
    private static void Ring(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 middle, float radius, float width,
        Color color, int segments)
    {
        int first = vertices.Count;
        Color32 c = color;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 way = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(middle + way * (radius + width * .5f));
            vertices.Add(middle + way * Mathf.Max(0f, radius - width * .5f));
            colors.Add(c); colors.Add(c);
        }
        for (int i = 0; i < segments; i++)
        {
            int v = first + i * 2;
            triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 1);
            triangles.Add(v + 1); triangles.Add(v + 2); triangles.Add(v + 3);
            triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
            triangles.Add(v + 1); triangles.Add(v + 3); triangles.Add(v + 2);
        }
    }

    // A strip lying along the ground from a to b, `half` to each side, its colour running from one end to the other.
    private static void Flat(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 a, Vector3 b, Vector3 half,
        Color atA, Color atB)
    {
        int first = vertices.Count;
        vertices.Add(a + half); vertices.Add(a - half); vertices.Add(b + half); vertices.Add(b - half);
        colors.Add(atA); colors.Add(atA); colors.Add(atB); colors.Add(atB);
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
        triangles.Add(first + 1); triangles.Add(first + 2); triangles.Add(first + 3);
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        triangles.Add(first + 1); triangles.Add(first + 3); triangles.Add(first + 2);
    }

    private static void Triangle(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        int first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        colors.Add(color); colors.Add(color); colors.Add(color);
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
    }

    private static Color With(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }

    // ---- The screen and the sound ----

    // The flash charge, near this PC's own eyes: the screen blanches a moment (not blinding: it is their own flash).
    private void Blanch()
    {
        blanch = new GameObject("Card Flash", typeof(RectTransform), typeof(Canvas));
        var canvas = blanch.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 81;
        var lid = FeelUI.Rect("White", blanch.transform);
        lid.anchorMin = Vector2.zero;
        lid.anchorMax = Vector2.one;
        lid.offsetMin = lid.offsetMax = Vector2.zero;
        blanchLid = lid.gameObject.AddComponent<UnityEngine.UI.Image>();
        blanchLid.raycastTarget = false;
        blanchLid.color = new Color(1f, 1f, 1f, .45f);
    }

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
