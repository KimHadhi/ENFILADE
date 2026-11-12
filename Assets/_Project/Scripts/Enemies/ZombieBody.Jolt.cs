using UnityEngine;

/// <summary>
/// The body jolts where it is hit (ZombieJolt: the bought Procedural Hit Reactions, fitted to this body). This part only
/// says when: while the Animator poses the body. Lying on the doll a blow pushes the doll instead (Hit), the blow that
/// kills it throws the corpse, and an execution plays its own scene. It is on top of what was already here and leaves
/// it alone: the flinch clip when an attack is interrupted, the reel when its posture breaks, and the push and the
/// slowing a bullet gives on the host (EnemyHitReaction).
/// </summary>
public sealed partial class ZombieBody
{
    private ZombieJolt jolt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void WatchBlows()
    {
        CombatDamageEvents.Damaged -= OnBlow;
        CombatDamageEvents.Damaged += OnBlow;
    }

    // Every PC hears of every hit once (the attacker's own guess, the host, the others as the host tells them), with
    // where it landed and which way it flew: no jolt has to be sent.
    private static void OnBlow(CombatDamageReport report)
    {
        if (report.Target is EnemyHealth enemy && enemy.TryGetComponent(out ZombieBody body)) body.Jolt(report);
    }

    // Once dressed and its graph is built (Start).
    private void BuildJolt()
    {
        if (Model != null) jolt = ZombieJolt.Build(transform, Model.transform, human);
    }

    private void Jolt(in CombatDamageReport report)
    {
        // Not while it eases out of the doll's pose either (the quarter second after it is stood up at once, EndDown):
        // that easing rewrites every bone from what it reads, so with the Animator off screen a jolt would be read back
        // in and pile up.
        if (jolt == null || Model == null || animator == null || !animator.enabled || down || blendLeft > 0f) return;
        var hit = report.Hit;
        if (report.IsKillingBlow || hit.Kind != DamageKind.Attack) return;
        // A player's blow: a bullet, a shove or a kick, a fist or a slide. A hit that is no bullet but comes with the
        // place it started from is a blast or a fire (it goes down or burns instead) or another zombie's swing that
        // caught it, and a grab, a bite, a leap or a slam is only ever a zombie's. The method and the origin are sent
        // with the hit, so every PC makes the same choice (who landed it is not: a zombie reads as the host elsewhere).
        bool blow = hit.Method == DamageMethod.Bullet || hit.Method == DamageMethod.Shove
            || (hit.Method == DamageMethod.Other && !hit.Origin.HasValue);
        if (!blow) return;
        jolt.Hit(report, target != null && target.Steadfast, network != null ? network.Pace : 1f);
    }

    // The Animator is about to stop posing the body: going down on the doll (GoDown), or dying (Fall).
    private void StopJolt()
    {
        if (jolt != null) jolt.Stop();
    }
}
