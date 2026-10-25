using System.Threading;
using UnityEngine;

public enum DamageKind { Attack, Fall, Execution }
public enum DamageResult { Ignored, Duplicate, Evaded, Damaged, Blocked, Parried, Invulnerable }
/// <summary>How the hit was delivered. Presentation only (hit and kill sounds).</summary>
public enum DamageMethod { Other, Bullet, Shove }

/// <summary>One ID per attack activation, shared by all of its hitboxes.</summary>
public readonly struct DamageHit
{
    private static long nextId;
    public static long NewAttackId() => Interlocked.Increment(ref nextId);
    public readonly long Id;
    public readonly float Amount;
    public readonly DamageKind Kind;
    public readonly Vector3 Point, Direction;
    public readonly GameObject Source;
    public readonly bool CanBeGuarded;
    public readonly float PostureDamage;
    public readonly bool IsComboFinisher;
    /// <summary>Critical hit (currently: weapon headshots). Presentation only; Amount already includes any multiplier.</summary>
    public readonly bool IsCritical;
    /// <summary>Perfect-defense counter attack. Presentation only.</summary>
    public readonly bool IsCounter;
    public readonly DamageMethod Method;
    public DamageHit(long id, float amount, DamageKind kind, Vector3 point, Vector3 direction, GameObject source = null,
        bool canBeGuarded = false, float postureDamage = 0f, bool isComboFinisher = true, bool isCritical = false, bool isCounter = false,
        DamageMethod method = DamageMethod.Other)
    { Id = id; Amount = amount; Kind = kind; Point = point; Direction = direction; Source = source;
        CanBeGuarded = canBeGuarded; PostureDamage = postureDamage; IsComboFinisher = isComboFinisher; IsCritical = isCritical; IsCounter = isCounter;
        Method = method; }
}

public interface IDamageReceiver { DamageResult ReceiveHit(DamageHit hit); }
public interface ICombatVitals { float Health { get; } float MaximumHealth { get; } }
/// <summary>Optional name shown above an enemy's health bar. Without it the object name is used.</summary>
public interface ICombatDisplayName { string DisplayName { get; } }
public interface IShoveReceiver { void ReceiveShove(Vector3 impulse, Vector3 point); }
