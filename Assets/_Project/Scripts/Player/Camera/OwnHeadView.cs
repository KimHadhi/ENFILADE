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
/// It is the one body the world has, not a copy: the same renderers in the same pose, drawn by their own materials and shader
/// passes (DrawRenderer, as OwnViewMask draws them), only into a picture of their own first and then laid over the camera's:
/// - each sample's distance from the camera gives how much of it shows: none nearer than PlayerBody.ownHeadFade's first
///   number, all of it from the second, in between that share of its colour (as an eye sees a strand at its lashes: not
///   sharp, not cut);
/// - the camera's own depth still decides what is in front, so another player's finger at the face covers the hair behind it;
/// - where it shows it writes its depth and the own-view mark (stencil bit 128, OwnViewMask), so what is drawn later (smoke,
///   glass, the see-through outlines) goes behind it as behind the rest of the body.
/// Whatever shader a character's hair uses (lilToon, Poiyomi, one a player's mod brings), it is drawn as that shader draws it:
/// nothing here asks anything of the shader. The renderers are first-person shadows meanwhile (PlayerBody.ApplyHeadVisibility),
/// so the camera's own passes leave them out and only their shadow is cast; a mirror shows them whole (HubMirror).
/// Up to 0.9.366 the whole head was drawn by the camera's own passes and the near clip plane cut it: at 5 cm the hair near the
/// eyes and any finger at the face, at 1 cm a strand swinging into the camera, cut off where it crossed.
/// </summary>
[DisallowMultipleComponent]
public sealed class OwnHeadView : MonoBehaviour
{
    private const string ShaderPath = "Combat/Shaders/OwnHeadView";
    // The last queue a material may have and still write depth: solid parts first, then the see-through ones far to near.
    private const int SolidQueue = 2500;
    private const int ColourPass = 0, DepthPass = 1;
    private static readonly ShaderTagId LightMode = new ShaderTagId("LightMode");
    private static readonly int ColourId = Shader.PropertyToID("_OwnHeadColor"), DepthId = Shader.PropertyToID("_OwnHeadDepth"),
        FadeId = Shader.PropertyToID("_OwnHeadFade"), SizeId = Shader.PropertyToID("_OwnHeadSize");
    private static Shader shader;
    // The lay with the keyword for each count of samples a pixel (the camera's: 1, 2, 4 or 8).
    private static readonly Dictionary<int, Material> lays = new Dictionary<int, Material>();
    // Each material's passes that draw it to the screen in URP (its forward pass, an outline's unlit pass), found once.
    private static readonly Dictionary<Material, int[]> passes = new Dictionary<Material, int[]>();

    private struct Draw
    {
        public Renderer Renderer;
        public Material Material;
        public int Submesh, Pass;
        public float Far;
    }

    private readonly List<Material> looks = new List<Material>();
    private readonly List<int> found = new List<int>();
    private IReadOnlyList<Renderer> parts;
    private Camera eyes;
    private PlayerBody body;
    private HeadPass pass;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        shader = null;
        lays.Clear();
        passes.Clear();
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

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeRender;

    private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeRender;

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

