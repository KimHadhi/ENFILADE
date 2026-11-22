using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Black Ops VI Hitmarker's marker (DrawHitMarker and the marker half of its bo6_hit / bo6_kill receivers): four arm
    /// pictures round the screen's middle, the head set for a headshot, white for a hit and red for a kill, turned a few
    /// degrees at random. A hit pulls the arms in and holds them 0.3 s, then they ease out to rest (farther out from the
    /// hip than down the sights) while the marker shrinks, fades and shakes for half a second. A hit on something not of
    /// flesh adds the prop badge at its lower right, a hit by fire on flesh the flame badge.
    /// <para>Kept as the addon keeps it: the hit's facts (headshot, flesh, hip fire, fire) are taken even with the
    /// marker turned off, and only the look (colour, shake, pull, fade, turn) is reset by a marker that shows.</para>
    /// </summary>
    internal sealed class HitFeedbackMarker
    {
        private const string Folder = "HitFeedback/bo6_hitmarker/";

        // Material('bo6_hitmarker/left_top') etc.: the vmts' $basetexture L1, L2, R1, R2 (and head/…).
        private readonly Texture leftTop, leftBottom, rightTop, rightBottom;
        private readonly Texture headLeftTop, headLeftBottom, headRightTop, headRightBottom;
        private readonly Texture prop, burnBadge;

        // hitmarker.{life, size, rotation, color, headshot, npc, hipfire, burn}; size is "grow" here.
        private float life, grow, turn;
        private Color32 colour = new Color32(255, 255, 255, 255);
        private bool head, flesh, hip, burning;
        // hitmarker.scale is 1 until the 0.3 s timer sets it to 0: a moment here, so no timer object is made per hit.
        private float holdUntil = float.NegativeInfinity;
        // The drawing state: scale (the pull), scale_rate (the rest spread), shake and its offset.
        private float pinch, spread = float.NaN, shake, shakeX, shakeY;
        // The shake's random draws (math.random() − 0.8 on each axis), held until the next is due.
        private float rollX, rollY, nextRoll = float.NegativeInfinity;

        public HitFeedbackMarker()
        {
            leftTop = Pictures.Get(Folder + "l1");
            leftBottom = Pictures.Get(Folder + "l2");
            rightTop = Pictures.Get(Folder + "r1");
            rightBottom = Pictures.Get(Folder + "r2");
            headLeftTop = Pictures.Get(Folder + "head/l1");
            headLeftBottom = Pictures.Get(Folder + "head/l2");
            headRightTop = Pictures.Get(Folder + "head/r1");
            headRightBottom = Pictures.Get(Folder + "head/r2");
            prop = Pictures.Get(Folder + "prop");
            burnBadge = Pictures.Get(Folder + "burn");
        }

        /// <summary>
        /// A confirmed hit or kill. The facts are always taken (as the receivers set hitmarker.* before their option
        /// check); <paramref name="show"/> (Hitmark / Killmark) resets the look. A kill is never hip fire (the addon's
        /// kill message always sends false), so a kill's arms rest at the aimed spread.
        /// </summary>
        public void Take(bool kill, bool headshot, bool organism, bool hipFire, bool fire, bool show, HitFeedbackMarkerLook look)
        {
            head = headshot;
            flesh = organism;
            hip = hipFire;
            burning = fire;
            if (!show) return;
            colour = kill ? look.killColour : look.hitColour;
            shake = 1f;
            nextRoll = float.NegativeInfinity;
            holdUntil = Smooth.Now + look.holdSeconds;
            life = 1f;
            grow = 1f;
            float most = kill ? look.killTurn : look.hitTurn;
            turn = (float)LuaRandom.Next(-most, most);
        }

        /// <summary>One frame of DrawHitMarker, in its order: the size and the shaken centre are this frame's before the
        /// eases move them, as the addon reads them first.</summary>
        public void Draw(Painter painter, Overlay overlay, HitFeedbackMarkerLook look, float markSize)
        {
            float screen = overlay.ScreenScale(look.screenScale);
            float size = (look.baseSize + grow * look.growSize) * screen * markSize;
            float x = overlay.Width * .5f + shakeX, y = overlay.Height * .5f + shakeY;

            // scale_rate starts at 20·screen_scale (without marksize), as the addon's file-level local does.
            if (float.IsNaN(spread)) spread = look.aimSpread * screen;
            spread = Smooth.Lerp(look.spreadRate, spread, (hip ? look.hipSpread : look.aimSpread) * screen * markSize);
            if (shake > 0f)
            {
                shake = Smooth.Approach(shake, 0f, look.shakeRate);
                if (Smooth.Now >= nextRoll)
                {
                    rollX = (float)(LuaRandom.Next() - look.shakeBias);
                    rollY = (float)(LuaRandom.Next() - look.shakeBias);
                    nextRoll = Smooth.Now + 1f / Mathf.Max(1f, look.shakeSteps);
                }
                float reach = shake * overlay.Width * look.shakeScreen;
                shakeX = reach * rollX;
                shakeY = reach * rollY;
            }
            float held = markSize * look.pinch * screen;
            pinch = Smooth.Now < holdUntil ? held : Smooth.Lerp(look.pinchRate, pinch, 0f);
            life = Smooth.Lerp(look.fadeRate, life, 0f);
            grow = Smooth.Lerp(look.shrinkRate, grow, 0f);

            // Nothing to see once the alpha is cut to 0 (the addon drew it on, invisible, every frame).
            float alpha = life * 255f;
            if (Gm.Byte(alpha) == 0 || size <= 0f) return;
            float pull = pinch - spread;

            if (!flesh || burning)
            {
                Color32 badge = look.badgeColour;
                painter.DrawColor = Gm.WithAlpha(badge, alpha);
                painter.Picture = flesh ? burnBadge : prop;
                float side = size / look.badgeShare;
                painter.TexturedRectRotated(x - pull + size, y - pull + size, side, side, turn);
            }

            painter.DrawColor = Gm.WithAlpha(colour, alpha);
            // The addon's places: 'left_top' (L1) at +pull on both axes, and so on; with pull ≤ 0 each arm's picture
            // moves out towards the corner its arm is drawn in.
            painter.Picture = head ? headLeftTop : leftTop;
            painter.TexturedRectRotated(x + pull, y + pull, size, size, turn);
            painter.Picture = head ? headLeftBottom : leftBottom;
            painter.TexturedRectRotated(x + pull, y - pull, size, size, turn);
            painter.Picture = head ? headRightTop : rightTop;
            painter.TexturedRectRotated(x - pull, y + pull, size, size, turn);
            painter.Picture = head ? headRightBottom : rightBottom;
            painter.TexturedRectRotated(x - pull, y - pull, size, size, turn);
        }
    }
}
