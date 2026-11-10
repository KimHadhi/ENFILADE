using System;
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
///   the shotgun refills and back (Killing Floor 2's gunslinger feel, the user's "화력이 끊기지 않고").
/// The other classes' abilities come in later stages (Classes.csv ready 0). Numbers: Tables/ClassAbilities.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassKit : MonoBehaviour
{
    private const string AssaultRow = "Assault", PointRow = "Point";

    /// <summary>This PC's own player's kit.</summary>
    public static ClassKit Local { get; private set; }

    /// <summary>This PC's player spent a full adrenaline on a CHARGE here: friends' PCs are told (PlayerNetworkCombat).</summary>
    public event Action<Vector3> Charged;

    private PlayerMovement movement;
    private PlayerWeaponSlots slots;
    private WeaponController weapon;
    private InputAction ability;
    private bool kitGiven;
    private int stacks;
    private float lastKill = -99f, nextEbb, chargeUntil, chargeTotal = 1f;
    // Point: when each gun slot's gun was last seen put away and short (-1: not waiting), and when one last refilled.
    private readonly float[] asideSince = { -1f, -1f }, refilledAt = { -99f, -99f };

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

    /// <summary>The factor this player's class puts on their shots now (adrenaline, a CHARGE).</summary>
    public float DamageFactor
    {
        get
        {
            float factor = 1f;
            if (PlayerClass.Is(PlayerClass.Assault)) factor *= 1f + stacks * PlayerClass.Ability(AssaultRow, "damagePerStack", 3f) / 100f;
            if (chargeUntil > Time.time) factor *= 1f + PlayerClass.Ability(AssaultRow, "chargeDamage", 25f) / 100f;
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
        if (Local == this) Local = null;
    }

    private void Start() => TryGiveKit();

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        Local = this;
        if (!kitGiven) TryGiveKit();
        Ebb();
        if (PlayerClass.Is(PlayerClass.Point)) ReloadAside();
        if (ability == null && InputSystem.actions != null)
        {
            KeyBindings.EnsureRestored();
            ability = InputSystem.actions.FindAction("Player/ClassAbility", false);
        }
        if (ability != null && ability.WasPressedThisFrame() && movement.InputAvailable && !movement.IsDead && !movement.IsHeld) Use();
    }

    // The class's guns in slots 1 and 2 once this player is ready, every chapter (a run starts with them).
    private void TryGiveKit()
    {
        if (kitGiven || movement == null || !movement.IsLocal || slots == null || !slots.isActiveAndEnabled) return;
        kitGiven = true;
        var info = PlayerClass.Current;
        if (info == null) return;
        Give(0, info.Primary);
        Give(1, info.Secondary);
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
            ReceiveCharge(at, Texts.Get("class.assault.charge.self"));
            Charged?.Invoke(at);
            return;
        }
        // Not built for this class yet.
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
    }

    // ---- Assault: adrenaline ----

    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || !report.IsKillingBlow || !(report.Target is EnemyHealth)) return;
        if (!PlayerPerks.IsMine(report.Hit.Source) || !PlayerClass.Is(PlayerClass.Assault)) return;
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
}
