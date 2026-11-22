using UnityEngine;

namespace HudAddons
{
    // The HUD settings' stage (HudStage): the same drawing with sample numbers, the pickup rows and hazard boxes kept up,
    // hits sent in for the parts that show one. Nothing of the player is read while it is up.
    public sealed partial class PlayerHud
    {
        private const float ArcEvery = 1.4f, FlareEvery = 1.8f, GlitchEvery = 2f;
        private static readonly float[] StageBearings = { 40f, -115f, 170f, -35f };
        // The stage's gun as a weapon's key (TryHeld), so its count going down lights the box as a shot does.
        private static readonly object StageKey = new object();

        private bool staged;
        private int stageArc, stageFlare;
        private string stageGun;
        private int stageGunIcon;

        private float HealthMost => HudStage.On || receiver == null ? 100f : Mathf.Max(1f, receiver.MaximumHealth);
        private float HealthNow => HudStage.On ? HudStage.Health * HealthMost : receiver.Health;
        private float ShieldNow => HudStage.On ? HudStage.Armor * Mathf.Max(1f, look.Status.armorMost) : receiver.Shield;

        // One frame of the stage, before the HUD draws.
        private void Stage(PlayerHudOptions o)
        {
            if (staged != HudStage.On)
            {
                staged = HudStage.On;
                LeaveBehind();
            }
            if (!staged) return;
            AddHazard(HazardFall, fallPicture);
            AddHazard(HazardBlast, blastPicture);
            if (records.Count == 0)
            {
                AddRecord(Upper(Texts.Get("settings.hudedit.sample.ammo")), ammoPictures[Mathf.Clamp(stageGunIcon, 0, ammoPictures.Length - 1)], 60);
                AddRecord(Upper(Texts.Get("settings.hudedit.sample.item")), crate, 1);
                AddRecord(Upper(Texts.Get("settings.hudedit.sample.money")), crate, 120);
            }
            foreach (var record in records) record.Start = now;
            // Health and armour stay up whatever "only when hurt" says, but for the moment it is turned on.
            hideUntil = o.hide && now - HudStage.HideShownAt < look.Hud.hideSeconds + 2f ? HudStage.HideShownAt + look.Hud.hideSeconds : now + 1f;
            switch (HudStage.Chosen)
            {
                case HudPart.Sense when HudStage.At(ArcEvery, .1f):
                    var s = look.Sense;
                    arcs[stageArc % MostArcs] = new Arc
                    {
                        Source = -1 - stageArc, Live = true, Thickness = 1f, Life = 1f, Staged = true,
                        Bearing = StageBearings[stageArc % StageBearings.Length],
                        Colour = stageArc % 3 == 2 ? s.blast : stageArc % 3 == 1 ? s.shielded : s.hit,
                    };
                    stageArc++;
                    break;
                case HudPart.Flare when HudStage.At(FlareEvery, .1f):
                    Flare(stageFlare++ % 3, o);
                    break;
                case HudPart.Fx when HudStage.At(GlitchEvery, .2f) && o.glitch:
                    fx.Glitch(look.Fx, now, screenHeight > 0f ? screenHeight : hud.Height);
                    break;
            }
        }

        // Nothing sampled stays for the game, and nothing of the game's is taken for a hit or a loss on the stage.
        private void LeaveBehind()
        {
            hazards.Clear();
            records.Clear();
            for (int i = 0; i < arcs.Length; i++) arcs[i].Live = false;
            vignetteLeft = shakeLeft = 0f;
            lastHealth = float.NaN;
            hideUntil = float.NegativeInfinity;
            flick = -1f;
            flickKey = null;
            magazinePrimed = false;
            changeAt = -99f;
            ammoName = null;
            if (receiver == null && !staged) return;
            healthBar.Snap(HealthNow / HealthMost);
            armorBar.Snap(ShieldNow / Mathf.Max(1f, look.Status.armorMost));
            clipShown = staged ? HudStage.Clip : clipShown;
            reserveShown = staged ? HudStage.Reserve : reserveShown;
        }

        // The sway a turning view would give, while its options are being changed.
        private void StageSway(PlayerHudOptions o)
        {
            bool shown = o.sway && now < HudStage.SwayUntil;
            swayX = shown ? Mathf.Sin(now * 2.4f) * 7f * o.swayIntensity : 0f;
            swayY = shown ? Mathf.Sin(now * 1.7f) * 4f * o.swayIntensity : 0f;
        }

        // The gun the stage shows: the one in hand, else the first the body can hold.
        private bool StageHeld(out Held held)
        {
            if (stageGun == null)
            {
                stageGun = "";
                var assets = FPSBodyAssets.Instance;
                if (assets != null)
                    foreach (var entry in assets.guns)
                    {
                        if (entry.weapon == null || entry.weapon.Category == WeaponCategory.Melee) continue;
                        stageGun = entry.weapon.DisplayName;
                        stageGunIcon = look.AmmoIcon.Of(entry.weapon.Ammunition);
                        break;
                    }
            }
            held = new Held
            {
                Key = StageKey, Name = stageGun, Clip = HudStage.Clip, MaxClip = HudStage.Magazine, Reserve = HudStage.Reserve, Icon = stageGunIcon,
            };
            return true;
        }
    }
}
