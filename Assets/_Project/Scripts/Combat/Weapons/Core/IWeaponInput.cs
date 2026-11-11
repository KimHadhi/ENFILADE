/// <summary>
/// Input intent for one Update. Pressed is an edge; Held is a continuous trigger. AltFirePressed is the second trigger
/// (Combat/AltFire): a dual pistol's left gun (AkimboGuns); other guns ignore it.
/// </summary>
public readonly struct WeaponInputFrame
{
    public readonly bool FirePressed, FireHeld, ReloadPressed, AimHeld, AltFirePressed;
    public WeaponInputFrame(bool firePressed, bool fireHeld, bool reloadPressed = false, bool aimHeld = false, bool altFirePressed = false)
    { FirePressed = firePressed; FireHeld = fireHeld; ReloadPressed = reloadPressed; AimHeld = aimHeld; AltFirePressed = altFirePressed; }
}

public interface IWeaponInput { WeaponInputFrame ReadInput(); }
