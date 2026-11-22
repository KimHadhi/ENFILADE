using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    // cl_history.lua: what was picked up, one row a thing ("NAME*count" after its picture in a faint box), the oldest at
    // the foot and the newer stacking up, each easing in, its count rolling up, and fading after duration + life x its
    // place. The same thing again adds to its row's count.
    public sealed partial class PlayerHud
    {
        private sealed class Record
        {
            public string Name, Line, Measure;
            public Texture Icon;
            public int Amount, Number;
            public float Shown, Start, Alpha, Rise;
        }

        private readonly List<Record> records = new List<Record>();
        private readonly Stack<Record> spareRecords = new Stack<Record>();

        /// <summary>
        /// AddPickupRecord: a new row, or more on the row of the same name. The addon kept a row's start when more came, so
        /// a thing taken again still left on its first taking's time (its "life = 3" refresh set a number that never
        /// changes); here more of it starts its time again.
        /// </summary>
        internal void AddRecord(string name, Texture icon, int amount)
        {
            if (string.IsNullOrEmpty(name)) return;
            float time = Smooth.Now;
            foreach (var record in records)
            {
                if (record.Name != name) continue;
                record.Amount += amount;
                record.Start = time;
                return;
            }
            var made = spareRecords.Count > 0 ? spareRecords.Pop() : new Record();
            made.Name = name;
            made.Icon = icon != null ? icon : crate;
            made.Amount = amount;
            // The addon's rolling count was kept by name for good, so the same thing taken a while later rolled from the
            // last total; a new row rolls up from nothing.
            made.Shown = 0f;
            made.Number = -1;
            made.Start = time;
            made.Alpha = made.Rise = 0f;
            records.Add(made);
        }

        /// <summary>HUDAmmoPickedUp: rounds of a gun's ammunition, named and pictured by the ammunition (Weapons.csv ammo).</summary>
        internal void PickedAmmo(WeaponData gun, int rounds)
        {
            int icon = look.AmmoIcon.Of(gun.Ammunition);
            AddRecord(Upper(AmmoKinds.NameOf(gun) ?? gun.DisplayName), ammoPictures[icon >= 0 && icon < ammoPictures.Length ? icon : 0], rounds);
        }

        /// <summary>HUDAmmoPickedUp("Grenade", n): the class throwable, by its name, with the grenade picture.</summary>
        internal void PickedThrowables(int count)
        {
            var grenades = PlayerGrenades.Local;
            var info = grenades != null ? Throwables.Of(grenades.Kind) : null;
            string name = info != null && !string.IsNullOrEmpty(info.Name) ? info.Name : Texts.Get("hud.playerhud.throwable");
            int icon = Mathf.Clamp(look.AmmoIcon.grenade, 0, ammoPictures.Length - 1);
            AddRecord(Upper(name), ammoPictures[icon], count);
        }

        /// <summary>HUDItemPickedUp / HUDWeaponPickedUp: one of a thing, with the crate picture.</summary>
        internal void PickedThing(string name) => AddRecord(Upper(name), crate, 1);

        private void DrawHistory(PlayerHudOptions o)
        {
            var h = look.History;
            float x = sw * look.Place.history.x + swayX + o.historyX, y = sh * look.Place.history.y - swayY + o.historyY;
            float follow = Smooth.Factor(h.follow);
            Color32 colour = Rgb(o.historyColor);
            painter.AlphaScale = Opacity(o.historyColor);
            float nearest = float.PositiveInfinity, farthest = float.NegativeInfinity, top = float.PositiveInfinity, foot = float.NegativeInfinity;
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                int place = i + 1;
                record.Shown = Smooth.Lerp(h.follow, record.Shown, record.Amount);
                int number = Digits.Round(record.Shown);
                if (number != record.Number)
                {
                    record.Number = number;
                    string count = Digits.Of(number);
                    // Measured with two spaces between, drawn with a star: as the addon does.
                    record.Measure = record.Name + "  " + count;
                    record.Line = record.Name + "*" + count;
                }
                Vector2 size = historyFont.Measure(record.Measure);
                float w = size.x, tall = size.y;
                float rx = x, ry = y - tall * h.rise - record.Rise;
                float fade = 1f + Mathf.Clamp((h.shown - place) * h.fadeStep, -1f, 0f);
                record.Rise = Mathf.LerpUnclamped(record.Rise, place * (tall + h.gap * scale), follow);
                float alpha = record.Alpha * fade;
                painter.RoundedBox(look.Hud.corner, rx - w * h.padLeft, ry - tall * h.boxTop, w * h.boxWidth, tall * h.boxHeight,
                    Gm.WithAlpha(background, h.boxAlpha * alpha));
                painter.OutlinedText(record.Line, historyFont, rx + h.textShift * scale, ry, Gm.WithAlpha(colour, 255f * alpha),
                    TextAlign.Left, TextAlign.Center, outlineWidth, Gm.WithAlpha(outline, outline.a * alpha));
                painter.DrawColor = Gm.WithAlpha(colour, 255f * alpha);
                painter.Picture = record.Icon;
                painter.TexturedRectRotated(rx, ry + h.iconDrop * scale, h.icon * scale, h.icon * scale, 0f);
                nearest = Mathf.Min(nearest, rx - w * h.padLeft);
                farthest = Mathf.Max(farthest, rx - w * h.padLeft + w * h.boxWidth);
                top = Mathf.Min(top, ry - tall * h.boxTop);
                foot = Mathf.Max(foot, ry - tall * h.boxTop + tall * h.boxHeight);
                bool over = now - record.Start > h.duration + h.life * place;
                record.Alpha = Mathf.LerpUnclamped(record.Alpha, over ? 0f : 1f, follow);
            }
            if (records.Count > 0) HudStage.Mark(HudPart.History, nearest, top, farthest - nearest, foot - top);
            // Gone rows leave after the loop: the addon took them out mid-loop, so the next row skipped a frame (a blink).
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];
                if (record.Alpha > .01f || now - record.Start <= h.duration + h.life * (i + 1)) continue;
                records.RemoveAt(i);
                spareRecords.Push(record);
            }
            painter.AlphaScale = 1f;
        }
    }
}
