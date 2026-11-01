using UnityEngine;

/// <summary>
/// Shows the crosshair profile of what is in hand: the weapon's own, else the melee profile in Feedback for fists and
/// melee weapons, else the one set on the crosshair in the scene. Added by PlayerWeaponSlots.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponCrosshairSelector : MonoBehaviour
{
    private PlayerWeaponSlots slots;
    private DynamicCrosshairView view;

    private void OnEnable()
    {
        slots = GetComponent<PlayerWeaponSlots>();
        slots.SelectionChanged += OnSelectionChanged;
        Refresh();
    }

    private void OnDisable() => slots.SelectionChanged -= OnSelectionChanged;
    private void OnSelectionChanged(int slot) => Refresh();

    private void Refresh()
    {
        if (view == null) view = FindFirstObjectByType<DynamicCrosshairView>();
        if (view == null) return;
        var weapon = slots.SelectedWeapon;
        var profile = weapon != null ? weapon.CrosshairProfile : null;
        bool closeCombat = weapon != null ? weapon.Category == WeaponCategory.Melee : slots.UsesFists;
        if (profile == null && closeCombat) profile = CombatFeedbackSettings.InstanceOrDefaults.meleeCrosshair;
        view.UseProfile(profile);
    }
}
