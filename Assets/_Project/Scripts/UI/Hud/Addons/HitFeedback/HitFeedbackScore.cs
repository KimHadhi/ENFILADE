using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Black Ops VI Hitmarker's score popup (AddScore and BlackOps6Hitmark): right of and above the crosshair, a row per
    /// achievement, its name swinging in from the left (overshooting to sin ¾π) and its "+N" starting large, each held
    /// 3 s and faded; new rows go at the bottom (table.insert) and the rows below one that leaves slide up. Rows from the
    /// third down are dimmer, and from the fifth they are not seen. Once two rows stand together the yellow total pops in
    /// above them and counts up to their sum; it fades when the last row has gone, and the sum starts again.
    /// <para>Fixed from the original: it removed a finished row in the middle of its loop over the rows, so the row
    /// after it skipped that frame (not moved, not drawn: a one-frame flicker each time a row left); here finished rows
    /// leave after the frame's rows are drawn. The total's x (total_x) was set only inside that loop, so a frame drew
    /// it at x + 0 before any row had; here it is always 87·screen_scale. surface.GetTextSize('+'..exp) was measured
    /// and never used: dropped.</para>
    /// </summary>
    internal sealed class HitFeedbackScore
    {
        private sealed class Row
        {
            public string name, plus;
            public float life, a, x, y, size;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly Stack<Row> spare = new Stack<Row>();
        // exp_alpha, exp_size, total, total_fade; the total's text is made again only when its whole number changes.
        private float totalAlpha, totalPop = float.NaN, total, totalShown;
        private int totalWritten = int.MinValue;
        private string totalText = "";

        /// <summary>AddScore(name, exp): a row at the bottom, and its points into the popup's sum.</summary>
        public void Add(string name, int points, HitFeedbackScoreLook look)
        {
            var row = spare.Count > 0 ? spare.Pop() : new Row();
            row.name = name ?? "";
            row.plus = "+" + points.ToString(CultureInfo.InvariantCulture);
            row.life = Smooth.Now + look.seconds;
            row.a = row.x = row.y = 0f;
            row.size = look.pop;
            rows.Add(row);
            total += points;
        }

        /// <summary>One frame of BlackOps6Hitmark, in its order.</summary>
        public void Draw(Painter painter, Overlay overlay, HitFeedbackScoreLook look, HitFeedbackOptions o)
        {
            float now = Smooth.Now;
            float screen = overlay.ScreenScale(look.screenScale);
            float x = overlay.Width * .5f * look.anchorX + o.horizontal, y = overlay.Height * .5f * look.anchorY + o.vertical;
            float sepW = look.columnGap * screen, sepH = look.rowStep * screen, lift = look.lift * screen;
            float textScale = screen * o.textSize;
            Color32 outline = look.outlineColour, white = look.rowColour;
            if (float.IsNaN(totalPop)) totalPop = look.pop;

            // The total shows once two rows stand together and stays while any row does.
            bool showing = totalAlpha != 1f ? rows.Count > 1 : rows.Count > 0;
            totalAlpha = showing ? Smooth.Approach(totalAlpha, 1f, look.totalInRate) : Smooth.Approach(totalAlpha, 0f, look.totalOutRate);
            if (rows.Count == 0) total = 0f;
            if (totalAlpha == 0f) totalShown = total;
            if (showing) totalPop = Smooth.Approach(totalPop, 0f, look.pop * look.totalPopRate);
            else if (totalAlpha == 0f) totalPop = look.pop;
            totalShown = Smooth.Lerp(look.countRate, totalShown, total);

            if (o.score && Gm.Byte(255f * totalAlpha) > 0)
            {
                var font = HudFont.Get(look.font, look.totalSize * textScale + Mathf.RoundToInt(totalPop), look.totalWeight);
                painter.OutlinedText(TotalText(totalShown), font, x + look.totalX * screen, y - sepH,
                    Gm.WithAlpha(look.totalColour, 255f * totalAlpha), TextAlign.Right, TextAlign.Bottom, look.outline,
                    Gm.WithAlpha(outline, look.outlineAlpha * totalAlpha));
            }

            float turnTo = look.nameTurn * Mathf.PI;
            var nameFont = HudFont.Get(look.font, look.nameSize * textScale, look.nameWeight);
            int finished = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                // The addon's i is 1-based: a row's place is i - 1 = this index.
                if (row.y > i) row.y = Smooth.Lerp(look.slideRate, row.y, i);
                else row.y = i;
                if (now > row.life) row.a = Smooth.Approach(row.a, 0f, look.outRate);
                else
                {
                    row.a = Smooth.Approach(row.a, 1f, look.inRate);
                    row.x = Smooth.Approach(row.x, turnTo, look.nameTurnRate);
                }
                row.size = Smooth.Approach(row.size, 0f, look.pop * look.rowPopRate);
                if (row.a == 0f && now > row.life) finished++;

                float dim = Mathf.Max((i + 1 - look.dimFrom) * look.dimStep, 0f);
                float alpha = 255f * row.a - dim;
                if (Gm.Byte(alpha) == 0) continue;
                float lineY = y + sepH * row.y - lift;
                var outlineColour = Gm.WithAlpha(outline, look.outlineAlpha * row.a - dim);
                if (o.achievements)
                    painter.OutlinedText(row.name, nameFont, x + look.nameSwing * Mathf.Sin(row.x) * screen + sepW, lineY,
                        Gm.WithAlpha(white, alpha), TextAlign.Left, TextAlign.Bottom, look.outline, outlineColour);
                if (o.score)
                    painter.OutlinedText(row.plus, HudFont.Get(look.font, look.plusSize * textScale + Mathf.RoundToInt(row.size), look.plusWeight),
                        x + sepW + look.plusX * screen, lineY, Gm.WithAlpha(white, alpha), TextAlign.Right, TextAlign.Bottom,
                        look.outline, outlineColour);
            }
            if (finished > 0) RemoveFinished(now);
        }

        // The rows done fading leave together, keeping the others' order.
        private void RemoveFinished(float now)
        {
            int kept = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.a == 0f && now > row.life) { spare.Push(row); continue; }
                rows[kept++] = row;
            }
            rows.RemoveRange(kept, rows.Count - kept);
        }

        // string.format("%.0f", total_fade), made again only when the number changes.
        private string TotalText(float value)
        {
            // "%.0f": to the nearest whole number, halves to even (the C library's rounding, as Mathf.RoundToInt does).
            int whole = Mathf.RoundToInt(value);
            if (whole != totalWritten)
            {
                totalWritten = whole;
                totalText = whole.ToString(CultureInfo.InvariantCulture);
            }
            return totalText;
        }
    }
}
