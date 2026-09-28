using UnityEngine;

/// <summary>Resolve a real hitscan and reward near misses only before its first solid impact.</summary>
public static class AttackTrace
{
    public static DamageResult Raycast(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers = ~0)
    {
        if (direction.sqrMagnitude < 0.000001f || distance <= 0f) return DamageResult.Ignored;
        direction.Normalize();
        var hits = Physics.RaycastAll(origin, direction, distance, layers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Vector3 end = origin + direction * distance;
        DamageResult result = DamageResult.Ignored;
        foreach (var hit in hits)
        {
            if (attack.Source != null && hit.transform.IsChildOf(attack.Source.transform)) continue;
            end = hit.point;
            var receiver = hit.collider.GetComponentInParent<IDamageReceiver>();
            if (receiver != null)
                result = receiver.ReceiveHit(new DamageHit(attack.Id, attack.Amount, attack.Kind, hit.point, direction, attack.Source));
            break;
        }
        StepDodgeTrail.RecognizeAttackSegment(attack, origin, end);
        return result;
    }
}
