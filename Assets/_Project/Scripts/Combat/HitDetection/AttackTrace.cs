using UnityEngine;

public readonly struct AttackTraceResult
{
    public readonly bool HasHit;
    public readonly RaycastHit Hit;
    public readonly Vector3 End;
    public readonly DamageResult Damage;
    public readonly bool IsHeadshot;
    public AttackTraceResult(bool hasHit, RaycastHit hit, Vector3 end, DamageResult damage, bool isHeadshot = false)
    { HasHit = hasHit; Hit = hit; End = end; Damage = damage; IsHeadshot = isHeadshot; }
}

/// <summary>A stretch of a bullet's path after its first hit (AttackTrace.Bullet).</summary>
public readonly struct BulletLeg
{
    /// <summary>What it went through or glanced off to set off on this stretch.</summary>
    public readonly Collider Through;
    public readonly BulletOnward Onward;
    /// <summary>The share of the gun's damage it still carried (penetration left over the gun's).</summary>
    public readonly float Share;
    /// <summary>What this stretch hit, and the damage that did.</summary>
    public readonly AttackTraceResult Trace;
    public BulletLeg(Collider through, BulletOnward onward, float share, AttackTraceResult trace)
    { Through = through; Onward = onward; Share = share; Trace = trace; }
}

/// <summary>Resolve a real hitscan and reward near misses only before its first solid impact.</summary>
public static class AttackTrace
{
    // Past this many stretches a bullet that is still going is let go; ArcCW has no such end, but it never needs one.
    private const int MostLegs = 16;
    private static readonly RaycastHit[] ownerSkipHits = new RaycastHit[32];
    private static readonly System.Collections.Generic.HashSet<IDamageReceiver> hurt = new System.Collections.Generic.HashSet<IDamageReceiver>();
    private static readonly System.Collections.Generic.List<BulletLeg> legs = new System.Collections.Generic.List<BulletLeg>();
    public static DamageResult Raycast(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers = ~0)
        => Trace(attack, origin, direction, distance, layers).Damage;

    public static AttackTraceResult Trace(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers = ~0, float headshotMultiplier = 1f)
    {
        if (direction.sqrMagnitude < 0.000001f || distance <= 0f)
            return new AttackTraceResult(false, default, origin, DamageResult.Ignored);
        direction.Normalize();
        var result = Strike(attack, origin, direction, distance, layers, headshotMultiplier, 1f, attack.Origin ?? origin, null, false);
        StepDodgeTrail.RecognizeAttackSegment(attack, origin, result.End);
        return result;
    }

    /// <summary>
    /// A gun's bullet: Trace's first hit, then on through what it can get through or off what it glances off
    /// (BulletPenetration), each later hit taking the share of the damage the bullet still carries; anything it passes
    /// is hurt once. `hurts` false only finds the path, for a shot another PC already judged. `later` is null when the
    /// bullet stopped at its first hit.
    /// </summary>
    public static AttackTraceResult Bullet(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers,
        float headshotMultiplier, float penetration, bool hurts, out BulletLeg[] later)
    {
        later = null;
        if (direction.sqrMagnitude < 0.000001f || distance <= 0f)
            return new AttackTraceResult(false, default, origin, DamageResult.Ignored);
        var random = BulletPenetration.RandomFor(origin, direction);
        direction.Normalize();
        Vector3 shooter = attack.Origin ?? origin;
        hurt.Clear();
        var first = Strike(attack, origin, direction, distance, layers, headshotMultiplier, hurts ? 1f : 0f, shooter, hurt, false);
        if (hurts) StepDodgeTrail.RecognizeAttackSegment(attack, origin, first.End);
        if (!first.HasHit || penetration <= 0f) return first;
        legs.Clear();
        RaycastHit hit = first.Hit;
        Vector3 from = origin, dir = direction;
        float left = penetration, rest = distance - hit.distance;
        while (legs.Count < MostLegs && BulletPenetration.Onward(hit, from, dir, left, random, layers, out BulletOnward onward))
        {
            rest -= Vector3.Distance(hit.point, onward.From);
            if (rest <= 0f) break;
            float share = onward.Left / penetration;
            // Through a wall or a body, or off a surface: the hit marker and the kill score show it (DamageHit.Pierced).
            var trace = Strike(attack, onward.From, onward.Direction, rest, layers, headshotMultiplier, hurts ? share : 0f, shooter, hurt, true);
            if (hurts) StepDodgeTrail.RecognizeAttackSegment(attack, onward.From, trace.End);
            legs.Add(new BulletLeg(hit.collider, onward, share, trace));
            if (!trace.HasHit) break;
            rest -= trace.Hit.distance;
            hit = trace.Hit; from = onward.From; dir = onward.Direction; left = onward.Left;
        }
        if (legs.Count > 0) later = legs.ToArray();
        return first;
    }

