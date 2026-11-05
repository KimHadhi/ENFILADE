// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS Animation Pack/Manual Weapon")]
    public class CasFPSManualWeapon : CasFPSWeapon
    {
        [Header("Manual Reload")]
        public FPSAnimationPair reloadStart;
        public FPSAnimationPair reloadLoop;
        public FPSAnimationPair reloadEnd;

        public override void Reload()
        {
            if (weaponSettings == null || _isReloading || _activeAmmo == weaponSettings.ammo) return;

            _isReloading = true;
            PlayAnimation(reloadStart);
            Invoke(nameof(ReloadLoop), GetAnimationLength(reloadStart, true));
        }

        protected virtual void ReloadLoop()
        {
            if (_activeAmmo >= weaponSettings.ammo)
            {
                ReloadEnd();
                return;
            }

            _activeAmmo++;
            PlayAnimation(reloadLoop);
            Invoke(nameof(ReloadLoop), GetAnimationLength(reloadLoop, true));
        }

        protected virtual void ReloadEnd()
        {
            _isReloading = false;
            PlayAnimation(reloadEnd);
        }
    }
}
