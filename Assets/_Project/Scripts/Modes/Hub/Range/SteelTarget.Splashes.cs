using System.Collections.Generic;
using UnityEngine;

/// <summary>A steel target's lead splashes: where a round flattened on the paint, riding the plate and fading.</summary>
public sealed partial class SteelTarget
{
    // A splash where the round flattened on the paint, on the face it hit, riding the plate, a hair off the paint.
    private void Stain(Plate plate, Vector3 point, Vector3 normal)
    {
        if (plate.Face == null || point == Vector3.zero) return;
        Vector3 local = plate.Face.InverseTransformPoint(point);
        bool front = plate.Face.InverseTransformDirection(normal).z >= -.01f;
        local.z = front ? SplashLift : -shape.Thickness - SplashLift;
        plate.Lead ??= new LeadSplashes(plate.Face, shape.SplashMost, meshes);
        plate.Lead.Stamp(local, Random.Range(0f, 360f), shape.SplashSize * Random.Range(.75f, 1.3f));
    }

    private void Fade(float dt)
    {
        foreach (var plate in plates) plate.Lead?.Age(dt, shape.SplashSeconds, shape.SplashFade);
    }

    /// <summary>
    /// A plate's lead splashes: quads of one mesh under its face, reused oldest first, faded by their vertex alpha (the
    /// FeelParticle shader multiplies by it) and drawn in the order they were made, the newest last. As one see-through quad
    /// each they were sorted by their distance from the eye, so where a splash's dark core lay over another's lighter ray,
    /// the one on top swapped as the view or the swinging plate moved: the flicker the user saw on blood (2026-11-14).
    /// Within one mesh the order is the triangles', which nothing but a new splash changes.
    /// </summary>
    private sealed class LeadSplashes
    {
        private static readonly Vector2[] Corners = { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(-.5f, .5f), new Vector2(.5f, .5f) };
        private readonly Mesh mesh;
        private readonly Vector3[] points;
        private readonly Color32[] colours;
        // Seconds since each was made; below 0 when the slot holds none.
        private readonly float[] ages;
        private readonly int[] made;
        private readonly List<int> triangles = new List<int>();
        private int next, count;

        public LeadSplashes(Transform face, int most, List<Mesh> owned)
        {
            points = new Vector3[most * 4];
            colours = new Color32[most * 4];
            ages = new float[most];
            made = new int[most];
            var uvs = new Vector2[most * 4];
            for (int i = 0; i < most; i++)
            {
                ages[i] = -1f;
                for (int c = 0; c < 4; c++) uvs[i * 4 + c] = Corners[c] + new Vector2(.5f, .5f);
            }
            mesh = new Mesh { name = "Lead Splashes" };
            mesh.MarkDynamic();
            mesh.vertices = points;
            mesh.uv = uvs;
            mesh.colors32 = colours;
            owned.Add(mesh);
            var host = new GameObject("납 자국");
            host.transform.SetParent(face, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FeelAssets.Particle(SplashTexture, false);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>A splash `size` across at `centre` (the face's own space, on its plane), turned `spin` degrees.</summary>
        public void Stamp(Vector3 centre, float spin, float size)
        {
            int slot = next;
            next = (next + 1) % ages.Length;
            var turn = Quaternion.Euler(0f, 0f, spin);
            for (int c = 0; c < 4; c++)
            {
                points[slot * 4 + c] = centre + turn * (Vector3)(Corners[c] * size);
                colours[slot * 4 + c] = new Color32(255, 255, 255, 255);
            }
            ages[slot] = 0f;
            made[slot] = ++count;
            mesh.vertices = points;
            mesh.colors32 = colours;
            Draw();
        }

        public void Age(float dt, float seconds, float fade)
        {
            bool tinted = false, gone = false;
            for (int i = 0; i < ages.Length; i++)
            {
                if (ages[i] < 0f) continue;
                ages[i] += dt;
                if (ages[i] <= seconds) continue;
                float left = 1f - (ages[i] - seconds) / Mathf.Max(.05f, fade);
                if (left <= 0f) { ages[i] = -1f; gone = true; continue; }
                byte alpha = (byte)Mathf.RoundToInt(left * 255f);
                for (int c = 0; c < 4; c++) colours[i * 4 + c].a = alpha;
                tinted = true;
            }
            if (tinted) mesh.colors32 = colours;
            if (gone) Draw();
        }

        // The live splashes' triangles, oldest first, so the newest is drawn last and lies on top.
        private void Draw()
        {
            triangles.Clear();
            for (int pass = 0, last = 0; pass < ages.Length; pass++)
            {
                int pick = -1;
                for (int i = 0; i < ages.Length; i++)
                    if (ages[i] >= 0f && made[i] > last && (pick < 0 || made[i] < made[pick])) pick = i;
                if (pick < 0) break;
                last = made[pick];
                int v = pick * 4;
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 1);
                triangles.Add(v + 2); triangles.Add(v + 3); triangles.Add(v + 1);
            }
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }
    }

    // Lead flattened on painted steel: a darker core with a lighter rim and thin rays thrown out along the face, a few
    // droplets past their ends. Grey, as lead is; the paint shows between the rays.
    private static Texture2D SplashTexture
    {
        get
        {
            if (splashTexture != null) return splashTexture;
            const int size = 128, rays = 16;
            var random = new System.Random(4207);
            float[] angle = new float[rays], reach = new float[rays], width = new float[rays];
            for (int i = 0; i < rays; i++)
            {
                angle[i] = (i + (float)random.NextDouble() * .7f) * Mathf.PI * 2f / rays;
                reach[i] = .45f + (float)random.NextDouble() * .5f;
                width[i] = .035f + (float)random.NextDouble() * .04f;
            }
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                float edge = .2f + .03f * Mathf.Sin(a * 5f + 1.1f) + .02f * Mathf.Sin(a * 9f + .3f);
                float core = Mathf.Clamp01((edge - r) * 40f), cover = core, grey = Mathf.Lerp(.6f, .33f, Mathf.Clamp01((edge - r) / edge * 1.6f));
                for (int i = 0; i < rays; i++)
                {
                    float off = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, angle[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                    float along = r / reach[i];
                    if (along < 1f && r > edge * .6f)
                    {
                        float ray = Mathf.Clamp01((width[i] * (1f - along * .85f) - off) * 90f) * (1f - along * along * .5f);
                        if (ray > cover) { cover = ray; grey = .55f; }
                    }
                    float dx = u - Mathf.Cos(angle[i]) * (reach[i] + .06f), dy = v - Mathf.Sin(angle[i]) * (reach[i] + .06f);
                    float drop = Mathf.Clamp01((.03f - Mathf.Sqrt(dx * dx + dy * dy)) * 70f);
                    if (drop > cover) { cover = drop; grey = .5f; }
                }
                byte g = (byte)Mathf.RoundToInt(grey * 255f);
                pixels[y * size + x] = new Color32(g, g, (byte)Mathf.Min(255, g + 6), (byte)Mathf.RoundToInt(cover * 235f));
            }
            splashTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
                { name = "Steel Lead Splash", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            splashTexture.SetPixels32(pixels);
            splashTexture.Apply(true, true);
            return splashTexture;
        }
    }
}
