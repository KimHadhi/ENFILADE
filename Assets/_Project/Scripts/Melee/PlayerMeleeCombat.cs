using System;
using UnityEngine;

/// <summary>
/// The melee slot's hands: the class knife's swings, the guard with Sekiro's parry, and V's shove and guard break.
/// A swing is not an instant (the user, 2026-11-18: "즉시 발동 대신 무기별 선딜레이 부여(… 꾹 누르면 선딜 이후 손이 떨리며
/// 차징됨)", "근접 적중 시 후딜레이 캔슬 제거"): the attack button starts the knife's wind-up; let go by its end, a light
/// swing leaves there; still held, the wind-up goes on into a charge, and letting go sends a stronger swing (the knife's
/// heavy clip from heavyPunchCharge on); the swing then plays its clip, which says itself when the blade is live and when
/// the next action may begin (KnifeSwing's marks), and nothing shortens that wait: not a hit, not the guard. All of those
/// moments are SwingTimeline's, stepped here with the button on the player's own PC and with the owner's words on a copy
/// (Wound, Punched, Dropped through PlayerNetworkCombat), so every PC's knife arm goes by the same clock (MeleeKnife).
/// A combo's strike (MeleeCombos: a riposte, a link, a chase, a beat) is the one thing that leaves with no wind-up and
/// may cut a recovery short: it is the reward for a press made in time, and it still waits for a blade that is live.
/// Until then the swing left on the button's release with no wind-up, a hit set the wait to nothing, and every knife
/// cut the same two bodies.
/// What a swing reaches and cuts is the knife's own blade as the arm shows it, from where the player stands (the arm
/// reaches a little for whoever it is aimed at; no knife steps in as the tables stand): PlayerMeleeCombat.Blade.cs,
/// which also writes every swing to Logs/knife-play.txt.
/// Who has the keys only decides what the input frame holds (the user's item 14, 2026-11-18): with the chat or a menu
/// up the buttons read as let go, which a charge takes as its release and a guard as its end, and nothing under way is
/// thrown away for it.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.MeleeCombat)]
public sealed partial class PlayerMeleeCombat : MonoBehaviour
{
    [Header("Attack")]
    [SerializeField, Min(0f), Tooltip("A press made while a swing is still committed is kept when the swing frees the next action within this long (s), and begins the next wind-up there.")]
    private float attackBuffer = .35f;
    [SerializeField, Range(0f, 1f), Tooltip("A swing charged at least this far is the knife's heavy one (its stab clip), and lands with the heavy hit sound.")]
    private float heavyPunchCharge = .6f;
    [SerializeField, Tooltip("A punch going out turns the view a little and lets it straight back (degrees: up, toward the "
        + "punching fist, roll toward it). Only the view: the aim stays (CameraViewPunch).")]
    private Vector3 punchThrowKick = new Vector3(-.3f, .8f, .6f);
    [SerializeField, Tooltip("A punch landing knocks the view (degrees: up, toward the fist, roll toward it), in real time so it "
        + "shows inside a hit stop.")]
    private Vector3 punchLandKick = new Vector3(-1f, .4f, .6f);
    [SerializeField, Min(0f), Tooltip("A punch landing narrows the view this much for a moment (degrees of field of view).")]
    private float punchLandZoom = 1.2f;
    [SerializeField, Min(1f), Tooltip("A full charge's kicks are this many times a quick punch's.")]
    private float punchChargedKick = 1.6f;
    [Header("Guard / Parry")]
    [Tooltip("Parry window per press, game seconds (Sekiro's deflect: 12 frames, then shorter each time the guard is let go and pressed again soon, down to 4 frames and then none). 0 means that press only guards.")]
    [SerializeField] private float[] parryWindowDurations = { .2f, .133f, .067f, 0f };
    [SerializeField, Min(0f), Tooltip("Guard left alone this long (s) gives the full window back, as a parry or a blocked hit does (Sekiro: 30 frames).")]
    private float spamResetDelay = .5f;
    [SerializeField, Min(0f)] private float guardInputBuffer = .12f;
    [SerializeField, Range(30f, 180f)] private float guardAngle = 150f;
    [SerializeField, Range(0f, 1f), Tooltip("Holding the guard up, the legs go at most this share of the walk speed, so guarding all the time costs something.")]
    private float guardMoveShare = .5f;
    [SerializeField, Min(0f), Tooltip("After a parry's hit stop the feet stay planted this much longer (real s). Punching, guarding and dodging still work.")]
    private float parryRootSeconds = .15f;
    [SerializeField, Min(0), Tooltip("Most blows one parry window turns (0: every blow in it, the user's choice; past it, a held guard blocks).")]
    private int parryMost;
    [Header("V: Shove / Guard Break")]
    [SerializeField, Min(0.05f)] private float holdThreshold = 0.14f;
    [SerializeField, Min(0.1f)] private float guardBreakReach = 2.2f;
    [SerializeField, Min(1f)] private float guardBreakApproachSpeed = 12f;
    [SerializeField, Min(0.05f)] private float guardBreakApproachTimeout = 0.24f;
    [SerializeField, Min(0f)] private float guardBreakStrikeDelay = 0.04f;
    [SerializeField, Min(0f)] private float guardBreakRecovery = 0.16f;
    [SerializeField, Min(0f)] private float guardBreakCooldown = 0.35f;
    [SerializeField, Min(0f)] private float guardBreakStun = 1.4f;
    [SerializeField, Min(0f)] private float guardBreakPostureDamage = 55f;
    [SerializeField] private LayerMask hitLayers = ~0;
    private PlayerMovement player;
    private PlayerWeaponSlots slots;
    private PlayerWeaponInput input;
    private PlayerFistAudio fistAudio;
    private MeleeCombos combos;
    private MeleeKnife knifeArm;
    private Transform aim;
    private MeleeCombatTarget target;
    private Collider targetCollider;
    // shoveWait: V's own wait after a shove or a guard break. It no longer holds the punches (League's auto-attack reset,
    // user 2026-11-11): a skill hands the punch straight back, as the spinning backfist (MeleeCombos link).
    private float shoveTime, shoveRest, shoveWait, grabTime, grabRecoveryRemaining, punchCharge;
    private float parryRemaining, guardBufferRemaining, guardIdleTime, rootUntil = float.NegativeInfinity;
    // This window's parries so far, when its first landed (real s), its strongest hit stop, and how near (squared m) the
    // attacker its riposte is open on stands.
    private float windowFirstAt, windowStop, windowRiposte;
    private int spamCount, windowParries;
    private ParryFeedback parryFeedback;
    private bool shovePending, grabConnected, guardAttemptHeld, defenseConnected, pendingWhiff, punchLanded;
    private readonly ShoveSweep.Attack shoveSweep = new ShoveSweep.Attack();

