using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Text alignment numbered as Garry's Mod numbers it (TEXT_ALIGN_LEFT 0, CENTER 1, RIGHT 2, TOP 3, BOTTOM 4): one set of
    /// values for both axes, so a ported call keeps the original constant in either place. Left, Center and Right place x;
    /// Top, Center and Bottom place y; anything else on y is the top, as draw.SimpleText treats it.
    /// </summary>
    public enum TextAlign { Left = 0, Center = 1, Right = 2, Top = 3, Bottom = 4 }

    /// <summary>
    /// One corner of a polygon as surface.DrawPoly takes it ({x, y, u, v}): screen units from the top-left with y down, and
    /// u/v from the picture's top-left with v down, as the addons' numbers are written.
    /// </summary>
    public struct PolyVertex
    {
        public float x, y, u, v;

        public PolyVertex(float x, float y, float u = 0f, float v = 0f)
        {
            this.x = x; this.y = y; this.u = u; this.v = v;
        }
    }

    /// <summary>
    /// Garry's Mod's small helpers with their exact rules, for the HUD addon modules (the drawing itself is Painter's).
    /// Colours: the addons build Color(r, g, b, a) from 0-255 numbers that are often fractions (255 * alpha), and the surface
    /// library takes them as whole numbers, so they are clamped and cut, never rounded: 127.9 draws as 127.
    /// </summary>
    public static class Gm
    {
        /// <summary>The screen the addons' pixel numbers are read against: the HUD canvas's 1920x1080 reference
        /// (Overlay.Width/Height give the live size, which differs off 16:9).</summary>
        public const float ScrW = 1920f, ScrH = 1080f;

        /// <summary>Color(r, g, b, a) of 0-255 numbers, clamped and cut as the surface library takes them.</summary>
        public static Color32 Rgba(float r, float g, float b, float a = 255f) => new Color32(Byte(r), Byte(g), Byte(b), Byte(a));

        /// <summary>One 0-255 channel, clamped and cut.</summary>
        public static byte Byte(float value) => value <= 0f ? (byte)0 : value >= 255f ? (byte)255 : (byte)value;

        /// <summary>ColorAlpha(colour, a): the colour with a new 0-255 alpha.</summary>
        public static Color32 WithAlpha(Color32 colour, float alpha)
        {
            colour.a = Byte(alpha);
            return colour;
        }

        /// <summary>math.Approach: moves by the step's size towards the target and stops on it.</summary>
        public static float Approach(float current, float target, float step)
        {
            step = Mathf.Abs(step);
            if (current < target) return Mathf.Min(current + step, target);
            if (current > target) return Mathf.Max(current - step, target);
            return target;
        }

        /// <summary>math.Remap: the value's place between inMin and inMax carried to outMin..outMax, unclamped.</summary>
        public static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
            => outMin + (value - inMin) / (inMax - inMin) * (outMax - outMin);

        /// <summary>
        /// A rectangle given in a layer's screen units (top-left origin, y down) as the root canvas's local rectangle,
        /// which is what CanvasRenderer.EnableRectClipping takes (RectMask2D measures in the same space). Computed when a
        /// clip is set, so the canvas's scale of the moment is in it.
        /// </summary>
        internal static Rect CanvasRect(Transform layer, Canvas root, float x0, float y0, float x1, float y1)
        {
            var to = root.transform;
            var low = to.InverseTransformPoint(layer.TransformPoint(new Vector3(Mathf.Min(x0, x1), -Mathf.Max(y0, y1), 0f)));
            var high = to.InverseTransformPoint(layer.TransformPoint(new Vector3(Mathf.Max(x0, x1), -Mathf.Min(y0, y1), 0f)));
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        internal static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }
}
