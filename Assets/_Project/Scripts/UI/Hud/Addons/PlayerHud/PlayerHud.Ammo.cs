using UnityEngine;

namespace HudAddons
{
    // cl_ammo.lua: the weapon's name, its magazine box (ammo picture, count over a faint "000"), the reserve box, the shot's
    // light, and the grenade box above (the "eqp" options).
    public sealed partial class PlayerHud
    {
        /// <summary>
        /// A slot's weapon as cl_ammo.lua and cl_selector.lua read one (GetWeaponData): its name, the rounds in it, its
        /// magazine, the reserve of its ammunition, its ammo picture. A melee weapon has no ammo (ammo type -1): one round
        /// of one, nothing in reserve, the crate's picture.
        /// </summary>
        private struct Held
        {
            public object Key;
            public string Name;
            public int Clip, MaxClip, Reserve, Icon;
            public bool Melee;
        }

        // The class knife in the melee slot, standing in a weapon's place (MeleeKnife), as one key.
        private static readonly object KnifeKey = new object();

        private readonly Fade magsColour = new Fade(), reserveColour = new Fade(), fragColour = new Fade();
        // Nene.Lerp('mags') / ('ammo'): the counts roll to the new number; both start at nothing.
        private float clipShown, reserveShown;
        // The shot's light (Nenefireflick): which weapon it watches and its last count; -1 until a count first changes.
        private object flickKey;
        private int flickClip;
        private float flick = -1f, glow;
        private string ammoName, ammoNameSpaced = string.Empty;
        // Y: the gun is looked over. A rank it reached since its last look-over is announced as it begins (its line for a
        // moment), then its rank, kills and headshots, in place of its name (the user, 2026-11-12: "킬스트릭 스탯은 무기
        // 관찰할 때만 뜨게 할 것"; the kill plate on the gun is GunKillPlate's).
        private const float RankUpHold = 2.5f;
        private WeaponController weapon;
        private WeaponData rankUpGun;
        private string rankUpText;
        private float rankUpUntil;

        private void OnRankUpAnnounced(WeaponData gun)
        {
            if (gun == null) return;
            rankUpText = Texts.Format("gunkills.rankup", GunKills.Title(gun));
            rankUpGun = gun;
            rankUpUntil = Smooth.Now + RankUpHold;
        }

        private string LookOverLine()
        {
            if (HudStage.On) return null;
            if (weapon == null) slots.TryGetComponent(out weapon);
            var runtime = slots.GetRuntime(slots.SelectedSlot);
            if (weapon == null || runtime == null || runtime.Data.Category == WeaponCategory.Melee
                || !weapon.IsInspecting || runtime.IsReloading)
            {
                rankUpUntil = 0f;
                return null;
            }
            GunKills.AnnounceRankUp(runtime.Data);
            if (Smooth.Now < rankUpUntil && rankUpGun == runtime.Data) return rankUpText;
            return Texts.Format("gunkills.inspect", GunKills.Title(runtime.Data), GunKills.Kills(runtime.Data), GunKills.Headshots(runtime.Data));
        }

        private bool TryHeld(int slot, out Held held)
        {
            if (HudStage.On) return StageHeld(out held);
            held = default;
            if (slots == null || slot < 0 || slot >= PlayerWeaponSlots.SlotCount) return false;
            var data = slots.GetWeapon(slot);
            if (data != null)
            {
                held.Key = data;
                held.Name = data.DisplayName;
                var runtime = data.Category == WeaponCategory.Melee ? null : slots.GetRuntime(slot);
                if (runtime == null) return Melee(ref held);
                held.Clip = runtime.Ammo;
                held.MaxClip = Mathf.Max(1, data.MagazineSize);
                held.Reserve = runtime.ReserveAmmo;
                held.Icon = look.AmmoIcon.Of(data.Ammunition);
                return true;
            }
            if (slot != MeleeSlot || player == null) return false;
            held.Key = KnifeKey;
            held.Name = MeleeKnife.SlotName(player);
            return Melee(ref held);
        }

        private static bool Melee(ref Held held)
        {
            held.Melee = true;
            held.Clip = held.MaxClip = 1;
            held.Reserve = 0;
            held.Icon = 0;
            return true;
        }

