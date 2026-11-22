using System;
using UnityEngine;

namespace HudAddons
{
    // The ultimate as a magazine left of the portrait: the old HUD's magazine (UltimateMagazine, the user's round 4 pick on
    // 2026-11-11: draft B's place, draft C's motion, draft A's magazine change), which the 0.9.327 segmented bar lost (the
    // user, 11-22: "궁극기 게이지는 탄창이란 느낌이 안 나잖아"), drawn on the HUD's painter in the addon's colours (the
    // user's pick: addon colours only, no pink):
    // - one round is a tenth of one use's cost (UltimateCharge.RequiredCharge), so ten rounds is ready; room up to the most
    //   held makes more slots over a white line, their cases in the addon's over-full colour. Rounds lie on their side,
    //   bullet towards the picture, stacked from the foot, a tick per round on the left;
    // - the next round's outline is drawn round as the charge comes; complete, it is pushed in from the left with sparks;
    // - ready: the cover over the magazine is slammed shut white, its edge beats, a light runs up the rounds;
    // - spent (the charge drops by about one cost while ready, as the perk cards' TryConsume does): the cover slides off,
    //   the spent rounds strip out of the top with their cases thrown left in the addon's loss orange, the magazine drops
    //   away turning, and a fresh one slides up with what was left over.
    // Game time, so a use made while the perk cards pause the game plays out as the game goes on. Numbers: PlayerHud.csv
    // Magazine rows; sizes are the old magazine's px on 1080p times the scale option.
    public sealed partial class PlayerHud
    {
        private const int MostRounds = 16;
        private const float MagazinePad = 4f, CoverHeight = 14f, CoverSkew = 3f, CoverSide = 2f, CaseWidth = 9f, CaseHeight = 4.5f;

        private bool magazinePrimed, magazineReady;
        private int roundsLoaded, roundSlots, changeFrom, changeTo;
        private float roundPart, lastCharge, readyAt = -99f, changeAt = -99f, gameNow, unit = 1f;
        private readonly float[] seatAt = new float[MostRounds];
        // Where drawing goes: turned about a pivot, then moved, at an opacity (the falling and the fresh magazine).
        private Vector2 poseMove, posePivot;
        private float poseCos = 1f, poseSin, poseAlpha = 1f;

        // The magazine's column: (x, y) its top left, w x h with the cover over the body.
        private void DrawMagazine(float x, float y, float w, float h)
        {
            var m = look.Magazine;
            unit = scale / Mathf.Max(.01f, hud.ScreenScale(look.Hud.scale));
            gameNow = Time.time;
            var source = player.Ultimate;
            if (source == null || source.RequiredCharge <= 0f)
            {
                roundSlots = 0;
                magazinePrimed = false;
                return;
            }
            ReadMagazine(source.CurrentCharge, source.RequiredCharge, source.MaximumCharge, m);
            float top = y + (CoverHeight + 4f) * unit;
            var body = Rect.MinMaxRect(x, top, x + w, y + h);
            float c = gameNow - changeAt;
            int spent = changeFrom - changeTo;
            float change = m.stripSeconds + m.freshDelay + m.freshSeconds;
            if (c >= 0f && c < Mathf.Max(change, m.stripSeconds + m.dropSeconds))
            {
                // The old magazine: the spent rounds leave from the top, then it falls away turning.
                int stripped = c < m.stripSeconds ? Mathf.FloorToInt(spent * c / m.stripSeconds) : spent;
                float d = Mathf.Clamp01((c - m.stripSeconds) / m.dropSeconds);
                if (d < 1f)
                {
                    Pose(new Vector2(0f, (d * d * m.dropFall + d * 8f) * unit), 35f * d * d, body.center, 1f - d);
                    Rounds(body, changeFrom - stripped, 0f, false, m);
                }
                float f = Mathf.Clamp01((c - m.stripSeconds - m.freshDelay) / m.freshSeconds);
                if (f > 0f)
                {
                    Pose(new Vector2(0f, m.freshRise * unit * (1f - Kick(f))), 0f, body.center, f);
                    Rounds(body, changeTo, roundPart, false, m);
                }
            }
            else
            {
                Pose(Vector2.zero, 0f, body.center, 1f);
                Rounds(body, roundsLoaded, roundPart, true, m);
            }
            Pose(Vector2.zero, 0f, body.center, 1f);
            Cover(body, c, spent, m);
            Cases(body, c, spent, m);
        }

