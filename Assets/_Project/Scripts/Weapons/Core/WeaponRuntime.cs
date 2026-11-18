using System;
using UnityEngine;

/// <summary>
/// One gun in a holder's hands or slot: what is in it, its timers and the rules of firing and reloading it. No input
/// devices, physics, coroutines or animation events. What is in the gun is the gun's own and goes with it when it is put
/// down (State, GunState); the reserve it reloads from is its holder's (AmmoReserve) and stays with them.
/// </summary>
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
    /// <summary>The rounds its holder carries for this kind of gun outside it: the holder's, not the gun's (AmmoReserve).</summary>
    public int ReserveAmmo { get => reserve.Rounds; private set => reserve.Rounds = Mathf.Max(0, value); }
    private readonly AmmoReserve reserve;
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
    /// The old magazine is out of the gun and no new one is in: from the moment a reload's hands take it out (its rounds
    /// are then with the reserve, so none are lost) until the new one is seated. A reload cut short in between leaves the
    /// gun so: it fires nothing until a reload brings the new one, which starts with it at the pouch.
    /// </summary>
    public bool MagazineOut { get; private set; }
    /// <summary>
    /// The new magazine is in (its rounds counted) but the bolt or slide not yet worked: from the seating until the bolt
    /// of a reload from empty. A reload cut short in between leaves the gun so: it fires nothing until a reload does just
    /// that last step.
    /// </summary>
    public bool ChamberDue { get; private set; }
    /// <summary>The gun can't fire until a reload does the rest of what one began (MagazineOut or ChamberDue).</summary>
    public bool ResumeDue => MagazineOut || ChamberDue;
    /// <summary>
    /// Goes up each time a reload is over with the gun loaded: its clip played out, or cut short past its bolt, where
    /// only its end was left (for whoever shows it: WeaponController.ReloadCompleted).
    /// </summary>
    public int Loads { get; private set; }
    // How far the magazine reload under way has got in what its hands do to the gun (ReloadSteps), each done once.
    private enum Stage { Begun, MagazineOut, MagazineIn, Loaded }
    private Stage stage;
    /// <summary>The gun is at the hip (WeaponController sets it each frame): a revolver in Fanning fans.</summary>
    internal bool FanFromHip { get; set; }
    // Which clip the reload that goes on from a cut-short one plays (from empty or not): the one that was cut.
    private bool resumeEmpty;
    // The reload under way is someone else's, shown on their copy: their PC counts their rounds and keeps their steps.
    private bool shownReload;

    private float cooldown;
    private bool wasHoldingFire, stopAfterShell;
    private int burstRemaining;

    /// <summary>
    /// Who is told what changed this gun's rounds other than a shot, with the magazine and the reserve as they were
    /// before it: the record of this PC's own player's guns (GunAmmoLog, set by PlayerWeaponSlots) and the rules' check
    /// (AmmoCheck). Nobody by default, and nothing here depends on being heard.
    /// </summary>
    internal Action<WeaponRuntime, string, int, int> Told;

    private void Tell(string what, int ammoBefore, int reserveBefore) => Told?.Invoke(this, what, ammoBefore, reserveBefore);

    /// <summary>A gun by itself, with a reserve of its own (a controller with no slots, a check): full unless told.</summary>
    public WeaponRuntime(WeaponData data, int? ammunition = null, int? reserve = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data.BindTable();
        Data = data; Ammo = Mathf.Clamp(ammunition ?? data.MagazineSize, 0, data.MagazineSize);
        this.reserve = new AmmoReserve { Rounds = Mathf.Max(0, reserve ?? data.InitialReserveAmmo) };
        FireMode = data.FireMode;
    }

    /// <summary>
    /// This gun in a holder's slot: what is in it is as the gun was (its rounds, a magazine a cut-short reload left
    /// out), and it reloads from <paramref name="reserve"/>, its holder's rounds for this kind of gun
    /// (PlayerWeaponSlots.ReserveOf). Rounds lying with the gun (GunState.Spare) are not taken here: whoever puts it in
    /// the slot hands them to the holder.
    /// </summary>
    public WeaponRuntime(in GunState gun, AmmoReserve reserve)
    {
        if (gun.Weapon == null) throw new ArgumentNullException(nameof(gun));
        gun.Weapon.BindTable();
        Data = gun.Weapon;
        this.reserve = reserve ?? new AmmoReserve();
        MagazineOut = gun.MagazineOut;
        ChamberDue = gun.ChamberDue && !gun.MagazineOut;
        resumeEmpty = gun.ResumeEmpty;
        Ammo = MagazineOut ? 0 : Mathf.Clamp(gun.Rounds, 0, Data.MagazineSize);
        FireMode = Data.FireMode;
    }

    /// <summary>
    /// The gun as a thing to put down, as it is now: its rounds stay in it, and so does a magazine a cut-short reload
    /// left out. Nothing of the reserve: that is its holder's. Whoever puts it down cuts a reload under way first.
    /// </summary>
    public GunState State => new GunState(Data, Ammo, 0, MagazineOut, ChamberDue, resumeEmpty);

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
            // A magazine reload moves its rounds as its hands do it (the old magazine out, the new one seated, the bolt
            // worked), not at its clip's end: what the gun holds is true at every moment of it, for the HUD and for a cut
            // (the user, 2026-11-18: "새 매거진을 총기에 삽입하여 내부 장전 판정이 완료되는 순간 즉시 HUD 탄약 수치가
            // 갱신되도록"). A friend's copy counts nothing: their PC does.
            if (Step == ReloadStep.None && !shownReload) Advance();
            if (ReloadRemaining > 0f || (Step != ReloadStep.None && NextShellStep()))
            { cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0; }
            // Its clip is over: what was left after the gun was loaded changed nothing in it, but until here the hands
            // were on it, so only now can it fire.
            Loads++;
            IsReloading = false; Step = ReloadStep.None; completedReload = true;
        }
        // A reload goes on from where one cut short got to (CutShort), so no step is done twice. A shell reload keeps the
        // shells already in, as a real one does.
        if (input.ReloadPressed && (ResumeDue || (Ammo < Data.MagazineSize && ReserveAmmo > 0)))
        {
            bool resuming = ResumeDue && !Data.ShellReload;
            IsEmptyReload = resuming ? resumeEmpty : Ammo == 0;
            IsReloading = true; ReloadDuration = IsEmptyReload ? Data.EmptyReloadDuration : Data.ReloadDuration;
            Step = Data.ShellReload ? ReloadStep.Start : ReloadStep.None;
            stopAfterShell = shownReload = false;
            // What the cut-short one did is done: its magazine is out already, or its new one already in.
            stage = !resuming ? Stage.Begun : MagazineOut ? Stage.MagazineOut : Stage.MagazineIn;
            // From where it got to, which what the gun is like says by itself: with its magazine out, the new one is taken
            // from the pouch; with the new one in, the bolt is worked. (A gun taken from the ground like that goes on the same.)
            var steps = ReloadSteps.Of(Data, IsEmptyReload);
            ReloadStartShare = !resuming ? 0f : MagazineOut ? steps.Pouch : steps.MagIn;
            ReloadRemaining = ReloadDuration * (1f - ReloadStartShare); burstRemaining = 0;
            cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false; return 0;
        }
        // Fanning from the hip fires while the trigger is held, at the fanning rate; down the sights one round a pull.
        bool fanning = FireMode == WeaponFireMode.Fanning && FanFromHip;
        bool automatic = FireMode == WeaponFireMode.Automatic || fanning;
        bool burst = FireMode == WeaponFireMode.Burst;
        // A gun worked by hand after every shot fires again only once its action is done (WeaponCycle), however fast its rate.
        float interval = fanning ? Data.FanInterval : WeaponCycle.IntervalOf(Data);
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
    // skips the first only on a tactical start, which loads none; a start from empty that only opens the action, as the
    // Kar98k's bolt, loads none either: WeaponData.EmptyStartLoads). Then the next shell, or the end once the gun is full,
    // the reserve is out or the trigger asked for it. False when the end is over too.
    private bool NextShellStep()
    {
        if (Step == ReloadStep.End) return false;
        bool loads = Step == ReloadStep.Shell || (IsEmptyReload && Data.EmptyStartLoads);
        if (loads && Ammo < Data.MagazineSize && ReserveAmmo > 0)
        {
            Ammo++; ReserveAmmo--;
            Tell("reload: shell in", Ammo - 1, ReserveAmmo + 1);
        }
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
        if (IsReloading && Step == ReloadStep.None && !shownReload) CutShort();
        StopReload();
    }

    // The magazine reload under way has got as far as its timer says: each thing its hands have done to the gun by now
    // is done to its rounds, once and in order (ReloadSteps). This is the only place a reload moves rounds; one cut
    // short has simply got no further.
    private void Advance()
    {
        float done = ReloadDuration > 0f ? Mathf.Clamp01(1f - ReloadRemaining / ReloadDuration) : 1f;
        var steps = ReloadSteps.Of(Data, IsEmptyReload);
        if (stage < Stage.MagazineOut && done >= steps.MagOut)
        {
            // The old magazine is out: the gun holds none, and its rounds are with the rest the player carries.
            int ammoBefore = Ammo, reserveBefore = ReserveAmmo;
            ReserveAmmo = (int)Math.Min(int.MaxValue, (long)ReserveAmmo + Ammo);
            Ammo = 0;
            MagazineOut = true;
            stage = Stage.MagazineOut;
            Tell("reload: magazine out", ammoBefore, reserveBefore);
        }
        if (stage < Stage.MagazineIn && done >= steps.MagIn)
        {
            // The new magazine is seated: its rounds are in the gun from this moment. The bolt is still to work.
            int ammoBefore = Ammo, reserveBefore = ReserveAmmo;
            int transfer = Mathf.Min(Data.MagazineSize - Ammo, ReserveAmmo);
            Ammo += transfer; ReserveAmmo -= transfer;
            MagazineOut = false;
            ChamberDue = true;
            stage = Stage.MagazineIn;
            Tell("reload: magazine in", ammoBefore, reserveBefore);
        }
        if (stage < Stage.Loaded && done >= steps.Loaded)
        {
            // The bolt worked (with a round still chambered, the seating was all there was): nothing is left to go on with.
            ChamberDue = false;
            stage = Stage.Loaded;
            if (steps.Loaded > steps.MagIn) Tell("reload: bolt worked", Ammo, ReserveAmmo);
        }
    }

    /// <summary>
    /// A magazine reload cut short. Its hands had done what they had done and the rounds were moved as they did it
    /// (Advance), so the cut itself judges nothing and moves nothing: it only keeps where the next reload goes on from, so
    /// that no step is done twice (the user, 2026-11-11: "탄창을 뺐다면 다시 탄창을 빼지 않고 새 탄창으로 갈아 끼웠다면 다시
    /// 갈아 끼우지 않고 노리쇠 후퇴까지 했다면 … 캔슬해도 장전된 상태로"). Loaded already (past its bolt; with a round
    /// chambered, past the new magazine seating): only its end was left and the reload counts as done. The new magazine
    /// in: the next one starts at the seating and works the bolt. The old one out: the next starts with the new one at
    /// the pouch. Before that nothing was done, and the next starts afresh.
    /// </summary>
    private void CutShort()
    {
        if (stage == Stage.Loaded) Loads++;
        // The next reload plays the same clip on from the step the gun is left at (MagazineOut or ChamberDue say which).
        else if (stage != Stage.Begun) resumeEmpty = IsEmptyReload;
        Tell(stage == Stage.Loaded ? "reload cut short: loaded already" : stage == Stage.MagazineIn ? "reload cut short: bolt left to work"
            : stage == Stage.MagazineOut ? "reload cut short: no magazine in" : "reload cut short: nothing done yet", Ammo, ReserveAmmo);
    }

    // The reload under way is over here and now, whatever it had got to; the trigger starts afresh.
    private void StopReload()
    {
        IsReloading = false; ReloadRemaining = 0f;
        Step = ReloadStep.None; stopAfterShell = shownReload = false;
        cooldown = Mathf.Max(0f, cooldown); wasHoldingFire = false;
        burstRemaining = 0;
    }

    // Nothing of a cut-short reload is left to do: the gun was filled some other way.
    private void ForgetResume() => MagazineOut = ChamberDue = false;

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
    public void AddReserve(int amount)
    {
        if (amount <= 0) return;
        int reserveBefore = ReserveAmmo;
        ReserveAmmo = (int)Math.Min(int.MaxValue, (long)ReserveAmmo + amount);
        Tell("reserve added", Ammo, reserveBefore);
    }

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
        Tell("reloaded while put away", Ammo - moved, ReserveAmmo + moved);
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
    /// 걱정 없이 시원시원하고 맛있게 쏠 수 있으니까"), and the room's slow motion as it begins (SkullSlowMotion; the user,
    /// 2026-11-18: "현재 장탄수만 최대치로 충전 (예비 탄약은 충전 제외)"). A reload under way is over at once, at any
    /// moment of it, its end after the rounds were in too, and so is what a cut-short one had left to do: the gun is
    /// loaded and can fire. The reserve stays exactly as it was: cutting the reload moves no round (CutShort) and the fill
    /// is not paid for. False when the gun was full and ready already.
    /// </summary>
    public bool TopUp()
    {
        if (Ammo >= Data.MagazineSize && !ResumeDue && !IsReloading) return false;
        int ammoBefore = Ammo, reserveBefore = ReserveAmmo;
        Interrupt();
        ForgetResume();
        Ammo = Data.MagazineSize;
        Tell("top-up", ammoBefore, reserveBefore);
        return true;
    }

    /// <summary>Fill the magazine and restore at least the starting reserve, preserving bonus ammunition.</summary>
    public void RefillAmmo() => RefillAmmo(Data.InitialReserveAmmo);

    /// <summary>
    /// As RefillAmmo, to <paramref name="reserveCap"/> rather than the starting reserve: what this gun's holder fills it to,
    /// which an ammo pouch card raises (PlayerWeaponSlots.ReserveCap). More already in reserve is kept.
    /// </summary>
    public void RefillAmmo(int reserveCap)
    {
        int ammoBefore = Ammo, reserveBefore = ReserveAmmo;
        Interrupt();
        ForgetResume();
        Ammo = Data.MagazineSize;
        ReserveAmmo = Mathf.Max(ReserveAmmo, reserveCap);
        Tell("refill", ammoBefore, reserveBefore);
    }
}
