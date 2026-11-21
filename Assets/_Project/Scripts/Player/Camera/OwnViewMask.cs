using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Marks where this PC's own player covers their own screen (stencil bit 128), so nothing drawn through walls takes the
/// player's own body, gun or scope lens for the wall a foe or friend stands behind. SeeThrough.hlsl holds the rule for
/// every see-through drawing: the enemy outlines (EnemyOutlines: pings, bounties, holders, the Ghost's sight, a wave's last
/// enemies; OutlineLay leaves the marked pixels alone) and MomentGlow (a friend's CHARGE outline).
/// The user, 2026-11-14, aiming a Ghost's magnified scope: "고스트 패시브가, 확대 조준경으로 보이는 확대댄 적과, 그냥 시야로
/// 보이는 적 두 개가 뜨는 버그". The lens writes depth like any solid part, so on the player's screen every foe behind it
/// counted as hidden. Its silhouette was drawn over the lens at the eye's own size, beside the scope's magnified picture,
/// and a foe behind the gun or the arms got one over them the same way.
/// Each part of the player the eye sees as solid (a submesh with an opaque or cut-out material, which writes depth) is drawn
/// once more with the mask shader (OwnViewMask), which writes the mark and nothing else, after the opaque world and before
/// anything transparent or see-through.
/// - Drawn straight from the player's own renderers (DrawRenderer), never from copies of them. Up to 0.9.274 each part had a
///   copy SkinnedMeshRenderer under a root of its own; Unity skins every SkinnedMeshRenderer for itself, so the copies
///   skinned the player's body, outfit and gun a second time every frame, and every blend shape weight was copied across
///   each frame to keep them on it. Drawing the renderer itself uses the skin this frame already made. (Read 2026-11-21 in a
///   first-person-body addon's notes: "the player and a hidden helper model were each rebuilt twice per frame".)
/// - A part one looks through (a sight's glass, see-through cloth) is left out by its material's queue, so a foe behind it
///   still shows; it is not drawn at all (the copies drew it with every pixel thrown away).
/// - Only cameras that draw to the screen draw it (the player's view, third person, spectating). A scope's camera draws
///   into its texture without the player, so its magnified picture follows the same rule as the eye.
/// - The player's renderers are looked over again every Tables/Hud/SeeThrough rescan seconds, and at once when the gun in
///   hand changes; which of them shows, and with which materials, is read each frame as it is drawn.
/// The shared gap (SeeThrough gap) is pushed to the shaders from here.
/// </summary>
[DisallowMultipleComponent]
public sealed class OwnViewMask : MonoBehaviour
{
    public const string TableName = "SeeThrough", TableRow = "Default";
    private const string ShaderPath = "Combat/Shaders/OwnViewMask";
    // The last queue a material may have and still write depth: opaque and cut-out (AlphaTest) parts. The ones after it are
    // looked through (a sight's glass), so they are left out and a foe behind them still shows.
    private const int SolidQueue = 2500;
    private static readonly int GapId = Shader.PropertyToID("_SeeThroughGap");
    private static Material mask;

    // A part as found: its renderer and what its submeshes come from (a skin's mesh, or a mesh filter's).
    private struct Part
    {
        public Renderer Renderer;
        public SkinnedMeshRenderer Skin;
        public MeshFilter Filter;
    }

    private readonly List<Part> parts = new List<Part>();
    private readonly List<Renderer> found = new List<Renderer>();
    private readonly List<Material> looks = new List<Material>();
    private MarkPass markPass;
    private FPSBody body;
    private FPSGun held;
    private float nextLook;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => mask = null;

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeRender;

    private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeRender;

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

    // The player's renderers looked over. Only what is switched on: a gun drawn later (a held gun's change is seen at once)
    // or a part put on comes in at the next look, and the list never holds every switched-off outfit piece and gun the
    // player carries.
    private void Look()
    {
        parts.Clear();
        GetComponentsInChildren(false, found);
        foreach (var source in found)
        {
            if (source == null || !Wanted(source)) continue;
            var skin = source as SkinnedMeshRenderer;
            parts.Add(new Part { Renderer = source, Skin = skin, Filter = skin == null ? source.GetComponent<MeshFilter>() : null });
        }
        found.Clear();
    }

