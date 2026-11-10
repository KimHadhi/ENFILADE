using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// How a zombie holds a player (ZombieGrip, PlayerHold): a shambler's grab, standing; kneeling to eat someone lying on
/// the ground; or a runner's pin (G&amp;B-2), knelt over them at their neck.
/// </summary>
public enum HoldKind : byte { Grab, Feed, Pin }

/// <summary>
/// Stage G&B-1 (Guts &amp; Blackpowder's wrestle): a shambler holding a player. The held player's own PC decides the hold
/// (PlayerHold: a grab landed, then a blow, a stun, death or time lets it go) and says so; this side plays it out.
/// Where the enemy really runs (the host, or the PC alone) it steps in close, faces its catch and stands there, its
/// chase and swing waiting (EnemyChaseAI, EnemyCombatAI), and lets go by itself should word never come. Every other PC
/// shows it from the host (EnemyNetwork's Condition.Holding), the held player's PC at once. Its capsule and the held
/// player's pass through each other meanwhile, so it can hold on that close. A runner's pin (G&amp;B-2) kneels over the
/// player it knocked flat, its mouth at their chest and neck, which lie on the far side of their feet from where it came.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieGrip : MonoBehaviour
{
    // Centre to centre while holding on (m), how fast it closes in (m/s) and turns (degrees a second), and the longest
    // the host lets it hold without word from the held player's PC (s).
    // Kneeling over someone lying down to eat them it comes closer still.
    private const float HoldDistance = .7f, FeedDistance = .55f, StepSpeed = 3f, TurnSpeed = 540f, LongestHold = 7f;
    // Pinning, from where it kneels to their chest (m), and how far their chest lies past their feet (m); and the longest
    // a pin holds without word (s): the bite kills well before.
    private const float PinDistance = .5f, PinChest = .35f, LongestPin = 12f;

    private static readonly List<ZombieGrip> holding = new List<ZombieGrip>();

    private EnemyNetwork network;
    private NavMeshAgent agent;
    private EnemyHealth health;
    private MeleeCombatTarget melee;
    private PlayerMovement held;
    private readonly List<Collider> body = new List<Collider>();
    private Collider heldCapsule;
    private float since;
    private bool shown, shownFeeding, feeding, pinning;
    private Vector3 pinWay;

    /// <summary>It holds a player: thought where it runs or by the held player's PC, shown everywhere else.</summary>
    public bool IsHolding => held != null || shown;
    /// <summary>The player it holds, where that is known (the host, alone, or the held player's PC).</summary>
    public PlayerMovement Held => held;
    /// <summary>It kneels over a player lying on the ground, eating them (ZombieBody shows the feeding).</summary>
    public bool Feeding => held != null ? feeding : shown && shownFeeding;
    /// <summary>A runner pins the player it holds (where that is known).</summary>
    public bool Pinning => held != null && pinning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatics() => holding.Clear();

    private void Awake()
    {
        network = GetComponent<EnemyNetwork>();
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<EnemyHealth>();
        melee = GetComponent<MeleeCombatTarget>();
    }

    private bool Runs => network == null || !network.IsCopy;

    /// <summary>It now holds this player (the held player's PC says so; on the host from their word).</summary>
    internal void Hold(PlayerMovement player, HoldKind kind = HoldKind.Grab)
    {
        if (player == null) return;
        if (held == player)
        {
            feeding |= kind != HoldKind.Grab;
            if (kind == HoldKind.Pin && !pinning) { pinning = true; since = Time.time; pinWay = WayTo(player); }
            return;
        }
        if (held != null) LetGo();
        held = player;
        feeding = kind != HoldKind.Grab;
        pinning = kind == HoldKind.Pin;
        pinWay = WayTo(player);
        since = Time.time;
        if (!holding.Contains(this)) holding.Add(this);
        HoldReveal.Watch(this);
        PassThrough(true);
        if (Runs && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
    }

    /// <summary>It lets go.</summary>
    internal void LetGo()
    {
        if (held == null) return;
        PassThrough(false);
        held = null;
        feeding = pinning = false;
        holding.Remove(this);
        if (Runs && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
    }

    /// <summary>Another PC: whether the host's zombie holds someone (EnemyNetwork).</summary>
    internal void ShowHolding(bool value, bool eat)
    {
        shown = value && !Runs;
        shownFeeding = eat;
        // Seen holding someone on this PC too: it stands out through walls while it does (HoldReveal).
        if (shown) HoldReveal.Watch(this);
    }

    /// <summary>The shamblers holding this player now, on this PC.</summary>
    internal static int CountHolding(PlayerMovement player)
    {
        int count = 0;
        foreach (var grip in holding) if (grip != null && grip.held == player) count++;
        return count;
    }

    /// <summary>The runners pinning this player now, on this PC (the host knows them all).</summary>
    internal static int CountPinning(PlayerMovement player)
    {
        int count = 0;
        foreach (var grip in holding) if (grip != null && grip.held == player && grip.pinning) count++;
        return count;
    }

    // Flat, from this zombie to the player; on top of them (a leap lands where they stood), the way it leapt.
    private Vector3 WayTo(PlayerMovement player)
    {
        Vector3 way = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
        return way.sqrMagnitude > .25f ? way.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
    }

    private void Update()
    {
        if (held == null) return;
        if (health == null || !health.IsAlive || held == null || !held.IsInitialized) { LetGo(); return; }
        if (!Runs) return;
        // Word that it let go never came (the held player left, or the message was lost).
        if (Time.time - since > (pinning ? LongestPin : LongestHold)) { LetGo(); return; }
        // Knocked down, its body lies where it fell until word that it let go comes.
        if (melee != null && melee.IsLying) return;
        // Pinning, it goes for their chest, which lies past their feet the way it came at them.
        Vector3 at = held.transform.position + (pinning ? pinWay * PinChest : Vector3.zero);
        Vector3 toward = Vector3.ProjectOnPlane(at - transform.position, Vector3.up);
        float distance = toward.magnitude;
        if (distance < .01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), TurnSpeed * Time.deltaTime);
        float step = Mathf.Min(StepSpeed * Time.deltaTime, distance - (pinning ? PinDistance : feeding ? FeedDistance : HoldDistance));
        if (step <= 0f) return;
        Vector3 move = toward / distance * step;
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.Move(move);
        else transform.position += move;
    }

    // Held this close, the shambler and its catch pass through each other's capsules.
    private void PassThrough(bool through)
    {
        if (through)
        {
            body.Clear();
            foreach (var collider in GetComponents<Collider>()) if (!collider.isTrigger) body.Add(collider);
            heldCapsule = held != null ? held.GetComponent<CharacterController>() : null;
        }
        if (heldCapsule == null) return;
        foreach (var collider in body) if (collider != null) Physics.IgnoreCollision(collider, heldCapsule, through);
        if (!through) heldCapsule = null;
    }

    private void OnDisable() => LetGo();
}
