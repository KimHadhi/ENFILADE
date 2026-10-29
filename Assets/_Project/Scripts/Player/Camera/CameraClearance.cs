using System;
using UnityEngine;

/// <summary>
/// Sweeps a camera volume from a stable, collision-free point inside the player capsule. Own colliders and
/// triggers do not obstruct it. The caller supplies the same anchor for pose tests and final placement;
/// neither the previous camera position nor the animated head becomes a different cast origin on contact.
/// </summary>
internal sealed class CameraClearance
{
    private const float Skin = .005f;
    private RaycastHit[] hits = new RaycastHit[16];
    private Collider[] overlaps = new Collider[16];

    public bool IsClear(Vector3 anchor, Vector3 target, float radius, LayerMask layers, Transform owner)
    {
        radius = Mathf.Max(.001f, radius);
        return !Overlaps(anchor, radius, layers, owner)
            && !Overlaps(target, radius, layers, owner)
            && !FirstHit(anchor, target, radius, layers, owner, out _);
    }

    public Vector3 Resolve(Vector3 anchor, Vector3 target, float radius, LayerMask layers, Transform owner)
    {
        radius = Mathf.Max(.001f, radius);
        // There is no safe prefix if the supplied anchor is already embedded. Let the caller recover that
        // anchor rather than selecting an unrelated origin or moving the camera through the obstruction.
        if (Overlaps(anchor, radius, layers, owner)) return anchor;
        Vector3 move = target - anchor;
        float distance = move.magnitude;
        if (distance <= .00001f) return anchor;
        Vector3 direction = move / distance;
        float allowed = FirstHit(anchor, target, radius, layers, owner, out float first)
            ? Mathf.Max(0f, first - Skin) : distance;
        Vector3 result = anchor + direction * allowed;
        if (!Overlaps(result, radius, layers, owner)) return result;

        // A target inside geometry or numerical contact at an edge can survive a sweep. Search only the
        // prefix before the nearest obstruction, with the known safe anchor as the lower bound.
        float low = 0f, high = allowed;
        for (int i = 0; i < 12; i++)
        {
            float middle = (low + high) * .5f;
            if (Overlaps(anchor + direction * middle, radius, layers, owner)) high = middle;
            else low = middle;
        }
        return anchor + direction * Mathf.Max(0f, low - Skin);
    }

    private bool FirstHit(Vector3 anchor, Vector3 target, float radius, LayerMask layers, Transform owner, out float first)
    {
        Vector3 move = target - anchor;
        float distance = move.magnitude;
        first = distance;
        if (distance <= .00001f) return false;
        int count;
        while (true)
        {
            count = Physics.SphereCastNonAlloc(anchor, radius, move / distance, hits, distance, layers, QueryTriggerInteraction.Ignore);
            if (count < hits.Length) break;
            // A full buffer can omit the nearest wall. Grow and repeat instead of trusting a partial set.
            Array.Resize(ref hits, hits.Length * 2);
        }
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (!External(hit.collider, owner) || hit.distance > first) continue;
            first = hit.distance;
            found = true;
        }
        return found;
    }

    private bool Overlaps(Vector3 point, float radius, LayerMask layers, Transform owner)
    {
        int count;
        while (true)
        {
            count = Physics.OverlapSphereNonAlloc(point, radius, overlaps, layers, QueryTriggerInteraction.Ignore);
            if (count < overlaps.Length) break;
            Array.Resize(ref overlaps, overlaps.Length * 2);
        }
        float squared = radius * radius;
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            if (External(collider, owner) && (!HasClosestPoint(collider) || (collider.ClosestPoint(point) - point).sqrMagnitude <= squared)) return true;
        }
        return false;
    }

    // Collider.ClosestPoint only supports these; for the rest (a non-convex mesh such as ProBuilder stairs, terrain)
    // the overlap query's own exact answer stands.
    private static bool HasClosestPoint(Collider collider)
        => collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider || collider is MeshCollider { convex: true };

    private static bool External(Collider collider, Transform owner)
        => collider != null && !collider.isTrigger && (owner == null || !collider.transform.IsChildOf(owner));
}
