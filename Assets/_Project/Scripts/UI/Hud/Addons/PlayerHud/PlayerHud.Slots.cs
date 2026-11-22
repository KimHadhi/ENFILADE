using UnityEngine;

namespace HudAddons
{
    // Our weapon slots in the place and order they always had, drawn as cl_selector.lua draws its boxes (the user,
    // 2026-11-22: "무기 슬롯 1,2,3,4번 그거는 기존 방식이 더 맘에 들어. 기존 방식으로 디자인만 바꿔서", and of where:
    // "예전 자리(오른쪽)").
    // Kept from our slots (WeaponSlotsHUD, its default look A "조각 판", Tables/WeaponSlots.csv Plates): a column of the four
    // slots at the screen's right, slot 1 on top, its right end 70 px in from the edge and its middle 14 px over the
    // screen's; 240 x 60 boxes 14 px apart, each standing its own distance out (0, 10, 3, 14 px); the slot in hand growing
    // to 80 tall on a spring and kicking 14 px out and back as it is taken; the knife in the melee slot when it holds no
    // weapon, an empty slot saying so; shown for 3 s after a switch, a pickup, a refill or an ammo change
    // (PlayerWeaponSlots.SlotActivity), then a 1 s fade; the room it takes on the right edge (HudRoom), so a callout
    // window steps clear of it.
    // From the selector: each box and what is in it: the name over the box's middle in capitals, "rounds/reserve" under
    // it (a melee weapon "1"), the ammo picture in its top left corner; the slot in hand in the selector's colour at alpha
    // 200 with black text, the others faint black with text in its colour, one at 30 % of its magazine or under in red;
    // the "Select" sound as the slot in hand changes.
    // Not taken from either: the selector's row across the screen's middle that slides to the chosen box and dims the
    // others by their distance, its choosing by scroll and click; the plates' cut corners, leans, number tabs, pink
    // edges, white flash, weapon pictures and name lines (the selector's box is their look now).
    public sealed partial class PlayerHud
    {
        /// <summary>One slot's eased colours, its spring and kick, and its texts, kept while they say the same.</summary>
        private sealed class SlotLook
        {
            public readonly Fade Text = new Fade(), Box = new Fade();
            public string NameRaw, Name = string.Empty, Rounds = string.Empty;
            public int Clip = -1, Reserve = -1;
            public float On, Speed, PoppedAt = float.NegativeInfinity;
        }

