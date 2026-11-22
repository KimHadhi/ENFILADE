using System.Globalization;
using System.Text;
using UnityEngine;

namespace HudAddons
{
    // The addon's shared drawing (utils/rect.lua, utils/text.lua, utils/lerp.lua, configs.lua's fonts and colours): the
    // boxes every element is made of, the bars' lag, the eased colours and numbers.
    public sealed partial class PlayerHud
    {
        // configs.lua's fonts (Nene.Label, Mags, Ammo, Alts, History, Selector), made again when what they hang on changes.
        private HudFont labelFont, magsFont, ammoFont, altsFont, historyFont, selectorFont;
        private float fontWidth = -1f, fontScale = -1f, fontWeight = -1f;
        private string fontFace;
        // Nene.Config.Colors this frame.
        private Color32 background, low, loss, ammoGrey, overOne, overTwo, outline;
        private float outlineWidth;

        private void MeasureFonts(PlayerHudOptions o)
        {
            string face = string.IsNullOrWhiteSpace(o.font) ? look.Font.face : o.font;
            if (screenWidth == fontWidth && o.scale == fontScale && o.weight == fontWeight && face == fontFace && labelFont != null) return;
            fontWidth = screenWidth;
            fontScale = o.scale;
            fontWeight = o.weight;
            fontFace = face;
            var f = look.Font;
            int weight = (int)(f.weightUnit * o.weight);
            labelFont = HudFont.Get(face, sw * f.label * o.scale, weight);
            magsFont = HudFont.Get(face, sw * f.mags * o.scale, weight);
            ammoFont = HudFont.Get(face, sw * f.ammo * o.scale, weight);
            altsFont = HudFont.Get(face, sw * f.alts * o.scale, weight);
            historyFont = HudFont.Get(face, sw * f.history * o.scale, weight);
            selectorFont = HudFont.Get(face, sw * f.selector * o.scale, weight);
        }

        private void MeasureColours()
        {
            var c = look.Colour;
            background = c.background;
            low = c.low;
            loss = c.loss;
            ammoGrey = c.ammo;
            overOne = c.overOne;
            overTwo = c.overTwo;
            outline = c.outline;
            outlineWidth = c.outlineWidth;
        }

        // ---- colours ----

        /// <summary>An option's colour as configs.lua builds it: Color(r, g, b), its alpha left to the element's opacity.</summary>
        private static Color32 Rgb(Color32 option) => new Color32(option.r, option.g, option.b, 255);

        /// <summary>The colour picker's alpha, which the addon never read (configs.lua builds Color(r, g, b)): the opacity
        /// of everything its element draws.</summary>
        private static float Opacity(Color32 option) => option.a / 255f;

        /// <summary>An empty count's colour (Color(r / 1.75, g / 1.75, b / 1.75)).</summary>
        private Color32 Dim(Color32 colour)
        {
            float d = Mathf.Max(1e-3f, look.Colour.dim);
            return Gm.Rgba(colour.r / d, colour.g / d, colour.b / d);
        }

        /// <summary>
        /// Nene.LerpColor: a colour easing toward its goal, each channel on its own, from clear black (its table starts
        /// empty), in 0-255 floats so a slow ease never stalls on whole steps.
        /// </summary>
        private sealed class Fade
        {
            private Vector4 value;

            public Color32 Step(Color32 goal, float rate)
            {
                float f = Smooth.Factor(rate);
                value += (new Vector4(goal.r, goal.g, goal.b, goal.a) - value) * f;
                return Gm.Rgba(value.x, value.y, value.z, value.w);
            }
        }

        // ---- bars (rect.lua) ----

        /// <summary>
        /// One bar's state in BL_Rect / BSS_Rect: the fill easing to the value, and the orange loss under it, which keeps
        /// where the bar stood until the value has been still for a moment and then eases down after it. A gain lifts the
        /// loss to the fill at once, so only losses show. Both start at nothing, as the addon's tables do: the bar fills in.
        /// </summary>
        private sealed class Bar
        {
            public float Fill, Loss = float.NaN;
            private float last = float.NaN, holdUntil;

