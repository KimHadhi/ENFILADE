using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A store of caged zombies with a way through between the cages (the map gimmick card "철창에 갇힌 좀비 창고", after Guts
/// &amp; Blackpowder's cells under Kaub): a quiet short cut, but the caged ones reach through the bars at whoever goes by
/// upright close to them (each PC bites its own player: biteDamage now and then). When its step's hold begins the cage
/// doors burst (its doors' boxes go, on every PC alike, and the host wakes everything in the cages), and they all come
/// out at once, unless the store's doors were barred first (GimmickBarDoor). A wipe that sends the run back before it
/// shuts the cages again. Shut in, they raise no alarm (ZombieMind.Cage). The bars are chain-link: they hold bodies,
/// thrown things and blasts, and shots go through them (AttackTrace), so whoever sees one in its cage may shoot it there.
/// It has no state of its own to share.
/// Who is shut in is the cage's own to say: how many stand in each of its cages, and the stretch they are set in
/// (Crowds, asked by CampaignRun.PlaceMapCrowds as that stretch begins quiet, and again after a wipe). Where they stand
/// comes from the cage's box, so a crowd can never be set outside the bars it belongs behind. Until 2026-11-18 a
/// separate spot said it (SleeperNest, a component made for crowds laid on the floor, with a switch for "caged"); the
/// user had every lying crowd removed ("[버그] 비활성(자는) 좀비 잔존 문제: 제거 지시된 자는 좀비 오브젝트 완전 삭제";
/// asked how far, "엎드린 무리까지"), which left that component with nothing to name but a cage's crowd, at a place
/// the cage already knew.
/// </summary>
public sealed class GimmickCage : GimmickSwitch
{
    private const float BiteEvery = 1.2f, BiteReach = 1.7f;
    // A caged one stands at least this far in from its cage's sides (m): its own half width and an arm's bend, so it
    // is set on the cage's floor and not in the bars.
    private const float InFromBars = .9f;
    private static readonly HashSet<Collider> through = new HashSet<Collider>();
    private static readonly List<GimmickCage> all = new List<GimmickCage>();

    /// <summary>A cage's crowd: its cage's box, where its middle is on that cage's floor, how many, and how far round it they stand.</summary>
    public readonly struct Crowd
    {
        public readonly Bounds Cage;
        public readonly Vector3 Middle;
        public readonly int Count;
        public readonly float Radius;
        public Crowd(Bounds cage, Vector3 middle, int count, float radius) { Cage = cage; Middle = middle; Count = count; Radius = radius; }
    }

    [SerializeField, Tooltip("The hold that bursts the cages (CampaignSteps id).")]
    private string step = "";
    [SerializeField, Tooltip("The way through between the cages (world box): who walks it upright near the bars is bitten.")]
    private Bounds passage;
    [SerializeField, Tooltip("The cages (world boxes): woken at the burst.")]
    private Bounds[] cages = new Bounds[0];
    [SerializeField, Tooltip("How many are shut in each cage, in the cages' order (none where it has no number).")]
    private int[] held = new int[0];
    [SerializeField, Tooltip("The campaign step whose stretch they are set in (CampaignSteps id): set as that stretch begins quiet.")]
    private string crowdStep = "";
    [SerializeField, Tooltip("The cage doors, gone when they burst.")]
    private GameObject[] doors = new GameObject[0];
    [SerializeField, Tooltip("The bars that stay (chain-link): shots go through them as through the doors.")]
    private GameObject[] bars = new GameObject[0];
    [SerializeField, Min(0f), Tooltip("A bite's damage to a player.")]
    private float biteDamage = 9f;

    private bool burst;
    private float nextBite;

    /// <summary>A cage's bars or door: shots go through it (AttackTrace), as through chain-link.</summary>
    public static bool LetsThrough(Collider collider) => collider != null && through.Contains(collider);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        through.Clear();
        all.Clear();
    }

    private void OnEnable()
    {
        all.Add(this);
        Mark(doors, true);
        Mark(bars, true);
    }

    private void OnDisable()
    {
        all.Remove(this);
        Mark(doors, false);
        Mark(bars, false);
    }

    /// <summary>The stretch this store's crowds are set in (CampaignSteps id); empty: nobody is set.</summary>
    public string CrowdStep => crowdStep;

    /// <summary>
    /// This store's crowds, added to <paramref name="into"/>: one for each cage that holds anyone, its middle on that
    /// cage's floor and its reach kept inside the bars. The run asks through Crowds; a map's build asks each store
    /// itself, to say whether the baked walk mesh gives them the floor to stand on (GreyboxBuilder.Audit), so an
    /// empty cage is read from the build's file and not found by playing to it.
    /// </summary>
    public void AddCrowds(List<Crowd> into)
    {
        for (int i = 0; i < cages.Length && i < held.Length; i++)
        {
            if (held[i] <= 0) continue;
            var box = cages[i];
            float room = Mathf.Max(0f, Mathf.Min(box.extents.x, box.extents.z) - InFromBars);
            into.Add(new Crowd(box, new Vector3(box.center.x, box.min.y, box.center.z), held[i], room));
        }
    }

    /// <summary>The crowds every cage store in the open scenes sets on the stretch of <paramref name="stepId"/>, added to <paramref name="into"/>.</summary>
    internal static void Crowds(string stepId, List<Crowd> into)
    {
        foreach (var cage in all)
            if (cage != null && string.Equals(cage.crowdStep, stepId, System.StringComparison.OrdinalIgnoreCase)) cage.AddCrowds(into);
    }

    private static void Mark(GameObject[] parts, bool on)
    {
        foreach (var part in parts)
        {
            if (part == null) continue;
            foreach (var collider in part.GetComponentsInChildren<Collider>(true))
                if (on) through.Add(collider);
                else through.Remove(collider);
        }
    }

    internal override void Tick(float dt, bool host)
    {
        bool open = CampaignRun.IsRunning(step) || CampaignRun.ProgressOf(step) >= 1f;
        if (open != burst) Burst(open, host);
        if (!burst) Bite();
    }

    private void Burst(bool open, bool host)
    {
        burst = open;
        foreach (var door in doors) if (door != null) door.SetActive(!open);
        if (!open) return;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Smash, passage.center, 1f, 8f, 60f);
        if (!host) return;
        var near = PlayerRoster.Nearest(passage.center);
        foreach (var cage in cages) ZombieMind.FreeWithin(cage, near);
    }

    // The caged ones reach through the bars at this PC's own player, upright in the passage beside them.
    private void Bite()
    {
        var mine = Mine;
        if (mine == null || mine.DamageReceiver == null || Time.time < nextBite) return;
        Vector3 at = mine.transform.position + Vector3.up;
        if (!passage.Contains(at) || mine.IsCrouchStance) return;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || (enemy.transform.position - mine.transform.position).sqrMagnitude > BiteReach * BiteReach) continue;
            if (!InCage(enemy.transform.position + Vector3.up * .5f)) continue;
            nextBite = Time.time + BiteEvery;
            mine.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), biteDamage, DamageKind.Attack, at,
                (mine.transform.position - enemy.transform.position).normalized, enemy.gameObject));
            return;
        }
    }

    private bool InCage(Vector3 at)
    {
        foreach (var cage in cages) if (cage.Contains(at)) return true;
        return false;
    }
}
