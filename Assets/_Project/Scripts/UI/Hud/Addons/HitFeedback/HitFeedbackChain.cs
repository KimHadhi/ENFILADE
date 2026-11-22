using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// What killed, as Cool Killchains tells its skulls apart (its killtype, first that fits): a kick, a blow or cut, a
    /// blast, a thrown prop, fire; anything else is a plain skull. Its roadkill (car) and dissolve (combine ball) kinds
    /// are left out: nothing in this game kills that way.
    /// </summary>
    public enum HitFeedbackKill : byte { Plain, Kick, Melee, Blast, Prop, Burn }

    /// <summary>
    /// Cool Killchains' row of kill skulls under the crosshair (its HUDPaint hook and CoolKillchainFunction): one picture
    /// per kill of the chain, coloured by how it died, each popping in from 1.75 times its size, the row re-centring as
    /// it grows and capped to half the screen's width (or the player's own most); the whole row fades and goes when the
    /// chain ends.
    /// <para>The user's rule over the original: the chain ends when the kill streak ends, on its clock
    /// (CombatFeedHUD.StreakWindow), not 4 s + 0.33 s per skull after the last kill ("킬 체인은 연속 처치 시간이랑 동기화
    /// 시켜야지"). The fades keep the original's timing: a skull fades in over its own decay/30, the row fades out over
    /// its last 2/decay seconds.</para>
    /// <para>Fixed from the original: every skull fades in over the decay it was added with (the original divided by
    /// the latest decay, so of kills in one frame only the last faded in and the others appeared at once), and the end
    /// of the chain is checked before drawing (it cleared the row half-way through drawing it).</para>
    /// </summary>
    internal sealed class HitFeedbackChain
    {
        private const string Folder = "HitFeedback/profiteers/";

        private struct Skull
        {
            public float added, decay;
            public HitFeedbackKill kind;
            public bool head;
        }

        private readonly List<Skull> skulls = new List<Skull>();
        private readonly Texture skull, skullHead, explosion, knife, kick, prop, fire;
        // skullsmoothcount, skullnextdelete, skulldecaytime.
        private float smoothCount, ends, decay;

        public HitFeedbackChain()
        {
            skull = Pictures.Get(Folder + "skull");
            skullHead = Pictures.Get(Folder + "skullhs");
            explosion = Pictures.Get(Folder + "explosion");
            knife = Pictures.Get(Folder + "knife");
            kick = Pictures.Get(Folder + "kick2");
            prop = Pictures.Get(Folder + "propkill");
            fire = Pictures.Get(Folder + "fire");
        }

        /// <summary>CoolKillchainFunction(head, killtype): a skull for this kill; the chain now lasts until
        /// <paramref name="ends"/> (the kill streak's end).</summary>
        public void Add(HitFeedbackKill kind, bool head, float ends, HitFeedbackChainLook look)
        {
            decay = look.decay + skulls.Count * look.perKill;
            this.ends = ends;
            skulls.Add(new Skull { added = Smooth.Now, decay = decay, kind = kind, head = head });
        }

        /// <summary>One frame of the HUDPaint hook (the caller skips it while the skulls are turned off, as the hook returns).</summary>
        public void Draw(Painter painter, Overlay overlay, HitFeedbackChainLook look, HitFeedbackOptions o)
        {
            if (skulls.Count == 0) return;
            float now = Smooth.Now;
            if (ends < now)
            {
                skulls.Clear();
                smoothCount = 0f;
                decay = look.decay;
                return;
            }
            float width = overlay.Width, unit = width / look.unit * o.skullScale;
            float cell = (look.skullSize + look.gap) * unit;
            smoothCount = Mathf.Max(1f, (float)System.Math.Round(Smooth.Lerp(look.smoothRate, smoothCount, skulls.Count), 5));
            float whole = smoothCount * cell * .5f;
            float fade = Mathf.Clamp01((ends - now) * decay * look.fadeOutSpeed);
            int most = o.skullMax == 0 ? Mathf.CeilToInt(width * look.widthShare / cell) : o.skullMax;
            if (skulls.Count > most)
            {
                // The original's "for noticable new skull": the row jumps a cell so the new one reads as new.
                smoothCount -= 1f;
                skulls.RemoveAt(0);
            }
            if (fade <= 0f) return;

            float centre = width * .5f, middle = overlay.Height * o.skullOffset;
            for (int i = 0; i < skulls.Count; i++)
            {
                var s = skulls[i];
                float t = Mathf.Min((now - s.added) / Mathf.Max(s.decay, 1e-4f) * look.fadeInSpeed, 1f);
                float fadeIn = t * t * t * t; // math.ease.InQuart
                float scaleIn = 1f + (1f - fadeIn) * look.popScale;
                float alpha = look.alpha * fade * fadeIn;
                if (Gm.Byte(alpha) == 0) continue;

                Color tint = look.plainColour;
                Texture picture = skull;
                if (s.head) { tint = look.headColour; picture = skullHead; }
                else if (s.kind == HitFeedbackKill.Blast) { tint = look.blastColour; picture = explosion; }
                else if (s.kind == HitFeedbackKill.Burn) { tint = look.burnColour; picture = fire; }
                if (s.kind == HitFeedbackKill.Prop) picture = prop;
                else if (s.kind == HitFeedbackKill.Kick) picture = kick;
                else if (s.kind == HitFeedbackKill.Melee) { tint = look.meleeColour; picture = knife; }

                float size = look.skullSize * scaleIn;
                float left = centre - (size * .5f + size * .5f + look.edge) * unit + (scaleIn - 1f) * look.popShift * unit
                    + (i + 1) * cell - whole;
                painter.DrawColor = Gm.WithAlpha(tint, alpha);
                painter.Picture = picture;
                painter.TexturedRect(left, middle - size * .5f * unit, size * unit, size * unit);
            }
        }
    }
}
