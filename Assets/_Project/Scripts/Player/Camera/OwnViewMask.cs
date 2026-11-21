using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Marks where this PC's own player covers their own screen (stencil bit 128), so nothing drawn through walls takes the
/// player's own body, gun or scope lens for the wall a foe or friend stands behind. SeeThrough.hlsl holds the rule for
/// every see-through drawing: the enemy outlines (EnemyOutlines: pings, bounties, holders, the Ghost's sight, a wave's last
/// enemies; OutlineLay leaves the marked pixels alone) and MomentGlow (a friend's CHARGE outline).
/// The user, 2026-11-14, aiming a Ghost's magnified scope: "고스트 패시브가, 확대 조준경으로 보이는 확대댄 적과, 그냥 시야로
/// 보이는 적 두 개가 뜨는 버그". The lens writes depth like any solid part, so on the player's screen every foe behind it
/// counted as hidden. Its silhouette was drawn over the lens at the eye's own size, beside the scope's magnified picture,
/// and a foe behind the gun or the arms got one over them the same way.
/// Each of the player's meshes the eye sees as solid (opaque or cut-out materials, which write depth) gets a copy that
/// draws nothing but the mark, after the world and before anything see-through (shader OwnViewMask).
/// - The copies hang under a root of their own, never under the player, so nothing that walks the player's meshes finds
///   them (the CHARGE glow, the scope's own list, a corpse, the shadow double, the hit flash). They are put where their
///   meshes are just before a screen camera draws.
/// - Only cameras that draw to the screen draw them (the player's view, third person, spectating). A scope's camera draws
///   into its texture without the player, so its magnified picture follows the same rule as the eye.
/// - The player's meshes are looked over again every Tables/Hud/SeeThrough rescan seconds, and at once when the gun in
///   hand changes.
/// The shared gap (SeeThrough gap) is pushed to the shaders from here.
/// </summary>
[DisallowMultipleComponent]
public sealed class OwnViewMask : MonoBehaviour
{
    public const string TableName = "SeeThrough", TableRow = "Default";
    private const string ShaderPath = "Combat/Shaders/OwnViewMask", CopyName = "Own View Mask";
    // The last queue a material may have and still write depth: opaque and cut-out (AlphaTest) parts. The ones after it are
    // looked through (a sight's glass), so they are left out and a foe behind them still shows.
    private const int SolidQueue = 2500;
    private static readonly int SkipId = Shader.PropertyToID("_Skip"), GapId = Shader.PropertyToID("_SeeThroughGap");
    private static Material mask, skip;

    private sealed class Copy
    {
        public Renderer Source, Renderer;
        public int SourceId, Shapes;
        public SkinnedMeshRenderer SkinSource, Skin;
        public MeshFilter FilterSource, Filter;
        // The source's materials when the copy was made: a change makes the copy again.
        public readonly List<Material> Looks = new List<Material>();
    }

    private readonly List<Copy> copies = new List<Copy>();
    private readonly HashSet<int> copied = new HashSet<int>();
    private readonly List<Renderer> found = new List<Renderer>();
    private readonly List<Material> looks = new List<Material>();
    private Transform root;
    private FPSBody body;
    private FPSGun held;
    private float nextLook;
    private int syncedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => mask = skip = null;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeRender;
        if (root != null) root.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        if (root != null) root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private void LateUpdate()
    {
        if (body == null) body = GetComponentInChildren<FPSBody>(true);
        var inHand = body != null ? body.Held : null;
        bool changed = inHand != held;
        if (Time.time < nextLook && !changed) return;
        held = inHand;
        // A gun just taken in hand may only switch on later this frame: then it is looked over again on the next one.
        nextLook = changed ? Time.time : Time.time + Mathf.Max(.1f, GameTables.Number(TableName, TableRow, "rescan", .5f));
        Shader.SetGlobalFloat(GapId, Mathf.Max(.0001f, GameTables.Number(TableName, TableRow, "gap", .12f)));
        Look();
    }

    // The player's meshes looked over: a copy for each new one; one whose mesh is gone or changed is let go of and made
    // again.
    private void Look()
    {
        if (root == null)
        {
            // A scene change took the root and the copies with it: start over.
            copies.Clear();
            copied.Clear();
            root = new GameObject(CopyName) { hideFlags = HideFlags.DontSave }.transform;
        }
        for (int i = copies.Count - 1; i >= 0; i--)
            if (copies[i].Source == null || copies[i].Renderer == null || !Same(copies[i])) Drop(i);
        // Only what is switched on: a gun drawn later (a held gun's change is seen at once) or a part put on comes in at
        // the next look, and nothing is copied for every switched-off outfit piece and gun the player carries.
        GetComponentsInChildren(false, found);
        foreach (var source in found)
            if (source != null && !copied.Contains(source.GetInstanceID()) && Wanted(source)) Make(source);
        found.Clear();
    }

    private static bool Wanted(Renderer source)
        => (source is SkinnedMeshRenderer || source is MeshRenderer) && source.GetComponentInParent<Canvas>(true) == null
            && !source.gameObject.name.StartsWith("Glow Shell");

