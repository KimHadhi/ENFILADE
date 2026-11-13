using UnityEngine;
using UnityEngine.AI;

// ZombieMind: What it knows, for anyone to read (the stealth HUD, a Ghost's detection tool). One part of the class; the rest is in ZombieMind.cs and the files beside it.
public sealed partial class ZombieMind
{
    /// <summary>It made someone out and is drawing breath to call the others (callTell), on every PC alike.</summary>
    public bool Calling => DrawsBreath && Living;

    /// <summary>How far into its tell it is, 0 to 1, on every PC: the call goes out at 1. 0 while it isn't calling.</summary>
    public float CallShare => !Calling ? 0f : callTell <= 0f ? 1f
        : Mathf.Clamp01(IsCopy ? (Time.time - shownCallSince) / callTell : 1f - (callAt - Time.time) / callTell);

    /// <summary>
    /// On the host or alone: some zombie that started calm is awake and after someone now (a local alarm, its own and
    /// those its call woke; a horde's don't count). A campaign's quiet stretch survives it unless it lasts (CampaignRun).
    /// </summary>
    public static bool AlarmUp
    {
        get
        {
            foreach (var mind in minds) if (mind != null && !mind.horde && mind.Thinks && mind.IsAwake) return true;
            return false;
        }
    }

    // Drawing breath, alive or not: the blow that kills it during its tell still counts (Witnessed), as its copies are told.
    private bool DrawsBreath => IsCopy ? shownCalling : calling;

    /// <summary>The host's tell, for its copies (EnemyNetwork).</summary>
    internal bool CallingShared => calling && Living;

    /// <summary>How far it sees now (m): its calm sight while calm, less again while eating or dozing.</summary>
    internal float ViewRange => (Mood == ZombieMood.Calm || lured ? calmSightRange : sightRange) * BusyRange;

    /// <summary>Its full view and the edge of its sight, side to side (degrees), narrower while eating or dozing.</summary>
    internal float FrontView => fieldOfView * BusyView;

    internal float EdgeView => Mathf.Max(fieldOfView, sideView) * BusyView;

    /// <summary>
    /// What its calm pose is busy with (ZombieBody sets it on every PC while it holds the pose; Rest otherwise): eating
    /// or dozing, it sees less far and narrower, hears less and is slower to notice (eat*, doze* in ZombieSenses). The
    /// 2026-11-11 memo, item 47.
    /// </summary>
    internal CalmPoseKind PoseKind { get; set; }

    private float BusyRange => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatRangeShare : PoseKind == CalmPoseKind.Doze ? dozeRangeShare : 1f;

    private float BusyView => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatViewShare : PoseKind == CalmPoseKind.Doze ? dozeViewShare : 1f;

    private float BusyHear => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatHearShare : PoseKind == CalmPoseKind.Doze ? dozeHearShare : 1f;

    private float BusyNotice => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatNoticeShare : PoseKind == CalmPoseKind.Doze ? dozeNoticeShare : 1f;

    /// <summary>
    /// Stage 4C-2: not awake (calm, searching or alert) and this spot behind its back, so a player standing there may take
    /// it down, an execution without breaking it first (MeleeCombatTarget.CanBeExecutedBy). Every PC judges it the same
    /// way from the mood the host shares; the host checks again when it holds the zombie for them.
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
        guns.Clear();
        Woke = null;
        Called = null;
        LastNoticed = float.NegativeInfinity;
        LastSeenAt = Vector3.zero;
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
        if (minds.Count != 1) return;
        Noise.Made += Hear;
        // Once, however many play sessions went before (statics may outlive one).
        CombatDamageEvents.Damaged -= Witnessed;
        CombatDamageEvents.Damaged += Witnessed;
    }

    private void OnDisable()
    {
        minds.Remove(this);
        if (minds.Count != 0) return;
        Noise.Made -= Hear;
        CombatDamageEvents.Damaged -= Witnessed;
    }

    // A witness killed while it drew breath to call: nobody else was told, and the one whose blow it was is paid on their
    // own PC, which saw it calling as every PC does (StealthScore.Witness; a hit gives a plus).
    private static void Witnessed(CombatDamageReport report)
    {
        if (!report.IsKillingBlow || report.IsPractice || report.Target == null || !report.Target.TryGetComponent(out ZombieMind mind)
            || !mind.DrawsBreath) return;
        var mine = PlayerRoster.Local;
        if (mine == null || report.Hit.Source == null || report.Hit.Source.GetComponentInParent<PlayerMovement>() != mine) return;
        int points = StealthScore.Witness;
        if (points > 0) CombatFeedHUD.Special(Texts.Get("feed.score.witness"), points, KitStyle.Accent);
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
}
