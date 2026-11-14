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
/// </summary>
public sealed class GimmickCage : GimmickSwitch
{
    private const float BiteEvery = 1.2f, BiteReach = 1.7f;
    private static readonly HashSet<Collider> through = new HashSet<Collider>();

    [SerializeField, Tooltip("The hold that bursts the cages (CampaignSteps id).")]
    private string step = "";
    [SerializeField, Tooltip("The way through between the cages (world box): who walks it upright near the bars is bitten.")]
    private Bounds passage;
    [SerializeField, Tooltip("The cages (world boxes): woken at the burst.")]
    private Bounds[] cages = new Bounds[0];
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
    private static void Clear() => through.Clear();

    private void OnEnable()
    {
        Mark(doors, true);
        Mark(bars, true);
    }

    private void OnDisable()
    {
        Mark(doors, false);
        Mark(bars, false);
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
