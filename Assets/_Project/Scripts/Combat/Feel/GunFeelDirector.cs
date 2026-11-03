using UnityEngine;

/// <summary>
/// Routes each accepted hit (CombatDamageEvents) to the hit-feedback layers, in this order:
/// target flash / blood / corpse (world) → hit marker + confirm sounds (HitConfirm) → kill score and kill feed → kill screen
/// (special look for executions and counters, a brightened, glowing look for every other kill).
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
        // kill, and are destroyed on death, so a corpse copy carries the kill; practice targets stay in place and just flash.
        bool enemy = report.Target is EnemyHealth;
        if (!kill || !enemy) HitFlashRenderer.For(report.Target)?.Flash(hit.Point, critical);
        if (enemy && hit.Kind != DamageKind.Fall && (hit.Method != DamageMethod.Shove || kill)) ImpactVfx.Blood(hit.Point, hit.Direction, critical, kill, bullet, report.Target);
        if (enemy && kill) CorpseTopple.Spawn(report.Target, hit, critical);
        // A shared enemy's death is the host's to call, so one count and one feed row reach every PC, whoever saw it first.
        bool shared = kill && report.Target.TryGetComponent(out EnemyNetwork network) && network.IsSpawned;
        if (shared && hit.Source != null && hit.Source.GetComponentInParent<PlayerNetworkScore>() is PlayerNetworkScore score)
            score.AnnounceKill(WeaponName(hit), CombatDamageEvents.DisplayNameOf(report.Target), critical && bullet);
        if (!byPlayer) return;

        HitConfirm.Landed(report);
        bool pulse = execution ? settings.killPulseOnExecution : hit.IsCounter ? settings.killPulseOnCounter
            : bullet ? settings.killPulseOnGunKill : settings.killPulseOnOtherKill;
        if (!kill) return;
        float distance = hit.Point != Vector3.zero ? Vector3.Distance(hit.Source.transform.position, hit.Point) : 0f;
        if (PlayerRoster.Local != null && PlayerRoster.Local.TryGetComponent(out PlayerTally tally))
        {
            tally.CountKill(execution);
            if (!shared)
            {
                tally.CountStreak();
                if (tally.Streak == CombatFeedHUD.SkullStreak) SkullSlowMotion.Begin();
            }
        }
        CombatFeedHUD.Kill(WeaponName(hit), CombatDamageEvents.DisplayNameOf(report.Target), critical && bullet, execution, hit.IsCounter, bullet, distance, !shared);
        if (pulse) KillConfirmEffect.Trigger(execution ? KillScreen.Execution : hit.IsCounter ? KillScreen.Counter : KillScreen.Ordinary,
            critical || execution);
    }

    private static string WeaponName(DamageHit hit)
    {
        if (hit.Kind == DamageKind.Execution) return "처형";
        if (hit.IsCounter) return "반격";
        if (hit.Method == DamageMethod.Shove) return "밀치기";
        if (hit.Method == DamageMethod.Bullet && hit.Source != null)
        {
            var weapon = hit.Source.GetComponentInChildren<WeaponController>();
            if (weapon != null && weapon.EquippedData != null) return weapon.EquippedData.DisplayName;
        }
        return "주먹";
    }
}