    private static bool Wanted(Renderer source)
        => (source is SkinnedMeshRenderer || source is MeshRenderer) && source.GetComponentInParent<Canvas>(true) == null
            && !source.gameObject.name.StartsWith("Glow Shell");

    private static bool Materials()
    {
        if (mask != null) return true;
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null) return false;
        mask = new Material(shader) { name = "Own View Mask (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    // As a camera begins: one drawing to the screen gets the mark in its queue; any other (a scope's, a mirror's, the
    // editor's scene view) does not. Put in the queue here, as OutlinePass is, so no renderer asset needs it listed.
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null || parts.Count == 0) return;
        if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base) return;
        var renderer = data.scriptableRenderer;
        if (renderer == null || !Materials()) return;
        if (markPass == null) markPass = new MarkPass(this);
        renderer.EnqueuePass(markPass);
    }

    // What this camera draws of the player this frame: each solid submesh of each part that shows to it (switched on, not
    // hidden, not a first-person shadow, on a layer it draws, inside its view), so the mark lies exactly on what was drawn.
    private void Collect(Camera camera, Plane[] planes, List<MarkPass.Draw> draws)
    {
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        int layers = camera.cullingMask;
        foreach (var part in parts)
        {
            var renderer = part.Renderer;
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff
                || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly || (layers & (1 << renderer.gameObject.layer)) == 0
                || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
            Mesh mesh = part.Skin != null ? part.Skin.sharedMesh : part.Filter != null ? part.Filter.sharedMesh : null;
            if (mesh == null) continue;
            renderer.GetSharedMaterials(looks);
            // A part with no material draws nothing, and a mesh draws only as many parts as it has materials for.
            int count = Mathf.Min(mesh.subMeshCount, looks.Count);
            for (int i = 0; i < count; i++)
                if (looks[i] != null && looks[i].renderQueue <= SolidQueue) draws.Add(new MarkPass.Draw { Renderer = renderer, Submesh = i });
            looks.Clear();
        }
    }

    /// <summary>The mark drawn into the camera's own depth-stencil buffer from the player's own renderers.</summary>
    private sealed class MarkPass : ScriptableRenderPass
    {
        public struct Draw
        {
            public Renderer Renderer;
            public int Submesh;
        }

        private sealed class Data
        {
            public Material Mask;
            public readonly List<Draw> Draws = new List<Draw>();
        }

        private readonly OwnViewMask owner;
        private readonly List<Draw> draws = new List<Draw>();
        private readonly Plane[] planes = new Plane[6];

        public MarkPass(OwnViewMask owner)
        {
            this.owner = owner;
            // After the opaque world and the sky, so the depth test finds whatever stands in front of the player's parts;
            // before the transparent queue, so MomentGlow there and the outlines after it all find the mark.
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            if (owner == null || mask == null) return;
            var resources = frame.Get<UniversalResourceData>();
            var color = resources.activeColorTexture;
            var depth = resources.activeDepthTexture;
            if (!color.IsValid() || !depth.IsValid()) return;
            draws.Clear();
            owner.Collect(frame.Get<UniversalCameraData>().camera, planes, draws);
            if (draws.Count == 0) return;
            using (var builder = graph.AddRasterRenderPass<Data>("Own view mask", out var data))
            {
                data.Mask = mask;
                data.Draws.Clear();
                data.Draws.AddRange(draws);
                // The camera's own targets, as its opaque and transparent passes have them: the shader writes no colour
                // (ColorMask 0), tests against the depth and writes only the stencil's bit.
                builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((Data job, RasterGraphContext context) =>
                {
                    foreach (var draw in job.Draws)
                        if (draw.Renderer != null) context.cmd.DrawRenderer(draw.Renderer, job.Mask, draw.Submesh, 0);
                });
            }
        }
    }
}
