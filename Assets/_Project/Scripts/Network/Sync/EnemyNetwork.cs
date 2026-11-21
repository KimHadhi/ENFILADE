using Unity.Netcode;
using Unity.Netcode.Components;
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
        /// <summary>
        /// How sure a zombie is that someone is there, in twentieths, and of whom (their client id + 1, 0 for nobody):
        /// every PC's stealth HUD shows its own player the zombies noticing them (StealthMarkersHUD, StealthView).
        /// </summary>
        public byte Suspicion;
        public ulong Watch;
        /// <summary>A zombie draws breath to call the others (ZombieMind.Calling): every PC shows and hears the same tell.</summary>
        public bool Calling;
        /// <summary>
        /// Lying knocked down on a doll a guest's PC runs (their player knocked it down there, ZombieBody.FallHere): that
        /// client's id + 1, so every copy follows that PC's fall and no other. 0: the host runs it, or it isn't down.
        /// </summary>
        public ulong Runner;
    }

    private const float ConditionInterval = .1f;
    private readonly NetworkVariable<float> health = new NetworkVariable<float>();
    private readonly NetworkVariable<Condition> condition = new NetworkVariable<Condition>();
    // A zombie's ZombieKind: its copies time their own swing by it.
    private readonly NetworkVariable<byte> kind = new NetworkVariable<byte>();
    // Its health on top of its kind's (ZombieType.Toughness, the tower's floors): a copy's health bar fills to the same maximum.
    private readonly NetworkVariable<float> toughness = new NetworkVariable<float>(1f);
    // Its EnemyRank (a tower champion, a song's brute): a copy must know an elite for what it is, because what a class
    // does to one (a Ghost's stagger, a Schießhund's bounty, the kill's marks) is judged on each player's own PC.
    private readonly NetworkVariable<byte> rank = new NetworkVariable<byte>();
    // The calm pose the host gave it (ZombieBody.ForcedPose) plus one, 0 for its own seeded pick, Standing for one that
    // takes none (a walker on its round, a cage's crowd): it holds the same pose, or none, on every PC.
    private readonly NetworkVariable<byte> pose = new NetworkVariable<byte>();
    private const byte Standing = byte.MaxValue;
    private ZombieType type;
    private ZombieGrip grip;
    private EnemyHealth enemy;
    private EnemyCombatAI combat;
    private MeleeCombatTarget target;
    private ZombieMind mind;
    private ZombieBody body;
    private float pace = 1f, paceLeft;
    // The host's send gate on its position (LateUpdate): the transform's own thresholds, whether it may go out on the next
    // tick, when it may next, and the tick it last saw.
    private const float Shut = 1e9f;
    private NetworkTransform motion;
    private float positionThreshold, rotationThreshold, scaleThreshold, nextSend;
    private int lastTick;
    // Open: its position may go out on the next tick. gateOpen: what the transform's thresholds were last set for.
    private bool open = true, gateOpen = true;

    /// <summary>This PC only shows the enemy; the host runs it.</summary>
    public bool IsCopy => IsSpawned && !IsServer;

    // PredictionHold as last worked out, and the frame it was (one answer serves every copy that frame).
    private static float hold = .3f;
    private static int holdFrame = -1;
    // The switches below as last read, and the frame they were.
    private static bool knockdownAtOnce = true, slideAtOnce = true;
    private static int switchFrame = -1;

    // The round trip to the host as the transport measures it (s); 0 on the host, or before there is a reading.
    private static float RoundTrip(NetworkManager manager)
    {
        if (manager == null || !manager.IsListening || manager.IsServer) return 0f;
        return manager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) * .001f;
    }

    private static void ReadSwitches()
    {
        if (switchFrame == Time.frameCount) return;
        switchFrame = Time.frameCount;
        knockdownAtOnce = NetSmoothing.Number("knockdownAtOnce", 1f) > .5f;
        slideAtOnce = NetSmoothing.Number("slideAtOnce", 1f) > .5f;
    }

    /// <summary>
    /// A zombie this PC's own player shoves or throws with a barge slides on this PC at once (SlideHere), where it used
    /// to stand until the host's zombie's slide came back a round trip later. Network.csv slideAtOnce: 0 puts the old
    /// way back.
    /// </summary>
    internal static bool SlideAtOnce { get { ReadSwitches(); return slideAtOnce; } }

    /// <summary>
    /// A zombie this PC's own player knocks down falls on this PC at once, on a doll it runs itself, where it used to
    /// wait for the host's doll to come back a round trip later (ZombieBody.FallHere). Network.csv knockdownAtOnce: 0
    /// puts the old way back, should the new one misbehave.
    /// </summary>
    internal static bool KnockdownAtOnce { get { ReadSwitches(); return knockdownAtOnce; } }

    /// <summary>
    /// How long a copy trusts what this PC's own player just did to it over what the host reports (s): a report the host
    /// sent before it heard of that is still on its way for about a round trip, and taking it would undo what the player
    /// just saw (health coming back between two shots, a broken posture mended). It is the time this PC's word takes to
    /// reach the host and the host's to come back (the round trip the transport measures), plus how seldom a report
    /// goes out (predictMargin). It was a fixed 0.3 s for posture and nothing for health: fine on a LAN, short on a slow
    /// link. Network.csv predictLeast, predictMargin, predictMost.
    /// </summary>
    internal float PredictionHold
    {
        get
        {
            if (holdFrame == Time.frameCount) return hold;
            holdFrame = Time.frameCount;
            float least = NetSmoothing.Number("predictLeast", .3f), most = NetSmoothing.Number("predictMost", 1f);
            hold = Mathf.Clamp(RoundTrip(NetworkManager) + NetSmoothing.Number("predictMargin", .15f), least, Mathf.Max(least, most));
            return hold;
        }
    }
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
        body = GetComponent<ZombieBody>();
        type = GetComponent<ZombieType>();
        grip = GetComponent<ZombieGrip>();
        // What cards do to zombies that isn't about one zombie rides a message this PC must be listening for.
        ZombieEffects.Listen();
        condition.SetUpdateTraits(new NetworkVariableUpdateTraits { MinSecondsBetweenUpdates = ConditionInterval });
        if (IsServer)
        {
            health.Value = enemy.Health;
            condition.Value = ReadCondition();
            if (type != null) kind.Value = (byte)type.Kind;
            if (type != null) toughness.Value = type.Toughness;
            rank.Value = (byte)enemy.Rank;
            if (body != null && body.ForcedPose >= 0 && body.ForcedPose < Standing - 1) pose.Value = (byte)(body.ForcedPose + 1);
            else if (body != null && body.ForcedPose == ZombieBody.NoPose) pose.Value = Standing;
            motion = GetComponent<NetworkTransform>();
            if (motion != null)
            {
                positionThreshold = motion.PositionThreshold;
                rotationThreshold = motion.RotAngleThreshold;
                scaleThreshold = motion.ScaleThreshold;
            }
            return;
        }
        if (type != null) type.Show((ZombieKind)kind.Value);
        if (type != null) type.ShowToughness(toughness.Value);
        enemy.ShowRank((EnemyRank)rank.Value);
        rank.OnValueChanged += OnRankChanged;
        // Before its body dresses (ZombieBody.Start), so it takes the host's pose from its first frame.
        int given = pose.Value;
        if (given == Standing && body != null) body.ForcedPose = ZombieBody.NoPose;
        else if (given > 0 && body != null) body.ForcedPose = given - 1;
        kind.OnValueChanged += OnKindChanged;
        toughness.OnValueChanged += OnToughnessChanged;
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
        toughness.OnValueChanged -= OnToughnessChanged;
        rank.OnValueChanged -= OnRankChanged;
    }

    private void OnKindChanged(byte previous, byte current)
    {
        if (type != null) type.Show((ZombieKind)current);
    }

    private void OnRankChanged(byte previous, byte current)
    {
        if (enemy != null) enemy.ShowRank((EnemyRank)current);
    }

    /// <summary>The host ranked its enemy after it appeared (/spawn n elite): its copies follow.</summary>
    internal void ShareRank(EnemyRank value)
    {
        if (IsSpawned && IsServer) rank.Value = (byte)value;
    }

    private void OnToughnessChanged(float previous, float current)
    {
        if (type != null) type.ShowToughness(current);
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
        long began = ZombieCost.Begin();
        health.Value = enemy.Health;
        condition.Value = ReadCondition();
        ZombieCost.End(ZombieCost.Part.Network, began);
    }

    // The host sends a zombie's position only so often, by how near a player it is (NetSmoothing.ZombieRate: 60 a second
    // close, 20 far off), not on all 120 ticks as a player's: far fewer bytes up from the host with a crowd about, and
    // the copies' buffer covers the gap (user, 2026-11-11: "좀비만 초당 30~60번으로 낮춰서 데이터를 아끼고"). Netcode has
    // no rate per transform, so between its turns the transform's thresholds stand past any move, and it sends what
    // gathered at its turn. Its tick runs at the start of a frame, before this decides the next one.
    private void LateUpdate()
    {
        // A copy: the slide this PC's own player just gave it, shown ahead of the host's (Slide).
        if (IsSpawned && !IsServer) { Slide(); return; }
        if (motion == null || !IsSpawned || !IsServer) return;
        long began = ZombieCost.Begin();
        int tick = NetworkManager.ServerTime.Tick;
        bool ticked = tick != lastTick;
        lastTick = tick;
        float now = Time.unscaledTime;
        if (open && ticked)
        {
            open = false;
            nextSend = now + 1f / NetSmoothing.ZombieRate(NetworkManager, transform.position);
            ZombieCost.Count(ZombieCost.Tally.Sends);
        }
        if (!open && now >= nextSend) open = true;
        // Only as the gate opens or shuts (they were set every frame for every zombie).
        if (open != gateOpen)
        {
            gateOpen = open;
            motion.PositionThreshold = open ? positionThreshold : Shut;
            motion.RotAngleThreshold = open ? rotationThreshold : Shut;
            motion.ScaleThreshold = open ? scaleThreshold : Shut;
        }
        ZombieCost.End(ZombieCost.Part.Network, began);
    }

    // ---- A slide shown at once (a copy) --------------------------------------------------------------------------
    // This PC's own player shoved its copy, or threw it with a barge. The zombie that really slides is the host's, and its
    // place comes back here a round trip later (and then a little more: copies are drawn a few ticks behind), so the
    // copy used to flinch at once and only then slide (the user's rule of 2026-11-14: what a player does shows on their
    // own screen at once). So the copy slides here at once, by the same rule the host's will (MeleeCombatTarget's
    // shove: the speed falling away evenly over its time, stopped at the walk mesh's edge), laid on top of the place the
    // host reports. As the host's own slide then arrives in those reports, the same amount is taken back out, this PC's
    // slide as it was slideLag ago, so the two never add up and the copy ends exactly where the host's zombie does. If
    // the host's slid less far (something stood in its way there), the copy eases back by the difference.

    // How much of the slide's past is kept, to take the host's arriving slide back out (marks, and s between them).
    private const int TrailSize = 128;
    private const float TrailStep = 1f / 60f;
    private Vector3[] slideTrail;
    private NavMeshAgent walker;
    private Vector3 slideVelocity, slid;
    // The slide under way: its whole time and what is left of it; since this PC began sliding it (s; below 0: it isn't);
    // when the last slide given ends on that clock; and how long the host's own slide takes to show here (s).
    private float slideFor, slideLeft, slideAge = -1f, slideEnds, slideLag;
    private int trailCount;

    /// <summary>
    /// On a copy: this PC's own player sent it sliding at this velocity, slowing to a stop over this long (a shove, a
    /// barge's throw). It slides here at once; the host is told apart (SendShove, SendBarge) and its zombie really moves.
    /// </summary>
    internal void SlideHere(Vector3 velocity, float seconds)
    {
        if (!IsCopy || seconds <= 0f || !SlideAtOnce) return;
        Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) return;
        if (slideTrail == null) slideTrail = new Vector3[TrailSize];
        // A slide given while the last still shows carries on from it: one unbroken run for the trail.
        if (slideAge < 0f)
        {
            slideAge = 0f;
            slid = Vector3.zero;
            slideTrail[0] = Vector3.zero;
            trailCount = 1;
        }
        slideVelocity = flat;
        slideFor = slideLeft = seconds;
        slideEnds = slideAge + seconds;
        slideLag = SlideLag();
    }

    // How long after this PC tells the host of a slide the host's zombie is seen sliding here: the round trip, Netcode's
    // own wait on the host's clock, the ticks a copy is drawn behind, and a frame or so at either end (slideLagExtra).
    private float SlideLag()
    {
        var manager = NetworkManager;
        if (manager == null || !manager.IsListening) return 0f;
        float tick = 1f / Mathf.Max(1, (int)manager.NetworkConfig.TickRate);
        float behind = Mathf.Max(1, manager.NetworkTimeSystem.TickLatency + NetworkTransform.InterpolationBufferTickOffset) * tick;
        return RoundTrip(manager) + (float)manager.NetworkTimeSystem.ServerBufferSec + behind + NetSmoothing.Number("slideLagExtra", .02f);
    }

    // After Netcode has put the copy where the host reports it (before LateUpdate, every frame, so nothing adds up).
    private void Slide()
    {
        if (slideAge < 0f) return;
        float dt = Time.deltaTime * Pace;
        if (slideLeft > 0f && dt > 0f)
        {
            // As the host's shove moves (MeleeCombatTarget.Tick): the speed falls away evenly to nothing.
            float slice = Mathf.Min(dt, slideLeft);
            float strength = (slideLeft + slideLeft - slice) / (2f * slideFor);
            slideLeft -= slice;
            slid += Clear(slideVelocity * (slice * strength), transform.position + slid - SlidAt(slideAge - slideLag));
        }
        slideAge += dt;
        for (int upTo = Mathf.FloorToInt(slideAge / TrailStep); trailCount <= upTo; trailCount++) slideTrail[trailCount % TrailSize] = slid;
        // The host's own slide has shown in full: the copy is where the host says, and stays there.
        if (slideAge >= slideEnds + slideLag + TrailStep) { slideAge = -1f; return; }
        transform.position += slid - SlidAt(slideAge - slideLag);
    }

    // How far this PC had slid it this long after it began (the trail, eased between its marks).
    private Vector3 SlidAt(float age)
    {
        if (age <= 0f || trailCount == 0) return Vector3.zero;
        float at = age / TrailStep;
        int index = Mathf.FloorToInt(at);
        if (index >= trailCount - 1) return slid;
        int oldest = Mathf.Max(0, trailCount - TrailSize);
        if (index < oldest) return slideTrail[oldest % TrailSize];
        return Vector3.Lerp(slideTrail[index % TrailSize], slideTrail[(index + 1) % TrailSize], at - index);
    }

    // No further than the walk mesh goes that way from where the copy is shown: the host's slide stops at its edge too.
    private Vector3 Clear(Vector3 step, Vector3 from)
    {
        if (walker == null) TryGetComponent(out walker);
        // The agent is off on a copy, but its numbers stand: it floats this far above the mesh.
        if (walker != null) from -= Vector3.up * walker.baseOffset;
        if (!NavMesh.SamplePosition(from, out var on, .6f, NavMesh.AllAreas)) return step;
        if (!NavMesh.Raycast(on.position, on.position + step, out var edge, NavMesh.AllAreas)) return step;
        float length = step.magnitude;
        if (length < 1e-6f) return step;
        return step * (Mathf.Min(length, Mathf.Max(0f, Vector3.Distance(on.position, edge.position) - .025f)) / length);
    }

    // Whole points of posture, so recovering doesn't send a report every few frames.
    private Condition ReadCondition() => new Condition
    {
        Fall = target.FallDirection, Posture = Mathf.Round(target.Posture), Staggered = target.IsStaggered, Held = target.IsHeld,
        // Lying, not getting up: a zombie's copies start getting up when the host's does (ZombieBody).
        Down = target.IsLying,
        Mood = mind != null ? (byte)mind.Mood : (byte)0,
        Holding = grip != null && grip.IsHolding,
        Feeding = grip != null && grip.Feeding,
        Suspicion = mind != null ? mind.SuspicionStep : (byte)0,
        Watch = mind != null ? WatchOf(mind.Watching) : 0UL,
        Calling = mind != null && mind.CallingShared,
        Runner = body != null ? body.RunnerShared : 0UL
    };

    // A player as their client id + 1 (0: nobody).
    private static ulong WatchOf(PlayerMovement player)
        => player != null && player.TryGetComponent(out NetworkObject owner) && owner.IsSpawned ? owner.OwnerClientId + 1 : 0UL;

    private void OnHealthChanged(float previous, float current) => enemy.ShowHealth(current);
    private void OnConditionChanged(Condition previous, Condition current) => Show(current);
    private void Show(in Condition now)
    {
        target.ShowCondition(now.Posture, now.Staggered, now.Held, now.Down, now.Fall);
        // With the word that it lies comes whose doll it lies on (the same report, so the two never disagree).
        if (body != null) body.ShowRunner(now.Runner);
        if (mind != null) mind.ShowMind(now.Mood, now.Suspicion, now.Watch, now.Calling);
        if (grip != null) grip.ShowHolding(now.Holding, now.Feeding);
    }

    /// <summary>
    /// This PC's player hit its copy, and this is what the hit did here: the health it took and whether it killed. The
    /// host applies exactly that (EnemyHealth.ReceiveSent).
    /// </summary>
    internal void SendHit(in DamageHit hit, float taken, bool killed) => HitRpc(new EnemyHitMessage(hit, NetworkObjectId, transform, taken, killed));

    /// <summary>The host took a hit: everyone but the one who landed it sees it now. They saw it when they fired.</summary>
    internal void AnnounceHit(in DamageHit hit, float before, float after)
    {
        if (!IsSpawned) return;
        ulong attacker = hit.Source != null && hit.Source.TryGetComponent(out NetworkObject player) ? player.OwnerClientId : NetworkManager.ServerClientId;
        ShowHitRpc(new EnemyHitMessage(hit, NetworkObjectId, transform), attacker, before, after);
    }

    /// <summary>A kill this PC judged: the corpse is out, and the host removes the enemy a moment later.</summary>
    internal void HideUntilRemoved()
    {
        foreach (var part in GetComponentsInChildren<Renderer>()) part.enabled = false;
        foreach (var part in GetComponentsInChildren<Collider>()) part.enabled = false;
    }

    /// <summary>The host's enemy began an attack on this player (null: on nobody's, a Support's barricade: ZombieBreach).</summary>
    internal void AnnounceWindup(PlayerMovement victim, int combo, AttackMove move, uint seed)
    {
        if (!IsSpawned) return;
        // No client has this id, so every copy shows the windup and none judges a blow of it on its player.
        ulong client = victim == null ? ulong.MaxValue
            : victim.TryGetComponent(out NetworkObject player) ? player.OwnerClientId : NetworkManager.ServerClientId;
        WindupRpc(client, combo, (byte)move, seed);
    }

    /// <summary>The host's zombie called the others: every PC hears its roar (ZombieMind.ShowCall).</summary>
    internal void ShareCall()
    {
        if (IsSpawned && IsServer) CallRpc();
    }

    /// <summary>
    /// The host's zombie, lying or sitting in its pose, turned its head to look at this point a while (a stone, a knock, a
    /// faint step): every PC's copy turns its head the same way (ZombieMind.ShowGaze, ZombieBody.Gaze), and its cone with it.
    /// </summary>
    internal void ShareGaze(Vector3 at, float seconds)
    {
        if (IsSpawned && IsServer) GazeRpc(at, seconds);
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
    internal void SendPosture(float amount, float stun, bool interrupt, bool broke = false) => PostureRpc(amount, stun, interrupt, broke);
    internal void SendInterrupt(float duration) => InterruptRpc(duration);
    /// <summary>On the host: it flinched, by that client's player; every other copy shows it.</summary>
    internal void ShareInterrupt(float duration, ulong by) => ShowInterruptRpc(duration, by);
    internal void SendShove(Vector3 impulse, Vector3 point) => ShoveRpc(impulse, point);
    /// <summary>
    /// This PC's player knocked its copy down. Runs: it fell here at once and this PC runs its doll (ZombieBody.FallHere),
    /// so the host's zombie follows this PC's fall; otherwise the host runs it as before.
    /// </summary>
    internal void SendKnockDown(Vector3 direction, float duration, bool runs = false) => KnockDownRpc(direction, duration, runs);
    /// <summary>
    /// This PC's player threw its copy with a barge. Runs: the copy slides here at once and, where the slide ends, falls
    /// on a doll this PC runs (MeleeCombatTarget.BargeHere), so the host's zombie follows this PC's fall at the end of
    /// its own slide.
    /// </summary>
    internal void SendBarge(Vector3 velocity, float throwTime, float knockdownTime, bool runs = false)
        => BargeRpc(velocity, throwTime, knockdownTime, runs);

    // ---- What cards do to it (ZombieEffects) -----------------------------------------------------------------
    // The last suppression this PC sent for it: when the next may go out (game time), and how strong it was.
    private float suppressNext;
    private float suppressWalk, suppressWindup;

    /// <summary>
    /// Any PC: its player's fire pins this zombie down (ZombieEffects.Suppress). Every PC hears it, this one at once.
    /// Rounds call this many times a second, so the word goes out again only once a good part of the last one's time
    /// has run, or when it is stronger than the last.
    /// </summary>
    internal void ShareSuppress(float seconds, float walkSlow, float windupSlow)
    {
        if (!IsSpawned) return;
        if (Time.time < suppressNext && walkSlow <= suppressWalk && windupSlow <= suppressWindup) return;
        suppressNext = Time.time + seconds * .35f;
        suppressWalk = walkSlow;
        suppressWindup = windupSlow;
        SuppressRpc(seconds, walkSlow, windupSlow);
    }

    [Rpc(SendTo.Everyone)]
    private void SuppressRpc(float seconds, float walkSlow, float windupSlow) => ZombieEffects.Suppressed(enemy, seconds, walkSlow, windupSlow);

    /// <summary>Any PC: this zombie is to be put there at once (ZombieEffects.Move). The host moves it.</summary>
    internal void ShareMove(Vector3 position, Quaternion rotation)
    {
        if (IsSpawned) MoveRpc(position, rotation);
    }

    [Rpc(SendTo.Server)]
    private void MoveRpc(Vector3 position, Quaternion rotation) => ZombieEffects.Moved(enemy, position, rotation);

    /// <summary>
    /// The host put its zombie somewhere else at once (ZombieEffects.Moved): its copies jump there with it. Without
    /// this they would glide across the map to it, as they follow every other move.
    /// </summary>
    internal void Jump()
    {
        if (IsSpawned && IsServer && motion != null) motion.Teleport(transform.position, transform.rotation, transform.localScale);
    }

    /// <summary>This PC's player started an execution on its copy: the host holds the enemy for them, if nobody else has it.</summary>
    internal void AskExecution() => ExecutionRpc();
    internal void EndExecution() => ExecutionEndedRpc();

    [Rpc(SendTo.Everyone)]
    private void SlowRpc(float scale, float seconds)
    {
        pace = scale;
        paceLeft = seconds;
    }

    // Seed: the wind-up's, so its voice is the same on every PC (EnemyCombatAI.Telegraph).
    [Rpc(SendTo.NotServer)]
    private void WindupRpc(ulong victim, int combo, byte move, uint seed)
        => combat.ShowWindup(victim == NetworkManager.LocalClientId, combo, (AttackMove)move, seed);

    [Rpc(SendTo.NotServer)]
    private void CancelRpc() => combat.CancelAttack();

    [Rpc(SendTo.NotServer)]
    private void CallRpc()
    {
        if (mind != null) mind.ShowCall();
    }

    [Rpc(SendTo.NotServer)]
    private void GazeRpc(Vector3 at, float seconds)
    {
        if (mind != null) mind.ShowGaze(at, seconds);
    }

    [Rpc(SendTo.NotMe)]
    private void BlockRpc() => CombatCueAudio.Play(gameObject, CombatCue.Block, 1f, true);

    [Rpc(SendTo.NotMe)]
    private void ParryRpc(Vector3 contact, Vector3 away, bool broken)
    {
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out ParryFeedback feedback)) feedback.ShowOtherParry(target, contact, away, broken);
    }

    // Results, each as the PC of the player who did it judged it on its own copy: the host applies them as they come
    // and judges none of them again. Only an execution can be turned down, and only for a true tie (ExecutionRpc).
    [Rpc(SendTo.Server)]
    private void PostureRpc(float amount, float stun, bool interrupt, bool broke) => target.ApplySentPosture(amount, stun, interrupt, broke);

    [Rpc(SendTo.Server)]
    private void InterruptRpc(float duration, RpcParams rpc = default) => target.Interrupt(duration, rpc.Receive.SenderClientId);

    [Rpc(SendTo.NotServer)]
    private void ShowInterruptRpc(float duration, ulong by)
    {
        if (by != NetworkManager.LocalClientId) target.ShowInterrupt(duration);
    }

    [Rpc(SendTo.Server)]
    private void ShoveRpc(Vector3 impulse, Vector3 point) => target.ReceiveShove(impulse, point);

    [Rpc(SendTo.Server)]
    private void KnockDownRpc(Vector3 direction, float duration, bool runs, RpcParams rpc = default)
        => target.KnockDown(direction, duration, runs ? rpc.Receive.SenderClientId : ZombieBody.NoRunner);

    [Rpc(SendTo.Server)]
    private void BargeRpc(Vector3 velocity, float throwTime, float knockdownTime, bool runs, RpcParams rpc = default)
        => target.Barge(velocity, throwTime, knockdownTime, runs ? rpc.Receive.SenderClientId : ZombieBody.NoRunner);

    [Rpc(SendTo.Server)]
    private void ExecutionRpc(RpcParams rpc = default)
    {
        ulong client = rpc.Receive.SenderClientId;
        var player = PlayerOf(client);
        var execution = player != null ? player.GetComponent<PlayerExecution>() : null;
        // Held as asked: their PC judged that it could be executed or taken down, and the host does not judge it again
        // with its own posture, mood and positions. Turned down only for a true tie (someone else holds it already).
        if (execution != null && target.HoldForExecution(execution)) execution.ShowExecuting(target);
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
    private void HitRpc(EnemyHitMessage message, RpcParams rpc = default)
        => enemy.ReceiveSent(message.ToHit(PlayerOf(rpc.Receive.SenderClientId), NetworkObjectId, transform), message.Taken, message.Killed);

    [Rpc(SendTo.NotServer)]
    private void ShowHitRpc(EnemyHitMessage message, ulong attacker, float before, float after)
    {
        if (attacker == NetworkManager.LocalClientId)
        {
            // Their own PC showed the hit as they landed it. Only a kill it did not see coming is news: the host's count,
            // with a friend's hit this PC had not heard of yet, reached zero on this hit.
            if (after <= 0f) enemy.ConfirmKill(message.ToHit(PlayerOf(attacker), NetworkObjectId, transform));
            return;
        }
        enemy.ShowHit(message.ToHit(PlayerOf(attacker), NetworkObjectId, transform), before, after);
    }

    private static GameObject PlayerOf(ulong client)
    {
        foreach (var player in PlayerRoster.All)
            if (player.TryGetComponent(out NetworkObject network) && network.OwnerClientId == client) return player.gameObject;
        return null;
    }
}
