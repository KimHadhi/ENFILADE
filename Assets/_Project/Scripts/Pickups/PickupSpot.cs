using UnityEngine;

/// <summary>
/// A place in a map where a field pickup lies (stage 6D): what lies there is rolled from Tables/Pickups (its spot
/// weights) each time the map starts, so runs differ. <see cref="Kind"/> keeps the roll to guns or to supplies (ammo,
/// heals, shields, money) where the map wants it; Any rolls from everything. Placed by the greybox builders
/// (GreyboxBuilder.Pickup) or by hand: put it where the item should rest on the floor. FieldPickups finds them.
/// </summary>
[DisallowMultipleComponent]
public sealed class PickupSpot : MonoBehaviour
{
    public enum Roll { Any, Gun, Supply }

    [Tooltip("What this spot can hold: anything, only guns, or only supplies (ammo, heals, shields, money).")]
    public Roll Kind = Roll.Supply;

    private void OnDrawGizmos()
    {
        Gizmos.color = Kind == Roll.Gun ? new Color(1f, .31f, .47f) : Kind == Roll.Supply ? new Color(.38f, .88f, 1f) : Color.white;
        Gizmos.DrawWireCube(transform.position + Vector3.up * .15f, new Vector3(.5f, .3f, .5f));
    }
}
