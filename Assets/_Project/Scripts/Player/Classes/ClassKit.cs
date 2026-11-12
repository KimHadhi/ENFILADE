using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A player's class at work (PlayerClass), on every player; the live part runs for this PC's own:
/// - its starting guns (the class's primary and secondary, slots 1 and 2) as each chapter begins;
/// - its ability on the class key (Player/ClassAbility, the wheel click by default, rebindable in Settings → 조작);
/// - Assault: every kill adds a stack of adrenaline (harder hits and a quicker pace a stack), which holds a few seconds
///   after the last kill and then ebbs; full, the class key spends it all on a CHARGE, Guts &amp; Blackpowder's officer's
///   charge: for some seconds it and every friend near hit harder and run faster (friends' PCs are told,
///   PlayerNetworkCombat, and each gives its own player the buff), and on every PC they glow gold while it lasts (ChargeGlow);
/// - Point: a gun put away reloads itself in the time its own reload takes, from its reserve, so the SMG empties while
///   the shotgun refills and back (Killing Floor 2's gunslinger feel, the user's "화력이 끊기지 않고"). Its class key is
///   the Gun Kata: for some seconds a gun comes out at once and full, its reserve untouched, and running doesn't spread
///   its shots (the user's solo all-rounder who can run the gauntlet alone, 2026-11-12);
/// - Ghost (stage 2; the user's design, with the Headhunter and Deadeye of their old class memo): a headshot adds a
///   Head Hunter stack, a shot into a body takes one away and any hurt taken loses them all, and every stack makes each
///   shot hit harder, compounding, so a steady hand grows into the elite killer of the late game whatever gun it holds.
///   The first round after a reload hits double (Deadeye). Its own guns stagger an elite they hit: a short stun that
///   breaks off its blow or its charge, longer for a headshot, each elite at most once in a while (the early game's
///   job, before the cards make it strong);
/// - Knight (stage 2; the user's design, with the memo's Perfect Parry and Deflect): it takes less from every enemy's
///   blow; its parries last longer, wear posture down harder and open the counter as a perfect dodge does; its counters
///   and fists hit harder; pinned under a runner it fights on; and its class key covers a friend (KnightCover, 2026-11-12,
///   in the taunt's place): held, the reach and the friend it can cover show; let go, a parry opens on the friend's behalf,
///   and a blow turned there brings the Knight to their side at once;
/// - Schießhund (stage 3; the user's design, with the Jagdfieber and Return Back of their old memo): its F ping is a
///   teal bounty mark (PlayerPing, PingMarks), one at a time, which its bullets hit harder. A marked enemy it kills pays
///   a bounty: money (CombatFeedHUD), a Return Back stack (quicker and harder-hitting a while), and points toward the
///   Desperado's next round. The Desperado is its own revolver in slot 4 (the class's own gun, the R08; slot 2 holds a
///   plain pistol, so the two are never mixed up: the user's word), loaded by bounties alone and never reloaded: every
///   round hits double, and one in the head of its own mark kills it outright, elites too (a brute loses a share of its
///   health), with its claim burst in the world for everyone to see, the hunter too (BountyBurst;
///   a plain bounty slams its small CLAIMED plate there). Its class key calls a duel on the foe under the crosshair (HoundDuel; the
///   user's, after Iso's Kill Contract): for some seconds the two can only hurt each other, its shots pass through every
///   other foe, the foe carries its bounty mark, and friends may help but never finish it (the Ace card lets them).
/// - Détente (the user's design): its syringe, Golden Hour and Resurrection are DetenteKit's; the class key injects.
/// - Support (stage 4): its supply bag, resupplying a friend with F and its barricades are SupportKit's; the class key
///   is held there to see where a barricade would go up and let go to put it up, which SupportKit reads itself.
/// A class picked (PlayerClass.Picked: the hub's class wall, the title screen) is a fresh start, as a chapter's: every
/// stack, buff and wait back to nothing, the class's guns full, and the Détente's and Support's kits as they start, the
/// last pick's barricades down (OnPicked; the user, 2026-11-12: "클래스 선택할 때 초기화 하는 방식 어때?").
/// The other classes' abilities come in later stages (Classes.csv ready 0). Numbers: Tables/ClassAbilities.
/// The class cards with code behind them (Docs/ClassCards.md) are ClassCards', added here; a Point's Gunslinger card and
/// a Ghost's White Tiger guard are read here (DamageFactor, OnHurt).
/// What a kill, a headshot or a mark does here takes any foe (ICombatFoe): a zombie, or a practice target on the hub's
/// range, so every class can be tried out there before a run (its kills never reach the run's counts: GunFeelDirector).
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
    /// <summary>This PC's Schießhund called a duel on this foe, for so long, with the Ace or not: the other PCs are told.</summary>
    public event Action<Component, float, bool> Dueled;
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
    // Schießhund: when its duel may be called again, and how long that wait is.
    private float duelReadyAt, duelWait = 1f;
    // Point: when its Gun Kata under way ends and how long it lasts; when it may be used again and how long that wait is.
    private float kataUntil, kataTotal = 1f, kataReadyAt, kataWait = 1f;
    // Schießhund: Desperado rounds and the points toward the next; Return Back's stacks and when they go; when the last
    // bounty came; and the bounty a Desperado round was just put in the head of, for its BOUNTY.
    private int bountyPoints, returnStacks;
    private float returnUntil, returnTotal = 1f, bountyAt = -99f, finishAt = -99f;
    // A zombie or a range target (ICombatFoe): the hunt is the same on either.
    private Component finishing;
    // Where the Desperado's round went into its head and where it was fired from (for the burst).
    private Vector3 finishPoint, finishFrom;
    // The special slot, where a Schießhund's Desperado sits.
    private const int DesperadoSlot = 3;
    private bool pingMade;
    private DetenteKit detente;
    private SupportKit support;
    private KnightCover cover;
    // The class cards with code behind them (ClassCards); and, for a Point's Gunslinger card, the gun slot last seen in
    // hand and when it was drawn.
    private ClassCards cards;
    private int drawnSlot = -2;
    private float drawnAt = -99f;

    public PlayerMovement Movement => movement;
    public WeaponData HeldGun => weapon != null ? weapon.EquippedData : null;

    /// <summary>Adrenaline stacks now, and the most (an Assault's).</summary>
    public int Stacks => stacks;
    public int StackMax => Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(AssaultRow, "stackMax", 8f)));
    /// <summary>An Assault with full adrenaline: the class key charges.</summary>
    public bool ChargeReady => PlayerClass.Is(PlayerClass.Assault) && stacks >= StackMax;
    /// <summary>How much of a CHARGE this player is in is left, 0 to 1 (0: none).</summary>
    public float ChargeShare => chargeUntil > Time.time ? Mathf.Clamp01((chargeUntil - Time.time) / chargeTotal) : 0f;

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

    /// <summary>A Schießhund whose duel may be called now; seconds until it may (0: now), and how much of that wait is left.</summary>
    public bool DuelReady => PlayerClass.Is(PlayerClass.Schiesshund) && Time.time >= duelReadyAt;
    public float DuelWait => Mathf.Max(0f, duelReadyAt - Time.time);
    public float DuelWaitShare => Mathf.Clamp01(DuelWait / duelWait);
    /// <summary>A Point whose Gun Kata may be used now; seconds until it may (0: now); how much of one under way is left (0: none).</summary>
    public bool KataReady => PlayerClass.Is(PlayerClass.Point) && Time.time >= kataReadyAt;
    public float KataWait => Mathf.Max(0f, kataReadyAt - Time.time);
    public float KataShare => kataUntil > Time.time ? Mathf.Clamp01((kataUntil - Time.time) / kataTotal) : 0f;

    /// <summary>How much of this player's duel under way is left, 0 to 1 (0: none).</summary>
    public float DuelShare => HoundDuel.Of(movement) is HoundDuel duel ? duel.Share : 0f;

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

    /// <summary>
    /// A Point's Gunslinger card (건슬링어): how much of the moment after a gun is drawn, while it hits harder, is left,
    /// 0 to 1 (0: none, or not a Point with the card).
    /// </summary>
    public float GunslingerShare
    {
        get
        {
            if (!PlayerClass.Is(PlayerClass.Point) || !PlayerPerks.Has(PlayerPerks.PointGunslinger)) return 0f;
            float seconds = Mathf.Max(.1f, PlayerClass.Ability(PointRow, "gunslingerSeconds", 2.5f));
            return Mathf.Clamp01(1f - (Time.time - drawnAt) / seconds);
        }
    }

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
            // A Point's Gunslinger card: the gun just drawn hits harder a moment.
            if (GunslingerShare > 0f) factor *= 1f + PlayerPerks.Total(PlayerPerks.PointGunslinger) / 100f;
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

    private void OnEnable()
    {
        CombatDamageEvents.Damaged += OnDamaged;
        PlayerClass.Picked += OnPicked;
    }

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        PlayerClass.Picked -= OnPicked;
        Unwire();
        if (Local == this) Local = null;
    }

    private void Start() => TryGiveKit();

    /// <summary>
    /// The class was changed in the hub (HubLobby): its guns now, to try on the range, as a chapter would start. The pick
    /// itself already handed the whole kit afresh (OnPicked), so this finds the guns in place.
    /// </summary>
    public void Rekit() { kitGiven = false; TryGiveKit(); }

    // A class picked (PlayerClass.Picked: only in the hub or on the title screen, never mid-run) starts the kit afresh,
    // as a chapter does, so nothing of the last pick carries over and every ability is there to try again (the user,
    // 2026-11-12: stacks built as an Assault ebbed away meanwhile; a Support back from another class could build again).
    private void OnPicked(string before, string now)
    {
        if (movement == null || !movement.IsLocal) return;
        // Assault: no adrenaline and no CHARGE (a friend's too) under way.
        stacks = 0;
        lastKill = -99f;
        nextEbb = 0f;
        chargeUntil = 0f;
        chargeTotal = 1f;
        // Point: no gun counting toward its reload put away; Ghost: both guns' first rounds Deadeye's, no Head Hunter,
        // no elite waiting to be staggered again.
        for (int slot = 0; slot < 2; slot++)
        {
            asideSince[slot] = -1f;
            refilledAt[slot] = -99f;
            loaded[slot] = true;
        }
        hunter = 0;
        hunterChange = 0;
        hunterChangedAt = -99f;
        shotHead = shotBody = false;
        staggerAgain.Clear();
        // Knight: its taunt ready now.
        tauntUntil = tauntReadyAt = 0f;
        tauntTotal = tauntWait = 1f;
        // Schießhund: its duel ready now, the last one over.
        duelReadyAt = 0f;
        duelWait = 1f;
        HoundDuel.End(movement);
        // Point: its Gun Kata ready now, none under way.
        kataUntil = kataReadyAt = 0f;
        kataTotal = kataWait = 1f;
        // Schießhund: no points toward a round, no Return Back, no Desperado shot on its way (its rounds come with its guns).
        bountyPoints = returnStacks = 0;
        returnUntil = 0f;
        returnTotal = 1f;
        bountyAt = finishAt = -99f;
        finishing = null;
        // A Point's Gunslinger card counts again from the next gun drawn.
        drawnSlot = -2;
        drawnAt = -99f;
        if (detente == null) detente = GetComponent<DetenteKit>();
        if (detente != null) detente.Fresh();
        if (support == null) support = GetComponent<SupportKit>();
        if (support != null) support.Fresh();
        if (cover == null) cover = GetComponent<KnightCover>();
        if (cover != null) cover.Fresh();
        // The class's guns, full, as a chapter starts them.
        kitGiven = false;
        TryGiveKit(true);
    }

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
        // A Knight's cover.
        if (cover == null && (cover = GetComponent<KnightCover>()) == null) cover = gameObject.AddComponent<KnightCover>();
        // The class cards with code behind them (Docs/ClassCards.md).
        if (cards == null && (cards = GetComponent<ClassCards>()) == null) cards = gameObject.AddComponent<ClassCards>();
        if (!kitGiven) TryGiveKit();
        // A gun drawn: a Point's Gunslinger card counts from now.
        if (slots != null && slots.SelectedSlot != drawnSlot)
        {
            drawnSlot = slots.SelectedSlot;
            drawnAt = Time.time;
            // In a Point's Gun Kata the gun drawn comes out full, its reserve untouched.
            if (KataShare > 0f && drawnSlot >= 0 && drawnSlot < 2)
            {
                var gun = slots.GetWeapon(drawnSlot) != null ? slots.GetRuntime(drawnSlot) : null;
                if (gun != null) { gun.TopUp(); slots.NotifyAmmo(); }
                // The draw seen: an orange arc whirls round the gun as it comes out (the special-moment pick C), for everyone.
                CardNet.Show(CardFx.Kind.KataDraw, transform.position + Vector3.up * 1.3f + transform.forward * .5f, transform.forward, 0f);
            }
        }
        Ebb();
        Crate();
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
        // The class key (the key window's Player/ClassAbility) acts as it goes down; a Support's is held to see where its
        // barricade would go and let go to put it up, which SupportKit reads itself, and a Knight's is held to choose the
        // friend to cover, which KnightCover reads.
        if (!PlayerClass.Is(PlayerClass.Support) && !PlayerClass.Is(PlayerClass.Knight) && KeyBindings.Pressed("Player/ClassAbility")
            && movement.InputAvailable && !movement.IsDead && !movement.IsHeld) Use();
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

    // The class's guns in slots 1 and 2 once this player is ready, every chapter (a run starts with them); full, the same
    // gun as before too, when a class was just picked.
    private void TryGiveKit(bool full = false)
    {
        if (kitGiven || movement == null || !movement.IsLocal || slots == null || !slots.isActiveAndEnabled) return;
        kitGiven = true;
        // Out of the box both guns are loaded: their first rounds are a Ghost's Deadeye shots too.
        loaded[0] = loaded[1] = true;
        var info = PlayerClass.Current;
        if (info == null) return;
        // No longer a Schießhund: its Desperado leaves the special slot with it (the hand on it goes to the first gun).
        if (slots.SpecialHoldsGun && !PlayerClass.Is(PlayerClass.Schiesshund))
        {
            if (slots.SelectedSlot == DesperadoSlot) slots.SelectSlot(0);
            slots.TrySetWeapon(DesperadoSlot, null);
        }
        Give(0, info.Primary, full);
        Give(1, info.Secondary, full);
        // A Schießhund's Desperado in slot 4: its own gun (the class's first), with a round or so in it to show what it
        // does, and nothing in reserve.
        slots.SpecialHoldsGun = PlayerClass.Is(PlayerClass.Schiesshund);
        if (!slots.SpecialHoldsGun || info.Weapons.Length == 0) return;
        var revolver = Gun(info.Weapons[0]);
        if (revolver == null || !slots.TrySetWeapon(DesperadoSlot, revolver)) return;
        slots.GetRuntime(DesperadoSlot)?.LoadOwnRounds(Mathf.Clamp(Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "startRounds", 1f)), 0, RoundMax));
        slots.NotifyAmmo();
    }

    private void Give(int slot, string name, bool full)
    {
        var gun = Gun(name);
        if (gun == null) return;
        if (slots.GetWeapon(slot) != gun) slots.TrySetWeapon(slot, gun);
        else if (full)
        {
            // The same gun kept from before: full, as a new one comes.
            slots.GetRuntime(slot)?.RefillAmmo();
            slots.NotifyAmmo();
        }
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
            ReceiveCharge(at, Texts.Get("class.assault.charge.self"));
            // Everyone it takes glows gold on this PC (the friends' PCs light them as they hear of it: ChargeRpc).
            ChargeGlow.Sounded(at);
            MomentHUD.Charge(PlayerClass.Ability(AssaultRow, "chargeSeconds", 7f), PlayerClass.Ability(AssaultRow, "chargeRadius", 15f));
            Charged?.Invoke(at);
            // The phantom cavalry card rides out with it (ClassCards).
            cards?.Charged(at);
            return;
        }
        if (PlayerClass.Is(PlayerClass.Knight)) { Taunt(); return; }
        if (PlayerClass.Is(PlayerClass.Detente) && detente != null) { detente.Inject(); return; }
        if (PlayerClass.Is(PlayerClass.Schiesshund)) { Duel(); return; }
        if (PlayerClass.Is(PlayerClass.Point)) { GunKata(); return; }
        // No ability on the key for this class (a Point's and a Ghost's are always on; a Support's is SupportKit's), or not
        // built yet.
        KitSound.Play(KitSound.Cue.Refuse, .35f);
    }

    /// <summary>
    /// A CHARGE was sounded here (this PC's player's, or a friend's as their PC said it): this PC's player, alive and
    /// within its reach, charges with it.
    /// </summary>
    public static void ReceiveCharge(Vector3 at, string said)
    {
        var kit = Local;
        if (kit == null || kit.movement == null || kit.movement.IsDead) return;
        float reach = PlayerClass.Ability(AssaultRow, "chargeRadius", 15f);
        if ((kit.transform.position - at).sqrMagnitude > reach * reach) return;
        kit.chargeTotal = Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "chargeSeconds", 7f));
        kit.chargeUntil = Time.time + kit.chargeTotal;
        KitSound.Play(KitSound.Cue.Charge, .9f);
        AlertHUD.Self(said);
        // And they shout back, as G&B's line answers its officer's charge (PlayerCallouts, Settings → 저절로 외치기).
        PlayerCallouts.JoinCharge();
    }

    // ---- Assault: adrenaline ----

    // Any foe's kill by this player: a zombie's, or a practice target's on the hub's range, where the classes are tried
    // out (the user, 2026-11-12); a practice kill stays out of the run's counts elsewhere (GunFeelDirector).
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || !report.IsKillingBlow || !(report.Target is ICombatFoe)) return;
        if (!PlayerPerks.IsMine(report.Hit.Source)) return;
        if (PlayerClass.Is(PlayerClass.Schiesshund) && PingMarks.IsBountyOf(report.Target, movement)) Bounty(report.Target);
        else if (PlayerClass.Is(PlayerClass.Schiesshund) && DyeMarks.IsDyedBy(report.Target, movement)) DyedKill(report.Target);
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

    // ---- Any class: a Support's ammo crate ----

    // When this PC's player last took from each crate (by its id), and whether its line is up.
    private static readonly Dictionary<ushort, float> tookAt = new Dictionary<ushort, float>();
    private bool crateShown;
    private UnityEngine.InputSystem.InputAction interact;

    // An ammo crate under the crosshair (Barricade.CrateFor): its line at the foot of the screen, and F takes a resupply
    // from it, once in a while each (Support.crateEvery); the host's crate loses a take.
    private void Crate()
    {
        var crate = Barricade.CrateFor(movement);
        if (crate == null)
        {
            if (crateShown) { crateShown = false; DetenteLooks.Prompt(null); }
            return;
        }
        float every = Mathf.Max(0f, PlayerClass.Ability("Support", "crateEvery", 20f));
        float wait = tookAt.TryGetValue(crate.Id, out float took) ? took + every - Time.time : 0f;
        crateShown = true;
        DetenteLooks.Prompt(wait > 0f ? Texts.Format("class.crate.wait", Mathf.CeilToInt(wait)) : Texts.Format("class.crate.prompt", crate.Stock));
        if (interact == null && UnityEngine.InputSystem.InputSystem.actions != null)
            interact = UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/Interact", false);
        if (wait > 0f || interact == null || !interact.WasPressedThisFrame() || !movement.InputAvailable || movement.IsDead || movement.IsDowned) return;
        tookAt[crate.Id] = Time.time;
        string owner = crate.Owner != null && crate.Owner.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : "";
        SupportKit.Receive(movement, Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability("Support", "crateMagazines", 2f))),
            Mathf.Max(0f, PlayerClass.Ability("Support", "resupplyOverfill", 25f)), Mathf.Max(0f, PlayerClass.Ability("Support", "resupplySyringe", 25f)), owner);
        if (Barricade.Authority) Barricade.Take(crate.Id);
        else if (TryGetComponent(out PlayerNetworkCombat network)) network.TakeCrate(crate.Id);
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
        // Any foe counts toward the stacks, a range target as a zombie does; only a zombie elite can be staggered.
        var foe = trace.HasHit && trace.Damage == DamageResult.Damaged ? CombatFoe.Of(trace.Hit.collider) : null;
        if (foe != null)
        {
            if (trace.IsHeadshot) shotHead = true;
            else shotBody = true;
            if (foe is EnemyHealth enemy && PlayerClass.IsElite(enemy) && PlayerClass.OwnsWeapon(PlayerClass.Current, data))
                Stagger(enemy, trace.IsHeadshot);
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
        // The White Tiger card holds: no hurt takes the stacks (ClassCards).
        if (cards != null && cards.TigerGuards) return;
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
    internal void Finishing(Component target, Vector3 point, Vector3 from)
    {
        finishing = target;
        finishAt = Time.time;
        finishPoint = point;
        finishFrom = from;
    }

    // Its own mark died at its hand (a zombie, or a range target in the hub): points toward the Desperado's next round
    // (an elite's count more), a Return Back stack, and the bounty's look and sound (its money is CombatFeedHUD's, with
    // the kill, which a practice kill never reaches).
    private void Bounty(Component target)
    {
        var enemy = target as EnemyHealth;
        EarnRounds(Mathf.Max(1, Mathf.RoundToInt(PlayerClass.IsElite(enemy) ? PlayerClass.Ability(HoundRow, "elitePoints", 2f) : 1f)));
        returnStacks = Mathf.Min(Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "returnMax", 5f))), ReturnStacks + 1);
        returnTotal = Mathf.Max(.1f, PlayerClass.Ability(HoundRow, "returnSeconds", 8f));
        returnUntil = Time.time + returnTotal;
        bountyAt = Time.time;
        bool claimed = finishing == target && Time.time - finishAt < 1f;
        finishing = null;
        KitSound.Play(KitSound.Cue.Bounty, claimed ? 1f : .7f);
        // In the world, for everyone: the Desperado's grand burst where its round went in, or a plain bounty's coins.
        Vector3 ground = target.transform.position;
        Vector3 head = claimed ? finishPoint : CombatFoe.Top(target) - Vector3.up * .15f;
        Vector3 from = claimed ? finishFrom : transform.position + Vector3.up * 1.5f;
        BountyBurst.Play(head, from, ground, claimed);
        Claimed?.Invoke(head, from, ground, claimed);
        // The hunter sees the claim in the world as everyone does (the special-moment picks), no banner over the screen.
    }

    // A zombie its own bounty dye splashed died at its hand (DyeMarks; the user's pick, "현상금 염료탄"): points toward
    // the Desperado's next round, as a little bounty, without a bounty's look, sound or Return Back. A dyed elite that
    // also carries the real mark counts as the mark's kill (OnDamaged asks for the mark first).
    private void DyedKill(Component target) => EarnRounds(DyeMarks.KillPoints(target, movement));

    // Points toward the Desperado's next round: every roundPoints of them load one, while the cylinder has room.
    private void EarnRounds(int points)
    {
        if (points <= 0) return;
        bountyPoints += points;
        int need = Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "roundPoints", 2f)));
        var desperado = Desperado;
        while (bountyPoints >= need)
        {
            bountyPoints -= need;
            if (desperado != null && desperado.Ammo < RoundMax) desperado.LoadRound();
        }
        slots?.NotifyAmmo();
    }

    // ---- Point: the Gun Kata ----

    // The class key: for some seconds a gun comes out at once and full (its reserve untouched), and running doesn't
    // spread its shots (WeaponController asks PlayerClass.DrawFactor and MovingSpreadFactor). Refused while it waits.
    private void GunKata()
    {
        if (Time.time < kataReadyAt) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        kataTotal = Mathf.Max(.5f, PlayerClass.Ability(PointRow, "kataSeconds", 8f));
        kataWait = Mathf.Max(kataTotal, PlayerClass.Ability(PointRow, "kataCooldown", 35f));
        kataUntil = Time.time + kataTotal;
        kataReadyAt = Time.time + kataWait;
        // The gun in hand comes up full too, as if just drawn.
        if (slots != null && slots.SelectedSlot >= 0 && slots.SelectedSlot < 2)
        {
            var gun = slots.GetWeapon(slots.SelectedSlot) != null ? slots.GetRuntime(slots.SelectedSlot) : null;
            if (gun != null) { gun.TopUp(); slots.NotifyAmmo(); }
        }
        KitSound.Play(KitSound.Cue.Charge, .7f);
        AlertHUD.Self(Texts.Get("class.point.kata.self"));
        MomentHUD.Kata(kataTotal);
    }

    // ---- Schießhund: the duel ----

    // The class key on a foe: a duel with it (HoundDuel). Refused while it waits, or with no foe under the crosshair.
    private void Duel()
    {
        if (Time.time < duelReadyAt) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        var ping = GetComponent<PlayerPing>();
        var foe = ping != null ? ping.AimedFoe() : null;
        if (foe == null) { KitSound.Play(KitSound.Cue.Refuse, .4f); return; }
        float seconds = Mathf.Max(.5f, PlayerClass.Ability(HoundRow, "duelSeconds", 12f));
        duelWait = Mathf.Max(seconds, PlayerClass.Ability(HoundRow, "duelCooldown", 30f));
        duelReadyAt = Time.time + duelWait;
        bool ace = PlayerPerks.Has(PlayerPerks.HoundAce);
        // Its foe carries the bounty mark too, whatever it was on: the duel's kill is a bounty, and a Desperado round in
        // its head ends it.
        ping.Mark(foe, true);
        HoundDuel.Begin(movement, foe, seconds, ace);
        Dueled?.Invoke(foe, seconds, ace);
        KitSound.Play(KitSound.Cue.Bounty, .8f);
        // Its tag on this screen (the special-moment pick A); the line and the brackets are HoundDuel's, for everyone.
        MomentHUD.Duel(seconds);
        AlertHUD.Self(Texts.Get(ace ? "class.schiesshund.duel.ace" : "class.schiesshund.duel.self"));
    }

    // ---- The ultimate (every card taken, a full gauge: ClassCards.Ultimate) ----

    /// <summary>An Assault's ultimate: adrenaline full and the CHARGE sounded at once (its riders with it, the card held).</summary>
    internal void UltimateCharge()
    {
        if (!PlayerClass.Is(PlayerClass.Assault)) return;
        stacks = StackMax;
        Use();
    }

    /// <summary>A Ghost's ultimate: the Head Hunter full.</summary>
    internal void FillHunter()
    {
        if (!PlayerClass.Is(PlayerClass.Ghost)) return;
        hunter = HunterMax;
        hunterChange = 1;
        hunterChangedAt = Time.time;
        KitSound.Play(KitSound.Cue.Hunter, .9f);
    }

    /// <summary>A Point's ultimate: both guns loaded at once, and the Gunslinger's moment from now.</summary>
    internal void ReloadAll()
    {
        if (slots == null) return;
        for (int slot = 0; slot < 2; slot++)
        {
            var gun = slots.GetWeapon(slot) != null ? slots.GetRuntime(slot) : null;
            if (gun != null && gun.ReloadAside()) refilledAt[slot] = Time.time;
        }
        slots.NotifyAmmo();
        drawnAt = Time.time;
    }

    /// <summary>A Schießhund's ultimate: the Desperado's cylinder full.</summary>
    internal void LoadDesperado()
    {
        var desperado = Desperado;
        if (desperado == null) return;
        for (int i = 0; i < 16 && desperado.Ammo < RoundMax; i++)
        {
            int before = desperado.Ammo;
            desperado.LoadRound();
            if (desperado.Ammo == before) break;
        }
        slots.NotifyAmmo();
        bountyAt = Time.time;
    }

    // ---- Knight: the taunt ----

    private void Taunt()
    {
        if (Time.time < tauntReadyAt) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        tauntTotal = Mathf.Max(.1f, PlayerClass.Ability(KnightRow, "tauntSeconds", 6f));
        tauntWait = Mathf.Max(tauntTotal, PlayerClass.Ability(KnightRow, "tauntCooldown", 25f));
        tauntUntil = Time.time + tauntTotal;
        tauntReadyAt = Time.time + tauntWait;
        Vector3 at = transform.position;
        ReceiveTaunt(movement, at, true);
        AlertHUD.Self(Texts.Get("class.knight.taunt.self"));
        Taunted?.Invoke(at);
    }

    /// <summary>
    /// A Knight taunted at <paramref name="at"/> (this PC's own, or a friend's as their PC said it): the zombies this PC
    /// runs that are already fighting within reach go after them (real only on the host or alone; EnemyTargeting), and
    /// every PC sees the ring go out and hears it, a friend's the softer the further off.
    /// </summary>
    public static void ReceiveTaunt(PlayerMovement knight, Vector3 at, bool mine)
    {
        float reach = Mathf.Max(0f, PlayerClass.Ability(KnightRow, "tauntRadius", 12f));
        EnemyTargeting.Taunt(knight, at, reach, PlayerClass.Ability(KnightRow, "tauntSeconds", 6f));
        TauntRing.Show(at, reach);
        float volume = 1f;
        var local = Local != null ? Local.movement : null;
        if (!mine && local != null) volume = Mathf.Clamp01(1f - Vector3.Distance(local.transform.position, at) / 40f) * .7f;
        KitSound.Play(KitSound.Cue.Taunt, volume);
    }
}
