using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The model a field pickup shows (PickupView), resting on its own floor: a gun wears its own KINEMATION model and an ammo
/// pile the Low Poly Weapon Bundle's ammo case and money the Heist pack's stacked notes (PickupLibrary), all copied with
/// their scripts, animators, colliders, lights and effects taken off and any skinned parts baked still; the medkit and
/// shield cell are built from boxes and cylinders (and money too when the library has none). Each kind is made once as a hidden template and copied for every pickup, so a map full of
/// them costs no hitch after the first.
/// </summary>
internal static class PickupProps
{
    private static readonly Dictionary<WeaponData, GameObject> guns = new Dictionary<WeaponData, GameObject>();
    private static readonly Dictionary<FieldPickups.Kind, GameObject> supplies = new Dictionary<FieldPickups.Kind, GameObject>();
    private static readonly Dictionary<Color, Material> paints = new Dictionary<Color, Material>();
    private static Transform shelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        guns.Clear();
        supplies.Clear();
        paints.Clear();
        shelf = null;
    }

    /// <summary>A model for this kind under <paramref name="parent"/>, its middle over the parent and its foot on y = 0.</summary>
    public static GameObject Make(FieldPickups.Kind kind, WeaponData weapon, Transform parent)
    {
        var template = kind == FieldPickups.Kind.Gun ? Gun(weapon) : Supply(kind);
        var made = template != null ? Object.Instantiate(template) : Box(new Vector3(.3f, .2f, .3f), Color.grey, false);
        made.name = "Model";
        made.SetActive(true);
        made.transform.SetParent(parent, false);
        if (kind == FieldPickups.Kind.Gun) made.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Rest(made.transform, parent);
        return made;
    }

    // ---- Templates ----

    // Hidden, inactive, kept across maps.
    private static Transform Shelf
    {
        get
        {
            if (shelf != null) return shelf;
            var root = new GameObject("Pickup Models");
            root.SetActive(false);
            root.hideFlags = HideFlags.HideInHierarchy;
            Object.DontDestroyOnLoad(root);
            return shelf = root.transform;
        }
    }

    private static GameObject Gun(WeaponData weapon)
    {
        if (weapon == null) return null;
        if (guns.TryGetValue(weapon, out var known)) return known;
        GameObject prefab = null;
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon == weapon && entry.prefab != null) { prefab = entry.prefab; break; }
        return guns[weapon] = prefab != null ? Strip(Object.Instantiate(prefab, Shelf)) : null;
    }

    private static GameObject Supply(FieldPickups.Kind kind)
    {
        if (supplies.TryGetValue(kind, out var known) && known != null) return known;
        GameObject made;
        switch (kind)
        {
            case FieldPickups.Kind.Ammo:
                var library = PickupLibrary.Instance;
                made = library != null && library.ammo != null ? Strip(Object.Instantiate(library.ammo, Shelf)) : null;
                break;
            case FieldPickups.Kind.Heal: made = Medkit(); break;
            case FieldPickups.Kind.Shield: made = Cell(); break;
            case FieldPickups.Kind.Money:
                var notes = PickupLibrary.Instance;
                made = notes != null && notes.money != null ? Strip(Object.Instantiate(notes.money, Shelf)) : Bundle();
                break;
            default: made = null; break;
        }
        if (made != null) made.transform.SetParent(Shelf, false);
        return supplies[kind] = made;
    }

    // Only what is seen stays: scripts, animators, physics, sound, lights and effects go, skinned parts are baked still.
    private static GameObject Strip(GameObject copy)
    {
        foreach (var system in copy.GetComponentsInChildren<ParticleSystem>(true)) if (system != null) Object.DestroyImmediate(system.gameObject);
        foreach (var trail in copy.GetComponentsInChildren<TrailRenderer>(true)) Object.DestroyImmediate(trail);
        foreach (var line in copy.GetComponentsInChildren<LineRenderer>(true)) Object.DestroyImmediate(line);
        // Twice: a script another one requires goes on the second pass.
        for (int pass = 0; pass < 2; pass++)
            foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null) Object.DestroyImmediate(script);
        // After the scripts: URP's UniversalAdditionalLightData requires its Light, so the light can't go first
        // (the flashlight attachment's lamp, "Can't remove Light", 2026-11-09).
        foreach (var light in copy.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);
        foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
        foreach (var joint in copy.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (var audio in copy.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
        foreach (var skinned in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = new Mesh { name = skinned.name + " (Pickup)" };
            skinned.BakeMesh(mesh);
            var materials = skinned.sharedMaterials;
            var host = skinned.gameObject;
            Object.DestroyImmediate(skinned);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            host.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.gameObject.layer = 0;
        }
        copy.SetActive(true);
        return copy;
    }

    // A white case with a red cross on its lid and front, and a dark handle.
    private static GameObject Medkit()
    {
        var root = new GameObject("Medkit");
        Part(root, Box(new Vector3(.36f, .2f, .24f), new Color(.9f, .89f, .87f), false), new Vector3(0f, .1f, 0f));
        Part(root, Box(new Vector3(.365f, .03f, .245f), new Color(.7f, .69f, .67f), false), new Vector3(0f, .185f, 0f));
        Color red = new Color(.85f, .12f, .1f);
        Part(root, Box(new Vector3(.13f, .006f, .04f), red, false), new Vector3(0f, .203f, 0f));
        Part(root, Box(new Vector3(.04f, .006f, .13f), red, false), new Vector3(0f, .203f, 0f));
        Part(root, Box(new Vector3(.12f, .04f, .006f), red, false), new Vector3(0f, .09f, .121f));
        Part(root, Box(new Vector3(.04f, .12f, .006f), red, false), new Vector3(0f, .09f, .121f));
        Part(root, Box(new Vector3(.14f, .025f, .03f), new Color(.15f, .15f, .16f), false), new Vector3(0f, .218f, 0f));
        return root;
    }

    // A dark canister with a glowing band and cap.
    private static GameObject Cell()
    {
        var root = new GameObject("Shield Cell");
        Color glow = new Color(.45f, .7f, 1f);
        Part(root, Cylinder(.085f, .3f, new Color(.13f, .15f, .17f), false), new Vector3(0f, .15f, 0f));
        Part(root, Cylinder(.09f, .035f, glow, true), new Vector3(0f, .2f, 0f));
        Part(root, Cylinder(.09f, .02f, glow, true), new Vector3(0f, .09f, 0f));
        Part(root, Cylinder(.05f, .03f, new Color(.6f, .62f, .65f), false), new Vector3(0f, .315f, 0f));
        return root;
    }

    // Three bricks of notes in a paper band.
    private static GameObject Bundle()
    {
        var root = new GameObject("Money");
        for (int i = 0; i < 3; i++)
        {
            var brick = Box(new Vector3(.24f, .045f, .12f), i % 2 == 0 ? new Color(.3f, .62f, .38f) : new Color(.26f, .55f, .33f), false);
            Part(root, brick, new Vector3((i - 1) * .015f, .0225f + i * .046f, (i - 1) * -.01f));
            brick.transform.localRotation = Quaternion.Euler(0f, (i - 1) * 9f, 0f);
        }
        Part(root, Box(new Vector3(.05f, .145f, .126f), new Color(.92f, .9f, .82f), false), new Vector3(0f, .0725f, 0f));
        return root;
    }

    private static void Part(GameObject root, GameObject part, Vector3 at)
    {
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = at;
    }

    private static GameObject Box(Vector3 size, Color color, bool glows) => Shape(PrimitiveType.Cube, size, color, glows);

    private static GameObject Cylinder(float radius, float height, Color color, bool glows)
        => Shape(PrimitiveType.Cylinder, new Vector3(radius * 2f, height * .5f, radius * 2f), color, glows);

    private static GameObject Shape(PrimitiveType type, Vector3 scale, Color color, bool glows)
    {
        var shape = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.transform.localScale = scale;
        var renderer = shape.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = Paint(color, glows);
        return shape;
    }

    private static Material Paint(Color color, bool glows)
    {
        var key = glows ? new Color(color.r, color.g, color.b, .5f) : color;
        if (paints.TryGetValue(key, out var known) && known != null) return known;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader != null ? shader : Shader.Find("Standard")) { name = "Pickup Paint", hideFlags = HideFlags.HideAndDontSave };
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .35f);
        if (glows && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.2f);
        }
        return paints[key] = material;
    }

    // Its middle over the parent's origin and its lowest point on the parent's floor (y = 0).
    private static void Rest(Transform model, Transform parent)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        Vector3 low = parent.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
        model.localPosition -= low;
    }
}
