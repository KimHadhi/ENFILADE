using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F on an enemy (stage 3 of the classes; the user's rule, 2026-11-11: "F가 상호작용이든 … 처형이나 암살로 이어질 거면
/// 거기서 바로 넘어가는데 그런 거 아니면 걍 적 찍게"). When F has nothing else to do here (no execution or takedown, no
/// pickup, no friend to get up, no ladder top, no button, door, can or shop counter), it pings the enemy the crosshair is
/// on, Back 4 Blood's way: the whole team sees it outlined, through walls too, with a diamond and its distance over it
/// (PingMarks). A Schießhund's ping is its teal bounty mark, which its class hunts (ClassKit), unless the foe is a lesser
/// quarry than the one it already hunts: that one is a plain ping (PlayerClass.Grade). The free hand points at it for a
/// moment as it goes (PlayerPoint.Flash, the held point's drawn hand), a quick tap of the left hand alone when aimed down
/// the sights; with the hand busy (a reload, a draw, a throw) the mark and its sound say it alone. The other PCs hear of it
/// from PlayerNetworkCombat. This PC's own player only. Numbers: Tables/ClassAbilities, Ping.*.
/// Any foe can be pinged (ICombatFoe): a zombie, or a target on the hub's range, where a Schießhund tries its mark out
/// (the user, 2026-11-12: "시순트 표적 찍기도 못 하네").
/// Held, F points (GTA V's way; the user, 2026-11-12: "F 꾹 누르고 있으면 GTA5에서 무슨 키 누르고 있으면 계속
/// 손가락질할 수 있잖아?"): a press that found F with nothing else to do, still held after Poke.csv Point.holdAfter, has the
/// free hand point where the player looks for as long as it is held (PlayerPoint, PointArm), and a fingertip on a friend
/// pokes them (SkinPoke). The tap's ping comes first as always. Anything F would do keeps it: an interaction coming
/// under the crosshair ends the point, as do aiming, a reload, a draw, a shot or a punch, until F is pressed again.
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
    private PlayerMeleeCombat melee;
    private PlayerPoint point;
    private InputAction interact;
    private float nextPing, pressedAt;
    // The press held now began with F free (nothing on screen took it), and nothing has ended its point since.
    private bool pressFree, pointEnded;
    private readonly RaycastHit[] hits = new RaycastHit[32];

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        execution = GetComponent<PlayerExecution>();
        revive = GetComponent<PlayerRevive>();
        weapon = GetComponent<WeaponController>();
        point = GetComponent<PlayerPoint>();
        if (point == null) point = gameObject.AddComponent<PlayerPoint>();
    }

    // A shot or a punch takes the hand back from a point (the other components are all on by now; the fists may come
    // later, Update listens to them then).
    private void Start()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.ShotFired += OnShot;
    }

    private void OnDestroy()
    {
        if (weapon != null) weapon.ShotFired -= OnShot;
        if (melee != null) melee.Punched -= OnPunched;
    }

    private void OnShot(WeaponShot shot) => EndPoint();
    private void OnPunched(float charge) => EndPoint();

    // A held point ends until F is pressed again, and a ping's moment ends at once.
    private void EndPoint()
    {
        if (point == null) return;
        if (point.Pointing) pointEnded = true;
        point.StopFlash();
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal || !movement.InputAvailable || movement.IsDead || movement.IsDowned)
        {
            if (point != null) point.Hold(false);
            return;
        }
        if (interact == null && InputSystem.actions != null) interact = InputSystem.actions.FindAction("Player/Interact", false);
        if (interact == null) return;
        if (melee == null && movement.MeleeCombat != null)
        {
            melee = movement.MeleeCombat;
            melee.Punched += OnPunched;
        }
        bool pressed = interact.WasPressedThisFrame();
        if (pressed)
        {
            pressedAt = Time.time;
            pressFree = !Busy();
            pointEnded = false;
        }
        Hold();
        if (!pressed || Time.time < nextPing || !pressFree) return;
        var foe = Aimed(out Vector3 pinged);
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
        Gesture(foe, pinged);
        Pinged?.Invoke(foe, bounty);
    }

    /// <summary>The living foe under the crosshair now, as F would find it, or null: a Schießhund's duel is called on it (ClassKit).</summary>
    public Component AimedFoe() => Aimed(out _);

    /// <summary>
    /// Marks this foe as F would, its sound and the hand's point (or the gun's nod) with it, the other PCs told: a
    /// Schießhund's duel puts its bounty mark on the foe it calls out (ClassKit), whatever the mark was on.
    /// </summary>
    public void Mark(Component foe, bool bounty)
    {
        if (foe == null || movement == null) return;
        PingMarks.Ping(foe, movement, bounty);
        KitSound.Play(bounty ? KitSound.Cue.Mark : KitSound.Cue.Ping, .7f);
        Gesture(foe, Middle(foe));
        Pinged?.Invoke(foe, bounty);
    }

    // The free hand points at what was pinged for a moment, the hand drawn for the held point (PlayerPoint.Flash; the
    // user, 2026-11-12: "적 핑 찍을 때 가리키는 애니메이션이 있었으면"), aimed down the sights too, as a quick tap of the
    // left hand while the gun stays on the aim. The gun's nod (FPSBody.Point) no longer stands in for a busy hand: it
    // threw the aim off (the user, 2026-11-12: "줌 한 상태로 핑 찍으면 그 총 잠깐 애니메이션 나오는 게 너무 조준을
    // 방해함 … 걍 왼손만 움직이게 해"), and through a reload or a throw the mark and its sound say enough.
    private void Gesture(Component foe, Vector3 at)
    {
        if (point != null) point.Flash(foe != null ? foe.transform : null, at);
    }

    // A foe's middle to point at: its first collider's centre, else a body's height over its feet.
    private static Vector3 Middle(Component foe)
    {
        var body = foe.GetComponentInChildren<Collider>();
        return body != null ? body.bounds.center : foe.transform.position + Vector3.up;
    }

    // Held past Point.holdAfter, a free press points for as long as F stays down and the hand is free (PlayerPoint.CanPoint);
    // whatever F would do coming on screen, or the hand being wanted (aiming, a reload, a shot, a punch), ends the point
    // until F is pressed again.
    private void Hold()
    {
        if (point == null) return;
        bool wants = pressFree && !pointEnded && interact.IsPressed() && Time.time - pressedAt >= PokeTables.HoldAfter;
        if (wants && (!PlayerPoint.CanPoint(movement, weapon) || Busy()))
        {
            if (point.Pointing) pointEnded = true;
            wants = false;
        }
        point.Hold(wants);
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
        // A Support with a friend in reach to resupply, or a barricade under the crosshair (F held takes the tool out).
        if (SupportKit.Local != null && (SupportKit.Local.Offered != null || SupportKit.Local.AimedAt != null || SupportKit.Local.Planning)) return true;
        // An ammo crate under the crosshair: F takes from it (ClassKit).
        if (Barricade.CrateFor(movement) != null) return true;
        if (movement.Abilities != null && movement.Abilities.Fixtures.ClimbDownLadder != null) return true;
        // The hub's stations (its map table, lockers, gun rack, ammo crate, ready button) take F when looked at.
        return CampaignRun.Offering || TowerRun.Offering || ShopMenu.IsOpen || HubLobby.Offering;
    }

    // The living foe the crosshair is on (a zombie or a range target): the nearest thing along the look that isn't this
    // player, if it is one, and where on the look it was met. A wall or the floor in front of it takes the ping instead
    // (nothing happens).
    private Component Aimed(out Vector3 at)
    {
        at = default;
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
        if (!CombatFoe.IsAlive(foe)) return null;
        at = nearest > 0f && !float.IsInfinity(nearest) ? eyes.position + eyes.forward * nearest : Middle(foe);
        return foe;
    }
}
