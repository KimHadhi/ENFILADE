using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Stage 7, the classes (the user's design, 2026-11-11: seven perks after Killing Floor 2 and GMod's Horde, "클래스나
/// 퍼크나 용어 혼동할 수 있는데 걍 그러려니"): Knight (melee, the toughest), Assault (adrenaline, CHARGE), Ghost (sniper,
/// head hunter), Point (SMG and shotgun, the gun put away reloads itself), Détente (healer), Schießhund (bounty marks,
/// the Desperado), Support (barricades, resupply). One is picked before a run (the title screen's 클래스 tab; kept in
/// ClassGrowth, each with its own rank) and may change between runs; a team may share one. This reads the table
/// (Tables/Classes: names, starting guns, the class's own guns and their bonus, whether its abilities are built yet)
/// and answers what this PC's player's class does to their shots and their pace (ClassKit keeps the live state).
/// Numbers of the abilities: Tables/ClassAbilities.
/// </summary>
public static class PlayerClass
{
    public const string TableName = ClassGrowth.ClassTable, AbilityTable = "ClassAbilities";
    public const string Knight = "knight", Assault = "assault", Ghost = "ghost", Point = "point", Detente = "detente",
        Schiesshund = "schiesshund", Support = "support";

    /// <summary>One class in the table.</summary>
    public sealed class Info
    {
        public string Id, Title, Role, Primary, Secondary;
        public string[] Weapons = Array.Empty<string>();
        public float GunBonus;
        public bool Ready;
    }

    private static List<Info> catalog;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => catalog = null;

    /// <summary>Every class, in the table's order.</summary>
    public static IReadOnlyList<Info> Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            catalog = new List<Info>();
            foreach (string id in GameTables.RowIds(TableName))
            {
                if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
                catalog.Add(new Info
                {
                    Id = id, Title = Cell(row, "title", id), Role = Cell(row, "role", ""), Primary = Cell(row, "primary", ""),
                    Secondary = Cell(row, "secondary", ""),
                    Weapons = Cell(row, "classWeapons", "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
                    GunBonus = Number(row, "gunBonus", 0f), Ready = Number(row, "ready", 0f) > .5f
                });
            }
            return catalog;
        }
    }

    /// <summary>The class this PC's player plays (their pick, kept with its rank in ClassGrowth).</summary>
    public static string CurrentId => ClassGrowth.CurrentId;

    public static Info Current => Find(CurrentId);

    public static Info Find(string id)
    {
        foreach (var info in Catalog) if (string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase)) return info;
        return null;
    }

    public static bool Is(string id) => string.Equals(CurrentId, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this gun is one of the class's own (it hits harder in their hands: gunBonus).</summary>
    public static bool OwnsWeapon(Info info, WeaponData gun)
    {
        if (info == null || gun == null) return false;
        foreach (string name in info.Weapons) if (string.Equals(name, gun.name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// The bullet's damage factor from this PC's player's class: its own guns' bonus, an Assault's adrenaline and a
    /// CHARGE they are in. AttackTrace multiplies it in with the perk cards' for this PC's shots only.
    /// </summary>
    public static float GunFactor(bool headshot)
    {
        var kit = ClassKit.Local;
        if (kit == null) return 1f;
        var info = Current;
        float factor = OwnsWeapon(info, kit.HeldGun) ? 1f + info.GunBonus / 100f : 1f;
        return factor * kit.DamageFactor;
    }

    /// <summary>This PC's player's pace from their class now (adrenaline, a CHARGE): LocomotionState multiplies it in.</summary>
    public static float MoveFactor(PlayerMovement player)
    {
        var kit = ClassKit.Local;
        return kit != null && player != null && player.IsLocal && kit.Movement == player ? kit.SpeedFactor : 1f;
    }

    /// <summary>An ability's number (Tables/ClassAbilities, "Class.name").</summary>
    public static float Ability(string classRow, string name, float fallback)
        => GameTables.TryGetRow(AbilityTable, classRow, out var row) ? Number(row, name, fallback) : fallback;

    private static string Cell(IReadOnlyDictionary<string, string> row, string key, string fallback)
        => row.TryGetValue(key, out string text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : fallback;

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;
}
