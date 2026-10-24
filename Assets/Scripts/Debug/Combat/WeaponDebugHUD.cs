using UnityEngine;

/// <summary>Compact prototype readout for magazine, reserve, aiming and reload state.</summary>
public sealed class WeaponDebugHUD : MonoBehaviour
{
    private WeaponController weapon;

    private void Awake()
    {
        weapon = GetComponent<WeaponController>();
    }

    private void OnGUI()
    {
        if (weapon == null || weapon.Runtime == null) return;

        var state = weapon.Runtime;
        string status = state.IsReloading ? $"{(state.IsEmptyReload ? "Empty reload" : "Tactical reload")} {state.ReloadRemaining:0.0}s"
            : weapon.IsHighReady ? "High ready" : state.Ammo == 0 ? "EMPTY - press R" : weapon.AimBlend > .5f ? "ADS" : "Ready";

        GUI.Box(new Rect(Screen.width - 330, 12, 318, 100),
            $"{state.Data.DisplayName} ({state.Data.FireMode})\n"
            + $"Ammo {state.Ammo}/{state.Data.MagazineSize} | Reserve: {state.ReserveAmmo}\n{status}\nLMB: Fire    RMB: Aim    R: Reload");
    }
}