            public void Step(float value, float now, PlayerHudLook.BarRow row)
            {
                Fill = Smooth.Lerp(row.follow, Fill, value);
                if (value != last)
                {
                    holdUntil = now + row.lossHold;
                    if (float.IsNaN(Loss) || Loss < value) Loss = Fill;
                    last = value;
                }
                if (now >= holdUntil) Loss = Smooth.Lerp(row.lossFollow, Loss, value);
            }
        }

        /// <summary>rect.lua BL_Rect: a framed bar (the health bar), its loss under its fill, a value past full drawn
        /// again over it in the over-colours.</summary>
        private void LossBar(Bar bar, float value, float x, float y, float w, float h, float frame, Color32 colour)
        {
            bar.Step(value, now, look.Bar);
            float left = x - w * .5f, top = y - h * .5f;
            painter.RoundedBox(look.Hud.corner, left - frame, top - frame, w + frame * 2f, h + frame * 2f, background);
            Fill(left, top, w * Mathf.Clamp01(bar.Loss), h, loss);
            Fill(left, top, w * Mathf.Clamp01(bar.Fill), h, colour);
            if (value > 1f) Fill(left, top, w * Mathf.Clamp01(bar.Fill - 1f), h, overOne);
            if (value > 2f) Fill(left, top, w * Mathf.Clamp01(bar.Fill - 2f), h, overTwo);
        }

        /// <summary>
        /// rect.lua BSS_Rect: the armour bar, its loss and fill shown only inside its segments (the addon's stencil masks,
        /// here each segment's share of the fill drawn as its own rectangle), with 0, 50 and 100 under it in the frame's
        /// lower part.
        /// </summary>
        private void SegmentBar(Bar bar, int parts, float value, float x, float y, float w, float h, float frame, Color32 colour)
        {
            float textHeight = labelFont.Size;
            bar.Step(value, now, look.Bar);
            float left = x - w * .5f, top = y - h * .5f;
            painter.RoundedBox(look.Hud.corner, left - frame, top - frame, w + frame * 2f, h + textHeight + frame * 2f, background);
            if (parts > 1)
            {
                Segments(left, top, w, h, parts, Mathf.Clamp01(bar.Loss), loss);
                Segments(left, top, w, h, parts, Mathf.Clamp01(bar.Fill), colour);
                if (value > 1f) Segments(left, top, w, h, parts, Mathf.Clamp01(bar.Fill - 1f), overOne);
                if (value > 2f) Segments(left, top, w, h, parts, Mathf.Clamp01(bar.Fill - 2f), overTwo);
            }
            else
            {
                Fill(left, top, w * Mathf.Clamp01(bar.Loss), h, loss);
                Fill(left, top, w * Mathf.Clamp01(bar.Fill), h, colour);
            }
            float under = y + textHeight;
            painter.OutlinedText("0", labelFont, left, under, ammoGrey, TextAlign.Left, TextAlign.Center, outlineWidth, outline);
            painter.OutlinedText("50", labelFont, x, under, ammoGrey, TextAlign.Center, TextAlign.Center, outlineWidth, outline);
            painter.OutlinedText("100", labelFont, x + w * .5f, under, ammoGrey, TextAlign.Right, TextAlign.Center, outlineWidth, outline);
        }

        // Segment i takes w / parts from its start and fills the first (1 - gap) of it.
        private void Segments(float left, float top, float w, float h, int parts, float share, Color32 colour)
        {
            if (share <= 0f) return;
            float end = left + w * share, room = w / parts, filled = room * (1f - look.Status.armorGap);
            for (int i = 0; i < parts; i++)
            {
                float start = left + room * i;
                if (start >= end) break;
                Fill(start, top, Mathf.Min(start + filled, end) - start, h, colour);
            }
        }

        private void Fill(float x, float y, float w, float h, Color32 colour)
        {
            if (w > 0f && h > 0f) painter.RoundedBox(0f, x, y, w, h, colour);
        }

        // ---- boxes with text (text.lua) ----

