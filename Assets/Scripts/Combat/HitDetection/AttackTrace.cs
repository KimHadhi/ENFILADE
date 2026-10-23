using UnityEngine;

public readonly struct AttackTraceResult
{
    public readonly bool HasHit;
    public readonly RaycastHit Hit;
    public readonly Vector3 End;
    public readonly DamageResult Damage;
    public AttackTraceResult(bool hasHit, RaycastHit hit, Vector3 end, DamageResult damage)
    { HasHit = hasHit; Hit = hit; End = end; Damage = damage; }
}

/// <summary>Resolve a real hitscan and reward near misses only before its first solid impact.</summary>
public static class AttackTrace
{
    private static readonly RaycastHit[] ownerSkipHits = new RaycastHit[32];
    public static DamageResult Raycast(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers = ~0)
        => Trace(attack, origin, direction, distance, layers).Damage;

    public static AttackTraceResult Trace(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers = ~0)
    {
        if (direction.sqrMagnitude < 0.000001f || distance <= 0f)
            return new AttackTraceResult(false, default, origin, DamageResult.Ignored);
        direction.Normalize();
        Vector3 end = origin + direction * distance;
        DamageResult result = DamageResult.Ignored;
        bool hasHit = FirstExternalHit(attack.Source, origin, direction, distance, layers, out RaycastHit hit);
        if (hasHit)
        {
            end = hit.point;
            var receiver = hit.collider.GetComponentInParent<IDamageReceiver>();
            if (receiver != null)
                result = receiver.ReceiveHit(new DamageHit(attack.Id, attack.Amount, attack.Kind, hit.point, direction, attack.Source,
                    attack.CanBeGuarded, attack.PostureDamage, attack.IsComboFinisher));
        }
        StepDodgeTrail.RecognizeAttackSegment(attack, origin, end);
        return new AttackTraceResult(hasHit, hit, end, result);
    }

    private static bool IsOwner(RaycastHit hit, GameObject owner)
        => owner != null && hit.collider.transform.IsChildOf(owner.transform);

    private static bool FirstExternalHit(GameObject owner, Vector3 origin, Vector3 direction, float range, int layers, out RaycastHit hit)
    {
        if (!Physics.Raycast(origin, direction, out hit, range, layers, QueryTriggerInteraction.Ignore)) return false;
        if (!IsOwner(hit, owner)) return true;
        // Most shots need one non-allocating Raycast. Only own-body obstruction needs a multi-hit query.
        int count = Physics.RaycastNonAlloc(origin, direction, ownerSkipHits, range, layers, QueryTriggerInteraction.Ignore);
        RaycastHit[] results = ownerSkipHits;
        if (count == results.Length)
        {
            // A full NonAlloc buffer may omit the nearest wall. Preserve correctness under saturation.
            results = Physics.RaycastAll(origin, direction, range, layers, QueryTriggerInteraction.Ignore);
            count = results.Length;
        }
        hit = default; float closest = float.PositiveInfinity; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var candidate = results[i];
            if (IsOwner(candidate, owner) || candidate.distance >= closest) continue;
            closest = candidate.distance; hit = candidate; found = true;
        }
        return found;
    }
}
