using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>A DamageHit in a form that can cross the network. Who landed it travels apart, because a GameObject can't.</summary>
public struct EnemyHitMessage : INetworkSerializeByMemcpy
{
    [Flags]
    private enum Marks : byte { None = 0, Guardable = 1, Finisher = 2, Critical = 4, Counter = 8, HasOrigin = 16 }

    private Vector3 point, direction, origin;
    private float amount, postureDamage;
    private byte kind, method;
    private Marks marks;

    public EnemyHitMessage(in DamageHit hit)
    {
        point = hit.Point;
        direction = hit.Direction;
        origin = hit.Origin ?? Vector3.zero;
        amount = hit.Amount;
        postureDamage = hit.PostureDamage;
        kind = (byte)hit.Kind;
        method = (byte)hit.Method;
        marks = Marks.None;
        if (hit.CanBeGuarded) marks |= Marks.Guardable;
        if (hit.IsComboFinisher) marks |= Marks.Finisher;
        if (hit.IsCritical) marks |= Marks.Critical;
        if (hit.IsCounter) marks |= Marks.Counter;
        if (hit.Origin.HasValue) marks |= Marks.HasOrigin;
    }

    public DamageHit ToHit(GameObject source) => new DamageHit(DamageHit.NewAttackId(), amount, (DamageKind)kind, point, direction, source,
        Has(Marks.Guardable), postureDamage, Has(Marks.Finisher), Has(Marks.Critical), Has(Marks.Counter), (DamageMethod)method,
        Has(Marks.HasOrigin) ? origin : (Vector3?)null);

    private bool Has(Marks mark) => (marks & mark) != 0;
}
