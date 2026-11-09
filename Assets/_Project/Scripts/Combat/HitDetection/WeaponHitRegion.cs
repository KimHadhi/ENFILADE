using UnityEngine;

/// <summary>
/// Explicit region of a hitbox: zombie bodies put one on each bone hitbox (ZombieBody), the head box being the head.
/// A plain enemy capsule with none uses its upper 22%.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponHitRegion : MonoBehaviour
{
    public enum Region { Body, Head }
    [SerializeField] private Region region;
    /// <summary>Set by code that builds hitboxes at runtime.</summary>
    public Region Kind { get => region; set => region = value; }
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
