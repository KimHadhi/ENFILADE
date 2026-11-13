using UnityEngine;

// ClassCards: Assault: the phantom cavalry. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    /// <summary>
    /// This PC's Assault sounded a CHARGE at <paramref name="at"/> (ClassKit): with the final card, the riders go out
    /// along where they look.
    /// </summary>
    public void Charged(Vector3 at)
    {
        if (!PlayerClass.Is(PlayerClass.Assault) || !PlayerPerks.Has(PlayerPerks.AssaultCavalry)) return;
        Cavalry(at);
    }

    private void Cavalry(Vector3 at)
    {
        var view = Camera.main;
        Vector3 way = Vector3.ProjectOnPlane(view != null ? view.transform.forward : transform.forward, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = Vector3.forward;
        rideWay = way.normalized;
        rideFrom = at;
        rideSince = Time.time;
        riding = true;
        ridden.Clear();
        CardNet.Show(CardFx.Kind.Cavalry, at, rideWay, Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "cavalryReach", 24f)));
    }

    // The riders' front sweeps on; whatever it reaches in its lane is hit once and thrown aside.
    private void Ride()
    {
        if (!riding) return;
        float reach = Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "cavalryReach", 24f));
        float half = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "cavalryWidth", 4.5f)) * .5f;
        float front = Mathf.Max(0f, Time.time - rideSince - CardFx.CavalryIn) * CardFx.CavalrySpeed;
        if (front >= reach + 1f) riding = false;
        Vector3 aside = Vector3.Cross(Vector3.up, rideWay);
        float damage = Mathf.Max(0f, PlayerPerks.Total(PlayerPerks.AssaultCavalry));
        if (damage <= 0f) damage = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "cavalryDamage", 80f));
        float down = Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "cavalryKnockdown", 2f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || ridden.Contains(enemy)) continue;
            Vector3 off = enemy.transform.position - rideFrom;
            float along = Vector3.Dot(off, rideWay), lateral = Vector3.Dot(off, aside);
            if (along < -.5f || along > Mathf.Min(front, reach) + .5f || Mathf.Abs(lateral) > half || Mathf.Abs(off.y) > 3f) continue;
            near.Add(enemy);
        }
        foreach (var enemy in near)
        {
            ridden.Add(enemy);
            float lateral = Vector3.Dot(enemy.transform.position - rideFrom, aside);
            Vector3 thrown = rideWay * 7f + aside * (lateral >= 0f ? 4f : -4f);
            Strike(enemy, damage, enemy.transform.position + Vector3.up, rideWay, 60f);
            if (!enemy.IsAlive || !enemy.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable) continue;
            if (target.Steadfast) target.Interrupt(1f);
            else target.Barge(thrown, .35f, down);
        }
    }

    // A card's own blow on an enemy, this player's (its kill and its money are theirs, as a punch's are).
    private void Strike(EnemyHealth enemy, float amount, Vector3 point, Vector3 direction, float posture)
    {
        if (amount <= 0f || enemy == null || !enemy.IsAlive) return;
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, point, direction, gameObject,
            false, posture, true));
    }
}
