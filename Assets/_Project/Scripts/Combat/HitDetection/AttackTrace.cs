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
    // Transforms reach physics only when someone syncs them (the project's Auto Sync Transforms is off). A zombie's bone
    // hitboxes move with its animation after the frame's scripts, so a bullet fired before anything synced this frame
    // met them where they had been a frame earlier still, off what was on screen by a running zombie's stride (user,
    // 2026-11-11 playtest: "히트 박스 좀 아쉬운 듯"). Once a frame, before the first bullet, they catch up with the picture.
    private static int syncedFrame = -1;
    private static void SyncBodies()
    {
        if (syncedFrame == Time.frameCount) return;
        syncedFrame = Time.frameCount;
        Physics.SyncTransforms();
    }

    public static AttackTraceResult Bullet(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers,
        float headshotMultiplier, float penetration, bool hurts, out BulletLeg[] later)
    {
        later = null;
        if (direction.sqrMagnitude < 0.000001f || distance <= 0f)
            return new AttackTraceResult(false, default, origin, DamageResult.Ignored);
        SyncBodies();
        var random = BulletPenetration.RandomFor(origin, direction);
        direction.Normalize();
        Vector3 shooter = attack.Origin ?? origin;
        hurt.Clear();
        // A Ghost's over-penetration card (과관통) carries this PC's own bullets further through bodies and walls.
        if (hurts) penetration *= PlayerPerks.PierceFactor(attack);
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

    // One straight stretch to the first thing it meets that this bullet hasn't met yet (`already`: what it passes unseen,
    // so a body it went through, whatever its colliders, or one it starts inside isn't met twice), hurting that by `share`
    // of the attack; share 0 hurts nothing but still counts the meeting, so a shot another PC judged takes the same path.
    // `pierced`: the bullet got here through or off something.
    private static AttackTraceResult Strike(DamageHit attack, Vector3 origin, Vector3 direction, float distance, int layers,
        float headshotMultiplier, float share, Vector3 shooter, System.Collections.Generic.HashSet<IDamageReceiver> already, bool pierced)
    {
        Vector3 end = origin + direction * distance;
        DamageResult result = DamageResult.Ignored;
        bool headshot = false;
        bool hasHit = FirstExternalHit(attack.Source, origin, direction, distance, layers, already, out RaycastHit hit);
        if (hasHit)
        {
            end = hit.point;
            var receiver = hit.collider.GetComponentInParent<IDamageReceiver>();
            headshot = headshotMultiplier > 1f && WeaponHitRegion.IsHead(hit);
            bool fresh = receiver != null && (already == null || already.Add(receiver));
            // This PC's own shooter carries their gun perks (PlayerPerks); anyone else's shots are as they are.
            // ...and their class: its own guns' bonus, an Assault's adrenaline, a CHARGE they are in, a Ghost's stacks, a
            // Schießhund's mark and Desperado (PlayerClass), whose round in its mark's head ends it (Finish).
            bool mine = attack.Method == DamageMethod.Bullet && PlayerPerks.IsMine(attack.Source);
            float perk = mine ? PlayerPerks.GunFactor(headshot) * PlayerClass.GunFactor(headshot, receiver as Component) : 1f;
            float amount = attack.Amount * share * (headshot ? headshotMultiplier : 1f) * perk;
            if (mine && fresh && share > 0f) amount = PlayerClass.Finish(amount, headshot, receiver, hit.point, shooter);
            if (fresh && share > 0f)
                result = receiver.ReceiveHit(new DamageHit(attack.Id, amount, attack.Kind, hit.point, direction, attack.Source,
                    attack.CanBeGuarded, attack.PostureDamage * share, attack.IsComboFinisher, attack.IsCritical || headshot, attack.IsCounter, attack.Method,
                    shooter, attack.IsFatal, attack.Pierced || pierced, ZombieGore.PartOf(hit.collider)));
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
    // A zombie's capsule lets shots through to its bone hitboxes (triggers), which take them instead (ZombieBody).
    private static bool ShouldSkip(RaycastHit hit, GameObject owner)
        => IsOwner(hit, owner) || PlayerLeanHitboxes.Replaces(hit.collider) || ZombieBody.Replaces(hit.collider)
            || IsDeadPlayer(hit) || GoreGib.IsPiece(hit.collider) || DroppedMagazine.IsMagazine(hit.collider) || ThrownStone.IsStone(hit.collider)
            || Throwables.Passes(hit.collider)
            || (hit.collider.isTrigger && !ZombieBody.IsHitbox(hit.collider) && !CampaignCanTarget.Is(hit.collider)
                && (hit.collider.GetComponent<PlayerCombatHitbox>() == null
                || hit.collider.GetComponentInParent<PlayerLeanHitboxes>()?.UsesSplitBody != true));

    public static bool FirstExternalHit(GameObject owner, Vector3 origin, Vector3 direction, float range, int layers, out RaycastHit hit)
        => FirstExternalHit(owner, origin, direction, range, layers, null, out hit);

    // A bullet's own query: also passes anything that takes damage in `passed` (what it already went through).
    private static bool Passes(RaycastHit hit, GameObject owner, System.Collections.Generic.HashSet<IDamageReceiver> passed)
    {
        if (ShouldSkip(hit, owner)) return true;
        if (passed == null || passed.Count == 0) return false;
        var receiver = hit.collider.GetComponentInParent<IDamageReceiver>();
        return receiver != null && passed.Contains(receiver);
    }

    private static bool FirstExternalHit(GameObject owner, Vector3 origin, Vector3 direction, float range, int layers,
        System.Collections.Generic.HashSet<IDamageReceiver> passed, out RaycastHit hit)
    {
        if (!Physics.Raycast(origin, direction, out hit, range, layers, QueryTriggerInteraction.Collide)) return false;
        if (!Passes(hit, owner, passed)) return true;
        // Most shots need one non-allocating Raycast. Only own-body obstruction, or a body the bullet already went
        // through, needs a multi-hit query.
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
            if (Passes(candidate, owner, passed) || candidate.distance >= closest) continue;
            closest = candidate.distance; hit = candidate; found = true;
        }
        return found;
    }
}