        private static readonly Color32 Black = new Color32(0, 0, 0, 255);
        private readonly SlotLook[] slotLooks = new SlotLook[PlayerWeaponSlots.SlotCount];
        private float slotsShownAt = float.NegativeInfinity;
        // The slot in hand last frame; -2 before the first look, when nothing kicks.
        private int slotsInHand = -2;
        private string emptySlot;

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
            if (!o.selector || slots == null)
            {
                HudRoom.Free(this);
                return;
            }
            var s = look.Slots;
            int count = PlayerWeaponSlots.SlotCount, inHand = slots.SelectedSlot;
            for (int i = 0; i < count; i++) slotLooks[i] ??= new SlotLook();
            if (inHand != slotsInHand)
            {
                if (slotsInHand != -2 && inHand >= 0 && inHand < count) slotLooks[inHand].PoppedAt = now;
                slotsInHand = inHand;
            }
            float size = o.scale, gap = s.gap * size, total = gap * (count - 1);
            for (int i = 0; i < count; i++)
            {
                Spring(slotLooks[i], i == inHand ? 1f : 0f, dt, s.grow);
                total += Mathf.LerpUnclamped(s.height, s.heightHeld, slotLooks[i].On) * size;
            }
            float age = now - slotsShownAt;
            float shown = age <= s.visibleSeconds ? 1f : s.fadeSeconds <= 0f ? 0f : 1f - Mathf.Clamp01((age - s.visibleSeconds) / s.fadeSeconds);
            float right = screenWidth - s.right + o.selectorX + swayX, top = sh + s.middle - total * .5f + o.selectorY - swayY;
            Color32 colour = Rgb(o.selectorColor);
            painter.AlphaScale = shown * Opacity(o.selectorColor);
            float near = float.PositiveInfinity, far = float.NegativeInfinity, highest = top, lowest = top;
            for (int i = 0; i < count; i++)
            {
                var slot = slotLooks[i];
                float width = Mathf.LerpUnclamped(s.width, s.widthHeld, slot.On) * size;
                float height = Mathf.LerpUnclamped(s.height, s.heightHeld, slot.On) * size;
                float end = right + ((s.shift != null && i < s.shift.Length ? s.shift[i] : 0f) + Kick(slot, s)) * size;
                float left = end - width, x = end - width * .5f, y = top + height * .5f;
                top += height + gap;
                bool any = TryHeld(i, out var held), picked = i == inHand;
                bool red = any && held.Clip <= held.MaxClip * s.low;
                // The colours ease every frame, shown or not, as the addon's do (it draws its boxes at alpha 0).
                var text = slot.Text.Step(red ? low : picked ? Black : colour, s.follow);
                var box = slot.Box.Step(picked ? Gm.WithAlpha(colour, s.heldAlpha) : Gm.WithAlpha(Black, s.idleAlpha), s.follow);
                if (shown <= 0f) continue;
                near = Mathf.Min(near, screenWidth - end);
                far = Mathf.Max(far, screenWidth - left);
                lowest = y + height * .5f;
                painter.RoundedBox(look.Hud.corner, left, y - height * .5f, width, height, box);
                if (!any)
                {
                    // An empty slot keeps its place and says so, as our slots did, in the selector's words' look.
                    emptySlot ??= Texts.Get("hud.weapon_slot.empty");
                    painter.OutlinedText(emptySlot, ammoFont, x, y, text, TextAlign.Center, TextAlign.Center, outlineWidth, outline);
                    continue;
                }
                Words(slot, held);
                painter.OutlinedText(slot.Name, selectorFont, x, y, text, TextAlign.Center, TextAlign.Bottom, outlineWidth, outline);
                painter.OutlinedText(held.Melee ? "1" : slot.Rounds, ammoFont, x, y, text, TextAlign.Center, TextAlign.Top, outlineWidth, outline);
                painter.DrawColor = Gm.WithAlpha(text, s.iconAlpha);
                painter.Picture = ammoPictures[held.Icon >= 0 && held.Icon < ammoPictures.Length ? held.Icon : 0];
                painter.TexturedRectRotated(left + height / 6f, y - height * .5f + height / 5f, height / 3f, height / 3f, 0f);
            }
            painter.AlphaScale = 1f;
            // While they show, the room they take on the right edge (px in from it and down from the top), as our slots took it.
            if (shown > .02f && far > near) HudRoom.Take(this, near, far, highest, lowest);
            else HudRoom.Free(this);
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

        // Our slots' spring toward 1 for the slot in hand and 0 for the rest: there in about `seconds`, a little past and
        // back (the drafts' cubic-bezier(.2, .9, .3, 1.2)), as WeaponSlotsHUD ran it.
        private static void Spring(SlotLook slot, float target, float dt, float seconds)
        {
            float omega = 4.2f / Mathf.Max(.03f, seconds);
            dt = Mathf.Min(dt, .05f);
            slot.Speed += ((target - slot.On) * omega * omega - slot.Speed * 1.2f * omega) * dt;
            slot.On += slot.Speed * dt;
        }

        // Our slots' kick as one is taken in hand: out to -kick, back past its place to +3 px at 55 %, home by the end.
        private float Kick(SlotLook slot, PlayerHudLook.SlotsRow s)
        {
            float k = (now - slot.PoppedAt) / Mathf.Max(.01f, s.kickSeconds);
            if (k < 0f || k >= 1f || s.kick <= 0f) return 0f;
            return k < .55f ? Mathf.Lerp(-s.kick, 3f, Ease.Out(k / .55f)) : Mathf.Lerp(3f, 0f, (k - .55f) / .45f);
        }
    }
}
