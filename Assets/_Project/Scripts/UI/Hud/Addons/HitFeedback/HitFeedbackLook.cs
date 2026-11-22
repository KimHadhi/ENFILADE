using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The hit marker's numbers (Tables/Hud/HitFeedback.csv, row Marker): Black Ops VI Hitmarker's DrawHitMarker and its
    /// bo6_hit / bo6_kill receivers (lua/autorun/blackops6_hitmarker.lua), as written there. Lengths are in its
    /// screen_scale (ScreenScale(0.256)), rates are its Lerp(FrameTime() * rate) / math.Approach(FrameTime() * rate)
    /// factors per second, colours its Color(...). The initialisers are the same values, so the marker is the addon's
    /// even before the table is read.
    /// </summary>
    [Serializable]
    public sealed class HitFeedbackMarkerLook
    {
        /// <summary>screen_scale = ScreenScale(0.256).</summary>
        public float screenScale = .256f;
        /// <summary>An arm picture's side: (50 + left·40)·screen_scale·marksize, "left" easing from 1 to 0.</summary>
        public float baseSize = 50f, growSize = 40f;
        /// <summary>scale_rate: where the arms rest, 20 (aiming) or 40 (hip fire) ·screen_scale·marksize, eased at 10.</summary>
        public float aimSpread = 20f, hipSpread = 40f, spreadRate = 10f;
        /// <summary>scale: the arms pulled in by 20·screen_scale·marksize for 0.3 s (the 'bo6_hitmarker' timer), then let go at 10.</summary>
        public float pinch = 20f, pinchRate = 10f, holdSeconds = .3f;
        /// <summary>hitmarker.life (its alpha) and hitmarker.size ease to 0 at FrameTime()·10/2.</summary>
        public float fadeRate = 5f, shrinkRate = 5f;
        /// <summary>The shake: ScrW()·0.0025 wide, gone in 0.5 s (Approach by FrameTime()·10/5), each frame at
        /// shake·width·(random() − 0.8) on both axes.</summary>
        public float shakeScreen = .0025f, shakeRate = 2f, shakeBias = .8f;
        /// <summary>The addon draws a new random shake every frame, which at 240 fps is four times the shaking it had at
        /// GMod's usual 60: here a new one 60 times a second, whatever the frame rate.</summary>
        public float shakeSteps = 60f;
        /// <summary>The turn: math.random(-2.5, 2.5) on a hit, math.random(-5, 5) on a kill (degrees).</summary>
        public float hitTurn = 2.5f, killTurn = 5f;
        /// <summary>The prop / flame badge: size / 2.5 square at the marker's lower right.</summary>
        public float badgeShare = 2.5f;
        /// <summary>Color(255, 255, 255) on a hit, Color(255, 0, 0) on a kill; the badge is always white.</summary>
        public Color hitColour = Color.white, killColour = Color.red, badgeColour = Color.white;
    }

    /// <summary>
    /// The score popup's numbers (row Score): Black Ops VI Hitmarker's BlackOps6Hitmark, AddScore and BO6CreatFont.
    /// The points each row is worth are row Points (read by HitFeedbackModule when a kill comes, drawn or not).
    /// </summary>
    [Serializable]
    public sealed class HitFeedbackScoreLook
    {
        /// <summary>surface.CreateFont's face (AddonFonts.csv row; the addon ships helveticaneue-condensed.ttf).</summary>
        public string font = "HelveticaNeue Condensed";
        public float screenScale = .256f;
        /// <summary>x, y = ScrW()/2·1.1 + horizontal, ScrH()/2·0.9 + vertical.</summary>
        public float anchorX = 1.1f, anchorY = .9f;
        /// <summary>sep_w, sep_h = 25, 40 ·screen_scale; rows sit 5·screen_scale above their line.</summary>
        public float columnGap = 25f, rowStep = 40f, lift = 5f;
        /// <summary>The total's right edge at x + 87·screen_scale; a row's "+N" right edge at x + sep_w + 60·screen_scale.</summary>
        public float totalX = 87f, plusX = 60f;
        /// <summary>A row's name at x + 100·sin(θ)·screen_scale + sep_w, θ going 0 → ¾π at 10 a second.</summary>
        public float nameSwing = 100f, nameTurn = .75f, nameTurnRate = 10f;
        /// <summary>fade_life 3: a row holds 3 s; it fades in at 2 and out at 3 a second.</summary>
        public float seconds = 3f, inRate = 2f, outRate = 3f;
        /// <summary>The total fades in at 10 and out at 5 a second.</summary>
        public float totalInRate = 10f, totalOutRate = 5f;
        /// <summary>max_size 30: a text starts 30 larger and shrinks back, the total at 30·10, a row's "+N" at 30·5 a second.</summary>
        public float pop = 30f, totalPopRate = 10f, rowPopRate = 5f;
        /// <summary>The total counts up at Lerp(FrameTime()·10); rows slide up at Lerp(FrameTime()·10).</summary>
        public float countRate = 10f, slideRate = 10f;
        /// <summary>Rows from the third down lose 100 of alpha per row: max((i − 2)·100, 0).</summary>
        public float dimStep = 100f;
        public int dimFrom = 2;
        /// <summary>draw.SimpleTextOutlined's outline: 2 wide, black at 20·alpha.</summary>
        public float outline = 2f, outlineAlpha = 20f;
        /// <summary>The fonts: bo6_hitmarker_exp 32 (600), _text 30 (500), _total 40 (600), each ·screen_scale·textsize + pop.</summary>
        public float plusSize = 32f, nameSize = 30f, totalSize = 40f;
        public int plusWeight = 600, nameWeight = 500, totalWeight = 600;
        /// <summary>Color(255, 233, 85) for the total, white rows, black outlines.</summary>
        public Color totalColour = new Color(1f, 233f / 255f, 85f / 255f), rowColour = Color.white, outlineColour = Color.black;
    }

    /// <summary>
    /// The kill chain's numbers (row Chain): Cool Killchains' HUDPaint hook and CoolKillchainFunction
    /// (lua/autorun/coolzkillchains.lua). Its DoSize2(s) is s·ScrW()/640·skull scale.
    /// </summary>
    [Serializable]
    public sealed class HitFeedbackChainLook
    {
        /// <summary>skullsize 10; a skull's cell is skullsize + 2; the row's first skull sits 1 further left.</summary>
        public float skullSize = 10f, gap = 2f, edge = 1f, unit = 640f;
        /// <summary>skulldecaytimeconstant 4, +0.33 per skull already in the chain. Only the fades use it now: when the
        /// chain ends is the kill streak's (CombatFeedHUD.StreakWindow).</summary>
        public float decay = 4f, perKill = .33f;
        /// <summary>A skull fades in over decay/30 (math.ease.InQuart); the chain fades out as (end − now)·decay·0.5.</summary>
        public float fadeInSpeed = 30f, fadeOutSpeed = .5f;
        /// <summary>A new skull starts 1.75 times its size (1 + 0.75) and 4 cells' worth of its growth to the right.</summary>
        public float popScale = .75f, popShift = 4f;
        /// <summary>Skulls draw at 200 of 255 alpha; the row re-centres at Lerp(FrameTime()·5).</summary>
        public float alpha = 200f, smoothRate = 5f;
        /// <summary>profiteers_skull_max 0 means no more than half the screen's width of skulls.</summary>
        public float widthShare = .5f;
        /// <summary>White skulls, Color(255, 58, 58) for a headshot and a melee kill, Color(255, 137, 59) for a blast and a burn.</summary>
        public Color plainColour = Color.white, headColour = new Color(1f, 58f / 255f, 58f / 255f),
            blastColour = new Color(1f, 137f / 255f, 59f / 255f), burnColour = new Color(1f, 137f / 255f, 59f / 255f),
            meleeColour = new Color(1f, 58f / 255f, 58f / 255f);
    }

    /// <summary>
    /// The sound scripts' numbers (row Sound): sound.Add's pitch {80, 110} on the marker sounds (Hit.Mark_bo6,
    /// Hit.Head.Mark_bo6, Kill.Mark_bo6, Kill.Head.Mark_bo6, Hit.Mark.Item) and volume 1 on all; the flesh layers
    /// (Hit.Body, Hit.Head and their .Layer) have no pitch of their own.
    /// </summary>
    [Serializable]
    public sealed class HitFeedbackSoundLook
    {
        public float markPitchMin = .8f, markPitchMax = 1.1f, markVolume = 1f, fleshVolume = 1f;
    }
}
