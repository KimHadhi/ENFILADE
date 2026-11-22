using UnityEngine;

namespace HudAddons
{
    // cl_indicator.lua (the damage direction arcs), cl_effects.lua (the vignette flare and the view shake) and the
    // server halves that trigger them (sv_indicator.lua, sv_effects.lua), all on this PC: the hit is its own player's.
    public sealed partial class PlayerHud
    {
        private const int FlareDamage = 0, FlareHeal = 1, FlareArmor = 2;
        private const int MostArcs = 16;

        /// <summary>One arc: where the hit came from (fixed when it landed, as the addon's attacker position), its colour,
        /// and its ease (thickness, life, the narrowing it lerps after its life).</summary>
        private struct Arc
        {
            public int Source;
            public Vector3 From;
            public Color32 Colour;
            public float Thickness, Life, Narrow;
            public bool Live;
            // On the settings' stage the hit comes from a bearing of its own (Stage).
            public bool Staged;
            public float Bearing;
        }

        private readonly Arc[] arcs = new Arc[MostArcs];
        private Color flareColour;
        private float vignetteLeft, vignetteShown, shakeLeft, lastHealth = float.NaN;
        private PlayerHudShake shake;

        /// <summary>
        /// NeneDamageSense's receipt: the glitch, health and armour shown for a while, and an arc for the hit's source
        /// (one per source: a new hit from it starts its arc again, keeping how narrow it was, as the addon's per-attacker
        /// lerp did). The addon drew a hit from the world (a fall) toward its own feet, which has no direction and came out
        /// at the world's fixed yaw; a hit with no direction draws no arc.
        /// </summary>
        private void Sensed(in DamageHit hit, bool shielded, bool blast, PlayerHudOptions o)
        {
            if (o.glitch) fx.Glitch(look.Fx, now, screenHeight > 0f ? screenHeight : hud.Height);
            hideUntil = now + look.Hud.hideSeconds;
            if (!From(hit, out Vector3 from)) return;
            var s = look.Sense;
            int source = hit.Source != null ? hit.Source.GetInstanceID() : 0;
            int slot = -1, oldest = 0;
            for (int i = 0; i < arcs.Length; i++)
            {
                if (arcs[i].Live && arcs[i].Source == source) { slot = i; break; }
                if (slot < 0 && !arcs[i].Live) slot = i;
                if (arcs[i].Life < arcs[oldest].Life) oldest = i;
            }
            if (slot < 0) slot = oldest;
            bool again = arcs[slot].Live && arcs[slot].Source == source;
            arcs[slot] = new Arc
            {
                Source = source, From = from, Live = true, Thickness = 1f, Life = 1f, Narrow = again ? arcs[slot].Narrow : 0f,
                Colour = blast ? s.blast : shielded ? s.shielded : s.hit,
            };
        }

