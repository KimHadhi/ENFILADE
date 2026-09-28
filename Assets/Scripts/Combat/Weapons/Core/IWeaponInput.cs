/// <summary>Input intent for one Update. Pressed is an edge; Held is a continuous trigger.</summary>
public readonly struct WeaponInputFrame
{
    public readonly bool FirePressed, FireHeld, ReloadPressed;
    public WeaponInputFrame(bool firePressed, bool fireHeld, bool reloadPressed = false)
    { FirePressed = firePressed; FireHeld = fireHeld; ReloadPressed = reloadPressed; }
}

public interface IWeaponInput { WeaponInputFrame ReadInput(); }
