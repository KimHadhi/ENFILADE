using UnityEngine;

namespace HudAddons
{
    // cl_health.lua and cl_armor.lua, with shared.lua's hiding of both (the "hide" option).
    public sealed partial class PlayerHud
    {
        private readonly Bar healthBar = new Bar(), armorBar = new Bar();
        private readonly Fade healthColour = new Fade(), stimsColour = new Fade();
        // shared.lua's statusAlpha (it starts at nothing) and until when a hit keeps health and armour up (Nene.hideTimer).
        private float statusAlpha, hideUntil = float.NegativeInfinity;

        private void DrawStatus(PlayerHudOptions o)
        {
            float alpha = 1f;
            if (o.hide)
            {
                statusAlpha = Smooth.Lerp(look.Hud.statusFollow, statusAlpha, now < hideUntil ? 1f : 0f);
                alpha = statusAlpha;
            }
            var place = look.Place;
            float x = sw * place.health.x + swayX, y = sh * place.health.y - swayY;
            DrawHealth(o, x, y, alpha);
            float drop = o.spacing == HudSpacing.Wide ? place.armorDropWide : place.armorDrop;
            DrawArmor(o, x, sh * place.health.y + sh * drop * o.scale - swayY, alpha);
            painter.AlphaScale = 1f;
        }

        // Nene.DrawHealth: the bar (low colour at 30 % and under) and the stims box left of it.
        private void DrawHealth(PlayerHudOptions o, float x, float y, float alpha)
        {
            var s = look.Status;
            float w = s.width * scale, h = s.healthHeight * scale;
            Color32 colour = Rgb(o.healthColor);
            if (o.health)
            {
                painter.AlphaScale = alpha * Opacity(o.healthColor);
                float health = HealthNow, most = HealthMost, frame = s.frame * scale;
                HudStage.Mark(HudPart.Health, x + o.healthX - w * .5f - frame, y + o.healthY - h * .5f - frame, w + frame * 2f, h + frame * 2f);
                var shown = healthColour.Step(health <= most * s.low ? low : colour, look.Colour.follow);
                LossBar(healthBar, health / most, x + o.healthX, y + o.healthY, w, h, frame, shown);
            }
            // The addon's stims are a carried healing count (a medishot or stim addon's); ours is a Détente's syringe,
            // its doses left. No other class carries one, so the box is theirs alone.
            var detente = DetenteKit.Local;
            if (o.stims && (detente != null || HudStage.On))
            {
                painter.AlphaScale = alpha * Opacity(o.healthColor);
                int doses = HudStage.On ? HudStage.Doses : detente.Doses;
                var shown = stimsColour.Step(doses <= 0 ? Dim(colour) : colour, look.Colour.follow);
                float size = s.stims * scale, bx = x + o.stimsX - w * .5f - size / s.stimsOut, by = y + o.stimsY + size * .5f - h;
                HudStage.Mark(HudPart.Stims, bx - size * .5f, by - size * .5f, size, size);
                IconBox(stimsPicture, Digits.Of(doses), altsFont, bx, by, size, size, shown, TextAlign.Right, TextAlign.Top);
            }
        }

        // Nene.DrawArmor: our shield over GMod's most armour, ten segments.
        private void DrawArmor(PlayerHudOptions o, float x, float y, float alpha)
        {
            if (!o.armor) return;
            var s = look.Status;
            painter.AlphaScale = alpha * Opacity(o.armorColor);
            float armor = ShieldNow / Mathf.Max(1f, s.armorMost), w = s.width * scale, h = s.armorHeight * scale, frame = s.frame * scale;
            HudStage.Mark(HudPart.Armor, x + o.armorX - w * .5f - frame, y + o.armorY - h * .5f - frame, w + frame * 2f, h + labelFont.Size + frame * 2f);
            SegmentBar(armorBar, s.armorParts, armor, x + o.armorX, y + o.armorY, s.width * scale, s.armorHeight * scale,
                s.frame * scale, Rgb(o.armorColor));
        }
    }
}
