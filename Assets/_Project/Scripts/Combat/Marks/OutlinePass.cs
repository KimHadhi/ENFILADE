using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Draws what EnemyOutlines holds, in every game camera that draws a world (the player's view, a scope's picture), after the
/// transparent things (smoke included) and before the post effects, so an HDR band catches the bloom:
/// 1. Mask: every marked body's parts drawn into a mask of their own (shader OutlineMask), the nearest marked body winning
///    each pixel. Each pixel says whether that body is seen or hidden there (the see-through rule, SeeThrough.hlsl), the
///    band's colour for that, and the fill if hidden.
/// 2. Across (OutlineLay pass 0): for each pixel, the nearest pixel along its row that wants a band, and its colour.
/// 3. Lay (OutlineLay pass 1): for each pixel the mask doesn't cover, the nearest such pixel in the round from the rows
///    above and below it (two one-way searches make the true distance), and the band laid by that distance: full
///    colour out to the band's width, then a thin dark edge so it reads on a bright wall as on a dark one. Over the
///    hidden parts the fill. Never over this PC's own first-person body, gun or scope lens (stencil bit 128, OwnViewMask).
/// Widths are pixels at 1080p and grow with the picture. It is put in a camera's queue as the camera begins
/// (RenderPipelineManager), so no renderer asset needs it listed; with nothing marked it costs nothing.
/// </summary>
public sealed class OutlinePass : ScriptableRenderPass
{
    private const string LayShader = "Combat/Shaders/OutlineLay";
    // The widest the search may reach (px), whatever the table and the screen say: the cost is twice this many reads a pixel.
    private const int Farthest = 24;
    private static readonly int EdgeId = Shader.PropertyToID("_OutlineEdge"), FillId = Shader.PropertyToID("_OutlineFill"),
        AcrossId = Shader.PropertyToID("_OutlineAcross"), AcrossReachId = Shader.PropertyToID("_OutlineAcrossReach"),
        SizeId = Shader.PropertyToID("_OutlineSize"), WidthsId = Shader.PropertyToID("_OutlineWidths");

    private static OutlinePass pass;
    private readonly List<EnemyOutlines.Draw> draws = new List<EnemyOutlines.Draw>();
    private readonly Material lay;

    private sealed class MaskData
    {
        public readonly List<EnemyOutlines.Draw> Draws = new List<EnemyOutlines.Draw>();
    }

    private sealed class ScreenData
    {
        public Material Material;
        public int Pass;
        public TextureHandle Edge, Fill, Across, AcrossReach;
        public Vector4 Size, Widths;
        public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    }

