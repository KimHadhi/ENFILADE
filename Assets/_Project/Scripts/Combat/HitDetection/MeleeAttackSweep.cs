using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Reusable melee window. World surfaces report contact without blocking targets or consuming their budget.</summary>
public sealed class MeleeAttackSweep
{
    private readonly AimSweepWindow window;
    private readonly HashSet<UnityEngine.Object> contacted = new HashSet<UnityEngine.Object>();
    private readonly List<Contact> contacts = new List<Contact>();
    private struct Contact { public Collider collider; public Vector3 point; public float distance; }
    private PlayerMovement player;
    private Transform aim;
    private float damage, posture, reach, radius;
    private int maxTargets, hitCount;
    private LayerMask layers;
    private long attackId;
    private Action onDamage, onWorldHit;
    private bool worldHit;
    public bool IsActive => window.IsActive;
    public MeleeAttackSweep() { window = new AimSweepWindow(Sample); }
    public void Begin(PlayerMovement owner, Transform view, float damage, float posture, float reach,
        float radius, float duration, int maxTargets, LayerMask layers, Action onDamage = null, Action onWorldHit = null)
    {
        Cancel(); player = owner; aim = view; this.damage = damage; this.posture = posture;
        this.reach = Mathf.Max(.1f, reach); this.radius = Mathf.Max(.01f, radius);
        this.maxTargets = Mathf.Max(1, maxTargets); this.layers = layers; this.onDamage = onDamage; this.onWorldHit = onWorldHit;
        hitCount = 0; worldHit = false; contacted.Clear(); attackId = DamageHit.NewAttackId();
        window.Begin(aim, duration);
    }
    public void Tick(float dt) { window.Tick(dt, aim); }
    public void Cancel() { window.Cancel(); }
    private void Sample(Vector3 origin, Vector3 forward)
    {
        if (player == null || player.CombatInputLocked) { Cancel(); return; }
        contacts.Clear();
        foreach (var hit in Physics.SphereCastAll(origin, radius, forward, reach, layers, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= 0f) continue; // Initial overlaps are resolved explicitly below.
            contacts.Add(new Contact { collider = hit.collider, point = hit.point, distance = hit.distance });
        }
        foreach (var collider in Physics.OverlapSphere(origin, radius, layers, QueryTriggerInteraction.Ignore))
        {
            if (!ShoveSweep.TryGetContactPoint(collider, origin, forward, radius, 0f, out var point)) continue;
            Vector3 delta = point - origin;
            if (delta.sqrMagnitude < .0001f) delta = collider.bounds.center - origin;
            if (Vector3.Dot(delta, forward) < -.01f) continue;
            contacts.Add(new Contact { collider = collider, point = point, distance = 0f });
        }
        contacts.Sort((a, b) => a.distance.CompareTo(b.distance));
        foreach (var contact in contacts)
        {
            var collider = contact.collider;
            if (collider.transform.IsChildOf(player.transform)) continue;
            var receiver = collider.GetComponentInParent<IDamageReceiver>();
            var identity = receiver as UnityEngine.Object;
            if (identity != null && contacted.Contains(identity)) continue;
            if (receiver == null)
            {
                if (!worldHit) { worldHit = true; onWorldHit?.Invoke(); }
                continue;
            }
            contacted.Add(identity);
            var result = receiver.ReceiveHit(new DamageHit(attackId, damage, DamageKind.Attack,
                contact.point, forward, player.gameObject, true, posture));
            if (result == DamageResult.Blocked || result == DamageResult.Parried) { Cancel(); break; }
            if (result != DamageResult.Damaged) continue;
            collider.GetComponentInParent<MeleeCombatTarget>()?.ApplyPostureDamage(posture);
            hitCount++; onDamage?.Invoke();
            if (hitCount >= maxTargets) { Cancel(); break; }
        }
    }
}
