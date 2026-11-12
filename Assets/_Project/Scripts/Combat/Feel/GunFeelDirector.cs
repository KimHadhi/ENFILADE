using UnityEngine;

/// <summary>
/// Routes each accepted hit (CombatDamageEvents) to the hit-feedback layers, in this order:
/// target flash / blood / corpse (world) → hit marker + confirm sounds (HitConfirm) → kill score and kill feed → kill screen
/// (special look for executions and counters, a brightened, glowing look for every other kill).
/// A practice target's kill (the hub's range) gets the marker, the sound and the kill screen, never the score or the feed.
/// Presentation only; gameplay reactions are called directly by the receivers (EnemyHitReaction).
/// </summary>
public static class GunFeelDirector
{

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
    }

    private static void OnDamaged(CombatDamageReport report)
    {
        if (!Application.isPlaying || report.Target == null) return;
        var settings = GunFeelSettings.Instance;
        var hit = report.Hit;
        bool byPlayer = hit.Kind != DamageKind.Fall && hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() != null && !report.ByOtherPlayer;
        bool kill = report.IsKillingBlow, critical = report.IsCritical, bullet = hit.Method == DamageMethod.Bullet;
        bool execution = hit.Kind == DamageKind.Execution;

        // World: every damaged target flashes, whoever hit it. Living enemies also bleed, except from a shove that doesn't
        // kill, and are destroyed on death, so a corpse copy carries the kill. A target that shows its own hits (the hub's
        // steel targets: sparks, a lead splash, a swing) doesn't flash red as a body does.
        bool enemy = report.Target is EnemyHealth;
        if ((!kill || !enemy) && !(report.Target is IOwnHitFeedback)) HitFlashRenderer.For(report.Target)?.Flash(hit.Point, critical);
        if (enemy && hit.Kind != DamageKind.Fall && (hit.Method != DamageMethod.Shove || kill)) ImpactVfx.Blood(hit.Point, hit.Direction, critical, kill, bullet, report.Target);
        if (enemy && kill) CorpseTopple.Spawn(report.Target, hit, critical);
        // A shared enemy's death is the host's to call, so one count and one feed row reach every PC, whoever saw it first.
        bool shared = kill && report.Target.TryGetComponent(out EnemyNetwork network) && network.IsSpawned;
        // A named or elite enemy's kill burns magenta in the feed and slows the game briefly (backlog 325).
        bool standout = kill && report.Target is EnemyHealth ranked && ranked.IsStandout;
        // What the kill happened under, for its flair in the feed (KillFlairs): decided here, by the PC that calls the kill.
        var killer = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        float reach = kill && hit.Point != Vector3.zero && hit.Source != null ? Vector3.Distance(hit.Source.transform.position, hit.Point) : 0f;
        var marks = kill ? KillConditions.Of(report, killer, reach) : KillMarks.None;
        if (shared && hit.Source != null && hit.Source.GetComponentInParent<PlayerNetworkScore>() is PlayerNetworkScore score)
            score.AnnounceKill(WeaponName(hit), CombatDamageEvents.DisplayNameOf(report.Target), critical && bullet, standout, WeaponIcon(hit), marks);
        if (!byPlayer) return;

        HitConfirm.Landed(report);
        bool pulse = execution ? settings.killPulseOnExecution : hit.IsCounter ? settings.killPulseOnCounter
            : bullet ? settings.killPulseOnGunKill : settings.killPulseOnOtherKill;
        if (!kill) return;
        var screen = execution ? KillScreen.Execution : hit.IsCounter ? KillScreen.Counter : KillScreen.Ordinary;
        // A practice target's kill (the hub's range, RangeTarget) lands on screen as a kill and is the classes' to count
        // (ClassKit, ClassCards), but nothing of the run's: no kill count, streak, feed line, money or named kill, so
        // trying the classes out there leaves the run as it was.
        if (report.IsPractice)
        {
            if (pulse) KillConfirmEffect.Trigger(screen, critical || execution);
            return;
        }
        float distance = hit.Point != Vector3.zero ? Vector3.Distance(hit.Source.transform.position, hit.Point) : 0f;
        if (PlayerRoster.Local != null && PlayerRoster.Local.TryGetComponent(out PlayerTally tally))
        {
            tally.CountKill(execution);
            if (!shared)
            {
                tally.CountStreak();
                if (tally.Streak == CombatFeedHUD.SkullStreak) SkullSlowMotion.Begin();
                else if (standout) SkullSlowMotion.BeginBrief();
            }
        }
        // A takedown of one that never knew they were there pays a stealth bonus (암살). It is judged before the zombie
        // goes, so its mood is still the one it died in (the host's, shared to this PC).
        bool takedown = execution && report.Target.TryGetComponent(out ZombieMind mind) && mind.Unaware;
        // A Schießhund's own mark pays its bounty on top (stage 3 of the classes): more for an elite.
        int bounty = PlayerClass.Is(PlayerClass.Schiesshund) && PingMarks.IsBountyOf(report.Target, PlayerRoster.Local)
            ? Mathf.RoundToInt(PlayerClass.Ability("Schiesshund", PlayerClass.IsElite(report.Target as EnemyHealth) ? "bountyElite" : "bountyMoney",
                PlayerClass.IsElite(report.Target as EnemyHealth) ? 300f : 100f)) : 0;
        CombatFeedHUD.Kill(WeaponName(hit), CombatDamageEvents.DisplayNameOf(report.Target), critical && bullet, execution, hit.IsCounter, bullet, distance, !shared,
            hit.Pierced, standout, takedown, bounty, WeaponIcon(hit), marks);
        // Kills done some way worth a name (MEATSHOT, RAILGUN, SKEET ...) stamp it and pay on top.
        SpecialKills.Kill(report, bullet, critical && bullet, execution);
        if (pulse) KillConfirmEffect.Trigger(screen, critical || execution);
    }

    /// <summary>
    /// The kill feed's picture of what killed (WeaponIcons): the gun's own, or the killer that isn't a weapon. Read from the
    /// hit as WeaponName reads its words: an execution, a counter, a shove (a kick, a sweep and a dash push are shoves too),
    /// a bullet's gun, and a blast, the one player hit other than a bullet that says where it came from (a can, CampaignCans);
    /// the rest is bare hands.
    /// </summary>
    private static string WeaponIcon(DamageHit hit)
    {
        if (hit.Kind == DamageKind.Execution) return WeaponIcons.Execution;
        if (hit.Kind == DamageKind.Fall) return WeaponIcons.Fall;
        if (hit.IsCounter) return WeaponIcons.Counter;
        if (hit.Method == DamageMethod.Shove) return WeaponIcons.Shove;
        // A class throwable's own picture (Throwables: ThrowingKnife, GrenadeFrag, ...).
        if (hit.Method == DamageMethod.Thrown || hit.Method == DamageMethod.Blast) return Throwables.FeedIcon(hit);
        if (hit.Method == DamageMethod.Bullet && hit.Source != null)
        {
            var weapon = hit.Source.GetComponentInChildren<WeaponController>();
            if (weapon != null && weapon.EquippedData != null) return WeaponIcons.IdOf(weapon.EquippedData);
        }
        if (hit.Method == DamageMethod.Other && hit.Origin.HasValue) return WeaponIcons.Explosion;
        return WeaponIcons.Fist;
    }

    private static string WeaponName(DamageHit hit)
    {
        if (hit.Kind == DamageKind.Execution) return Texts.Get("feed.weapon.execution");
        if (hit.IsCounter) return Texts.Get("feed.weapon.counter");
        if (hit.Method == DamageMethod.Shove) return Texts.Get("feed.weapon.shove");
        // A class throwable's: the knife that struck or the frag that burst, by the thrower's throwable (Throwables).
        if (hit.Method == DamageMethod.Thrown || hit.Method == DamageMethod.Blast) return Throwables.FeedName(hit);
        if (hit.Method == DamageMethod.Bullet && hit.Source != null)
        {
            var weapon = hit.Source.GetComponentInChildren<WeaponController>();
            if (weapon != null && weapon.EquippedData != null) return weapon.EquippedData.DisplayName;
        }
        return Texts.Get("feed.weapon.fist");
    }
}
