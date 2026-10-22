using System.Collections.Generic;
using UnityEngine;

/// <summary>A single frontal shove hits each actor once. Other actors do not shield the crowd; solid cover does.</summary>
public static class ShoveSweep
{
    public static int Apply(PlayerMovement player, Transform aim, float damage, LayerMask layers)
    {
        if (player == null || aim == null || player.CombatInputLocked) return 0;
        var settings = player.AbilitySettings;
        Vector3 origin = aim.position, forward = aim.forward;
        float reach = Mathf.Max(.1f, settings.shoveSweepReach);
        float cone = Mathf.Cos(Mathf.Clamp(settings.shoveSweepAngle, 1f, 179f) * .5f * Mathf.Deg2Rad);
        long attackId = DamageHit.NewAttackId();
        var contacted = new HashSet<Object>();
        int count = 0;
        bool hitEnemy = false, hitWorld = false;
        foreach (var collider in Physics.OverlapSphere(origin, reach, layers, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(player.transform)) continue;
            var posture = collider.GetComponentInParent<MeleeCombatTarget>();
            var receiver = collider.GetComponentInParent<IDamageReceiver>();
            var shove = posture != null ? posture : collider.GetComponentInParent<IShoveReceiver>();
            var body = collider.attachedRigidbody;
            Object identity = (Object)posture ?? receiver as Object ?? shove as Object ?? body;
            if ((identity != null && contacted.Contains(identity)) || (posture != null && !posture.IsAvailable)) continue;
            if (!TryGetContactPoint(collider, origin, forward, reach, cone, out Vector3 point)) continue;
            Vector3 delta = point - origin;
            // A collider overlapping the cast origin still needs to be in front of the player.
            Vector3 facingDelta = delta.sqrMagnitude > .0001f ? delta : collider.bounds.center - origin;
            if (facingDelta.sqrMagnitude > .0001f && Vector3.Dot(facingDelta.normalized, forward) < cone) continue;
            if (!HasSight(player.transform, collider, origin, point, layers)) continue;
            // Static walls have no damage/shove receiver but still produce a contact sound.
            if (posture == null) hitWorld = true;
            if (identity == null) continue;
            contacted.Add(identity);
            Vector3 push = Vector3.ProjectOnPlane(collider.bounds.center - origin, Vector3.up).normalized;
            if (push.sqrMagnitude < .01f) push = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (receiver != null)
            {
                var result = receiver.ReceiveHit(new DamageHit(attackId, damage, DamageKind.Attack, point, push,
                    player.gameObject, true, settings.shovePostureDamage));
                if (result != DamageResult.Damaged) continue;
            }
            if (posture != null)
            {
                posture.ApplyPostureDamage(settings.shovePostureDamage);
                posture.Interrupt(settings.shoveStun);
            }
            Vector3 impulse = push * settings.shovePushSpeed;
            if (shove != null) shove.ReceiveShove(impulse, point);
            else if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            count++;
            if (posture != null) hitEnemy = true;
        }
        var impact = hitEnemy ? ShoveImpactKind.Enemy : hitWorld ? ShoveImpactKind.World : ShoveImpactKind.None;
        var audio = player.GetComponent<PlayerShoveAudio>();
        if (audio == null && Application.isPlaying && impact != ShoveImpactKind.None) audio = player.gameObject.AddComponent<PlayerShoveAudio>();
        audio?.Play(impact);
        return count;
    }
    private static bool TryGetContactPoint(Collider collider, Vector3 origin, Vector3 forward,
        float reach, float cone, out Vector3 point)
    {
        // Unity only supports ClosestPoint on these shapes, not terrain or concave level meshes.
        if (collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider
            || (collider is MeshCollider mesh && mesh.convex))
        {
            point = collider.ClosestPoint(origin);
            return (point - origin).sqrMagnitude <= reach * reach;
        }
        // Bounds are only used to aim a ray. Treating the bounds as a surface would register
        // empty space inside a concave mesh (or a large terrain) as a wall impact.
        return TrySurfaceRay(collider, origin, collider.bounds.ClosestPoint(origin) - origin, forward, reach, cone, out point)
            || TrySurfaceRay(collider, origin, forward, forward, reach, cone, out point)
            || TrySurfaceRay(collider, origin, collider.bounds.center - origin, forward, reach, cone, out point);
    }
    private static bool TrySurfaceRay(Collider collider, Vector3 origin, Vector3 direction,
        Vector3 forward, float reach, float cone, out Vector3 point)
    {
        point = default;
        if (direction.sqrMagnitude < .0001f || Vector3.Dot(direction.normalized, forward) < cone) return false;
        if (!collider.Raycast(new Ray(origin, direction.normalized), out RaycastHit hit, reach)) return false;
        point = hit.point;
        return true;
    }
    private static bool HasSight(Transform owner, Collider candidate, Vector3 origin, Vector3 point, LayerMask layers)
    {
        Vector3 delta = point - origin;
        if (delta.sqrMagnitude < .0001f) return true;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, layers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == candidate || hit.transform.IsChildOf(owner)) continue;
            if (hit.collider.GetComponentInParent<MeleeCombatTarget>() != null) continue;
            if (candidate.attachedRigidbody != null && hit.rigidbody == candidate.attachedRigidbody) continue;
            return false;
        }
        return true;
    }
}