    // Still a true copy: the same mesh (and root bone), and the same materials in the same order.
    private bool Same(Copy copy)
    {
        if (copy.Skin != null && (copy.SkinSource == null || copy.SkinSource.sharedMesh != copy.Skin.sharedMesh
            || copy.SkinSource.rootBone != copy.Skin.rootBone)) return false;
        if (copy.Filter != null && (copy.FilterSource == null || copy.FilterSource.sharedMesh != copy.Filter.sharedMesh)) return false;
        copy.Source.GetSharedMaterials(looks);
        bool same = looks.Count == copy.Looks.Count;
        for (int i = 0; same && i < looks.Count; i++) same = looks[i] == copy.Looks[i];
        looks.Clear();
        return same;
    }

    private void Make(Renderer source)
    {
        if (!Materials()) return;
        var copy = new Copy { Source = source, SourceId = source.GetInstanceID() };
        source.GetSharedMaterials(copy.Looks);
        // A part with no material draws nothing, and a mesh draws only as many parts as it has materials for.
        int parts;
        if (source is SkinnedMeshRenderer skin) parts = skin.sharedMesh != null ? skin.sharedMesh.subMeshCount : 0;
        else
        {
            var filter = source.GetComponent<MeshFilter>();
            parts = filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 0;
            copy.FilterSource = filter;
        }
        parts = Mathf.Min(parts, copy.Looks.Count);
        if (parts <= 0) return;
        var host = new GameObject(CopyName) { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
        host.transform.SetParent(root, false);
        if (source is SkinnedMeshRenderer skinned)
        {
            var made = host.AddComponent<SkinnedMeshRenderer>();
            made.sharedMesh = skinned.sharedMesh;
            made.rootBone = skinned.rootBone;
            made.bones = skinned.bones;
            made.localBounds = skinned.localBounds;
            made.updateWhenOffscreen = skinned.updateWhenOffscreen;
            made.quality = skinned.quality;
            copy.SkinSource = skinned;
            copy.Skin = made;
            copy.Shapes = skinned.sharedMesh.blendShapeCount;
            copy.Renderer = made;
        }
        else
        {
            copy.Filter = host.AddComponent<MeshFilter>();
            copy.Filter.sharedMesh = copy.FilterSource.sharedMesh;
            copy.Renderer = host.AddComponent<MeshRenderer>();
        }
        var slots = new Material[parts];
        for (int i = 0; i < parts; i++)
        {
            var look = copy.Looks[i];
            slots[i] = look != null && look.renderQueue <= SolidQueue ? mask : skip;
        }
        var renderer = copy.Renderer;
        renderer.sharedMaterials = slots;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.enabled = false;
        copies.Add(copy);
        copied.Add(copy.SourceId);
    }

    private void Drop(int index)
    {
        var copy = copies[index];
        copies.RemoveAt(index);
        copied.Remove(copy.SourceId);
        if (copy.Renderer != null) Destroy(copy.Renderer.gameObject);
    }

    private static bool Materials()
    {
        if (mask != null && skip != null) return true;
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null) return false;
        mask = new Material(shader) { name = "Own View Mask (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        skip = new Material(shader) { name = "Own View Mask, looked through (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        skip.SetFloat(SkipId, 1f);
        return true;
    }

    // Just before a camera draws: for one drawing to the screen, the copies where their meshes are this frame (once a frame)
    // and drawn; for any other (a scope's, a mirror's, the editor's scene view), not drawn.
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (root == null || camera == null) return;
        bool screen = camera.cameraType == CameraType.Game && camera.targetTexture == null;
        if (screen && syncedFrame != Time.frameCount)
        {
            syncedFrame = Time.frameCount;
            Sync();
        }
        foreach (var copy in copies)
            if (copy.Renderer != null && copy.Renderer.forceRenderingOff == screen) copy.Renderer.forceRenderingOff = !screen;
    }

    // Each copy on its mesh: shown while the mesh shows to this eye (not switched off or hidden, not a first-person shadow),
    // where it is, its blend shapes as they are, so the mark lies exactly on what was drawn.
    private void Sync()
    {
        for (int i = copies.Count - 1; i >= 0; i--)
        {
            var copy = copies[i];
            var source = copy.Source;
            if (source == null || copy.Renderer == null)
            {
                Drop(i);
                continue;
            }
            bool shows = source.enabled && source.gameObject.activeInHierarchy && !source.forceRenderingOff
                && source.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
            if (copy.Renderer.enabled != shows) copy.Renderer.enabled = shows;
            if (!shows) continue;
            Transform from = source.transform, to = copy.Renderer.transform;
            to.SetPositionAndRotation(from.position, from.rotation);
            Vector3 scale = from.lossyScale;
            if (to.localScale != scale) to.localScale = scale;
            if (to.gameObject.layer != source.gameObject.layer) to.gameObject.layer = source.gameObject.layer;
            if (copy.Skin == null || copy.SkinSource == null) continue;
            for (int shape = 0; shape < copy.Shapes; shape++)
            {
                float weight = copy.SkinSource.GetBlendShapeWeight(shape);
                if (copy.Skin.GetBlendShapeWeight(shape) != weight) copy.Skin.SetBlendShapeWeight(shape, weight);
            }
        }
    }
}
