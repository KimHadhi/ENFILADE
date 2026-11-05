// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS Animation Pack/Machine Gun")]
    public class CasFPSMachineGun : CasFPSWeapon
    {
        [SerializeField] protected List<Transform> gunTape;
        [SerializeField, Min(0f)] protected float tapeResetTime;
        public FPSAnimationPair reloadExtra;

        public override void Reload()
        {
            if (weaponSettings == null || _isReloading || _activeAmmo == weaponSettings.ammo) return;

            bool useExtraReload = _activeAmmo > 0 && _activeAmmo <= gunTape.Count;
            FPSAnimationPair selectedReload = _activeAmmo == 0
                ? reloadEmpty
                : useExtraReload ? reloadExtra : reloadTactical;

            PlayAnimation(selectedReload);
            Invoke(nameof(ResetActiveAmmo), tapeResetTime * weaponSettings.ammoResetTimeScale);
            _isReloading = true;
        }

        private void LateUpdate()
        {
            int count = gunTape.Count;
            if (_activeAmmo > count) return;

            for (int i = 0; i < count; i++)
            {
                if(i > count - _activeAmmo) continue;
                
                KTransform childWorldTransform = KTransform.Identity;
                if (i < count - 1)
                {
                    childWorldTransform = new KTransform(gunTape[i + 1]);
                }

                gunTape[i].localScale /= 100f;
                if (i < count - 1)
                {
                    gunTape[i + 1].localScale *= 100f;
                    gunTape[i + 1].position = childWorldTransform.position;
                    gunTape[i + 1].rotation = childWorldTransform.rotation;
                }
            }
        }
    }
}
