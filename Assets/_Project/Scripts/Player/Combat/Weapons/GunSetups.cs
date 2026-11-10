using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// How the player last fitted each gun (sight, muzzle and underbarrel parts, grip, flashlight, fire mode), kept on this
/// PC in gun_setups.json and put back on the gun whenever it is drawn, so a new game starts with the guns as they were
/// left (user, 2026-11-11 playtest: "매번 게임 시작할 때마다 다시 총기 커스터마이징 해야 됨"). Written as soon as
/// something is changed (WeaponController's Choose*, ToggleLight, CycleFireMode). This PC's own player only: a friend's
/// copy shows what their PC sends. A class's starting kit, when classes come, can start from the same file.
/// </summary>
public static class GunSetups
{
    private const string FileName = "gun_setups.json";

    [Serializable]
    private sealed class Entry
    {
        public string gun;
        public int sight = -1, muzzle = -1, under = -1, grip = -1, mode = -1;
        public bool light;
    }

    [Serializable]
    private sealed class Store { public List<Entry> guns = new List<Entry>(); }

    private static Store data;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => data = null;

    /// <summary>The held gun's fitting now, written down for its next draw and the next game.</summary>
    internal static void Remember(WeaponController weapon)
    {
        var gun = weapon != null ? weapon.EquippedData : null;
        if (gun == null) return;
        var entry = Find(gun.name, true);
        entry.sight = weapon.SightIndex;
        entry.muzzle = weapon.MuzzleIndex;
        entry.under = weapon.UnderbarrelIndex;
        entry.grip = weapon.GripIndex;
        entry.light = weapon.LightOn;
        entry.mode = weapon.Runtime != null ? (int)weapon.Runtime.FireMode : -1;
        Save();
    }

    /// <summary>
    /// Puts the held gun's remembered fitting back on it. False while the body hasn't built that gun yet (try again), true
    /// once done or when nothing is remembered for it.
    /// </summary>
    internal static bool Restore(WeaponController weapon)
    {
        var gun = weapon != null ? weapon.EquippedData : null;
        if (gun == null) return false;
        // The parts live on the body's model of the gun, which it builds a frame or so after the draw.
        var held = weapon.HeldGun;
        if (held == null || held.Data != gun) return false;
        var entry = Find(gun.name, false);
        if (entry == null) return true;
        // Friends' PCs see the change as they see any (PlayerNetworkCombat sends what the gun shows).
        if (entry.sight >= 0 && weapon.SightIndex != entry.sight) weapon.ShowSight(entry.sight);
        if (entry.muzzle >= 0 && weapon.MuzzleIndex != entry.muzzle) weapon.ShowMuzzle(entry.muzzle);
        if (entry.under >= 0 && weapon.UnderbarrelIndex != entry.under) weapon.ShowUnderbarrel(entry.under);
        if (entry.grip >= 0 && weapon.GripIndex != entry.grip) weapon.ShowGrip(entry.grip);
        if (weapon.LightOn != entry.light) weapon.ShowLight(entry.light);
        var runtime = weapon.Runtime;
        if (entry.mode >= 0 && runtime != null && (int)runtime.FireMode != entry.mode && Has(gun, runtime.FireMode, (WeaponFireMode)entry.mode))
            runtime.ShowFireMode((WeaponFireMode)entry.mode);
        return true;
    }

    // Only a fire mode the gun still has: Weapons.csv may have taken it away since.
    private static bool Has(WeaponData gun, WeaponFireMode from, WeaponFireMode wanted)
    {
        var mode = from;
        for (int step = 0; step < 8; step++)
        {
            mode = gun.NextFireMode(mode);
            if (mode == wanted) return true;
            if (mode == from) return false;
        }
        return false;
    }

    private static Entry Find(string gun, bool make)
    {
        Load();
        foreach (var entry in data.guns) if (entry.gun == gun) return entry;
        if (!make) return null;
        var made = new Entry { gun = gun };
        data.guns.Add(made);
        return made;
    }

    private static void Load()
    {
        if (data != null) return;
        data = new Store();
        try
        {
            if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), data);
        }
        catch (Exception problem) { Debug.LogWarning($"[GunSetups] {FilePath} couldn't be read ({problem.Message}); guns start as they come."); }
        if (data.guns == null) data.guns = new List<Entry>();
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(data, true)); }
        catch (Exception problem) { Debug.LogWarning($"[GunSetups] {FilePath} couldn't be written ({problem.Message})."); }
    }
}
