using UnityEngine;

/// <summary>
/// Models the field pickups wear (PickupProps), at Resources/Combat/PickupLibrary: the ammo case from the Low Poly Weapon
/// Bundle. Only its meshes and materials are used; the copy's scripts and colliders are removed. The medkit, shield cell
/// and money are built in code, and a gun wears its own KINEMATION model.
/// </summary>
[CreateAssetMenu(fileName = "PickupLibrary", menuName = "ENFILADE/Combat/Pickup Library")]
public sealed class PickupLibrary : ScriptableObject
{
    [Tooltip("What an ammo pile looks like.")]
    public GameObject ammo;

    private static PickupLibrary instance;
    private static bool looked;

    public static PickupLibrary Instance
    {
        get
        {
            if (looked) return instance;
            looked = true;
            return instance = Resources.Load<PickupLibrary>("Combat/PickupLibrary");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() { instance = null; looked = false; }
}
