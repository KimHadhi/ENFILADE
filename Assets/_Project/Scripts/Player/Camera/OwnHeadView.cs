using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The own head's hair, ears and hat in first person, laid over the player's own view so that what comes right up to the
/// camera thins out and goes instead of being cut (the user, 2026-11-23: "머리카락이 자꾸 가까운 부분은 사라져, 즉 일부만 보임",
/// "어떤 캐릭터는 릴툰을 쓰고 어떤 캐릭터는 포요요미 … 모드로 유저들이 추가한 캐릭터는 어떤 쉐이더를 쓸지 모르는 상황").
/// It is the one body the world has, not a copy: the same renderers in the same pose, drawn as the camera's own passes draw
/// anything (a renderer list of the camera's own culling, by the same shader passes, lit by the same light probes, lights and
/// shadows), only into a picture of their own first and then laid over the camera's:
/// - each sample's distance from the camera gives how much of it shows: none nearer than PlayerBody.ownHeadFade's first
///   number, all of it from the second, in between that share of its colour (as an eye sees a strand at its lashes: not
///   sharp, not cut);
/// - the camera's own depth still decides what is in front, so another player's finger at the face covers the hair behind it;
/// - laid after the transparent queue, as what it is: the nearest thing there is, a few centimetres from the eyes. Smoke,
///   fire, glass, tracers, a muzzle's flame and the boards standing about the map (world-space canvases) all lie further off,
///   so they go behind it, its thinning parts too (the user, 2026-11-23: "현실적이고 자연스럽고 몰입감 있는 쪽으로"). 0.9.369
///   laid it before them so that smoke would cover it, and with the smoke every board of the map showed over the hair ("이런
///   맵에 있는 UI … 머리 무시하고 걍 위에 그려져 있는 게 의도된 거야?");
/// - where it shows it leaves the own-view mark (stencil bit 128, OwnViewMask), so the see-through outlines drawn after it
///   go behind it as behind the rest of the body.
/// Whatever shader a character's hair uses (lilToon, Poiyomi, one a player's mod brings), it is drawn as that shader draws it:
/// nothing here asks anything of the shader. Between frames the renderers are first-person shadows (PlayerBody.
/// ApplyHeadVisibility), as the face is, so any other camera leaves them out or shows them its own way (a mirror, a callout's
/// portrait). For the moment the player's own view renders they are put on a layer of their own (WorldLayers.OwnHead) that the
/// renderer assets leave out of every pass of theirs, and drawn whole, with their shadow; then they go back.
/// 0.9.367 drew them one renderer at a time (DrawRenderer): no light probe reached lilToon's hair that way, and it came out
/// flat and dark against the mirror's (the user's screenshots of 2026-11-23). Up to 0.9.366 the whole head was drawn by the camera's own passes and the near clip plane
/// cut it: at 5 cm the hair near the eyes and any finger at the face, at 1 cm a strand swinging into the camera.
/// </summary>
[DisallowMultipleComponent]
public sealed class OwnHeadView : MonoBehaviour
{
    private const string ShaderPath = "Combat/Shaders/OwnHeadView";
    private const int ColourPass = 0, MarkPass = 1;
    private static readonly int ColourId = Shader.PropertyToID("_OwnHeadColor"), DepthId = Shader.PropertyToID("_OwnHeadDepth"),
        FadeId = Shader.PropertyToID("_OwnHeadFade"), SizeId = Shader.PropertyToID("_OwnHeadSize");
    // The passes URP's own opaque and transparent passes draw, in their order (DrawObjectsPass).
    private static readonly List<ShaderTagId> Tags = new List<ShaderTagId>
        { new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly") };
    private static Shader shader;
    // The lay with the keyword for each count of samples a pixel (the camera's: 1, 2, 4 or 8).
    private static readonly Dictionary<int, Material> lays = new Dictionary<int, Material>();

    // A part put on the layer for the view's render, and what it had before.
    private struct Lifted
    {
        public Renderer Renderer;
        public int Layer;
        public ShadowCastingMode Mode;
    }

    private readonly List<Lifted> lifted = new List<Lifted>();
    private IReadOnlyList<Renderer> parts;
    private Camera eyes;
    private PlayerBody body;
    private HeadPass pass;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        shader = null;
        lays.Clear();
    }

