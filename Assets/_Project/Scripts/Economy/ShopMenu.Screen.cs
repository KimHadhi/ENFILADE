using System;
using System.Globalization;
using UnityEngine;

// ShopMenu: Screen. One part of the class; the rest is in ShopMenu.cs and the files beside it.
public sealed partial class ShopMenu
{
    private void Refresh()
    {
        // The money counts up as the panel opens, then follows the balance: at once when it grows, running down in the
        // accent when something was just paid for, so the eye catches the number changing.
        float counting = Mathf.Clamp01((reveal.Elapsed - CountFrom) / CountSeconds);
        long balance = Wallet.Balance;
        if (counting < 1f) moneyShown = balance * (double)KitReveal.EaseOut(counting);
        else if (moneyShown - balance < 1d) moneyShown = balance;
        else moneyShown = Math.Max(balance, moneyShown - Math.Max(1d, (moneyShown - balance) * MoneyRun * Time.unscaledDeltaTime));
        money.text = Wallet.Format((long)Math.Round(moneyShown));
        money.color = counting >= 1f && moneyShown - balance >= 1d ? KitStyle.Accent : KitStyle.Text;
        // The box grows leftward with the number (100,000 and up ran out of it; user, 2026-11-10); past its widest the
        // number shrinks instead. The banknote stands just left of the number.
        float natural = money.GetPreferredValues(money.text).x * MoneySize / Mathf.Max(1f, money.fontSize);
        float room = MoneyWidest - MoneyPad, width = Mathf.Min(natural, room);
        money.fontSize = natural > room ? MoneySize * room / natural : MoneySize;
        float box = Mathf.Max(MoneyWidth, width + MoneyPad);
        moneyBox.sizeDelta = new Vector2(box, CloseSize);
        moneyBox.anchoredPosition = new Vector2(Width - Inner - CloseSize - 8f - box, -18f);
        moneyIcon.anchoredPosition = new Vector2(-width - 18f, 0f);
        float now = Time.unscaledTime;
        // What was just paid falls away under the money box and fades.
        float falling = Mathf.Clamp01((now - spentAt) / SpentSeconds);
        spent.color = KitStyle.With(KitStyle.Accent, 1f - falling * falling);
        spentBox.anchoredPosition = new Vector2(Width - Inner - CloseSize - 18f - spentBox.sizeDelta.x, -(18f + CloseSize + 1f + SpentFall * falling));
        var slots = player != null ? player.GetComponent<PlayerWeaponSlots>() : null;
        foreach (var row in rows)
        {
            if (row.Back) continue;
            if (row.Group != null) RefreshGroup(row, slots, balance);
            else if (row.Item != null) RefreshItem(row, slots, balance, now);
            // A buy's pulse, once the page's opening has let go of the card's flash.
            if (pageReveal == null || pageReveal.enabled || now - row.FlashAt > KitReveal.DrainSeconds + .05f) continue;
            var flash = row.FlashColor;
            flash.a *= 1f - Mathf.Clamp01((now - row.FlashAt) / KitReveal.DrainSeconds);
            row.Flash.color = flash;
        }
    }

    // An item's card as things stand: just bought, held, nothing to do (full), short of money, or for sale. A held gun's
    // card dims with a white 가진 총 tag; a gun the money doesn't reach shows its price red and what is short.
    private void RefreshItem(Row row, PlayerWeaponSlots slots, long balance, float now)
    {
        var item = row.Item;
        bool bought = now - row.BoughtAt < BoughtSeconds;
        bool held = item.Kind == Kind.Gun && slots != null && item.Weapon != null && Holds(slots, item.Weapon);
        string note = player == null || held ? "" : Note(item, slots);
        long shortBy = item.Price - balance;
        bool idle = held || note.Length > 0;
        row.Name.color = idle ? KitStyle.Soft : KitStyle.Text;
        row.Price.color = idle ? KitStyle.Soft : shortBy > 0 ? KitStyle.Red : KitStyle.Text;
        row.Plate.color = held ? KitStyle.With(KitStyle.Night, .92f) : row.PlateColor;
        row.Bar.color = held ? KitStyle.Ice : row.BarColor;
        if (row.Picture != null) row.Picture.color = held ? KitStyle.Soft : KitStyle.Text;
        if (row.Tag != null)
        {
            // A gun card's tag: 구매 완료 for a moment, then 가진 총; else what the money is short by.
            int state = bought ? 1 : held ? 2 : shortBy > 0 ? 3 : 0;
            if (state == row.TagState && (state != 3 || shortBy == row.ShortBy)) return;
            row.TagState = state;
            row.ShortBy = shortBy;
            row.Tag.text = state == 1 ? Texts.Get("shop.note.bought") : state == 2 ? Texts.Get("shop.note.owned")
                : state == 3 ? Texts.Format("shop.note.short", Wallet.Format(shortBy)) : "";
            row.Tag.color = state == 1 ? KitStyle.Accent : state == 2 ? KitStyle.Ink : KitStyle.Red;
            row.TagBack.color = state == 2 ? KitStyle.Ice : Color.clear;
            return;
        }
        // A tile's small print gives way to what stops a buy (탄약 가득, 체력 가득 …) and, for a moment, to 구매 완료.
        row.Info.text = bought ? Texts.Get("shop.note.bought") : note.Length > 0 ? note : row.Detail;
        row.Info.color = bought ? KitStyle.Accent : note.Length > 0 ? KitStyle.Text : KitStyle.Soft;
        if (row.Count == null) return;
        if (!Amounts(item, out int have, out int most)) have = most = -1;
        if (have == row.CountNow && most == row.CountMost) return;
        row.CountNow = have;
        row.CountMost = most;
        row.Count.text = have < 0 ? "" : have.ToString(CultureInfo.InvariantCulture) + " / " + most.ToString(CultureInfo.InvariantCulture);
    }