        /// <summary>BT_Text: a box, its picture square in the middle at alpha 150, its text at the aligned edge.</summary>
        private void IconBox(Texture icon, string text, HudFont font, float x, float y, float w, float h, Color32 colour,
            TextAlign alignX, TextAlign alignY)
        {
            painter.RoundedBox(look.Hud.corner, x - w * .5f, y - h * .5f, w, h, background);
            Picture(icon, colour, x - h * .5f, y - h * .5f, h);
            BoxText(text, font, x, y, w, h, colour, alignX, alignY);
        }

        /// <summary>BTL_Text: the magazine box, its picture square at the left, faint "000" under the count.</summary>
        private void MagBox(Texture icon, string text, HudFont font, float x, float y, float w, float h, Color32 colour,
            TextAlign alignX, TextAlign alignY)
        {
            painter.RoundedBox(look.Hud.corner, x - w * .5f, y - h * .5f, w, h, background);
            Picture(icon, colour, x - w * .5f, y - h * .5f, h);
            float tx = TextX(x, w, alignX), ty = TextY(y, h, alignY);
            painter.Text(look.Ammo.ghost, font, tx, ty, Gm.WithAlpha(colour, look.Ammo.ghostAlpha), alignX, alignY);
            painter.OutlinedText(text, font, tx, ty, colour, alignX, alignY, outlineWidth, outline);
        }

        /// <summary>BL_Text: a box and its text.</summary>
        private void TextBox(string text, HudFont font, float x, float y, float w, float h, Color32 colour, TextAlign alignX, TextAlign alignY)
        {
            painter.RoundedBox(look.Hud.corner, x - w * .5f, y - h * .5f, w, h, background);
            BoxText(text, font, x, y, w, h, colour, alignX, alignY);
        }

        private void Picture(Texture icon, Color32 colour, float x, float y, float size)
        {
            painter.DrawColor = Gm.WithAlpha(colour, look.Ammo.iconAlpha);
            painter.Picture = icon;
            painter.TexturedRect(x, y, size, size);
        }

        // The text sits 2/5 of the box's width from its middle toward the side it is aligned to, at its top or foot.
        private void BoxText(string text, HudFont font, float x, float y, float w, float h, Color32 colour, TextAlign alignX, TextAlign alignY)
            => painter.OutlinedText(text, font, TextX(x, w, alignX), TextY(y, h, alignY), colour, alignX, alignY, outlineWidth, outline);

        private static float TextX(float x, float w, TextAlign align)
            => align == TextAlign.Right ? x + w / 2.5f : align == TextAlign.Left ? x - w / 2.5f : x;

        private static float TextY(float y, float h, TextAlign align)
            => align == TextAlign.Top ? y - h * .5f : align == TextAlign.Bottom ? y + h * .5f : y;

        // ---- numbers and words ----

        /// <summary>A whole number's text, made once and kept (the counts change often; their texts need not be new each time).</summary>
        private static class Digits
        {
            private static readonly string[] made = new string[10000];

            public static string Of(int value)
            {
                if (value < 0 || value >= made.Length) return value.ToString(CultureInfo.InvariantCulture);
                return made[value] ??= value.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>string.format("%.0f", value): to the nearest whole, a half to the even one, as C's printf does.</summary>
            public static int Round(float value) => Mathf.RoundToInt(value);
        }

        /// <summary>
        /// text.lua InsertSpaceBetweenChars of a name in capitals (string.upper, which only knows a-z): "M 4 A 1". The
        /// addon split the name into bytes, which cut a Korean letter in pieces; here it is split between letters.
        /// </summary>
        private static string Spaced(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string upper = Upper(name);
            var letters = StringInfo.GetTextElementEnumerator(upper);
            var spaced = new StringBuilder(upper.Length * 2);
            while (letters.MoveNext())
            {
                if (spaced.Length > 0) spaced.Append(' ');
                spaced.Append(letters.GetTextElement());
            }
            return spaced.ToString();
        }

        /// <summary>string.upper: a-z to A-Z, everything else as it is.</summary>
        private static string Upper(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            bool any = false;
            foreach (char c in text)
                if (c >= 'a' && c <= 'z') { any = true; break; }
            if (!any) return text;
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] >= 'a' && chars[i] <= 'z') chars[i] = (char)(chars[i] - 32);
            return new string(chars);
        }
    }
}
