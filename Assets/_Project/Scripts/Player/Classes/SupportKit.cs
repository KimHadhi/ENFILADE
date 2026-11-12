using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Stage 4 of the classes, the Support (the user's design, 2026-11-11: "아군을 위한 클래스 … Guts &amp; Blackpowder의
/// Sapper처럼 … 임시 바리케이드 … KF2의 서포트처럼 아군에게 총알이나 보급품을 보충 … 걸어다니는 간이 상점"), with their old
/// memo's Resupply and Fully Loaded. A supply bag that fills back by itself pays for both its jobs:
/// - Resupply: Interact (F) on a friend in reach, when F has nothing else to do there, hands them ammo: each of their guns
///   some magazines more, up to a quarter past what it starts with (Fully Loaded), and a Détente's syringe some more.
///   Their own PC fills it (PlayerNetworkCombat). KF2's Support hands ammo over with the use key too.
/// - Barricade: the class key held shows where one would go up, a see-through blue barricade exactly where and how it
///   would stand (BarricadeGhost), red where it can't, and let go it goes up there (the user, 2026-11-12: "능력 키 꾹
///   누르고 있으면 … 미리 보기 뜨면 좋을 듯. 설치 불가능하면 빨간색으로 뜨고 키 놓으면 거기에 배치"). A tap puts it up at
///   once where the preview stood, as a tap of the stone's T throws at once; the guard key (right click) while holding
///   calls it off. It stands where the view comes down to the ground, between the nearest and the farthest reach ahead,
///   across the look. At most a few stand at once; a new one past that takes the oldest down, which the preview shows
///   blinking amber. The host's or a lone PC's goes up at once; a client's PC asks the host, which checks the spot again
///   against its own world (Barricade.Judge) and puts it up for everyone, or says why not and the bag gets its share back.
/// - Mending: F held on a worn barricade in reach, anyone's, puts health back into it, its planks nailed back on (the
///   user, 2026-11-12: "G&amp;B 공병처럼 수리하고 유지 보수할 수 있으면 재밌을 듯"); a resupply goes first. It costs the
///   bag nothing: a hammer does it, and the welding torch card does it faster on a fuel of its own that fills back
///   ("처음에는 망치로 고쳐 … 나중에 카드 업그레이드하면 토치로 더 빠르게"), the tool in hand as it works (RepairLooks).
/// A class picked starts it afresh (Fresh, from ClassKit on PlayerClass.Picked): the bag as it starts, its barricades down.
/// This PC's own player only. Numbers: Tables/ClassAbilities, row Support.
/// </summary>
[DisallowMultipleComponent]
public sealed class SupportKit : MonoBehaviour
{
    private const string Row = "Support";
    // How long a client waits for the host's word on a barricade it asked for before it may ask for another (s), and how
    // long the preview stays red where one was turned down (s).
    private const float AnswerWait = 3f, RefusedShow = .35f;
    // How often a client's hammer tells the host what it put in (s), and the beat of the hammer's knock (s).
    private const float MendEvery = .2f, KnockEvery = .45f;

    /// <summary>This PC's own player's, while they play a Support.</summary>
    public static SupportKit Local { get; private set; }

    /// <summary>
    /// This PC's Support put barricade id up at this spot, facing this way (on the host or alone, where it decides that
    /// itself): the others are told (PlayerNetworkCombat).
    /// </summary>
    public event Action<ushort, Vector3, float> Built;
    /// <summary>This PC's Support took barricade id down (for another past its most, or a class picked): the others are told.</summary>
    public event Action<ushort> TookDown;

    private PlayerMovement movement;
    private InputAction interact;
    private float supply = -1f, lastGive = -99f, lastBuild = -99f;
    private int made;
    // The preview while the class key is held: where and how it would stand now and whether it may; and the one to come
    // down for it. Both drawn by BarricadeGhost, made on first use.
    private BarricadeGhost ghost, doomed;
    private bool planning;
    private Barricade.Fit planFit = Barricade.Fit.Ok;
    private Vector3 planAt;
    private float planYaw;
    // A client's barricade asked of the host and not answered yet (0: none), what it took from the bag and when; one asked
    // for before a class pick, to come down again should it still go up; where the key was last let go (the preview stays
    // there while the host answers, or red a moment when turned down), and when one was last turned down.
    private ushort pendingId, dropped;
    private float pendingAt, pendingCost, refusedAt = -99f;
    private Vector3 heldSpot;
    private float heldYaw;
    private readonly List<ushort> taken = new List<ushort>();
    private int worldMask;
    // A client's mending not yet told the host: how much, on which barricade, and when it may tell next; the hammer's
    // next knock (or the empty bag's refusal).
    private float mendOwed, nextMendSend, nextKnock;
    private ushort mendId;
    // The welding torch's fuel (-1: full at the next look), and the tool shown in hand.
    private float torchFuel = -1f;
    private RepairLooks.Tool shownTool;

    /// <summary>The bag now and the most it holds; what a resupply and a barricade take from it.</summary>
    public float Supply => Mathf.Max(0f, supply);
    public float SupplyMax => Mathf.Max(1f, Number("supplyMax", 100f));
    public float GiveCost => Mathf.Max(1f, Number("resupplyCost", 25f));
    public float BuildCost => Mathf.Max(1f, Number("barricadeCost", 50f));
    public bool CanBuild => Supply >= BuildCost - .001f;
    /// <summary>How many of its barricades stand now, and the most at once.</summary>
    public int Standing => movement != null ? Barricade.CountOf(movement) : 0;
    public int MostStanding => Mathf.Max(1, Mathf.RoundToInt(Number("barricadeMax", 2f)));
    /// <summary>Seconds since it last handed ammo over, and since it last put a barricade up (the HUD's flash).</summary>
    public float SinceGive => Time.time - lastGive;
    /// <summary>The class key is held to place a barricade (the preview is up), and whether one may go where it stands now.</summary>
    public bool Planning => planning;
    public Barricade.Fit PlanFit => planFit;

    /// <summary>The ultimate (every card taken, a full gauge: ClassCards.Ultimate): the bag full at once.</summary>
    internal void Restock()
    {
        supply = SupplyMax;
        lastGive = Time.time;
    }
    public float SinceBuild => Time.time - lastBuild;
    /// <summary>The friend F would resupply now (standing, in reach, under the crosshair), or null.</summary>
    public PlayerMovement Offered { get; private set; }
    /// <summary>The worn barricade F held would mend now (under the crosshair, in reach, anyone's), or null; and whether F is held on it.</summary>
    public Barricade Mendable { get; private set; }
    public bool Mending { get; private set; }
    /// <summary>The welding torch card held; its fuel now and the most; whether it does the mending now (else the hammer).</summary>
    public bool HasTorch => PlayerPerks.Has(PlayerPerks.SupportTorch);
    public float TorchFuel => Mathf.Max(0f, torchFuel);
    public float TorchFuelMax => Mathf.Max(1f, Number("torchFuel", 100f));
    public bool Torching => HasTorch && TorchFuel > .5f;
    /// <summary>The tool in hand changed (0 none, 1 hammer, 2 torch): the others see it (PlayerNetworkCombat).</summary>
    public event Action<byte> ToolHeld;

    private void Awake() => movement = GetComponent<PlayerMovement>();

    private void OnDisable()
    {
        if (Local == this) Local = null;
        planning = false;
        HidePreview();
    }

    private void OnDestroy()
    {
        if (ghost != null) Destroy(ghost.gameObject);
        if (doomed != null) Destroy(doomed.gameObject);
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        if (!PlayerClass.Is(PlayerClass.Support))
        {
            if (Local == this) Local = null;
            Offered = null;
            Mendable = null;
            Mending = false;
            Show(RepairLooks.Tool.None);
            planning = false;
            HidePreview();
            return;
        }
        Local = this;
        if (supply < 0f) supply = Mathf.Clamp(Number("supplyStart", 100f), 0f, SupplyMax);
        supply = Mathf.Min(SupplyMax, supply + Mathf.Max(0f, Number("supplyRegen", 1.5f)) * Time.deltaTime);
        Offered = movement.IsDead || movement.IsDowned || !HandsFree() ? null : Aimed();
        if (interact == null && InputSystem.actions != null) interact = InputSystem.actions.FindAction("Player/Interact", false);
        if (Offered != null && interact != null && interact.WasPressedThisFrame() && movement.InputAvailable) Give(Offered);
        // No friend there: a worn barricade under the crosshair, mended while F is held.
        Mendable = Offered == null && !movement.IsDead && !movement.IsDowned && HandsFree() ? AimedBarricade() : null;
        Mending = Mendable != null && interact != null && interact.IsPressed() && movement.InputAvailable && !movement.IsHeld;
        bool torching = Mending && Torching;
        if (Mending) Mend(Mendable);
        if (!Mending || Time.time >= nextMendSend) FlushMend();
        // The torch's fuel fills back while it rests; the tool in hand is the one at work.
        if (torchFuel < 0f) torchFuel = TorchFuelMax;
        if (!torching) torchFuel = Mathf.Min(TorchFuelMax, TorchFuel + Mathf.Max(0f, Number("torchRegen", 8f)) * Time.deltaTime);
        Show(!Mending ? RepairLooks.Tool.None : torching ? RepairLooks.Tool.Torch : RepairLooks.Tool.Hammer);
        Plan();
        Preview();
    }

    /// <summary>
    /// A class was picked (PlayerClass.Picked, by ClassKit): a fresh start, as a chapter's (the user, 2026-11-12: "서포트로
    /// 바리케이드 설치하고 다른 거 바꿨다가 다시 서포트로 돌아오면 다시 바리케이드를 설치할 수 있게"). The bag as it starts,
    /// no preview, and every barricade of the last pick down, the others told; one asked of the host and not yet answered
    /// comes down as soon as it goes up.
    /// </summary>
    internal void Fresh()
    {
        planning = false;
        if (pendingId != 0) dropped = pendingId;
        pendingId = 0;
        refusedAt = -99f;
        Mendable = null;
        Mending = false;
        mendOwed = 0f;
        torchFuel = -1f;
        Show(RepairLooks.Tool.None);
        supply = -1f;
        lastGive = lastBuild = -99f;
        HidePreview();
        if (movement == null) return;
        foreach (ushort id in Barricade.StandingOf(movement))
        {
            Barricade.Collapse(id);
            TookDown?.Invoke(id);
        }
    }

    // ---- Resupply ----

    private void Give(PlayerMovement friend)
    {
        if (Supply < GiveCost - .001f)
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            AlertHUD.Self(Texts.Get("class.support.low"));
            return;
        }
        supply -= GiveCost;
        lastGive = Time.time;
        int magazines = Mathf.Max(1, Mathf.RoundToInt(Number("resupplyMagazines", 2f)));
        float overfill = Mathf.Max(0f, Number("resupplyOverfill", 25f)), syringe = Mathf.Max(0f, Number("resupplySyringe", 25f));
        if (friend.TryGetComponent(out PlayerNetworkCombat network) && network.IsSpawned) network.Resupply(magazines, overfill, syringe);
        // Handing a friend ammo is the Support's fight: it fills the gauge (UltimateCharge.AwardHelp).
        UltimateCharge.AwardHelp(UltimateCharge.Help.Resupplied);
        KitSound.Play(KitSound.Cue.Supply, .8f);
        AlertHUD.Self(Texts.Format("class.support.gave", NameOf(friend)));
    }

    /// <summary>
    /// This PC's own player was resupplied by a friend's Support (PlayerNetworkCombat): each of their guns gets that many
    /// magazines more in reserve, up to its starting reserve and <paramref name="overfill"/> per cent past it (Fully
    /// Loaded), and a Détente's syringe gets <paramref name="syringe"/> more. A Schießhund's Desperado is left alone: its
    /// bounties alone load it.
    /// </summary>
    public static void Receive(PlayerMovement player, int magazines, float overfill, float syringe, string from)
    {
        if (player == null || !player.TryGetComponent(out PlayerWeaponSlots slots)) return;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            if (data == null || data.Category != WeaponCategory.Ranged || slots.KeepsOwnRounds(i)) continue;
            var gun = slots.GetRuntime(i);
            int cap = Mathf.CeilToInt(data.InitialReserveAmmo * (1f + Mathf.Max(0f, overfill) / 100f));
            int room = cap - gun.ReserveAmmo;
            if (room > 0) gun.AddReserve(Mathf.Min(room, data.MagazineSize * Mathf.Max(1, magazines)));
        }
        slots.NotifyAmmo();
        if (syringe > 0f && DetenteKit.Local != null) DetenteKit.Local.Refill(syringe);
        KitSound.Play(KitSound.Cue.PickAmmo, .9f);
        AmmoHUD.Blink(KitStyle.Ice);
        AlertHUD.Self(Texts.Format("class.support.got", from));
    }

    // ---- The barricade (the class key; ClassKit leaves a Support's key to this) ----

    // The class key, the key window's Player/ClassAbility: held, the preview; let go (or tapped), the barricade there. The
    // guard key calls it off, and so does anything that takes the hands (a menu, a hold, going down, a counter, death).
    private void Plan()
    {
        var key = KeyBindings.Find("Player/ClassAbility");
        bool able = key != null && movement.InputAvailable && !movement.IsDead && !movement.IsDowned && !movement.IsHeld
            && !movement.CombatInputLocked;
        if (!able)
        {
            planning = false;
            return;
        }
        if (KeyBindings.Pressed(key)) planning = true;
        if (!planning) return;
        // The guard key (right click unless the key window says otherwise): off, and letting go then puts nothing up.
        if (KeyBindings.Pressed("Combat/Guard"))
        {
            planning = false;
            KitSound.Play(KitSound.Cue.Refuse, .25f);
            return;
        }
        planFit = Spot(out planAt, out planYaw);
        if (KeyBindings.Held(key)) return;
        planning = false;
        Build(planFit, planAt, planYaw);
    }

    // Let go: a barricade where the preview stood, by the very rule it was drawn by.
    private void Build(Barricade.Fit fit, Vector3 at, float yaw)
    {
        // A client still waiting for the host's word on the last one asks for none other meanwhile.
        if (pendingId != 0 && Time.time - pendingAt < AnswerWait)
        {
            KitSound.Play(KitSound.Cue.Refuse, .35f);
            return;
        }
        pendingId = 0;
        if (fit != Barricade.Fit.Ok)
        {
            Refuse(fit, at, yaw);
            return;
        }
        supply -= BuildCost;
        lastBuild = Time.time;
        ushort id = NextId();
        if (Barricade.Authority)
        {
            Raise(id, at, yaw);
            return;
        }
        // A client asks the host, whose world is the true one (its zombies stand where they really are). The bag pays now
        // and gets it back if the host says no; the preview stays where it was let go until the host's word comes.
        if (!movement.TryGetComponent(out PlayerNetworkCombat network) || !network.IsSpawned)
        {
            supply += BuildCost;
            Refuse(Barricade.Fit.Blocked, at, yaw);
            return;
        }
        pendingId = id;
        pendingAt = Time.time;
        pendingCost = BuildCost;
        heldSpot = at;
        heldYaw = yaw;
        network.AskBarricade(id, at, yaw, MostStanding);
    }

    // The host's or a lone PC's own: up at once, its oldest down past its most, and the others told.
    private void Raise(ushort id, Vector3 at, float yaw)
    {
        taken.Clear();
        Barricade.Raise(id, at, yaw, movement, MostStanding, taken);
        foreach (ushort old in taken) TookDown?.Invoke(old);
        KitSound.Play(KitSound.Cue.Hammer, .9f);
        Built?.Invoke(id, at, yaw);
    }

    /// <summary>
    /// The host put this PC's Support's barricade <paramref name="id"/> up (PlayerNetworkCombat, on this PC): the one it
    /// asked for stands, with its hammer. One asked for before a class pick comes down again at once.
    /// </summary>
    internal void Placed(ushort id)
    {
        if (id != 0 && id == dropped)
        {
            dropped = 0;
            Barricade.Collapse(id);
            TookDown?.Invoke(id);
            return;
        }
        if (id != pendingId) return;
        pendingId = 0;
        KitSound.Play(KitSound.Cue.Hammer, .9f);
    }

    /// <summary>The host turned barricade <paramref name="id"/> down (its world had no room there): the bag gets its share back, and why is said.</summary>
    internal void Refused(ushort id, Barricade.Fit fit)
    {
        if (id == 0 || id != pendingId) return;
        pendingId = 0;
        supply = Mathf.Min(SupplyMax, Supply + pendingCost);
        Refuse(fit, heldSpot, heldYaw);
    }

    // Not there: the refusal's sound, why on the screen, and the preview red a moment where it was let go.
    private void Refuse(Barricade.Fit fit, Vector3 at, float yaw)
    {
        KitSound.Play(KitSound.Cue.Refuse, .5f);
        AlertHUD.Self(Why(fit));
        refusedAt = Time.time;
        heldSpot = at;
        heldYaw = yaw;
    }

    /// <summary>Why a barricade can't go up there, in words (the alert, and the HUD's note while the key is held).</summary>
    public static string Why(Barricade.Fit fit)
    {
        switch (fit)
        {
            case Barricade.Fit.NoGround: return Texts.Get("class.support.fit.ground");
            case Barricade.Fit.Steep: return Texts.Get("class.support.fit.steep");
            case Barricade.Fit.Walled: return Texts.Get("class.support.fit.wall");
            case Barricade.Fit.Blocked: return Texts.Get("class.support.fit.blocked");
            case Barricade.Fit.Far: return Texts.Get("class.support.fit.far");
            case Barricade.Fit.Short: return Texts.Get("class.support.low");
            default: return Texts.Get("class.support.nowhere");
        }
    }

    // Its owner's client in the high byte, a count in the low one: no two PCs make the same.
    private ushort NextId()
    {
        ulong client = movement.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;
        made++;
        return (ushort)(((int)(client & 0xFF) << 8) | (made & 0xFF));
    }

    /// <summary>
    /// The placement rule on the builder's PC, the preview's and the build's alike: it stands where the view comes down to
    /// the ground between the nearest and the farthest reach ahead (looking level or up, the farthest; at a wall, just
    /// before it; at a step, ramp or ledge, on it), across the look. There must be ground there, level enough, with
    /// nothing in its way (Barricade.Room, which the host checks again), no wall between the eyes and it, and a
    /// barricade's worth in the bag.
    /// </summary>
    private Barricade.Fit Spot(out Vector3 at, out float yaw)
    {
        var feet = movement.transform.position;
        at = feet;
        yaw = movement.transform.eulerAngles.y;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return Barricade.Fit.NoGround;
        Vector3 look = eyes.forward, flat = Vector3.ProjectOnPlane(look, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(movement.transform.forward, Vector3.up);
        flat.Normalize();
        yaw = Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
        float near = Mathf.Max(.8f, Number("barricadeAhead", 1.8f)), far = Mathf.Max(near, Number("barricadeReach", 3.5f));
        // How far ahead the view comes down to the floor under the feet (looking level or up: never, so the farthest) ...
        float ahead = far;
        if (look.y < -.01f) ahead = Vector3.Dot(eyes.position + look * ((eyes.position.y - feet.y) / -look.y) - feet, flat);
        // ... unless the view meets something first: ground (a step, a ramp, a ledge), or a wall it would stand just before.
        if (worldMask == 0) worldMask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy") & ~(1 << movement.gameObject.layer);
        if (Physics.Raycast(eyes.position, look, out RaycastHit seen, far + 3f, worldMask, QueryTriggerInteraction.Ignore)
            && !seen.collider.transform.IsChildOf(movement.transform))
        {
            float there = Vector3.Dot(seen.point - feet, flat);
            ahead = seen.normal.y >= .7f ? there : Mathf.Min(ahead, there - Barricade.Depth * .5f - .1f);
        }
        ahead = Mathf.Clamp(ahead, near, far);
        Vector3 above = feet + flat * ahead + Vector3.up * 1.2f;
        if (!Physics.Raycast(above, Vector3.down, out RaycastHit ground, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            at = feet + flat * ahead;
            return Barricade.Fit.NoGround;
        }
        at = ground.point;
        var fit = Barricade.Room(at, yaw, movement.transform);
        if (fit != Barricade.Fit.Ok) return fit;
        // Through a wall it can't go up: the eyes must see its middle (a hit right at its foot is the ground it stands on;
        // a zombie or a friend between is no wall).
        if (Physics.Linecast(eyes.position, at + Vector3.up * .5f, out RaycastHit wall, worldMask, QueryTriggerInteraction.Ignore)
            && !wall.collider.transform.IsChildOf(movement.transform) && (wall.point - at).sqrMagnitude > .36f)
            return Barricade.Fit.Walled;
        return CanBuild ? Barricade.Fit.Ok : Barricade.Fit.Short;
    }

    // The preview while the key is held: blue where it may go up, red where not, and amber over the oldest it would take
    // down. Let go, it stays where it was let go while a client waits for the host's word, and red a moment if turned down.
    private void Preview()
    {
        float alpha = Mathf.Clamp01(Number("previewAlpha", .34f));
        if (planning)
        {
            bool fits = planFit == Barricade.Fit.Ok;
            Ghost().Show(planAt, planYaw, fits ? BarricadeGhost.Look.Fits : BarricadeGhost.Look.Refused, alpha);
            var oldest = fits && Standing >= MostStanding ? Barricade.OldestOf(movement) : null;
            if (oldest != null) Doomed().Show(oldest.transform.position, oldest.transform.eulerAngles.y, BarricadeGhost.Look.Doomed, alpha);
            else if (doomed != null) doomed.Hide();
            return;
        }
        if (doomed != null) doomed.Hide();
        if (pendingId != 0 && Time.time - pendingAt < AnswerWait) Ghost().Show(heldSpot, heldYaw, BarricadeGhost.Look.Fits, alpha * 1.4f);
        else if (Time.time - refusedAt < RefusedShow) Ghost().Show(heldSpot, heldYaw, BarricadeGhost.Look.Refused, alpha);
        else if (ghost != null) ghost.Hide();
    }

    private BarricadeGhost Ghost() => ghost != null ? ghost : ghost = BarricadeGhost.Make("Barricade Preview");

    private BarricadeGhost Doomed() => doomed != null ? doomed : doomed = BarricadeGhost.Make("Barricade Preview (Oldest)");

    private void HidePreview()
    {
        if (ghost != null) ghost.Hide();
        if (doomed != null) doomed.Hide();
    }

    // The friend under the crosshair within reach, standing (the Détente's way of choosing).
    private PlayerMovement Aimed()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = Number("resupplyReach", 3.5f), best = Number("resupplyAngle", 14f);
        PlayerMovement found = null;
        foreach (var other in PlayerRoster.All)
        {
            if (other == null || other == movement || !other.IsInitialized || other.DamageReceiver == null) continue;
            if (!other.DamageReceiver.IsAlive || other.DamageReceiver.IsDowned) continue;
            Vector3 chest = other.transform.position + Vector3.up * 1.1f, to = chest - eyes.position;
            if (to.sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eyes.forward, to);
            if (angle > best) continue;
            if (Physics.Linecast(eyes.position, chest, out RaycastHit hit, ~(1 << movement.gameObject.layer), QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(other.transform)) continue;
            best = angle;
            found = other;
        }
        return found;
    }

    // Whatever else Interact would do here goes first (an execution, a pickup, getting a downed friend up, a ladder, a
    // mission's button, a door, a shop).
    // ---- Mending (F held on a worn barricade) ----

    // The worn barricade under the crosshair within reach, anyone's (a sapper mends whatever stands), or null.
    private Barricade AimedBarricade()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = Mathf.Max(.5f, Number("repairReach", 3.5f));
        if (!Physics.Raycast(eyes.position, eyes.forward, out RaycastHit hit, reach, ~(1 << movement.gameObject.layer), QueryTriggerInteraction.Ignore))
            return null;
        var barricade = hit.collider.GetComponentInParent<Barricade>();
        return barricade != null && barricade.Worn ? barricade : null;
    }

    // The tool at work: the hammer at its pace, or the torch faster while its fuel lasts (spent by the health put in;
    // empty, the hammer takes over). The bag pays nothing. The host's or a lone PC's mends at once; a client's adds it up
    // and tells the host every so often (FlushMend), whose word brings the planks back on this copy.
    private void Mend(Barricade barricade)
    {
        bool torch = Torching;
        float rate = Mathf.Max(0f, torch ? Number("torchRate", 120f) : Number("repairRate", 40f));
        if (mendId != barricade.Id) FlushMend();
        float amount = Mathf.Min(rate * Time.deltaTime, barricade.MostHealth - barricade.Health - mendOwed);
        if (torch)
        {
            float cost = Mathf.Max(0f, Number("torchCost", 15f)) / 100f;
            if (cost > 0f) amount = Mathf.Min(amount, TorchFuel / cost);
            torchFuel = Mathf.Max(0f, TorchFuel - Mathf.Max(0f, amount) * cost);
        }
        if (amount <= 0f) return;
        // Keeping the team's wall up is the Support's fight too: it fills the gauge (UltimateCharge.AwardHelp).
        UltimateCharge.AwardHelp(UltimateCharge.Help.Repaired, amount);
        // The hammer knocks in time with its strokes (the torch hisses by itself: RepairLooks).
        if (!torch && Time.time >= nextKnock)
        {
            nextKnock = Time.time + KnockEvery;
            KitSound.Play(KitSound.Cue.Hammer, .7f);
        }
        if (Barricade.Authority)
        {
            Barricade.Mend(barricade.Id, amount);
            return;
        }
        mendId = barricade.Id;
        mendOwed += amount;
    }

    // The tool in hand, here and in the others' sight, when it changes.
    private void Show(RepairLooks.Tool tool)
    {
        if (tool == shownTool) return;
        shownTool = tool;
        RepairLooks.Hold(movement, tool);
        ToolHeld?.Invoke((byte)tool);
    }

    // A client's mending so far, told the host.
    private void FlushMend()
    {
        if (mendOwed <= 0f) return;
        if (movement.TryGetComponent(out PlayerNetworkCombat network) && network.IsSpawned) network.SendBarricadeMend(mendId, mendOwed);
        mendOwed = 0f;
        nextMendSend = Time.time + MendEvery;
    }

    private bool HandsFree()
    {
        var execution = movement.Execution;
        if (execution != null && (execution.IsExecuting || execution.Candidate != null)) return false;
        if (FieldPickups.Targeting) return false;
        if (TryGetComponent(out PlayerRevive revive) && revive.Candidate != null) return false;
        if (movement.Abilities != null && movement.Abilities.Fixtures.ClimbDownLadder != null) return false;
        return !CampaignRun.Offering && !ShopMenu.IsOpen;
    }

    private static string NameOf(PlayerMovement player)
        => player.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName : player.name;

    private static float Number(string name, float fallback) => PlayerClass.Ability(Row, name, fallback);
}