    // As a camera begins: only the player's own view, drawing to the screen, lays the head; a mirror, a scope, the editor's
    // scene view and any other camera draw it as they draw the rest of the body.
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (parts == null || parts.Count == 0 || camera == null || camera != eyes || camera.targetTexture != null) return;
        if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base) return;
        var renderer = data.scriptableRenderer;
        if (renderer == null || !Ready()) return;
        if (pass == null) pass = new HeadPass(this);
        renderer.EnqueuePass(pass);
    }

    private Vector2 Fade => body != null ? body.OwnHeadFade : new Vector2(.02f, .05f);

    // What of the head this camera draws this frame: each submesh of each part that is switched on and in view, by each of its
    // material's screen passes; the solid ones first, then the see-through ones far to near.
    private void Collect(Camera camera, Plane[] planes, List<Draw> solid, List<Draw> clear)
    {
        if (parts == null) return;
        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        Vector3 at = camera.transform.position;
        foreach (var renderer in parts)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff
                || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            if (mesh == null) continue;
            renderer.GetSharedMaterials(looks);
            float far = (renderer.bounds.center - at).sqrMagnitude;
            // A mesh draws only as many parts as it has materials for.
            int count = Mathf.Min(mesh.subMeshCount, looks.Count);
            for (int i = 0; i < count; i++)
            {
                var look = looks[i];
                if (look == null || look.shader == null) continue;
                var list = look.renderQueue <= SolidQueue ? solid : clear;
                foreach (int index in PassesOf(look))
                    list.Add(new Draw { Renderer = renderer, Material = look, Submesh = i, Pass = index, Far = far });
            }
            looks.Clear();
        }
        clear.Sort((a, b) => b.Far.CompareTo(a.Far));
    }

    // The passes URP draws a material with on the screen, in their order: its forward pass, and an unlit one (lilToon's
    // outline, or a shader that names no light mode), each as the material has it switched on.
    private int[] PassesOf(Material look)
    {
        if (passes.TryGetValue(look, out var known)) return known;
        found.Clear();
        for (int index = 0; index < look.passCount; index++)
        {
            string mode = look.shader.FindPassTagValue(index, LightMode).name;
            if (string.IsNullOrEmpty(mode)) mode = "SRPDefaultUnlit";
            if (mode != "UniversalForward" && mode != "UniversalForwardOnly" && mode != "SRPDefaultUnlit") continue;
            if (look.GetShaderPassEnabled(mode)) found.Add(index);
        }
        known = found.ToArray();
        passes[look] = known;
        return known;
    }

    /// <summary>The head drawn into its own picture, then laid over the camera's.</summary>
    private sealed class HeadPass : ScriptableRenderPass
    {
        private sealed class DrawData
        {
            public readonly List<Draw> Solid = new List<Draw>(), Clear = new List<Draw>();
        }

        private sealed class LayData
        {
            public Material Lay;
            public TextureHandle Colour, Depth;
            public Vector4 Fade, Size;
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }

        private readonly OwnHeadView owner;
        private readonly List<Draw> solid = new List<Draw>(), clear = new List<Draw>();
        private readonly Plane[] planes = new Plane[6];

        public HeadPass(OwnHeadView owner)
        {
            this.owner = owner;
            // After the opaque world and the sky, so the lay is tested against all of it; before the transparent queue, so
            // what is drawn there goes behind the hair as behind the rest of the body.
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            // Laid with the camera's own depth buffer, never the screen's.
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            if (owner == null) return;
            var resources = frame.Get<UniversalResourceData>();
            var camera = frame.Get<UniversalCameraData>().camera;
            var target = resources.activeColorTexture;
            var depth = resources.activeDepthTexture;
            if (camera == null || !target.IsValid() || !depth.IsValid()) return;
            solid.Clear();
            clear.Clear();
            owner.Collect(camera, planes, solid, clear);
            if (solid.Count + clear.Count == 0) return;
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

            using (var builder = graph.AddRasterRenderPass<DrawData>("Own head", out var data))
            {
                data.Solid.Clear();
                data.Solid.AddRange(solid);
                data.Clear.Clear();
                data.Clear.AddRange(clear);
                builder.SetRenderAttachment(colour, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(headDepth, AccessFlags.Write);
                // The hair's own shaders light it as the camera's own passes would: the lights, their shadows and the rest
                // URP has set for the frame.
                builder.UseAllGlobalTextures(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((DrawData job, RasterGraphContext context) =>
                {
                    foreach (var draw in job.Solid)
                        if (draw.Renderer != null) context.cmd.DrawRenderer(draw.Renderer, draw.Material, draw.Submesh, draw.Pass);
                    foreach (var draw in job.Clear)
                        if (draw.Renderer != null) context.cmd.DrawRenderer(draw.Renderer, draw.Material, draw.Submesh, draw.Pass);
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
                    // One triangle over the whole picture: the colour, then the depth and the mark where it shows.
                    context.cmd.DrawProcedural(Matrix4x4.identity, job.Lay, ColourPass, MeshTopology.Triangles, 3, 1, block);
                    context.cmd.DrawProcedural(Matrix4x4.identity, job.Lay, DepthPass, MeshTopology.Triangles, 3, 1, block);
                });
            }
        }
    }
}
