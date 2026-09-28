using UnityEngine;

/// <summary>Phase 1 verification only; no weapon model or animation is required.</summary>
public sealed class WeaponDebugHUD : MonoBehaviour
{
    private WeaponController weapon;
    private void Awake() { weapon = GetComponent<WeaponController>(); }
    private void OnGUI()
    {
        if (weapon == null || weapon.Runtime == null) return;
        var state = weapon.Runtime;
        string status = state.IsReloading ? $"Reloading {state.ReloadRemaining:0.0}s"
            : state.Ammo == 0 ? "EMPTY - press R" : "Ready";
        GUI.Box(new Rect(Screen.width - 330, 12, 318, 100),
            $"{state.Data.DisplayName} ({state.Data.FireMode})\n"
            + $"Ammo {state.Ammo}/{state.Data.MagazineSize} | Reserve: unlimited\n{status}\nLMB: Fire    R: Reload");
        GUI.Label(new Rect(Screen.width * 0.5f - 5, Screen.height * 0.5f - 10, 20, 20), "+");
    }
}
