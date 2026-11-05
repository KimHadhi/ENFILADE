using UnityEngine;

/// <summary>
/// Takes the animation events KINEMATION's character clips send to its demo's FPSPlayerSound (the equip and unequip
/// clips call PlayEquipSound and PlayUnEquipSound, the jump clips the jump and land sounds), so the hidden arms rig
/// needs no demo component and Unity reports no event without a receiver. Our own sound banks play those moments.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunRigEvents : MonoBehaviour
{
    public void PlayEquipSound() { }
    public void PlayUnEquipSound() { }
    public void PlayJumpSound() { }
    public void PlayLandSound() { }
    public void PlayPlayerSound(int index) { }
}
