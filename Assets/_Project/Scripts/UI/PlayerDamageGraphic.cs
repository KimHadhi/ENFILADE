using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One full-screen UI mesh drawn with the procedural blood vignette shader. The grid's vertex alpha is the blood amount
/// per screen direction (first 49x29 vertices); the shader turns it into an organic liquid front with veins and gloss.
/// Drips and world-bearing arcs follow with uv.x = -1 so the shader passes their vertex colour through unchanged.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PlayerDamageGraphic : MaskableGraphic
{
    private const string ShaderPath = "Combat/Shaders/PlayerDamageBlood";
    private const int Columns = 48, Rows = 28;
    private static readonly int BloodColorId = Shader.PropertyToID("_BloodColor"), FreshColorId = Shader.PropertyToID("_FreshColor"),
        SpeckDensityId = Shader.PropertyToID("_SpeckDensity"), SeedId = Shader.PropertyToID("_Seed"),
        GradientOpacityId = Shader.PropertyToID("_GradientOpacity"), GradientReachId = Shader.PropertyToID("_GradientReach"),
        NoiseScaleId = Shader.PropertyToID("_NoiseScale"), FreshId = Shader.PropertyToID("_Fresh"), WashId = Shader.PropertyToID("_Wash"),
        PulseId = Shader.PropertyToID("_Pulse"), VignetteId = Shader.PropertyToID("_Vignette"),
        ClockId = Shader.PropertyToID("_Clock"), AspectId = Shader.PropertyToID("_Aspect");
    private static readonly Vector2 Passthrough = new Vector2(-1f, -1f);
    private Material blood;
    internal PlayerDamageFeedback Owner;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (blood != null) return;
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null) { Debug.LogWarning($"Player damage blood shader missing at Resources/{ShaderPath}."); return; }
        blood = new Material(shader) { name = "Player Damage Blood", hideFlags = HideFlags.HideAndDontSave };
        material = blood;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (blood != null) Destroy(blood);
    }

    internal void ApplyShaderState()
    {
        if (blood == null || Owner == null) return;
        var settings = PlayerDamageFeedbackSettings.Instance;
        Rect rect = rectTransform.rect;
        blood.SetColor(BloodColorId, settings.bloodColor);
        blood.SetColor(FreshColorId, settings.freshColor);
        blood.SetFloat(GradientOpacityId, settings.gradientOpacity);
        blood.SetFloat(GradientReachId, Mathf.Max(.45f, settings.gradientReach));
        blood.SetFloat(SpeckDensityId, settings.speckDensity);
        blood.SetVector(SeedId, Owner.PatternSeed);
        blood.SetFloat(NoiseScaleId, settings.noiseScale);
        blood.SetFloat(FreshId, Owner.Freshness);
        blood.SetFloat(WashId, Owner.RearWash);
        blood.SetFloat(PulseId, Owner.PulseWeight);
        blood.SetFloat(VignetteId, Owner.Vignette);
        blood.SetFloat(ClockId, Owner.Clock);
        blood.SetFloat(AspectId, rect.height > 1f ? rect.width / rect.height : 16f / 9f);
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (Owner == null || !Owner.isActiveAndEnabled) return;
        var settings = PlayerDamageFeedbackSettings.Instance;
        Rect rect = GetPixelAdjustedRect();
        AddBloodField(mesh, rect);
        for (int i = 0; i < Owner.ActiveDripCount; i++) AddDrip(mesh, rect, Owner.Drips[i], settings);
        if (settings.directionArcs)
            for (int i = 0; i < Owner.ActiveHitCount; i++)
            {
                var hit = Owner.Hits[i];
                if (!hit.Directional) continue;
                float radius = Mathf.Max(30f, settings.arcRadius) + i % 3 * (settings.arcThickness + 3f)
                    + 9f * Mathf.Exp(-hit.Age * 18f);
                float angle = Mathf.Atan2(hit.Direction.y, hit.Direction.x);
                Color arc = Color.Lerp(Color.white, settings.arcColor, Mathf.Clamp01(hit.Age / .075f));
                arc.a = settings.arcColor.a * hit.Alpha;
                AddArc(mesh, rect.center, angle, radius, settings.arcThickness + 3f, settings.arcDegrees + 1.5f,
                    new Color(.05f, 0f, 0f, arc.a * .7f));
                AddArc(mesh, rect.center, angle, radius, settings.arcThickness, settings.arcDegrees, arc);
            }
    }

    private void AddBloodField(VertexHelper mesh, Rect rect)
    {
        for (int y = 0; y <= Rows; y++)
        for (int x = 0; x <= Columns; x++)
        {
            float u = (float)x / Columns, v = (float)y / Rows;
            float amount = Owner.BloodAt(new Vector2(u * 2f - 1f, v * 2f - 1f));
            mesh.AddVert(new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v), new Color(1f, 1f, 1f, amount),
                new Vector2(u, v));
            if (x == Columns || y == Rows) continue;
            int a = y * (Columns + 1) + x;
            mesh.AddTriangle(a, a + Columns + 1, a + 1);
            mesh.AddTriangle(a + 1, a + Columns + 1, a + Columns + 2);
        }
    }

    private static void AddDrip(VertexHelper mesh, Rect rect, in PlayerDamageFeedback.Drip drip, PlayerDamageFeedbackSettings settings)
    {
        float run = Mathf.InverseLerp(drip.Delay, drip.Delay + Mathf.Max(.1f, settings.dripSeconds), drip.Age);
        if (run <= 0f) return;
        // Runs fast while the pool feeds it, then slows to a crawl as the trail thins.
        float length = drip.Length * (1f - (1f - run) * (1f - run) * (1f - run));
        float hold = settings.bloodHoldSeconds / Mathf.Max(.01f, drip.Lifetime);
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hold, 1f, drip.Age / drip.Lifetime));
        Color body = settings.bloodColor;
        // Five columns across: clear, deep edge, slightly fuller core, deep edge, clear. Translucent and dark, never glossy.
        Color edge = new Color(body.r * .8f, body.g, body.b, alpha * .7f), core = new Color(body.r * 1.1f, body.g, body.b, alpha * .85f);
        Color clear = edge; clear.a = 0f;
        Vector2 top = rect.center + Vector2.Scale(drip.Start, rect.size * .5f);
        const int rows = 14;
        const float feather = 1.2f;
        int first = mesh.currentVertCount;
        Vector2 point = top;
        for (int j = 0; j <= rows; j++)
        {
            float s = (float)j / rows;
            float half = drip.Width * .5f * Mathf.Lerp(.85f, .55f, s);
            point = top + new Vector2(Mathf.Sin(s * 3.3f + drip.Phase) * drip.Width * .35f * s, -length * s);
            // The first stretch fades in so the rivulet emerges from under the blood instead of starting with a cap.
            float emerge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s * length / Mathf.Max(1f, drip.Width * 2.5f)));
            Color e = edge, c = core, o = clear;
            e.a *= emerge; c.a *= emerge;
            mesh.AddVert(point + Vector2.left * (half + feather), o, Passthrough);
            mesh.AddVert(point + Vector2.left * half, e, Passthrough);
            mesh.AddVert(point + Vector2.left * half * .2f, c, Passthrough);
            mesh.AddVert(point + Vector2.right * half, e, Passthrough);
            mesh.AddVert(point + Vector2.right * (half + feather), o, Passthrough);
            if (j == rows) continue;
            int v = first + j * 5;
            for (int k = 0; k < 4; k++) { mesh.AddTriangle(v + k, v + k + 1, v + k + 6); mesh.AddTriangle(v + k, v + k + 6, v + k + 5); }
        }
        // The heavier bead gathering at the leading tip.
        AddBead(mesh, point + Vector2.down * drip.Width * .3f, drip.Width * .55f, drip.Width * .7f, edge, core);
    }

    private static void AddBead(VertexHelper mesh, Vector2 center, float halfWidth, float halfHeight, Color edge, Color core)
    {
        const int sides = 16;
        int first = mesh.currentVertCount;
        Color clear = edge; clear.a = 0f;
        mesh.AddVert(center, core, Passthrough);
        for (int i = 0; i <= sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            // Teardrop: the lower half bulges, as gravity pulls the bead down.
            Vector2 offset = new Vector2(Mathf.Cos(angle) * halfWidth, Mathf.Sin(angle) * halfHeight * (Mathf.Sin(angle) < 0f ? 1.15f : .85f));
            mesh.AddVert(center + offset, edge, Passthrough);
            mesh.AddVert(center + offset * (1f + 1.2f / Mathf.Max(1f, halfWidth)), clear, Passthrough);
            if (i == sides) continue;
            int v = first + 1 + i * 2;
            mesh.AddTriangle(first, v, v + 2);
            mesh.AddTriangle(v, v + 1, v + 3); mesh.AddTriangle(v, v + 3, v + 2);
        }
    }

    private static void AddArc(VertexHelper mesh, Vector2 center, float angle, float radius, float thickness, float degrees, Color color)
    {
        const int segments = 24;
        int start = mesh.currentVertCount;
        float half = Mathf.Clamp(degrees, 1f, 150f) * Mathf.Deg2Rad * .5f;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float a = angle + Mathf.Lerp(-half, half, t);
            Vector2 normal = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Color tint = color;
            tint.a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(t, 1f - t) * 8f));
            mesh.AddVert(center + normal * (radius - thickness * .5f), tint, Passthrough);
            mesh.AddVert(center + normal * (radius + thickness * .5f), tint, Passthrough);
            if (i == segments) continue;
            int v = start + i * 2;
            mesh.AddTriangle(v, v + 2, v + 1); mesh.AddTriangle(v + 1, v + 2, v + 3);
        }
    }
}