    // One straight stretch to the first thing it meets, hurting that by `share` of the attack unless it was hurt already
    // by this bullet (`already`); share 0 hurts nothing. `pierced`: the bullet got here through or off something.
    private static AttackTraceResult Strike(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers,
        float headshotMultiplier, float share, Vector3 shooter, System.Collections.Generic.HashSet<IDamageReceiver> already, bool pierced)
    {
        Vector3 end = origin + direction * distance;
        DamageResult result = DamageResult.Ignored;
        bool headshot = false;
        bool hasHit = FirstExternalHit(attack.Source, origin, direction, distance, layers, out RaycastHit hit);
        if (hasHit)
        {
            end = hit.point;
            var receiver = hit.collider.GetComponentInParent<IDamageReceiver>();
            headshot = headshotMultiplier > 1f && WeaponHitRegion.IsHead(hit);
            if (receiver != null && share > 0f && (already == null || already.Add(receiver)))
                result = receiver.ReceiveHit(new DamageHit(attack.Id, attack.Amount * share * (headshot ? headshotMultiplier : 1f), attack.Kind, hit.point, direction, attack.Source,
                    attack.CanBeGuarded, attack.PostureDamage * share, attack.IsComboFinisher, attack.IsCritical || headshot, attack.IsCounter, attack.Method,
                    shooter, attack.IsFatal, attack.Pierced || pierced));
        }
        return new AttackTraceResult(hasHit, hit, end, result, headshot);
    }

    private static bool IsOwner(RaycastHit hit, GameObject owner)
        => owner != null && hit.collider.transform.IsChildOf(owner.transform);
    // The dead lie in their corpse; the capsule they left behind must not stop a shot on its way to it.
    private static bool IsDeadPlayer(RaycastHit hit)
    {
        var player = hit.collider.GetComponentInParent<PlayerMovement>();
        return player != null && player.IsDead;
    }
    private static bool ShouldSkip(RaycastHit hit, GameObject owner)
        => IsOwner(hit, owner) || PlayerLeanHitboxes.Replaces(hit.collider)
            || IsDeadPlayer(hit)
            || (hit.collider.isTrigger && (hit.collider.GetComponent<PlayerCombatHitbox>() == null
                || hit.collider.GetComponentInParent<PlayerLeanHitboxes>()?.UsesSplitBody != true));

    public static bool FirstExternalHit(GameObject owner, Vector3 origin, Vector3 direction, float range, int layers, out RaycastHit hit)
    {
        if (!Physics.Raycast(origin, direction, out hit, range, layers, QueryTriggerInteraction.Collide)) return false;
        if (!ShouldSkip(hit, owner)) return true;
        // Most shots need one non-allocating Raycast. Only own-body obstruction needs a multi-hit query.
        int count = Physics.RaycastNonAlloc(origin, direction, ownerSkipHits, range, layers, QueryTriggerInteraction.Collide);
        RaycastHit[] results = ownerSkipHits;
        if (count == results.Length)
        {
            // A full NonAlloc buffer may omit the nearest wall. Preserve correctness under saturation.
            results = Physics.RaycastAll(origin, direction, range, layers, QueryTriggerInteraction.Collide);
            count = results.Length;
        }
        hit = default; float closest = float.PositiveInfinity; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var candidate = results[i];
            if (ShouldSkip(candidate, owner) || candidate.distance >= closest) continue;
            closest = candidate.distance; hit = candidate; found = true;
        }
        return found;
    }
}
