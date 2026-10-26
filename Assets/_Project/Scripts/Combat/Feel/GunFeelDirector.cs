using UnityEngine;

/// <summary>
/// Routes each accepted hit (CombatDamageEvents) to the hit-feedback layers, in this order:
/// target flash / blood / corpse (world) → hit marker + kill sound → kill score and kill feed → kill screen
/// (special look for executions and counters, GTA V-style look for every other kill).
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
        bool byPlayer = hit.Kind != DamageKind.Fall && hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() != null;
        bool kill = report.IsKillingBlow, critical = report.IsCritical, bullet = hit.Method == DamageMethod.Bullet;
        bool execution = hit.Kind == DamageKind.Execution;

        // World: every damaged target flashes, whoever hit it. Living enemies also bleed, and are destroyed on death,
        // so a corpse copy carries the kill; practice targets stay in place and just flash.
        bool enemy = report.Target is EnemyHealth;
        if (!kill || !enemy) HitFlashRenderer.For(report.Target)?.Flash(hit.Point, critical);
        if (enemy && hit.Kind != DamageKind.Fall && (bullet || kill)) ImpactVfx.Blood(hit.Point, hit.Direction, critical, kill, bullet, report.Target);
        if (enemy && kill) CorpseTopple.Spawn(report.Target, hit, critical);
        if (!byPlayer) return;

        HitMarkerHUD.Show(kill ? (critical ? HitMarkerKind.HeadKill : HitMarkerKind.BodyKill) : critical ? HitMarkerKind.Head : HitMarkerKind.Body);
        ConfirmSound(settings, kill, critical);
        bool pulse = execution ? settings.killPulseOnExecution : hit.IsCounter ? settings.killPulseOnCounter
            : bullet ? settings.killPulseOnGunKill : settings.killPulseOnOtherKill;
        if (!kill) return;
        float distance = hit.Point != Vector3.zero ? Vector3.Distance(hit.Source.transform.position, hit.Point) : 0f;
        CombatFeedHUD.Kill(WeaponName(hit), CombatDamageEvents.DisplayNameOf(report.Target), critical && bullet, execution, hit.IsCounter, bullet, distance);
        if (pulse) KillConfirmEffect.Trigger(execution ? KillScreen.Execution : hit.IsCounter ? KillScreen.Counter : KillScreen.Gta,
            critical || execution);
    }

    /// <summary>
    /// UI confirm on top of the flesh sounds from CombatHitAudio: a low thump for a kill. Ordinary hits have no confirm
    /// sound; the marker and the flesh impact carry them.
    /// </summary>
    private static void ConfirmSound(GunFeelSettings settings, bool kill, bool critical)
    {
        if (!kill) return;
        FeelAudio.Play(critical ? FeelSound.HeadshotKill : FeelSound.Kill, critical ? settings.headshotKillSounds : settings.killSounds,
            settings.killVolume, Random.Range(.97f, 1.03f));
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
