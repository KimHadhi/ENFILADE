/// <summary>
/// What may end a magazine reload before its clip is over, and when (Tables/Weapons/ReloadRules.csv, row Default).
/// <para>A reload moves its rounds as its hands do it (WeaponRuntime.Advance), so once the gun is loaded the rest of the
/// clip is only the hands coming back: nothing more moves, and cutting it there costs nothing (WeaponRuntime.OnlyEndLeft).
/// The trigger, the sights, a sprint and a shove may each do so (asked whether what is left of a reload after the
/// rounds are in may be cut by firing, aiming or sprinting, the user, 2026-11-18: "네, 밀치기로 장전 캔슬해도
/// 됩니다").</para>
/// <para>Before the gun is loaded only a shove could be asked to end a reload, and as the table stands it does not:
/// both hands are on the magazine, so V is the kick that leaves the reload alone (ShoveMoves.TryKick), as it has been.
/// shoveCancels 1 makes V the gun's bash at any moment of a reload, cutting it where it stands (the next one goes on
/// from that step, as after any cut).</para>
/// A shell reload is no part of this: its own end already follows the trigger (WeaponRuntime.StopAfterShell).
/// </summary>
public static class ReloadRules
{
    public const string TableName = "ReloadRules";
    public const string Row = "Default";
    /// <summary>The table's cells, each a yes or a no.</summary>
    public const string ByFire = "cutByFire", ByAim = "cutByAim", BySprint = "cutBySprint", ByShove = "cutByShove", ShoveCancels = "shoveCancels";

    /// <summary>
    /// The gun is loaded and only its clip's end is left (WeaponRuntime.OnlyEndLeft): what the player does now ends it,
    /// each asked only where the table lets it.
    /// </summary>
    public static bool EndCutBy(bool trigger, bool sights, bool sprint)
        => (trigger && Allows(ByFire, true)) || (sights && Allows(ByAim, true)) || (sprint && Allows(BySprint, true));

    /// <summary>
    /// V during this gun's reload is the shove that ends it (the gun's bash), not the kick that leaves it alone: once
    /// only the clip's end is left (cutByShove), or at any moment of it when the table says so (shoveCancels).
    /// </summary>
    public static bool ShoveEnds(WeaponRuntime gun)
        => gun != null && gun.IsReloading && Allows(gun.OnlyEndLeft ? ByShove : ShoveCancels, gun.OnlyEndLeft);

    /// <summary>One rule of the table; the fallback is what holds when the table or the cell is missing.</summary>
    public static bool Allows(string rule, bool fallback) => GameTables.Flag(TableName, Row, rule, fallback);
}
