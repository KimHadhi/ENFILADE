using UnityEngine;

/// <summary>
/// The rounds a player carries for one kind of gun, outside any gun: their own, whichever gun of that kind they hold or
/// held (the user, 2026-11-18: "예비 탄약은 플레이어 본인 기준으로 관리"). A gun in a slot reloads from it; a gun put down
/// leaves it with its player, and the same kind taken up again finds it as it was.
/// </summary>
public sealed class AmmoReserve
{
    public int Rounds;
}

/// <summary>
/// A gun as a thing that lies on the ground or sits in a slot: which gun it is and what travels with it. The rounds in it
/// are its own, so a gun put down and taken up again holds what it held (an empty one is still empty; the user,
/// 2026-11-18: "탄약이 바닥난 무기를 버렸다 다시 주워 탄약을 초기화(복사)하는 악용 버그 수정", and of the Tab drop to
/// come: "드랍된 무기는 현재 장탄수 그대로 유지"). Rounds that lie with it and are nobody's yet (what the map put beside
/// a gun it placed) go to whoever takes it first, once. A magazine a cut-short reload left out is still out.
/// What is not here is the holder's: the reserve (AmmoReserve), and the parts and skin a player keeps for that kind of
/// gun (GunSetups).
/// </summary>
public readonly struct GunState
{
    public readonly WeaponData Weapon;
    /// <summary>Rounds in the gun.</summary>
    public readonly int Rounds;
    /// <summary>Rounds lying with it that are nobody's yet: its first taker's reserve gets them.</summary>
    public readonly int Spare;
    /// <summary>A reload cut short left it without a magazine, or with the new one in and the bolt not worked (WeaponRuntime).</summary>
    public readonly bool MagazineOut, ChamberDue;
    /// <summary>That reload was one from empty: the next goes on with the same clip.</summary>
    public readonly bool ResumeEmpty;

    public GunState(WeaponData weapon, int rounds, int spare = 0, bool magazineOut = false, bool chamberDue = false, bool resumeEmpty = false)
    {
        Weapon = weapon;
        // Not held to the magazine's size here: that is the table's, which the gun may not have read yet. The slot it goes
        // into holds it to it (WeaponRuntime).
        Rounds = weapon != null ? Mathf.Max(0, rounds) : 0;
        Spare = Mathf.Max(0, spare);
        MagazineOut = magazineOut; ChamberDue = chamberDue; ResumeEmpty = resumeEmpty;
    }

    /// <summary>A gun fresh from the shop, a class's kit or the rack: its magazine full, nothing lying with it.</summary>
    public static GunState New(WeaponData weapon)
    {
        if (weapon == null) return default;
        // Its magazine's size is the table's (Weapons.csv), read before it is asked.
        weapon.BindTable();
        return new GunState(weapon, weapon.MagazineSize);
    }

    /// <summary>The same gun holding these rounds, with these lying with it.</summary>
    public GunState Holding(int rounds, int spare) => new GunState(Weapon, rounds, spare, MagazineOut, ChamberDue, ResumeEmpty);
}

/// <summary>
/// The game's guns by number, the same on every PC: a gun's place in the body's gun list (FPSBodyAssets.guns), from 1; 0
/// is no gun. What a gun on the ground is told to the other PCs as (FieldPickups), so any gun of the list can lie there,
/// not only those with a row in Tables/Pickups.
/// </summary>
public static class GunList
{
    public static ushort IdOf(WeaponData gun)
    {
        var guns = FPSBodyAssets.Instance != null ? FPSBodyAssets.Instance.guns : null;
        if (gun == null || guns == null) return 0;
        for (int i = 0; i < guns.Length; i++)
            if (guns[i].weapon == gun) return (ushort)(i + 1);
        return 0;
    }

    public static WeaponData Of(ushort id)
    {
        var guns = FPSBodyAssets.Instance != null ? FPSBodyAssets.Instance.guns : null;
        return guns != null && id > 0 && id <= guns.Length ? guns[id - 1].weapon : null;
    }
}
