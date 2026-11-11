using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F on an enemy (stage 3 of the classes; the user's rule, 2026-11-11: "F가 상호작용이든 … 처형이나 암살로 이어질 거면
/// 거기서 바로 넘어가는데 그런 거 아니면 걍 적 찍게"). When F has nothing else to do here (no execution or takedown, no
/// pickup, no friend to get up, no ladder top, no button, door, can or shop counter), it pings the enemy the crosshair is
/// on, Back 4 Blood's way: the whole team sees it outlined, through walls too, with a diamond and its distance over it
/// (PingMarks). A Schießhund's ping is its teal bounty mark, which its class hunts (ClassKit). The gun gives a nod as it
/// goes (FPSBody.Point; a real left hand pointing wants a clip of its own, the gun-hands stage's). The other PCs hear of it
/// from PlayerNetworkCombat. This PC's own player only. Numbers: Tables/ClassAbilities, Ping.*.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPing : MonoBehaviour
{
    private const string Row = "Ping";
    // How fat the look's cast is (m): a ping needn't be dead on, as a shot must.
    private const float Radius = .3f;

    /// <summary>This PC's player pinged this enemy; true for a Schießhund's bounty mark. PlayerNetworkCombat tells the others.</summary>
    public event Action<EnemyHealth, bool> Pinged;

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
        var enemy = Aimed();
        if (enemy == null) return;
        nextPing = Time.time + Mathf.Max(0f, PlayerClass.Ability(Row, "gap", .4f));
        bool bounty = PlayerClass.Is(PlayerClass.Schiesshund);
        PingMarks.Ping(enemy, movement, bounty);
        KitSound.Play(bounty ? KitSound.Cue.Mark : KitSound.Cue.Ping, .7f);
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) FPSBody.Holding(weapon)?.Point();
        Pinged?.Invoke(enemy, bounty);
    }

    // F has a job here already: whatever offers it on screen goes first.
    private bool Busy()
    {
        if (execution == null) execution = GetComponent<PlayerExecution>();
        if (execution != null && (execution.IsExecuting || execution.Candidate != null)) return true;
        if (FieldPickups.Targeting) return true;
        if (revive == null) revive = GetComponent<PlayerRevive>();
        if (revive != null && revive.Candidate != null) return true;
        // A Détente treating a friend by hand, or bringing one back.
        if (DetenteKit.Local != null && DetenteKit.Local.TakesInteract) return true;
        if (movement.Abilities != null && movement.Abilities.Fixtures.ClimbDownLadder != null) return true;
        return CampaignRun.Offering || ShopMenu.IsOpen;
    }

    // The living enemy the crosshair is on: the nearest thing along the look that isn't this player, if it is one. A wall
    // or the floor in front of it takes the ping instead (nothing happens).
    private EnemyHealth Aimed()
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
            // A cast that starts inside something reports it at no distance: only an enemy's body counts there.
            if (hit.distance <= 0f && hit.collider.GetComponentInParent<EnemyHealth>() == null) continue;
            if (hit.distance < nearest) { nearest = hit.distance; first = hit.collider; }
        }
        var enemy = first != null ? first.GetComponentInParent<EnemyHealth>() : null;
        return enemy != null && enemy.IsAlive ? enemy : null;
    }
}
