using UnityEngine;

// ClassCards: Assault: the phantom cavalry. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    // How far this ride's riders sweep (m): the card's own value at its level, kept as the ride starts.
    private float rideReach = 24f;

    /// <summary>
    /// This PC's Assault sounded a CHARGE at <paramref name="at"/> (ClassKit): with the 러프 라이더스 card, the riders go
    /// out along where they look, at every CHARGE (the class key's, whenever the adrenaline is full; not the ultimate's
    /// alone). Its levels send them farther (12/18/24 m: the card's value; the user, 2026-11-14, of the damage it used to
    /// raise: "궁극기 피해량을 늘려 봤자 뭐 해?"), and every rider hits as hard (Assault.cavalryDamage).
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
        // The reach goes with the look, so every PC's wedge slides as far as these riders sweep (CardFx Cavalry).
        rideReach = Mathf.Max(1f, PlayerPerks.Total(PlayerPerks.AssaultCavalry));
        CardNet.Show(CardFx.Kind.Cavalry, at, rideWay, rideReach);
    }

    // The riders' front sweeps on; whatever it reaches in its lane is hit once and thrown aside.
    private void Ride()
    {
        if (!riding) return;
        float reach = rideReach;
        float half = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "cavalryWidth", 4.5f)) * .5f;
        float front = Mathf.Max(0f, Time.time - rideSince - CardFx.CavalryIn) * CardFx.CavalrySpeed;
        if (front >= reach + 1f) riding = false;
        Vector3 aside = Vector3.Cross(Vector3.up, rideWay);
        float damage = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "cavalryDamage", 80f));
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
