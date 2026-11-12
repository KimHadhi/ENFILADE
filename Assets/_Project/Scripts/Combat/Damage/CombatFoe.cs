using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something the players fight that is not a player: a zombie (EnemyHealth), or a practice target on the hub's firing
/// range (RangeTarget). The classes take any foe the same way: its kill stacks what kills stack and pays what kills pay
/// them (ClassKit, ClassCards), its hits charge the ultimate (UltimateCharge.AwardEnemyHit), and F marks it, a
/// Schießhund's bounty mark included (PlayerPing, PingMarks, PlayerClass). A practice foe counts for that alone (the
/// user, 2026-11-12: "로비에 있는 타겟 상대로도 처치 효과 얻을 수 있게. 뭐 킬로그는 안 뜬다 해도"), so trying the classes out on
/// the range never touches the run: no kill feed line, money, XP, streak, stats or records (GunFeelDirector; PlayerTally,
/// PlayerRecords, GunKills and FieldPickups count zombies alone).
/// </summary>
public interface ICombatFoe : ICombatVitals
{
    /// <summary>It can still be hit down: a zombie alive, a range target standing.</summary>
    bool IsAlive { get; }

    /// <summary>A practice target: its kills try the classes out and count for nothing else.</summary>
    bool IsPractice { get; }
}

/// <summary>Finding foes from what a look or a shot met, and where their heads are.</summary>
public static class CombatFoe
{
    private static readonly List<Collider> parts = new List<Collider>();

    /// <summary>The foe <paramref name="collider"/> is part of, or null (a wall, a player, a prop).</summary>
    public static Component Of(Collider collider)
        => collider != null ? collider.GetComponentInParent<ICombatFoe>() as Component : null;

    /// <summary>Whether <paramref name="target"/> is a foe that can still be hit down.</summary>
    public static bool IsAlive(Component target) => target != null && target is ICombatFoe foe && foe.IsAlive;

    /// <summary>
    /// The middle of the top of <paramref name="foe"/>, where a mark stands over it and a bounty bursts: of its own
    /// collider when it has one (a zombie's capsule), else of its solid parts together (a range target's root has none;
    /// its first part is its legs, its head another part). Its feet and a man's height when it has no collider at all.
    /// </summary>
    public static Vector3 Top(Component foe)
    {
        if (foe == null) return Vector3.zero;
        if (foe.TryGetComponent(out Collider own) && own.enabled) return TopOf(own.bounds);
        bool any = false;
        var all = new Bounds();
        foe.GetComponentsInChildren(parts);
        foreach (var part in parts)
        {
            // A disabled collider's bounds are empty, and a trigger (a zombie's bone hitbox) is no solid part.
            if (part == null || !part.enabled || part.isTrigger) continue;
            if (!any) { all = part.bounds; any = true; }
            else all.Encapsulate(part.bounds);
        }
        parts.Clear();
        return any ? TopOf(all) : foe.transform.position + Vector3.up * 1.8f;
    }

    private static Vector3 TopOf(Bounds bounds) => new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
}
