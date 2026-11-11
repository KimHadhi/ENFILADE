using System;
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
/// - Barricade: the class key puts one up on the ground ahead, across the look; at most a few stand at once, and a new
///   one past that takes the oldest down. Barricade does the rest; the others are told by PlayerNetworkCombat.
/// This PC's own player only. Numbers: Tables/ClassAbilities, row Support.
/// </summary>
[DisallowMultipleComponent]
public sealed class SupportKit : MonoBehaviour
{
    private const string Row = "Support";

    /// <summary>This PC's own player's, while they play a Support.</summary>
    public static SupportKit Local { get; private set; }

    /// <summary>This PC's Support put barricade id up at this spot, facing this way: the others are told (PlayerNetworkCombat).</summary>
    public event Action<ushort, Vector3, float> Built;
    /// <summary>This PC's Support took barricade id down to put up another: the others are told.</summary>
    public event Action<ushort> TookDown;

    private PlayerMovement movement;
    private InputAction interact;
    // bagShown: the most the bag held when last looked at, so a bigger bag (a class card) comes with what it added.
    private float supply = -1f, lastGive = -99f, lastBuild = -99f, bagShown;
    private int made;

    /// <summary>The bag now and the most it holds; what a resupply and a barricade take from it.</summary>
    public float Supply => Mathf.Max(0f, supply);
    public float SupplyMax => Mathf.Max(1f, Number("supplyMax", 100f));
    public float GiveCost => Mathf.Max(1f, Number("resupplyCost", 25f));
    public float BuildCost => Mathf.Max(1f, Number("barricadeCost", 50f));
    public bool CanBuild => Supply >= BuildCost - .001f;
    /// <summary>The health its barricades go up with, class cards in (every other PC is sent it with the barricade).</summary>
    public float BarricadeHealth => Mathf.Max(1f, Number("barricadeHealth", 400f));
    /// <summary>How many of its barricades stand now, and the most at once.</summary>
    public int Standing => movement != null ? Barricade.StandingOf(movement).Count : 0;
    public int MostStanding => Mathf.Max(1, Mathf.RoundToInt(Number("barricadeMax", 2f)));
    /// <summary>Seconds since it last handed ammo over, and since it last put a barricade up (the HUD's flash).</summary>
    public float SinceGive => Time.time - lastGive;
    public float SinceBuild => Time.time - lastBuild;
    /// <summary>The friend F would resupply now (standing, in reach, under the crosshair), or null.</summary>
    public PlayerMovement Offered { get; private set; }

    private void Awake() => movement = GetComponent<PlayerMovement>();

    private void OnDisable()
    {
        if (Local == this) Local = null;
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        if (!PlayerClass.Is(PlayerClass.Support))
        {
            if (Local == this) Local = null;
            Offered = null;
            return;
        }
        Local = this;
        if (supply < 0f) supply = Mathf.Clamp(Number("supplyStart", 100f), 0f, SupplyMax);
        float most = SupplyMax;
        if (bagShown > 0f && most > bagShown) supply += most - bagShown;
        bagShown = most;
        supply = Mathf.Min(most, supply + Mathf.Max(0f, Number("supplyRegen", 1.5f)) * Time.deltaTime);
        Offered = movement.IsDead || movement.IsDowned || !HandsFree() ? null : Aimed();
        if (interact == null && InputSystem.actions != null) interact = InputSystem.actions.FindAction("Player/Interact", false);
        if (Offered != null && interact != null && interact.WasPressedThisFrame() && movement.InputAvailable) Give(Offered);
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

    // ---- The barricade (the class key, ClassKit) ----

    /// <summary>The class key: a barricade on the ground ahead, across the look, if the bag has enough and there is room.</summary>
    public void Build()
    {
        if (movement == null || !movement.IsLocal) return;
        if (!CanBuild)
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            AlertHUD.Self(Texts.Get("class.support.low"));
            return;
        }
        if (!Spot(out Vector3 at, out float yaw))
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            AlertHUD.Self(Texts.Get("class.support.nowhere"));
            return;
        }
        // Past the most standing, the oldest comes down for the new one.
        var mine = Barricade.StandingOf(movement);
        while (mine.Count >= MostStanding)
        {
            ushort oldest = mine[0];
            mine.RemoveAt(0);
            Barricade.Collapse(oldest);
            TookDown?.Invoke(oldest);
        }
        supply -= BuildCost;
        lastBuild = Time.time;
        ushort id = NextId();
        Barricade.Place(id, at, yaw, movement, BarricadeHealth);
        KitSound.Play(KitSound.Cue.Hammer, .9f);
        Built?.Invoke(id, at, yaw);
    }

    // Its owner's client in the high byte, a count in the low one: no two PCs make the same.
    private ushort NextId()
    {
        ulong client = movement.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;
        made++;
        return (ushort)(((int)(client & 0xFF) << 8) | (made & 0xFF));
    }

    // On the ground some way ahead along the look, seen from the eyes, level enough, with nobody and nothing in its way.
    private bool Spot(out Vector3 at, out float yaw)
    {
        at = default;
        yaw = 0f;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return false;
        Vector3 flat = Vector3.ProjectOnPlane(eyes.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(movement.transform.forward, Vector3.up);
        flat.Normalize();
        yaw = Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
        Vector3 above = movement.transform.position + flat * Mathf.Max(.8f, Number("barricadeAhead", 1.8f)) + Vector3.up * 1.2f;
        if (!Physics.Raycast(above, Vector3.down, out RaycastHit ground, 3f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (ground.normal.y < Mathf.Cos(25f * Mathf.Deg2Rad) || ground.collider.transform.IsChildOf(movement.transform)) return false;
        if (ground.collider.GetComponentInParent<EnemyHealth>() != null || ground.collider.GetComponentInParent<PlayerMovement>() != null) return false;
        at = ground.point;
        // Through a wall it can't go up.
        if (Physics.Linecast(eyes.position, at + Vector3.up * .5f, out RaycastHit wall, ~0, QueryTriggerInteraction.Ignore)
            && !wall.collider.transform.IsChildOf(movement.transform) && (wall.point - at).sqrMagnitude > .36f) return false;
        Vector3 middle = at + Vector3.up * (Barricade.Height * .5f + .1f);
        var half = new Vector3(Barricade.Width * .5f, Barricade.Height * .5f - .05f, Barricade.Depth * .5f);
        foreach (var hit in Physics.OverlapBox(middle, half, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(movement.transform)) return false;
        return true;
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
