using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Procedural textures, meshes and materials for shot and hit effects, so no imported art is required.
/// Shaders live in Resources/Combat/Shaders and are therefore included in builds.
/// </summary>
public static class FeelAssets
{
    private static Texture2D softDot, flashStar, flashSpike, bloodSplat, ring;
    private static Mesh muzzleMesh, casingMesh, quad;
    private static Shader particleShader, overlayShader;
    private static readonly Color32 White = new Color32(255, 255, 255, 255);
    private static readonly Dictionary<(Texture, bool), Material> particleMaterials = new Dictionary<(Texture, bool), Material>();

    public static Shader OverlayShader => overlayShader != null ? overlayShader
        : overlayShader = Resources.Load<Shader>("Combat/Shaders/HitFlashOverlay");

    private static Shader ParticleShader
    {
        get
        {
            if (particleShader != null) return particleShader;
            particleShader = Resources.Load<Shader>("Combat/Shaders/FeelParticle");
            if (particleShader == null) particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
            return particleShader;
        }
    }

    /// <summary>Shared unlit material. Additive for light (flash, sparks), alpha blended for matter (blood, smoke, brass).</summary>
    public static Material Particle(Texture texture, bool additive)
    {
        var key = (texture, additive);
        if (particleMaterials.TryGetValue(key, out var material) && material != null) return material;
        material = new Material(ParticleShader) { name = (additive ? "Feel Additive " : "Feel Alpha ") + (texture != null ? texture.name : "White"),
            hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        }
        material.renderQueue = 3000;
        particleMaterials[key] = material;
        return material;
    }

    public static Texture2D SoftDot => softDot != null ? softDot : softDot = Generate("Feel Soft Dot", 64, 64, (u, v) =>
    {
        float r = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
        float a = 1f - r; return a * a * (3f - 2f * a);
    });

