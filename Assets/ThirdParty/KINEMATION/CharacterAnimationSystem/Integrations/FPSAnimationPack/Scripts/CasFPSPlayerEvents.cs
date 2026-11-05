// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS Animation Pack/Player Events")]
    public class CasFPSPlayerEvents : MonoBehaviour
    {
        [SerializeField] protected FPSPlayerSettings playerSettings;

        [Header("Weapon Swapping")]
        [SerializeField] protected AudioClip equipSound;
        [SerializeField] protected AudioClip unEquipSound;

        [Header("Movement")]
        [SerializeField] protected List<AudioClip> walkSounds;
        [SerializeField, Min(0f)] protected float walkDelay = 0.4f;
        [SerializeField] protected List<AudioClip> sprintSounds;
        [SerializeField, Min(0f)] protected float sprintDelay = 0.4f;

        [Header("Jumping")]
        [SerializeField] protected AudioClip jumpSound;
        [SerializeField] protected AudioClip landSound;

        [Header("Aiming")]
        [SerializeField] protected AudioClip aimInSound;
        [SerializeField] protected AudioClip aimOutSound;

        [Header("Fire Mode")]
        [SerializeField] protected AudioClip fireModeSwitchSound;

        protected CasFPSPlayerController _controller;
        protected AudioSource _audioSource;
        protected float _movementPlayback;

        protected virtual void Awake()
        {
            _controller = GetComponentInParent<CasFPSPlayerController>();
            _audioSource = transform.root.GetComponentInChildren<AudioSource>();
        }

        protected virtual void Update()
        {
            if (_controller == null || !_controller.IsGrounded || Mathf.Approximately(_controller.Gait, 0f))
            {
                _movementPlayback = 0f;
                return;
            }

            bool isSprinting = _controller.Gait > 1.4f;
            float delay = isSprinting ? sprintDelay : walkDelay;
            _movementPlayback += Time.deltaTime;

            if (_movementPlayback < delay) return;

            PlayRandom(isSprinting ? sprintSounds : walkSounds);
            _movementPlayback = 0f;
        }

        protected void Play(AudioClip clip, float volume = 1f)
        {
            if (_audioSource != null && clip != null) _audioSource.PlayOneShot(clip, volume);
        }

        protected void PlayRandom(List<AudioClip> clips)
        {
            if (clips == null || clips.Count == 0) return;
            Play(clips[Random.Range(0, clips.Count)]);
        }

        public void PlayAimSound(bool isAimIn = true)
        {
            Play(isAimIn ? aimInSound : aimOutSound);
        }

        public void PlayFireModeSwitchSound()
        {
            Play(fireModeSwitchSound, Random.Range(0.2f, 0.25f));
        }

        public void PlayEquipSound()
        {
            Play(equipSound);
        }

        public void PlayUnEquipSound()
        {
            Play(unEquipSound);
        }

        public void PlayWalkSound()
        {
            PlayRandom(walkSounds);
        }

        public void PlaySprintSound()
        {
            PlayRandom(sprintSounds);
        }

        public void PlayJumpSound()
        {
            Play(jumpSound);
        }

        public void PlayLandSound()
        {
            Play(landSound);
        }

        public void PlayPlayerSound(int index)
        {
            if (playerSettings == null || index < 0 || index >= playerSettings.generalSounds.Count) return;
            Play(playerSettings.generalSounds[index]);
        }
    }
}
