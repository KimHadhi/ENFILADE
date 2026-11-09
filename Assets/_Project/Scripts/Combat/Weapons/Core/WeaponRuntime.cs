using System;
using UnityEngine;

/// <summary>Per-instance magazine and timers. No input devices, physics, coroutines or animation events.</summary>
public sealed class WeaponRuntime
{
    /// <summary>
    /// Where a shell reload is (WeaponData.ShellReload), as KINEMATION's Tactical Shooter Pack shotgun plays it
    /// (TacticalShotgun): the start, then one shell after another, then the end.
    /// </summary>
    public enum ReloadStep { None, Start, Shell, End }

    public const int MaxShotsPerUpdate = 4;
    public WeaponData Data { get; }
    public int Ammo { get; private set; }
    public int ReserveAmmo { get; private set; }
    public bool IsEmptyReload { get; private set; }
    /// <summary>How long the reload takes; for a shell reload, how long its current step takes.</summary>
    public float ReloadDuration { get; private set; }
    public bool IsReloading { get; private set; }
    public float ReloadRemaining { get; private set; }
    public float CooldownRemaining => Mathf.Max(0f, cooldown);
    /// <summary>The shell reload's step; None for a magazine reload or none at all.</summary>
    public ReloadStep Step { get; private set; }
    /// <summary>Goes up each time a shell reload moves on to a shell or its end, for whoever shows it.</summary>
    public int StepCount { get; private set; }
    /// <summary>The fire mode the gun is set to now: its Fire Mode, until B switches it (WeaponData.NextFireMode).</summary>
    public WeaponFireMode FireMode { get; private set; }
    private float cooldown;
    private bool wasHoldingFire, stopAfterShell;
    private int burstRemaining;

    public WeaponRuntime(WeaponData data, int? ammunition = null, int? reserve = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data.BindTable();
        Data = data; Ammo = Mathf.Clamp(ammunition ?? data.MagazineSize, 0, data.MagazineSize);
        ReserveAmmo = Mathf.Max(0, reserve ?? data.InitialReserveAmmo);
        FireMode = data.FireMode;
    }

    /// <summary>B: the gun's next fire mode. False when it has only one.</summary>
    public bool CycleFireMode()
    {
        var next = Data.NextFireMode(FireMode);
        if (next == FireMode) return false;
        FireMode = next;
        burstRemaining = 0; wasHoldingFire = false;
        return true;
    }

    /// <summary>A friend's copy: the fire mode their PC has the gun in (PlayerNetworkCombat).</summary>
    public void ShowFireMode(WeaponFireMode mode)
    {
        if (mode == FireMode) return;
        FireMode = mode;
        burstRemaining = 0; wasHoldingFire = false;
    }

    public int Tick(float dt, WeaponInputFrame input)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return 0;
        cooldown -= dt;
        bool completedReload = false;
        if (IsReloading)
        {
            ReloadRemaining = Mathf.Max(0f, ReloadRemaining - dt);
            if (ReloadRemaining > 0f || (Step != ReloadStep.None && NextShellStep()))
            { cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0; }
            if (Step == ReloadStep.None)
            {
                int transfer = Mathf.Min(Data.MagazineSize - Ammo, ReserveAmmo);
                Ammo += transfer; ReserveAmmo -= transfer;
            }
            IsReloading = false; Step = ReloadStep.None; completedReload = true;
        }
        // Transfer ammunition only on completion, so survival cancels never grant or lose rounds. A shell reload keeps the
        // shells already in, as a real one does.
        if (input.ReloadPressed && Ammo < Data.MagazineSize && ReserveAmmo > 0)
        {
            IsEmptyReload = Ammo == 0;
            IsReloading = true; ReloadDuration = IsEmptyReload ? Data.EmptyReloadDuration : Data.ReloadDuration;
            Step = Data.ShellReload ? ReloadStep.Start : ReloadStep.None;
            stopAfterShell = false;
            ReloadRemaining = ReloadDuration; burstRemaining = 0;
            cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0;
        }
        bool automatic = FireMode == WeaponFireMode.Automatic;
        bool burst = FireMode == WeaponFireMode.Burst;
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

    // A shell reload's step is over: the start from empty and each shell have put a shell in (KINEMATION's TacticalShotgun
    // skips the first only on a tactical start, which loads none). Then the next shell, or the end once the gun is full,
    // the reserve is out or the trigger asked for it. False when the end is over too.
    private bool NextShellStep()
    {
        if (Step == ReloadStep.End) return false;
        if ((Step == ReloadStep.Shell || IsEmptyReload) && Ammo < Data.MagazineSize && ReserveAmmo > 0) { Ammo++; ReserveAmmo--; }
        bool another = Ammo < Data.MagazineSize && ReserveAmmo > 0 && !stopAfterShell;
        Step = another ? ReloadStep.Shell : ReloadStep.End;
        ReloadDuration = ReloadRemaining = another ? Data.ShellInsertDuration : Data.ShellEndDuration;
        StepCount++;
        return another || ReloadRemaining > 0f;
    }

    /// <summary>The trigger during a shell reload: it ends once the shell in hand is in, if there is a shell to fire.</summary>
    public void StopAfterShell()
    {
        if (IsReloading && Step != ReloadStep.None && Ammo > 0) stopAfterShell = true;
    }

    public void Interrupt()
    {
        // Disable/death never completes a reload or grants ammo.
        IsReloading = false; ReloadRemaining = 0f;
        Step = ReloadStep.None; stopAfterShell = false;
        cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false;
        burstRemaining = 0;
    }
    /// <summary>On someone else's copy: their reload, for the gun's pose. Their ammunition is counted on their own PC.</summary>
    public void ShowReload(bool empty)
    {
        IsEmptyReload = empty;
        IsReloading = true;
        Step = Data.ShellReload ? ReloadStep.Start : ReloadStep.None;
        stopAfterShell = false;
        ReloadDuration = ReloadRemaining = empty ? Data.EmptyReloadDuration : Data.ReloadDuration;
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
