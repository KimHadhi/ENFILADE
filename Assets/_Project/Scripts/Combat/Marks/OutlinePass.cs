using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Draws what EnemyOutlines holds, in every game camera that draws a world (the player's view, a scope's picture), after the
/// transparent things (smoke included) and before the post effects, so an HDR band catches the bloom:
/// 1. Mask: every marked body's solid parts drawn into a mask of their own with the camera's samples a pixel and against
///    the camera's own depth buffer (shader OutlineMask): first where something stands in front of them, then where
///    nothing does, so the surface the eye sees wins each sample. The hardware's depth test tells seen from hidden (the
///    see-through rule, SeeThrough.hlsl) sample by sample; each says the band's colour for that, and the fill if hidden.
/// 2. Gather (OutlineLay pass 0): each pixel's samples as one: the band's colour, how much of the pixel wants a band, how
///    much a marked body covers, the fill.
/// 3. Across (pass 1): for each pixel, the pixel along its row whose body edge lies nearest, to a fraction of a pixel.
/// 4. Lay (pass 2): the true distance from the rows above and below it, and the band laid by it over what of the pixel no
///    body covers: full colour out to the band's width, then a thin dark edge so it reads on a bright wall as on a dark
///    one. Over the hidden parts the fill. Never over this PC's own first-person body, gun or scope lens (stencil bit 128,
///    OwnViewMask).
/// The samples are what keep the band and the fill still: laid from whole pixels they stepped, and a picture drifting a
/// fraction of a pixel a frame (an aimed sight's breathing) turned whole pixels of the band on and off, and lines of fill
/// along whatever stood in front of the body, the flicker the user saw on a pinged target while aiming (2026-11-21;
/// OutlineBench measures it as "sway").
/// Widths are pixels at 1080p and grow with the picture. It is put in a camera's queue as the camera begins
/// (RenderPipelineManager), so no renderer asset needs it listed; with nothing marked it costs nothing.
/// </summary>
public sealed class OutlinePass : ScriptableRenderPass
{
    private const string LayShader = "Combat/Shaders/OutlineLay";
    // The widest the search may reach (px), whatever the table and the screen say: the cost is twice this many reads a pixel.
    private const int Farthest = 24;
    private const int GatherPass = 0, AcrossPass = 1, LayPass = 2;
    // OutlineMask's passes, drawn in this order.
    private const int HiddenPass = 0, SeenPass = 1;
    private static readonly int EdgeMsId = Shader.PropertyToID("_OutlineEdgeMS"), FillMsId = Shader.PropertyToID("_OutlineFillMS"),
        GatheredId = Shader.PropertyToID("_OutlineGathered"), CoverId = Shader.PropertyToID("_OutlineCover"),
        FillId = Shader.PropertyToID("_OutlineFill"), AcrossId = Shader.PropertyToID("_OutlineAcross"),
        AcrossReachId = Shader.PropertyToID("_OutlineAcrossReach"), SizeId = Shader.PropertyToID("_OutlineSize"),
        WidthsId = Shader.PropertyToID("_OutlineWidths");

    private static OutlinePass pass;
    private readonly List<EnemyOutlines.Draw> draws = new List<EnemyOutlines.Draw>();
    private readonly Shader layShader;
    // OutlineLay with the keyword for each count of samples a pixel (the camera's: 1, 2, 4 or 8).
    private readonly Dictionary<int, Material> lays = new Dictionary<int, Material>();

    private sealed class MaskData
    {
        public readonly List<EnemyOutlines.Draw> Draws = new List<EnemyOutlines.Draw>();
    }

    // What one of the screen passes reads; each sets only its own.
    private struct Inputs
    {
        public TextureHandle EdgeMs, FillMs, Gathered, Cover, Fill, Across, AcrossReach;
    }

    private sealed class ScreenData
    {
        public Material Material;
        public int Pass;
        public Inputs Read;
        public Vector4 Size, Widths;
        public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    }

