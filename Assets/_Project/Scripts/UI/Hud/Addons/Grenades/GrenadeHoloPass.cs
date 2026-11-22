using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HudAddons
{
    /// <summary>
    /// Draws Arctic's grenade holograms as its HUDPaint hook did with cam.Start3D: after the world (post effects included),
    /// under the 2D HUD, through a projection of its own (Source's fov 100, near 2, far 1024 units) whatever the player's
    /// field of view, over everything (cam.IgnoreZ), each model with the holo material. The models are laid out in the eye's
    /// own space by GrenadesHud each frame (Draws); this pass sets that projection, draws them into the player's camera
    /// after its post-processing, and puts the camera's matrices back. It is put in the camera's queue as the camera
    /// begins (RenderPipelineManager), only while there is a hologram to draw, so no renderer asset lists it.
    /// </summary>
    internal sealed class GrenadeHoloPass : ScriptableRenderPass
    {
        /// <summary>One submesh of one model, its matrix in the eye's space (x right, y up, z ahead, metres).</summary>
        internal struct Draw
        {
            public Mesh Mesh;
            public int Submesh;
            public Matrix4x4 Matrix;
        }

        private sealed class PassData
        {
            public readonly List<Draw> Draws = new List<Draw>();
            public Material Material;
            public TextureHandle Target;
            public Matrix4x4 Projection, CameraView, CameraProjection;
        }

        // The eye's space to view space: Unity's view looks down -z.
        private static readonly Matrix4x4 EyeToView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
        private static GrenadeHoloPass pass;
        private static bool hooked;

        /// <summary>This frame's models to draw (GrenadesHud fills it in LateUpdate).</summary>
        public static readonly List<Draw> Draws = new List<Draw>();
        /// <summary>The holo material (GrenadeHolo shader), its frames and colour set by GrenadesHud.</summary>
        public static Material Material;
        /// <summary>The camera the holograms are drawn into: the player's view, the one the HUD reads EyePos from.</summary>
        public static Camera Eye;
        /// <summary>The hologram's projection: vertical field of view (°), near and far (m).</summary>
        public static float FieldOfView = 83.6f, Near = .0508f, Far = 26f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pass = null;
            hooked = false;
            Draws.Clear();
            Material = null;
            Eye = null;
        }

        private GrenadeHoloPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += Begin;
        }

        public static void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            RenderPipelineManager.beginCameraRendering -= Begin;
            Draws.Clear();
        }

        private static void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (Draws.Count == 0 || Material == null || camera == null || camera != Eye || camera.cameraType != CameraType.Game) return;
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base) return;
            var renderer = data.scriptableRenderer;
            if (renderer == null) return;
            pass ??= new GrenadeHoloPass();
            renderer.EnqueuePass(pass);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var resources = frame.Get<UniversalResourceData>();
            var cameraData = frame.Get<UniversalCameraData>();
            var target = resources.activeColorTexture;
            if (Draws.Count == 0 || Material == null || !target.IsValid()) return;
            using (var builder = graph.AddRasterRenderPass<PassData>("Grenade hologram", out var data))
            {
                data.Draws.Clear();
                data.Draws.AddRange(Draws);
                data.Material = Material;
                data.Target = target;
                data.Projection = Matrix4x4.Perspective(FieldOfView, cameraData.camera.aspect, Near, Far);
                data.CameraView = cameraData.GetViewMatrix();
                data.CameraProjection = cameraData.GetProjectionMatrix();
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData job, RasterGraphContext context) => DrawAll(job, context));
            }
        }

        private static void DrawAll(PassData job, RasterGraphContext context)
        {
            var cmd = context.cmd;
            // Flipped as URP's own camera-override pass flips it (RenderObjectsPass): by where the target's picture starts.
            bool flipped = context.GetTextureUVOrigin(in job.Target) == TextureUVOrigin.BottomLeft;
            RenderingUtils.SetViewAndProjectionMatrices(cmd, EyeToView, GL.GetGPUProjectionMatrix(job.Projection, flipped), false);
            var draws = job.Draws;
            for (int i = 0; i < draws.Count; i++)
                if (draws[i].Mesh != null) cmd.DrawMesh(draws[i].Mesh, draws[i].Matrix, job.Material, draws[i].Submesh, 0);
            RenderingUtils.SetViewAndProjectionMatrices(cmd, job.CameraView, GL.GetGPUProjectionMatrix(job.CameraProjection, flipped), false);
        }
    }
}