    // A category's plate: 가진 총 while a gun of it is held; its prices red while even its cheapest is out of reach.
    private static void RefreshGroup(Row row, PlayerWeaponSlots slots, long balance)
    {
        bool held = false;
        if (slots != null)
            foreach (var gun in row.Group.Guns)
                if (gun.Weapon != null && Holds(slots, gun.Weapon)) { held = true; break; }
        row.Price.color = row.Group.Guns[0].Price > balance ? KitStyle.Red : KitStyle.Soft;
        int state = held ? 2 : 0;
        if (state == row.TagState) return;
        row.TagState = state;
        row.Tag.text = held ? Texts.Get("shop.note.owned") : "";
        row.Tag.color = KitStyle.Ink;
        row.TagBack.color = held ? KitStyle.Ice : Color.clear;
    }

    // How much of a refill is held now, for the tiles with a number to show: health, the shield, the throwables, the syringe.
    private bool Amounts(Item item, out int now, out int most)
    {
        now = most = 0;
        var receiver = player != null ? player.DamageReceiver : null;
        switch (item.Kind)
        {
            case Kind.Heal:
                if (receiver == null) return false;
                now = Mathf.CeilToInt(receiver.Health - .01f);
                most = Mathf.RoundToInt(receiver.MaximumHealth);
                return true;
            case Kind.Shield:
                if (receiver == null) return false;
                now = Mathf.CeilToInt(receiver.Shield - .01f);
                most = Mathf.Max(now, Mathf.RoundToInt(item.Amount));
                return true;
            case Kind.Grenade:
                if (PlayerGrenades.Local == null) return false;
                now = PlayerGrenades.Local.Count;
                most = PlayerGrenades.Local.Carry;
                return true;
            case Kind.Syringe:
                if (DetenteKit.Local == null) return false;
                now = Mathf.RoundToInt(DetenteKit.Local.Syringe);
                most = Mathf.RoundToInt(DetenteKit.Local.SyringeMax);
                return true;
        }
        return false;
    }

    private string Note(Item item, PlayerWeaponSlots slots)
    {
        if (item.Kind == Kind.Gun && slots != null && item.Weapon != null && Holds(slots, item.Weapon)) return Texts.Get("shop.note.owned");
        if (item.Kind == Kind.Ammo && slots != null && PlayerWeaponSlots.HoldsAnyGun(slots) && !slots.NeedsAmmo()) return Texts.Get("shop.note.ammofull");
        if (item.Kind == Kind.Heal && player.DamageReceiver.Health >= player.DamageReceiver.MaximumHealth - .5f) return Texts.Get("shop.note.healthfull");
        if (item.Kind == Kind.Shield && player.DamageReceiver.Shield >= item.Amount - .5f) return Texts.Get("shop.note.shield");
        if (item.Kind == Kind.Syringe) return DetenteKit.Local == null ? Texts.Get("shop.note.notdetente") : DetenteKit.Local.Full ? Texts.Get("shop.note.syringefull") : "";
        if (item.Kind == Kind.Grenade)
            return PlayerGrenades.Local == null || PlayerGrenades.Local.Kind == Throwable.None ? Texts.Get("shop.note.nogrenade")
                : !PlayerGrenades.HasRoom(player) ? Texts.Get("shop.note.grenadefull") : "";
        return "";
    }

    private void Say(string text, Color color)
    {
        if (message == null) return;
        message.text = text ?? Texts.Get(shownGroup != null ? "shop.hint.back" : "shop.hint.close");
        message.color = text != null ? color : KitStyle.Soft;
        messageLeft = text != null ? MessageSeconds : 0f;
    }
}
