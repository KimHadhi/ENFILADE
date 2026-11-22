using UnityEngine;

namespace HudAddons
{
    // Our portrait, the class's ultimate and its rank, which HUD Mk II has no element for, drawn in the addon's boxes and
    // colours (the user, 2026-11-22: "프사 이 정도로 크게랑 경험치 바랑 탄창 느낌의 궁극기 게이지 살려서 저 디자인으로
    // 개편", the design being the addon's; then "프로필 사진 쪽 노이즈는 살짝 연하게", "경험치 쪽은 가시성이 안 좋고",
    // "궁극기 게이지는 탄창이란 느낌이 안 나잖아"; colours: the addon's only). Over the health bar's left end, following its
    // place (health_x/y) and swaying with the rest:
    // - the profile picture in a framed box (the name's first letter when none is chosen), drawn over the special effects
    //   with faint scanlines of its own, so the photo reads (the rest of the HUD keeps the addon's);
    // - left of it the ultimate as the old HUD's magazine (PlayerHud.Magazine);
    // - under it the rank: the insignia in the stims box (how many this game climbs at its corner) beside a bar to the next
    //   insignia in the score popup's yellow: white what was held when the game began, yellow this game's kills, faint
    //   yellow the share of the money held, with a kill's "+N XP" rising off it.
    // Replaces HudPortrait, UltimateMagazine and RankStrip (the old portrait tile on the left board).
    public sealed partial class PlayerHud
    {
        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private readonly Bar rankBar = new Bar();
        // The rank bar's parts as drawn (held, then held + kills), and the insignia they belong to (-1: none seen yet).
        private float heldShown, killsShown;
        private int rankShown = -1, prestigeShown = -1, climbShown;
        private string climbText = string.Empty, gainText = string.Empty, initialName, initialText = string.Empty;
        private long killsSeen = -1, gainAmount;
        private float gainAge = float.MaxValue;
        // Where the picture goes this frame (drawn over the effects by DrawPicture), and its scanlines' pattern.
        private Rect pictureRect;
        private Texture2D scanlines;

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
            // Where the class UI stands when it goes beside the portrait (PlayerHud.Class).
            panelRight = pictureLeft + tall;
            panelTop = top;
            panelRowFoot = rowTop + box;
            healthRight = barX + s.width * scale * .5f + frame;

            magazineAccent = Accent(o);
            DrawMagazine(left, top, magazine, tall);
            painter.RoundedBox(look.Hud.corner, pictureLeft, top, tall, tall, ownBox);
            pictureRect = new Rect(pictureLeft + frame, top + frame, side, side);
            if (ProfilePicture.Current == null) DrawInitial();
            DrawRank(left, rowTop, box, pictureLeft + tall, frame, gap, p);
        }

        // No picture chosen: the name's first letter, as every profile square shows it.
        private void DrawInitial()
        {
            string name = GameSettings.Current.DisplayName;
            if (name != initialName)
            {
                initialName = name;
                initialText = ProfilePicture.Initial(name);
            }
            painter.OutlinedText(initialText, initialFont, pictureRect.center.x, pictureRect.center.y, White,
                TextAlign.Center, TextAlign.Center, outlineWidth, outline);
        }

        // The photo over the special effects (their scanlines darken every other row at 175 and the HUD goes on the screen
        // at three quarters), with its own fainter scanlines while the HUD's are on.
        private void DrawPicture(PlayerHudOptions o)
        {
            var picture = ProfilePicture.Current;
            if (picture == null) return;
            picturePainter.DrawColor = White;
            picturePainter.Picture = picture;
            picturePainter.TexturedRect(pictureRect.x, pictureRect.y, pictureRect.width, pictureRect.height);
            if (!o.sfx || !o.scanline || look.Portrait.scanline <= 0f) return;
            // One texel a screen row, the dark row every other one, as the effects' scanlines lie.
            float row = hud.Height / Mathf.Max(1, Screen.height);
            picturePainter.DrawColor = Gm.Rgba(0f, 0f, 0f, look.Portrait.scanline);
            picturePainter.Picture = Scanlines();
            picturePainter.TexturedRectUV(pictureRect.x, pictureRect.y, pictureRect.width, pictureRect.height,
                0f, pictureRect.yMin / (row * 2f), 1f, pictureRect.yMax / (row * 2f));
        }

        private Texture2D Scanlines()
        {
            if (scanlines != null) return scanlines;
            scanlines = new Texture2D(1, 2, TextureFormat.RGBA32, false)
            {
                name = "Portrait Scanlines (Runtime)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave
            };
            scanlines.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 0) });
            scanlines.Apply(false, true);
            return scanlines;
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

            // The stims box with the rank's plain white emblem for its picture (the coloured insignia on its dark hexagon
            // stood out alone: the user, 11-22), whole (the addon's 150 alpha made it hard to read).
            painter.RoundedBox(look.Hud.corner, left, top, box, box, ownBox);
            var emblem = ClassGrowth.Emblem(view.Rank);
            if (emblem != null)
            {
                float inset = box * p.emblemInset;
                painter.DrawColor = White;
                painter.Picture = emblem;
                painter.TexturedRect(left + inset, top + inset, box - inset * 2f, box - inset * 2f);
            }
            if (climbText.Length > 0) BoxText(climbText, altsFont, left + box * .5f, top + box * .5f, box, box, p.game, TextAlign.Right, TextAlign.Top);

            // BL_Rect beside it, the frame's outer edge reaching the picture box's right side.
            float barLeft = left + box + gap + frame, barWidth = right - frame - barLeft, barHeight = p.rankHeight * scale;
            float barTop = top + (box - barHeight) * .5f;
            if (barWidth <= 0f) return;
            painter.RoundedBox(look.Hud.corner, barLeft - frame, barTop - frame, barWidth + frame * 2f, barHeight + frame * 2f, ownBox);
            rankBar.Step(view.Share, now, look.Bar);
            heldShown = Smooth.Lerp(look.Bar.follow, heldShown, view.StoredShare);
            killsShown = Smooth.Lerp(look.Bar.follow, killsShown, kills);
            float total = Mathf.Clamp01(rankBar.Fill);
            Fill(barLeft, barTop, barWidth, barHeight, p.track);
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
                p.game, TextAlign.Center, TextAlign.Bottom, outlineWidth, outline);
            painter.AlphaScale = 1f;
        }
    }
}