        private void DrawAmmo(PlayerHudOptions o)
        {
            var a = look.Ammo;
            float x = sw * look.Place.ammo.x + swayX, y = sh * look.Place.ammo.y - swayY;
            float magW = a.magWidth * scale, magH = a.magHeight * scale, resW = a.reserveWidth * scale, resH = a.reserveHeight * scale;
            if (o.weapon && (slots != null || HudStage.On) && TryHeld(slots != null ? slots.SelectedSlot : 0, out var held))
            {
                painter.AlphaScale = Opacity(o.weaponColor);
                float ax = x + o.weaponX, ay = y + o.weaponY;
                // A count that went down on the same weapon is a shot: its light comes up; one that went up puts it out.
                if (ReferenceEquals(held.Key, flickKey))
                {
                    if (flickClip != held.Clip)
                    {
                        flick = flick >= 0f && flickClip > held.Clip ? 1f : 0f;
                        flickClip = held.Clip;
                    }
                }
                else
                {
                    flickKey = held.Key;
                    flickClip = held.Clip;
                }
                Color32 colour = Rgb(o.weaponColor);
                var reserveTint = reserveColour.Step(!held.Melee && held.Reserve <= held.MaxClip ? low : colour, look.Colour.follow);
                var magsTint = magsColour.Step(!held.Melee && held.Clip <= held.MaxClip * a.low ? low : colour, look.Colour.follow);
                clipShown = Smooth.Lerp(a.countFollow, clipShown, held.Clip);
                reserveShown = Smooth.Lerp(a.countFollow, reserveShown, held.Reserve);
                int clip = Digits.Round(clipShown);
                if (flick >= 0f)
                {
                    // The addon's light jumped to full at every shot and faded in a third of a second, a flash per shot;
                    // under the photosensitivity rule the drawn light eases after it, so a burst glows and a shot swells.
                    flick = Mathf.Clamp01(flick - dt * a.flickFade);
                    glow = a.flickFollow > 0f ? Smooth.Lerp(a.flickFollow, glow, flick) : flick;
                    float share = clip / (float)held.MaxClip;
                    painter.RoundedBox(look.Hud.corner, ax - magW * .5f, ay - magH * .5f, magW, magH,
                        Gm.Rgba(colour.r, colour.g * share, colour.b * share, a.flickAlpha * glow));
                }
                var icon = ammoPictures[held.Icon >= 0 && held.Icon < ammoPictures.Length ? held.Icon : 0];
                MagBox(icon, Digits.Of(clip), magsFont, ax, ay, magW, magH, magsTint, TextAlign.Right, TextAlign.Center);
                if (held.Name != ammoName)
                {
                    ammoName = held.Name;
                    ammoNameSpaced = Spaced(held.Name);
                }
                string line = LookOverLine();
                painter.OutlinedText(line ?? ammoNameSpaced, labelFont, ax - magW * a.nameLeft, ay - magW * a.nameUp, line != null ? loss : magsTint,
                    TextAlign.Left, TextAlign.Top, outlineWidth, outline);
                TextBox(Digits.Of(Digits.Round(reserveShown)), ammoFont, ax + (magW + resW) / a.reserveSpread, ay, resW, resH,
                    Gm.WithAlpha(reserveTint, a.reserveAlpha), TextAlign.Center, TextAlign.Center);
                // The boxes stay under the right edge's column of callout windows, so those keep above them (HudRoom; on a 21:9
                // screen a second window came down onto the ammo).
                float roomRight = ax + (magW + resW) / a.reserveSpread + resW * .5f, roomTop = ay - magW * a.nameUp;
                HudStage.Mark(HudPart.Ammo, ax - magW * .5f, roomTop, roomRight - ax + magW * .5f, ay + magH * .5f - roomTop);
                if (o.eqp) roomTop = Mathf.Min(roomTop, y + o.eqpY - a.eqp * scale * (a.eqpRise + .5f));
                HudRoom.Take(this, screenWidth - roomRight, screenWidth - (ax - magW * .5f), roomTop, ay + magH * .5f, HudRoom.Way.Over);
            }
            if (o.eqp)
            {
                painter.AlphaScale = Opacity(o.eqpColor);
                float size = a.eqp * scale;
                var grenades = PlayerGrenades.Local;
                int count = HudStage.On ? HudStage.Grenades : grenades != null ? grenades.Count : 0;
                Color32 colour = Rgb(o.eqpColor);
                var tint = fragColour.Step(count <= 0 ? Dim(colour) : colour, look.Colour.follow);
                int picture = Mathf.Clamp(look.AmmoIcon.grenade, 0, ammoPictures.Length - 1);
                // As the addon places it: over the reserve box, (reserve - box) x Scale / 2 to its right.
                float gx = x + o.eqpX + (magW + resW) / a.reserveSpread + (resW - size) * scale * .5f, gy = y + o.eqpY - size * a.eqpRise;
                HudStage.Mark(HudPart.Grenade, gx - size * .5f, gy - size * .5f, size, size);
                IconBox(ammoPictures[picture], Digits.Of(count), altsFont, gx, gy, size, size, tint, TextAlign.Right, TextAlign.Top);
            }
            painter.AlphaScale = 1f;
        }
    }
}
