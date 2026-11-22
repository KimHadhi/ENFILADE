using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HudAddons
{
    /// <summary>
    /// Draws the MW3 Death Screen addon's screen (DeathScreenDriver's numbers) into the picture of the game camera that
    /// shows the world on the screen, after its post effects and before the HUD camera draws the HUD over it: the addon's
    /// RenderScreenspaceEffects (the colour) and HUDPaintBackground (the blur, then the vignette and the blood, "drawn
    /// under the normal HUD"), in that order, the HUD and our death card staying sharp on top as in Garry's Mod.
    /// <para>Why a render pass and not a Volume plus a canvas: URP's colour grading works in log light and its only
    /// full-screen blur is a depth-of-field bokeh, so neither could do what Garry's Mod did, and a UI canvas blends in
    /// linear light where Garry's Mod blended in the screen's gamma. Here each step is Garry's Mod's own maths
    /// (DeathScreenFilm.shader): pass 0 its colour modify; passes 1..n pp/blurscreen's 3x3 box, the i-th spaced
    /// (i / n) x the blur strength apart; the last pass the two pictures with their alpha, all on 8-bit gamma-encoded
    /// targets like its frame buffer.</para>
    /// <para>It is put in a camera's queue as the camera begins (RenderPipelineManager), so no renderer asset lists it;
    /// with no death showing it is not queued at all.</para>
    /// </summary>
    internal sealed class DeathScreenPass : ScriptableRenderPass
    {
        // Resources paths: the shader beside the addon's two pictures (Art/Resources/UI/Addons/DeathScreen).
        private const string ShaderPath = Pictures.Folder + "DeathScreen/DeathScreenFilm";
        private const string VignettePicture = "DeathScreen/vignette", BloodPicture = "DeathScreen/blood";
        private const int ColourPass = 0, BlurPass = 1, FilmPass = 2;
        // Profiler names of the steps.
        private const string ColourName = "Death screen: colour", BlurName = "Death screen: blur", FilmName = "Death screen: vignette and blood";

        private static readonly int SourceId = Shader.PropertyToID("_DeathSource"), VignetteId = Shader.PropertyToID("_DeathVignette"),
            BloodId = Shader.PropertyToID("_DeathBlood"), SizeId = Shader.PropertyToID("_DeathSize"), AddId = Shader.PropertyToID("_DeathAdd"),
            ModifyId = Shader.PropertyToID("_DeathModify"), MulId = Shader.PropertyToID("_DeathMul"), StepId = Shader.PropertyToID("_DeathStep"),
            FilmId = Shader.PropertyToID("_DeathFilm");

        private static DeathScreenPass pass;
        private static bool failed;

        private readonly Material material;
        private readonly Texture vignette, blood;
        // The numbers of the camera being recorded (set as it begins).
        private DeathScreenDriver.Frame frame;

        private sealed class PassData
        {
            public Material Material;
            public int Pass;
            public TextureHandle Source;
            public Vector4 Size, Add, Modify, Mul, Step, Film;
            public Texture Vignette, Blood;
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }

        private DeathScreenPass(Shader shader)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            // The picture is read back and drawn over: the camera's own, never the screen, which nothing can read.
            requiresIntermediateTexture = true;
            material = new Material(shader) { name = "Death Screen (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            // A picture that is missing (reported once by Pictures) is not drawn, as Garry's Mod's missing-texture
            // checkers would have been the addon's.
            vignette = Pictures.Get(VignettePicture);
            blood = Pictures.Get(BloodPicture);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            if (pass != null && pass.material != null) Object.Destroy(pass.material);
            pass = null;
            failed = false;
        }

        /// <summary>Puts the pass in every game camera's queue while a death screen shows. Safe to call again.</summary>
        internal static void Hook()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.beginCameraRendering += Begin;
        }

        /// <summary>Loads the shader and the two pictures now (when the HUD comes up), not on the first death.</summary>
        internal static void Prepare() => Get();

        private static DeathScreenPass Get()
        {
            if (pass != null || failed) return pass;
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported)
            {
                failed = true;
                Debug.LogWarning($"[HudAddons] DeathScreen: the shader Resources/{ShaderPath} is missing or not supported here; the death screen's colour, blur and pictures are off.");
                return null;
            }
            return pass = new DeathScreenPass(shader);
        }

        private static void Begin(ScriptableRenderContext context, Camera camera)
        {
            var driver = DeathScreenDriver.Existing;
            if (driver == null || !driver.TryFrame(out var numbers)) return;
            // The camera that shows the world on the screen: not a scope's picture, the scene view or the HUD camera.
            if (camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null) return;
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.renderType != CameraRenderType.Base) return;
            var renderer = data.scriptableRenderer;
            var self = Get();
            if (renderer == null || self == null) return;
            self.frame = numbers;
            renderer.EnqueuePass(self);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            var target = resources.activeColorTexture;
            if (!target.IsValid()) return;
            var desc = graph.GetTextureDesc(target);
            int width = desc.width, height = desc.height;
            if (width <= 0 || height <= 0) return;
            var size = new Vector4(width, height, 1f / width, 1f / height);
            // Garry's Mod's frame buffer and its copy (_rt_FullFrameFB): 8 bits a channel, gamma-encoded, filtered.
            var screen = new TextureDesc(width, height)
            {
                format = GraphicsFormat.R8G8B8A8_UNorm, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                name = "_DeathScreen"
            };

            var current = graph.CreateTexture(screen);
            Add(graph, ColourName, ColourPass, target, current, size, Vector4.zero);
            if (frame.BlurPixels > 0f)
            {
                // The addon's pixels read as 1080p pixels (the HUD canvas's measure), so the blur covers the same share of
                // any screen as it did of a 1080p one.
                float scale = Mathf.Sqrt(width / Gm.ScrW * (height / Gm.ScrH));
                for (int i = 1; i <= frame.BlurPasses; i++)
                {
                    float pixels = (float)i / frame.BlurPasses * frame.BlurPixels * scale;
                    var next = graph.CreateTexture(screen);
                    Add(graph, BlurName, BlurPass, current, next, size, new Vector4(pixels / width, pixels / height, 0f, 0f));
                    current = next;
                }
            }
            Add(graph, FilmName, FilmPass, current, target, size, Vector4.zero);
        }

        private void Add(RenderGraph graph, string name, int index, TextureHandle source, TextureHandle destination, Vector4 size, Vector4 step)
        {
            using (var builder = graph.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.Material = material;
                data.Pass = index;
                data.Source = source;
                data.Size = size;
                data.Step = step;
                data.Add = frame.Add;
                data.Modify = frame.Modify;
                data.Mul = frame.Mul;
                data.Film = new Vector4(vignette != null ? frame.Vignette : 0f, blood != null ? frame.Blood : 0f, 0f, 0f);
                data.Vignette = vignette != null ? vignette : Texture2D.blackTexture;
                data.Blood = blood != null ? blood : Texture2D.blackTexture;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData job, RasterGraphContext context) => Run(job, context));
            }
        }

        // One triangle over the whole target (DeathScreenFilm's vertex makes it from the vertex number).
        private static void Run(PassData job, RasterGraphContext context)
        {
            var block = job.Block;
            block.Clear();
            block.SetTexture(SourceId, (Texture)job.Source);
            block.SetVector(SizeId, job.Size);
            switch (job.Pass)
            {
                case ColourPass:
                    block.SetVector(AddId, job.Add);
                    block.SetVector(ModifyId, job.Modify);
                    block.SetVector(MulId, job.Mul);
                    break;
                case BlurPass:
                    block.SetVector(StepId, job.Step);
                    break;
                default:
                    block.SetTexture(VignetteId, job.Vignette);
                    block.SetTexture(BloodId, job.Blood);
                    block.SetVector(FilmId, job.Film);
                    break;
            }
            context.cmd.DrawProcedural(Matrix4x4.identity, job.Material, job.Pass, MeshTopology.Triangles, 3, 1, block);
        }
    }
}
