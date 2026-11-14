using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A DamageHit in a form that can cross the network. Who landed it travels apart, because a GameObject can't. It also
/// carries what the attacker's own PC knew as they landed it (their CHARGE, their Gun Kata: KillConditions.Attacker), so
/// the host can name it in the kill's flair (KillFlairs), and whether the bullet went through something on the way.
/// Sent to the host it also carries what the hit did on the attacker's own PC, where it was judged: the health it took
/// (the target's own share included: lying, a weak head, an execution's part) and whether it killed. The host takes
/// that as sent and works nothing out again (the user's rule of 2026-11-14: the acting player's PC decides and sends
/// only the result), so what the shooter's screen showed is what happened.
/// </summary>
public struct EnemyHitMessage : INetworkSerializeByMemcpy
{
    [Flags]
    private enum Marks : byte { None = 0, Guardable = 1, Finisher = 2, Critical = 4, Counter = 8, HasOrigin = 16, Pierced = 32, Killed = 64, Silent = 128 }

    private Vector3 point, direction, origin;
    private float amount, postureDamage, taken;
    private uint attacker;
    private byte kind, method, part;
    private Marks marks;

    /// <summary>The health the hit took where it was judged (0 on a message that only shows a hit).</summary>
    public float Taken => taken;
    /// <summary>It killed where it was judged.</summary>
    public bool Killed => Has(Marks.Killed);

    /// <summary>A hit as the attacker's own PC judged it, for the host: what it took there and whether it killed.</summary>
    public EnemyHitMessage(in DamageHit hit, float taken, bool killed) : this(hit)
    {
        this.taken = Mathf.Max(0f, taken);
        if (killed) marks |= Marks.Killed;
    }

    public EnemyHitMessage(in DamageHit hit)
    {
        point = hit.Point;
        direction = hit.Direction;
        origin = hit.Origin ?? Vector3.zero;
        amount = hit.Amount;
        postureDamage = hit.PostureDamage;
        taken = 0f;
        kind = (byte)hit.Kind;
        method = (byte)hit.Method;
        part = (byte)hit.Part;
        marks = Marks.None;
        if (hit.CanBeGuarded) marks |= Marks.Guardable;
        if (hit.IsComboFinisher) marks |= Marks.Finisher;
        if (hit.IsCritical) marks |= Marks.Critical;
        if (hit.IsCounter) marks |= Marks.Counter;
        if (hit.Origin.HasValue) marks |= Marks.HasOrigin;
        if (hit.Pierced) marks |= Marks.Pierced;
        if (hit.Silent) marks |= Marks.Silent;
        // What the hit already carried (a friend's, relayed by the host) and, for this PC's own player, their state now.
        attacker = (uint)(hit.Marks | KillConditions.Attacker(hit.Source));
    }

    public DamageHit ToHit(GameObject source) => new DamageHit(DamageHit.NewAttackId(), amount, (DamageKind)kind, point, direction, source,
        Has(Marks.Guardable), postureDamage, Has(Marks.Finisher), Has(Marks.Critical), Has(Marks.Counter), (DamageMethod)method,
        Has(Marks.HasOrigin) ? origin : (Vector3?)null, pierced: Has(Marks.Pierced), part: (BodyPart)part, marks: (KillMarks)attacker,
        silent: Has(Marks.Silent));

    private bool Has(Marks mark) => (marks & mark) != 0;
}
