using UnityEngine;

namespace HudAddons
{
    // Our portrait, the class's ultimate and its rank, which HUD Mk II has no element for, drawn with the addon's own
    // pieces (the user, 2026-11-22: "프사 이 정도로 크게랑 경험치 바랑 탄창 느낌의 궁극기 게이지 살려서 저 디자인으로
    // 개편", the design being the addon's, with leave to rework it a little for them). Over the health bar's left end,
    // following its place (health_x/y) and swaying with the rest:
    // - the profile picture in a framed box (the name's first letter when none is chosen);
    // - left of it the ultimate as rect.lua's segmented bar stood up, its segments the rounds of a magazine: one use's
    //   charge fills them all, what is held past that fills them again in the addon's over-colour, a use drops them with
    //   the orange loss lagging behind;
    // - under it the rank as the stims box (the insignia, how many this game climbs at its corner) beside a lagging bar
    //   to the next insignia: grey what was held when the game began, white this game's kills, faint white the share of
    //   the money held, with a kill's "+N XP" rising off it.
    // Replaces HudPortrait, UltimateMagazine and RankStrip (the old portrait tile on the left board).
    public sealed partial class PlayerHud
    {
        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private readonly Bar ultimateBar = new Bar(), rankBar = new Bar();
        // The rank bar's parts as drawn (held, then held + kills), and the insignia they belong to (-1: none seen yet).
        private float heldShown, killsShown;
        private int rankShown = -1, prestigeShown = -1, climbShown;
        private string climbText = string.Empty, gainText = string.Empty, initialName, initialText = string.Empty;
        private long killsSeen = -1, gainAmount;
        private float gainAge = float.MaxValue;

        private void DrawPortrait()
        {
            var p = look.Portrait;
            var s = look.Status;
            var o = Options;
            float frame = s.frame * scale, gap = p.gap * scale, side = p.size * scale, box = p.insignia * scale;
            // The health bar's frame, wherever its options put it: the panel stands on its left end.
            float barX = sw * look.Place.health.x + swayX + o.healthX, barY = sh * look.Place.health.y - swayY + o.healthY;
            float left = barX - s.width * scale * .5f - frame;
            float rowTop = barY - s.healthHeight * scale * .5f - frame - p.rise * scale - box;
            float tall = side + frame * 2f, top = rowTop - gap - tall;
            float magazine = p.magazine * scale, pictureLeft = left + magazine + gap;

            DrawUltimate(left, top, magazine, tall, frame, p);
            DrawPicture(pictureLeft, top, side, frame);
            DrawRank(left, rowTop, box, pictureLeft + tall, frame, gap, p);
        }

        private void DrawPicture(float x, float y, float side, float frame)
        {
            painter.RoundedBox(look.Hud.corner, x, y, side + frame * 2f, side + frame * 2f, background);
            var picture = ProfilePicture.Current;
            if (picture != null)
            {
                painter.DrawColor = White;
                painter.Picture = picture;
                painter.TexturedRect(x + frame, y + frame, side, side);
                return;
            }
            string name = GameSettings.Current.DisplayName;
            if (name != initialName)
            {
                initialName = name;
                initialText = ProfilePicture.Initial(name);
            }
            painter.OutlinedText(initialText, initialFont, x + frame + side * .5f, y + frame + side * .5f, White,
                TextAlign.Center, TextAlign.Center, outlineWidth, outline);
        }

        // BSS_Rect stood up: the loss, the fill, and what is held past one use and two drawn again over it.
        private void DrawUltimate(float x, float y, float w, float h, float frame, PlayerHudLook.PortraitRow p)
        {
            painter.RoundedBox(look.Hud.corner, x, y, w, h, background);
            var source = player.Ultimate;
            if (source == null || source.RequiredCharge <= 0f) return;
            float value = source.CurrentCharge / source.RequiredCharge;
            ultimateBar.Step(value, now, look.Bar);
            float left = x + frame, top = y + frame, width = w - frame * 2f, height = h - frame * 2f;
            Rounds(left, top, width, height, p, Mathf.Clamp01(ultimateBar.Loss), loss);
            Rounds(left, top, width, height, p, Mathf.Clamp01(ultimateBar.Fill), p.ultimate);
            if (value > 1f) Rounds(left, top, width, height, p, Mathf.Clamp01(ultimateBar.Fill - 1f), overOne);
            if (value > 2f) Rounds(left, top, width, height, p, Mathf.Clamp01(ultimateBar.Fill - 2f), overTwo);
        }

