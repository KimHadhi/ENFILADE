using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeleeCombatTarget))]
public sealed class WeaponTrainingTarget : MonoBehaviour, IDamageReceiver, ICombatVitals, ICombatDisplayName
{
    [SerializeField, Min(1f)] private float maximumHealth = 1000f;
    [Tooltip("체력 바 위에 표시할 이름. 비워두면 오브젝트 이름을 사용한다.")]
    [SerializeField] private string displayName = "";
    public string DisplayName => displayName;
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    public float Health { get; private set; }
    public float MaximumHealth => Mathf.Max(1f, maximumHealth);
    private void Awake()
    {
        if (GetComponent<MeleeCombatTarget>() == null) gameObject.AddComponent<MeleeCombatTarget>();
        ResetTarget();
    }
    [ContextMenu("Reset Target")]
    public void ResetTarget() { Health = maximumHealth; seen.Clear(); recent.Clear(); GetComponent<MeleeCombatTarget>()?.ResetPosture(); }
    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (hit.Id <= 0 || hit.Amount <= 0f || Health <= 0f) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id); if (recent.Count > 256) seen.Remove(recent.Dequeue());
        float before = Health;
        Health = Mathf.Max(0f, Health - hit.Amount);
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth);
        return DamageResult.Damaged;
    }
}
