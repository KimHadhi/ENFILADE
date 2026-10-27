using System;
using UnityEngine;

/// <summary>Per-instance magazine and timers. No input devices, physics, coroutines or animation events.</summary>
public sealed class WeaponRuntime
{
    public const int MaxShotsPerUpdate = 4;
    public WeaponData Data { get; }
    public int Ammo { get; private set; }
    public int ReserveAmmo { get; private set; }
    public bool IsEmptyReload { get; private set; }
    public float ReloadDuration { get; private set; }
    public bool IsReloading { get; private set; }
    public float ReloadRemaining { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, cooldown);
    private float cooldown;
    private bool wasHoldingFire;
    private int burstRemaining;

    public WeaponRuntime(WeaponData data, int? ammunition = null, int? reserve = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data.BindTable();
        Data = data; Ammo = Mathf.Clamp(ammunition ?? data.MagazineSize, 0, data.MagazineSize);
        ReserveAmmo = Mathf.Max(0, reserve ?? data.InitialReserveAmmo);
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
            int transfer = Mathf.Min(Data.MagazineSize - Ammo, ReserveAmmo);
            Ammo += transfer; ReserveAmmo -= transfer;
            IsReloading = false; completedReload = true;
        }
        // Transfer ammunition only on completion, so survival cancels never grant or lose rounds.
        if (input.ReloadPressed && Ammo < Data.MagazineSize && ReserveAmmo > 0)
        {
            IsEmptyReload = Ammo == 0;
            IsReloading = true; ReloadDuration = IsEmptyReload ? Data.EmptyReloadDuration : Data.ReloadDuration;
            ReloadRemaining = ReloadDuration; burstRemaining = 0;
            cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0;
        }
        bool automatic = Data.FireMode == WeaponFireMode.Automatic;
        bool burst = Data.FireMode == WeaponFireMode.Burst;
        bool continuingBurst = burstRemaining > 0;
        if (burst && !continuingBurst && input.FirePressed && cooldown <= .000001f)
            burstRemaining = Data.BurstCount;
        bool trigger = burst ? burstRemaining > 0 : automatic ? input.FireHeld || input.FirePressed : input.FirePressed;
        if (!trigger || Ammo <= 0)
        { cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0; }
        // Only a continuous automatic trigger or an in-progress burst carries fractional time.
        if (((!automatic || !wasHoldingFire) && !continuingBurst) || completedReload) cooldown = Mathf.Max(0f, cooldown);
        int shots = 0;
        while (cooldown <= 0.000001f && Ammo > 0 && shots < MaxShotsPerUpdate)
        {
            Ammo--; shots++; cooldown += Data.ShotInterval;
            if (burst)
            {
                if (--burstRemaining <= 0 || Ammo == 0)
                { burstRemaining = 0; cooldown = Mathf.Max(cooldown, Data.BurstPause); break; }
            }
            else if (!automatic) break;
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
        burstRemaining = 0;
    }
    public void CancelBurst() { burstRemaining = 0; wasHoldingFire = false; }
    internal void RefundUnfiredShots(int count) { Ammo = Mathf.Min(Data.MagazineSize, Ammo + Mathf.Max(0, count)); CancelBurst(); }
    public void AddReserve(int amount) { if (amount > 0) ReserveAmmo = (int)Math.Min(int.MaxValue, (long)ReserveAmmo + amount); }

    /// <summary>Fill the magazine and restore at least the starting reserve, preserving bonus ammunition.</summary>
    public void RefillAmmo()
    {
        Interrupt();
        Ammo = Data.MagazineSize;
        ReserveAmmo = Mathf.Max(ReserveAmmo, Data.InitialReserveAmmo);
    }
}
