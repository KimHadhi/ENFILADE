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

    /// <summary>
    /// The reload under way started this far into its clip (0-1): 0 for a fresh one, more for one going on from where a
    /// cut-short reload got to. FPSBody starts the clips there and WeaponFeedback skips the sounds before it.
    /// </summary>
    public float ReloadStartShare { get; private set; }
    /// <summary>
    /// A reload was cut short with the old magazine already out (its rounds went back to the reserve): the gun has no
    /// magazine and fires nothing until a reload brings the new one, which starts with it at the pouch.
    /// </summary>
    public bool MagazineOut { get; private set; }
    /// <summary>
    /// A reload was cut short with the new magazine in (its rounds counted) but the bolt or slide not yet worked: the gun
    /// fires nothing until a reload does just that last step.
    /// </summary>
    public bool ChamberDue { get; private set; }
    /// <summary>A cut-short reload has its rest to do before the gun can fire (MagazineOut or ChamberDue).</summary>
    public bool ResumeDue => MagazineOut || ChamberDue;
    /// <summary>Goes up each time a reload leaves the gun loaded, done in full or cut short past its bolt (for whoever shows it).</summary>
    public int Loads { get; private set; }
    /// <summary>The gun is at the hip (WeaponController sets it each frame): a revolver in Fanning fans.</summary>
    internal bool FanFromHip { get; set; }
    // Where the next reload goes on from (a share of its clip) and which clip that is (empty or tactical).
    private float resumeShare;
    private bool resumeEmpty;
    // The reload under way is someone else's, shown on their copy: their PC counts their rounds and keeps their steps.
    private bool shownReload;

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
            if (Step == ReloadStep.None) Fill(true);
            else Loads++;
            IsReloading = false; Step = ReloadStep.None; completedReload = true;
        }
        // A magazine reload moves its rounds as it seats the new magazine, or at its end; a cut short one keeps the steps it
        // got through (KeepWhatWasDone) and the next one goes on from there. A shell reload keeps the shells already in, as a
        // real one does.
        if (input.ReloadPressed && (ResumeDue || (Ammo < Data.MagazineSize && ReserveAmmo > 0)))
        {
            bool resuming = ResumeDue && !Data.ShellReload;
            IsEmptyReload = resuming ? resumeEmpty : Ammo == 0;
            IsReloading = true; ReloadDuration = IsEmptyReload ? Data.EmptyReloadDuration : Data.ReloadDuration;
            Step = Data.ShellReload ? ReloadStep.Start : ReloadStep.None;
            stopAfterShell = shownReload = false;
            ReloadStartShare = resuming ? Mathf.Clamp01(resumeShare) : 0f;
            ReloadRemaining = ReloadDuration * (1f - ReloadStartShare); burstRemaining = 0;
            cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0;
        }
        // Fanning from the hip fires while the trigger is held, at the fanning rate; down the sights one round a pull.
        bool fanning = FireMode == WeaponFireMode.Fanning && FanFromHip;
        bool automatic = FireMode == WeaponFireMode.Automatic || fanning;
        bool burst = FireMode == WeaponFireMode.Burst;
        float interval = fanning ? Data.FanInterval : Data.ShotInterval;
        bool continuingBurst = burstRemaining > 0;
        if (burst && !continuingBurst && input.FirePressed && cooldown <= .000001f)
            burstRemaining = Data.BurstCount;
        bool trigger = burst ? burstRemaining > 0 : automatic ? input.FireHeld || input.FirePressed : input.FirePressed;
        // A bolt never worked after the new magazine went in: nothing in the chamber.
        if (!trigger || Ammo <= 0 || ChamberDue)
        { cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0; }
        // Only a continuous automatic trigger or an in-progress burst carries fractional time.
        if (((!automatic || !wasHoldingFire) && !continuingBurst) || completedReload) cooldown = Mathf.Max(0f, cooldown);
        int shots = 0;
        while (cooldown <= 0.000001f && Ammo > 0 && shots < MaxShotsPerUpdate)
        {
            Ammo--; shots++; cooldown += interval;
            if (burst)
            {
                if (--burstRemaining <= 0 || Ammo == 0)
                { burstRemaining = 0; cooldown = Mathf.Max(cooldown, Data.BurstPause); break; }
            }
            else if (!automatic) break;
        }
        if (shots == MaxShotsPerUpdate && cooldown <= 0f) cooldown = interval;
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
        // A magazine reload cut short keeps what its hands already did; nothing more is granted or lost.
        if (IsReloading && Step == ReloadStep.None && !shownReload) KeepWhatWasDone();
        IsReloading = false; ReloadRemaining = 0f;
        Step = ReloadStep.None; stopAfterShell = shownReload = false;
        cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false;
        burstRemaining = 0;
    }

    /// <summary>
    /// How far a cut-short magazine reload got, by where its clip moves the magazine and the bolt (ReloadMoments), so the
    /// next reload doesn't do it again (user, 2026-11-11: "탄창을 뺐다면 다시 탄창을 빼지 않고 새 탄창으로 갈아 끼웠다면 다시
    /// 갈아 끼우지 않고 노리쇠 후퇴까지 했다면 … 캔슬해도 장전된 상태로"). Past its bolt (a tactical reload: past the new
    /// magazine seating) the gun is loaded. Past the seating the rounds are in, and only the bolt is left. Past the old
    /// magazine leaving, it is out: its rounds go back to the reserve, so none are lost, and the next reload starts with the
    /// new one at the pouch. Before that nothing was done.
    /// </summary>
    private void KeepWhatWasDone()
    {
        float done = ReloadDuration > 0f ? Mathf.Clamp01(1f - ReloadRemaining / ReloadDuration) : 0f;
        Stages(IsEmptyReload, out float magOut, out float pouch, out float magIn, out float loaded);
        if (done >= loaded) { Fill(true); return; }
        if (done >= magIn) { Fill(false); ChamberDue = true; Remember(magIn); return; }
        if (done < magOut) return;
        if (!MagazineOut)
        {
            ReserveAmmo = (int)Math.Min(int.MaxValue, (long)ReserveAmmo + Ammo);
            Ammo = 0;
            MagazineOut = true;
        }
        Remember(pouch);
    }

    // The new magazine's rounds go in from the reserve; complete, the gun is ready and nothing is left to resume.
    private void Fill(bool complete)
    {
        int transfer = Mathf.Min(Data.MagazineSize - Ammo, ReserveAmmo);
        Ammo += transfer; ReserveAmmo -= transfer;
        MagazineOut = false;
        if (!complete) return;
        ChamberDue = false;
        resumeShare = 0f;
        Loads++;
    }

    private void Remember(float share)
    {
        resumeShare = share;
        resumeEmpty = IsEmptyReload;
    }

    // Nothing of a cut-short reload is left to do: the gun was filled some other way.
    private void ForgetResume()
    {
        MagazineOut = ChamberDue = false;
        resumeShare = 0f;
    }

    // Shares of a reload clip: the old magazine starting to leave, the new one at the pouch, the new one seated, and the
    // gun loaded (an empty reload's bolt or slide worked after the seating; a tactical reload is loaded once seated). From
    // the clip's own motion when the gun's body has measured it, else the shares the reload sounds fall back on.
    private void Stages(bool empty, out float magOut, out float pouch, out float magIn, out float loaded)
    {
        magOut = .2f;
        magIn = empty ? .5f : .62f;
        loaded = empty ? .86f : magIn;
        if (ReloadMoments.TryGet(Data, empty, out var moments))
        {
            if (moments.MagOut >= 0f) magOut = moments.MagOut;
            if (moments.MagIn >= 0f) magIn = moments.MagIn;
            loaded = magIn;
            if (empty)
            {
                if (moments.BoltBack > magIn) loaded = moments.BoltBack;
                else if (moments.BoltForward > magIn) loaded = moments.BoltForward;
            }
        }
        magIn = Mathf.Max(magIn, magOut);
        loaded = Mathf.Max(loaded, magIn);
        pouch = ReloadMoments.TryGetMagazine(Data, empty, out var magazine) && magazine.Far >= magOut && magazine.Far <= magIn
            ? magazine.Far : magOut;
    }

    /// <summary>
    /// On someone else's copy: their reload, for the gun's pose, from <paramref name="from"/> of its clip when theirs goes
    /// on from a cut-short one. Their ammunition and steps are counted on their own PC.
    /// </summary>
    public void ShowReload(bool empty, float from = 0f)
    {
        IsEmptyReload = empty;
        IsReloading = shownReload = true;
        Step = Data.ShellReload ? ReloadStep.Start : ReloadStep.None;
        stopAfterShell = false;
        ReloadStartShare = Data.ShellReload ? 0f : Mathf.Clamp01(from);
        ReloadDuration = empty ? Data.EmptyReloadDuration : Data.ReloadDuration;
        ReloadRemaining = ReloadDuration * (1f - ReloadStartShare);
    }
    public void CancelBurst() { burstRemaining = 0; wasHoldingFire = false; }
    internal void RefundUnfiredShots(int count) { Ammo = Mathf.Min(Data.MagazineSize, Ammo + Mathf.Max(0, count)); CancelBurst(); }
    public void AddReserve(int amount) { if (amount > 0) ReserveAmmo = (int)Math.Min(int.MaxValue, (long)ReserveAmmo + amount); }

    /// <summary>
    /// A gun put away reloads itself (a Point's passive, ClassKit): its magazine fills from its reserve at once. False
    /// when it is being reloaded, full or out of reserve.
    /// </summary>
    internal bool ReloadAside()
    {
        if (IsReloading || (!ResumeDue && (Ammo >= Data.MagazineSize || ReserveAmmo <= 0))) return false;
        int moved = Math.Min(Data.MagazineSize - Ammo, ReserveAmmo);
        Ammo += moved;
        ReserveAmmo -= moved;
        IsEmptyReload = false;
        ForgetResume();
        return true;
    }

    /// <summary>
    /// A gun loaded only from outside (a Schießhund's Desperado, ClassKit): this many rounds in it and nothing in reserve,
    /// so no reload can fill it.
    /// </summary>
    internal void LoadOwnRounds(int rounds)
    {
        Interrupt();
        ForgetResume();
        Ammo = Mathf.Clamp(rounds, 0, Data.MagazineSize);
        ReserveAmmo = 0;
    }

    /// <summary>One round more into such a gun (a bounty claimed). False when it is full.</summary>
    internal bool LoadRound()
    {
        if (Ammo >= Data.MagazineSize) return false;
        Ammo++;
        return true;
    }

    /// <summary>
    /// The magazine full at once without the reserve paying for it: a Point's Gun Kata, where the gun drawn comes out full
    /// (ClassKit; the user, 2026-11-12: "장전처럼 탄약을 소모하는 게 아니라 그냥 꽉 찬 채로 나오는 게 어때? 그럼 총알 다 떨어질
    /// 걱정 없이 시원시원하고 맛있게 쏠 수 있으니까"). Any reload under way stops.
    /// </summary>
    public void TopUp()
    {
        if (Ammo >= Data.MagazineSize) return;
        Interrupt();
        ForgetResume();
        Ammo = Data.MagazineSize;
    }

    /// <summary>Fill the magazine and restore at least the starting reserve, preserving bonus ammunition.</summary>
    public void RefillAmmo()
    {
        Interrupt();
        ForgetResume();
        Ammo = Data.MagazineSize;
        ReserveAmmo = Mathf.Max(ReserveAmmo, Data.InitialReserveAmmo);
    }
}
