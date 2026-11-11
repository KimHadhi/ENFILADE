using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A target on the hub's firing range (memo 46, "총을 쏴 볼 수 있는 사격장"): a zombie dummy or a steel plate. It takes a
/// gun's hit as a zombie would (the head, marked by a WeaponHitRegion, takes the gun's headshot multiplier; the same hit
/// is never counted twice), so the damage numbers and markers show what that shot does to a zombie. Its health is a
/// normal zombie's (Tables/Hub): when a run of shots would have killed one it tips over, says so on the range board
/// (RangeBoard), and stands up again full a moment later. It is practice only: it never reports a killing blow, so it
/// pays no money, counts no kill, charges no ultimate and stays out of the chapter's results. Every PC shows its own
/// shots on it; a friend's shots show their marks but hurt nothing here, as on any copy.
/// </summary>
[DisallowMultipleComponent]
public sealed class RangeTarget : MonoBehaviour, IDamageReceiver, ICombatVitals, ICombatDisplayName
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Struck = null;

    private void Awake()
    {
        standing = transform.localRotation;
        maximum = HubLobby.Number(zombie ? "dummyHealth" : "plateHealth", zombie ? 100f : 300f);
        Health = MaximumHealth;
    }

    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (hit.Id <= 0 || hit.Amount <= 0f || float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount) || IsDown) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        float before = Health;
        Health = Mathf.Max(0f, Health - hit.Amount);
        bool downed = Health <= 0f;
        // Never down to zero in the report: a practice target's "kill" must not count as one (GunFeelDirector pays and
        // counts every killing blow a player lands).
        CombatDamageEvents.Report(this, hit, before, Mathf.Max(.01f, Health), MaximumHealth);
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
