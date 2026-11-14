using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Things that drown out the shot that hits them (a map's alarm car, GimmickAlarmCar; the user's note on the map gimmick
/// card "경보 차가 섞인 정체 구간": "경보 차를 멀리서 쏘면 그 자리에 좀비가 몰리게 하는 건 좋아. 전략적이니까. 근데 총 쏴서
/// 그 주변 좀비들이 경계 상태 됐다고 잘 진행하는 스텔스가 망치는 건 싫어. 이러면 아무도 이런 기능을 쓸 생각을 하지 못할 거야"):
/// a shot whose first hit is one of them makes no noise of its own, so the zombies round the shooter hear nothing, and
/// what they hear is the alarm where it went off. Every PC asks it of the same traced path (the shooter's, and the host's
/// copy of a friend's shot: WeaponController), so the host's zombies are spared the shot too.
/// </summary>
public static class ShotMuffle
{
    private static readonly HashSet<Collider> muffling = new HashSet<Collider>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => muffling.Clear();

    /// <summary>This collider drowns out the shots that hit it while it is registered.</summary>
    public static void Add(Collider collider)
    {
        if (collider != null) muffling.Add(collider);
    }

    public static void Remove(Collider collider)
    {
        if (collider != null) muffling.Remove(collider);
    }

    /// <summary>The shot's first hit drowns its noise out.</summary>
    public static bool Hides(in AttackTraceResult trace) => trace.HasHit && trace.Hit.collider != null && muffling.Contains(trace.Hit.collider);
}
