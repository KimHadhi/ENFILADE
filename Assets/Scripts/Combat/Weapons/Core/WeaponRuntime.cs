using System;
using UnityEngine;

/// <summary>Per-instance magazine and timers. No input devices, physics, coroutines or animation events.</summary>
public sealed class WeaponRuntime
{
    public const int MaxShotsPerUpdate = 4;
    public WeaponData Data { get; }
    public int Ammo { get; private set; }
    public bool IsReloading { get; private set; }
    public float ReloadRemaining { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, cooldown);
    private float cooldown;
    private bool wasHoldingFire;

    public WeaponRuntime(WeaponData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        Data = data; Ammo = data.MagazineSize;
    }

    public int Tick(float dt, WeaponInputFrame input)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return 0;
        cooldown -= dt;
        bool completedReload = false;
        if (IsReloading)
        {
            ReloadRemaining = Mathf.Max(0f, ReloadRemaining - dt);
            if (ReloadRemaining > 0f) { cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0; }
            IsReloading = false; Ammo = Data.MagazineSize; completedReload = true;
        }
        // Reload wins over fire when both are pressed together. Reserve is unlimited in Phase 1.
        if (input.ReloadPressed && Ammo < Data.MagazineSize)
        {
            IsReloading = true; ReloadRemaining = Data.ReloadDuration;
            cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0;
        }
        bool automatic = Data.FireMode == WeaponFireMode.Automatic;
        bool trigger = automatic ? input.FireHeld || input.FirePressed : input.FirePressed;
        if (!trigger || Ammo <= 0)
        { cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0; }
        // Only an uninterrupted automatic trigger can carry fractional time across frames.
        if (!automatic || !wasHoldingFire || completedReload) cooldown = Mathf.Max(0f, cooldown);
        int shots = 0;
        while (cooldown <= 0.000001f && Ammo > 0 && shots < MaxShotsPerUpdate)
        {
            Ammo--; shots++; cooldown += Data.ShotInterval;
            if (!automatic) break;
        }
        if (shots == MaxShotsPerUpdate && cooldown <= 0f) cooldown = Data.ShotInterval;
        wasHoldingFire = automatic && input.FireHeld && Ammo > 0;
        return shots;
    }

    public void Interrupt()
    {
        // Disable/death never completes a reload or grants ammo.
        IsReloading = false; ReloadRemaining = 0f;
        cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false;
    }
}
