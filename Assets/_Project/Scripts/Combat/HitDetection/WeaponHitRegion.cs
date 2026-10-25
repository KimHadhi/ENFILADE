using UnityEngine;

/// <summary>Optional explicit region for future rigs. Prototype enemy capsules use their upper 22%.</summary>
[DisallowMultipleComponent]
public sealed class WeaponHitRegion : MonoBehaviour
{
    public enum Region { Body, Head }
    [SerializeField] private Region region;
    public static bool IsHead(RaycastHit hit)
    {
        var explicitRegion = hit.collider.GetComponentInParent<WeaponHitRegion>();
        if (explicitRegion != null) return explicitRegion.region == Region.Head;
        if (hit.collider.GetComponentInParent<EnemyHealth>() == null) return false;
        if (!(hit.collider is CapsuleCollider) && !(hit.collider is CharacterController)) return false;
        var bounds = hit.collider.bounds;
        return hit.point.y >= bounds.min.y + bounds.size.y * .78f;
    }
}
