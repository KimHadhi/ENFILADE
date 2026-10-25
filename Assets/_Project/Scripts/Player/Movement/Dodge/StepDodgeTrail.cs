using System.Collections.Generic;
using UnityEngine;

/// <summary>A reward-only swept body volume. Never a damage collider or a solid body.</summary>
public sealed class StepDodgeTrail
{
    internal static readonly List<StepDodgeTrail> Active = new List<StepDodgeTrail>();
    private readonly PlayerMovement ctx;
    private readonly List<Vector3> points = new List<Vector3>(48);
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private Vector3 tip, bottomOffset, spine;
    private float radius;
    private int activation;
    public PlayerDamageReceiver Receiver => ctx != null ? ctx.DamageReceiver : null;
    public bool IsActive => points.Count > 0 && ctx != null && ctx.isActiveAndEnabled && Receiver != null && Receiver.IsAlive
        && ctx.Abilities.Dodge.Kind == DodgeKind.Step && ctx.Abilities.Dodge.IsInvulnerable
        && ctx.Abilities.Dodge.Activation == activation;

    internal StepDodgeTrail(PlayerMovement player) { ctx = player; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => Active.Clear();
    internal void Begin()
    {
        Active.RemoveAll(trail => !trail.IsActive);
        Clear(); activation = ctx.Abilities.Dodge.Activation;
        var cc = ctx.Motor.controller;
        float scale = Mathf.Max(Mathf.Abs(ctx.transform.lossyScale.x), Mathf.Abs(ctx.transform.lossyScale.z));
        float bodyRadius = cc.radius * scale;
        radius = bodyRadius + ctx.AbilitySettings.stepDodgeRecognitionPadding;
        Vector3 center = ctx.transform.TransformPoint(cc.center);
        float halfSpine = Mathf.Max(0f, cc.height * Mathf.Abs(ctx.transform.lossyScale.y) * 0.5f - bodyRadius);
        bottomOffset = center - Vector3.up * halfSpine - ctx.transform.position;
        spine = Vector3.up * (2f * halfSpine);
        tip = ctx.transform.position + bottomOffset;
        points.Add(tip); Active.Add(this);
    }
    internal void Tick() { if (!IsActive) Clear(); }
    internal void RecordMovement()
    {
        if (!IsActive) { Clear(); return; }
        if (ctx.CurrentState != PlayerMovement.MovementState.StepDodge) return;
        tip = ctx.transform.position + bottomOffset;
        if ((tip - points[points.Count - 1]).sqrMagnitude >= 0.0064f) points.Add(tip);
    }
    internal void Clear() { points.Clear(); Active.Remove(this); }

    public bool OverlapsAttack(Collider attack)
    {
        if (!IsActive || attack == null || !attack.enabled || !attack.gameObject.activeInHierarchy) return false;
        int mask = 1 << attack.gameObject.layer;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 a = points[i], b = i + 1 < points.Count ? points[i + 1] : tip;
            int count = Physics.OverlapCapsuleNonAlloc(a, a + spine, radius, overlaps, mask, QueryTriggerInteraction.Collide);
            for (int j = 0; j < count; j++) if (overlaps[j] == attack) return true;
            Vector3 delta = b - a;
            if (delta.sqrMagnitude < 0.000001f) continue;
            count = Physics.CapsuleCastNonAlloc(a, a + spine, radius, delta.normalized, hits, delta.magnitude, mask, QueryTriggerInteraction.Collide);
            for (int j = 0; j < count; j++) if (hits[j].collider == attack) return true;
        }
        int endCount = Physics.OverlapCapsuleNonAlloc(tip, tip + spine, radius, overlaps, mask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < endCount; i++) if (overlaps[i] == attack) return true;
        return false;
    }

    // The segment must end at the first real impact. AttackTrace performs that
    // clipping; projectile integrations can pass their already-clipped sweep.
    public bool IntersectsSegment(Vector3 start, Vector3 end)
    {
        if (!IsActive) return false;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 a = points[i], b = i + 1 < points.Count ? points[i + 1] : tip;
            if (DodgeTrailGeometry.SegmentQuadDistanceSquared(start, end, a, a + spine, b + spine, b) <= radius * radius)
                return true;
        }
        return false;
    }

    internal DamageResult Recognize(DamageHit hit)
        => IsActive ? Receiver.ReceiveDodgeNearMiss(hit) : DamageResult.Ignored;

    public static void RecognizeAttackSegment(DamageHit hit, Vector3 start, Vector3 clippedEnd)
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (i >= Active.Count) continue;
            var trail = Active[i];
            if (!trail.IsActive) { Active.RemoveAt(i); continue; }
            if (hit.Source != null && trail.ctx.transform.IsChildOf(hit.Source.transform)) continue;
            if (trail.IntersectsSegment(start, clippedEnd)) trail.Recognize(hit);
        }
    }
}
