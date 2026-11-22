using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    // cl_hazard.lua: a box a kind of harm taken (its damage type's picture over a back pulsing slowly), growing down into
    // place with a flash of the hazards' colour, side by side, each up for 5 s after its last hit. The addon knows eleven
    // damage types (poison and nerve gas, acid, radiation, two blasts, burn, fall, shock, drowning, dissolving); this game
    // deals two of them: a fall (DamageKind.Fall) and a blast (a frag's or an exploder's).
    public sealed partial class PlayerHud
    {
        // The addon's damage types (DMG_FALL, DMG_BLAST), a box each.
        private const int HazardFall = 32, HazardBlast = 64;

        private sealed class Hazard
        {
            public int Kind;
            public Texture Icon;
            public float X, Until, Alpha, Flare, Height;
        }

        private readonly List<Hazard> hazards = new List<Hazard>();
        private readonly Stack<Hazard> spareHazards = new Stack<Hazard>();

        /// <summary>AddHazards: a new box, or five more seconds for the one of that kind.</summary>
        private void AddHazard(int kind, Texture icon)
        {
            float until = Smooth.Now + look.Hazard.hold;
            foreach (var hazard in hazards)
            {
                if (hazard.Kind != kind) continue;
                hazard.Until = until;
                return;
            }
            var made = spareHazards.Count > 0 ? spareHazards.Pop() : new Hazard();
            made.Kind = kind;
            made.Icon = icon;
            made.X = 0f;
            made.Until = until;
            made.Alpha = 0f;
            made.Flare = 1f;
            made.Height = 0f;
            hazards.Add(made);
        }

        private void DrawHazards(PlayerHudOptions o)
        {
            if (!o.hazards) return;
            var z = look.Hazard;
            float size = screenWidth * z.size * scale;
            float x = sw * look.Place.hazards.x + swayX + o.hazardsX, y = sh * look.Place.hazards.y - swayY + o.hazardsY;
            float pulse = Mathf.Abs(Mathf.Sin(now * z.pulse));
            Color32 colour = Rgb(o.hazardsColor);
            float opacity = Opacity(o.hazardsColor);
            for (int i = 0; i < hazards.Count; i++)
            {
                var hazard = hazards[i];
                int place = i + 1;
                hazard.Alpha = hazard.Until >= now ? Smooth.Approach(hazard.Alpha, 1f, z.fadeIn) : Smooth.Approach(hazard.Alpha, 0f, z.fadeOut);
                if (hazard.Height < 1f) hazard.Height = Smooth.Approach(hazard.Height, 1f, z.grow);
                else hazard.Flare = Smooth.Approach(hazard.Flare, 0f, z.flareFade);
                hazard.X = Smooth.Lerp(z.follow, hazard.X, (place - 1) * z.spacing);
                painter.AlphaScale = (1f + Mathf.Clamp((z.shown - place) * z.fadeStep, -1f, 0f)) * opacity;
                float left = x + size * hazard.X, tall = size * hazard.Height;
                painter.DrawColor = Gm.WithAlpha(background, z.backAlpha * hazard.Alpha * pulse);
                painter.Rect(left, y, size, tall);
                painter.DrawColor = Gm.WithAlpha(colour, 255f * hazard.Alpha);
                painter.Picture = hazard.Icon;
                painter.TexturedRect(left, y, size, tall);
                painter.DrawColor = Gm.WithAlpha(colour, 255f * hazard.Flare);
                painter.Rect(left, y, size, tall);
            }
            // Gone boxes leave after the loop (the addon's mid-loop removal skipped the next box for a frame).
            for (int i = hazards.Count - 1; i >= 0; i--)
            {
                if (hazards[i].Alpha > 0f || hazards[i].Until >= now) continue;
                spareHazards.Push(hazards[i]);
                hazards.RemoveAt(i);
            }
            painter.AlphaScale = 1f;
        }
    }
}
