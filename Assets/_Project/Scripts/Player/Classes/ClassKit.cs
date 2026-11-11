using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A player's class at work (PlayerClass), on every player; the live part runs for this PC's own:
/// - its starting guns (the class's primary and secondary, slots 1 and 2) as each chapter begins;
/// - its ability on the class key (Player/ClassAbility, the wheel click by default, rebindable in Settings → 조작);
/// - Assault: every kill adds a stack of adrenaline (harder hits and a quicker pace a stack), which holds a few seconds
///   after the last kill and then ebbs; full, the class key spends it all on a CHARGE, Guts &amp; Blackpowder's officer's
///   charge: for some seconds it and every friend near hit harder and run faster (friends' PCs are told,
///   PlayerNetworkCombat, and each gives its own player the buff);
/// - Point: a gun put away reloads itself in the time its own reload takes, from its reserve, so the SMG empties while
///   the shotgun refills and back (Killing Floor 2's gunslinger feel, the user's "화력이 끊기지 않고");
/// - Ghost (stage 2; the user's design, with the Headhunter and Deadeye of their old class memo): a headshot adds a
///   Head Hunter stack, a shot into a body takes one away and any hurt taken loses them all, and every stack makes each
///   shot hit harder, compounding, so a steady hand grows into the elite killer of the late game whatever gun it holds.
///   The first round after a reload hits double (Deadeye). Its own guns stagger an elite they hit: a short stun that
///   breaks off its blow or its charge, longer for a headshot, each elite at most once in a while (the early game's
///   job, before the cards make it strong);
/// - Knight (stage 2; the user's design, with the memo's Perfect Parry and Deflect): it takes less from every enemy's
///   blow; its parries last longer, wear posture down harder and open the counter as a perfect dodge does; its counters
///   and fists hit harder; pinned under a runner it fights on; and the class key taunts: the zombies already fighting
///   near it go after it for a few seconds, elites too, while it takes less again (the host's zombies, as
///   PlayerNetworkCombat tells the host), so the elites keep off the shooters;
/// - Schießhund (stage 3; the user's design, with the Jagdfieber and Return Back of their old memo): its F ping is a
///   teal bounty mark (PlayerPing, PingMarks), one at a time, which its bullets hit harder. A marked enemy it kills pays
///   a bounty: money (CombatFeedHUD), a Return Back stack (quicker and harder-hitting a while), and points toward the
///   Desperado's next round. The Desperado is its own revolver in slot 4 (the class's own gun, the R08; slot 2 holds a
///   plain pistol, so the two are never mixed up: the user's word), loaded by bounties alone and never reloaded: every
///   round hits double, and one in the head of its own mark kills it outright, elites too (a brute loses a share of its
///   health), with BOUNTY slammed across the screen in teal and burst in the world for everyone to see (BountyBurst;
///   a plain bounty spills its coins there too).
/// - Détente (the user's design): its syringe, Golden Hour and Resurrection are DetenteKit's; the class key injects.
/// - Support (stage 4): its supply bag, resupplying a friend with F and its barricades are SupportKit's; the class key
///   puts a barricade up.
/// The other classes' abilities come in later stages (Classes.csv ready 0). Numbers: Tables/ClassAbilities.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassKit : MonoBehaviour
{
    private const string AssaultRow = "Assault", PointRow = "Point", GhostRow = PlayerClass.GhostRow, KnightRow = PlayerClass.KnightRow,
        HoundRow = PlayerClass.HoundRow;
    // Elites remembered as lately staggered: past this many, those free again are forgotten.
    private const int StaggerMemory = 32;

    /// <summary>This PC's own player's kit.</summary>
    public static ClassKit Local { get; private set; }

    /// <summary>This PC's player spent a full adrenaline on a CHARGE here: friends' PCs are told (PlayerNetworkCombat).</summary>
    public event Action<Vector3> Charged;
    /// <summary>This PC's Knight taunted here: the other PCs are told (PlayerNetworkCombat), and the host's zombies go after them.</summary>
    public event Action<Vector3> Taunted;
    /// <summary>
    /// This PC's Schießhund claimed a bounty: where the head was, where the shot came from, the ground under it, and
    /// whether the Desperado claimed it (the grand burst) or a plain kill (the coins). The other PCs are told (PlayerNetworkCombat).
    /// </summary>
    public event Action<Vector3, Vector3, Vector3, bool> Claimed;

    private PlayerMovement movement;
    private PlayerWeaponSlots slots;
    private WeaponController weapon;
    private PlayerDamageReceiver receiver;
    private PlayerHold hold;
    private InputAction ability;
    private bool kitGiven, wired;
    private int stacks;
    private float lastKill = -99f, nextEbb, chargeUntil, chargeTotal = 1f;
    // Point: when each gun slot's gun was last seen put away and short (-1: not waiting), and when one last refilled.
    private readonly float[] asideSince = { -1f, -1f }, refilledAt = { -99f, -99f };
    // Ghost: the Head Hunter stacks, their last change (+1 one more, -1 one fewer, -2 all lost) and when; which of the two
    // gun slots holds a gun loaded and not fired since (Deadeye); what the shot under way has met so far, pellet by
    // pellet; and when each elite this Ghost staggered may be staggered again.
    private int hunter, hunterChange;
    private float hunterChangedAt = -99f;
    private readonly bool[] loaded = { true, true };
    private bool shotHead, shotBody;
    private readonly Dictionary<MeleeCombatTarget, float> staggerAgain = new Dictionary<MeleeCombatTarget, float>();
    private readonly List<MeleeCombatTarget> forget = new List<MeleeCombatTarget>();
    // Knight: when the taunt under way ends and how long it lasts; when it may be used again and how long that wait is.
    private float tauntUntil, tauntTotal = 1f, tauntReadyAt, tauntWait = 1f;
    // Schießhund: Desperado rounds and the points toward the next; Return Back's stacks and when they go; when the last
    // bounty came; and the bounty a Desperado round was just put in the head of, for its BOUNTY.
    private int bountyPoints, returnStacks;
    private float returnUntil, returnTotal = 1f, bountyAt = -99f, finishAt = -99f;
    private EnemyHealth finishing;
    // Where the Desperado's round went into its head and where it was fired from (for the burst).
    private Vector3 finishPoint, finishFrom;
    // The special slot, where a Schießhund's Desperado sits.
    private const int DesperadoSlot = 3;
    private bool pingMade;
    private DetenteKit detente;
    private SupportKit support;

    public PlayerMovement Movement => movement;
    public WeaponData HeldGun => weapon != null ? weapon.EquippedData : null;

    /// <summary>Adrenaline stacks now, and the most (an Assault's).</summary>
    public int Stacks => stacks;
    public int StackMax => Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(AssaultRow, "stackMax", 8f)));
    /// <summary>An Assault with full adrenaline: the class key charges.</summary>
    public bool ChargeReady => PlayerClass.Is(PlayerClass.Assault) && stacks >= StackMax;
    /// <summary>How much of a CHARGE this player is in is left, 0 to 1 (0: none).</summary>
    public float ChargeShare => chargeUntil > Time.time ? Mathf.Clamp01((chargeUntil - Time.time) / chargeTotal) : 0f;
    /// <summary>A CHARGE this player sounds: how long it lasts and how far it reaches, their class cards in (friends' PCs are sent them).</summary>
    public float ChargeSeconds => Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "chargeSeconds", 7f));
    public float ChargeReach => Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "chargeRadius", 15f));
    /// <summary>A taunt this Knight makes: how far it reaches and how long it holds, their class cards in (the host is sent them).</summary>
    public float TauntReach => Mathf.Max(0f, PlayerClass.Ability(KnightRow, "tauntRadius", 12f));
    public float TauntSeconds => Mathf.Max(.1f, PlayerClass.Ability(KnightRow, "tauntSeconds", 6f));

    /// <summary>Point: how far slot's gun put away is on its way to refilled (0 to 1), and seconds since it last refilled.</summary>
    public float AsideShare(int slot)
    {
        if (slot < 0 || slot > 1 || asideSince[slot] < 0f || slots == null) return 0f;
        var gun = slots.GetRuntime(slot);
        return gun != null ? Mathf.Clamp01((Time.time - asideSince[slot]) / AsideTime(gun)) : 0f;
    }
    public float SinceRefill(int slot) => slot >= 0 && slot <= 1 ? Time.time - refilledAt[slot] : 99f;

    /// <summary>A Ghost's Head Hunter stacks now, and the most (the cards will raise it).</summary>
    public int Hunter => hunter;
    public int HunterMax => Mathf.Clamp(Mathf.RoundToInt(PlayerClass.Ability(GhostRow, "hunterMax", 5f)), 1, 99);
    /// <summary>The Head Hunter's last change: +1 a stack more, -1 a stack fewer, -2 all lost; and seconds since.</summary>
    public int HunterChange => hunterChange;
    public float SinceHunterChange => Time.time - hunterChangedAt;

    /// <summary>A Ghost's gun in hand is loaded and not fired since: its next shot hits Deadeye's double.</summary>
    public bool DeadeyeReady
    {
        get
        {
            if (!PlayerClass.Is(PlayerClass.Ghost) || slots == null || HeldGun == null) return false;
            int slot = slots.SelectedSlot;
            return slot >= 0 && slot < loaded.Length && loaded[slot];
        }
    }

    /// <summary>How much of a Knight's taunt under way is left, 0 to 1 (0: none).</summary>
    public float TauntShare => tauntUntil > Time.time ? Mathf.Clamp01((tauntUntil - Time.time) / tauntTotal) : 0f;
    /// <summary>A Knight whose taunt may be used now.</summary>
    public bool TauntReady => PlayerClass.Is(PlayerClass.Knight) && Time.time >= tauntReadyAt;
    /// <summary>Seconds until a Knight's taunt may be used again (0: now), and how much of that wait is left, 0 to 1.</summary>
    public float TauntWait => Mathf.Max(0f, tauntReadyAt - Time.time);
    public float TauntWaitShare => Mathf.Clamp01(TauntWait / tauntWait);

    // A Schießhund's Desperado as it is (the gun in slot 4, its rounds its own), or null.
    private WeaponRuntime Desperado => PlayerClass.Is(PlayerClass.Schiesshund) && slots != null && slots.KeepsOwnRounds(DesperadoSlot)
        ? slots.GetRuntime(DesperadoSlot) : null;
    /// <summary>A Schießhund's Desperado rounds, and the most its cylinder holds (the table's, within the gun's own).</summary>
    public int Rounds => Desperado != null ? Desperado.Ammo : 0;
    public int RoundMax
    {
        get
        {
            int most = Mathf.Clamp(Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "desperadoMax", 6f)), 1, 12);
            var gun = Desperado;
            return gun != null ? Mathf.Min(most, gun.Data.MagazineSize) : most;
        }
    }
    /// <summary>A Schießhund holding its Desperado (slot 4) with a round in it: its next shot may claim the bounty.</summary>
    public bool DesperadoLoaded => slots != null && slots.SelectedSlot == DesperadoSlot && Rounds > 0;
    /// <summary>Return Back's stacks now (0 when they have gone), and seconds since the last bounty.</summary>
    public int ReturnStacks => returnUntil > Time.time ? returnStacks : 0;
    /// <summary>How much of Return Back is left, 0 to 1 (0: none).</summary>
    public float ReturnShare => returnUntil > Time.time ? Mathf.Clamp01((returnUntil - Time.time) / returnTotal) : 0f;
    public float SinceBounty => Time.time - bountyAt;

    /// <summary>The factor this player's class puts on their shots now (adrenaline, a CHARGE, Head Hunter, Deadeye).</summary>
    public float DamageFactor
    {
        get
        {
            float factor = 1f;
            if (PlayerClass.Is(PlayerClass.Assault)) factor *= 1f + stacks * PlayerClass.Ability(AssaultRow, "damagePerStack", 3f) / 100f;
            if (chargeUntil > Time.time) factor *= 1f + PlayerClass.Ability(AssaultRow, "chargeDamage", 25f) / 100f;
            if (PlayerClass.Is(PlayerClass.Ghost))
            {
                // Compounding, as the memo has it: five stacks of 7% are 1.07^5, some 40%.
                if (hunter > 0) factor *= Mathf.Pow(1f + PlayerClass.Ability(GhostRow, "hunterDamage", 7f) / 100f, hunter);
                // Read while the shot is traced, before ShotFired spends it: every pellet of that first shot has it.
                if (DeadeyeReady) factor *= 1f + PlayerClass.Ability(GhostRow, "deadeye", 100f) / 100f;
            }
            if (PlayerClass.Is(PlayerClass.Schiesshund))
            {
                factor *= 1f + ReturnStacks * PlayerClass.Ability(HoundRow, "returnDamage", 6f) / 100f;
                // A Desperado round, likewise read before the shot spends it.
                if (DesperadoLoaded) factor *= 1f + PlayerClass.Ability(HoundRow, "desperadoDamage", 100f) / 100f;
            }
            return factor;
        }
    }

    /// <summary>The factor on this player's pace now (adrenaline, a CHARGE).</summary>
    public float SpeedFactor
    {
        get
        {
            float factor = 1f;
            if (PlayerClass.Is(PlayerClass.Assault)) factor *= 1f + stacks * PlayerClass.Ability(AssaultRow, "speedPerStack", 1.5f) / 100f;
            if (chargeUntil > Time.time) factor *= 1f + PlayerClass.Ability(AssaultRow, "chargeSpeed", 25f) / 100f;
            if (PlayerClass.Is(PlayerClass.Schiesshund)) factor *= 1f + ReturnStacks * PlayerClass.Ability(HoundRow, "returnSpeed", 6f) / 100f;
            // A Détente's Golden Hour after a heal.
            if (detente != null && PlayerClass.Is(PlayerClass.Detente)) factor *= detente.SpeedFactor;
            return factor;
        }
    }

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        slots = GetComponent<PlayerWeaponSlots>();
        weapon = GetComponent<WeaponController>();
    }

    private void OnEnable() => CombatDamageEvents.Damaged += OnDamaged;

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        Unwire();
        if (Local == this) Local = null;
    }

    private void Start() => TryGiveKit();

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        Local = this;
        if (!wired) Wire();
        // F on an enemy pings it for the team, alone too (PlayerNetworkCombat makes it in a room).
        if (!pingMade) { pingMade = true; if (GetComponent<PlayerPing>() == null) gameObject.AddComponent<PlayerPing>(); }
        // A Détente's syringe, treatment and Resurrection.
        if (detente == null && (detente = GetComponent<DetenteKit>()) == null) detente = gameObject.AddComponent<DetenteKit>();
        // A Support's bag, resupply and barricades.
        if (support == null && (support = GetComponent<SupportKit>()) == null) support = gameObject.AddComponent<SupportKit>();
        if (!kitGiven) TryGiveKit();
        Ebb();
        if (PlayerClass.Is(PlayerClass.Point)) ReloadAside();
        // A Knight fights on pinned under a runner with its fists and shoves, and kicks it off with the shove key every
        // few seconds without the shield (PlayerHold, PlayerMeleeCombat; the user: alone under a runner it must get out).
        if (hold == null && receiver != null) hold = receiver.GetComponent<PlayerHold>();
        if (hold != null)
        {
            bool knight = PlayerClass.Is(PlayerClass.Knight);
            hold.FightsPinned = knight && PlayerClass.Ability(KnightRow, "fightsPinned", 1f) > .5f;
            hold.BreakFreeEvery = knight ? PlayerClass.Ability(KnightRow, "breakFree", 6f) : -1f;
        }
        if (ability == null && InputSystem.actions != null)
        {
            KeyBindings.EnsureRestored();
            ability = InputSystem.actions.FindAction("Player/ClassAbility", false);
        }
        if (ability != null && ability.WasPressedThisFrame() && movement.InputAvailable && !movement.IsDead && !movement.IsHeld) Use();
    }

    // This PC's own player's gun and hurt, once both are there (the gun and the health may be made after this kit).
    private void Wire()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (receiver == null) receiver = movement.DamageReceiver;
        if (weapon == null || receiver == null) return;
        weapon.ShotFired += OnShot;
        weapon.ReloadCompleted += OnReloaded;
        receiver.HitResolved += OnHurt;
        wired = true;
    }

    private void Unwire()
    {
        if (!wired) return;
        wired = false;
        if (weapon != null) { weapon.ShotFired -= OnShot; weapon.ReloadCompleted -= OnReloaded; }
        if (receiver != null) receiver.HitResolved -= OnHurt;
    }

    // The class's guns in slots 1 and 2 once this player is ready, every chapter (a run starts with them).
    private void TryGiveKit()
    {
        if (kitGiven || movement == null || !movement.IsLocal || slots == null || !slots.isActiveAndEnabled) return;
        kitGiven = true;
        // Out of the box both guns are loaded: their first rounds are a Ghost's Deadeye shots too.
        loaded[0] = loaded[1] = true;
        var info = PlayerClass.Current;
        if (info == null) return;
        Give(0, info.Primary);
        Give(1, info.Secondary);
        // A Schießhund's Desperado in slot 4: its own gun (the class's first), with a round or so in it to show what it
        // does, and nothing in reserve.
        slots.SpecialHoldsGun = PlayerClass.Is(PlayerClass.Schiesshund);
        if (!slots.SpecialHoldsGun || info.Weapons.Length == 0) return;
        var revolver = Gun(info.Weapons[0]);
        if (revolver == null || !slots.TrySetWeapon(DesperadoSlot, revolver)) return;
        slots.GetRuntime(DesperadoSlot)?.LoadOwnRounds(Mathf.Clamp(Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "startRounds", 1f)), 0, RoundMax));
        slots.NotifyAmmo();
    }

    private void Give(int slot, string name)
    {
        var gun = Gun(name);
        if (gun != null && slots.GetWeapon(slot) != gun) slots.TrySetWeapon(slot, gun);
    }

    private static WeaponData Gun(string name)
    {
        var assets = FPSBodyAssets.Instance;
        if (string.IsNullOrEmpty(name) || assets == null) return null;
        foreach (var entry in assets.guns)
            if (entry.weapon != null && string.Equals(entry.weapon.name, name, StringComparison.OrdinalIgnoreCase)) return entry.weapon;
        return null;
    }

    // ---- The class key ----

    private void Use()
    {
        if (PlayerClass.Is(PlayerClass.Assault))
        {
            if (stacks < StackMax) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
            stacks = 0;
            Vector3 at = transform.position;
            ReceiveCharge(at, Texts.Get("class.assault.charge.self"), ChargeSeconds, ChargeReach);
            Charged?.Invoke(at);
            return;
        }
        if (PlayerClass.Is(PlayerClass.Knight)) { Taunt(); return; }
        if (PlayerClass.Is(PlayerClass.Detente) && detente != null) { detente.Inject(); return; }
        if (PlayerClass.Is(PlayerClass.Support) && support != null) { support.Build(); return; }
        // No ability on the key for this class (a Point's and a Ghost's are always on), or not built yet.
        KitSound.Play(KitSound.Cue.Refuse, .35f);
    }

    /// <summary>
    /// A CHARGE was sounded here (this PC's player's, or a friend's as their PC said it): this PC's player, alive and
    /// within its reach, charges with it. Its length and reach are the Assault's own (their class cards in), as their
    /// PC sent them; below 0, the table's.
    /// </summary>
    public static void ReceiveCharge(Vector3 at, string said, float seconds = -1f, float reach = -1f)
    {
        var kit = Local;
        if (kit == null || kit.movement == null || kit.movement.IsDead) return;
        if (reach < 0f) reach = PlayerClass.TableAbility(AssaultRow, "chargeRadius", 15f);
        if ((kit.transform.position - at).sqrMagnitude > reach * reach) return;
        kit.chargeTotal = Mathf.Max(.1f, seconds >= 0f ? seconds : PlayerClass.TableAbility(AssaultRow, "chargeSeconds", 7f));
        kit.chargeUntil = Time.time + kit.chargeTotal;
        KitSound.Play(KitSound.Cue.Charge, .9f);
        AlertHUD.Self(said);
    }

    // ---- Assault: adrenaline ----

    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || !report.IsKillingBlow || !(report.Target is EnemyHealth enemy)) return;
        if (!PlayerPerks.IsMine(report.Hit.Source)) return;
        if (PlayerClass.Is(PlayerClass.Schiesshund) && PingMarks.IsBountyOf(enemy, movement)) Bounty(enemy);
        if (!PlayerClass.Is(PlayerClass.Assault)) return;
        stacks = Mathf.Min(StackMax, stacks + 1);
        lastKill = Time.time;
        nextEbb = lastKill + PlayerClass.Ability(AssaultRow, "stackHold", 6f);
    }

    // Past its hold, adrenaline ebbs a stack at a time.
    private void Ebb()
    {
        if (stacks <= 0 || Time.time < nextEbb) return;
        stacks--;
        nextEbb = Time.time + Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "stackDecay", 1.5f));
    }

    // ---- Point: the gun put away reloads itself ----

    private void ReloadAside()
    {
        if (slots == null) return;
        for (int slot = 0; slot < 2; slot++)
        {
            var data = slots.GetWeapon(slot);
            var gun = data != null ? slots.GetRuntime(slot) : null;
            bool waiting = gun != null && slots.SelectedSlot != slot && gun.Ammo < data.MagazineSize && gun.ReserveAmmo > 0;
            if (!waiting) { asideSince[slot] = -1f; continue; }
            if (asideSince[slot] < 0f) asideSince[slot] = Time.time;
            if (Time.time - asideSince[slot] < AsideTime(gun)) continue;
            asideSince[slot] = -1f;
            if (!gun.ReloadAside()) continue;
            refilledAt[slot] = Time.time;
            slots.NotifyAmmo();
        }
    }

    // As long as that gun's own reload would take from where it is (shell by shell for a pump), times the table's share.
    private static float AsideTime(WeaponRuntime gun)
    {
        var data = gun.Data;
        float time = data.ShellReload
            ? data.ReloadDuration + data.ShellInsertDuration * Mathf.Max(1, data.MagazineSize - gun.Ammo) + data.ShellEndDuration
            : gun.Ammo <= 0 ? data.EmptyReloadDuration : data.ReloadDuration;
        return Mathf.Max(.05f, time * Mathf.Max(0f, PlayerClass.Ability(PointRow, "reloadShare", 1f)));
    }

    // ---- Ghost: Head Hunter, Deadeye, the stagger ----

    // Each pellet of this PC's own shot, once traced (a lone bullet is pellet 0 of 1). What the shot met counts once, at
    // its last pellet: a head among its pellets is a headshot, else a body is a body shot.
    private void OnShot(WeaponShot shot)
    {
        if (movement == null || !movement.IsLocal || !PlayerClass.Is(PlayerClass.Ghost)) return;
        var data = HeldGun;
        var trace = shot.Trace;
        var enemy = trace.HasHit && trace.Damage == DamageResult.Damaged && trace.Hit.collider != null
            ? trace.Hit.collider.GetComponentInParent<EnemyHealth>() : null;
        if (enemy != null)
        {
            if (trace.IsHeadshot) shotHead = true;
            else shotBody = true;
            if (PlayerClass.IsElite(enemy) && PlayerClass.OwnsWeapon(PlayerClass.Current, data)) Stagger(enemy, trace.IsHeadshot);
        }
        if (shot.Pellet < (data != null ? Mathf.Max(1, data.Pellets) : 1) - 1) return;
        // The round loaded first is spent.
        int slot = slots != null ? slots.SelectedSlot : -1;
        if (slot >= 0 && slot < loaded.Length) loaded[slot] = false;
        if (shotHead) Hunt(1);
        else if (shotBody) Hunt(-Mathf.Max(0, Mathf.RoundToInt(PlayerClass.Ability(GhostRow, "hunterBodyLoss", 1f))));
        shotHead = shotBody = false;
    }

    // A reload done: the gun in hand is loaded again, its next round Deadeye's.
    private void OnReloaded()
    {
        if (movement == null || !movement.IsLocal || slots == null) return;
        int slot = slots.SelectedSlot;
        if (slot >= 0 && slot < loaded.Length) loaded[slot] = true;
    }

    private void Hunt(int change)
    {
        int before = hunter;
        hunter = Mathf.Clamp(hunter + change, 0, HunterMax);
        if (hunter == before) return;
        hunterChange = hunter > before ? 1 : -1;
        hunterChangedAt = Time.time;
        if (hunter > before) KitSound.Play(KitSound.Cue.Hunter, .45f);
    }

    // Hurt, shield or health: a Ghost loses every stack (the memo's rule: the Head Hunter is for whoever isn't touched).
    private void OnHurt(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || hunter <= 0 || receiver == null || !PlayerClass.Is(PlayerClass.Ghost)) return;
        if (receiver.LastShieldTaken + receiver.LastHealthTaken <= 0f) return;
        hunter = 0;
        hunterChange = -2;
        hunterChangedAt = Time.time;
        KitSound.Play(KitSound.Cue.HunterLost, .6f);
    }

    // A Ghost's own gun hit an elite: it staggers (a stun that cuts off its blow or its charge; the host is told by the
    // stun itself, MeleeCombatTarget), unless this Ghost staggered it lately.
    private void Stagger(EnemyHealth enemy, bool head)
    {
        if (!enemy.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable) return;
        if (staggerAgain.TryGetValue(target, out float again) && Time.time < again) return;
        float seconds = head ? PlayerClass.Ability(GhostRow, "stumbleHead", 1.6f) : PlayerClass.Ability(GhostRow, "stumbleBody", .8f);
        if (seconds <= 0f) return;
        target.Interrupt(seconds);
        if (staggerAgain.Count >= StaggerMemory) Forget();
        staggerAgain[target] = Time.time + seconds + Mathf.Max(0f, PlayerClass.Ability(GhostRow, "stumbleRest", 3f));
    }

    private void Forget()
    {
        forget.Clear();
        foreach (var pair in staggerAgain) if (pair.Key == null || Time.time >= pair.Value) forget.Add(pair.Key);
        foreach (var key in forget) staggerAgain.Remove(key);
    }

    // ---- Schießhund: the bounty, Return Back, the Desperado ----

    /// <summary>
    /// A Desperado round is going into the head of this Schießhund's own mark (PlayerClass.Finish, as AttackTrace weighs
    /// the hit): when it dies of it, BOUNTY.
    /// </summary>
    internal void Finishing(EnemyHealth enemy, Vector3 point, Vector3 from)
    {
        finishing = enemy;
        finishAt = Time.time;
        finishPoint = point;
        finishFrom = from;
    }

    // Its own mark died at its hand: points toward the Desperado's next round (an elite's count more), a Return Back
    // stack, and the bounty's look and sound (its money is CombatFeedHUD's, with the kill).
    private void Bounty(EnemyHealth enemy)
    {
        bountyPoints += Mathf.Max(1, Mathf.RoundToInt(PlayerClass.IsElite(enemy) ? PlayerClass.Ability(HoundRow, "elitePoints", 2f) : 1f));
        int need = Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "roundPoints", 2f)));
        var desperado = Desperado;
        while (bountyPoints >= need)
        {
            bountyPoints -= need;
            if (desperado != null && desperado.Ammo < RoundMax) desperado.LoadRound();
        }
        slots?.NotifyAmmo();
        returnStacks = Mathf.Min(Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "returnMax", 5f))), ReturnStacks + 1);
        returnTotal = Mathf.Max(.1f, PlayerClass.Ability(HoundRow, "returnSeconds", 8f));
        returnUntil = Time.time + returnTotal;
        bountyAt = Time.time;
        bool claimed = finishing == enemy && Time.time - finishAt < 1f;
        finishing = null;
        KitSound.Play(KitSound.Cue.Bounty, claimed ? 1f : .7f);
        // In the world, for everyone: the Desperado's grand burst where its round went in, or a plain bounty's coins.
        var body = enemy.GetComponentInChildren<Collider>();
        Vector3 ground = enemy.transform.position;
        Vector3 head = claimed ? finishPoint : body != null ? new Vector3(body.bounds.center.x, body.bounds.max.y - .15f, body.bounds.center.z)
            : ground + Vector3.up * 1.6f;
        Vector3 from = claimed ? finishFrom : transform.position + Vector3.up * 1.5f;
        BountyBurst.Play(head, from, ground, claimed);
        Claimed?.Invoke(head, from, ground, claimed);
        if (!claimed) return;
        // The Desperado's own kill on this screen too: BOUNTY, the counter's banner at its most in the mark's teal.
        if (!TryGetComponent(out CounterBanner banner)) banner = gameObject.AddComponent<CounterBanner>();
        banner.Play(.95f, CounterBanner.Grades - 1, "BOUNTY", PingMarks.BountyColor);
    }

    // ---- Knight: the taunt ----

    private void Taunt()
    {
        if (Time.time < tauntReadyAt) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        tauntTotal = TauntSeconds;
        tauntWait = Mathf.Max(tauntTotal, PlayerClass.Ability(KnightRow, "tauntCooldown", 25f));
        tauntUntil = Time.time + tauntTotal;
        tauntReadyAt = Time.time + tauntWait;
        Vector3 at = transform.position;
        ReceiveTaunt(movement, at, true, TauntReach, tauntTotal);
        AlertHUD.Self(Texts.Get("class.knight.taunt.self"));
        Taunted?.Invoke(at);
    }

    /// <summary>
    /// A Knight taunted at <paramref name="at"/> (this PC's own, or a friend's as their PC said it): the zombies this PC
    /// runs that are already fighting within reach go after them (real only on the host or alone; EnemyTargeting), and
    /// every PC sees the ring go out and hears it, a friend's the softer the further off. Its reach and length are the
    /// Knight's own (their class cards in), as their PC sent them; below 0, the table's.
    /// </summary>
    public static void ReceiveTaunt(PlayerMovement knight, Vector3 at, bool mine, float reach = -1f, float seconds = -1f)
    {
        if (reach < 0f) reach = Mathf.Max(0f, PlayerClass.TableAbility(KnightRow, "tauntRadius", 12f));
        if (seconds < 0f) seconds = PlayerClass.TableAbility(KnightRow, "tauntSeconds", 6f);
        EnemyTargeting.Taunt(knight, at, reach, seconds);
        TauntRing.Show(at, reach);
        float volume = 1f;
        var local = Local != null ? Local.movement : null;
        if (!mine && local != null) volume = Mathf.Clamp01(1f - Vector3.Distance(local.transform.position, at) / 40f) * .7f;
        KitSound.Play(KitSound.Cue.Taunt, volume);
    }
}
