using UnityEngine;

/// <summary>
/// Models the field pickups wear (PickupProps), at Resources/Combat/PickupLibrary: the ammo case from the Low Poly Weapon
/// Bundle, and money as the Heist Essentials pack's stacked notes (the user's word, 2026-11-12: "돈 프롭도 저걸로").
/// Only their meshes and materials are used; the copies' scripts and colliders are removed. The medkit and shield cell
/// are built in code, and a gun wears its own KINEMATION model.
/// </summary>
[CreateAssetMenu(fileName = "PickupLibrary", menuName = "ENFILADE/Combat/Pickup Library")]
public sealed class PickupLibrary : ScriptableObject
{
    [Tooltip("What an ammo pile looks like.")]
    public GameObject ammo;
    [Tooltip("What money looks like, on the floor as a pickup and when someone drops it (MoneyDrops). Empty: the old bricks "
        + "of notes built from boxes.")]
    public GameObject money;

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
