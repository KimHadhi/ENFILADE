using UnityEngine;

// PlayerGrenades: Some back. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    /// <summary>
    /// Gives this player's own PC that many more, up to what is carried; returns how many it took. The HUD blinks for any.
    /// </summary>
    public static int Refill(PlayerMovement who, int amount)
    {
        if (who == null || amount <= 0 || !who.TryGetComponent(out PlayerGrenades grenades) || grenades.kind == Throwable.None) return 0;
        int room = Mathf.Max(0, grenades.Carry - grenades.count), given = Mathf.Min(room, amount);
        if (given <= 0) return 0;
        grenades.count += given;
        if (who.IsLocal) GrenadeHUD.Blink(KitStyle.Ice);
        return given;
    }

    /// <summary>All of their throwable back (the hub's ammo crate): what is carried; returns how many it took.</summary>
    public static int Fill(PlayerMovement who) => Refill(who, 999);

    /// <summary>Whether this player has room for more of their throwable (an ammo pile may go for it alone).</summary>
    public static bool HasRoom(PlayerMovement who)
        => who != null && who.TryGetComponent(out PlayerGrenades grenades) && grenades.kind != Throwable.None && grenades.count < grenades.Carry;

    /// <summary>An ammo pile was taken on the field (FieldPickups): its table's refill.</summary>
    public static int FromAmmoPile(PlayerMovement who)
    {
        var mine = who != null && who.TryGetComponent(out PlayerGrenades grenades) ? grenades.Info : null;
        return mine != null ? Refill(who, mine.Refill) : 0;
    }

    /// <summary>
    /// A Support's resupply reached this player (SupportKit.Receive, on their own PC): GrenadeRules resupply of them, as
    /// the walking shop hands out magazines.
    /// </summary>
    public static int Resupplied(PlayerMovement who) => Refill(who, Mathf.Max(0, Mathf.RoundToInt(Throwables.Rule("resupply", 1f))));
}
