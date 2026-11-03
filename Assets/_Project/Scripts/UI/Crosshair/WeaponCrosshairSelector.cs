using UnityEngine;

/// <summary>
/// Shows the crosshair of what is in hand: the one the player drew in settings for guns or for fists and melee weapons,
/// else the weapon's own profile, else Feedback's designed gun or melee profile, else the one set on the crosshair in
/// the scene. Follows settings changes live, the steady crosshair setting included, and while the settings' real-screen
/// button is held it shows the kind being edited instead (<see cref="PlayerCrosshairs.Showcase"/>). Added by PlayerWeaponSlots.
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
        GameSettings.Changed += Refresh;
        PlayerCrosshairs.ShowcaseChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        slots.SelectionChanged -= OnSelectionChanged;
        GameSettings.Changed -= Refresh;
        PlayerCrosshairs.ShowcaseChanged -= Refresh;
    }

    private void OnSelectionChanged(int slot) => Refresh();

    private void Refresh()
    {
        if (view == null) view = DynamicCrosshairView.FindHud();
        if (view == null) return;
        var weapon = slots.SelectedWeapon;
        bool closeCombat = weapon != null ? weapon.Category == WeaponCategory.Melee : slots.UsesFists;
        var held = closeCombat ? CrosshairKind.Melee : CrosshairKind.Gun;
        var kind = PlayerCrosshairs.Showcase ?? held;
        var profile = PlayerCrosshairs.For(kind);
        if (profile == null && kind == held && weapon != null) profile = weapon.CrosshairProfile;
        if (profile == null) profile = PlayerCrosshairs.Design(kind);
        view.UseProfile(profile);
        view.HoldShape = GameSettings.Current.steadyCrosshair;
        view.UnscaledTime = PlayerCrosshairs.Showcase != null; // the menu pauses the game; the shown look still moves
    }
}
