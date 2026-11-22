using UnityEngine;
using UnityEngine.UI;

namespace HudAddons
{
    /// <summary>
    /// A screen-space canvas for one HUD addon module, scaled like the rest of the combat HUD (FeelUI.SetupOverlay: 1920x1080
    /// reference, width and height weighed equally), with a root whose coordinates are Garry's Mod's screen: origin at the
    /// top-left, x right, y down, in canvas units. On a 16:9 screen that is exactly 1920x1080, so a ported addon's ScrW() /
    /// ScrH() maths and its pixel numbers (read as 1080p pixels) carry over as written: ScrW() is <see cref="Width"/>.
    /// Layers in it (painters, text pools) stack in the order they are added.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Overlay : MonoBehaviour
    {
        private const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;

        /// <summary>The screen root: stretched over the canvas, pivot at its top-left.</summary>
        public RectTransform Root { get; private set; }
        public Canvas Canvas { get; private set; }

        /// <summary>
        /// ScrW(): the canvas's width in units. Worked out from the screen as the canvas scaler does (scale = √(sw/1920 ·
        /// sh/1080) for an even match), so it is right from the first frame, before the canvas has laid itself out.
        /// </summary>
        public float Width => Screen.width / Scale;

        /// <summary>ScrH(): the canvas's height in units.</summary>
        public float Height => Screen.height / Scale;

        /// <summary>The overlay shows (Canvas.enabled); hiding keeps every piece for when it shows again.</summary>
        public bool Visible
        {
            get => Canvas.enabled;
            set => Canvas.enabled = value;
        }

        private static float Scale => Mathf.Max(1e-4f, Mathf.Sqrt(Screen.width / ReferenceWidth * (Screen.height / ReferenceHeight)));

        /// <summary>ScreenScale(n): n × ScrW() / 640.</summary>
        public float ScreenScale(float size) => size * Width / 640f;

        /// <summary>ScreenScaleH(n): n × ScrH() / 480.</summary>
        public float ScreenScaleH(float size) => size * Height / 480f;

        /// <summary>A new overlay drawn at <paramref name="sortOrder"/> among the screen canvases (the HUD's pieces use 24-95).</summary>
        public static Overlay Create(string name, int sortOrder)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            FeelUI.SetupOverlay(host, sortOrder);
            var overlay = host.AddComponent<Overlay>();
            overlay.Canvas = host.GetComponent<Canvas>();
            // TextMeshPro's text and its outline stamps carry a second UV set; ask for it up front rather than on the
            // first text's mesh.
            overlay.Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            overlay.Root = Stretched("Screen", host.transform);
            return overlay;
        }

        /// <summary>A painter on a new layer above the ones added before it.</summary>
        public Painter AddPainter(string name) => new Painter(AddLayer(name));

        /// <summary>A text pool on a new layer above the ones added before it.</summary>
        public TextPool AddTextPool(string name) => new TextPool(AddLayer(name));

        /// <summary>An empty layer over the whole screen root (origin top-left), for pieces a module places itself.</summary>
        public RectTransform AddLayer(string name) => Stretched(name, Root);

        private static RectTransform Stretched(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
