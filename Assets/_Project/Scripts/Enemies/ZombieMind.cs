using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What a zombie knows of the players (ZombieMind). Awake is 0, so an older report reads as awake. Rousing: just woken
/// from calm, it rouses (its rouse clip on every PC) before it charges.
/// </summary>
public enum ZombieMood : byte { Awake, Calm, Searching, Alert, Rousing }

/// <summary>
/// Multiplayer plan stage 4C: what a zombie knows. A horde zombie (a wave's, or a plain /spawn) is awake from the start
/// and always knows where the nearest player is, as before. One that starts calm (placed in a map with startCalm, or
/// /spawn n calm) has to notice them: it stands Calm, grows Alert (stops and turns to what it noticed), goes Searching
/// (walks over to look) and wakes when sure, then chases and fights as before, and calls the calm ones nearby. Its
/// suspicion grows from what it sees and hears. Sight: players within range and in front (slower to the side), each
/// checked at the head, chest and hips with rays, faster the closer, the more of them shows and the faster they move,
/// slower crouched and slower still lying down; anyone this close and seen wakes it at once. Hearing: every Noise
/// (footsteps, landings, vaults, voices, shots) within its reach, half the reach through a wall, louder the nearer. A
/// blow wakes it at once. Awake but no longer seeing nor hearing its quarry, it searches where it last knew them, and
/// gives up in the end. The host thinks; other PCs only show its mood (EnemyNetwork). Numbers: Tables/ZombieSenses.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieMind : MonoBehaviour
{
    public const string TableName = "ZombieSenses", TableRow = "Default";
    private const float ThinkEvery = .1f, ArriveDistance = 1f;
    private const string EnemyLayer = "Enemy";
    // Where a player is looked at, as shares of their capsule's height from its bottom.
    private static readonly float[] LookHeights = { .9f, .62f, .3f };

    [Tooltip("Placed in a map: it starts calm and must notice the players. Otherwise it is a horde zombie, awake from the start.")]
    [SerializeField] private bool startCalm;

    [Header("Sight")]
    [SerializeField, Min(0f), Tooltip("How far it sees, alert or searching (m).")]
    private float sightRange = 22f;
    [SerializeField, Min(0f), Tooltip("How far it sees while calm (m).")]
    private float calmSightRange = 12f;
    [SerializeField, Range(1f, 360f), Tooltip("Its full view, side to side (degrees). Inside it, it notices at full speed.")]
    private float fieldOfView = 110f;
    [SerializeField, Range(1f, 360f), Tooltip("The edge of its sight, side to side (degrees). Between the two it notices at sideShare.")]
    private float sideView = 200f;
    [SerializeField, Range(0f, 1f), Tooltip("How fast it notices at the edge of its sight, as a share.")]
    private float sideShare = .35f;
    [SerializeField, Min(0f), Tooltip("Suspicion a second from a player standing still in full view right in front (1 wakes it).")]
    private float nearRate = 2.5f;
    [SerializeField, Range(0f, 1f), Tooltip("How fast it notices at the far end of its sight, as a share of nearRate.")]
    private float farShare = .15f;
    [SerializeField, Min(0f), Tooltip("Seen this close (m), a player wakes it at once.")]
    private float touchRange = 2f;
    [SerializeField, Range(0f, 1f)] private float crouchShare = .55f;
    [SerializeField, Range(0f, 1f), Tooltip("Lying down: prone, on the back, crawling.")]
    private float lyingShare = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("A player standing still, as a share. Moving it rises to 1 at walking pace.")]
    private float stillShare = .6f;
    [SerializeField, Min(1f), Tooltip("A sprinting player, as a share.")]
    private float sprintShare = 1.6f;
    [SerializeField, Min(.1f), Tooltip("Speed that counts as sprinting (m/s).")]
    private float sprintSpeed = 5f;

    [Header("Hearing")]
    [SerializeField, Range(0f, 2f), Tooltip("Share of a noise's reach it hears, alert or searching.")]
    private float hearShare = 1f;
    [SerializeField, Range(0f, 2f), Tooltip("Share of a noise's reach it hears while calm.")]
    private float calmHearShare = .6f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the reach left through a wall.")]
    private float wallShare = .5f;
    [SerializeField, Min(0f), Tooltip("Suspicion a noise gives right at its source; less the farther off.")]
    private float hearGain = 1.5f;

    [Header("Mind")]
    [SerializeField, Min(0f), Tooltip("Suspicion lost a second while it notices nothing.")]
    private float forget = .25f;
    [SerializeField, Range(0f, 1f), Tooltip("Suspicion at which it stops and turns to look.")]
    private float alertAt = .3f;
    [SerializeField, Min(0f), Tooltip("Awake, with its quarry neither seen nor heard this long (s), it goes searching where it last knew them.")]
    private float loseSeconds = 6f;
    [SerializeField, Min(0f), Tooltip("How long a search lasts before it calms down (s).")]
    private float searchSeconds = 12f;
    [SerializeField, Min(0f), Tooltip("How long it looks round once it reaches the place (s).")]
    private float lookSeconds = 3f;
    [SerializeField, Range(.05f, 1f), Tooltip("Searching, it walks at this share of its chase speed.")]
    private float searchSpeedShare = .3f;
    [SerializeField, Min(0f), Tooltip("Waking, it wakes the calm ones this near (m).")]
    private float callRadius = 12f;
    [SerializeField, Min(0f), Tooltip("Alert, how fast it turns to what it noticed (degrees a second).")]
    private float turnSpeed = 240f;
    [SerializeField, Min(0f), Tooltip("Woken from calm, how long it rouses before it charges (s): its rouse clip.")]
    private float rouseSeconds = 1.1f;

    [Header("Stealth kill")]
    [SerializeField, Range(10f, 180f), Tooltip("Within this angle either side of its back (degrees), a player may take an unaware zombie down (F, or a left click with the fists).")]
    private float takedownAngle = 70f;

    private static readonly List<ZombieMind> minds = new List<ZombieMind>();

    /// <summary>
    /// On the host or alone: a zombie that had not noticed anyone has, and who it is after. A campaign's quiet stretch
    /// ends on it (CampaignRun).
    /// </summary>
    public static event System.Action<ZombieMind, PlayerMovement> Woke;

    private EnemyHealth health;
    private EnemyNetwork network;
    private MeleeCombatTarget melee;
    private NavMeshAgent agent;
    private ZombieBody body;
    private PlayerMovement focus;
    private Vector3 stimulus, lastKnown;
    private float suspicion, thinkLeft, lastSeen, searchStart, arrivedAt, rouseUntil, chaseSpeed = -1f;
    private bool horde = true, arrived;

    /// <summary>Its mood: thought on the host (or alone), shown on every other PC.</summary>
    public ZombieMood Mood { get; private set; } = ZombieMood.Awake;
    /// <summary>It knows someone is there: rousing or awake.</summary>
    public bool IsAwake => Mood == ZombieMood.Awake || Mood == ZombieMood.Rousing;
    /// <summary>Awake and done rousing: it may chase and fight (EnemyChaseAI, EnemyCombatAI).</summary>
    public bool Ready => Mood == ZombieMood.Awake;
    /// <summary>Unaware of anyone: calm or searching, not alert.</summary>
    public bool Unaware => Mood == ZombieMood.Calm || Mood == ZombieMood.Searching;
    /// <summary>The player it is after, awake; null for a horde zombie, which takes the nearest (EnemyTargeting).</summary>
    public PlayerMovement Target => IsAwake && !horde && Alive(focus) ? focus : null;
    /// <summary>How sure it is that someone is there, 0 to 1.</summary>
    public float Suspicion => suspicion;

    /// <summary>
    /// Stage 4C-2: unaware of anyone (calm or searching) and this spot behind its back, so a player standing there may
    /// take it down, an execution without breaking it first (MeleeCombatTarget.CanBeExecutedBy). Every PC judges it the
    /// same way from the mood the host shares; the host checks again when it holds the zombie for them.
    /// </summary>
    public bool CanBeTakenDownFrom(Vector3 at)
    {
        if (!Unaware) return false;
        Vector3 toward = Vector3.ProjectOnPlane(at - transform.position, Vector3.up);
        return toward.sqrMagnitude < 1e-4f || Vector3.Angle(-transform.forward, toward) <= takedownAngle;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatics()
    {
        minds.Clear();
        Woke = null;
    }

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        melee = GetComponent<MeleeCombatTarget>();
        agent = GetComponent<NavMeshAgent>();
        body = GetComponent<ZombieBody>();
        GameTables.Bind(TableName, TableRow, this);
    }

    private void OnEnable()
    {
        minds.Add(this);
        if (minds.Count == 1) Noise.Made += Hear;
    }

    private void OnDisable()
    {
        minds.Remove(this);
        if (minds.Count == 0) Noise.Made -= Hear;
    }

    private void Start()
    {
        // The table has set the agent by now (EnemyHealth binds it in Awake).
        if (agent != null) chaseSpeed = agent.speed;
        thinkLeft = Random.Range(0f, ThinkEvery);
        if (startCalm && !IsCopy) Calm();
    }

    private bool IsCopy => network != null && network.IsCopy;
    private bool Thinks => !IsCopy && health != null && health.IsAlive && !health.WavePaused;

    // ---- Moods -------------------------------------------------------------------------------------------------

    /// <summary>Calms it: it stands where it is and has to notice the players (a placed zombie, /spawn n calm).</summary>
    public void Calm()
    {
        if (IsCopy) return;
        horde = false;
        Mood = ZombieMood.Calm;
        suspicion = 0f;
        focus = null;
        Halt();
    }

    /// <summary>
    /// Awake already, it rouses again for this long: it stands, faces this player and plays its rouse on every PC, then
    /// goes on as before. A charger winding up (ZombieCharger).
    /// </summary>
    internal void Rouse(PlayerMovement at, float seconds)
    {
        if (IsCopy || Mood != ZombieMood.Awake) return;
        Mood = ZombieMood.Rousing;
        if (Alive(at)) focus = at;
        rouseUntil = Time.time + Mathf.Max(0f, seconds);
        Halt();
    }

    /// <summary>Wakes it after who (or the nearest player). Rouse: it rouses a moment first. Call: it wakes the calm near it.</summary>
    public void Wake(PlayerMovement who, bool rouse = true, bool call = true)
    {
        if (IsCopy || IsAwake) return;
        Mood = rouse && rouseSeconds > 0f ? ZombieMood.Rousing : ZombieMood.Awake;
        suspicion = 1f;
        focus = Alive(who) ? who : PlayerRoster.Nearest(transform.position);
        lastSeen = Time.time;
        if (focus != null) lastKnown = focus.transform.position;
        rouseUntil = Time.time + rouseSeconds;
        SetSpeed(1f);
        // Rousing, it stands and faces its quarry; then EnemyChaseAI takes it.
        if (Mood == ZombieMood.Rousing) Halt();
        else if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
        Woke?.Invoke(this, focus);
        if (!call) return;
        foreach (var other in minds)
            if (other != this && !other.IsAwake && other.Thinks && (other.transform.position - transform.position).sqrMagnitude <= callRadius * callRadius)
                other.Wake(focus, true, false);
    }

    private void Alert(Vector3 at)
    {
        stimulus = at;
        if (Mood == ZombieMood.Alert) return;
        Mood = ZombieMood.Alert;
        Halt();
    }

    private void Search(Vector3 at)
    {
        Mood = ZombieMood.Searching;
        stimulus = at;
        searchStart = Time.time;
        arrived = false;
        SetSpeed(searchSpeedShare);
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.SetDestination(at);
    }

    private void Halt()
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
    }

    private void SetSpeed(float share)
    {
        if (agent != null && chaseSpeed > 0f) agent.speed = chaseSpeed * share;
    }

    /// <summary>
    /// Its chase speed changed (its kind, or crawling: ZombieType). Searching walks a share of it, and a flinch slows
    /// from and returns to it.
    /// </summary>
    internal void SetChaseSpeed(float speed)
    {
        chaseSpeed = speed;
        if (TryGetComponent(out EnemyHitReaction reaction)) reaction.SetBaseSpeed(speed);
        SetSpeed(Mood == ZombieMood.Searching ? searchSpeedShare : 1f);
    }

    /// <summary>A blow (EnemyHealth, where it thinks): it wakes at once, after whoever landed it.</summary>
    internal void Struck(in DamageHit hit)
    {
        if (!Thinks || IsAwake) return;
        Wake(hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null, false);
    }

    /// <summary>On another PC: the mood the host's zombie is in.</summary>
    internal void ShowMood(byte mood)
    {
        if (IsCopy) Mood = (ZombieMood)mood;
    }

    // ---- Thinking ----------------------------------------------------------------------------------------------

    private void Update()
    {
        if (!Thinks) return;
        if (Mood == ZombieMood.Alert) TurnTo(stimulus, Time.deltaTime);
        if (Mood == ZombieMood.Rousing)
        {
            if (focus != null) TurnTo(focus.transform.position, Time.deltaTime);
            if (Time.time >= rouseUntil)
            {
                Mood = ZombieMood.Awake;
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
            }
        }
        if ((thinkLeft -= Time.deltaTime) > 0f) return;
        thinkLeft += ThinkEvery;
        if (horde) return;
        // Down, stunned or held it notices nothing new; a blow has woken it anyway.
        if (melee != null && melee.IsStunned) return;
        Think(ThinkEvery);
    }

    private void Think(float dt)
    {
        var seen = Look(out float rate);
        if (IsAwake)
        {
            if (seen != null && (focus == null || seen == focus || !Alive(focus))) focus = seen;
            if (focus != null && seen == focus) { lastSeen = Time.time; lastKnown = focus.transform.position; }
            else if (Time.time - lastSeen > loseSeconds) { suspicion = alertAt; Search(lastKnown); }
            return;
        }
        if (seen != null)
        {
            suspicion += rate * dt;
            focus = seen;
            stimulus = seen.transform.position;
            if (Vector3.Distance(seen.transform.position, transform.position) <= touchRange) { Wake(seen); return; }
        }
        else suspicion = Mathf.Max(0f, suspicion - forget * dt);

        if (suspicion >= 1f) { Wake(focus); return; }
        if (suspicion >= alertAt) { Alert(stimulus); return; }
        if (Mood == ZombieMood.Alert) { Search(stimulus); return; }
        if (Mood == ZombieMood.Searching) Searching();
    }

    private void Searching()
    {
        if (!arrived && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && !agent.pathPending
            && agent.remainingDistance <= ArriveDistance)
        {
            arrived = true;
            arrivedAt = Time.time;
            Halt();
        }
        if ((arrived && Time.time - arrivedAt >= lookSeconds) || Time.time - searchStart >= searchSeconds)
        {
            Mood = ZombieMood.Calm;
            SetSpeed(1f);
            Halt();
        }
    }

    private void TurnTo(Vector3 at, float dt)
    {
        Vector3 way = Vector3.ProjectOnPlane(at - transform.position, Vector3.up);
        if (way.sqrMagnitude < .01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(way), turnSpeed * dt);
    }

    // The player it notices fastest now, and how fast (suspicion a second).
    private PlayerMovement Look(out float best)
    {
        best = 0f;
        PlayerMovement found = null;
        float range = Mood == ZombieMood.Calm ? calmSightRange : sightRange;
        Vector3 eye = Eye();
        foreach (var player in PlayerRoster.All)
        {
            if (!Alive(player)) continue;
            Vector3 toward = player.transform.position - transform.position;
            float distance = toward.magnitude;
            if (distance > range) continue;
            float angle = Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(toward, Vector3.up));
            if (angle > sideView * .5f) continue;
            float shown = Shown(player, eye);
            if (shown <= 0f) continue;
            float rate = nearRate * Mathf.Lerp(1f, farShare, distance / Mathf.Max(.01f, range)) * shown * Posture(player)
                * (angle <= fieldOfView * .5f ? 1f : sideShare);
            if (rate > best) { best = rate; found = player; }
        }
        return found;
    }

    // How much of them it can see: the share of head, chest and hips no wall hides.
    private static float Shown(PlayerMovement player, Vector3 eye)
    {
        var capsule = player.GetComponent<CharacterController>();
        Bounds bounds = capsule != null && capsule.enabled ? capsule.bounds : new Bounds(player.transform.position + Vector3.up * .9f, new Vector3(.6f, 1.8f, .6f));
        int seen = 0;
        foreach (float height in LookHeights)
        {
            Vector3 point = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * height, bounds.center.z);
            Vector3 path = point - eye;
            float length = path.magnitude;
            if (length < .01f || !Physics.Raycast(eye, path / length, out RaycastHit hit, length, Sight, QueryTriggerInteraction.Ignore)
                || hit.collider.GetComponentInParent<PlayerMovement>() == player)
                seen++;
        }
        return seen / (float)LookHeights.Length;
    }

    // Crouched or lying down they are harder to make out; moving, easier, and a sprint most of all.
    private float Posture(PlayerMovement player)
    {
        float share = 1f;
        var state = player.CurrentState;
        if (player.IsProning || state == PlayerMovement.MovementState.Supine || state == PlayerMovement.MovementState.Sliding
            || state == PlayerMovement.MovementState.HeadFirstSliding || state == PlayerMovement.MovementState.SupineSliding)
            share = lyingShare;
        else if (player.IsCrouching) share = crouchShare;
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        float motion = speed >= sprintSpeed ? sprintShare : Mathf.Lerp(stillShare, 1f, Mathf.Clamp01(speed / 1.5f));
        return share * motion;
    }

    private Vector3 Eye()
    {
        var head = body != null && body.Model != null ? body.Head : null;
        return head != null ? head.position : transform.position + Vector3.up * .7f;
    }

    // Walls and the world hide players (maps build them on Default); other zombies don't.
    private static int Sight => ~LayerMask.GetMask(EnemyLayer);

    private static bool Alive(PlayerMovement player)
        => player != null && player.IsInitialized && !player.IsDead && player.DamageReceiver != null && player.DamageReceiver.IsAlive;

    // ---- Hearing -------------------------------------------------------------------------------------------------

    private static void Hear(NoiseEvent noise)
    {
        var source = noise.Source != null ? noise.Source.GetComponentInParent<PlayerMovement>() : null;
        if (source == null || !Alive(source)) return;
        foreach (var mind in minds) if (mind != null && mind.Thinks && !mind.horde) mind.Heard(noise, source);
    }

    private void Heard(in NoiseEvent noise, PlayerMovement source)
    {
        float reach = noise.Radius * (Mood == ZombieMood.Calm ? calmHearShare : hearShare);
        Vector3 ear = Eye();
        float distance = Vector3.Distance(noise.Point, ear);
        if (distance > reach) return;
        Vector3 path = ear - noise.Point;
        if (distance > .01f && Physics.Raycast(noise.Point, path / distance, distance, Sight, QueryTriggerInteraction.Ignore))
        {
            reach *= wallShare;
            if (distance > reach) return;
        }
        float loudness = 1f - distance / Mathf.Max(.01f, reach);
        if (IsAwake)
        {
            // Its quarry gives itself away.
            if (source == focus) { lastSeen = Time.time; lastKnown = source.transform.position; }
            return;
        }
        if (melee != null && melee.IsStunned) return;
        suspicion += hearGain * loudness;
        focus = source;
        stimulus = noise.Point;
        if (suspicion >= 1f) Wake(source);
        else if (suspicion >= alertAt) Alert(noise.Point);
    }
}
