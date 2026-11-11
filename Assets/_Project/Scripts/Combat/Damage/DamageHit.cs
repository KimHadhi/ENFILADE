using System.Threading;
using UnityEngine;

public enum DamageKind { Attack, Fall, Execution }
/// <summary>Countered: a grab or a leap met by a timed shove (ShoveCounter).</summary>
public enum DamageResult { Ignored, Duplicate, Evaded, Damaged, Blocked, Parried, Invulnerable, Countered }
/// <summary>How the hit was delivered. Presentation only (hit and kill sounds). Slam: a brute's blow that throws the player (PlayerHold).
/// Thrown: a throwing knife (ThrownKnife), named in the kill feed, its kill of an unaware zombie a takedown (GunFeelDirector).</summary>
public enum DamageMethod { Other, Bullet, Shove, Grab, Feed, Pounce, Slam, Thrown }

/// <summary>One ID per attack activation, shared by all of its hitboxes.</summary>
public readonly struct DamageHit
{
    private static long nextId;
    public static long NewAttackId() => Interlocked.Increment(ref nextId);
    public readonly long Id;
    public readonly float Amount;
    public readonly DamageKind Kind;
    public readonly Vector3 Point, Direction;
    /// <summary>World-space attack origin (muzzle/launch position), captured when fired. Null for legacy/contact hits.</summary>
    public readonly Vector3? Origin;
    public readonly GameObject Source;
    public readonly bool CanBeGuarded;
    public readonly float PostureDamage;
    public readonly bool IsComboFinisher;
    /// <summary>Critical hit (currently: weapon headshots). Presentation only; Amount already includes any multiplier.</summary>
    public readonly bool IsCritical;
    /// <summary>Perfect-defense counter attack. Presentation only.</summary>
    public readonly bool IsCounter;
    public readonly DamageMethod Method;
    /// <summary>On a player: kills where it lands, whatever health and shield are left, with no going down first. A dodge or parry still beats it.</summary>
    public readonly bool IsFatal;
    /// <summary>
    /// A shambler's grab, a kneel to eat or a runner's leap taking hold: it hurts nothing by itself (PlayerHold's bites
    /// do), so it draws no blood and no hurt sound or look on anyone's screen (user, 2026-11-11 playtest: "잡혔을 때도
    /// 맞은 것처럼 피가 나오는 게 아쉬움").
    /// </summary>
    public bool TakesHold => Method == DamageMethod.Grab || Method == DamageMethod.Feed || Method == DamageMethod.Pounce;
    /// <summary>A bullet that reached this target after going through or glancing off something on the way (AttackTrace). Presentation and score only.</summary>
    public readonly bool Pierced;
    /// <summary>The body part a bullet met (a zombie's bone hitbox), for gore. Presentation only.</summary>
    public readonly BodyPart Part;
    public DamageHit(long id, float amount, DamageKind kind, Vector3 point, Vector3 direction, GameObject source = null,
        bool canBeGuarded = false, float postureDamage = 0f, bool isComboFinisher = true, bool isCritical = false, bool isCounter = false,
        DamageMethod method = DamageMethod.Other, Vector3? origin = null, bool isFatal = false, bool pierced = false,
        BodyPart part = BodyPart.None)
    { Id = id; Amount = amount; Kind = kind; Point = point; Direction = direction; Source = source;
        CanBeGuarded = canBeGuarded; PostureDamage = postureDamage; IsComboFinisher = isComboFinisher; IsCritical = isCritical; IsCounter = isCounter;
        Method = method; Origin = origin; IsFatal = isFatal; Pierced = pierced; Part = part; }
}

public interface IDamageReceiver { DamageResult ReceiveHit(DamageHit hit); }
public interface ICombatVitals { float Health { get; } float MaximumHealth { get; } }
/// <summary>Optional name shown above an enemy's health bar. Without it the object name is used.</summary>
public interface ICombatDisplayName { string DisplayName { get; } }
public interface IShoveReceiver { void ReceiveShove(Vector3 impulse, Vector3 point); }
