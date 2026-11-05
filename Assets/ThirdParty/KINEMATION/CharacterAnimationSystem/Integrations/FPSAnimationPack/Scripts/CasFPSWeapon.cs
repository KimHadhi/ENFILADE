// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.FPSAnimationPack.Scripts.Sounds;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts
{
    [Serializable]
    public struct FPSAnimationPair
    {
        public AnimationAsset character;
        public AnimationAsset weapon;
    }

    [AddComponentMenu(CasNames.Path_Addons + "FPS Animation Pack/FPS Pack Weapon")]
    public class CasFPSWeapon : CasProp, IAmmoProvider
    {
        public FireMode ActiveFireMode => fireMode;
        public bool IsFiring => _isFiring;

        [Header("Weapon")] public FPSWeaponSettings weaponSettings;
        public Transform aimPoint;
        [SerializeField] protected FireMode fireMode = FireMode.Semi;
        [SerializeField] protected CharacterCameraShake cameraShake;

        [Header("Animations")]
        public FPSAnimationPair fire;
        public FPSAnimationPair fireOut;
        public FPSAnimationPair reloadEmpty;
        public FPSAnimationPair reloadTactical;
        public FPSAnimationPair equip;
        public FPSAnimationPair unequip;

        protected CharacterAnimationComponent _characterAnimation;
        protected SimpleAnimationPlayer _weaponAnimation;
        protected RecoilAnimation _recoilAnimation;
        protected CharacterCamera _characterCamera;
        protected AudioSource _audioSource;
        protected FPSPlayerSound _playerSound;

        protected int _activeAmmo;
        protected bool _isReloading;
        protected bool _isFiring;

        public KTransform GetWeaponOffset()
        {
            return weaponSettings == null
                ? KTransform.Identity
                : new KTransform(weaponSettings.ikOffset, Quaternion.identity);
        }

        protected void Awake()
        {
            Transform root = transform.root;
            _characterAnimation = root.GetComponentInChildren<CharacterAnimationComponent>();
            _recoilAnimation = root.GetComponentInChildren<RecoilAnimation>();
            _characterCamera = root.GetComponentInChildren<CharacterCamera>();
            _audioSource = root.GetComponentInChildren<AudioSource>();
            _weaponAnimation = GetComponentInChildren<SimpleAnimationPlayer>(true);
            _playerSound = root.GetComponentInChildren<FPSPlayerSound>();

            _activeAmmo = weaponSettings.ammo;
        }

        protected virtual void PlayAnimation(FPSAnimationPair animationPair)
        {
            if (_characterAnimation != null)
            {
                _characterAnimation.PlayAnimation(animationPair.character);
            }

            if (_weaponAnimation != null)
            {
                _weaponAnimation.PlayAnimation(animationPair.weapon);
            }
        }

        protected static float GetAnimationLength(AnimationAsset animation)
        {
            if (animation == null || animation.clip == null || Mathf.Approximately(animation.playRate, 0f))
            {
                return 0f;
            }

            return animation.clip.length / animation.playRate;
        }

        protected static float GetAnimationLength(FPSAnimationPair animation, bool preferCharacter = false)
        {
            float characterLength = GetAnimationLength(animation.character);
            float weaponLength = GetAnimationLength(animation.weapon);

            if (preferCharacter)
            {
                return characterLength > 0f ? characterLength : weaponLength;
            }

            return Mathf.Max(characterLength, weaponLength);
        }

        protected virtual void ResetActiveAmmo()
        {
            if (weaponSettings != null) _activeAmmo = weaponSettings.ammo;
            _isReloading = false;
        }

        protected virtual void Fire()
        {
            if (!_isFiring || _isReloading || weaponSettings == null) return;

            if (_activeAmmo <= 0)
            {
                StopUsingItem();
                return;
            }

            if (_recoilAnimation != null) _recoilAnimation.Play();
            if (_characterCamera != null) _characterCamera.PlayCameraShake(cameraShake);
            PlayFireSound();

            FPSAnimationPair selectedFire = weaponSettings.hasFireOut && _activeAmmo == 1 ? fireOut : fire;
            PlayAnimation(selectedFire);

            _activeAmmo--;

            if (fireMode == FireMode.Auto)
            {
                float fireRate = Mathf.Approximately(weaponSettings.fireRate, 0f) ? 600f : weaponSettings.fireRate;
                Invoke(nameof(Fire), 60f / fireRate);
            }
        }

        protected virtual void PlayFireSound()
        {
            if (_audioSource == null || weaponSettings == null || weaponSettings.fireSounds.Count == 0) return;

            _audioSource.pitch = UnityEngine.Random.Range(weaponSettings.firePitchRange.x,
                weaponSettings.firePitchRange.y);
            _audioSource.volume = UnityEngine.Random.Range(weaponSettings.fireVolumeRange.x,
                weaponSettings.fireVolumeRange.y);
            _audioSource.PlayOneShot(weaponSettings.fireSounds[UnityEngine.Random.Range(0,
                weaponSettings.fireSounds.Count - 1)]);
        }

        public void PlayWeaponSound(int index)
        {
            if (_audioSource == null || weaponSettings == null || index < 0 ||
                index >= weaponSettings.weaponEventSounds.Count)
            {
                return;
            }

            _audioSource.PlayOneShot(weaponSettings.weaponEventSounds[index]);
        }

        public virtual void ChangeFireMode()
        {
            if (weaponSettings == null) return;

            fireMode = fireMode == FireMode.Auto
                ? FireMode.Semi
                : weaponSettings.fullAuto
                    ? FireMode.Auto
                    : FireMode.Semi;

            if (_recoilAnimation != null) _recoilAnimation.fireMode = fireMode;
        }

        public virtual void Reload()
        {
            if (weaponSettings == null || _isReloading || _activeAmmo == weaponSettings.ammo) return;

            FPSAnimationPair selectedReload = _activeAmmo == 0 ? reloadEmpty : reloadTactical;
            PlayAnimation(selectedReload);

            float resetDelay = GetAnimationLength(selectedReload, true) * weaponSettings.ammoResetTimeScale;
            Invoke(nameof(ResetActiveAmmo), resetDelay);
            _isReloading = true;
        }

        public override void UseItem()
        {
            if (_isFiring) return;

            _isFiring = true;
            CancelInvoke(nameof(Fire));
            Fire();
        }

        public override void StopUsingItem()
        {
            _isFiring = false;
            CancelInvoke(nameof(Fire));
            if (_recoilAnimation != null) _recoilAnimation.Stop();
        }

        public override void OnEquipped()
        {
            SetVisibility(true);
            CancelInvoke();
            _isFiring = _isReloading = false;

            if (_recoilAnimation != null && weaponSettings != null)
            {
                _recoilAnimation.Init(weaponSettings.recoilAnimData, weaponSettings.fireRate, fireMode);
            }
            
            PlayAnimation(equip);
            if (equip.character != null && equip.character.slot == AnimationSlot.Overlay)
            {
                _characterAnimation.StopAnimation(AnimationSlot.FullBody, 0.15f);
            }
            
            if(_playerSound != null) _playerSound.PlayEquipSound();
            
            UpdateCameraFov();
        }

        public override float OnUnEquipped()
        {
            StopUsingItem();

            float length = 0f;
            
            PlayAnimation(unequip);
            length = GetAnimationLength(unequip);
            
            if(_playerSound != null) _playerSound.PlayUnEquipSound();
            
            return length;
        }

        public override void OnAim(bool isAiming)
        {
            if (_recoilAnimation != null) _recoilAnimation.isAiming = isAiming;
            UpdateCameraFov();
        }

        protected virtual void UpdateCameraFov()
        {
            if (_characterCamera == null || weaponSettings == null) return;

            float targetFov = _recoilAnimation != null && _recoilAnimation.isAiming
                ? weaponSettings.aimFov
                : _characterCamera.DefaultFov;

            _characterCamera.UpdateTargetFOV(targetFov, 8f);
        }

        public int GetActiveAmmo()
        {
            return _activeAmmo;
        }

        public int GetMaxAmmo()
        {
            return weaponSettings == null ? 0 : weaponSettings.ammo;
        }
    }
}