    // ---- The swing (SwingTimeline) ----
    private readonly SwingTimeline swing = new SwingTimeline();
    // The swing that leaves next when it is not the button's own: a combo's strike and its numbers, or one only shown
    // (the Knight's counter punch, a copy's word), which hits nothing here.
    private ComboStrike nextStrike;
    private MeleeCombatTarget nextAt;
    private float nextDamage = 1f, nextPosture = 1f, nextReach, nextReachIn;
    private int nextTargets;
    private bool nextShown;
    // The swing under way: what its blade deals, on whom a combo aimed it (and how near the eyes they must be for its
    // sure strike), and whether its blade has opened yet.
    private float swingDamage, swingPosture, swingReach;
    private int swingTargets;
    private MeleeCombatTarget swingAt;
    private bool swingJudged, bladeOpened;

    public bool IsGuarding { get; private set; }
    public bool IsParryActive => parryRemaining > 0f;
    /// <summary>The attack button is winding up or charging: the hands are up for it (PlayerBodyHands) and busy (PlayerPoint).</summary>
    public bool IsCharging => swing.Now == SwingTimeline.Phase.Windup || swing.Now == SwingTimeline.Phase.Charging;
    /// <summary>How far the wind-up has gone, 0 to 1 (1 while charging): the knife is drawn back by it (MeleeKnife).</summary>
    public float WindShare => swing.WindShare;
    /// <summary>How charged the hold is, 0 to 1 (0 until the wind-up is over): the knife trembles by it.</summary>
    public float ChargeRatio => swing.Now == SwingTimeline.Phase.Charging ? swing.Charge : 0f;
    /// <summary>A swing's clip is playing, and where in it (s of the clip): every PC's knife arm plays that moment (MeleeKnife).</summary>
    public bool IsSwinging => swing.Now == SwingTimeline.Phase.Swing;
    public float SwingClipTime => swing.ClipTime;
    public bool IsGuardBreaking => target != null;
    // Pinned by a runner the fists do nothing, unless a melee perk lets them fight it lying there (PlayerHold.FightsPinned).
    private bool MovementBlocksAttack => player.IsParkour || player.IsDiving || player.IsSliding || player.IsDowned
        || player.CurrentState == PlayerMovement.MovementState.StepDodge
        || player.CurrentState == PlayerMovement.MovementState.DashPush
        || (player.IsLandingRecovery && !player.FightsPinned) || (player.IsPinned && !player.FightsPinned);
    /// <summary>
    /// The knife's arm is out: winding up, charging, or a swing whose clip is not home yet. No parkour move begins over it
    /// (MovementAbilities.SwingHoldsBody), as no swing begins over a parkour move (MovementBlocksAttack): whichever the body
    /// began first keeps it. A vault or a ledge caught mid-swing used to drop the swing (SuspendActionsForMovement), so a
    /// jump at a head with the knife charged went over the target or hung from it and the stab was thrown away, its blade
    /// cut off mid-cut (Logs/knife-play.txt and ledge-probe.txt, 2026-11-21; the user: "칼을 차징하거나 휘두르고 있을 땐
    /// 파쿠르 안 되게"). The whole clip, not only up to its next mark: a jump's stab reaches its next mark about a quarter
    /// of a second after it leaves (KnifeMeleeStab: next 0.233 s of 0.367), with the body still in the air at the head,
    /// where the same vault would come right after it.
    /// </summary>
    public bool HoldsBody => swing.Now != SwingTimeline.Phase.Idle;
    public bool IsActive => isActiveAndEnabled && slots != null && slots.UsesFists && player != null && player.IsInitialized && player.DamageReceiver.IsAlive;
    /// <summary>The feet are planted after a parry (real time): the legs give no speed, the hands are free.</summary>
    public bool IsRooted => IsActive && Time.unscaledTime < rootUntil;
    /// <summary>A swing left (how charged, 0 to 1). LastStrike says which move it is while this is raised.</summary>
    public event Action<float> Punched;
    /// <summary>On the player's own PC: a wind-up began, for the other PCs' copies (PlayerNetworkCombat).</summary>
    public event Action Wound;
    /// <summary>On the player's own PC: a wind-up or a charge was let go of with no swing (the guard, V, a move), for the copies.</summary>
    public event Action Dropped;
    /// <summary>The combo move of the punch Punched is announcing, read during that event (FistsView); None otherwise.</summary>
    public ComboStrike LastStrike { get; private set; }
    /// <summary>This player's combos (the beat's step, for FistsView); null on a copy made before play.</summary>
    internal MeleeCombos Combos => combos;

