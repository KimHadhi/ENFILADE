
// WeaponController: fire mode, sight and attachments, chosen here and shown on friends' copies. One part of the class; the rest is in WeaponController.cs and the files beside it.
public sealed partial class WeaponController
{
    /// <summary>B: the held gun's next fire mode (WeaponData Fire Modes), unless it is being reloaded.</summary>
    public void CycleFireMode()
    {
        if (Runtime == null || Runtime.IsReloading || Runtime.Data.Category == WeaponCategory.Melee) return;
        if (!Runtime.CycleFireMode()) return;
        FireModeChanged?.Invoke(Runtime.FireMode);
        KeepSetup();
    }

    /// <summary>The gun of ours drawn now (its sights and parts), for the customize menu; null without one.</summary>
    internal FPSGun HeldGun => presentation is FPSBody body ? body.Held : null;

    /// <summary>The customize menu is open for this player: the body holds the gun out before the eyes to show it.</summary>
    internal bool Customizing { get; set; }

    /// <summary>Why the held gun's parts can't be changed now, or null (the customize menu says it).</summary>
    internal string CustomizeRefusal => Runtime == null || EquippedData == null ? Texts.Get("gun.refuse.no_gun")
        : Runtime.IsReloading ? Texts.Get("gun.refuse.reloading") : IsSwitching ? Texts.Get("gun.refuse.drawing") : null;

    /// <summary>
    /// The customize menu (C, GunCustomizeMenu) puts sight, muzzle part or underbarrel part <paramref name="index"/> on the
    /// held gun. False when it can't be changed now or it is on already; the click plays as for a sight swap.
    /// </summary>
    internal bool ChooseSight(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowSight(EquippedData, index)) return false;
        SightChanged?.Invoke();
        KeepSetup();
        return true;
    }

    internal bool ChooseMuzzle(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowMuzzle(EquippedData, index)) return false;
        AttachmentChanged?.Invoke();
        KeepSetup();
        return true;
    }

    internal bool ChooseUnderbarrel(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowUnderbarrel(EquippedData, index)) return false;
        AttachmentChanged?.Invoke();
        KeepSetup();
        return true;
    }

    internal bool ChooseGrip(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowGrip(EquippedData, index)) return false;
        AttachmentChanged?.Invoke();
        KeepSetup();
        return true;
    }

    /// <summary>The held gun's grip (-1 for no choice), for friends' PCs, and a friend's copy taking theirs.</summary>
    public int GripIndex => presentation is FPSBody body && EquippedData != null ? body.GripOf(EquippedData) : -1;

    internal void ShowGrip(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowGrip(EquippedData, index);
    }

    /// <summary>The held gun's shown sight (GunSights), -1 for a gun without a choice of sights: for friends' PCs.</summary>
    public int SightIndex => presentation is FPSBody body && EquippedData != null ? body.SightOf(EquippedData) : -1;

    /// <summary>A friend's copy: the sight their PC shows on the held gun (PlayerNetworkCombat).</summary>
    internal void ShowSight(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowSight(EquippedData, index);
    }

    /// <summary>A friend's copy: the fire mode their PC has the held gun in (PlayerNetworkCombat).</summary>
    internal void ShowFireMode(WeaponFireMode mode) => Runtime?.ShowFireMode(mode);

    /// <summary>L: the held gun's flashlight on or off.</summary>
    public void ToggleLight()
    {
        if (Runtime == null || !(presentation is FPSBody body)) return;
        if (!body.ToggleLight()) return;
        AttachmentChanged?.Invoke();
        KeepSetup();
    }

    // This PC's own player, not a friend's copy: only its choices are kept, and only its guns get them back.
    private bool Mine => movement == null || movement.IsLocal && !movement.IsNetworkDriven;

    // Its fitting is written down at once, so the next game starts with the gun as it was left (GunSetups).
    private void KeepSetup()
    {
        if (Mine) GunSetups.Remember(this);
    }

    /// <summary>
    /// The held gun wears a suppressor: its shot is heard by zombies at the gun's Suppressed Noise share, sounds quieter and
    /// throws no flash or light (WeaponFeedback, MuzzleFlash, MuzzleLight).
    /// </summary>
    public bool Suppressed => presentation is FPSBody body && body.HeldSuppressed;

    /// <summary>The held gun's muzzle and underbarrel parts (-1 for no choice) and its flashlight, for friends' PCs.</summary>
    public int MuzzleIndex => presentation is FPSBody body && EquippedData != null ? body.MuzzleOf(EquippedData) : -1;

    public int UnderbarrelIndex => presentation is FPSBody body && EquippedData != null ? body.UnderbarrelOf(EquippedData) : -1;

    public bool LightOn => presentation is FPSBody body && EquippedData != null && body.LightOf(EquippedData);

    /// <summary>A friend's copy: the parts and flashlight their PC has on the held gun (PlayerNetworkCombat).</summary>
    internal void ShowMuzzle(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowMuzzle(EquippedData, index);
    }

    internal void ShowUnderbarrel(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowUnderbarrel(EquippedData, index);
    }

    internal void ShowLight(bool on)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowLight(EquippedData, on);
    }
}
