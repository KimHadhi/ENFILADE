using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A target on the hub's firing range (memo 46, "총을 쏴 볼 수 있는 사격장"): a steel target with a zombie's health, or a
/// thick steel plate. It takes a gun's hit as a zombie would (the head plate, marked by a WeaponHitRegion, takes the gun's
/// headshot multiplier; the same hit is never counted twice), so the damage numbers and markers show what that shot does
/// to a zombie. Its health is a normal zombie's (Tables/Hub): when a run of shots would have killed one it is knocked down
/// on its back, says so on the range board (RangeBoard), and its spring stands it up again full a moment later.
/// <para>
/// It is steel (2026-11-12, the user: "로비에 있는 트레이닝 더미는 타격음이 피 터지는 소리 말고 ㄹㅇ 쇠나 철로 된 타겟
/// 때리는 사운드"): its body is a SteelTarget built here at play (painted plates on a post that ring, swing, spark and
/// take lead splashes, a stand that is a miss when hit), so it shows and sounds its own hits (IOwnHitFeedback): no
/// flesh hit sounds and no red hit flash. The fists and a shove ring it as bullets do; bullets ring it through
/// IBulletStruck, whoever fired them.
/// </para>
/// <para>
/// It is a practice foe (ICombatFoe; the user, 2026-11-12: "퍼크 별로 잡았을 때 스택 얻어서 스킬 써 보고 싶은데 어려워서. 아
/// 시순트 표적 찍기도 못 하네"): the shot that knocks it down is a real killing blow, so everything the classes do with a
/// kill happens here as on a zombie (an Assault's adrenaline, a Ghost's Head Hunter and White Tiger, a Schießhund's
/// bounty with its Desperado rounds and BOUNTY, the kill marker and sound), its hits charge the ultimate as an enemy's do,
/// and F marks it, a Schießhund's bounty mark too. Being practice keeps it out of the run: no kill feed line, money, XP,
/// streak, stats, records or results (GunFeelDirector and the counters skip practice kills). Every PC shows its own shots
/// on it; a friend's shots are traced here without hurting it (AttackTrace, share 0), as on any copy, so a target is
/// knocked down by this PC's own player alone (a friend's shots still ring and swing it). Another PC's mark on it finds
/// this PC's copy by where it stood as the hub began (Home).
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class RangeTarget : MonoBehaviour, IDamageReceiver, ICombatVitals, ICombatDisplayName, ICombatFoe, IOwnHitFeedback
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

    // The knockdown: over onto its back, a while there, then its spring stands it up (s, degrees).
    private const float FallSeconds = .22f, RiseSeconds = .3f, LieAngle = 85f;

    /// <summary>This PC's player hit a range target.</summary>
    public static event Action<Shot> Struck;

    [SerializeField, Tooltip("Texts id of its name (강철 과녁, 두꺼운 강철판).")]
    private string nameId = "hub.range.dummy";
    [SerializeField, Tooltip("A zombie's target: its health is a normal zombie's (Hub.csv dummyHealth), shaped as RangeTargets.csv Zombie. Off, a thick plate (plateHealth, row Plate).")]
    private bool zombie = true;
    [SerializeField, Tooltip("The axis it is knocked down about, on its own axes; it goes over away from the shooter.")]
    private Vector3 tipAxis = Vector3.right;

    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private SteelTarget look;
    private float downAt = -1f, maximum = 100f, tipSign = 1f;
    private bool landed;

    public float Health { get; private set; }
    public float MaximumHealth => Mathf.Max(1f, maximum);
    public string DisplayName => Texts.Get(nameId);
    /// <summary>Knocked down until it stands up again.</summary>
    public bool IsDown => downAt >= 0f;
    /// <summary>Standing with health left: it can be knocked down (ICombatFoe), and a mark on it holds.</summary>
    public bool IsAlive => !IsDown && Health > 0f;
    /// <summary>Always practice: its kills are the classes' alone (ICombatFoe).</summary>
    public bool IsPractice => true;
    /// <summary>The thick steel plate rather than the zombie dummy: an elite's grade to a Schießhund's mark (PlayerClass.Grade).</summary>
    public bool IsPlate => !zombie;
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
        // Read before any rail moves it (RangeMover moves from its first Update on).
        Home = transform.position;
        maximum = HubLobby.Number(zombie ? "dummyHealth" : "plateHealth", zombie ? 100f : 300f);
        Health = MaximumHealth;
        // Its steel body, made here so a hub built before it (wooden dummies) needs no rebuild.
        look = gameObject.AddComponent<SteelTarget>();
        look.Build(zombie, tipAxis);
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
        UltimateCharge.AwardEnemyHit(hit, before - Health);
        // Its true health, down to zero on the shot that knocks it down: that is a killing blow, as on a zombie, and the
        // classes' kill hooks take it (ClassKit, ClassCards). Being practice (IsPractice) keeps it off the kill feed,
        // the money and the counts (GunFeelDirector).
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth);
        // A bullet rings it on its own way (IBulletStruck, from the gun's impact); the fists and a shove ring it here.
        if (hit.Method != DamageMethod.Bullet && look != null) look.Struck(hit.Point, hit.Direction, hit.Method == DamageMethod.Shove);
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
        landed = false;
        Vector3 axis = transform.TransformDirection(tipAxis);
        tipSign = Vector3.Dot(Vector3.Cross(axis, Vector3.up), along) >= 0f ? 1f : -1f;
    }

    private void Update()
    {
        if (downAt < 0f) return;
        float since = Time.time - downAt, stay = HubLobby.Number("targetDownSeconds", 2f);
        float angle = since < FallSeconds ? Mathf.SmoothStep(0f, LieAngle, since / FallSeconds)
            : since < stay ? LieAngle : Mathf.SmoothStep(LieAngle, 0f, (since - stay) / RiseSeconds);
        if (look != null) look.Tip(angle * tipSign);
        if (!landed && since >= FallSeconds) { landed = true; if (look != null) look.Landed(); }
        if (since < stay + RiseSeconds) return;
        if (look != null) look.Stood(tipSign);
        downAt = -1f;
        Health = MaximumHealth;
    }

    // Where its plates will stand, in the scene view before Play builds them.
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) SteelTarget.DrawGizmo(transform, zombie);
    }
}
