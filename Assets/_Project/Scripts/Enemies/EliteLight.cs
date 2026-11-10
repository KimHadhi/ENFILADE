using UnityEngine;

/// <summary>
/// The light an elite zombie carries, so it can be told from the pack at a distance as a runner is by its eyes: a soft
/// glow held at one of its bones, turned to the camera and a little toward it so the body doesn't hide it. An exploder
/// has one beating in its chest (ZombieExploder), a charger one burning at its head (ZombieCharger). It is its own
/// object, not a child of the body, since the hit flash is laid over every renderer under a zombie. Every PC shows its own.
/// </summary>
internal sealed class EliteLight
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly Component owner;
    private readonly HumanBodyBones bone, otherwise;
    private Transform at;
    private GameObject glow;
    private Renderer renderer;
    private MaterialPropertyBlock block;
    private float nextLook;

    public EliteLight(Component owner, HumanBodyBones bone, HumanBodyBones otherwise)
    {
        this.owner = owner;
        this.bone = bone;
        this.otherwise = otherwise;
    }

    /// <summary>This frame: lit in this colour (its alpha is its strength), this big (m).</summary>
    public void Show(Color color, float size)
    {
        var view = Camera.main;
        if (view == null || !Find()) { Hide(); return; }
        if (glow == null) Build();
        if (!glow.activeSelf) glow.SetActive(true);
        Vector3 from = at.position, toward = view.transform.position - from;
        glow.transform.position = from + toward.normalized * Mathf.Min(.3f, toward.magnitude * .5f);
        glow.transform.rotation = view.transform.rotation;
        glow.transform.localScale = Vector3.one * size;
        block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(block);
    }

    public void Hide()
    {
        if (glow != null && glow.activeSelf) glow.SetActive(false);
    }

    public void Remove()
    {
        if (glow != null) Object.Destroy(glow);
        glow = null;
    }

    // Its body is put on after it appears, and may be changed: the bone is looked for again now and then until it is there.
    private bool Find()
    {
        if (at != null) return true;
        if (Time.time < nextLook) return false;
        nextLook = Time.time + .4f;
        foreach (var candidate in owner.GetComponentsInChildren<Animator>())
        {
            if (!candidate.isHuman) continue;
            at = candidate.GetBoneTransform(bone) ?? candidate.GetBoneTransform(otherwise);
            break;
        }
        return at != null;
    }

    private void Build()
    {
        glow = new GameObject("Elite Light");
        RuntimeSceneHierarchy.Group(glow, "EFFECTS", "World", owner.gameObject);
        glow.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
        var made = glow.AddComponent<MeshRenderer>();
        made.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
        made.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        made.receiveShadows = false;
        renderer = made;
        block = new MaterialPropertyBlock();
    }
}
