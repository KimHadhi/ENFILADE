using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rules' side of a gun worked by hand after every shot (a bolt, a pump, a lever: FPSBodyAssets.Gun.sourceFire): how long
/// after a shot it can fire again. That is the moment in the action's clip when both hands are back where the gun's pose clip
/// holds it, measured once per gun from KINEMATION's own clip on its own arms as the body sets the gun up (FPSBody.MeasureCycle),
/// so it is the same on every PC. WeaponRuntime holds the trigger at least that long, however fast Weapons.csv lets the gun
/// fire, so a shot never comes while a hand is still on the bolt or the pump (the user, 2026-11-13: "근본적으로 리팩토링을 해서
/// 고치면 되잖아"). A gun with no such action, or one not set up on a body yet, has none, and its table's rate alone holds it.
/// </summary>
public static class WeaponCycle
{
    private static readonly Dictionary<WeaponData, float> ready = new();

    // A new play session measures afresh (Enter Play Mode without a domain reload keeps statics).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => ready.Clear();

    /// <summary>The gun's action has been measured this session.</summary>
    public static bool Has(WeaponData gun) => gun != null && ready.ContainsKey(gun);

    /// <summary>The gun can fire again this long after a shot (s), its hands back on it (FPSBody).</summary>
    public static void Set(WeaponData gun, float seconds)
    {
        if (gun != null) ready[gun] = Mathf.Max(0f, seconds);
    }

    /// <summary>How long after a shot the gun's action lets it fire again (s); 0 for a gun with none.</summary>
    public static float ReadyAfter(WeaponData gun) => gun != null && ready.TryGetValue(gun, out float seconds) ? seconds : 0f;

    /// <summary>Seconds from one shot to the next: the table's rate of fire or the action's, whichever is slower.</summary>
    public static float IntervalOf(WeaponData gun) => gun == null ? 0f : Mathf.Max(gun.ShotInterval, ReadyAfter(gun));
}
