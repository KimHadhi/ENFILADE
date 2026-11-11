using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// GMod Horde's weight limit (2401598805: a carry limit of 15, a knife 2, a katana 5): every weapon weighs something
/// (Tables/Weapons weight) and what a player's four slots hold together may not go past the limit (Tables/Player
/// Carry.weightMax). A weapon the table gives no weight weighs its kind's (Carry.gun, Carry.melee, Carry.special), so a
/// new weapon is never weightless. The shop (ShopMenu) and the field pickups (FieldPickups) refuse a weapon that would
/// take the load past the limit and say so; the class's own starting guns are handed out as they are (their weights are
/// made to fit), and so are a friend's guns on their copy here (their PC already weighed them).
/// </summary>
public static class CarryWeight
{
    // Player.csv's row: Carry.weightMax, Carry.gun ...
    private const string Row = "Carry";
    // The weapons whose Weapons.csv row is on them: a gun in the shop or on the floor may never have been drawn, and a
    // drawn one binds its row itself (WeaponRuntime). A reload of the tables writes every bound one again.
    private static readonly HashSet<WeaponData> bound = new HashSet<WeaponData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => bound.Clear();

    /// <summary>The most a player may carry.</summary>
    public static float Limit => Mathf.Max(0f, Number("weightMax", 15f));

    /// <summary>What <paramref name="data"/> weighs: its own weight, else its kind's; nothing for no weapon.</summary>
    public static float Of(WeaponData data)
    {
        if (data == null) return 0f;
        if (bound.Add(data)) data.BindTable();
        if (data.Weight >= 0f) return data.Weight;
        return Mathf.Max(0f, data.Category == WeaponCategory.Melee ? Number("melee", 3f)
            : data.Category == WeaponCategory.Special ? Number("special", 1f) : Number("gun", 4f));
    }

    /// <summary>What the slots hold together now.</summary>
    public static float Carried(PlayerWeaponSlots slots)
    {
        if (slots == null) return 0f;
        float sum = 0f;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) sum += Of(slots.GetWeapon(i));
        return sum;
    }

    /// <summary>What they would hold with <paramref name="data"/> in <paramref name="slot"/>, in place of what is there.</summary>
    public static float With(PlayerWeaponSlots slots, int slot, WeaponData data)
    {
        float replaced = slots != null && slot >= 0 && slot < PlayerWeaponSlots.SlotCount ? Of(slots.GetWeapon(slot)) : 0f;
        return Carried(slots) - replaced + Of(data);
    }

    /// <summary>
    /// Whether <paramref name="data"/> may go in <paramref name="slot"/>: the load stays within the limit, or at least gets
    /// no heavier (someone already past it, as after the limit was lowered mid-run, may still swap down).
    /// </summary>
    public static bool Fits(PlayerWeaponSlots slots, int slot, WeaponData data)
    {
        float after = With(slots, slot, data);
        return after <= Limit + .001f || after <= Carried(slots) + .001f;
    }

    /// <summary>A weight as the screen shows it (6, 2.5).</summary>
    public static string Show(float weight) => weight.ToString("0.#", CultureInfo.InvariantCulture);

    private static float Number(string name, float fallback)
        => GameTables.TryGetRow(PlayerMovement.TableName, Row, out var row) && row.TryGetValue(name, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
}
