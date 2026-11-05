// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS Animation Pack/Weapon Events")]
    public class CasFPSWeaponEvents : MonoBehaviour
    {
        protected CasFPSWeapon _weapon;
        protected FPSBoneVisibility _boneVisibility;

        protected virtual void Awake()
        {
            _weapon = GetComponentInParent<CasFPSWeapon>();
            _boneVisibility = GetComponent<FPSBoneVisibility>();
            if (_boneVisibility == null) _boneVisibility = GetComponentInParent<FPSBoneVisibility>();
            if (_boneVisibility == null && _weapon != null)
            {
                _boneVisibility = _weapon.GetComponentInChildren<FPSBoneVisibility>(true);
            }
        }

        public void PlayWeaponSound(int index)
        {
            if (_weapon != null) _weapon.PlayWeaponSound(index);
        }

        public void SetBoneVisibility(int value)
        {
            if (_boneVisibility != null) _boneVisibility.SetBoneVisibility(value);
        }
    }
}
