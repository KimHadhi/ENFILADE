using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using UnityEngine;

/// <summary>
/// What KINEMATION's procedural settings read off the player. In KINEMATION's demo their bindings point at
/// CasFPSPlayerController, CharacterExampleController and CharacterCamera; GunRigBindings points them here, and GunRig
/// writes these every frame from our movement, look and weapon. The names are KINEMATION's, so a binding path such as
/// "GetWeaponItem.aimPoint" or "LookInput.y" reads the same member it read there.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunRigInputs : MonoBehaviour
{
    /// <summary>KINEMATION's gait: 0 still, 1 walk, 2 jog, 3 sprint.</summary>
    public float Gait { get; internal set; }
    public bool IsAiming { get; internal set; }
    /// <summary>KINEMATION's aiming pose weight. Its first person player keeps it at 1.</summary>
    public float AimingWeight { get; internal set; } = 1f;
    /// <summary>Yaw, pitch (negative is up) and lean in degrees, as KINEMATION's controller keeps them.</summary>
    public Vector3 LookInput { get; internal set; }
    /// <summary>This frame's turn of the view in degrees: x to the right, y up.</summary>
    public Vector2 DeltaLookInput { get; internal set; }
    /// <summary>Movement in the body's frame: x to the right, y forward, at most 1.</summary>
    public Vector2 MoveInput { get; internal set; }
    public bool IsGrounded { get; internal set; } = true;
    /// <summary>KINEMATION's CharacterCamera view weight: 0 is first person.</summary>
    public float ViewWeight => 0f;

    internal CasFPSWeapon Weapon { get; set; }

    /// <summary>The gun in the rig's hands (KINEMATION's CasFPSPlayerController.GetWeaponItem).</summary>
    public CasFPSWeapon GetWeaponItem() => Weapon;
}