    private OutlinePass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        var shader = Resources.Load<Shader>(LayShader);
        if (shader != null) lay = new Material(shader) { name = "Outline Lay (Runtime)", hideFlags = HideFlags.HideAndDontSave };
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
        if (pass.lay == null) return;
        renderer.EnqueuePass(pass);
    }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
    {
        var resources = frame.Get<UniversalResourceData>();
        var cameraData = frame.Get<UniversalCameraData>();
        EnemyOutlines.Collect(cameraData.camera, draws);
        var target = resources.activeColorTexture;
        if (draws.Count == 0 || !target.IsValid() || !resources.activeDepthTexture.IsValid()) return;
        var targetDesc = graph.GetTextureDesc(target);
        int width = targetDesc.width, height = targetDesc.height;
        if (width <= 0 || height <= 0) return;

        // The band and the dark edge outside it, in this picture's pixels.
        float scale = height / 1080f;
        float band = Mathf.Max(.5f, GameTables.Number(EnemyOutlines.TableName, EnemyOutlines.TableRow, "width", 3f)) * scale;
        float edge = Mathf.Max(0f, GameTables.Number(EnemyOutlines.TableName, EnemyOutlines.TableRow, "edge", 1.5f)) * scale;
        float edgeAlpha = Mathf.Clamp01(GameTables.Number(EnemyOutlines.TableName, EnemyOutlines.TableRow, "edgeAlpha", .45f));
        int reach = Mathf.Clamp(Mathf.CeilToInt(band + edge), 1, Farthest);
        var size = new Vector4(width, height, 0f, 0f);
        var widths = new Vector4(band, reach, edgeAlpha, 0f);

        var edgeMask = graph.CreateTexture(Colour(width, height, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineEdge"));
        var fillMask = graph.CreateTexture(Colour(width, height, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineFill"));
        var maskDepth = graph.CreateTexture(new TextureDesc(width, height) { depthBufferBits = DepthBits.Depth32, clearBuffer = true, name = "_OutlineDepth" });
        using (var builder = graph.AddRasterRenderPass<MaskData>("Outline mask", out var data))
        {
            data.Draws.Clear();
            data.Draws.AddRange(draws);
            builder.SetRenderAttachment(edgeMask, 0, AccessFlags.Write);
            builder.SetRenderAttachment(fillMask, 1, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(maskDepth, AccessFlags.Write);
            // Seen or hidden is told against the scene's depth (SeeThrough.hlsl).
            if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((MaskData job, RasterGraphContext context) =>
            {
                foreach (var draw in job.Draws)
                    if (draw.Renderer != null) context.cmd.DrawRenderer(draw.Renderer, draw.Material, draw.Submesh, 0);
            });
        }

        var across = graph.CreateTexture(Colour(width, height, GraphicsFormat.R16G16B16A16_SFloat, "_OutlineAcross"));
        var acrossReach = graph.CreateTexture(Colour(width, height, GraphicsFormat.R16_SFloat, "_OutlineAcrossReach"));
        using (var builder = graph.AddRasterRenderPass<ScreenData>("Outline across", out var data))
        {
            Prepare(data, 0, edgeMask, fillMask, across, acrossReach, size, widths);
            builder.UseTexture(edgeMask, AccessFlags.Read);
            builder.SetRenderAttachment(across, 0, AccessFlags.Write);
            builder.SetRenderAttachment(acrossReach, 1, AccessFlags.Write);
            builder.SetRenderFunc((ScreenData job, RasterGraphContext context) => DrawScreen(job, context));
        }

        using (var builder = graph.AddRasterRenderPass<ScreenData>("Outline lay", out var data))
        {
            Prepare(data, 1, edgeMask, fillMask, across, acrossReach, size, widths);
            builder.UseTexture(edgeMask, AccessFlags.Read);
            builder.UseTexture(fillMask, AccessFlags.Read);
            builder.UseTexture(across, AccessFlags.Read);
            builder.UseTexture(acrossReach, AccessFlags.Read);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            // Read for its stencil: the player's own first-person things are left alone.
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.SetRenderFunc((ScreenData job, RasterGraphContext context) => DrawScreen(job, context));
        }
    }

    private static TextureDesc Colour(int width, int height, GraphicsFormat format, string name)
        => new TextureDesc(width, height) { format = format, clearBuffer = true, clearColor = Color.clear, name = name, filterMode = FilterMode.Point };

    private void Prepare(ScreenData data, int index, TextureHandle edge, TextureHandle fill, TextureHandle across, TextureHandle acrossReach,
        Vector4 size, Vector4 widths)
    {
        data.Material = lay;
        data.Pass = index;
        data.Edge = edge;
        data.Fill = fill;
        data.Across = across;
        data.AcrossReach = acrossReach;
        data.Size = size;
        data.Widths = widths;
    }

    // One triangle over the whole picture (OutlineLay's vertex makes it from the vertex number).
    private static void DrawScreen(ScreenData data, RasterGraphContext context)
    {
        var block = data.Block;
        block.Clear();
        block.SetTexture(EdgeId, ((RTHandle)data.Edge).rt);
        if (data.Pass == 1)
        {
            block.SetTexture(FillId, ((RTHandle)data.Fill).rt);
            block.SetTexture(AcrossId, ((RTHandle)data.Across).rt);
            block.SetTexture(AcrossReachId, ((RTHandle)data.AcrossReach).rt);
        }
        block.SetVector(SizeId, data.Size);
        block.SetVector(WidthsId, data.Widths);
        context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, data.Pass, MeshTopology.Triangles, 3, 1, block);
    }
}
