using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One flat-shape mesh for a world effect, rebuilt every frame the way CardFx draws: blades, discs, rings, strips and
/// sigils made of plain triangles in vertex colours, facing the eye where they stand up and lying flat where they lie on
/// the ground. An additive one is light; a blended one is matter (a dark plate). Nothing is kept between frames.
/// </summary>
public sealed class FxShapes
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<int> triangles = new List<int>();
    private readonly Mesh mesh;
    private readonly GameObject host;

    /// <summary>The eye's way and its right and up, set by Begin for the shapes that turn to it.</summary>
    public Vector3 Forward { get; private set; } = Vector3.forward;
    public Vector3 Right { get; private set; } = Vector3.right;
    public Vector3 Up { get; private set; } = Vector3.up;

    /// <summary>A new mesh under <paramref name="parent"/>: additive light, or blended matter.</summary>
    public FxShapes(Transform parent, string name, bool additive)
    {
        host = new GameObject(name);
        host.transform.SetParent(parent, false);
        mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, additive);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>Starts a frame's shapes, facing this eye (Camera.main when null).</summary>
    public bool Begin(Camera eye = null)
    {
        vertices.Clear(); colors.Clear(); triangles.Clear();
        if (eye == null) eye = Camera.main;
        if (eye == null) return false;
        Forward = eye.transform.forward;
        Right = eye.transform.right;
        Up = eye.transform.up;
        return true;
    }

    /// <summary>Hands the frame's shapes to the mesh, in its host's space.</summary>
    public void End()
    {
        mesh.Clear();
        if (vertices.Count == 0) return;
        var space = host.transform;
        for (int i = 0; i < vertices.Count; i++) vertices[i] = space.InverseTransformPoint(vertices[i]);
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    public void Destroy()
    {
        if (mesh != null) Object.Destroy(mesh);
        if (host != null) Object.Destroy(host);
    }

    public static Color With(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }

    // ---- Shapes ----

    /// <summary>
    /// A blade from a to b, bowed by `bow` at its middle and `width` across at its widest. It faces the eye.
    /// taper 1 runs it to points at both ends; 0 keeps its width.
    /// </summary>
    public void Blade(Vector3 a, Vector3 b, float width, Color color, Vector3 bow = default, int steps = 1, float taper = 0f)
    {
        Vector3 along = b - a;
        if (along.sqrMagnitude < 1e-8f || width <= 0f || color.a <= .004f) return;
        Vector3 side = Vector3.Cross(Forward, along).normalized;
        if (side.sqrMagnitude < .5f) side = Vector3.Cross(Vector3.up, along).normalized;
        Strip(a, b, side, width, color, bow, steps, taper);
    }

    /// <summary>A strip lying in the plane its `side` gives (a blade not turned to the eye: flat on the ground with side across).</summary>
    public void Strip(Vector3 a, Vector3 b, Vector3 side, float width, Color color, Vector3 bow = default, int steps = 1, float taper = 0f)
    {
        if (width <= 0f || color.a <= .004f) return;
        int first = vertices.Count;
        Color32 c = color;
        steps = Mathf.Max(1, steps);
        for (int i = 0; i <= steps; i++)
        {
            float s = i / (float)steps;
            Vector3 point = Vector3.Lerp(a, b, s) + bow * (4f * s * (1f - s));
            float half = width * .5f * Mathf.Lerp(1f, Mathf.Sin(Mathf.PI * Mathf.Clamp(s, .04f, .96f)), taper);
            vertices.Add(point + side * half);
            vertices.Add(point - side * half);
            colors.Add(c); colors.Add(c);
        }
        for (int i = 0; i < steps; i++)
        {
            int v = first + i * 2;
            Both(v, v + 2, v + 1);
            Both(v + 1, v + 2, v + 3);
        }
    }

    /// <summary>A lit line: a soft wide glow and a near-white core (hairlines, a sigil's strokes).</summary>
    public void Glow(Vector3 a, Vector3 b, Color color, float core, float wide)
    {
        if (color.a <= .004f) return;
        if (wide > core) Blade(a, b, wide, With(color, color.a * .22f));
        Blade(a, b, core * 2.2f, With(color, color.a * .8f));
        Blade(a, b, core, With(Color.Lerp(color, Color.white, .7f), color.a));
    }

    /// <summary>A round shape in the plane of `across` × `tall`, bright in the middle and fading to `rim`.</summary>
    public void Disc(Vector3 middle, Vector3 across, Vector3 tall, Color inner, Color rim, int segments = 20)
    {
        if (inner.a <= .004f && rim.a <= .004f) return;
        int first = vertices.Count;
        vertices.Add(middle); colors.Add(inner);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices.Add(middle + across * Mathf.Cos(angle) + tall * Mathf.Sin(angle));
            colors.Add(rim);
        }
        for (int i = 0; i < segments; i++) Both(first, first + 1 + (i + 1) % segments, first + 1 + i);
    }

    /// <summary>A ring in the plane of `across` × `tall` (flat on the ground: Vector3.right, Vector3.forward).</summary>
    public void Ring(Vector3 middle, Vector3 across, Vector3 tall, float radius, float width, Color color, int segments = 48,
        float fromAngle = 0f, float toAngle = Mathf.PI * 2f)
    {
        if (color.a <= .004f || width <= 0f || radius <= 0f) return;
        int first = vertices.Count;
        Color32 c = color;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(fromAngle, toAngle, i / (float)segments);
            Vector3 way = across * Mathf.Cos(angle) + tall * Mathf.Sin(angle);
            vertices.Add(middle + way * (radius + width * .5f));
            vertices.Add(middle + way * Mathf.Max(0f, radius - width * .5f));
            colors.Add(c); colors.Add(c);
        }
        for (int i = 0; i < segments; i++)
        {
            int v = first + i * 2;
            Both(v, v + 2, v + 1);
            Both(v + 1, v + 2, v + 3);
        }
    }

    /// <summary>Short ticks round a ring in the plane of `across` × `tall`, every `major`th one longer.</summary>
    public void Ticks(Vector3 middle, Vector3 across, Vector3 tall, float radius, int count, float length, float width, Color color,
        float turn = 0f, int major = 4, int skipFrom = int.MaxValue)
    {
        if (color.a <= .004f || count <= 0) return;
        Vector3 normal = Vector3.Cross(across, tall).normalized;
        for (int i = 0; i < count; i++)
        {
            if (i >= skipFrom) continue;
            float angle = turn + i * Mathf.PI * 2f / count;
            Vector3 way = across * Mathf.Cos(angle) + tall * Mathf.Sin(angle);
            float reach = major > 0 && i % major == 0 ? length * 1.8f : length;
            Vector3 side = Vector3.Cross(normal, way).normalized;
            Strip(middle + way * radius, middle + way * (radius + reach), side, width, color);
        }
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        if (color.a <= .004f) return;
        int first = vertices.Count;
        Color32 shade = color;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        colors.Add(shade); colors.Add(shade); colors.Add(shade);
        Both(first, first + 1, first + 2);
    }

    /// <summary>A convex shape's fill (a fan from its first point).</summary>
    public void Fill(IList<Vector3> points, Color color)
    {
        if (color.a <= .004f || points.Count < 3) return;
        int first = vertices.Count;
        Color32 shade = color;
        foreach (var point in points) { vertices.Add(point); colors.Add(shade); }
        for (int i = 1; i < points.Count - 1; i++) Both(first, first + i, first + i + 1);
    }

    private readonly List<Vector3> scratch = new List<Vector3>();

    /// <summary>
    /// A class sigil drawn up to <paramref name="progress"/> (0 to 1) as if by one pen, with its middle at `middle`, its
    /// right and up given (a world size of `size` metres across its 100-unit box) and turned by `turn` radians. Traced
    /// strokes are lit lines (core and glow width in metres); filled ones are filled.
    /// </summary>
    public void Sigil(MomentLook.Sigil sigil, Vector3 middle, Vector3 right, Vector3 up, float size, float progress, Color color,
        float core, float wide, float turn = 0f)
    {
        if (color.a <= .004f || progress <= 0f) return;
        float k = size / 100f, cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
        Vector3 Place(Vector2 p) => middle + right * ((p.x * cos - p.y * sin) * k) + up * ((p.x * sin + p.y * cos) * k);
        var white = With(Color.white, color.a);
        // Turned to the eye, its strokes face the eye; lying flat (on the ground), they lie in its plane.
        Vector3 normal = Vector3.Cross(right, up).normalized;
        bool facing = Mathf.Abs(Vector3.Dot(normal, Forward)) > .2f;
        MomentLook.Walk(sigil, progress,
            (a, b, isWhite) =>
            {
                Vector3 from = Place(a), to = Place(b);
                if (facing) Glow(from, to, isWhite ? white : color, core, wide);
                else FlatGlow(from, to, Vector3.Cross(normal, (to - from).normalized), isWhite ? white : color, core, wide);
            },
            (points, isWhite) =>
            {
                scratch.Clear();
                foreach (var p in points) scratch.Add(Place(p));
                Fill(scratch, isWhite ? white : color);
            });
    }

    // A lit line lying in a plane (a sigil on the ground seen edge on would vanish if it turned to the eye).
    private void FlatGlow(Vector3 a, Vector3 b, Vector3 side, Color color, float core, float wide)
    {
        if (side.sqrMagnitude < 1e-6f) { Glow(a, b, color, core, wide); return; }
        side.Normalize();
        if (wide > core) Strip(a, b, side, wide, With(color, color.a * .22f));
        Strip(a, b, side, core * 2.2f, With(color, color.a * .8f));
        Strip(a, b, side, core, With(Color.Lerp(color, Color.white, .7f), color.a));
    }

    // Both faces, so a flat shape shows from either side.
    private void Both(int a, int b, int c)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(b);
    }
}
