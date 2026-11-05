// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.FPSAnimationPack.Scripts.Sounds;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS Animation Pack/FPS Pack CAS Controller")]
    public class CasFPSPlayerController : CharacterExampleController
    {
        [Header("FPS Animation Pack")]
        [SerializeField, Range(0f, 1f)] protected float aimSensitivityMultiplier = 0.7f;
        [SerializeField] protected IkMotionSettings aimingMotion;
        [SerializeField] protected IkMotionSettings fireModeMotion;
        [SerializeField] protected IkMotionSettings movementMotion;

        protected RecoilAnimation _recoilAnimation;
        protected FPSPlayerSound _playerEvents;
        protected float _defaultMouseSensitivity;

        public CasFPSWeapon GetWeaponItem()
        {
            return GetActiveItem() as CasFPSWeapon;
        }

        protected override void Start()
        {
            base.Start();
            _defaultMouseSensitivity = mouseSensitivity;
            _recoilAnimation = GetComponentInChildren<RecoilAnimation>();
            _playerEvents = GetComponentInChildren<FPSPlayerSound>();
        }

        protected override Vector2 GetDeltaLookInput()
        {
            Vector2 recoilDelta = Vector2.zero;

            if (_recoilAnimation != null)
            {
                _recoilAnimation.UpdateDeltaInput(_deltaLookInput);
                recoilDelta = _recoilAnimation.GetRecoilDelta();
            }

            return base.GetDeltaLookInput() + recoilDelta;
        }

        protected override void OnMovementChange(bool isMoving)
        {
            base.OnMovementChange(isMoving);
            if (_proceduralAnimation != null) _proceduralAnimation.UpdateAnimationModifier(movementMotion);
        }
        
        

#if ENABLE_INPUT_SYSTEM
        public override void OnUseItem(InputValue value)
        {
            if (!isFirstPerson && !_isAiming) return;
            base.OnUseItem(value);
        }

        public virtual void OnChangeFireMode()
        {
            CasFPSWeapon weapon = GetWeaponItem();
            if (weapon == null) return;

            FireMode previousMode = weapon.ActiveFireMode;
            weapon.ChangeFireMode();

            if (previousMode == weapon.ActiveFireMode) return;
            
            if (_playerEvents != null) _playerEvents.PlayFireModeSwitchSound();
            if (_proceduralAnimation != null) _proceduralAnimation.UpdateAnimationModifier(fireModeMotion);
        }

        public virtual void OnChangeWeapon()
        {
            OnChangeItem();
        }

        public virtual void OnFire(InputValue value)
        {
            OnUseItem(value);
        }

        public override void OnAim(InputValue value)
        {
            bool wasAiming = _isAiming;
            base.OnAim(value);

            if (_proceduralAnimation != null) _proceduralAnimation.UpdateAnimationModifier(aimingMotion);
            mouseSensitivity = _isAiming
                ? _defaultMouseSensitivity * aimSensitivityMultiplier
                : _defaultMouseSensitivity;

            if (wasAiming != _isAiming && _playerEvents != null)
            {
                _playerEvents.PlayAimSound(_isAiming);
            }

            if (!isFirstPerson && !_isAiming) GetActiveItem()?.StopUsingItem();
        }

        public virtual void OnReload()
        {
            GetWeaponItem()?.Reload();
        }
#endif
    }
}
