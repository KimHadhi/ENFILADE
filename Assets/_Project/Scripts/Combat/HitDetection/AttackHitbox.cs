using System.Collections.Generic;
using UnityEngine;

/// <summary>Animation events can call BeginAttack/EndAttack. Triggers need a Rigidbody on either participant.</summary>
[RequireComponent(typeof(Collider))]
public sealed class AttackHitbox : MonoBehaviour
{
    [SerializeField, Min(0f)] private float damage = 15f;
    [SerializeField] private GameObject owner;
    private long attackId;
    private readonly HashSet<IDamageReceiver> contacted = new HashSet<IDamageReceiver>();
    public void BeginAttack() { attackId = DamageHit.NewAttackId(); contacted.Clear(); }
    public void BeginAttackWithId(long sharedAttackId) { attackId = sharedAttackId > 0 ? sharedAttackId : 0; contacted.Clear(); }
    public void EndAttack() { attackId = 0; contacted.Clear(); }
    private void OnDisable() => EndAttack();
    private void OnTriggerEnter(Collider other) => TryHit(other);
    private void OnTriggerStay(Collider other) => TryHit(other);
    private void LateUpdate() => CheckDodgeTrails();
    public void CheckDodgeTrails()
    {
        if (attackId == 0 || !isActiveAndEnabled) return;
        var attackCollider = GetComponent<Collider>();
        for (int i = StepDodgeTrail.Active.Count - 1; i >= 0; i--)
        {
            if (i >= StepDodgeTrail.Active.Count) continue;
            var trail = StepDodgeTrail.Active[i];
            if (!trail.IsActive) { StepDodgeTrail.Active.RemoveAt(i); continue; }
            var receiver = trail.Receiver;
            if (receiver == null || contacted.Contains(receiver)
                || (owner != null && receiver.transform.IsChildOf(owner.transform)) || !trail.OverlapsAttack(attackCollider)) continue;
            var result = trail.Recognize(new DamageHit(attackId, damage, DamageKind.Attack,
                attackCollider.ClosestPoint(receiver.transform.position), transform.forward, owner));
            if (result == DamageResult.Evaded || result == DamageResult.Duplicate) contacted.Add(receiver);
        }
    }
    public DamageResult TryHit(Collider other)
    {
        if (attackId == 0 || other == null || (owner != null && other.transform.IsChildOf(owner.transform))) return DamageResult.Ignored;
        if (PlayerLeanHitboxes.Replaces(other)) return DamageResult.Ignored;
        var receiver = other.GetComponentInParent<IDamageReceiver>();
        if (receiver == null || !contacted.Add(receiver)) return DamageResult.Ignored;
        return receiver.ReceiveHit(new DamageHit(attackId, damage, DamageKind.Attack,
            other.ClosestPoint(transform.position), transform.forward, owner, true));
    }
}
