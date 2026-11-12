using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player this enemy is after: the nearest one alive, reconsidered now and then so it doesn't keep flipping. A
/// Knight's taunt (ClassKit, stage 2 of the classes) turns it on the Knight for a few seconds, whatever else it was
/// after, so long as the Knight stays on their feet.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyTargeting : MonoBehaviour
{
    [SerializeField, Min(.05f)] private float retargetInterval = .5f;
    private static readonly List<EnemyTargeting> all = new List<EnemyTargeting>();
    private PlayerMovement player, taunter;
    private ZombieMind mind;
    private EnemyNetwork network;
    private EnemyCombatAI combat;
    private float nextLook, tauntUntil;
    // Called out in a Schießhund's duel: the blow it is winding up on someone else is finished first.
    private bool finishFirst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => all.Clear();

    private void OnEnable() => all.Add(this);

    private void OnDisable()
    {
        all.Remove(this);
        taunter = null;
    }

    public PlayerMovement Player
    {
        get
        {
            // Taunted: the Knight, until it wears off or they go down. Called out in a duel, it first finishes the blow it
            // was already winding up on someone else, which then lands on nothing (HoundDuel): turning on the hound mid-swing
            // would cheapen the save (the user, 2026-11-12: "적이 공격하려고 내려찍으려 하고 있었어 근데 결투를 하면 갑자기
            // 캔슬하고 시순트를 바라보면 구해지는 입장에서 짜칠 것 같아").
            if (taunter != null)
            {
                if (Time.time < tauntUntil && Standing(taunter))
                {
                    if (combat == null) combat = GetComponent<EnemyCombatAI>();
                    if (finishFirst && player != null && player != taunter && combat != null && combat.IsPreparingAttack) return player;
                    finishFirst = false;
                    return player = taunter;
                }
                taunter = null;
                finishFirst = false;
            }
            // A zombie that noticed someone goes after them, not whoever is nearest.
            if (mind == null) mind = GetComponent<ZombieMind>();
            var noticed = mind != null ? mind.Target : null;
            if (noticed != null) return player = noticed;
            bool lost = player == null || !player.IsInitialized || !player.DamageReceiver.IsAlive;
            if (lost || Time.time >= nextLook)
            {
                nextLook = Time.time + retargetInterval;
                player = PlayerRoster.Nearest(transform.position);
            }
            return player;
        }
    }

    /// <summary>
    /// A Knight's taunt at <paramref name="at"/>: every enemy this PC runs (the host's or a lone PC's; a copy only follows
    /// its host) within <paramref name="reach"/> that is already in the fight goes after <paramref name="knight"/> for
    /// <paramref name="seconds"/>, an elite as much as the pack. One standing calm is left be: a taunt never wakes anything,
    /// so a Knight can't give the team's stealth away with it.
    /// </summary>
    public static void Taunt(PlayerMovement knight, Vector3 at, float reach, float seconds)
    {
        if (!Standing(knight) || seconds <= 0f || reach <= 0f) return;
        float until = Time.time + seconds, most = reach * reach;
        foreach (var enemy in all)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || (enemy.transform.position - at).sqrMagnitude > most) continue;
            if (enemy.network == null) enemy.network = enemy.GetComponent<EnemyNetwork>();
            if (enemy.network != null && enemy.network.IsCopy) continue;
            if (enemy.mind == null) enemy.mind = enemy.GetComponent<ZombieMind>();
            if (enemy.mind != null && !enemy.mind.IsAwake) continue;
            enemy.taunter = knight;
            enemy.tauntUntil = until;
        }
    }

    /// <summary>
    /// A Schießhund's duel (HoundDuel): this one enemy, if this PC runs it (the host's or a lone PC's; a copy follows its
    /// host), goes after <paramref name="hound"/> alone for <paramref name="seconds"/>, whatever it was after, as a taunt
    /// turns it, until the hound goes down.
    /// </summary>
    public static void Duel(EnemyHealth enemy, PlayerMovement hound, float seconds)
    {
        if (enemy == null || seconds <= 0f || !Standing(hound) || !enemy.TryGetComponent(out EnemyTargeting targeting)) return;
        if (targeting.network == null) targeting.network = enemy.GetComponent<EnemyNetwork>();
        if (targeting.network != null && targeting.network.IsCopy) return;
        targeting.taunter = hound;
        targeting.tauntUntil = Time.time + seconds;
        targeting.finishFirst = true;
    }

    // Alive and not down: a Knight lying on the ground no longer holds anything's eye (they'd only be eaten).
    private static bool Standing(PlayerMovement who) => who != null && who.IsInitialized && who.DamageReceiver.IsAlive && !who.IsDowned;
}
