using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional playground receiver; production enemies can implement the same two interfaces.</summary>
[RequireComponent(typeof(MeleeCombatTarget))]
public sealed class MovementTrainingTarget : MonoBehaviour, IDamageReceiver, IShoveReceiver, ICombatVitals
{
    public float Health { get; private set; } = 100f;
    public float MaximumHealth => 100f;
    private readonly HashSet<long> attacks = new HashSet<long>();
    private void Awake() { if (GetComponent<MeleeCombatTarget>() == null) gameObject.AddComponent<MeleeCombatTarget>(); }
    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (!attacks.Add(hit.Id)) return DamageResult.Duplicate;
        Health = Mathf.Max(0f, Health - hit.Amount);
        if (attacks.Count > 256) attacks.Clear();
        return DamageResult.Damaged;
    }
    public void ReceiveShove(Vector3 impulse, Vector3 point)
    {
        var body = GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
    }
}
