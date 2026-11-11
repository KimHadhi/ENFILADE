using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// What the parts a gun wears do to its numbers, by ArcCW's stacking rule (2131057232, sh_attach.lua's GetBuff): a part
/// names only entries Mult_x, Add_x and Override_x, and the gun's stat x comes out as
///     final = (every part's Mult_x multiplied together) × ((the Override_x, if a part sets one, else the gun's own x)
///             + every part's Add_x)
/// always in that order: Override first, then Add, then Mult. Two parts overriding one stat: the first in slot order
/// (sight, muzzle, grip, underbarrel) wins, as ArcCW keeps the first of an equal priority. The entries are
/// Tables/Attachments ("VerticalGrip.Mult_spread"), by the part's name: Suppressor, VerticalGrip, AngledGrip,
/// Flashlight, and for sights Iron, RedDot, Holo, Scope (magnified). Three entries are each gun's own, in Tables/Weapons,
/// and count as that part's Mult_ on that gun: suppressedNoise (Suppressor.Mult_noise), gripRecoil
/// (VerticalGrip.Mult_recoil) and gripAimTime (AngledGrip.Mult_aimTime). The stats, each on the gun's own number:
/// recoil (Recoil Vertical: the climb a shot gives the view, degrees), spread (Hip Spread: the hip-fire cone, degrees),
/// aimTime (Ads Transition Time, seconds), moveSpeed (Ads Move Multiplier: the pace's share while aimed) and noise
/// (Noise Radius: how far zombies hear a shot, m). WeaponController fires by these; the customize menu (C) shows them.
/// </summary>
public static class GunStats
{
    public const string TableName = "Attachments";

    public enum Stat { Recoil, Spread, AimTime, MoveSpeed, Noise }

    /// <summary>The parts' names (the table's rows) for the slots, and the sights'.</summary>
    public const string Suppressor = "Suppressor", VerticalGrip = "VerticalGrip", AngledGrip = "AngledGrip", Flashlight = "Flashlight",
        Iron = "Iron", RedDot = "RedDot", Holo = "Holo", Scope = "Scope";

    /// <summary>The slots in their order, the one an Override's tie goes by.</summary>
    public const int SightSlot = 0, MuzzleSlot = 1, GripSlot = 2, UnderbarrelSlot = 3, Slots = 4;

    /// <summary>The parts a gun wears by their names in the table, in slot order; null for an empty slot.</summary>
    public struct Fit
    {
        public string Sight, Muzzle, Grip, Underbarrel;

        public string this[int slot] => slot == SightSlot ? Sight : slot == MuzzleSlot ? Muzzle : slot == GripSlot ? Grip
            : slot == UnderbarrelSlot ? Underbarrel : null;

        public void Set(int slot, string part)
        {
            if (slot == SightSlot) Sight = part;
            else if (slot == MuzzleSlot) Muzzle = part;
            else if (slot == GripSlot) Grip = part;
            else if (slot == UnderbarrelSlot) Underbarrel = part;
        }
    }

    // One part's entries for every stat: its Mult (1 for none), its Add (0) and its Override (NaN for none).
    private sealed class Entries
    {
        public readonly float[] Mult = { 1f, 1f, 1f, 1f, 1f }, Add = new float[StatCount],
            Override = { float.NaN, float.NaN, float.NaN, float.NaN, float.NaN };
    }