        private void ReadMagazine(float charge, float required, float most, PlayerHudLook.MagazineRow m)
        {
            int perUse = Mathf.Clamp(m.rounds, 1, MostRounds);
            float size = required / perUse;
            int slots = Mathf.Clamp(Mathf.CeilToInt(most / size - .001f), perUse, MostRounds);
            float rounds = charge / size;
            int loaded = Mathf.Clamp(Mathf.FloorToInt(rounds + 1e-4f), 0, slots);
            float part = loaded >= slots ? 0f : Mathf.Clamp01(rounds - loaded);
            bool ready = loaded >= perUse;
            if (!magazinePrimed)
            {
                magazinePrimed = true;
                for (int i = 0; i < MostRounds; i++) seatAt[i] = -99f;
                readyAt = -99f;
            }
            else if (magazineReady && charge < lastCharge - required * .9f)
            {
                // Spent: the magazine is changed; what is left over comes up in the fresh one, already seated.
                changeAt = gameNow;
                changeFrom = roundsLoaded;
                changeTo = loaded;
                for (int i = 0; i < MostRounds; i++) seatAt[i] = -99f;
                readyAt = ready ? gameNow + m.stripSeconds + m.freshDelay + m.freshSeconds : -99f;
            }
            else
            {
                // New rounds go in one after another, a beat apart, when several come at once.
                for (int i = roundsLoaded; i < loaded && i < MostRounds; i++) seatAt[i] = gameNow + (i - roundsLoaded) * .05f;
                if (ready && !magazineReady) readyAt = gameNow + Mathf.Max(0, loaded - 1 - roundsLoaded) * .05f + m.seatSeconds * .5f;
            }
            magazineReady = ready;
            roundsLoaded = loaded;
            roundPart = part;
            roundSlots = slots;
            lastCharge = charge;
        }

