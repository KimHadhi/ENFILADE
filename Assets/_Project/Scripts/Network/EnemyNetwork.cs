using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// An enemy in a room. The host runs it; on every other PC it is a copy that shows the host's position and health. A
/// player's PC judges their own hits on its copy, shows them at once and tells the host, whose health is the true one.
/// The same goes for its attacks: the victim's PC times the swing and judges the parry, and what that does to the enemy
/// comes back here. Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyNetwork : NetworkBehaviour
{
    /// <summary>How the host's enemy stands, beyond its health.</summary>
    private struct Condition : INetworkSerializeByMemcpy
    {
        public Vector3 Fall;
        public float Posture;
        public bool Staggered, Held, Down;
        /// <summary>A zombie's ZombieMood, for its copies to show (0, awake, for anything else).</summary>
        public byte Mood;
        /// <summary>A shambler holds a player (ZombieGrip), or kneels eating one who lies on the ground.</summary>
        public bool Holding, Feeding;
    }

    private const float ConditionInterval = .1f;
    private readonly NetworkVariable<float> health = new NetworkVariable<float>();
    private readonly NetworkVariable<Condition> condition = new NetworkVariable<Condition>();
    // A zombie's ZombieKind: its copies time their own swing by it.
    private readonly NetworkVariable<byte> kind = new NetworkVariable<byte>();
    private ZombieType type;
    private ZombieGrip grip;
    private EnemyHealth enemy;
    private EnemyCombatAI combat;
    private MeleeCombatTarget target;
    private ZombieMind mind;
    private float pace = 1f, paceLeft;

    /// <summary>This PC only shows the enemy; the host runs it.</summary>
    public bool IsCopy => IsSpawned && !IsServer;
    /// <summary>How fast this enemy's time runs. Slow motion in a room covers it and the player it fights, on every PC.</summary>
    public float Pace => paceLeft > 0f ? pace : 1f;

    /// <summary>Puts a new enemy into the room, if there is one. Call it on the host, right after making the enemy.</summary>
    public static void SpawnInRoom(EnemyHealth enemy)
    {
        if (GameSession.IsOnline && enemy.TryGetComponent(out NetworkObject network)) network.Spawn(destroyWithScene: true);
    }

    public override void OnNetworkSpawn()
    {
        enemy = GetComponent<EnemyHealth>();
        combat = GetComponent<EnemyCombatAI>();
        target = GetComponent<MeleeCombatTarget>();
        mind = GetComponent<ZombieMind>();
        type = GetComponent<ZombieType>();
        grip = GetComponent<ZombieGrip>();
        condition.SetUpdateTraits(new NetworkVariableUpdateTraits { MinSecondsBetweenUpdates = ConditionInterval });
        if (IsServer)
        {
            health.Value = enemy.Health;
            condition.Value = ReadCondition();
            if (type != null) kind.Value = (byte)type.Kind;
            return;
        }
        if (type != null) type.Show((ZombieKind)kind.Value);
        kind.OnValueChanged += OnKindChanged;
        if (TryGetComponent(out EnemyChaseAI chase)) chase.enabled = false;
        if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
        health.OnValueChanged += OnHealthChanged;
        condition.OnValueChanged += OnConditionChanged;
        enemy.ShowHealth(health.Value);
        Show(condition.Value);
    }

    public override void OnNetworkDespawn()
    {
        health.OnValueChanged -= OnHealthChanged;
        condition.OnValueChanged -= OnConditionChanged;
        kind.OnValueChanged -= OnKindChanged;
    }

    private void OnKindChanged(byte previous, byte current)
    {
        if (type != null) type.Show((ZombieKind)current);
    }

    /// <summary>The host made its zombie this kind after it appeared (/spawn n runner): its copies follow.</summary>
    internal void ShareKind(ZombieKind value)
    {
        if (IsSpawned && IsServer) kind.Value = (byte)value;
    }

    private void Update()
    {
        paceLeft = Mathf.Max(0f, paceLeft - Time.unscaledDeltaTime);
        if (!IsSpawned || !IsServer) return;
        health.Value = enemy.Health;
        condition.Value = ReadCondition();
    }

    // Whole points of posture, so recovering doesn't send a report every few frames.
    private Condition ReadCondition() => new Condition
    {
        Fall = target.FallDirection, Posture = Mathf.Round(target.Posture), Staggered = target.IsStaggered, Held = target.IsHeld,
        // Lying, not getting up: a zombie's copies start getting up when the host's does (ZombieBody).
        Down = target.IsLying,
        Mood = mind != null ? (byte)mind.Mood : (byte)0,
        Holding = grip != null && grip.IsHolding,
        Feeding = grip != null && grip.Feeding
    };

    private void OnHealthChanged(float previous, float current) => enemy.ShowHealth(current);
    private void OnConditionChanged(Condition previous, Condition current) => Show(current);
    private void Show(in Condition now)
    {
        target.ShowCondition(now.Posture, now.Staggered, now.Held, now.Down, now.Fall);
        if (mind != null) mind.ShowMood(now.Mood);
        if (grip != null) grip.ShowHolding(now.Holding, now.Feeding);
    }

    /// <summary>This PC's player hit its copy: the host applies it for real.</summary>
    internal void SendHit(in DamageHit hit) => HitRpc(new EnemyHitMessage(hit));

    /// <summary>The host took a hit: everyone but the one who landed it sees it now. They saw it when they fired.</summary>
    internal void AnnounceHit(in DamageHit hit, float before, float after)
    {
        if (!IsSpawned) return;
        ulong attacker = hit.Source != null && hit.Source.TryGetComponent(out NetworkObject player) ? player.OwnerClientId : NetworkManager.ServerClientId;
        ShowHitRpc(new EnemyHitMessage(hit), attacker, before, after);
    }

    /// <summary>A kill this PC judged: the corpse is out, and the host removes the enemy a moment later.</summary>
    internal void HideUntilRemoved()
    {
        foreach (var part in GetComponentsInChildren<Renderer>()) part.enabled = false;
        foreach (var part in GetComponentsInChildren<Collider>()) part.enabled = false;
    }

    /// <summary>The host's enemy began an attack on this player.</summary>
    internal void AnnounceWindup(PlayerMovement victim, int combo, AttackMove move)
    {
        if (!IsSpawned) return;
        ulong client = victim.TryGetComponent(out NetworkObject player) ? player.OwnerClientId : NetworkManager.ServerClientId;
        WindupRpc(client, combo, (byte)move);
    }

    internal void AnnounceCancel()
    {
        if (IsSpawned && IsServer) CancelRpc();
    }

    /// <summary>This PC's player parried this enemy: everyone else sees it happen.</summary>
    internal void ShareParry(Vector3 contact, Vector3 away, bool broken)
    {
        if (IsSpawned) ParryRpc(contact, away, broken);
    }

    /// <summary>This PC's player blocked this enemy: everyone else hears it.</summary>
    internal void ShareBlock()
    {
        if (IsSpawned) BlockRpc();
    }

    /// <summary>Slows this enemy's time on every PC, or ends that with a scale of 1.</summary>
    internal void ShareSlow(float scale, float seconds)
    {
        if (IsSpawned) SlowRpc(scale, seconds);
    }

    /// <summary>A parry, a punch or a shove on this PC's copy reaches the enemy that really moves and fights.</summary>
    internal void SendPosture(float amount, float stun, bool interrupt) => PostureRpc(amount, stun, interrupt);
    internal void SendInterrupt(float duration) => InterruptRpc(duration);
    internal void SendShove(Vector3 impulse, Vector3 point) => ShoveRpc(impulse, point);
    internal void SendKnockDown(Vector3 direction, float duration) => KnockDownRpc(direction, duration);
    internal void SendBarge(Vector3 velocity, float throwTime, float knockdownTime) => BargeRpc(velocity, throwTime, knockdownTime);

    /// <summary>This PC's player started an execution on its copy: the host holds the enemy for them, if nobody else has it.</summary>
    internal void AskExecution() => ExecutionRpc();
    internal void EndExecution() => ExecutionEndedRpc();

    [Rpc(SendTo.Everyone)]
    private void SlowRpc(float scale, float seconds)
    {
        pace = scale;
        paceLeft = seconds;
    }

    [Rpc(SendTo.NotServer)]
    private void WindupRpc(ulong victim, int combo, byte move) => combat.ShowWindup(victim == NetworkManager.LocalClientId, combo, (AttackMove)move);

    [Rpc(SendTo.NotServer)]
    private void CancelRpc() => combat.CancelAttack();

    [Rpc(SendTo.NotMe)]
    private void BlockRpc() => CombatCueAudio.Play(gameObject, CombatCue.Block, 1f, true);

    [Rpc(SendTo.NotMe)]
    private void ParryRpc(Vector3 contact, Vector3 away, bool broken)
    {
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out ParryFeedback feedback)) feedback.ShowOtherParry(target, contact, away, broken);
    }

    [Rpc(SendTo.Server)]
    private void PostureRpc(float amount, float stun, bool interrupt) => target.ApplyPostureDamage(amount, stun, interrupt);

    [Rpc(SendTo.Server)]
    private void InterruptRpc(float duration) => target.Interrupt(duration);

    [Rpc(SendTo.Server)]
    private void ShoveRpc(Vector3 impulse, Vector3 point) => target.ReceiveShove(impulse, point);

    [Rpc(SendTo.Server)]
    private void KnockDownRpc(Vector3 direction, float duration) => target.KnockDown(direction, duration);

    [Rpc(SendTo.Server)]
    private void BargeRpc(Vector3 velocity, float throwTime, float knockdownTime) => target.Barge(velocity, throwTime, knockdownTime);

    [Rpc(SendTo.Server)]
    private void ExecutionRpc(RpcParams rpc = default)
    {
        ulong client = rpc.Receive.SenderClientId;
        var player = PlayerOf(client);
        var execution = player != null ? player.GetComponent<PlayerExecution>() : null;
        if (execution != null && target.TryReserveExecution(execution)) execution.ShowExecuting(target);
        else ExecutionDeniedRpc(client);
    }

    [Rpc(SendTo.Server)]
    private void ExecutionEndedRpc(RpcParams rpc = default)
    {
        var player = PlayerOf(rpc.Receive.SenderClientId);
        if (player != null && player.TryGetComponent(out PlayerExecution execution)) execution.Cancel();
    }

    [Rpc(SendTo.NotServer)]
    private void ExecutionDeniedRpc(ulong client)
    {
        if (client == NetworkManager.LocalClientId) target.DenyExecution();
    }

    [Rpc(SendTo.Server)]
    private void HitRpc(EnemyHitMessage message, RpcParams rpc = default) => enemy.ReceiveHit(message.ToHit(PlayerOf(rpc.Receive.SenderClientId)));

    [Rpc(SendTo.NotServer)]
    private void ShowHitRpc(EnemyHitMessage message, ulong attacker, float before, float after)
    {
        if (attacker == NetworkManager.LocalClientId) return;
        enemy.ShowHit(message.ToHit(PlayerOf(attacker)), before, after);
    }

    private static GameObject PlayerOf(ulong client)
    {
        foreach (var player in PlayerRoster.All)
            if (player.TryGetComponent(out NetworkObject network) && network.OwnerClientId == client) return player.gameObject;
        return null;
    }
}
