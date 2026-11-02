using UnityEngine;

/// <summary>
/// Overrules the automatic vault-or-climb choice for one object and its children. Most obstacles need none of this: the
/// player goes over when the far side is open and onto it otherwise. Add it where a map object reads the other way.
/// </summary>
public sealed class ParkourObstacle : MonoBehaviour
{
    public enum Use { Automatic, VaultOver, ClimbOnto }

    [SerializeField, Tooltip("Vault Over never climbs onto this object. Climb Onto never vaults over it.")]
    private Use use;

    public static bool AllowsVault(Collider surface) => UseOf(surface) != Use.ClimbOnto;
    public static bool AllowsClimb(Collider surface) => UseOf(surface) != Use.VaultOver;

    private static Use UseOf(Collider surface)
    {
        var obstacle = surface != null ? surface.GetComponentInParent<ParkourObstacle>() : null;
        return obstacle != null ? obstacle.use : Use.Automatic;
    }
}
