using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The constants of cl_bo6healthbar.lua (the Black Ops 6 Zombies Healthbars addon), one row of
    /// Tables/Hud/EnemyBars.csv each (Row.field), bound by EnemyBarsHud with GameTables.BindNested. The initialisers are
    /// the addon's own numbers, so a missing row draws the addon as written. Lengths are what the Lua multiplies ScrW() or
    /// ScrH() by (each field says which), or screen units where the Lua writes pixels; times are seconds; alphas 0-255.
    /// </summary>
    internal static class EnemyBarsLook
    {
        /// <summary>Tables/Hud/EnemyBars.csv.</summary>
        public const string Table = "EnemyBars";

        // The rows. Named apart from the hand-made copy's old Bar/Name/Icon/Hit/Aim rows (removed with it, 2026-11-22), whose
        // fields of those names meant other things (pixels where these are shares), so an old mod table cannot steer these.
        public const string BarRow = "HealthBar", NameRow = "NameTag", IconRow = "HeadIcon", TotalRow = "Total",
            PillarRow = "Pillar", AimRow = "Target";

        /// <summary>The bar itself (TBL.HUD and the HUDPaint drawing).</summary>
        [Serializable]
        public sealed class Bar
        {
            /// <summary>BarW = W * 0.07395833 (of ScrW).</summary>
            public float width = .07395833f;
            /// <summary>BarH = H * 0.00833 (of ScrH).</summary>
            public float height = .00833f;
            /// <summary>barpos_y = pos2d_y - bar_h * 0.5 - H * 0.02 (of ScrH): the bar's middle over the hull's top.</summary>
            public float raise = .02f;
            /// <summary>ColorBG (69, 73, 76).</summary>
            public Color background = new Color32(69, 73, 76, 255);
            /// <summary>froze_bo6hbar_alt_bg_texture's ground: MatAltBG, bo6healthbar/bg_alt_1.png.</summary>
            public string pattern = "EnemyBars/bg_alt_1";
            /// <summary>The follower (ColorWhite): what a hit took, held and then sliding down to the health.</summary>
            public Color follower = Color.white;
            /// <summary>The health: surface.SetDrawColor(221, 71, 59, 255) (TBL.HUD.ColorHP).</summary>
            public Color health = new Color32(221, 71, 59, 255);
            /// <summary>The 1-pixel line where the health ends, Color(0, 0, 0, 255).</summary>
            public Color edge = Color.black;
            public float edgeWidth = 1f;
            /// <summary>surface.DrawOutlinedRect(..., 1) in black.</summary>
            public Color outline = Color.black;
            public float outlineWidth = 1f;
            /// <summary>timer.Simple(1, ...): how long the follower holds after the hit that started a hold.</summary>
            public float hold = 1f;
            /// <summary>Lerp(RFT / 0.11, follower_w, hp_w): the follower's time to close on the health.</summary>
            public float followTime = .11f;
            /// <summary>hp_w * 1.03 > follower_w: the follower lands on the health.</summary>
            public float snap = 1.03f;
            /// <summary>lifetime_after_removed >= 0.4: a dead or removed enemy's bar stays this long where it was.</summary>
            public float linger = .4f;
        }

        /// <summary>The name under the bar (fonts BO6HBAR_font_name and BO6HBAR_font_name_blur).</summary>
        [Serializable]
        public sealed class Name
        {
            /// <summary>font = "Hitmarker Text Medium" (Tables/Hud/AddonFonts.csv).</summary>
            public string face = "Hitmarker Text Medium";
            public int weight = 500;
            /// <summary>size = H * 0.014 (of ScrH; cut to a whole size as GMod's font size is).</summary>
            public float size = .014f;
            /// <summary>draw.DrawText(..., barpos_y + bar_h + 2, ...): screen units under the bar's foot.</summary>
            public float gap = 2f;
            /// <summary>color_white.</summary>
            public Color color = Color.white;
            /// <summary>shadow = true: Source's drop shadow, one pixel right and down, black, under the glyphs.</summary>
            public float shadow = 1f;
            public Color shadowColor = Color.black;
            /// <summary>The blurred font's size = H * 0.015 (of ScrH).</summary>
            public float blurSize = .015f;
            /// <summary>blursize = 2: Source blurs each glyph with a (2 * 2 + 1)² Gaussian kernel.</summary>
            public int blur = 2;
            /// <summary>for i = 1, 6: the blurred name is drawn six times over itself, in color_black.</summary>
            public int passes = 6;
            public Color blurColor = Color.black;
            /// <summary>Source's ApplyGaussianBlurToTexture: sigma = 0.683 * blursize, with its own rounded 3.14 and 2.7.</summary>
            public float sigma = .683f;
            public float pi = 3.14f;
            public float e = 2.7f;
        }

        /// <summary>The head left of the bar (MatIconBoss, MatIconNormal).</summary>
        [Serializable]
        public sealed class Icon
        {
            public string normal = "EnemyBars/icon_normal";
            public string boss = "EnemyBars/icon_boss";
            /// <summary>The normal icon: W * 0.0125 square (of ScrW, both sides).</summary>
            public float normalSize = .0125f;
            /// <summary>The boss icon: W * 0.0138 wide (of ScrW) ...</summary>
            public float bossWidth = .0138f;
            /// <summary>... and H * 0.0352 high (of ScrH).</summary>
            public float bossHeight = .0352f;
            /// <summary>W * 0.00078125 (of ScrW) between the icon's right and the bar's left.</summary>
            public float gap = .00078125f;
            /// <summary>barpos_y - H * 0.00463 (of ScrH): the icon's top over the bar's.</summary>
            public float raise = .00463f;
            /// <summary>surface.SetDrawColor(255, 255, 255, 255).</summary>
            public Color tint = Color.white;
        }

        /// <summary>The damage total right of the bar (BO6HBAR_font_dmg, froze_bo6hbar_enabled_dmg_numbers_cl).</summary>
        [Serializable]
        public sealed class Total
        {
            /// <summary>font = "Hitmarker VF Condensed Light": the addon's hitmarker-vf.ttf at its Condensed Light instance
            /// (wght 300, wdth 0), cut to a static face (Resources/UI/Fonts/hitmarker-vf-condensed-light.ttf).</summary>
            public string face = "Hitmarker VF Condensed Light";
            public int weight = 500;
            /// <summary>size = H * 0.016 (of ScrH).</summary>
            public float size = .016f;
            /// <summary>barpos_x + bar_w + 3: screen units right of the bar.</summary>
            public float gap = 3f;
            /// <summary>barpos_y - H * 0.016 * 0.5: raised by this share of the (uncut) size.</summary>
            public float raise = .5f;
            public Color color = Color.white;
            /// <summary>shadow = true, as the name's.</summary>
            public float shadow = 1f;
            public Color shadowColor = Color.black;
            /// <summary>timer.Simple(1, ... v.dmg_total = 0): the total clears this long after the hit that opened it.</summary>
            public float reset = 1f;
        }

        /// <summary>The damage pillar a drop in health raises (DamageBlocks).</summary>
        [Serializable]
        public sealed class Pillar
        {
            /// <summary>h = BarH * 0.5: the block's first height, of the bar's height.</summary>
            public float start = .5f;
            /// <summary>target_h = BarH + H * 0.0111: how far above the bar's height it rises (of ScrH).</summary>
            public float rise = .0111f;
            /// <summary>Lerp(RFT / 0.16, h, target_h).</summary>
            public float riseTime = .16f;
            /// <summary>Lerp(RFT / 0.08, h_line, target_h): the line at its left.</summary>
            public float lineTime = .08f;
            /// <summary>Past 0.95 of target_h it fades.</summary>
            public float fadeFrom = .95f;
            /// <summary>Lerp(RFT / 0.15, color.a, 0).</summary>
            public float fadeTime = .15f;
            /// <summary>Lerp(RFT / 0.1, color2.a, 0).</summary>
            public float lineFadeTime = .1f;
            /// <summary>math.max(0.01, a), removed at 0.01.</summary>
            public float gone = .01f;
            /// <summary>A body hit's block: Color(255, 255, 255, ...).</summary>
            public Color color = Color.white;
            /// <summary>A headshot's: Color(255, 255, 94, ...).</summary>
            public Color headColor = new Color32(255, 255, 94, 255);
            /// <summary>The block's and its line's alpha once the hit's word painted it: 100 and 140.</summary>
            public float alpha = 100f;
            public float lineAlpha = 140f;
            /// <summary>A drop no hit's word ever painted (health lost without a reported hit): 60 and 100, white.</summary>
            public float plainAlpha = 60f;
            public float plainLineAlpha = 100f;
            /// <summary>The solid chunk inside the bar: surface.SetDrawColor(r, g, b, 255).</summary>
            public float chunkAlpha = 255f;
            /// <summary>Two 1-pixel lines side by side, at the block's left (and its right while it has a front line).</summary>
            public float lineWidth = 2f;
            /// <summary>Without its front line the block is drawn this much wider (mod_w = 1).</summary>
            public float widen = 1f;
            /// <summary>barpos_y + bar_h - h - 1: the block and lines stand this far above the bar's foot.</summary>
            public float foot = 1f;
            /// <summary>The chunk: barpos_y + 1, bar_h - 2.</summary>
            public float inset = 1f;
        }

        /// <summary>Which bar shows (HUDPaint's trace and the hit's word).</summary>
        [Serializable]
        public sealed class Aim
        {
            /// <summary>TimeWithoutTarget >= 3: every bar hides this long after the crosshair was last on something in reach.</summary>
            public float forget = 3f;
            /// <summary>ply:GetEyeTrace()'s reach, 32768 units (util.GetPlayerTrace), in metres.</summary>
            public float traceLength = 32768f * EnemyBarsOptions.MetresPerUnit;
            /// <summary>bo6_pause_trace: after a kill its dealer's aim and hits move no bar for this long.</summary>
            public float killPause = .4f;
            /// <summary>GET_DAMAGED: after a hit near its dealer, anything solid under the crosshair counts, for this long.</summary>
            public float damagedWindow = .1f;
        }
    }
}
