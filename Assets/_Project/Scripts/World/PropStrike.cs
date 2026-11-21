using UnityEngine;

/// <summary>
/// A loose physics object striking an enemy hard enough to hurt it, and whose blow that is (the user, 2026-11-21: "프롭
/// 같은 거 던졌을 때 맞춰서 그걸로 죽이면 ... 물리 프롭 처치"). Who it belongs to follows the Source engine's physics attacker:
/// the player who last set a prop moving (threw it, kicked or shoved it, shot it, walked it along) is its attacker for a few
/// seconds (PhysicsProp.SetAttacker), and a strike in that time is their hit, DamageMethod.Prop, which the kill feed and
/// the crosshair's kill picture name (KillMarks.Prop). How much it hurts is the prop's own motion: its kinetic energy as
/// it meets the enemy (½ m v², at the speed the two met), so a thrown fuel can floors a zombie, a soda can stings and a
/// marble does nothing. The blow's posture damage is the same share of it, so a hard one staggers as a shove does
/// (EnemyHitReaction). Numbers: Tables/Maps/PropStrike.csv, rows Strike.*.
/// Who judges a strike: the PC whose own player is the attacker, as every hit is judged by the PC that lands it, on its
/// own copy of the prop. A prop whose motion one PC runs for everyone (a fuel can, on the host: CampaignCans,
/// PhysicsProp.HostJudged) is judged there, for whoever set it moving, and marked JudgedByHost so that player's own PC
/// still shows the kill.
/// </summary>
public static class PropStrike
{
    public const string TableName = "PropStrike", Row = "Strike";

    /// <summary>Seconds a player stays a prop's attacker after setting it moving.</summary>
    public static float CreditSeconds => Mathf.Max(0f, GameTables.Number(TableName, Row, "creditSeconds", 3f));

    /// <summary>
    /// PhysicsProp.OnCollisionEnter: a strike on an enemy, if the prop has an attacker, this PC judges for them, and it met
    /// the enemy fast enough to hurt. False when nothing came of it.
    /// </summary>
    internal static bool Struck(PhysicsProp prop, Rigidbody body, Collision collision)
    {
        var attacker = prop != null ? prop.Attacker : null;
        if (attacker == null || body == null || collision == null || collision.collider == null) return false;
        var enemy = collision.collider.GetComponentInParent<EnemyHealth>();
        if (enemy == null || !enemy.IsAlive) return false;
        // The attacker's own PC judges, or the one PC that runs this prop for everyone.
        if (prop.HostJudged ? GameSession.IsGuest : !attacker.IsLocal) return false;
        float speed = collision.relativeVelocity.magnitude;
        if (speed < GameTables.Number(TableName, Row, "minSpeed", 3f) || !prop.MayStrike(enemy)) return false;
        float joules = .5f * body.mass * speed * speed;
        float damage = Mathf.Min(joules * GameTables.Number(TableName, Row, "damagePerJoule", .35f), GameTables.Number(TableName, Row, "maxDamage", 400f));
        if (damage < GameTables.Number(TableName, Row, "minDamage", 2f)) return false;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.worldCenterOfMass;
        // The way it was going as it met them, from the speed it carried into the contact.
        Vector3 going = prop.Going.sqrMagnitude > .01f ? prop.Going.normalized : (enemy.transform.position - body.position).normalized;
        var marks = attacker.IsLocal ? KillMarks.None : KillMarks.JudgedByHost;
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), damage, DamageKind.Attack, point, going, attacker.gameObject,
            canBeGuarded: false, postureDamage: damage * GameTables.Number(TableName, Row, "postureShare", 1f), method: DamageMethod.Prop,
            marks: marks));
        return true;
    }
}
