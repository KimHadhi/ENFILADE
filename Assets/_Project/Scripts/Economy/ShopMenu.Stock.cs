using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// ShopMenu: What it sells. One part of the class; the rest is in ShopMenu.cs and the files beside it.
public sealed partial class ShopMenu
{
    private void LoadStock()
    {
        stock.Clear();
        groups.Clear();
        var guns = new Dictionary<string, WeaponData>(StringComparer.OrdinalIgnoreCase);
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon != null && !guns.ContainsKey(entry.weapon.name)) guns[entry.weapon.name] = entry.weapon;
        var byId = new Dictionary<string, GunGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !Enum.TryParse(row.Text("kind"), true, out Kind kind)) continue;
            // A Texts row the cell names (shop.<id>.name), or plain words; empty takes the gun's name.
            string name = Texts.Cell(row.Text("name"));
            float amount = row.Number("amount", 0f);
            int order = (int)row.Number("order", 0f);
            if (kind == Kind.Category)
            {
                if (byId.ContainsKey(id)) continue;
                var group = new GunGroup { Id = id, Name = name.Length > 0 ? name : id, Order = order, Threshold = amount };
                byId[id] = group;
                groups.Add(group);
                continue;
            }
            WeaponData weapon = null;
            if (kind == Kind.Gun && !guns.TryGetValue(row.Text("weapon"), out weapon)) continue;
            stock.Add(new Item
            {
                Id = id, Kind = kind, Weapon = weapon, Price = (long)row.Number("price", 0f), Amount = amount, Order = order,
                Group = kind == Kind.Gun ? row.Text("category") : "",
                Name = name.Length > 0 ? name : weapon != null ? weapon.DisplayName : id,
                Detail = DetailOf(kind, weapon, amount)
            });
        }
        stock.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        // Each gun under its category: its own cell, else what its numbers say. An id no Category row names gets a plate
        // of its own after the others (its id read as a Texts id or as words).
        foreach (var item in stock)
        {
            if (item.Kind != Kind.Gun) continue;
            string id = item.Group.Length > 0 ? item.Group : AutoGroup(item.Weapon, byId);
            if (!byId.TryGetValue(id, out var group))
            {
                group = new GunGroup { Id = id, Name = Texts.Cell(id), Order = int.MaxValue };
                byId[id] = group;
                groups.Add(group);
            }
            item.Group = group.Id;
            group.Guns.Add(item);
        }
        groups.RemoveAll(group => group.Guns.Count == 0);
        groups.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        // Cheapest first within a category; the table's order between guns of one price.
        foreach (var group in groups)
            group.Guns.Sort((a, b) => a.Price != b.Price ? a.Price.CompareTo(b.Price)
                : a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
    }

    /// <summary>
    /// The category a gun with an empty cell goes under, from its own numbers, so a modder's gun finds its plate. Each
    /// Category row's amount is its line (the default here when it has none). Tried in this order, the first that fits
    /// taking the gun: Shotgun, that many pellets or more; Marksman, single shots by default reaching that far (m; the
    /// line the pack looks draw for marksmen, PackFx class.marksmanRange); a light gun (a pistol's sound, the way the body
    /// knows a pistol, or penetration up to SMG's) is a Pistol when it fires single shots or is automatic from a magazine
    /// up to Pistol's (a machine pistol: the X18), else an SMG; MG, automatic from a magazine that big or bigger (belt-fed);
    /// anything else a Rifle. The table names every gun of its own; this sorts only those it leaves empty.
    /// </summary>
    private static string AutoGroup(WeaponData gun, Dictionary<string, GunGroup> byId)
    {
        float Line(string id, float fallback) => byId.TryGetValue(id, out var group) && group.Threshold > 0f ? group.Threshold : fallback;
        if (gun == null) return Rifle;
        bool single = gun.FireMode == WeaponFireMode.SemiAutomatic || gun.FireMode == WeaponFireMode.Fanning;
        if (gun.Pellets >= Mathf.Max(2f, Line(Shotgun, 2f))) return Shotgun;
        if (single && gun.Range >= Line(Marksman, 120f)) return Marksman;
        if (gun.SoundProfile == GunSoundProfile.Pistol || gun.Penetration <= Line(Smg, 14f))
            return single || gun.MagazineSize <= Line(Pistol, 20f) ? Pistol : Smg;
        if (gun.FireMode == WeaponFireMode.Automatic && gun.MagazineSize >= Line(Mg, 75f)) return Mg;
        return Rifle;
    }

    // What a card says under its name: a gun its shot's damage and its magazine, a refill what it gives.
    private static string DetailOf(Kind kind, WeaponData weapon, float amount)
    {
        switch (kind)
        {
            case Kind.Gun:
                if (weapon == null) return "";
                int damage = Mathf.RoundToInt(weapon.Damage);
                // A shotgun's shot is its pellets: 26×16.
                string shot = weapon.Pellets > 1 ? Texts.Format("shop.gun.pellets", damage, weapon.Pellets) : damage.ToString(CultureInfo.InvariantCulture);
                return Texts.Format("shop.gun.detail", shot, weapon.MagazineSize);
            case Kind.Ammo:
                return Texts.Get("shop.detail.ammo");
            case Kind.Syringe:
                return Texts.Format("shop.detail.syringe", amount.ToString("0"));
            case Kind.Grenade:
                // The class's own throwable by name (세열 수류탄 +1) where the class has one.
                string count = Mathf.Max(1f, amount).ToString("0");
                var info = PlayerGrenades.Local != null ? Throwables.Of(PlayerGrenades.Local.Kind) : null;
                return info != null ? Texts.Format("shop.detail.grenade.named", info.Name, count) : Texts.Format("shop.detail.grenade", count);
            case Kind.Heal:
                return Texts.Format("shop.detail.heal", amount.ToString("0"));
            default:
                return Texts.Format("shop.detail.shield", amount.ToString("0"));
        }
    }

    // Whether the first page offers a refill: a class's own only to that class.
    private static bool Offered(Item item)
    {
        switch (item.Kind)
        {
            case Kind.Syringe: return DetenteKit.Local != null;
            case Kind.Grenade: return PlayerGrenades.Local != null && PlayerGrenades.Local.Kind != Throwable.None;
            default: return true;
        }
    }

    // Ammo, health and the shield are what a fight runs out of: the big tiles.
    private static bool IsBig(Kind kind) => kind == Kind.Ammo || kind == Kind.Heal || kind == Kind.Shield;

    // Each kind's bar down a card's left side: guns white (the pink is the menus' one accent, and too near heal's red),
    // ammo cyan, heal red, shield green.
    private static Color AccentOf(Kind kind) => kind == Kind.Gun ? KitStyle.Text : kind == Kind.Ammo ? KitStyle.Cyan
        : kind == Kind.Heal || kind == Kind.Syringe ? KitStyle.Red : kind == Kind.Grenade ? new Color(1f, .62f, .3f) : KitStyle.Green;
}