    /// <summary>
    /// The speed the legs may go with the hands doing what they are doing (LocomotionState asks): nothing while rooted
    /// after a parry, a slow walk with the guard held up (as Sekiro's guard walk), otherwise speed as it is.
    /// </summary>
    public float LimitMoveSpeed(float speed)
    {
        if (!IsActive) return speed;
        if (IsRooted) return 0f;
        return IsGuarding ? Mathf.Min(speed, player.WalkSpeed * guardMoveShare) : speed;
    }

    private void OnEnable()
    {
        player = GetComponentInParent<PlayerMovement>();
        slots = GetComponent<PlayerWeaponSlots>(); input = GetComponent<PlayerWeaponInput>();
        var camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        // Punches start from the eyes, which stay with the body in the F5 view.
        aim = camera != null ? PlayerView.EyesOf(player) : transform;
        if (player != null) player.MeleeCombat = this;
        if (Application.isPlaying && camera != null && GetComponent<FistsView>() == null) gameObject.AddComponent<FistsView>();
        fistAudio = GetComponent<PlayerFistAudio>();
        if (fistAudio == null && Application.isPlaying) fistAudio = gameObject.AddComponent<PlayerFistAudio>();
        if (fistAudio != null) fistAudio.Prepare();
        combos = MeleeCombos.For(player);
        swing.Leaving = OnLeaving;
        CancelAction();
    }
    /// <summary>On someone else's copy: their guard went up or down.</summary>
    internal void ShowGuard(bool guarding) => IsGuarding = guarding;
    /// <summary>On someone else's copy: their wind-up began. It goes on into its charge here as theirs does, until their swing or their letting go comes.</summary>
    internal void ShowWind()
    {
        Weigh();
        swing.Wind();
    }
    /// <summary>On someone else's copy: their wind-up or charge was let go of with no swing.</summary>
    internal void ShowDrop()
    {
        if (IsCharging) swing.Drop();
    }
    /// <summary>
    /// On someone else's copy: their swing left, charged this far, as this move, their arm reaching this much further
    /// ahead to meet whom it is aimed at (m). Their PC judged what it hit.
    /// </summary>
    internal void ShowPunch(float charge, ComboStrike strike = ComboStrike.None, float reachIn = 0f)
    {
        Weigh();
        nextStrike = strike;
        nextShown = true;
        nextReachIn = Mathf.Max(0f, reachIn);
        swing.Strike(charge);
    }
    private void Update()
    {
        if (player != null && player.IsNetworkDriven)
        {
            // A copy's timeline only runs on: its wind-up into its charge (the button counts as held until their word
            // comes), its swing's clip to its end.
            if (IsActive) swing.Tick(player.DeltaTime, true, false);
            else swing.Drop();
            return;
        }
        if (!IsActive) { CancelAction(); TickGuardInput(player != null ? player.DeltaTime : 0f, default); return; }
        CutWithoutArm();
        // The hands not the player's this frame (the chat, a menu, the window behind another): every button reads as let
        // go, and the frame goes on with that. It used to throw away whatever was under way.
        if (IsGuardBreaking)
        {
            ProcessFrame(player.DeltaTime, input != null && input.TryReadGuardBuffer(out var guardFrame) ? guardFrame : default);
            return;
        }
        ProcessFrame(player.DeltaTime, input != null && input.TryReadMeleeInput(out var frame) ? frame : default);
    }
    private void LateUpdate()
    {
        // PlayerLook updates after input processing. Include this frame's final camera rotation.
        if (Time.timeScale <= 0f || (player != null && player.IsNetworkDriven)) return;
        if (!IsActive || player.Execution.OrNull()?.IsExecuting == true) { CancelAction(); return; }
        if (IsGuardBreaking) return;
        if (MovementBlocksAttack) { SuspendActionsForMovement(); return; }
        shoveSweep.Tick(0f);
    }
    /// <summary>A counter hands the shove straight back (ShoveCounter): the fists can shove again at once.</summary>
    internal void RefreshShove() { shoveRest = shoveWait = 0f; }
    public void ProcessFrame(float dt, MeleeInputFrame frame)
    {
        if (dt <= 0f) return;
        if (!IsActive || player.Execution.OrNull()?.IsExecuting == true) { CancelAction(); return; }
        shoveRest = Mathf.Max(0f, shoveRest - dt);
        shoveWait = Mathf.Max(0f, shoveWait - dt);
        TickGuardInput(dt, frame);
        if (IsGuardBreaking)
        {
            float buffered = guardBufferRemaining;
            TickGuardBreak(dt);
            guardBufferRemaining = buffered;
            return;
        }
        if (MovementBlocksAttack)
        { SuspendActionsForMovement(); return; }
        Weigh();
        // A combo's dash in takes no other press; a guard press calls it off and guards (MeleeCombos).
        if (combos != null && combos.IsDashing)
        {
            if (!frame.GuardPressed)
            {
                // The arm of a swing still going home keeps moving through the dash.
                swing.Tick(dt, false, false);
                return;
            }
            combos.CancelDash();
        }
        // A combo strike goes on the press itself, ahead of the guard, the wind-up and a recovery (MeleeCombos): the
        // riposte after a parry, the guard still held or not. Only a blade still live makes it wait.
        else if (frame.AttackPressed && !BladeLive && combos != null && combos.TryTake()) return;
        // The guard comes up at once out of a wind-up or a charge, which it lets go of (a feint costs nothing), but a
        // swing that has left is committed until its clip frees the next action: the guard waits for that too, a press
        // kept for guardInputBuffer and a hold coming up as a block. It used to cut any swing, and gave the next one at once.
        bool committed = swing.Committed;
        if (!committed)
        {
            if (guardBufferRemaining > 0f) BeginGuard(frame.GuardHeld);
            else if (frame.GuardHeld && !IsGuarding)
            {
                // A long hold through an uninterruptible action resumes block, never a fresh parry.
                CancelOffense(); IsGuarding = true; guardAttemptHeld = true; defenseConnected = false;
            }
        }
        if (IsGuarding) return;
        shoveSweep.Tick(dt);
        TickStep(dt);
        // Crouched, V is the leg sweep round the front at once, no hold to wait for (ShoveMoves; it keeps its own interval).
        if (frame.ShovePressed && player.IsCrouching && !committed && shoveWait <= 0f)
        {
            DropSwing(); shovePending = false; IsGuarding = false; parryRemaining = 0f;
            ShoveMoves.For(player).OrNull()?.TrySweep();
        }
        else if (frame.ShovePressed && !committed && shoveWait <= 0f)
        { DropSwing(); shoveSweep.Cancel(); shovePending = true; shoveTime = 0f; IsGuarding = false; parryRemaining = 0f; }
        if (shovePending)
        {
            shoveTime += dt;
            if (frame.ShoveHeld && shoveTime >= holdThreshold)
            {
                shovePending = false; shoveWait = guardBreakCooldown;
                TryStartGuardBreak(); // Holding never also emits the tap shove on release.
            }
            else if (frame.ShoveReleased)
            {
                // Fatigue only slows the next shove; punches keep their own cooldown.
                shovePending = false;
                if (shoveRest <= 0f)
                {
                    // The wait is set before the shove goes out: a counter on its first contact clears it again (RefreshShove).
                    shoveWait = 0.55f;
                    shoveRest = player.Abilities.Combat.Fatigue.Spend(shoveWait, player.AbilitySettings);
                    Shove();
                    combos.OrNull()?.OpenLink();
                }
            }
            else if (!frame.ShoveHeld) shovePending = false;
        }
        // The attack button, unless V has the hand this frame: the timeline takes it from here (a press kept while a
        // swing is committed, the wind-up, the charge, the swing leaving through OnLeaving).
        bool mine = !shovePending && !IsGuardBreaking;
        var happened = swing.Tick(dt, mine && frame.AttackHeld, mine && frame.AttackPressed);
        if ((happened & SwingTimeline.Events.Wound) != 0)
        {
            parryRemaining = 0f;
            Wound?.Invoke();
            // Still winding up (a wind-up shorter than the frame has left already, aimed as it did).
            if (IsCharging) AimWindup();
        }
        OpenBlade();
        NoteLive();
    }
    // The knife in hand sets the timeline's pace: its wind-up, its charge, how fast all of it runs (ClassKnives.csv).
    private void Weigh()
    {
        var knife = MeleeKnife.Held(player);
        swing.Windup = knife.Windup;
        swing.ChargeTime = knife.ChargeTime;
        swing.Speed = knife.AttackSpeed;
        swing.Buffer = attackBuffer;
    }
    // The blade of the swing under way is live now (its clip is inside its marks), or has yet to be.
    private bool BladeLive => swing.Now == SwingTimeline.Phase.Swing && swing.ClipTime < swing.Marks.To;
    private void TickGuardInput(float dt, MeleeInputFrame frame)
    {
        parryRemaining = Mathf.Max(0f, parryRemaining - dt);
        guardBufferRemaining = Mathf.Max(0f, guardBufferRemaining - dt);
        if (!frame.GuardHeld)
        {
            if (guardAttemptHeld)
            {
                if (!defenseConnected) pendingWhiff = true;
                guardAttemptHeld = false; guardIdleTime = 0f;
            }
            else guardIdleTime += dt;
            IsGuarding = false;
            if (guardIdleTime >= Mathf.Max(0f, spamResetDelay)) ResetParrySpam();
        }
        else guardIdleTime = 0f;
        if (frame.GuardPressed) guardBufferRemaining = Mathf.Max(.001f, guardInputBuffer);
    }
    private void BeginGuard(bool held)
    {
        int last = parryWindowDurations != null ? parryWindowDurations.Length - 1 : -1;
        spamCount = Mathf.Clamp(spamCount + (pendingWhiff ? 1 : 0), 0, Mathf.Max(0, last));
        // A Knight's windows are longer, the shrinking ones as much (PlayerClass).
        float duration = (last >= 0 ? parryWindowDurations[spamCount] : .2f) * PlayerClass.ParryWindowFactor(player);
        // A window of 0 is Sekiro's mashing floor: that press only guards.
        parryRemaining = float.IsNaN(duration) || float.IsInfinity(duration) ? .2f : Mathf.Max(0f, duration);
        windowParries = 0;
        pendingWhiff = !held; defenseConnected = false; guardAttemptHeld = held;
        IsGuarding = held; guardIdleTime = guardBufferRemaining = 0f;
        CancelOffense();
    }
    private void CancelOffense()
    {
        DropSwing(); shoveSweep.Cancel();
        shovePending = false; shoveTime = 0f;
    }
    // Whatever the attack button had under way is let go of; a wind-up or a charge that ends so is told to the copies.
    private void DropSwing()
    {
        bool winding = IsCharging;
        EndBlade();
        swing.Drop();
        nextStrike = ComboStrike.None; nextAt = null; nextShown = false;
        swingAt = null; swingJudged = false;
        if (winding && player != null && !player.IsNetworkDriven) Dropped?.Invoke();
    }
    private void SuspendActionsForMovement()
    {
        float buffered = guardBufferRemaining;
        CancelAction();
        guardBufferRemaining = buffered;
    }
    private void ResetParrySpam() { spamCount = 0; pendingWhiff = false; }
    public void NotifyDamaged() { ResetParrySpam(); defenseConnected = true; }
    /// <summary>A riposte lets the feet go at once (MeleeCombos): the parry's planted feet would hold its step in.</summary>
    internal void FreeFeet() => rootUntil = float.NegativeInfinity;
    /// <summary>
    /// A combo strike goes now (MeleeCombos): a swing at full charge times the move's own numbers, on the press, with no
    /// wind-up, the guard dropped for it and whatever recovery was left cut short. Aimed at someone, it lands on them as
    /// its blade goes live, when they are within the move's reach of the eyes with nothing solid between; and its blade
    /// cuts whoever else it passes, as any swing's does (PlayerMeleeCombat.Blade.cs).
    /// </summary>
    internal void StrikeCombo(ComboStrike strike, MeleeCombatTarget at = null)
    {
        if (!IsActive || combos == null) return;
        DropSwing();
        shovePending = false; shoveTime = 0f;
        IsGuarding = guardAttemptHeld = false; parryRemaining = guardBufferRemaining = 0f;
        Weigh();
        combos.Numbers(strike, out nextDamage, out nextPosture, out nextReach, out nextTargets);
        nextStrike = strike; nextAt = at; nextShown = false;
        swing.Strike(1f);
        OpenBlade();
    }
    // A swing leaves (SwingTimeline asks for its clip's marks): the button's own, a combo's strike, or one only shown.
    // The knife arm picks the clip (MeleeKnife.Take: a slash, its backhand, the heavy one), and on the player's own PC the
    // swing's numbers are set for when its blade goes live (OpenBlade) and the swing is aimed (AimSwing).
    private SwingMarks OnLeaving(float charge)
    {
        var strike = nextStrike; var at = nextAt; bool shown = nextShown;
        float damageFactor = strike != ComboStrike.None ? nextDamage : 1f, postureFactor = strike != ComboStrike.None ? nextPosture : 1f;
        float strikeReach = strike != ComboStrike.None ? nextReach : 0f, shownReachIn = shown ? nextReachIn : 0f;
        int targets = strike != ComboStrike.None ? nextTargets : 0;
        nextStrike = ComboStrike.None; nextAt = null; nextShown = false; nextReachIn = 0f;
        if (knifeArm == null && player != null) knifeArm = player.GetComponent<MeleeKnife>();
        var clip = knifeArm != null ? knifeArm.Take(charge, strike, heavyPunchCharge) : null;
        bool judged = !shown && player != null && !player.IsNetworkDriven;
        swingJudged = judged; bladeOpened = false; swingAt = judged ? at : null;
        EndBlade();
        SwingReachIn = shownReachIn;
        if (judged)
        {
            var knife = MeleeKnife.Held(player);
            // The beat counts from the punch leaving, before its blade can land (MeleeCombos).
            combos.OrNull()?.OnPunchThrown(strike);
            punchLanded = false; punchCharge = charge; fistAudio.OrNull()?.PlaySwing();
            throwFelt = landFeltLater = false;
            // A Knight's blade hits harder, flesh and posture (PlayerClass).
            float fist = PlayerClass.FistFactor(player);
            swingDamage = Mathf.Lerp(knife.Slash, knife.Stab, charge) * fist * damageFactor;
            swingPosture = Mathf.Lerp(knife.SlashPosture, knife.StabPosture, charge) * fist * postureFactor;
            swingReach = strikeReach;
            // A combo's own count of bodies (a spin goes through three), else what the knife cuts.
            swingTargets = targets > 0 ? targets : knife.Penetration;
            // The swing is aimed at the one a combo names, else at the one under the crosshair: the arm reaches for them
            // (and a knife with a lunge steps in).
            NoteLeave(strike, charge, clip);
            AimSwing(at, clip, knife, clip != null ? clip.ActiveFrom / Mathf.Max(.01f, swing.Speed) : 0f, "leave");
        }
        LastStrike = strike;
        Punched?.Invoke(charge);
        LastStrike = ComboStrike.None;
        return clip != null ? clip.Marks : default;
    }
    // The aimed combo strike: on that body when it is within reach of the eyes with nothing solid between, as the
    // perfect-defense counter's punch lands (PlayerPerfectDefense.Strike).
    private bool StrikeTarget(MeleeCombatTarget at, float dealt, float posture, float strikeReach)
    {
        var body = at.GetComponentInChildren<Collider>();
        var victim = at.GetComponent<IDamageReceiver>();
        if (body == null || victim == null || !at.IsAvailable) return false;
        Vector3 point = body.ClosestPoint(aim.position), delta = point - aim.position;
        if (delta.magnitude > strikeReach || !HasLineOfSight(body, point)) return false;
        // This PC's own player's fists carry their perk, as the blade's cut does (PlayerPerks).
        if (player.IsLocal) dealt *= PlayerPerks.FistFactor;
        var result = victim.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), dealt, DamageKind.Attack, point,
            delta.sqrMagnitude > .0001f ? delta.normalized : aim.forward, player.gameObject, true, posture, method: MeleeKnife.MethodOf(player)));
        if (result != DamageResult.Damaged) return false;
        at.ApplyPostureDamage(posture);
        OnPunchLanded();
        return true;
    }
    // The blade met someone. Nothing about the swing's timing changes for it: until 2026-11-18 this set the wait to
    // nothing, so a hit could be followed at once and only a miss had a recovery (the user: "근접 적중 시 후딜레이 캔슬 제거").
    private void OnPunchLanded()
    {
        // One hit cue per punch, even when it lands on several targets; and one beat waiting after it (MeleeCombos).
        if (!punchLanded)
        {
            if (punchCharge >= heavyPunchCharge) fistAudio.OrNull()?.PlayHeavyHit();
            else fistAudio.OrNull()?.PlayHit();
            combos.OrNull()?.OnPunchLanded();
            // The knock needs the hand, which FistsView picks as the swing leaves: a blow landing before that
            // gets its knock from FeelThrow.
            if (throwFelt) FeelLanding();
            else landFeltLater = true;
        }
        punchLanded = true;
    }

    // The punch's feel on this PC's own view (the user, 2026-11-12, as the head stays steadier through a punch and the
    // camera with it: "손맛은 회전으로"): a little turn toward the fist as it goes out, and a knock and a narrowing as it
    // lands. Only the view kicks (CameraViewPunch); the aim keeps to the steady view.
    private bool throwFelt, landFeltLater, punchFromRight = true;
    private Camera ownView;

    /// <summary>A punch went out from this hand (FistsView): the view turns toward the fist, unless the strike turns the
    /// view itself (<paramref name="viewTurns"/>, the spinning backfist's whip).</summary>
    internal void FeelThrow(bool right, bool viewTurns)
    {
        punchFromRight = right;
        throwFelt = true;
        if (!viewTurns && OwnViewPunch(out var punch)) punch.Punch(TowardFist(punchThrowKick) * ChargedKick);
        if (!landFeltLater) return;
        landFeltLater = false;
        FeelLanding();
    }

    private void FeelLanding()
    {
        if (OwnViewPunch(out var punch)) punch.ImpactPunch(TowardFist(punchLandKick) * ChargedKick, -punchLandZoom * ChargedKick);
    }

    private float ChargedKick => Mathf.Lerp(1f, punchChargedKick, punchCharge);

    // Up, toward the punching fist and rolled toward it: yaw is to the right and roll to the left for positive values.
    private Vector3 TowardFist(Vector3 kick)
    {
        float side = punchFromRight ? 1f : -1f;
        return new Vector3(kick.x, kick.y * side, -kick.z * side);
    }

    private bool OwnViewPunch(out CameraViewPunch punch)
    {
        punch = null;
        if (player == null || !player.IsLocal || player.IsNetworkDriven) return false;
        if (ownView == null) ownView = player.GetComponentInChildren<Camera>();
        punch = CameraViewPunch.For(ownView);
        return punch != null;
    }
    private void OnPunchWallHit() => fistAudio.OrNull()?.PlayWallHit();
    /// <summary>The Knight's counter lands its own blow (PlayerPerfectDefense): the knife arm only shows the swing.</summary>
    public void ShowCounterPunch(bool landed)
    {
        if (IsActive) { fistAudio.OrNull()?.PlaySwing(); if (landed) fistAudio.OrNull()?.PlayHeavyHit(); }
        Shown(1f);
    }
    // A swing that is only seen: it leaves now and hits nothing by itself.
    private void Shown(float charge)
    {
        EndBlade();
        Weigh();
        nextStrike = ComboStrike.None; nextAt = null;
        nextShown = true;
        swing.Strike(charge);
    }
    /// <summary>The counter's back elbow, driven back on the attacker's side (+1 right): the fist pulls back past the shoulder.</summary>
    public event Action<int> Elbowed;
    public void ShowCounterElbow(bool landed, int side)
    {
        if (IsActive) { fistAudio.OrNull()?.PlaySwing(); if (landed) fistAudio.OrNull()?.PlayHeavyHit(); }
        Elbowed?.Invoke(side);
    }
    /// <summary>
    /// On someone else's copy: their counter's back elbow (+1 right). Its fist pulls back as on their own screen (FistsView),
    /// and the body's hands follow it. Their PC judged it, and its swing and hit sounds already came as fist sounds.
    /// </summary>
    internal void ShowElbow(int side) => Elbowed?.Invoke(side);
    private void Shove()
    {
        shoveSweep.Begin(player, aim, 3f, hitLayers, player.AbilitySettings.shoveActiveTime);
        // Standing still, it lunges at someone just out of reach, as the gun's shove does (ShoveMoves).
        ShoveMoves.For(player).Lunge(aim, player.AbilitySettings.shoveSweepReach, hitLayers);
    }
    public bool TryDefend(DamageHit hit, out DamageResult result)
    {
        result = DamageResult.Ignored;
        // The window turns every blow that comes in it, not just the first (the user, 2026-11-15: "지금 패링 동시에 한 명밖에
        // 못 해? … 동시에 여러 명이 때리면 패링했을 때 다 되는 게 아니야?" — "응, 다 튕기게"): it stays open to its end,
        // up to parryMost blows when that is set (0: no limit; past it, a held guard blocks).
        bool window = IsParryActive && (parryMost <= 0 || windowParries < parryMost);
        // A blow that can't be guarded is still a Knight's to parry, though never to block (PlayerClass.ParriesAll: a
        // runner's leap, a grab, an elite's blow from behind). Whether the keys are the player's is not asked: a guard
        // already up, or a window already open, turns the blow whoever has them (the input frame lets the guard go).
        if (!IsActive || (!IsGuarding && !window) || IsGuardBreaking
            || MovementBlocksAttack || hit.Kind != DamageKind.Attack
            || !(hit.CanBeGuarded || (window && PlayerClass.ParriesAll(player, hit)))) return false;
        Vector3 incoming = hit.Source != null ? hit.Source.transform.position - player.transform.position : -hit.Direction;
        incoming = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        Vector3 facing = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        if (incoming.sqrMagnitude < 0.01f || Vector3.Dot(incoming, facing) < Mathf.Cos(guardAngle * 0.5f * Mathf.Deg2Rad)) return false;
        result = window ? DamageResult.Parried : DamageResult.Blocked;
        ResetParrySpam(); defenseConnected = true;
        if (window)
        {
            windowParries++;
            var attacker = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
            // Each attacker's own: a Knight's parry breaks a pack zombie at once and wears an elite down harder
            // (PlayerClass.ParryPosture). Taken before the parry is told, so a break shows and slows into the execution as
            // one (ParryFeedback, PlayerPerfectDefense, which also give each attacker its sound, burst and reel).
            if (attacker != null) attacker.ApplyPostureDamage(PlayerClass.ParryPosture(player, attacker, 25f), 0.8f);
            // The window's, once: the deflect plants the feet, the hit stop holding everything and the legs staying put a
            // moment longer, as long as the window's strongest stop asks, counted from its first parry and never stacked.
            if (parryFeedback == null) parryFeedback = GetComponent<ParryFeedback>();
            float stop = parryFeedback != null ? parryFeedback.HitStopFor(attacker != null && attacker.IsStaggered) : 0f;
            if (windowParries == 1) { windowFirstAt = Time.unscaledTime; windowStop = 0f; }
            windowStop = Mathf.Max(windowStop, stop);
            rootUntil = windowFirstAt + windowStop + parryRootSeconds;
            player.HorizontalVelocity = Vector3.zero;
            // An attack press now goes straight back at the nearest of the window's attackers (MeleeCombos riposte).
            float near = attacker != null ? (attacker.transform.position - player.transform.position).sqrMagnitude : float.MaxValue;
            if (windowParries == 1 || near < windowRiposte)
            {
                windowRiposte = near;
                combos.OrNull()?.OpenRiposte(attacker);
            }
        }
        return true;
    }
    private bool TryStartGuardBreak()
    {
        if (!player.IsGrounded || !FindContact(guardBreakReach, out var hit, out _)) return false;
        var candidate = hit.GetComponentInParent<MeleeCombatTarget>();
        if (candidate == null || !candidate.TryReserve(this)) return false;
        target = candidate; targetCollider = hit; grabTime = grabRecoveryRemaining = 0f; grabConnected = false;
        IsGuarding = false; parryRemaining = 0f;
        DropSwing();
        player.HorizontalVelocity = Vector3.zero;
        player.Abilities.Dodge.CancelWindow(); player.Abilities.Lean.Reset();
        CombatCueAudio.Play(gameObject, CombatCue.Grab);
        return true;
    }
    private void TickGuardBreak(float dt)
    {
        if (dt <= 0f) return;
        if (target == null || !target.IsAvailable || targetCollider == null || !player.IsGrounded)
        { CancelAction(); return; }
        grabTime += dt;
        if (grabConnected)
        {
            grabRecoveryRemaining -= dt;
            // Landed, it hands the punch straight back as the spinning backfist; V waits on its own.
            if (grabRecoveryRemaining <= 0f) { CancelAction(); shoveWait = guardBreakCooldown; combos.OrNull()?.OpenLink(); }
            return;
        }
        Vector3 center = player.transform.position + Vector3.up * Mathf.Min(1f, player.ProneColliderHeight + 0.5f);
        Vector3 delta = Vector3.ProjectOnPlane(targetCollider.ClosestPoint(center) - center, Vector3.up);
        if (delta.magnitude > guardBreakReach + 0.3f) { CancelAction(); return; }
        // Recheck cover and identity throughout approach; never teleport through intervening geometry.
        if (!HasLineOfSight(targetCollider, targetCollider.ClosestPoint(aim.position))) { CancelAction(); return; }
        if (delta.magnitude <= 0.85f && grabTime >= guardBreakStrikeDelay)
        {
            grabConnected = true;
            grabRecoveryRemaining = guardBreakRecovery;
            target.ApplyPostureDamage(guardBreakPostureDamage, guardBreakStun);
            fistAudio.OrNull()?.PlayHeavyHit();
            BashImpact.Play(targetCollider.bounds.ClosestPoint(aim.position), Vector3.ProjectOnPlane(aim.forward, Vector3.up), 1.1f, player);
        }
        else if (grabTime >= guardBreakApproachTimeout) CancelAction();
        else if (delta.magnitude > 0.65f)
            player.Motor.AddDisplacement(delta.normalized * Mathf.Min(delta.magnitude - 0.65f, guardBreakApproachSpeed * dt));
    }
    private bool FindContact(float distance, out Collider collider, out Vector3 point)
    {
        collider = null; point = default;
        float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.SphereCastAll(aim.position, 0.22f, aim.forward, distance, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(player.transform) || hit.distance >= nearest) continue;
            nearest = hit.distance; collider = hit.collider; point = hit.point;
        }
        // Sphere casts omit colliders which already overlap the origin.
        foreach (var overlap in Physics.OverlapSphere(aim.position, 0.22f, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (overlap.transform.IsChildOf(player.transform)) continue;
            if (!ShoveSweep.TryGetContactPoint(overlap, aim.position, aim.forward, .22f, 0f, out var closest)) continue;
            if (Vector3.Dot(closest - aim.position, aim.forward) < -0.01f) continue;
            collider = overlap; point = closest; break;
        }
        return collider != null && HasLineOfSight(collider, point);
    }
    private bool HasLineOfSight(Collider candidate, Vector3 point)
    {
        Vector3 direction = point - aim.position;
        foreach (var hit in Physics.RaycastAll(aim.position, direction.normalized, direction.magnitude, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(player.transform) || hit.collider == candidate) continue;
            var receiver = candidate.GetComponentInParent<IDamageReceiver>();
            if (receiver != null && hit.collider.GetComponentInParent<IDamageReceiver>() == receiver) continue;
            return false;
        }
        return true;
    }
    /// <summary>
    /// Everything the hands had under way ends, for a reason of the game's: the melee slot put away, an execution, a
    /// move that takes the body, death. Never because input became unavailable.
    /// </summary>
    public void CancelAction()
    {
        DropSwing(); shoveSweep.Cancel();
        combos.OrNull()?.CancelDash();
        if (target != null) target.Release(this);
        target = null; targetCollider = null;
        if (guardAttemptHeld && !defenseConnected) pendingWhiff = true;
        shovePending = IsGuarding = guardAttemptHeld = false;
        shoveTime = parryRemaining = guardBufferRemaining = 0f;
    }
    private void OnDisable() { CancelAction(); input.OrNull()?.RequireTriggerRelease(); }
}