    /// <summary>Lays <paramref name="renderers"/> over what <paramref name="camera"/> sees, faded by <paramref name="owner"/>'s
    /// ownHeadFade, until Hide.</summary>
    internal void Show(Camera camera, IReadOnlyList<Renderer> renderers, PlayerBody owner)
    {
        eyes = camera;
        parts = renderers;
        body = owner;
    }

    internal void Hide() => parts = null;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeRender;
        RenderPipelineManager.endCameraRendering += AfterRender;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        RenderPipelineManager.endCameraRendering -= AfterRender;
        Drop();
    }

    private static bool Ready()
    {
        if (shader == null) shader = Resources.Load<Shader>(ShaderPath);
        return shader != null;
    }

    private static Material Lay(int samples)
    {
        if (lays.TryGetValue(samples, out var made) && made != null) return made;
        made = new Material(shader) { name = $"Own Head Lay x{samples} (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        made.EnableKeyword($"OWN_HEAD_SAMPLES_{samples}");
        lays[samples] = made;
        return made;
    }

    // As a camera begins, before it culls: only the player's own view, drawing to the screen, lays the head; a mirror, a
    // scope, the editor's scene view and any other camera find the parts as first-person shadows.
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        Drop();
        if (parts == null || parts.Count == 0 || camera == null || camera != eyes || camera.targetTexture != null) return;
        if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base) return;
        var renderer = data.scriptableRenderer;
        if (renderer == null || !Ready()) return;
        Lift();
        if (lifted.Count == 0) return;
        if (pass == null) pass = new HeadPass(this);
        renderer.EnqueuePass(pass);
    }

    private void AfterRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == eyes) Drop();
    }

    // Each part that shows, on the head's layer and drawn whole (with its shadow) for this render.
    private void Lift()
    {
        foreach (var renderer in parts)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) continue;
            lifted.Add(new Lifted { Renderer = renderer, Layer = renderer.gameObject.layer, Mode = renderer.shadowCastingMode });
            renderer.gameObject.layer = WorldLayers.OwnHead;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
    }

    // Back as they were, before anything but this render sees them.
    private void Drop()
    {
        foreach (var part in lifted)
        {
            if (part.Renderer == null) continue;
            part.Renderer.gameObject.layer = part.Layer;
            part.Renderer.shadowCastingMode = part.Mode;
        }
        lifted.Clear();
    }

    private Vector2 Fade => body != null ? body.OwnHeadFade : new Vector2(.02f, .05f);

    /// <summary>The head drawn into its own picture, then laid over the camera's.</summary>
    private sealed class HeadPass : ScriptableRenderPass
    {
        private sealed class DrawData
        {
            public RendererListHandle Solid, Clear;
        }

        private sealed class LayData
        {
            public Material Lay;
            public TextureHandle Colour, Depth;
            public Vector4 Fade, Size;
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }

        private readonly OwnHeadView owner;

        public HeadPass(OwnHeadView owner)
        {
            this.owner = owner;
            // After the transparent queue, so everything further off than the hair lies behind it; before the see-through
            // outlines (OutlinePass, AfterRenderingTransparents), so they find the mark it leaves.
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents - 1;
            // Laid with the camera's own depth buffer, never the screen's.
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            if (owner == null || owner.lifted.Count == 0) return;
            var resources = frame.Get<UniversalResourceData>();
            var cameraData = frame.Get<UniversalCameraData>();
            var renderingData = frame.Get<UniversalRenderingData>();
            var lightData = frame.Get<UniversalLightData>();
            var camera = cameraData.camera;
            var target = resources.activeColorTexture;
            var depth = resources.activeDepthTexture;
            if (camera == null || !target.IsValid() || !depth.IsValid()) return;
            var depthDesc = graph.GetTextureDesc(depth);
            int width = depthDesc.width, height = depthDesc.height;
            if (width <= 0 || height <= 0) return;
            // As many samples a pixel as the camera's buffers, laid sample by sample, so the hair's edges are smoothed as the
            // rest of the picture's are.
            var samples = depthDesc.msaaSamples;
            int count = samples == MSAASamples.MSAA8x ? 8 : samples == MSAASamples.MSAA4x ? 4 : samples == MSAASamples.MSAA2x ? 2 : 1;
            var lay = Lay(count);

            // A colour with its own alpha (the camera's may have none): where nothing of the head is, it stays clear.
            var colour = graph.CreateTexture(new TextureDesc(width, height)
            {
                format = GraphicsFormat.R16G16B16A16_SFloat, msaaSamples = samples, bindTextureMS = count > 1, clearBuffer = true,
                clearColor = Color.clear, name = "_OwnHeadColor", filterMode = FilterMode.Point
            });
            var headDepthDesc = depthDesc;
            headDepthDesc.name = "_OwnHeadDepth";
            headDepthDesc.clearBuffer = true;
            headDepthDesc.bindTextureMS = count > 1;
            var headDepth = graph.CreateTexture(headDepthDesc);

            // The head's layer, as the camera's own opaque and transparent passes would draw it.
            int layer = 1 << WorldLayers.OwnHead;
            var solid = graph.CreateRendererList(new RendererListParams(renderingData.cullResults,
                CreateDrawingSettings(Tags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags),
                new FilteringSettings(RenderQueueRange.opaque, layer)));
            var clear = graph.CreateRendererList(new RendererListParams(renderingData.cullResults,
                CreateDrawingSettings(Tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent),
                new FilteringSettings(RenderQueueRange.transparent, layer)));

            using (var builder = graph.AddRasterRenderPass<DrawData>("Own head", out var data))
            {
                data.Solid = solid;
                data.Clear = clear;
                builder.UseRendererList(solid);
                builder.UseRendererList(clear);
                builder.SetRenderAttachment(colour, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(headDepth, AccessFlags.Write);
                // The lights' shadows and the rest URP has set for the frame, as its own passes read them.
                builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((DrawData job, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(job.Solid);
                    context.cmd.DrawRendererList(job.Clear);
                });
            }

            Vector2 fade = owner.Fade;
            float tanUp = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            using (var builder = graph.AddRasterRenderPass<LayData>("Own head lay", out var data))
            {
                data.Lay = lay;
                data.Colour = colour;
                data.Depth = headDepth;
                data.Fade = new Vector4(Mathf.Max(0f, fade.x), Mathf.Max(fade.x + .001f, fade.y), 0f, 0f);
                data.Size = new Vector4(width, height, tanUp * width / height, tanUp);
                builder.UseTexture(colour, AccessFlags.Read);
                builder.UseTexture(headDepth, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((LayData job, RasterGraphContext context) =>
                {
                    var block = job.Block;
                    block.Clear();
                    block.SetTexture(ColourId, ((RTHandle)job.Colour).rt);
                    block.SetTexture(DepthId, ((RTHandle)job.Depth).rt);
                    block.SetVector(FadeId, job.Fade);
                    block.SetVector(SizeId, job.Size);
                    // One triangle over the whole picture: the colour, then the mark where it shows.
                    context.cmd.DrawProcedural(Matrix4x4.identity, job.Lay, ColourPass, MeshTopology.Triangles, 3, 1, block);
                    context.cmd.DrawProcedural(Matrix4x4.identity, job.Lay, MarkPass, MeshTopology.Triangles, 3, 1, block);
                });
            }
        }
    }
}