        // Segments() from the foot up: round i takes h / rounds and fills the lower (1 - gap) of it, as far as the share reaches.
        private void Rounds(float left, float top, float w, float h, PlayerHudLook.PortraitRow p, float share, Color32 colour)
        {
            int rounds = Mathf.Max(1, p.rounds);
            if (share <= 0f) return;
            float foot = top + h, room = h / rounds, filled = room * (1f - p.roundGap), reach = foot - h * share;
            for (int i = 0; i < rounds; i++)
            {
                float bottom = foot - room * i;
                if (bottom <= reach) break;
                float upper = Mathf.Max(bottom - filled, reach);
                Fill(left, upper, w, bottom - upper, colour);
            }
        }

        private void DrawRank(float left, float top, float box, float right, float frame, float gap, PlayerHudLook.PortraitRow p)
        {
            var view = RankProgress.Now();
            float kills = view.Earned > 0 ? Mathf.Lerp(view.StoredShare, view.Share, view.Kills / (float)view.Earned) : view.StoredShare;
            // A new insignia starts the bar over: no orange for the growth that took it there.
            if (view.Rank != rankShown || view.Prestige != prestigeShown)
            {
                if (rankShown >= 0)
                {
                    rankBar.Snap(view.Share);
                    heldShown = view.StoredShare;
                    killsShown = kills;
                }
                rankShown = view.Rank;
                prestigeShown = view.Prestige;
            }
            if (view.Climb != climbShown)
            {
                climbShown = view.Climb;
                climbText = climbShown > 0 ? "+" + Digits.Of(climbShown) : string.Empty;
            }

            // The stims box with the insignia for its picture.
            painter.RoundedBox(look.Hud.corner, left, top, box, box, background);
            var insignia = ClassGrowth.Insignia(view.Rank);
            if (insignia != null) Picture(insignia, White, left, top, box);
            if (climbText.Length > 0) BoxText(climbText, altsFont, left + box * .5f, top + box * .5f, box, box, White, TextAlign.Right, TextAlign.Top);

            // BL_Rect beside it, the frame's outer edge reaching the picture box's right side.
            float barLeft = left + box + gap + frame, barWidth = right - frame - barLeft, barHeight = p.rankHeight * scale;
            float barTop = top + (box - barHeight) * .5f;
            if (barWidth <= 0f) return;
            painter.RoundedBox(look.Hud.corner, barLeft - frame, barTop - frame, barWidth + frame * 2f, barHeight + frame * 2f, background);
            rankBar.Step(view.Share, now, look.Bar);
            heldShown = Smooth.Lerp(look.Bar.follow, heldShown, view.StoredShare);
            killsShown = Smooth.Lerp(look.Bar.follow, killsShown, kills);
            float total = Mathf.Clamp01(rankBar.Fill);
            Fill(barLeft, barTop, barWidth * Mathf.Clamp01(rankBar.Loss), barHeight, loss);
            Fill(barLeft, barTop, barWidth * total, barHeight, p.kept);
            Fill(barLeft, barTop, barWidth * Mathf.Min(total, Mathf.Clamp01(killsShown)), barHeight, p.game);
            Fill(barLeft, barTop, barWidth * Mathf.Min(total, Mathf.Clamp01(heldShown)), barHeight, p.held);

            // A kill's growth rises off where this game's part ends; kills close together add up in one.
            if (killsSeen >= 0 && view.Playing && view.Kills > killsSeen)
            {
                gainAmount = gainAge < .4f ? gainAmount + (view.Kills - killsSeen) : view.Kills - killsSeen;
                gainAge = 0f;
                gainText = Texts.Format("hud.rank.gain", gainAmount.ToString("N0"));
            }
            killsSeen = view.Kills;
            if (gainAge >= p.gainSeconds) return;
            gainAge += dt;
            float k = Mathf.Clamp01(gainAge / Mathf.Max(.05f, p.gainSeconds));
            painter.AlphaScale = 1f - k * k;
            painter.OutlinedText(gainText, altsFont, barLeft + barWidth * Mathf.Clamp01(killsShown), barTop - frame - p.gainRise * scale * k,
                White, TextAlign.Center, TextAlign.Bottom, outlineWidth, outline);
            painter.AlphaScale = 1f;
        }
    }
}