    private const int StatCount = 5;
    // The stats' names in the table, in Stat's order.
    private static readonly string[] Names = { "recoil", "spread", "aimTime", "moveSpeed", "noise" };
    // GunAttachments.Kind's parts by their names, in the enum's order (None first).
    private static readonly string[] KindNames = { null, Suppressor, VerticalGrip, AngledGrip, Flashlight };
    private static readonly Dictionary<string, Entries> parts = new Dictionary<string, Entries>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> reported = new HashSet<string>();
    // A sight part's name in the table, worked out once: Unity hands an object's name over as a new string every time,
    // and the held gun's fit is read every frame (its time to aim, its spread).
    private static readonly Dictionary<GameObject, string> sightNames = new Dictionary<GameObject, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Forget();
        sightNames.Clear();
        // A table saved during Play counts at once.
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        parts.Clear();
        reported.Clear();
    }

    /// <summary>The gun's stat <paramref name="stat"/>, whose own number is <paramref name="own"/>, with <paramref name="fit"/> on.</summary>
    public static float Apply(Stat stat, float own, WeaponData gun, in Fit fit)
    {
        Gather(stat, gun, fit, out float mult, out float add, out bool overridden, out float over);
        return mult * ((overridden ? over : own) + add);
    }

    /// <summary>
    /// What share of its own number <paramref name="own"/> the stat comes to with <paramref name="fit"/> on (Apply ÷ own),
    /// for a number used as a factor on something larger (a shot's whole kick); only the Mults when the gun's own is 0.
    /// </summary>
    public static float Share(Stat stat, float own, WeaponData gun, in Fit fit)
    {
        Gather(stat, gun, fit, out float mult, out float add, out bool overridden, out float over);
        return own > .0001f ? mult * ((overridden ? over : own) + add) / own : mult;
    }

    /// <summary>
    /// What <paramref name="gun"/> wears now, by name; with slot <paramref name="slot"/>'s part swapped for its choice
    /// <paramref name="index"/> when asked (the customize menu's "what if"). An empty fit without a gun.
    /// </summary>
    internal static Fit Of(FPSGun gun, int slot = -1, int index = -1)
    {
        var fit = new Fit();
        if (gun == null) return fit;
        var sights = gun.Sights;
        if (sights != null) fit.Sight = SightName(sights, slot == SightSlot ? index : sights.Current);
        var worn = gun.Attachments;
        if (worn == null) return fit;
        fit.Muzzle = PartName(worn.muzzle, slot == MuzzleSlot ? index : worn.Muzzle);
        fit.Grip = PartName(worn.grips, slot == GripSlot ? index : worn.Grip);
        fit.Underbarrel = PartName(worn.underbarrel, slot == UnderbarrelSlot ? index : worn.Underbarrel);
        return fit;
    }

    /// <summary>Only choice <paramref name="index"/> of slot <paramref name="slot"/> on <paramref name="gun"/>, nothing else: what that part does by itself.</summary>
    internal static Fit Alone(FPSGun gun, int slot, int index)
    {
        var whole = Of(gun, slot, index);
        var fit = new Fit();
        fit.Set(slot, whole[slot]);
        return fit;
    }

    // ---- The entries ----

    private static void Gather(Stat stat, WeaponData gun, in Fit fit, out float mult, out float add, out bool overridden, out float over)
    {
        mult = 1f;
        add = over = 0f;
        overridden = false;
        int at = (int)stat;
        for (int slot = 0; slot < Slots; slot++)
        {
            string part = fit[slot];
            if (string.IsNullOrEmpty(part)) continue;
            var entries = EntriesOf(part);
            // Override, then Add, then Mult: the order only shows in the sum below, every entry is gathered alike.
            if (!overridden && !float.IsNaN(entries.Override[at])) { overridden = true; over = entries.Override[at]; }
            add += entries.Add[at];
            mult *= entries.Mult[at] * Own(stat, part, gun);
        }
    }

    // The gun's own Mult_ for this part (Tables/Weapons): its suppressor's noise, its vertical grip's climb, its angled
    // grip's time to aim; 1 for any other.
    private static float Own(Stat stat, string part, WeaponData gun)
    {
        if (gun == null) return 1f;
        if (stat == Stat.Noise && part == Suppressor) return gun.SuppressedNoise;
        if (stat == Stat.Recoil && part == VerticalGrip) return gun.GripRecoil;
        if (stat == Stat.AimTime && part == AngledGrip) return gun.GripAimTime;
        return 1f;
    }

    // A part's row of Tables/Attachments, read once per load. Its entries' names are matched without minding the case
    // (ArcCW writes Mult_Recoil); one that names no stat is reported once.
    private static Entries EntriesOf(string part)
    {
        if (parts.TryGetValue(part, out var entries)) return entries;
        entries = new Entries();
        parts[part] = entries;
        if (!GameTables.TryGetRow(TableName, part, out var row)) return entries;
        foreach (var cell in row)
        {
            int stat = -1;
            float[] into = null;
            string key = cell.Key;
            if (key.StartsWith("Mult_", StringComparison.OrdinalIgnoreCase)) { into = entries.Mult; stat = StatOf(key.Substring(5)); }
            else if (key.StartsWith("Add_", StringComparison.OrdinalIgnoreCase)) { into = entries.Add; stat = StatOf(key.Substring(4)); }
            else if (key.StartsWith("Override_", StringComparison.OrdinalIgnoreCase)) { into = entries.Override; stat = StatOf(key.Substring(9)); }
            if (stat < 0 || !float.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                if (reported.Add(part + "." + key))
                    Debug.LogWarning($"[Tables] {TableName}.csv [{part}] '{key}' = '{cell.Value}': Mult_·Add_·Override_ 뒤에 "
                        + string.Join("·", Names) + " 중 하나와 숫자를 적습니다.");
                continue;
            }
            into[stat] = value;
        }
        return entries;
    }

    private static int StatOf(string name)
    {
        for (int i = 0; i < Names.Length; i++) if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // ---- The parts' names ----

    private static string PartName(GunAttachments.Choice[] choices, int index)
    {
        if (choices == null || index < 0 || index >= choices.Length) return null;
        var kind = choices[index].kind;
        int at = (int)kind;
        return at >= 0 && at < KindNames.Length ? KindNames[at] : kind.ToString();
    }

    // A sight by its kind, as the customize menu names them: a magnified one is a Scope, the gun's own iron sights Iron,
    // else the pack part's (a red dot, a holographic sight, or its own name).
    private static string SightName(GunSights sights, int index)
    {
        if (sights == null || index < 0 || index >= sights.Count) return null;
        var sight = sights.sights[index];
        if (sight.scope != null) return Scope;
        if (sight.part == null || sight.ironSight) return Iron;
        if (sightNames.TryGetValue(sight.part, out string known)) return known;
        string name = sight.part.name;
        known = name.Contains("Iron") ? Iron : name.Contains("Holograph") ? Holo : name.Contains("RedDot") ? RedDot : name;
        sightNames[sight.part] = known;
        return known;
    }
}
