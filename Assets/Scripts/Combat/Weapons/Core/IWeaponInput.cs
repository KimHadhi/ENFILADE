/// <summary>Input intent for one Update. Pressed is an edge; Held is a continuous trigger.</summary>
public readonly struct WeaponInputFrame
{
    public readonly bool FirePressed, FireHeld, ReloadPressed, AimHeld;
    public WeaponInputFrame(bool firePressed, bool fireHeld, bool reloadPressed = false, bool aimHeld = false)
    { FirePressed = firePressed; FireHeld = fireHeld; ReloadPressed = reloadPressed; AimHeld = aimHeld; }
}

public interface IWeaponInput { WeaponInputFrame ReadInput(); }
