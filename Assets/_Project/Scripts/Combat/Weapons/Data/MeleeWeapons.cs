using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The melee weapons, the melee slot's (slot 3) answer to FPSBody's guns. They have no gun asset to hang on, so each is
/// made in code from its name the first time it is asked for (WeaponData.Make), its HUD name from Texts.csv
/// (weapon.&lt;name in lower case&gt;). Today there is one: the katana (PlayerKatana, bundle 5 of the addon port).
/// Shop.csv's Melee rows, Classes.csv's classWeapons, the console's /gun and the weapon a friend holds
/// (PlayerNetworkCombat, by its place in this list) find them here. A model dropped in Art/Models/Weapons/&lt;name&gt;
/// takes the stand-in's place (MeleeModels).
/// </summary>
public static class MeleeWeapons
{
    public const string Katana = "Katana";

    // In this order on every PC of a build: the network sends a weapon's place in it.
    private static readonly string[] Names = { Katana };
    private static WeaponData[] made;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => made = null;

    /// <summary>Every melee weapon's name (also its folder under Art/Models/Weapons and Art/Audio/Melee).</summary>
    public static IReadOnlyList<string> All => Names;

    public static int Count => Names.Length;

    /// <summary>The weapon at this place in the list, or null.</summary>
    public static WeaponData At(int index)
    {
        if (index < 0 || index >= Names.Length || !Application.isPlaying) return null;
        if (made == null) made = new WeaponData[Names.Length];
        if (made[index] == null)
        {
            string name = Names[index];
            made[index] = WeaponData.Make(name, WeaponCategory.Melee, Texts.Get("weapon." + name.ToLowerInvariant()));
        }
        return made[index];
    }

    /// <summary>The weapon of this name (its asset name, any case), or null.</summary>
    public static WeaponData Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        for (int i = 0; i < Names.Length; i++)
            if (string.Equals(Names[i], name.Trim(), StringComparison.OrdinalIgnoreCase)) return At(i);
        return null;
    }

    /// <summary>This weapon's place in the list, or -1 for anything else (a gun, null).</summary>
    public static int IndexOf(WeaponData data)
    {
        if (data == null || made == null) return -1;
        for (int i = 0; i < made.Length; i++) if (made[i] == data) return i;
        return -1;
    }

    /// <summary>The katana: what PlayerKatana swings.</summary>
    public static bool IsKatana(WeaponData data) => data != null && IndexOf(data) == 0;
}
