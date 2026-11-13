using UnityEngine;

// ShopMenu: Buying. One part of the class; the rest is in ShopMenu.cs and the files beside it.
public sealed partial class ShopMenu
{
    private void Buy(Row row)
    {
        if (!open || reveal.Closing || player == null) return;
        var item = row.Item;
        string why = Refusal(item);
        if (why == null && !Wallet.TrySpend(item.Price)) why = Texts.Get("shop.refuse.money");
        if (why == null && !Hand(item)) { Wallet.Unspend(item.Price); why = Texts.Get("shop.refuse.now"); }
        if (why != null)
        {
            Say(why, KitStyle.Red);
            Pulse(row, new Color(KitStyle.Red.r, KitStyle.Red.g, KitStyle.Red.b, .35f));
            KitSound.Play(KitSound.Cue.Refuse);
            return;
        }
        string paid = "-" + Wallet.Format(item.Price);
        Say(Texts.Format("shop.bought", item.Name, paid), KitStyle.Accent);
        Pulse(row, KitStyle.Flash);
        row.BoughtAt = spentAt = Time.unscaledTime;
        spent.text = paid;
        KitPulse.Play(moneyFlash, KitStyle.Flash, .45f);
        KitSound.Play(KitSound.Cue.Buy);
        CombatFeedHUD.Notice(player, Texts.Format("shop.bought.feed", item.Name, paid), false);
    }

    // A card lights up again and drains, pink for a buy, red for a refusal.
    private static void Pulse(Row row, Color color)
    {
        row.FlashAt = Time.unscaledTime;
        row.FlashColor = color;
    }

    // Why it can't be bought now, or null.
    private string Refusal(Item item)
    {
        var slots = player.GetComponent<PlayerWeaponSlots>();
        var receiver = player.DamageReceiver;
        switch (item.Kind)
        {
            case Kind.Gun:
                if (item.Weapon == null || slots == null) return Texts.Get("shop.refuse.unsold");
                if (Holds(slots, item.Weapon)) return Texts.Get("shop.refuse.owned");
                if (player.CombatInputLocked) return Texts.Get("shop.refuse.now");
                return null;
            case Kind.Ammo:
                return slots == null || !PlayerWeaponSlots.HoldsAnyGun(slots) ? Texts.Get("shop.refuse.nogun") : !slots.NeedsAmmo() ? Texts.Get("shop.refuse.ammofull") : null;
            case Kind.Heal:
                return receiver.Health >= receiver.MaximumHealth - .5f ? Texts.Get("shop.refuse.healthfull") : null;
            case Kind.Shield:
                return receiver.Shield >= item.Amount - .5f ? Texts.Get("shop.refuse.shield") : null;
            case Kind.Syringe:
                return DetenteKit.Local == null ? Texts.Get("shop.refuse.notdetente") : DetenteKit.Local.Full ? Texts.Get("shop.refuse.syringefull") : null;
            case Kind.Grenade:
                return PlayerGrenades.Local == null || PlayerGrenades.Local.Kind == Throwable.None ? Texts.Get("shop.refuse.nogrenade")
                    : !PlayerGrenades.HasRoom(player) ? Texts.Get("shop.refuse.grenadefull") : null;
        }
        return null;
    }

    private bool Hand(Item item)
    {
        var slots = player.GetComponent<PlayerWeaponSlots>();
        var receiver = player.DamageReceiver;
        switch (item.Kind)
        {
            case Kind.Gun:
                int slot = GunSlotFor(slots);
                if (!slots.TrySetWeapon(slot, item.Weapon)) return false;
                if (!slots.HandsFull) slots.SelectSlot(slot);
                return true;
            case Kind.Ammo:
                return slots.RefillAmmo() > 0;
            case Kind.Heal:
                receiver.Heal(item.Amount);
                return true;
            case Kind.Shield:
                receiver.SetShield(Mathf.Max(receiver.Shield, item.Amount));
                return true;
            case Kind.Syringe:
                return DetenteKit.Local != null && DetenteKit.Local.Refill(item.Amount);
            case Kind.Grenade:
                return PlayerGrenades.Refill(player, Mathf.Max(1, Mathf.RoundToInt(item.Amount))) > 0;
        }
        return false;
    }

    // The gun slot in hand, else an empty gun slot, else the first.
    private static int GunSlotFor(PlayerWeaponSlots slots)
    {
        int held = slots.SelectedSlot;
        if (held >= 0 && PlayerWeaponSlots.GetSlotCategory(held) == WeaponCategory.Ranged) return held;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            if (PlayerWeaponSlots.GetSlotCategory(i) == WeaponCategory.Ranged && slots.GetWeapon(i) == null) return i;
        return 0;
    }

    private static bool Holds(PlayerWeaponSlots slots, WeaponData gun)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) if (slots.GetWeapon(i) == gun) return true;
        return false;
    }
}
