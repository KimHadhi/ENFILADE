using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HudAddons
{
    /// <summary>
    /// HUD Mk II's special effects (sfx.lua, the options' Special Effects: scanlines, aberration, glitch). The addon draws
    /// its whole HUD into a render target of the screen's size and lays that on the screen through three materials; here the
    /// HUD's canvas is drawn by a camera of its own into a render texture of the screen's size (the canvas turned Screen
    /// Space - Camera, the camera far below the world seeing only that canvas, as HudCamera does), and PlayerHudFxImage
    /// lays the texture over the screen through the PlayerHudFx shader. With the effects off the canvas goes back to a
    /// plain overlay and the camera stops, so nothing is paid for them.
    /// <para>The glitch: every hit cut the screen into strips 1/75 to 1/25 of its height and moved them sideways, twelve new
    /// cuts in half a second (Nene.GenerateGlitch(0.5, 12, 10)). Twelve jumps in half a second strobe, so under the
    /// photosensitivity rule it is one cut eased out and back as a single smooth pulse (FxRow rise/fall, under 1.6 Hz),
    /// held while hits keep coming. A new cut is made only once the last has eased home, so the strips never jump.</para>
    /// <para>The camera draws with the renderer that runs no features (PlainRenderer), so the game renderer's SSAO no
    /// longer runs over its empty picture; the camera itself is the effects' price, paid only while they are on.</para>
    /// </summary>
    internal sealed class PlayerHudFx
    {
        public const string ShaderPath = "UI/Addons/PlayerHud/PlayerHudFx";
        // Far below the world and below HudCamera's spot (0, -20000, 0), so no other camera sees this canvas.
        private static readonly Vector3 Home = new Vector3(0f, -40000f, 0f);
        private const float PlaneDistance = 10f;
        private const int MostStrips = 128;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha"), ScanId = Shader.PropertyToID("_Scan");
        private static bool reported;

        private readonly Overlay hud;
        private readonly PlayerHudFxImage image;
        private readonly Material material;
        private readonly PlayerHudFxImage.Strip[] strips = new PlayerHudFxImage.Strip[MostStrips];
        private Camera view;
        private RenderTexture target;
        private int stripCount, layout;
        private float glitch, heldUntil = float.NegativeInfinity;
        private bool on;

        public PlayerHudFx(Overlay hud, RectTransform layer)
        {
            this.hud = hud;
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported)
            {
                if (!reported) Debug.LogWarning($"[HudAddons] PlayerHud: no usable shader at Resources/{ShaderPath}: the HUD is drawn without its special effects.");
                reported = true;
                return;
            }
            material = new Material(shader) { name = "PlayerHud FX (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            image = PlayerHudFxImage.Create(layer, material);
            image.enabled = false;
        }

        /// <summary>The effects are drawing this frame (wanted, and possible here).</summary>
        public bool On => on;

        /// <summary>A hit: the glitch eases in (a fresh cut of strips if the last one has eased home) and holds a while.</summary>
        public void Glitch(PlayerHudLook.FxRow look, float now, float screenHeight)
        {
            if (glitch <= 0f) Cut(look, screenHeight);
            heldUntil = now + Mathf.Max(0f, look.hold);
        }

        /// <summary>Turns the effects on or off for this frame (the canvas, the camera, the image).</summary>
        public void Set(bool wanted)
        {
            wanted &= material != null;
            if (wanted) EnsureTarget();
            if (wanted == on) return;
            on = wanted;
            var canvas = hud.Canvas;
            if (on)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = view;
                canvas.planeDistance = PlaneDistance;
                // The camera sees one layer, the canvas's, and no world body is on UI.
                hud.gameObject.layer = view.gameObject.layer = LayerMask.NameToLayer("UI");
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                hud.ApplyOrder();
                ReleaseTarget();
            }
            if (view != null) view.enabled = on;
            image.enabled = on;
        }

        /// <summary>This frame's laying: the glitch's ease, the scanlines and the copies as the options say.</summary>
        public void Draw(PlayerHudLook.FxRow look, PlayerHudOptions options, float now, float dt, float screenWidth, float screenHeight)
        {
            // The ease runs whether or not the effects draw, so turning them on mid-glitch shows it where it stands.
            float goal = now < heldUntil ? 1f : 0f;
            float seconds = goal > glitch ? look.rise : look.fall;
            glitch = seconds <= 0f ? goal : Mathf.MoveTowards(glitch, goal, dt / seconds);
            if (!on) return;
            material.SetFloat(AlphaId, look.alpha);
            material.SetFloat(ScanId, options.scanline ? Mathf.Clamp01(look.scanline / 255f) : 1f);
            float eased = glitch * glitch * (3f - 2f * glitch);
            image.Show(target, screenWidth, screenHeight, options.aberration, look.fringe, strips, stripCount, layout, eased, look.tint);
        }

        public void Destroy()
        {
            if (view != null) Object.Destroy(view.gameObject);
            ReleaseTarget();
            if (material != null) Object.Destroy(material);
        }

        // One cut of the screen into strips, as Nene.GenerateGlitch made each of its twelve (LuaJIT's math.random rules):
        // the strips start 10 px above the screen, each 1/75 .. 1/25 of its height tall (cut to whole px), moved by a draw
        // between two offsets of up to ±strength, its copies 2..8 px apart.
        private void Cut(PlayerHudLook.FxRow look, float screenHeight)
        {
            layout++;
            stripCount = 0;
            double strength = Mathf.Max(0f, look.strength);
            double offset = LuaRandom.Next(-strength, strength), spread = LuaRandom.Next(-strength, strength);
            double fine = screenHeight / Mathf.Max(1f, look.cutFine), coarse = screenHeight / Mathf.Max(1f, look.cutCoarse);
            float top = look.top;
            while (top < screenHeight && stripCount < MostStrips)
            {
                float tall = Mathf.Max(1f, (float)System.Math.Floor(LuaRandom.Next(fine, coarse)));
                strips[stripCount++] = new PlayerHudFxImage.Strip
                {
                    Aberration = (float)LuaRandom.Next(look.abMin, look.abMax),
                    Height = tall,
                    X = (float)LuaRandom.Next(offset, offset + spread),
                    Y = Mathf.Floor(top),
                };
                top += tall;
            }
        }

        private void EnsureTarget()
        {
            int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            if (view == null) MakeCamera();
            if (target != null && target.width == width && target.height == height) return;
            ReleaseTarget();
            // sRGB, as the screen is: the canvas blends in the same space it does over the screen.
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "PlayerHud FX (Runtime)",
                antiAliasing = 1,
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            target.Create();
            view.targetTexture = target;
        }

        private void ReleaseTarget()
        {
            if (target == null) return;
            if (view != null) view.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            target = null;
        }

        private void MakeCamera()
        {
            var host = new GameObject("PlayerHud FX Camera");
            RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Effects, "Screen", hud.gameObject);
            host.transform.SetPositionAndRotation(Home, Quaternion.identity);
            view = host.AddComponent<Camera>();
            view.enabled = false;
            view.cullingMask = 1 << LayerMask.NameToLayer("UI");
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.clear;
            view.nearClipPlane = .05f;
            view.farClipPlane = 100f;
            // Before any camera that draws the screen, so the picture is this frame's when the overlay lays it.
            view.depth = -90f;
            view.allowHDR = false;
            view.allowMSAA = false;
            view.useOcclusionCulling = false;
            var data = view.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            // Without the game renderer's SSAO, which had nothing here to darken.
            PlainRenderer.Use(view);
        }
    }
}