        // The attacker's place: where the blow was launched or burst, else the attacker, else back along its way.
        private static bool From(in DamageHit hit, out Vector3 from)
        {
            from = default;
            if (hit.Kind == DamageKind.Fall) return false;
            if (hit.Origin.HasValue && Finite(hit.Origin.Value)) { from = hit.Origin.Value; return true; }
            if (hit.Source != null) { from = hit.Source.transform.position; return true; }
            if (!Finite(hit.Direction) || hit.Direction.sqrMagnitude < 1e-6f) return false;
            from = hit.Point - hit.Direction.normalized * 30f;
            return Finite(from);
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        /// <summary>
        /// Nene.DrawDamageSense: each arc on a ring a quarter of the screen's height out from the middle, toward its hit
        /// (the top is ahead), thick at first, thinning to 15 %, then fading over a second, narrowing from 30° to 2° as it
        /// lives and widening again as it fades.
        /// </summary>
        private void DrawSense(PlayerHudOptions o)
        {
            var s = look.Sense;
            var eye = Eye();
            float cx = sw * look.Place.sense.x + swayX, cy = sh * look.Place.sense.y - swayY, radius = screenHeight * s.radius;
            for (int i = 0; i < arcs.Length; i++)
            {
                ref var arc = ref arcs[i];
                if (!arc.Live) continue;
                arc.Thickness = Mathf.Clamp(arc.Thickness - dt * s.thin, s.thinnest, 1f);
                arc.Narrow = Smooth.Lerp(s.narrowFollow, arc.Narrow, arc.Life);
                if (arc.Thickness <= s.thinnest * 1.25f) arc.Life = Mathf.Clamp01(arc.Life - dt);
                if (arc.Life <= 0f)
                {
                    arc.Live = false;
                    continue;
                }
                if (!o.sense || (eye == null && !arc.Staged)) continue;
                float bearing = arc.Staged ? arc.Bearing : Bearing(eye, arc.From);
                DrawArc(cx, cy, radius, -screenHeight * s.thickness * arc.Thickness * arc.Life,
                    s.start + s.narrow * arc.Narrow - bearing, s.end - s.narrow * arc.Narrow - bearing, s.segments,
                    Gm.WithAlpha(arc.Colour, arc.Colour.a * arc.Life));
            }
        }

        // Where the hit came from, in degrees from straight ahead, to the left positive (Garry's Mod's yaw).
        private static float Bearing(Transform eye, Vector3 from)
        {
            Vector3 to = from - eye.position;
            if (to.x * to.x + to.z * to.z < 1e-8f) return 0f;
            float toward = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            return -Mathf.DeltaAngle(eye.eulerAngles.y, toward);
        }

        /// <summary>Nene.DrawArc: a ring segment from startAngle to endAngle (degrees, clockwise on the screen from its
        /// right), a negative thickness growing inward from the radius, as quads.</summary>
        private void DrawArc(float cx, float cy, float radius, float thickness, float startAngle, float endAngle, int segments,
            Color32 colour)
        {
            segments = Mathf.Max(segments, 1);
            if (thickness < 0f)
            {
                radius += thickness;
                thickness = -thickness;
            }
            float outer = radius + thickness;
            float angle = startAngle * Mathf.Deg2Rad, step = (endAngle - startAngle) * Mathf.Deg2Rad / segments;
            var quads = painter.Quads(null);
            float x = Mathf.Cos(angle), y = Mathf.Sin(angle);
            for (int i = 1; i <= segments; i++)
            {
                float nx = Mathf.Cos(angle + step * i), ny = Mathf.Sin(angle + step * i);
                quads.Quad(new PolyVertex(cx + x * outer, cy + y * outer), new PolyVertex(cx + nx * outer, cy + ny * outer),
                    new PolyVertex(cx + nx * radius, cy + ny * radius), new PolyVertex(cx + x * radius, cy + y * radius), colour);
                x = nx;
                y = ny;
            }
        }

        // ---- the flare and the shake (cl_effects.lua) ----

        /// <summary>
        /// Nene.DamageEffect's receipt: a hit (red, and the shake), a heal (yellow-green) or a hit the shield took (white, and
        /// the shake), each behind its own "glitter" option. The addon started the shake at the intensity option clamped to
        /// one next frame and an armour hit's at 1, then scaled the motion by the intensity again, so the slider did nothing
        /// past 1 and counted twice under it; here every shake starts whole and the intensity sizes it once.
        /// </summary>
        private void Flare(int kind, PlayerHudOptions o)
        {
            var f = look.Flare;
            switch (kind)
            {
                case FlareDamage when o.flickDamage:
                    flareColour = f.damage;
                    shakeLeft = 1f;
                    vignetteLeft = 1f;
                    break;
                case FlareHeal when o.flickHeal:
                    flareColour = f.heal;
                    vignetteLeft = 1f;
                    break;
                case FlareArmor when o.flickArmor:
                    flareColour = f.armor;
                    shakeLeft = 1f;
                    vignetteLeft = 1f;
                    break;
            }
        }

        // sv_effects.lua's Think: health going up is a heal. The addon watched Entity(1), the first player only, and took a
        // respawn's climb from 0 for a heal; here it is this PC's own player, and coming back from nothing is no heal.
        private void WatchHealing()
        {
            float health = receiver.Health;
            if (float.IsNaN(lastHealth) || lastHealth > health || lastHealth <= 0f)
            {
                lastHealth = health;
                return;
            }
            if (lastHealth < health)
            {
                lastHealth = health;
                var o = Options;
                if (o != null) Flare(FlareHeal, o);
            }
        }

        // The vignette (vig.png over the whole screen) in the flare's colour, fading over flick_fade seconds. The addon's
        // came on whole at every hit, so blows in quick succession (a few zombies at once) flashed the screen's edges at
        // their pace; under the photosensitivity rule the drawn light eases after it (Flare.follow, 0 = as the addon), so a
        // hit swells and fades and a flurry holds a glow.
        private void DrawVignette(PlayerHudOptions o)
        {
            vignetteLeft = o.flickFade <= 0f ? 0f : Mathf.Clamp01(vignetteLeft - dt / o.flickFade);
            var f = look.Flare;
            vignetteShown = f.follow > 0f ? Smooth.Lerp(f.follow, vignetteShown, vignetteLeft) : vignetteLeft;
            if (f.alpha * vignetteShown < 1f || vignette == null) return;
            flarePainter.DrawColor = Gm.Rgba(f.grey * flareColour.r, f.grey * flareColour.g, f.grey * flareColour.b, f.alpha * vignetteShown);
            flarePainter.Picture = vignette;
            flarePainter.TexturedRectRotated(screenWidth * .5f, screenHeight * .5f, screenWidth, screenHeight, 0f);
        }

        // DoViewShake: the envelope runs out over shake_fade seconds (also while the shake is off, so turning it on shows
        // no leftover), the motion goes to the camera's PlayerHudShake.
        private void Shake(PlayerHudOptions o)
        {
            shakeLeft = o.shakeFade <= 0f ? 0f : Mathf.Clamp01(shakeLeft - dt / o.shakeFade);
            float amount = o.shake ? shakeLeft * o.shakeIntensity : 0f;
            if (amount <= 0f)
            {
                HoldShake();
                return;
            }
            if (shake == null)
            {
                if (view == null || !view.isActiveAndEnabled) return;
                if (!view.TryGetComponent(out shake)) shake = view.gameObject.AddComponent<PlayerHudShake>();
                if (!view.TryGetComponent(out PlayerHudShakeRestore _)) view.gameObject.AddComponent<PlayerHudShakeRestore>();
            }
            var f = look.Flare;
            float rate = now * f.shakeSpeed;
            shake.Yaw = Mathf.Cos(rate) * f.shakeYaw * amount;
            shake.Roll = -Mathf.Sin(rate) * f.shakeRoll * amount;
        }

        private void HoldShake()
        {
            if (shake == null) return;
            shake.Yaw = shake.Roll = 0f;
        }
    }
}
