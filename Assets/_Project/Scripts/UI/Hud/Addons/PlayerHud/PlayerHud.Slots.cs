using UnityEngine;

namespace HudAddons
{
    // Our weapon slots drawn as cl_selector.lua draws its boxes (the user, 2026-11-22: "무기 슬롯 1,2,3,4번 그거는 기존
    // 방식이 더 맘에 들어. 기존 방식으로 디자인만 바꿔서"). Kept from our slots (WeaponSlotsHUD): which slots there are (1-4,
    // the class knife in the melee slot), that the one in hand is the chosen one, and when they show: for 3 s after a switch,
    // a pickup, a refill or an ammo change (PlayerWeaponSlots.SlotActivity), then a 1 s fade. Not kept: the selector's own
    // choosing by scroll and click, its opening on a key and its confirm. From the selector: a 450 x 150 box a weapon side
    // by side from the screen's middle, the row sliding so the chosen one stands there, boxes dimming with their distance
    // from it, the name over the middle in capitals, "rounds/reserve" under it (a melee weapon "1"), the ammo picture in
    // the top left corner; the chosen box in the selector's colour with black text, the rest faint black with text in its
    // colour, any at 30 % of its magazine or under in red; the "Select" sound when the chosen slot changes.
    public sealed partial class PlayerHud
    {
        /// <summary>One slot's eased colours and its texts, kept while they say the same.</summary>
        private sealed class SlotLook
        {
            public readonly Fade Text = new Fade(), Box = new Fade();
            public string NameRaw, Name = string.Empty, Rounds = string.Empty;
            public int Clip = -1, Reserve = -1;
        }

        private static readonly Color32 Black = new Color32(0, 0, 0, 255);
        private readonly SlotLook[] slotLooks = new SlotLook[PlayerWeaponSlots.SlotCount];
        private float slotsShift, slotsShownAt = float.NegativeInfinity;

        private void OnSlotActivity() => slotsShownAt = Smooth.Now;

        // cl_selector.lua plays "Select" (Nene.HUD/select.wav, pitch 80-110) as the choice moves; here the choice is the
        // slot taken in hand.
        private void OnSelectionChanged(int slot)
        {
            var o = Options;
            if (o == null || !o.enabled || !o.selector || !isActiveAndEnabled) return;
            var s = look.Slots;
            Sound.Play("PlayerHud/Select", s.pitchMin, s.pitchMax, s.volume);
        }

        private void DrawSlots(PlayerHudOptions o)
        {
            if (!o.selector || slots == null) return;
            var s = look.Slots;
            float age = now - slotsShownAt;
            float shown = age <= s.visibleSeconds ? 1f : s.fadeSeconds <= 0f ? 0f : 1f - Mathf.Clamp01((age - s.visibleSeconds) / s.fadeSeconds);
            float x0 = sw * look.Place.selector.x + o.selectorX + swayX, y0 = sh * look.Place.selector.y + o.selectorY - swayY;
            float width = s.width * scale, height = s.height * scale, step = width + s.gap * scale;
            int inHand = slots.SelectedSlot, column = 0, chosen = -1;
            for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            {
                if (!TryHeld(i, out _)) continue;
                if (i == inHand) chosen = column;
                column++;
            }
            if (chosen >= 0) slotsShift = Smooth.Lerp(s.follow, slotsShift, chosen * step);
            Color32 colour = Rgb(o.selectorColor);
            float opacity = Opacity(o.selectorColor);
            column = 0;
            for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            {
                if (!TryHeld(i, out var held)) continue;
                var slot = slotLooks[i] ??= new SlotLook();
                float x = x0 + column * step - slotsShift;
                column++;
                bool picked = i == inHand, red = held.Clip <= held.MaxClip * s.low;
                // The colours ease every frame, shown or not, as the addon's do (it draws its boxes at alpha 0).
                var text = slot.Text.Step(red ? low : picked ? Black : colour, s.follow);
                var box = slot.Box.Step(picked ? Gm.WithAlpha(colour, s.heldAlpha) : Gm.WithAlpha(Black, s.idleAlpha), s.follow);
                if (shown <= 0f) continue;
                // The addon dimmed a box by its distance from the screen's middle, so an offset selector dimmed whole;
                // here from the selector's own place, where the chosen box stands.
                float dim = Mathf.Clamp01(1f - Mathf.Abs(x - x0) / screenWidth * s.falloff);
                painter.AlphaScale = dim * shown * opacity;
                Words(slot, held);
                painter.RoundedBox(look.Hud.corner, x - width * .5f, y0 - height * .5f, width, height, box);
                painter.OutlinedText(slot.Name, selectorFont, x, y0, text, TextAlign.Center, TextAlign.Bottom, outlineWidth, outline);
                painter.OutlinedText(held.Melee ? "1" : slot.Rounds, ammoFont, x, y0, text, TextAlign.Center, TextAlign.Top, outlineWidth, outline);
                painter.DrawColor = Gm.WithAlpha(text, s.iconAlpha);
                painter.Picture = ammoPictures[held.Icon >= 0 && held.Icon < ammoPictures.Length ? held.Icon : 0];
                painter.TexturedRectRotated(x - width * .5f + height / 6f, y0 - height * .5f + height / 5f, height / 3f, height / 3f, 0f);
            }
            painter.AlphaScale = 1f;
        }

        // The slot's name in capitals and its "rounds/reserve", made again only when they change.
        private static void Words(SlotLook slot, in Held held)
        {
            if (held.Name != slot.NameRaw)
            {
                slot.NameRaw = held.Name;
                slot.Name = Upper(held.Name ?? string.Empty);
            }
            if (held.Melee || (held.Clip == slot.Clip && held.Reserve == slot.Reserve)) return;
            slot.Clip = held.Clip;
            slot.Reserve = held.Reserve;
            slot.Rounds = Digits.Of(held.Clip) + "/" + Digits.Of(held.Reserve);
        }
    }
}
