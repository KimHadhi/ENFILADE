using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F on an enemy (stage 3 of the classes; the user's rule, 2026-11-11: "F가 상호작용이든 … 처형이나 암살로 이어질 거면
/// 거기서 바로 넘어가는데 그런 거 아니면 걍 적 찍게"). When F has nothing else to do here (no execution or takedown, no
/// pickup, no friend to get up, no ladder top, no button, door, can or shop counter), it pings the enemy the crosshair is
/// on, Back 4 Blood's way: the whole team sees it outlined, through walls too, with a diamond and its distance over it
/// (PingMarks). A Schießhund's ping is its teal bounty mark, which its class hunts (ClassKit), unless the foe is a lesser
/// quarry than the one it already hunts: that one is a plain ping (PlayerClass.Grade). The gun gives a nod as it
/// goes (FPSBody.Point; a real left hand pointing wants a clip of its own, the gun-hands stage's). The other PCs hear of it
/// from PlayerNetworkCombat. This PC's own player only. Numbers: Tables/ClassAbilities, Ping.*.
/// Any foe can be pinged (ICombatFoe): a zombie, or a target on the hub's range, where a Schießhund tries its mark out
/// (the user, 2026-11-12: "시순트 표적 찍기도 못 하네").
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPing : MonoBehaviour
{
    private const string Row = "Ping";
    // How fat the look's cast is (m): a ping needn't be dead on, as a shot must.
    private const float Radius = .3f;

    /// <summary>
    /// This PC's player pinged this foe (a zombie or a range target); true for a Schießhund's bounty mark.
    /// PlayerNetworkCombat tells the others.
    /// </summary>
    public event Action<Component, bool> Pinged;

    private PlayerMovement movement;
    private PlayerExecution execution;
    private PlayerRevive revive;
    private WeaponController weapon;
    private InputAction interact;
    private float nextPing;
    private readonly RaycastHit[] hits = new RaycastHit[32];

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        execution = GetComponent<PlayerExecution>();
        revive = GetComponent<PlayerRevive>();
        weapon = GetComponent<WeaponController>();
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal || !movement.InputAvailable || movement.IsDead || movement.IsDowned) return;
        if (interact == null && InputSystem.actions != null) interact = InputSystem.actions.FindAction("Player/Interact", false);
        if (interact == null || !interact.WasPressedThisFrame() || Time.time < nextPing || Busy()) return;
        var foe = Aimed();
        if (foe == null) return;
        nextPing = Time.time + Mathf.Max(0f, PlayerClass.Ability(Row, "gap", .4f));
        bool bounty = PlayerClass.Is(PlayerClass.Schiesshund);
        // A Schießhund's mark moves only onto a quarry as big or bigger than the one it hunts: F on a lesser foe meanwhile
        // is a plain ping for the team and the hunt goes on (the user, 2026-11-12: "엘리트를 조준했는데 일반 좀비를 조준한다면
        // 자기보다 급 낮은 애로 다시 조준하는 건 표식이 옮겨가진 않고 그냥 하이라이트 하는 걸로 바꾸자"). Before, every F
        // moved it, so calling out a runner for the team cost the elite being hunted.
        if (bounty)
        {
            var hunted = PingMarks.BountyOf(movement);
            if (hunted != null && hunted != foe && PlayerClass.Grade(foe) < PlayerClass.Grade(hunted)) bounty = false;
            // In a duel the mark stays on the foe called out (HoundDuel): F elsewhere is the team's plain ping.
            if (HoundDuel.Of(movement) is HoundDuel duel && duel.Foe != null && duel.Foe.gameObject != foe.gameObject) bounty = false;
        }
        PingMarks.Ping(foe, movement, bounty);
        KitSound.Play(bounty ? KitSound.Cue.Mark : KitSound.Cue.Ping, .7f);
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) FPSBody.Holding(weapon)?.Point();
        Pinged?.Invoke(foe, bounty);
    }

    /// <summary>The living foe under the crosshair now, as F would find it, or null: a Schießhund's duel is called on it (ClassKit).</summary>
    public Component AimedFoe() => Aimed();

    /// <summary>
    /// Marks this foe as F would, its sound and the gun's nod with it, the other PCs told: a Schießhund's duel puts its
    /// bounty mark on the foe it calls out (ClassKit), whatever the mark was on.
    /// </summary>
    public void Mark(Component foe, bool bounty)
    {
        if (foe == null || movement == null) return;
        PingMarks.Ping(foe, movement, bounty);
        KitSound.Play(bounty ? KitSound.Cue.Mark : KitSound.Cue.Ping, .7f);
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) FPSBody.Holding(weapon)?.Point();
        Pinged?.Invoke(foe, bounty);
    }

    // F has a job here already: whatever offers it on screen goes first.
    private bool Busy()
    {
        if (execution == null) execution = GetComponent<PlayerExecution>();
        if (execution != null && (execution.IsExecuting || execution.Candidate != null)) return true;
        if (FieldPickups.Targeting) return true;
        if (revive == null) revive = GetComponent<PlayerRevive>();
        if (revive != null && revive.Candidate != null) return true;
        // A Détente treating a friend by hand, or bringing one back; or a Détente's offer waiting for this player's F.
        if (DetenteKit.Local != null && DetenteKit.Local.TakesInteract) return true;
        if (DetenteKit.Answering) return true;
        // A Support with a friend in reach to resupply, or a worn barricade to mend.
        if (SupportKit.Local != null && (SupportKit.Local.Offered != null || SupportKit.Local.Mendable != null || SupportKit.Local.Planning)) return true;
        // An ammo crate under the crosshair: F takes from it (ClassKit).
        if (Barricade.CrateFor(movement) != null) return true;
        if (movement.Abilities != null && movement.Abilities.Fixtures.ClimbDownLadder != null) return true;
        // The hub's stations (its map table, lockers, gun rack, ammo crate, ready button) take F when looked at.
        return CampaignRun.Offering || ShopMenu.IsOpen || HubLobby.Offering;
    }

    // The living foe the crosshair is on (a zombie or a range target): the nearest thing along the look that isn't this
    // player, if it is one. A wall or the floor in front of it takes the ping instead (nothing happens).
    private Component Aimed()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = Mathf.Max(1f, PlayerClass.Ability(Row, "reach", 120f));
        int count = Physics.SphereCastNonAlloc(eyes.position, Radius, eyes.forward, hits, reach, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Collider first = null;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
            // A cast that starts inside something reports it at no distance: only a foe's body counts there.
            if (hit.distance <= 0f && CombatFoe.Of(hit.collider) == null) continue;
            if (hit.distance < nearest) { nearest = hit.distance; first = hit.collider; }
        }
        var foe = CombatFoe.Of(first);
        return CombatFoe.IsAlive(foe) ? foe : null;
    }
}