    private OutlinePass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        // The mask is tested against the camera's depth buffer and laid with its stencil: buffers of the camera's own,
        // never the screen's, which nothing else may draw beside.
        requiresIntermediateTexture = true;
        layShader = Resources.Load<Shader>(LayShader);
    }

    private Material Lay(int samples)
    {
        if (lays.TryGetValue(samples, out var made) && made != null) return made;
        made = new Material(layShader) { name = $"Outline Lay x{samples} (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        made.EnableKeyword($"OUTLINE_SAMPLES_{samples}");
        lays[samples] = made;
        return made;
    }

    /// <summary>Puts the pass in every game camera's queue as it begins. Play does this on its own; the editor's bench calls it.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Hook()
    {
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.beginCameraRendering += Begin;
    }

    private static void Begin(ScriptableRenderContext context, Camera camera)
    {
        if (!EnemyOutlines.Any || camera == null || camera.cameraType != CameraType.Game) return;
        if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base) return;
        var renderer = data.scriptableRenderer;
        if (renderer == null) return;
        if (pass == null) pass = new OutlinePass();
        if (pass.layShader == null) return;
        renderer.EnqueuePass(pass);
    }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
    {
        var resources = frame.Get<UniversalResourceData>();
        var cameraData = frame.Get<UniversalCameraData>();
        EnemyOutlines.Collect(cameraData.camera, draws);
        var target = resources.activeColorTexture;
        var depth = resources.activeDepthTexture;
        if (draws.Count == 0 || !target.IsValid() || !depth.IsValid()) return;
        var depthDesc = graph.GetTextureDesc(depth);
        int width = depthDesc.width, height = depthDesc.height;
        if (width <= 0 || height <= 0) return;
        // As many samples a pixel as the camera's depth buffer, which the mask is tested against sample by sample.
        var samples = depthDesc.msaaSamples;
        int count = samples == MSAASamples.MSAA8x ? 8 : samples == MSAASamples.MSAA4x ? 4 : samples == MSAASamples.MSAA2x ? 2 : 1;
        var lay = Lay(count);

        // The band and the dark edge outside it, in this picture's pixels; the search reaches half a pixel past the edge's
        // outer side, since a pixel's own edge may lie half a pixel from its middle.
        float scale = height / 1080f;
        float band = Mathf.Max(.5f, GameTables.Number(EnemyOutlines.TableName, EnemyOutlines.TableRow, "width", 3f)) * scale;
        float edge = Mathf.Max(0f, GameTables.Number(EnemyOutlines.TableName, EnemyOutlines.TableRow, "edge", 1.5f)) * scale;
        float edgeAlpha = Mathf.Clamp01(GameTables.Number(EnemyOutlines.TableName, EnemyOutlines.TableRow, "edgeAlpha", .45f));
        int reach = Mathf.Clamp(Mathf.CeilToInt(band + edge + 1f), 1, Farthest);
        var size = new Vector4(width, height, 0f, 0f);
        var widths = new Vector4(band, band + edge, edgeAlpha, reach);

        var edgeMs = graph.CreateTexture(Sampled(width, height, samples, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineEdgeMS"));
        var fillMs = graph.CreateTexture(Sampled(width, height, samples, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineFillMS"));
        using (var builder = graph.AddRasterRenderPass<MaskData>("Outline mask", out var data))
        {
            data.Draws.Clear();
            data.Draws.AddRange(draws);
            builder.SetRenderAttachment(edgeMs, 0, AccessFlags.Write);
            builder.SetRenderAttachment(fillMs, 1, AccessFlags.Write);
            // Tested against, never written: seen or hidden is the hardware's depth test, sample by sample.
            builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((MaskData job, RasterGraphContext context) =>
            {
                // Hidden first, then seen: the surface the eye sees wins each sample.
                foreach (var draw in job.Draws)
                    if (draw.Renderer != null) context.cmd.DrawRenderer(draw.Renderer, draw.Material, draw.Submesh, HiddenPass);
                foreach (var draw in job.Draws)
                    if (draw.Renderer != null) context.cmd.DrawRenderer(draw.Renderer, draw.Material, draw.Submesh, SeenPass);
            });
        }

        var gathered = graph.CreateTexture(Plain(width, height, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineGathered"));
        var cover = graph.CreateTexture(Plain(width, height, GraphicsFormat.R16G16_SFloat, "_OutlineCover"));
        var fill = graph.CreateTexture(Plain(width, height, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineFill"));
        using (var builder = graph.AddRasterRenderPass<ScreenData>("Outline gather", out var data))
        {
            Prepare(data, lay, GatherPass, new Inputs { EdgeMs = edgeMs, FillMs = fillMs }, size, widths);
            builder.UseTexture(edgeMs, AccessFlags.Read);
            builder.UseTexture(fillMs, AccessFlags.Read);
            builder.SetRenderAttachment(gathered, 0, AccessFlags.Write);
            builder.SetRenderAttachment(cover, 1, AccessFlags.Write);
            builder.SetRenderAttachment(fill, 2, AccessFlags.Write);
            builder.SetRenderFunc((ScreenData job, RasterGraphContext context) => DrawScreen(job, context));
        }

        var across = graph.CreateTexture(Plain(width, height, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineAcross"));
        var acrossReach = graph.CreateTexture(Plain(width, height, GraphicsFormat.R16G16_SFloat, "_OutlineAcrossReach"));
        using (var builder = graph.AddRasterRenderPass<ScreenData>("Outline across", out var data))
        {
            Prepare(data, lay, AcrossPass, new Inputs { Gathered = gathered, Cover = cover }, size, widths);
            builder.UseTexture(gathered, AccessFlags.Read);
            builder.UseTexture(cover, AccessFlags.Read);
            builder.SetRenderAttachment(across, 0, AccessFlags.Write);
            builder.SetRenderAttachment(acrossReach, 1, AccessFlags.Write);
            builder.SetRenderFunc((ScreenData job, RasterGraphContext context) => DrawScreen(job, context));
        }

        using (var builder = graph.AddRasterRenderPass<ScreenData>("Outline lay", out var data))
        {
            Prepare(data, lay, LayPass, new Inputs { Cover = cover, Fill = fill, Across = across, AcrossReach = acrossReach }, size, widths);
            builder.UseTexture(cover, AccessFlags.Read);
            builder.UseTexture(fill, AccessFlags.Read);
            builder.UseTexture(across, AccessFlags.Read);
            builder.UseTexture(acrossReach, AccessFlags.Read);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            // Read for its stencil: the player's own first-person things are left alone.
            builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
            builder.SetRenderFunc((ScreenData job, RasterGraphContext context) => DrawScreen(job, context));
        }
    }

    // The mask's own targets: the camera's samples a pixel, read sample by sample (no resolve).
    private static TextureDesc Sampled(int width, int height, MSAASamples samples, GraphicsFormat format, string name)
        => new TextureDesc(width, height)
        {
            format = format, msaaSamples = samples, bindTextureMS = samples != MSAASamples.None, clearBuffer = true,
            clearColor = Color.clear, name = name, filterMode = FilterMode.Point
        };

    private static TextureDesc Plain(int width, int height, GraphicsFormat format, string name)
        => new TextureDesc(width, height) { format = format, clearBuffer = true, clearColor = Color.clear, name = name, filterMode = FilterMode.Point };

    private static void Prepare(ScreenData data, Material lay, int index, Inputs read, Vector4 size, Vector4 widths)
    {
        data.Material = lay;
        data.Pass = index;
        data.Read = read;
        data.Size = size;
        data.Widths = widths;
    }

    // One triangle over the whole picture (OutlineLay's vertex makes it from the vertex number).
    private static void DrawScreen(ScreenData data, RasterGraphContext context)
    {
        var block = data.Block;
        block.Clear();
        var read = data.Read;
        switch (data.Pass)
        {
            case GatherPass:
                block.SetTexture(EdgeMsId, ((RTHandle)read.EdgeMs).rt);
                block.SetTexture(FillMsId, ((RTHandle)read.FillMs).rt);
                break;
            case AcrossPass:
                block.SetTexture(GatheredId, ((RTHandle)read.Gathered).rt);
                block.SetTexture(CoverId, ((RTHandle)read.Cover).rt);
                break;
            default:
                block.SetTexture(CoverId, ((RTHandle)read.Cover).rt);
                block.SetTexture(FillId, ((RTHandle)read.Fill).rt);
                block.SetTexture(AcrossId, ((RTHandle)read.Across).rt);
                block.SetTexture(AcrossReachId, ((RTHandle)read.AcrossReach).rt);
                break;
        }
        block.SetVector(SizeId, data.Size);
        block.SetVector(WidthsId, data.Widths);
        context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, data.Pass, MeshTopology.Triangles, 3, 1, block);
    }
}
