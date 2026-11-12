using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The crouched leg sweep's mark on the floor (ShoveMoves, the user's #33): a white crescent that follows the foot round
/// the front, brightest at the leading edge and thinning behind it, then gone a moment after the foot stops. Short and
/// bright, as hit effects here are. Every PC draws its own copy from the kick's event, so friends see it too. A few
/// meshes are kept and reused; additive, so it lights the floor rather than painting it.
/// </summary>
public sealed class SweepArcVfx : MonoBehaviour
{
    // Segments round the arc, the crescent's inner edge (m from the body's middle), its height off the floor (m), how
    // much of the arc behind the foot still shows (degrees), and the fade once the foot stops (s).
    private const int Segments = 40;
    private const float Inner = .35f, Lift = .04f, Trail = 140f, FadeSeconds = .14f;
    private const float Hot = 2.2f;
    private static SweepArcVfx instance;

    private sealed class Arc
    {
        public GameObject host;
        public Mesh mesh;
        public Vector3 forward;
        public float arc, radius, seconds, age;
        public bool busy;
    }

    private readonly List<Arc> arcs = new List<Arc>(4);
    private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
    private readonly Color[] colors = new Color[(Segments + 1) * 2];
    private readonly int[] triangles = new int[Segments * 6];
    private Material material;

    /// <summary>A sweep began at body, going round arc degrees about forward over seconds, out to radius.</summary>
    public static void Play(Transform body, Vector3 forward, float arc, float radius, float seconds)
    {
        if (!Application.isPlaying || body == null) return;
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Sweep Arc VFX").AddComponent<SweepArcVfx>();
        instance.Begin(body.position + Vector3.up * Lift, forward, arc, radius, seconds);
    }

    private void Awake()
    {
        material = FeelAssets.Particle(Texture2D.whiteTexture, true);
        for (int i = 0; i < Segments; i++)
        {
            int a = i * 2, t = i * 6;
            triangles[t] = a; triangles[t + 1] = a + 2; triangles[t + 2] = a + 1;
            triangles[t + 3] = a + 1; triangles[t + 4] = a + 2; triangles[t + 5] = a + 3;
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var arc in arcs) if (arc.mesh != null) Destroy(arc.mesh);
    }

    private void Begin(Vector3 at, Vector3 forward, float arcDegrees, float radius, float seconds)
    {
        Arc arc = null;
        foreach (var free in arcs) if (!free.busy) { arc = free; break; }
        if (arc == null)
        {
            arc = new Arc { host = new GameObject("Sweep arc"), mesh = new Mesh { name = "Sweep arc" } };
            arc.mesh.MarkDynamic();
            arc.host.transform.SetParent(transform, false);
            arc.host.AddComponent<MeshFilter>().sharedMesh = arc.mesh;
            var renderer = arc.host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            arcs.Add(arc);
        }
        arc.host.transform.SetPositionAndRotation(at, Quaternion.identity);
        arc.host.SetActive(true);
        arc.forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        arc.arc = arcDegrees; arc.radius = Mathf.Max(Inner + .1f, radius); arc.seconds = Mathf.Max(.05f, seconds);
        arc.age = 0f; arc.busy = true;
        Draw(arc);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        float dt = Time.deltaTime;
        foreach (var arc in arcs)
        {
            if (!arc.busy) continue;
            arc.age += dt;
            if (arc.age >= arc.seconds + FadeSeconds) { arc.busy = false; arc.host.SetActive(false); continue; }
            Draw(arc);
        }
    }

    // From the side it started on round to where the foot is now (eased out, as the leg goes), the light falling off
    // behind the foot and the whole fading once it stops.
    private void Draw(Arc arc)
    {
        float t = Mathf.Clamp01(arc.age / arc.seconds), eased = 1f - (1f - t) * (1f - t);
        float start = arc.arc * .5f, foot = Mathf.Lerp(start, -start, eased);
        float fade = arc.age <= arc.seconds ? 1f : 1f - (arc.age - arc.seconds) / FadeSeconds;
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Lerp(start, foot, i / (float)Segments);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * arc.forward;
            // Brightest at the foot, nothing Trail degrees behind it; the outer edge brighter than the inner.
            float behind = Mathf.Abs(angle - foot);
            float glow = Mathf.Clamp01(1f - behind / Trail);
            glow *= glow * fade;
            vertices[i * 2] = direction * Inner;
            vertices[i * 2 + 1] = direction * arc.radius;
            colors[i * 2] = new Color(Hot, Hot, Hot, glow * .25f);
            colors[i * 2 + 1] = new Color(Hot, Hot, Hot, glow);
        }
        arc.mesh.vertices = vertices;
        arc.mesh.colors = colors;
        arc.mesh.triangles = triangles;
        arc.mesh.RecalculateBounds();
    }
}
