using UnityEngine;

/// <summary>
/// How one field pickup looks (FieldPickups), from the user's picks of three mockups (2026-11-09: draft B's light beam
/// and its take effect, draft C's outline): the item's own model bobbing gently and turning, outlined in its kind's
/// colour by a soft rim that breathes (pink while it is the one [F] would take), a beam of that colour standing up out of
/// it so it shows from afar, and a soft pool of the colour on the floor. Taken, the beam drops into a ring that spreads
/// over the floor and the item shrinks away, in a moment. The beam fades out far away; walls hide all of it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PickupView : MonoBehaviour
{
    // The beam (m), the pool (m), the outline (m), the bob (m), the turn (°/s) and the take (s).
    private const float BeamHeight = 2.6f, BeamWidth = .16f, CoreWidth = .05f, PoolSize = .95f, Outline = .012f;
    private const float Bob = .035f, Lift = .04f, Turn = 25f, TakeSeconds = .32f, RingFrom = .3f, RingTo = 1.3f;
    private const float FadeFrom = 32f, FadeTo = 48f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static Material outlineMaterial, beamMaterial, poolMaterial, ringMaterial;
    private static Mesh standing, lying;
    private static Texture2D beamTexture;

    private Transform model, beam, core, pool, ring;
    private Renderer[] outlines;
    private Renderer beamRenderer, coreRenderer, poolRenderer, ringRenderer;
    private MaterialPropertyBlock block;
    private Color tint;
    private float phase, taken = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        outlineMaterial = beamMaterial = poolMaterial = ringMaterial = null;
        standing = lying = null;
        beamTexture = null;
    }

    public static PickupView Make(Transform parent, string name, FieldPickups.Kind kind, WeaponData weapon, Color tint)
    {
        var host = new GameObject("Pickup " + name);
        host.transform.SetParent(parent, false);
        var view = host.AddComponent<PickupView>();
        view.tint = tint;
        view.phase = Random.value * 6.3f;
        view.Build(kind, weapon);
        return view;
    }

    /// <summary>It was taken (or is gone): the take plays out and the view removes itself.</summary>
    public void Take()
    {
        if (taken >= 0f) return;
        taken = 0f;
        ring.gameObject.SetActive(true);
    }

    private void Build(FieldPickups.Kind kind, WeaponData weapon)
    {
        block = new MaterialPropertyBlock();
        var spin = new GameObject("Turn").transform;
        spin.SetParent(transform, false);
        model = spin;
        PickupProps.Make(kind, weapon, spin);
        // The outline: every mesh again, pushed out and drawn from behind as light.
        var meshes = spin.GetComponentsInChildren<MeshFilter>();
        outlines = new Renderer[meshes.Length];
        for (int i = 0; i < meshes.Length; i++)
        {
            var rim = new GameObject("Outline");
            rim.transform.SetParent(meshes[i].transform, false);
            rim.AddComponent<MeshFilter>().sharedMesh = meshes[i].sharedMesh;
            var renderer = rim.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = OutlineMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            outlines[i] = renderer;
        }
        beam = Quad("Beam", Standing, BeamMaterial, out beamRenderer);
        core = Quad("Beam Core", Standing, BeamMaterial, out coreRenderer);
        pool = Quad("Pool", Lying, PoolMaterial, out poolRenderer);
        pool.localPosition = Vector3.up * .015f;
        pool.localScale = new Vector3(PoolSize, 1f, PoolSize);
        ring = Quad("Ring", Lying, RingMaterial, out ringRenderer);
        ring.localPosition = Vector3.up * .02f;
        ring.gameObject.SetActive(false);
    }

    private Transform Quad(string name, Mesh mesh, Material material, out Renderer renderer)
    {
        var part = new GameObject(name);
        part.transform.SetParent(transform, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var made = part.AddComponent<MeshRenderer>();
        made.sharedMaterial = material;
        made.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        made.receiveShadows = false;
        renderer = made;
        return part.transform;
    }

    /// <summary>Each frame: its beam turned to <paramref name="eye"/>, bobbing and turning, pink while <paramref name="targeted"/>.</summary>
    public void Show(bool targeted, Transform eye, float time)
    {
        if (taken >= 0f) return;
        float far = 0f;
        if (eye != null)
        {
            Vector3 away = transform.position - eye.position;
            far = Mathf.InverseLerp(FadeFrom, FadeTo, away.magnitude);
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) beam.rotation = core.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
        model.localPosition = Vector3.up * (Lift + Mathf.Sin(time * 2f + phase) * Bob);
        model.localRotation = Quaternion.Euler(0f, time * Turn + phase * 40f, 0f);
        beam.localScale = new Vector3(BeamWidth, BeamHeight, 1f);
        core.localScale = new Vector3(CoreWidth, BeamHeight * .8f, 1f);

        float breathe = .7f + .3f * Mathf.Sin(time * 4.5f + phase);
        Color rim = targeted ? KitStyle.Accent : tint;
        Paint(outlines, KitStyle.With(rim, (targeted ? 1f : .75f * breathe)));
        Paint(beamRenderer, KitStyle.With(tint, .7f * (1f - far)));
        Paint(coreRenderer, KitStyle.With(Color.Lerp(tint, Color.white, .7f), .5f * (1f - far)));
        Paint(poolRenderer, KitStyle.With(tint, .32f));
    }

    private void Update()
    {
        if (taken < 0f) return;
        taken += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(taken / TakeSeconds), ease = 1f - (1f - k) * (1f - k);
        // The beam drops into the floor, the item shrinks away, a ring spreads and fades.
        beam.localScale = new Vector3(BeamWidth * (1f + k), BeamHeight * (1f - ease), 1f);
        core.localScale = new Vector3(CoreWidth, BeamHeight * .8f * (1f - ease), 1f);
        model.localScale = Vector3.one * (1f - ease);
        model.localPosition = Vector3.up * (Lift + .25f * ease);
        float size = Mathf.Lerp(RingFrom, RingTo, ease);
        ring.localScale = new Vector3(size, 1f, size);
        Paint(ringRenderer, KitStyle.With(tint, .9f * (1f - k)));
        Paint(beamRenderer, KitStyle.With(tint, .8f * (1f - k)));
        Paint(coreRenderer, KitStyle.With(Color.white, .6f * (1f - k)));
        Paint(poolRenderer, KitStyle.With(tint, .32f * (1f - k)));
        Paint(outlines, KitStyle.With(tint, 1f - k));
        if (k >= 1f) Destroy(gameObject);
    }

    private void Paint(Renderer[] renderers, Color color)
    {
        foreach (var renderer in renderers) Paint(renderer, color);
    }

    private void Paint(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(block);
    }

    // ---- Shared art ----

    private static Material OutlineMaterial
    {
        get
        {
            if (outlineMaterial != null) return outlineMaterial;
            var shader = Resources.Load<Shader>("Combat/Shaders/PickupOutline");
            outlineMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Pickup Outline", hideFlags = HideFlags.HideAndDontSave };
            if (outlineMaterial.HasProperty("_Width")) outlineMaterial.SetFloat("_Width", Outline);
            outlineMaterial.renderQueue = 3001;
            return outlineMaterial;
        }
    }

    private static Material BeamMaterial => beamMaterial != null ? beamMaterial : beamMaterial = Copy(FeelAssets.Particle(BeamTexture, true), "Pickup Beam");
    private static Material PoolMaterial => poolMaterial != null ? poolMaterial : poolMaterial = Copy(FeelAssets.Particle(FeelAssets.SoftDot, true), "Pickup Pool");
    private static Material RingMaterial => ringMaterial != null ? ringMaterial : ringMaterial = Copy(FeelAssets.Particle(FeelAssets.Ring, true), "Pickup Ring");

    // Its own copy, so the colour this view sets through its property block stays apart from the shared effect material.
    private static Material Copy(Material from, string name) => new Material(from) { name = name, hideFlags = HideFlags.HideAndDontSave };

    // A shaft of light, brightest at its foot and fading upward, soft across.
    private static Texture2D BeamTexture
    {
        get
        {
            if (beamTexture != null) return beamTexture;
            const int width = 32, height = 128;
            beamTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Pickup Beam", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float across = (x + .5f) / width * 2f - 1f, up = (y + .5f) / height;
                float alpha = Mathf.Exp(-across * across * 7f) * Mathf.Pow(1f - up, 1.4f) * Mathf.Clamp01(up / .03f);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
            }
            beamTexture.SetPixels32(pixels);
            beamTexture.Apply(false, true);
            return beamTexture;
        }
    }

    // A unit quad standing up from its foot (x across, y up), and one lying flat on the floor, centred.
    private static Mesh Standing => standing != null ? standing : standing = MakeQuad(new[]
    {
        new Vector3(-.5f, 0f, 0f), new Vector3(.5f, 0f, 0f), new Vector3(.5f, 1f, 0f), new Vector3(-.5f, 1f, 0f),
    }, "Pickup Standing Quad");

    private static Mesh Lying => lying != null ? lying : lying = MakeQuad(new[]
    {
        new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f), new Vector3(.5f, 0f, .5f), new Vector3(-.5f, 0f, .5f),
    }, "Pickup Lying Quad");

    private static Mesh MakeQuad(Vector3[] corners, string name)
    {
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = corners;
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        var white = new Color32(255, 255, 255, 255);
        mesh.colors32 = new[] { white, white, white, white };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
