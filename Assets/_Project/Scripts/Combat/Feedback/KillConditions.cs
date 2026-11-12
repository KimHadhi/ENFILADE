using System.Collections.Generic;
using UnityEngine;

/// <summary>Reads a kill's <see cref="KillMarks"/> from the damage report and from what every PC already shares.</summary>
public static class KillConditions
{
    /// <summary>
    /// What a hit carries from the one who landed it: their own state, which only their PC knows. Nothing when the hit
    /// isn't this PC's player's (the host learns a friend's from their hit: EnemyHitMessage).
    /// </summary>
    public static KillMarks Attacker(GameObject source)
    {
        if (source == null) return KillMarks.None;
        var who = source.GetComponentInParent<PlayerMovement>();
        var kit = ClassKit.Local;
        if (who == null || !who.IsLocal || kit == null || kit.Movement != who) return KillMarks.None;
        var marks = KillMarks.None;
        // Under a CHARGE, the Assault's own or an Assault friend's (ClassKit.ReceiveCharge).
        if (kit.ChargeShare > 0f) marks |= KillMarks.Charged;
        if (kit.KataShare > 0f) marks |= KillMarks.Kata;
        // In the ultimate's seconds after spending it, as long as its own effects last (ClassCards.Ultimate).
        if (Time.time - DuelWatch.UltimateAt <= Mathf.Max(1f, PlayerClass.Ability("Cards", "ultimateSeconds", 10f))) marks |= KillMarks.Ultimate;
        return marks;
    }

    /// <summary>
    /// Everything a kill happened under, as the PC that calls it sees it: the blow (headshot, through something, from far,
    /// a blast, an execution of one that never knew, a counter, a shove, a thrown knife, bare hands), who fell (named or elite), the
    /// killer's own state (from the hit, or this PC's own player's), and a Schießhund's quarry. distance: shooter to hit
    /// point (m), 0 when unknown.
    /// </summary>
    public static KillMarks Of(CombatDamageReport report, PlayerMovement killer, float distance)
    {
        var hit = report.Hit;
        var target = report.Target;
        var marks = hit.Marks | Attacker(hit.Source);
        bool bullet = hit.Method == DamageMethod.Bullet;
        if (bullet && report.IsCritical) marks |= KillMarks.Headshot;
        if (bullet && hit.Pierced) marks |= KillMarks.Pierced;
        if (bullet && distance > 0f && distance >= GunFeelSettings.Instance.longRangeDistance) marks |= KillMarks.LongRange;
        if (hit.Kind == DamageKind.Execution)
        {
            marks |= KillMarks.Execution;
            if (target != null && target.TryGetComponent(out ZombieMind mind) && mind.Unaware) marks |= KillMarks.Takedown;
        }
        else if (hit.IsCounter) marks |= KillMarks.Counter;
        else if (hit.Method == DamageMethod.Shove) marks |= KillMarks.Shove;
        // A class throwable: a frag's burst, a knife that struck as it flew (Throwables).
        else if (hit.Method == DamageMethod.Blast) marks |= KillMarks.Explosion;
        else if (hit.Method == DamageMethod.Thrown) marks |= KillMarks.Thrown;
        // A can's blast is the one other player hit that says where it came from (CampaignCans); the rest is bare hands.
        else if (!bullet && hit.Method == DamageMethod.Other) marks |= hit.Origin.HasValue ? KillMarks.Explosion : KillMarks.Melee;
        if (target is EnemyHealth enemy)
        {
            if (enemy.IsStandout) marks |= KillMarks.Standout;
            if (PlayerClass.IsElite(enemy)) marks |= KillMarks.Elite;
        }
        if (killer != null && target != null)
        {
            // Every PC keeps everyone's bounty marks and duels (PlayerNetworkCombat's PingRpc and DuelRpc).
            if (PingMarks.IsBountyOf(target, killer)) marks |= KillMarks.Bounty;
            if (DuelWatch.Was(killer, target)) marks |= KillMarks.Duel;
            // A CHARGE glows on everyone it took, on every PC, for as long as it lasts (ChargeGlow): the host sees a
            // friend's even for a kill whose hit didn't say.
            if (!killer.IsLocal && killer.TryGetComponent(out ChargeGlow glow) && glow.enabled) marks |= KillMarks.Charged;
        }
        return marks;
    }

    // The duel watch lives for the whole run, from the first scene on (it reads every player's duel each frame).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void WatchDuels() => DuelWatch.Begin();

    /// <summary>
    /// Each player's duel foe as last seen while it lived. A duel ends the moment its foe falls (HoundDuel only answers
    /// for a live one), so the kill that ends it looks here: the foe of a duel seen in the last <see cref="Grace"/> s.
    /// It also sees this PC's player spend their ultimate: the gauge emptying at once (UltimateCharge), since the ultimate
    /// itself says nothing others can hear.
    /// </summary>
    private sealed class DuelWatch : MonoBehaviour
    {
        private const float Grace = .75f;
        private static readonly Dictionary<PlayerMovement, (GameObject foe, float at)> seen = new Dictionary<PlayerMovement, (GameObject, float)>();
        private static DuelWatch instance;
        private static float lastCharge = -1f;

        /// <summary>When this PC's player last spent their ultimate (Time.time), far back for never.</summary>
        public static float UltimateAt { get; private set; } = -999f;

        public static void Begin()
        {
            seen.Clear();
            lastCharge = -1f;
            UltimateAt = -999f;
            if (instance != null) return;
            var host = new GameObject("Kill Duel Watch");
            RuntimeSceneHierarchy.Keep(host);
            instance = host.AddComponent<DuelWatch>();
        }

        public static bool Was(PlayerMovement hound, Component foe)
            => hound != null && foe != null && seen.TryGetValue(hound, out var last) && last.foe == foe.gameObject && Time.time - last.at <= Grace;

        private void Update()
        {
            var players = PlayerRoster.All;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null) continue;
                var duel = HoundDuel.Of(player);
                if (duel != null && duel.Foe != null) seen[player] = (duel.Foe.gameObject, Time.time);
            }
            var gauge = PlayerRoster.Local != null ? PlayerRoster.Local.Ultimate : null;
            if (gauge == null) { lastCharge = -1f; return; }
            float now = gauge.CurrentCharge;
            // Spent: most of the cost gone in one frame (a refill or a slow drain never drops like that).
            if (lastCharge >= 0f && gauge.RequiredCharge > 0f && lastCharge - now >= gauge.RequiredCharge * .9f) UltimateAt = Time.time;
            lastCharge = now;
        }

        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
