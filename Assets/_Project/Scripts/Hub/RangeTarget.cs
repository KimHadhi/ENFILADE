using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A target on the hub's firing range (memo 46, "총을 쏴 볼 수 있는 사격장"): a zombie dummy or a steel plate. It takes a
/// gun's hit as a zombie would (the head, marked by a WeaponHitRegion, takes the gun's headshot multiplier; the same hit
/// is never counted twice), so the damage numbers and markers show what that shot does to a zombie. Its health is a
/// normal zombie's (Tables/Hub): when a run of shots would have killed one it tips over, says so on the range board
/// (RangeBoard), and stands up again full a moment later.
/// <para>
/// It is a practice foe (ICombatFoe; the user, 2026-11-12: "퍼크 별로 잡았을 때 스택 얻어서 스킬 써 보고 싶은데 어려워서. 아
/// 시순트 표적 찍기도 못 하네"): the shot that tips it over is a real killing blow, so everything the classes do with a kill
/// happens here as on a zombie (an Assault's adrenaline, a Ghost's Head Hunter and White Tiger, a Schießhund's bounty with
/// its Desperado rounds and BOUNTY, the kill marker and sound), its hits charge the ultimate as an enemy's do, and F marks
/// it, a Schießhund's bounty mark too. Being practice keeps it out of the run: no kill feed line, money, XP, streak,
/// stats, records or results (GunFeelDirector and the counters skip practice kills). Every PC shows its own shots on it;
/// a friend's shots are traced here without hurting it (AttackTrace, share 0), as on any copy, so a target is felled by
/// this PC's own player alone. Another PC's mark on it finds this PC's copy by where it stood as the hub began (Home).
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class RangeTarget : MonoBehaviour, IDamageReceiver, ICombatVitals, ICombatDisplayName, ICombatFoe
{
    /// <summary>What one hit on a range target did, for the range board.</summary>
    public readonly struct Shot
    {
        public readonly RangeTarget Target;
        public readonly float Damage, Distance;
        public readonly bool Headshot, Downed;
        public Shot(RangeTarget target, float damage, float distance, bool headshot, bool downed)
        { Target = target; Damage = damage; Distance = distance; Headshot = headshot; Downed = downed; }
    }

    /// <summary>This PC's player hit a range target.</summary>
    public static event Action<Shot> Struck;

    [SerializeField, Tooltip("Texts id of its name (좀비 더미, 강철판).")]
    private string nameId = "hub.range.dummy";
    [SerializeField, Tooltip("A zombie dummy: its health is a normal zombie's (Hub.csv dummyHealth). Off, a steel plate (plateHealth).")]
    private bool zombie = true;
    [SerializeField, Tooltip("The axis it tips over about, on its own axes; it falls away from the shooter.")]
    private Vector3 tipAxis = Vector3.right;

    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private Quaternion standing;
    private float downAt = -1f, maximum = 100f, tipSign = 1f;

    public float Health { get; private set; }
    public float MaximumHealth => Mathf.Max(1f, maximum);
    public string DisplayName => Texts.Get(nameId);
    /// <summary>Lying over until it stands up again.</summary>
    public bool IsDown => downAt >= 0f;
    /// <summary>Standing with health left: it can be hit down (ICombatFoe), and a mark on it holds.</summary>
    public bool IsAlive => !IsDown && Health > 0f;
    /// <summary>Always practice: its kills are the classes' alone (ICombatFoe).</summary>
    public bool IsPractice => true;
    /// <summary>Where it stood as the hub began (a moving one's rail start), the same on every PC.</summary>
    public Vector3 Home { get; private set; }

    // Every target in the hub, for finding the one another PC means (Nearest).
    private static readonly List<RangeTarget> all = new List<RangeTarget>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Struck = null;
        all.Clear();
    }

    /// <summary>
    /// This PC's copy of the target another PC's player means by <paramref name="home"/> (its Home there): the scene is
    /// the same on every PC, so it is the one that stood there, within a step. Null if none did.
    /// </summary>
    public static RangeTarget Nearest(Vector3 home)
    {
        RangeTarget found = null;
        float best = 1f;
        foreach (var target in all)
        {
            if (target == null) continue;
            float far = (target.Home - home).sqrMagnitude;
            if (far < best) { best = far; found = target; }
        }
        return found;
    }

    private void Awake()
    {
        standing = transform.localRotation;
        // Read before any rail moves it (RangeMover moves from its first Update on).
        Home = transform.position;
        maximum = HubLobby.Number(zombie ? "dummyHealth" : "plateHealth", zombie ? 100f : 300f);
        Health = MaximumHealth;
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (hit.Id <= 0 || hit.Amount <= 0f || float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount) || IsDown) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        float before = Health;
        Health = Mathf.Max(0f, Health - hit.Amount);
        bool downed = Health <= 0f;
        // As an enemy's hit does (EnemyHealth): the ultimate charges, so its skills can be tried here.
        UltimateCharge.AwardEnemyHit(hit);
        // Its true health, down to zero on the shot that fells it: that is a killing blow, as on a zombie, and the
        // classes' kill hooks take it (ClassKit, ClassCards). Being practice (IsPractice) keeps it off the kill feed,
        // the money and the counts (GunFeelDirector).
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth);
        var shooter = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        if (shooter != null && shooter.IsLocal)
        {
            float distance = hit.Point != Vector3.zero ? Vector3.Distance(hit.Origin ?? hit.Source.transform.position, hit.Point) : 0f;
            Struck?.Invoke(new Shot(this, hit.Amount, distance, hit.IsCritical && hit.Method == DamageMethod.Bullet, downed));
        }
        if (downed) Fall(hit.Direction);
        return DamageResult.Damaged;
    }

    // Over it goes, away from the shot: a turn about the tip axis carries its top along Cross(axis, up).
    private void Fall(Vector3 along)
    {
        downAt = Time.time;
        Vector3 axis = transform.TransformDirection(tipAxis);
        tipSign = Vector3.Dot(Vector3.Cross(axis, Vector3.up), along) >= 0f ? 1f : -1f;
    }

    private void Update()
    {
        if (downAt < 0f) return;
        float since = Time.time - downAt, stay = HubLobby.Number("targetDownSeconds", 2f), rise = .3f;
        float angle = since < .22f ? Mathf.SmoothStep(0f, 85f, since / .22f)
            : since < stay ? 85f : Mathf.SmoothStep(85f, 0f, (since - stay) / rise);
        transform.localRotation = standing * Quaternion.AngleAxis(angle * tipSign, tipAxis);
        if (since < stay + rise) return;
        transform.localRotation = standing;
        downAt = -1f;
        Health = MaximumHealth;
    }
}
