using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HudAddons
{
    // Our class UI drawn with the addon's own parts on its painter, so it sways with the HUD and wears its scanlines and
    // colour fringes (the user, 2026-11-22, the class UI draft's 7th round: "B" rebuilt from the addon's parts, white,
    // the player's own colour only as a point, the addon's noise). What it shows comes from ClassReadout (the old class
    // plate's rules), the deployables' counts (Deployables.Readout) and the timed effects (TimedEffects.Running).
    // - The class box: the grenade box's 41.25 box with the class icon, a count at its corner, a recharging ability
    //   filling it from its foot.
    // - The class name in white, spaced like the weapon's name, one short bar of the player's colour before it.
    // - Pips: the armour bar's segmented bar, white, the grey words under it where the armour's 0 · 50 · 100 stand (the
    //   note left, "5/8" right). The Point's two guns: two small magazine boxes with their reload lines.
    // - The key: a box like the reserve box with a key cap in the player's colour and a bold white word; it glows softly
    //   while it may be acted on; red where it can't. DEADEYE / DESPERADO in the player's colour, beating.
    // - Deployables and timed effects: rows like the pickup history's (a faint box, a word, a number).
    // Two places (an option): under the screen's middle, the rows hanging under it; or beside the portrait over the
    // health bar, the rows stacking up from it. Where the mark and the key would meet on a narrow row, the mark takes a
    // row of its own above (beside the portrait DESPERADO met the duel's key: the user, 11-22).
    public sealed partial class PlayerHud
    {
        private readonly ClassReadout.View classView = new ClassReadout.View();
        private readonly List<Deployables.Line> deployLines = new List<Deployables.Line>();
        private readonly Dictionary<int, string> fractions = new Dictionary<int, string>();
        private readonly Dictionary<string, (float value, int stacks, string words)> effectWords = new Dictionary<string, (float, int, string)>();
        private static readonly string[] tenths = new string[1000];
        private PlayerTally tally;
        // The portrait panel this frame (DrawPortrait): its right edge, its top, the rank row's foot, the health bar's right end.
        private float panelRight, panelTop, panelRowFoot, healthRight;

        /// <summary>The colour the class UI and the magazine point with: the player's own, unless the options give one.</summary>
        private Color32 Accent(PlayerHudOptions o)
        {
            if (!o.classOwnColour) return o.classColor;
            if (tally == null && player != null) player.TryGetComponent(out tally);
            return tally != null ? (Color32)tally.Color : new Color32(255, 79, 120, 255);
        }

        private void DrawClass(PlayerHudOptions o)
        {
            if (!o.classUi) return;
            ClassReadout.Read(player, classView);
            if (!classView.Shown) return;
            var c = look.Class;
            var v = classView;
            float u = scale / Mathf.Max(.01f, hud.ScreenScale(look.Hud.scale)) * o.classScale;
            Color32 accent = Accent(o), white = White, grey = ammoGrey, outlineColour = c.outline;
            int bold = 1000, regular = (int)(look.Font.weightUnit * o.weight);
            var nameFont = HudFont.Get(o.font, c.nameText * u, bold);
            var smallFont = HudFont.Get(o.font, c.smallText * u, regular);
            var smallBold = HudFont.Get(o.font, c.smallText * u, bold);
            var wordFont = HudFont.Get(o.font, c.wordText * u, bold);
            float outlineW = outlineWidth;

            // Where it stands: the class box's top left, the bar's length.
            bool portrait = o.classPlace == ClassUiPlace.Portrait;
            float box = c.box * u, gap = c.gap * u, frame = c.frame * u, barH = c.barHeight * u;
            float x0, rowTop, barW;
            if (portrait)
            {
                // On the rank row's foot, so a larger class UI grows up and never onto the health bar.
                x0 = panelRight + c.portraitGap * u;
                rowTop = panelRowFoot - box;
                barW = Mathf.Max(40f * u, healthRight - frame - (x0 + box + gap));
            }
            else
            {
                x0 = sw - c.centreLeft * u + swayX;
                rowTop = screenHeight - c.centreUp * u - swayY;
                barW = c.barWidth * u;
            }
            x0 += o.classX;
            rowTop += o.classY;
            float barX = x0 + box + gap, barMid = rowTop + box * .55f, right = barX + barW + frame;
            float blockW = right - x0;

            // The class box.
            painter.RoundedBox(look.Hud.corner, x0, rowTop, box, box, background);
            if (v.Rise > 0f) Fill(x0, rowTop + box * (1f - v.Rise), box, box * v.Rise, Gm.WithAlpha(white, 255f * c.riseAlpha));
            if (v.Icon != null)
            {
                painter.DrawColor = Gm.WithAlpha(white, 230f);
                painter.Picture = v.Icon;
                painter.TexturedRect(x0 + 5f * u, rowTop + 5f * u, box - 10f * u, box - 10f * u);
            }
            if (v.Count >= 0)
                painter.OutlinedText(Digits.Of(v.Count), smallBold, x0 + box * .5f + box / 2.5f, rowTop + u, white, TextAlign.Right, TextAlign.Top, outlineW, outlineColour);

            // The name, a short bar of the player's colour before it.
            float nameFoot = barMid - barH * .5f - frame - 3f * u;
            Fill(barX, nameFoot - 11f * u, 3f * u, 11f * u, accent);
            if (v.Title != spacedTitleOf)
            {
                spacedTitleOf = v.Title;
                spacedTitle = Spaced(v.Title);
            }
            painter.OutlinedText(spacedTitle, nameFont, barX + 8f * u, nameFoot, white, TextAlign.Left, TextAlign.Bottom, outlineW, outlineColour);

            // The pips as the armour bar, or the Point's guns, or the words alone.
            float underY = barMid + barH * .5f + c.under * u * .5f + u;
            if (v.Most > 0)
            {
                painter.RoundedBox(look.Hud.corner, barX - frame, barMid - barH * .5f - frame, barW + frame * 2f, barH + c.under * u + frame * 2f, background);
                Pips(v, barX, barMid - barH * .5f, barW, barH, c, white, u);
                string count = Fraction(Mathf.FloorToInt(v.Filled + .001f), v.Most);
                float sideRoom = barW - (smallFont.Width(count) + 8f * u);
                if (v.Side.Length > 0) painter.OutlinedText(v.Side, smallFont, barX, underY, grey, TextAlign.Left, TextAlign.Center, outlineW, outlineColour);
                // A note too long for the bar keeps the room; the count goes (beside the portrait the bar is short).
                if (v.Side.Length == 0 || smallFont.Width(v.Side) <= sideRoom)
                    painter.OutlinedText(count, smallFont, barX + barW, underY, grey, TextAlign.Right, TextAlign.Center, outlineW, outlineColour);
            }
            else if (v.Guns)
            {
                var digits = HudFont.Get(o.font, c.digitText * u, bold);
                float gw = (barW - 8f * u) * .5f, gh = 28f * u, gy = barMid - gh * .5f;
                for (int g = 0; g < 2; g++)
                {
                    float gx = barX + g * (gw + 8f * u);
                    painter.RoundedBox(look.Hud.corner, gx, gy, gw, gh, background);
                    if (v.GunFlash[g] > 0f) painter.RoundedBox(look.Hud.corner, gx, gy, gw, gh, Gm.WithAlpha(accent, 115f * v.GunFlash[g]));
                    painter.OutlinedText(Digits.Of(g + 1), digits, gx + 8f * u, gy + gh * .5f, white, TextAlign.Left, TextAlign.Center, outlineW, outlineColour);
                    Fill(gx + 3f * u, gy + gh - 4f * u, (gw - 6f * u) * Mathf.Clamp01(v.Gun[g]), 2.5f * u, white);
                }
                if (v.Side.Length > 0) painter.OutlinedText(v.Side, smallFont, barX, gy + gh + 9f * u, grey, TextAlign.Left, TextAlign.Center, outlineW, outlineColour);
            }
            else if (v.Side.Length > 0)
                painter.OutlinedText(v.Side, smallFont, barX, barMid, grey, TextAlign.Left, TextAlign.Center, outlineW, outlineColour);

            // The key row over the class row: the key at the right, the mark at the left; the mark takes its own row above
            // when the two would meet.
            float keyTop = rowTop - c.keyAbove * u, keyH = c.keyHeight * u, rowsTop = keyTop;
            float keyLeft = right;
            if (v.Key)
            {
                float keyW = 40f * u + wordFont.Width(v.Word) + 10f * u;
                keyLeft = right - keyW;
                painter.RoundedBox(look.Hud.corner, keyLeft, keyTop, keyW, keyH, background);
                float beat = .5f + .5f * Mathf.Sin(now * Mathf.PI * 2f * c.beatsPerSecond);
                if (v.Beat) painter.RoundedBox(look.Hud.corner, keyLeft, keyTop, keyW, keyH, Gm.WithAlpha(accent, 255f * Mathf.Lerp(c.glowLeast, c.glowMost, beat)));
                Color32 cap = v.Deny ? low : accent;
                painter.RoundedBox(4f * u, keyLeft + 5f * u, keyTop + 3f * u, 26f * u, keyH - 6f * u, cap);
                painter.Text(KeyName(), smallBold, keyLeft + 18f * u, keyTop + keyH * .5f, new Color32(12, 12, 15, 255), TextAlign.Center, TextAlign.Center);
                painter.OutlinedText(v.Word, wordFont, keyLeft + 38f * u, keyTop + keyH * .5f, v.Deny ? low : white, TextAlign.Left, TextAlign.Center, outlineW, outlineColour);
            }
            if (v.Mark != null)
            {
                var markFont = HudFont.Get(o.font, c.markText * u * (portrait ? .875f : 1f), bold);
                float markW = markFont.Width(v.Mark), markY = keyTop + keyH * .5f;
                if (v.Key && barX + markW + 8f * u > keyLeft)
                {
                    markY -= keyH + c.rowGap * u;
                    rowsTop = keyTop - keyH - c.rowGap * u;
                }
                float beat = .8f + .2f * Mathf.Sin(now * Mathf.PI * 2f * c.beatsPerSecond);
                painter.AlphaScale = beat;
                painter.OutlinedText(v.Mark, markFont, barX, markY, accent, TextAlign.Left, TextAlign.Center, outlineW, outlineColour);
                painter.AlphaScale = 1f;
            }

            // Deployables and timed effects: under the middle they hang below the class row; beside the portrait they
            // stack up above the key row.
            Deployables.Readout(player, deployLines);
            float rowH = c.rowHeight * u, rowGap = c.rowGap * u;
            if (portrait)
            {
                float y = rowsTop - rowGap;
                y = DeployRows(x0, y, blockW, 2, true, rowH, rowGap, accent, u, smallFont, smallBold, outlineColour);
                EffectRows(x0, y, blockW, 1, true, rowH, rowGap, u, smallFont, smallBold, outlineColour);
            }
            else
            {
                float y = rowTop + box + 14f * u;
                y = DeployRows(x0, y, blockW, 4, false, rowH, rowGap, accent, u, smallFont, smallBold, outlineColour);
                EffectRows(x0, y, blockW, 2, false, rowH, rowGap, u, smallFont, smallBold, outlineColour);
            }
        }

        private string spacedTitleOf, spacedTitle = string.Empty;

        // The armour bar's segments: faint where empty, white as full as the pips are, a pip that just changed lit white.
        private void Pips(ClassReadout.View v, float x, float y, float w, float h, PlayerHudLook.ClassRow c, Color32 white, float u)
        {
            float room = w / v.Most, seg = room * (1f - c.pipGap), end = x + w * Mathf.Clamp01(v.Filled / v.Most);
            Color32 empty = Gm.WithAlpha(white, 255f * c.emptyAlpha);
            for (int i = 0; i < v.Most; i++)
            {
                float sx = x + room * i;
                Fill(sx, y, seg, h, empty);
                if (sx < end) Fill(sx, y, Mathf.Min(seg, end - sx), h, white);
                bool lit = v.Flash > 0f && (v.FlashAll || v.FlashAt == i);
                if (!lit) continue;
                // A gained pip swells as it lights; a lost one only lights.
                float grow = (v.Gained ? 3f : 1.5f) * u * v.Flash;
                Fill(sx - grow, y - grow, seg + grow * 2f, h + grow * 2f, Gm.WithAlpha(white, 230f * v.Flash));
            }
        }

        // One row of chips per line of deployables, the kind being placed underlined in the player's colour, a full kind's
        // count in the addon's orange (breathing while it is the one placed). Returns the y the next rows start from.
        private float DeployRows(float x, float y, float width, int perRow, bool upward, float rowH, float rowGap, Color32 accent, float u,
            HudFont font, HudFont bold, Color32 outlineColour)
        {
            int count = deployLines.Count;
            if (count == 0) return y;
            int columns = Mathf.Min(perRow, count), rows = (count + columns - 1) / columns;
            float chipW = Mathf.Min(look.Class.chipMost * u, (width - rowGap * (columns - 1)) / columns);
            float top = upward ? y - rows * rowH - (rows - 1) * rowGap : y;
            float breath = .775f + .225f * Mathf.Sin(now * Mathf.PI * 2f * 1.2f);
            string placing = classView.Placing;
            for (int i = 0; i < count; i++)
            {
                var line = deployLines[i];
                float cx = x + (i % columns) * (chipW + rowGap), cy = top + (i / columns) * (rowH + rowGap);
                bool picked = placing.Length > 0 && string.Equals(placing, line.Kind.Id, System.StringComparison.OrdinalIgnoreCase);
                float held = line.Held ? 1f : look.Class.unheldAlpha;
                painter.AlphaScale = held;
                painter.RoundedBox(look.Hud.corner, cx, cy, chipW, rowH, background);
                if (picked) Fill(cx + 3f * u, cy + rowH - 3f * u, chipW - 6f * u, 2f * u, accent);
                painter.OutlinedText(line.Kind.Short, font, cx + 6f * u, cy + rowH * .5f, White, TextAlign.Left, TextAlign.Center, outlineWidth, outlineColour);
                Color32 tint = line.Full ? Gm.WithAlpha(loss, 255f * (picked ? breath : 1f)) : White;
                painter.OutlinedText(Fraction(line.Count, line.Most), bold, cx + chipW - 6f * u, cy + rowH * .5f, tint, TextAlign.Right, TextAlign.Center, outlineWidth, outlineColour);
                painter.AlphaScale = 1f;
            }
            return upward ? top - rowGap : top + rows * rowH + (rows - 1) * rowGap + rowGap;
        }

        // A row per running effect, in the order they began: its colour's dot, its name, its seconds, a thin bar of what is
        // left. Under the middle as many rows as fit above the screen's foot.
        private void EffectRows(float x, float y, float width, int perRow, bool upward, float rowH, float rowGap, float u,
            HudFont font, HudFont bold, Color32 outlineColour)
        {
            var running = TimedEffects.Running;
            int shown = 0;
            for (int i = 0; i < running.Count; i++) if (running[i].Kind.Shown) shown++;
            if (shown == 0) return;
            if (upward && shown > 3) perRow = 2;
            int columns = Mathf.Min(perRow, shown);
            float cellW = (width - rowGap * (columns - 1)) / columns, floor = screenHeight - 8f * u;
            int index = 0;
            for (int i = 0; i < running.Count; i++)
            {
                var entry = running[i];
                if (!entry.Kind.Shown) continue;
                int line = index / columns;
                float cx = x + (index % columns) * (cellW + rowGap);
                float cy = upward ? y - (line + 1) * rowH - line * rowGap : y + line * (rowH + rowGap);
                index++;
                if (!upward && cy + rowH > floor) break;
                Color32 colour = entry.Kind.Colour;
                painter.RoundedBox(look.Hud.corner, cx, cy, cellW, rowH, background);
                painter.RoundedBox(2f * u, cx + 6f * u, cy + rowH * .5f - 4f * u, 8f * u, 8f * u, colour);
                painter.OutlinedText(EffectName(entry), font, cx + 19f * u, cy + rowH * .5f, White, TextAlign.Left, TextAlign.Center, outlineWidth, outlineColour);
                painter.OutlinedText(Tenths(entry.Left), bold, cx + cellW - 6f * u, cy + rowH * .5f, White, TextAlign.Right, TextAlign.Center, outlineWidth, outlineColour);
                Fill(cx + 2f * u, cy + rowH - 3f * u, (cellW - 4f * u) * entry.Share, 2f * u, Gm.WithAlpha(White, 178f));
            }
        }

        // The words an effect shows, made again only when its number or stacks change.
        private string EffectName(in TimedEffects.Entry entry)
        {
            string id = entry.Kind.Id;
            if (effectWords.TryGetValue(id, out var kept) && kept.value == entry.Value && kept.stacks == entry.Stacks) return kept.words;
            string words = entry.Kind.Name(entry.Value, entry.Stacks);
            effectWords[id] = (entry.Value, entry.Stacks, words);
            return words;
        }

        private string Fraction(int count, int most)
        {
            int key = count * 1024 + most;
            if (!fractions.TryGetValue(key, out string text)) fractions[key] = text = count + "/" + most;
            return text;
        }

        // Seconds to one decimal, the string kept (up to 99.9).
        private static string Tenths(float seconds)
        {
            int t = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0f, seconds) * 10f), 0, tenths.Length - 1);
            return tenths[t] ??= (t / 10f).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private string keyNameRead, keyName = string.Empty;

        // The class key's name, read again only when it changes (KeyBindings).
        private string KeyName()
        {
            string name = ClassReadout.KeyName();
            if (name != keyNameRead)
            {
                keyNameRead = name;
                keyName = string.IsNullOrEmpty(name) ? "?" : name;
            }
            return keyName;
        }
    }
}