        // The body (the addon's box with a faint frame), its ticks, the line past one use, the rounds and the ready light.
        private void Rounds(Rect r, int count, float partial, bool live, PlayerHudLook.MagazineRow m)
        {
            if (roundSlots <= 0) return;
            int perUse = Mathf.Clamp(m.rounds, 1, MostRounds);
            float pad = MagazinePad * unit, pitch = (r.height - 2f * pad) / roundSlots;
            float h = Mathf.Min(8f * unit, pitch * .62f), length = r.width - 6f * unit;
            Quad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), (Color)background);
            Frame(r, unit, With(Color.white, .28f));
            for (int i = 0; i < roundSlots; i++)
            {
                float y = r.yMax - pad - pitch * (i + .5f), tick = ((i + 1) % 5 == 0 ? 8f : 4f) * unit;
                Line(new Vector2(r.xMin - tick - unit, y), new Vector2(r.xMin - unit, y), 1.4f * unit, With(Color.white, i < count ? .7f : .22f));
            }
            if (roundSlots > perUse)
            {
                float y = r.yMax - pad - pitch * perUse, half = .8f * unit;
                Quad(new Vector2(r.xMin - 11f * unit, y - half), new Vector2(r.xMax, y - half), new Vector2(r.xMax, y + half),
                    new Vector2(r.xMin - 11f * unit, y + half), Color.white);
            }
            float x = r.xMin + 3f * unit;
            for (int i = 0; i < roundSlots; i++)
            {
                float y = r.yMax - pad - pitch * (i + .5f);
                if (i < count)
                {
                    float s = live && i < MostRounds ? (gameNow - seatAt[i]) / Mathf.Max(.01f, m.seatSeconds) : 9f;
                    if (s < 0f) continue; // queued behind the round before it
                    bool seating = s < 1f;
                    float dx = seating ? -m.seatPush * unit * (1f - Kick(s)) : 0f;
                    Color body = i < perUse ? m.round : (Color)overOne;
                    Round(x + dx, y, length, h, body, seating && s < .3f ? Color.white : m.bullet);
                    if (seating) Sparks(new Vector2(x + dx - 2f * unit, y), s, i);
                }
                else
                {
                    Round(x, y, length, h, With(Color.white, .06f), With(Color.white, .09f));
                    if (live && i == count && partial > .02f)
                    {
                        Round(x, y, length, h, With(m.round, .3f * partial), With(Color.white, .3f * partial));
                        Outline(x, y, length, h, partial, With(Color.white, .9f));
                    }
                }
            }
            if (live && magazineReady && gameNow >= readyAt)
            {
                float k = Mathf.Repeat((gameNow - readyAt) / Mathf.Max(.1f, m.sweepSeconds), 1f), y = r.yMax - k * r.height;
                Quad(new Vector2(r.xMin + 2f * unit, y - unit), new Vector2(r.xMax - 2f * unit, y - unit), new Vector2(r.xMax - 2f * unit, y + unit),
                    new Vector2(r.xMin + 2f * unit, y + unit), With(Color.white, .5f * (1f - .5f * k)));
            }
        }

        // The cover over the magazine: open (a faint plate with an empty nested diamond) until ready, then slammed shut white.
        private void Cover(Rect r, float c, int spent, PlayerHudLook.MagazineRow m)
        {
            if (roundSlots <= 0) return;
            float x0 = r.xMin - CoverSide * unit, foot = r.yMin - 4f * unit, w = r.width + CoverSide * 2f * unit;
            float cx = x0 + w * .5f + CoverSkew * unit * .5f, cy = foot - CoverHeight * unit * .5f;
            Plate(x0, foot, w, (Color)background);
            PlateOutline(x0, foot, w, 0f, 1.2f * unit, With(Color.white, .35f));
            Diamond(cx, cy, 4.5f * unit, With(Color.white, .45f), false);
            bool closed = magazineReady && gameNow >= readyAt;
            bool leaving = c >= 0f && c < .1f;
            if (closed || leaving)
            {
                float k = closed ? Kick(Mathf.Clamp01((gameNow - readyAt) / Mathf.Max(.01f, m.slamSeconds))) : 1f - Mathf.Clamp01(c / .1f);
                float shift = -30f * unit * (1f - k), a = closed ? 1f : k;
                Plate(x0 + shift, foot, w, With(m.round, a));
                Diamond(cx + shift, cy, 4f * unit, With(Color.black, a), true);
                if (closed)
                    PlateOutline(x0, foot, w, 3f * unit, 2f * unit, With(Color.white, .5f * Smooth.Heartbeat(gameNow - readyAt, m.beatsPerSecond)));
            }
            // Each spent round lights the cover as it goes.
            for (int i = 0; i < spent; i++)
            {
                float age = c - m.stripSeconds * (i + 1) / Mathf.Max(1, spent);
                if (age >= 0f && age < .07f) Plate(x0, foot, w, With(Color.white, 1f - age / .07f));
            }
        }

        // The spent rounds' cases, thrown out of the cover to the left.
        private void Cases(Rect r, float c, int spent, PlayerHudLook.MagazineRow m)
        {
            var port = new Vector2(r.xMin - CoverSide * unit, r.yMin - 4f * unit - CoverHeight * unit * .5f);
            for (int i = 0; i < spent; i++)
            {
                float k = (c - m.stripSeconds * (i + 1) / Mathf.Max(1, spent)) / Mathf.Max(.05f, m.caseSeconds);
                if (k < 0f || k >= 1f) continue;
                var at = port + new Vector2(-k * 60f * (.8f + .4f * Hash(i)), -(k * 48f - k * k * 90f)) * unit;
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.6f, 1f, k));
                Spun(at, new Vector2(CaseWidth, CaseHeight) * (.5f * unit), k * 520f, With(m.spent, a));
            }
        }

        // ---- shapes (screen units, y down) ----

        // A cartridge on its side, bullet towards the picture; x, y: its back end, centre line.
        private void Round(float x, float y, float length, float h, Color body, Color bullet)
        {
            float neck = length * .62f, hh = h * .5f;
            Quad(new Vector2(x, y - hh), new Vector2(x + neck, y - hh), new Vector2(x + neck, y + hh), new Vector2(x, y + hh), body);
            Span<Vector2> tip = stackalloc Vector2[5];
            tip[0] = new Vector2(x + neck, y + hh - .5f * unit);
            tip[1] = new Vector2(x + neck, y - hh + .5f * unit);
            tip[2] = new Vector2(x + length * .84f, y - hh + 1.1f * unit);
            tip[3] = new Vector2(x + length, y);
            tip[4] = new Vector2(x + length * .84f, y + hh - 1.1f * unit);
            Fan(tip, bullet);
            float rim0 = x + 1.5f * unit, rim1 = x + 3f * unit;
            Quad(new Vector2(rim0, y - hh), new Vector2(rim1, y - hh), new Vector2(rim1, y + hh), new Vector2(rim0, y + hh), With(Color.black, .45f * body.a));
        }

        // The next round's outline, drawn round from its back end as far as the charge has come.
        private void Outline(float x, float y, float length, float h, float share, Color tint)
        {
            float hh = h * .5f;
            Span<Vector2> p = stackalloc Vector2[6];
            p[0] = new Vector2(x, y + hh);
            p[1] = new Vector2(x, y - hh);
            p[2] = new Vector2(x + length * .84f, y - hh);
            p[3] = new Vector2(x + length, y);
            p[4] = new Vector2(x + length * .84f, y + hh);
            p[5] = new Vector2(x, y + hh);
            float total = 0f;
            for (int i = 1; i < p.Length; i++) total += Vector2.Distance(p[i - 1], p[i]);
            float left = total * Mathf.Clamp01(share);
            for (int i = 1; i < p.Length && left > 0f; i++)
            {
                float segment = Vector2.Distance(p[i - 1], p[i]);
                var end = segment <= left ? p[i] : Vector2.Lerp(p[i - 1], p[i], left / segment);
                Line(p[i - 1], end, 1.2f * unit, tint);
                left -= segment;
            }
        }

        private void Sparks(Vector2 from, float k, int seed)
        {
            Span<Vector2> spark = stackalloc Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                float angle = Mathf.PI + (Hash(seed * 7 + i) - .5f) * 1.6f;
                float reach = (4f + (1f - Mathf.Pow(1f - k, 3f)) * (14f + Hash(seed * 7 + i + 3) * 12f)) * unit;
                float size = (4f * (1f - k) + 1.2f) * unit;
                var dir = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle));
                var side = new Vector2(Mathf.Cos(angle + 1.6f), -Mathf.Sin(angle + 1.6f));
                var at = from + dir * reach;
                spark[0] = at;
                spark[1] = at + dir * size * 1.8f;
                spark[2] = at + side * size * .6f;
                Fan(spark, With(i % 2 == 0 ? Color.white : (Color)ammoGrey, 1f - k));
            }
        }

        // The cover's plate, its foot at y, leaning right as it rises.
        private void Plate(float x, float foot, float w, Color tint)
        {
            float skew = CoverSkew * unit, top = foot - CoverHeight * unit;
            Quad(new Vector2(x, foot), new Vector2(x + skew, top), new Vector2(x + w + skew, top), new Vector2(x + w, foot), tint);
        }

        private void PlateOutline(float x, float foot, float w, float grow, float thickness, Color tint)
        {
            if (tint.a <= 0f) return;
            float skew = CoverSkew * unit, top = foot - CoverHeight * unit;
            Vector2 a = new Vector2(x - grow, foot + grow), b = new Vector2(x + skew - grow, top - grow);
            Vector2 c = new Vector2(x + w + skew + grow, top - grow), d = new Vector2(x + w + grow, foot + grow);
            Line(a, b, thickness, tint); Line(b, c, thickness, tint); Line(c, d, thickness, tint); Line(d, a, thickness, tint);
        }

        // A nested diamond: an outline with a small filled one inside, or one solid diamond.
        private void Diamond(float x, float y, float radius, Color tint, bool solid)
        {
            Vector2 t = new Vector2(x, y - radius), r = new Vector2(x + radius, y), b = new Vector2(x, y + radius), l = new Vector2(x - radius, y);
            if (solid)
            {
                Quad(l, t, r, b, tint);
                return;
            }
            float width = 1.2f * unit, s = radius * .45f;
            Line(l, t, width, tint); Line(t, r, width, tint); Line(r, b, width, tint); Line(b, l, width, tint);
            Quad(new Vector2(x - s, y), new Vector2(x, y - s), new Vector2(x + s, y), new Vector2(x, y + s), tint);
        }

        private void Frame(Rect r, float thickness, Color tint)
        {
            Vector2 a = new Vector2(r.xMin, r.yMin), b = new Vector2(r.xMax, r.yMin), c = new Vector2(r.xMax, r.yMax), d = new Vector2(r.xMin, r.yMax);
            Line(a, b, thickness, tint); Line(b, c, thickness, tint); Line(c, d, thickness, tint); Line(d, a, thickness, tint);
        }

        private void Line(Vector2 a, Vector2 b, float thickness, Color tint)
        {
            var along = b - a;
            if (along.sqrMagnitude < 1e-6f) return;
            var n = new Vector2(-along.y, along.x).normalized * (thickness * .5f);
            Quad(a - n, b - n, b + n, a + n, tint);
        }

        private void Spun(Vector2 centre, Vector2 half, float degrees, Color tint)
        {
            float rad = degrees * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector2 Corner(float x, float y) => centre + new Vector2(x * c - y * s, x * s + y * c);
            Quad(Corner(-half.x, -half.y), Corner(half.x, -half.y), Corner(half.x, half.y), Corner(-half.x, half.y), tint);
        }

        // ---- vertices: the pose, then the painter's flat batch ----

        private void Pose(Vector2 move, float degrees, Vector2 about, float opacity)
        {
            poseMove = move;
            posePivot = about;
            poseAlpha = opacity;
            float rad = degrees * Mathf.Deg2Rad;
            poseCos = Mathf.Cos(rad);
            poseSin = Mathf.Sin(rad);
        }

        private PolyVertex Placed(Vector2 p)
        {
            var d = p - posePivot;
            return new PolyVertex(posePivot.x + d.x * poseCos - d.y * poseSin + poseMove.x, posePivot.y + d.x * poseSin + d.y * poseCos + poseMove.y);
        }

        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            tint.a *= poseAlpha;
            if (tint.a <= .002f) return;
            painter.Quads(null).Quad(Placed(a), Placed(b), Placed(c), Placed(d), tint);
        }

        // A convex polygon as a fan from its first point.
        private void Fan(ReadOnlySpan<Vector2> points, Color tint)
        {
            tint.a *= poseAlpha;
            if (tint.a <= .002f || points.Length < 3) return;
            Span<PolyVertex> corners = stackalloc PolyVertex[points.Length];
            for (int i = 0; i < points.Length; i++) corners[i] = Placed(points[i]);
            painter.Quads(null).Poly(corners, tint);
        }

        private static Color With(Color colour, float alpha)
        {
            colour.a *= alpha;
            return colour;
        }

        // Ease out past the end and back: the snap of something seating.
        private static float Kick(float t)
        {
            t = Mathf.Clamp01(t);
            float u = t - 1f;
            return 1f + 2.9f * u * u * u + 1.9f * u * u;
        }

        private static float Hash(int i)
        {
            float x = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }
    }
}