    /// <summary>Muzzle flash seen down the barrel: a hot core with uneven radial petals.</summary>
    public static Texture2D FlashStar => flashStar != null ? flashStar : flashStar = Generate("Feel Flash Star", 128, 128, (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v), angle = Mathf.Atan2(v, u);
        float core = Mathf.Exp(-r * r * 22f);
        // Six petals of different lengths; the cosine power keeps them narrow.
        float petals = 0f;
        for (int i = 0; i < 6; i++)
        {
            float centre = i * Mathf.PI / 3f + (i % 2 == 0 ? .12f : -.18f);
            float reach = i % 3 == 0 ? .98f : i % 3 == 1 ? .72f : .55f;
            float along = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(angle - centre)), 18f);
            petals = Mathf.Max(petals, along * Mathf.Clamp01(1f - r / reach));
        }
        return Mathf.Clamp01(core + petals * petals * 1.6f);
    });

    /// <summary>Muzzle flash seen from the side: a flame tongue along +U.</summary>
    public static Texture2D FlashSpike => flashSpike != null ? flashSpike : flashSpike = Generate("Feel Flash Spike", 128, 64, (u, v) =>
    {
        float x = (u + 1f) * .5f; // 0 at the muzzle, 1 at the tip
        float width = .55f * Mathf.Pow(Mathf.Clamp01(1f - x), .7f) * Mathf.Clamp01(x * 8f + .25f);
        if (width <= .001f) return 0f;
        float across = v / width;
        return Mathf.Clamp01(Mathf.Exp(-across * across * 2.2f) * (1f - x * .55f));
    });

    public static Texture2D BloodSplat => bloodSplat != null ? bloodSplat : bloodSplat = Generate("Feel Blood Splat", 128, 128, (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v), angle = Mathf.Atan2(v, u);
        // Lumpy main pool from a few fixed harmonics.
        float edge = .52f + .08f * Mathf.Sin(angle * 3f + 1.3f) + .06f * Mathf.Sin(angle * 7f + .4f) + .04f * Mathf.Sin(angle * 13f + 2.1f);
        float pool = Mathf.Clamp01((edge - r) * 14f);
        float drops = 0f;
        for (int i = 0; i < 9; i++)
        {
            float a = i * 2.39996f; float d = .62f + .27f * Mathf.Repeat(i * .618f, 1f);
            float size = .035f + .03f * Mathf.Repeat(i * .37f, 1f);
            float dx = u - Mathf.Cos(a) * d, dy = v - Mathf.Sin(a) * d;
            drops = Mathf.Max(drops, Mathf.Clamp01((size - Mathf.Sqrt(dx * dx + dy * dy)) * 60f));
        }
        return Mathf.Max(pool, drops);
    });

    public static Texture2D Ring => ring != null ? ring : ring = Generate("Feel Ring", 128, 128, (u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        return Mathf.Clamp01(1f - Mathf.Abs(r - .82f) / .12f);
    });

    /// <summary>Unit quad in the XY plane facing -Z (double-sided material).</summary>
    public static Mesh Quad
    {
        get
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Feel Quad", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.colors32 = new[] { White, White, White, White };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return quad;
        }
    }

    /// <summary>
    /// Two submeshes, +Z is the barrel direction. 0: front flash in the XY plane. 1: two crossed flame planes from z=0 to z=1.
    /// </summary>
    public static Mesh MuzzleFlash
    {
        get
        {
            if (muzzleMesh != null) return muzzleMesh;
            muzzleMesh = new Mesh { name = "Feel Muzzle Flash", hideFlags = HideFlags.HideAndDontSave };
            var vertices = new List<Vector3>
            {
                new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(.5f, .5f, 0f), new Vector3(-.5f, .5f, 0f),
                new Vector3(0f, -.5f, 0f), new Vector3(0f, -.5f, 1f), new Vector3(0f, .5f, 1f), new Vector3(0f, .5f, 0f),
                new Vector3(-.5f, 0f, 0f), new Vector3(-.5f, 0f, 1f), new Vector3(.5f, 0f, 1f), new Vector3(.5f, 0f, 0f),
            };
            var uv = new List<Vector2>
            {
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
            };
            muzzleMesh.SetVertices(vertices); muzzleMesh.SetUVs(0, uv);
            muzzleMesh.SetColors(Enumerable.Repeat(White, vertices.Count).ToList());
            muzzleMesh.subMeshCount = 2;
            muzzleMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            muzzleMesh.SetTriangles(new[] { 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11 }, 1);
            muzzleMesh.bounds = new Bounds(new Vector3(0f, 0f, .5f), Vector3.one * 1.2f);
            return muzzleMesh;
        }
    }

    /// <summary>Rifle-sized brass case along +Z, metres.</summary>
    public static Mesh Casing
    {
        get
        {
            if (casingMesh != null) return casingMesh;
            const int sides = 8; const float radius = .0048f, length = .024f;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var p = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                vertices.Add(p); vertices.Add(p + Vector3.forward * length);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = (i + 1) % sides * 2;
                triangles.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
            }
            int back = vertices.Count; vertices.Add(Vector3.zero);
            int front = vertices.Count; vertices.Add(Vector3.forward * length);
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = (i + 1) % sides * 2;
                triangles.AddRange(new[] { back, b, a, front, a + 1, b + 1 });
            }
            casingMesh = new Mesh { name = "Feel Casing", hideFlags = HideFlags.HideAndDontSave };
            casingMesh.SetVertices(vertices); casingMesh.SetColors(Enumerable.Repeat(White, vertices.Count).ToList());
            casingMesh.SetTriangles(triangles, 0);
            casingMesh.RecalculateNormals(); casingMesh.RecalculateBounds();
            return casingMesh;
        }
    }

    /// <summary>
    /// World-space system driven only by Emit(EmitParams); it keeps playing with emission off so emitted particles simulate.
    /// Callers add size/colour-over-lifetime modules.
    /// </summary>
    public static ParticleSystem CreateEmitter(string name, Transform parent, Material material, int maxParticles, float gravity,
        ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
    {
        var host = new GameObject(name);
        host.transform.SetParent(parent, false);
        var system = host.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = true; main.playOnAwake = false; main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles; main.gravityModifier = gravity;
        main.startSpeed = 0f; main.startLifetime = 1f; main.startSize = .1f;
        var emission = system.emission; emission.enabled = false;
        var shape = system.shape; shape.enabled = false;
        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material; renderer.renderMode = mode;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        system.Play();
        return system;
    }

    public static ParticleSystem.MinMaxGradient Fade(Color from, Color to)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(from.a, 0f), new GradientAlphaKey(to.a, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient);
    }

    private delegate float Coverage(float u, float v);

    /// <summary>White texture whose alpha is the coverage function over [-1, 1] x [-1, 1].</summary>
    private static Texture2D Generate(string name, int width, int height, Coverage coverage)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
        { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = (x + .5f) / width * 2f - 1f, v = (y + .5f) / height * 2f - 1f;
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage(u, v)) * 255f);
            pixels[y * width + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }
}
