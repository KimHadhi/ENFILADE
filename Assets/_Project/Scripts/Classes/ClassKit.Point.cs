using UnityEngine;

// ClassKit: Point: the gun put away reloads itself. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    private void ReloadAside()
    {
        if (slots == null) return;
        for (int slot = 0; slot < 2; slot++)
        {
            var data = slots.GetWeapon(slot);
            var gun = data != null ? slots.GetRuntime(slot) : null;
            bool waiting = gun != null && slots.SelectedSlot != slot && gun.Ammo < data.MagazineSize && gun.ReserveAmmo > 0;
            if (!waiting) { asideSince[slot] = -1f; continue; }
            if (asideSince[slot] < 0f) asideSince[slot] = Time.time;
            if (Time.time - asideSince[slot] < AsideTime(gun)) continue;
            asideSince[slot] = -1f;
            if (!gun.ReloadAside()) continue;
            refilledAt[slot] = Time.time;
            slots.NotifyAmmo();
        }
    }

    // As long as that gun's own reload would take from where it is (shell by shell for a pump), times the table's share.
    private static float AsideTime(WeaponRuntime gun)
    {
        var data = gun.Data;
        float time = data.ShellReload
            ? data.ReloadDuration + data.ShellInsertDuration * Mathf.Max(1, data.MagazineSize - gun.Ammo) + data.ShellEndDuration
            : gun.Ammo <= 0 ? data.EmptyReloadDuration : data.ReloadDuration;
        return Mathf.Max(.05f, time * Mathf.Max(0f, PlayerClass.Ability(PointRow, "reloadShare", 1f)));
    }

    // The class key: for some seconds a gun comes out at once and full (its reserve untouched), and running doesn't
    // spread its shots (WeaponController asks PlayerClass.DrawFactor and MovingSpreadFactor). Refused while it waits.
    private void GunKata()
    {
        if (Time.time < kataReadyAt) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        kataTotal = Mathf.Max(.5f, PlayerClass.Ability(PointRow, "kataSeconds", 8f));
        kataWait = Mathf.Max(kataTotal, PlayerClass.Ability(PointRow, "kataCooldown", 35f));
        kataUntil = Time.time + kataTotal;
        kataReadyAt = Time.time + kataWait;
        // The gun in hand comes up full too, as if just drawn.
        if (slots != null && slots.SelectedSlot >= 0 && slots.SelectedSlot < 2)
        {
            var gun = slots.GetWeapon(slots.SelectedSlot) != null ? slots.GetRuntime(slots.SelectedSlot) : null;
            if (gun != null) { gun.TopUp(); slots.NotifyAmmo(); }
        }
        KitSound.Play(KitSound.Cue.Charge, .7f);
        AlertHUD.Self(Texts.Get("class.point.kata.self"));
        MomentHUD.Kata(kataTotal);
        // Its aura for everyone: gold rounds turning round the Point while it lasts (MomentFx.KataAura).
        CardNet.Show(CardFx.Kind.KataAura, transform.position, transform.forward, kataTotal);
    }
}
