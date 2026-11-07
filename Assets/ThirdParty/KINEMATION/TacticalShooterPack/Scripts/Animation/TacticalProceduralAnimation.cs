// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.KShooterCore.Runtime;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;
using KINEMATION.TacticalShooterPack.Scripts.Attachments;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace KINEMATION.TacticalShooterPack.Scripts.Animation
{
    [Serializable]
    public struct SpineBone
    {
        public Transform bone;
        public Vector2 clampedAngle;
        [HideInInspector] public Vector2 cachedClampedAngle;
    }

    public struct SpineBoneAtom
    {
        public TransformStreamHandle handle;
        public Vector2 clampedAngle;
    }

    [Serializable]
    public struct TacSkeletonBones
    {
        public Transform ikHandGun;
        public Transform ikHandGunAdditive;

        public Transform headBone;
        public Transform camera;

        public Transform ikRightHand;
        public Transform ikLeftHand;
        public Transform rightHand;
        public Transform leftHand;
        public Transform spineRoot;

        [Unfold] public List<SpineBone> lookUpBones;
        [Unfold] public List<SpineBone> lookRightBones;
    }

    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Tac Procedural Animation")]
    public class TacticalProceduralAnimation : MonoBehaviour
    {
        [Header("Inputs")]
        public AnimationClip quickPistolPose;
        [Range(0f, 1f)] public float aimingWeight = 0f;
        [Range(-90f, 90f)] public float pitchInput = 0f;
        [Range(-90, 90f)] public float yawInput = 0f;
        [Range(-90, 90f)] public float leanInput = 0f;
        [Range(0f, 1f)] public float spineStabilization = 0f;
        [HideInInspector] public Vector2 deltaLookInput;
        [HideInInspector] public Vector2 moveInput = Vector2.zero;

        [Header("Bone References")]
        public TacSkeletonBones bones;

        protected TacticalShooterPlayerJob _job;
        protected AnimationScriptPlayable _playable;
        protected AnimationPlayableOutput _tacOutput;

        protected Animator _animator;
        protected IkMotionPlayer _ikMotionPlayer = new IkMotionPlayer();
        private AnimationClipPlayable _gripPlayable;
        private AnimationClip _gripPose;
        private KTransform _targetAimPoint = KTransform.Identity;
        private KTransform _targetGripTransform = KTransform.Identity;
        private bool _hasAimPoint;
        private float _targetGripWeight;

        public void UpdateRightHandPose(KTransform rightHandPose, Vector3 pistolRightHandOffset)
        {
            _job.cachedIkHandGunRight = rightHandPose;
            _job.pistolRightHandOffset = pistolRightHandOffset;
        }

        public void UpdateAimPoint(KTransform newAimPointTransform)
        {
            _targetAimPoint = newAimPointTransform;
            if (_hasAimPoint) return;
            _job.aimPointTransform = newAimPointTransform;
            _hasAimPoint = true;
        }

        public void UpdateGrip(GripAttachmentData data)
        {
            if (!_playable.IsValid()) return;
            _targetGripWeight = data.ikTarget != null ? 1f : 0f;
            if (data.ikTarget != null)
            {
                _targetGripTransform = new KTransform(bones.ikHandGun)
                    .GetRelativeTransform(new KTransform(data.ikTarget), false);
                if (_job.gripWeight <= 0.001f) _job.gripTransform = _targetGripTransform;
            }

            // Keep the outgoing finger pose while the hand returns to the weapon animation.
            if (_gripPose == data.pose || (data.ikTarget == null && _job.gripWeight > 0.001f)) return;
            _gripPose = data.pose;
            _playable.GetGraph().Disconnect(_playable, 0);
            if (_gripPlayable.IsValid()) _gripPlayable.Destroy();
            if (_gripPose == null) return;
            _gripPlayable = AnimationClipPlayable.Create(_playable.GetGraph(), _gripPose);
            _gripPlayable.SetTime(0d);
            _gripPlayable.SetSpeed(0d);
            _playable.GetGraph().Connect(_gripPlayable, 0, _playable, 0);
            _playable.SetInputWeight(0, 1f);
        }

        public void PlayIkMotion(IKMotion newMotion)
        {
            _ikMotionPlayer.PlayIkMotion(newMotion);
        }

        public void UpdateAnimationSettings(TacticalShooterWeapon weapon)
        {
            Initialize();
            _job._weaponSettings = weapon.tacWeaponSettings;
            _hasAimPoint = false;
            _job.gripWeight = 0f;
            _targetGripWeight = 0f;

            AnimationClip basePose = weapon.IsOneHanded ? quickPistolPose : weapon.BaseIdlePose;
            if (basePose != null)
            {
                basePose.SampleAnimation(_animator.gameObject, 0f);

                Transform pelvis = bones.spineRoot.parent;
                _job.stablePelvisRotation = Quaternion.Inverse(_animator.transform.rotation) * pelvis.rotation;
            }

            _playable.SetJobData(_job);
        }

        protected void Start() => Initialize();

        public void Initialize()
        {
            if (_playable.IsValid()) return;
            _animator = GetComponentInChildren<Animator>();
            _job = new TacticalShooterPlayerJob();

            _job.lookUpBones
                = new NativeArray<SpineBoneAtom>(bones.lookUpBones.Count, Allocator.Persistent);
            for (int i = 0; i < bones.lookUpBones.Count; i++)
            {
                _job.lookUpBones[i] = new SpineBoneAtom()
                {
                    handle = _animator.BindStreamTransform(bones.lookUpBones[i].bone),
                    clampedAngle = bones.lookUpBones[i].clampedAngle
                };
            }

            _job.lookRightBones = new NativeArray<SpineBoneAtom>(bones.lookRightBones.Count, Allocator.Persistent);
            for (int i = 0; i < bones.lookRightBones.Count; i++)
            {
                _job.lookRightBones[i] = new SpineBoneAtom()
                {
                    handle = _animator.BindStreamTransform(bones.lookRightBones[i].bone),
                    clampedAngle = bones.lookRightBones[i].clampedAngle
                };
            }

            _job.SetupIkBones(_animator, bones);
            var fingerBones = bones.leftHand.GetComponentsInChildren<Transform>();
            _job.gripFingers = new NativeArray<TransformStreamHandle>(fingerBones.Length - 1, Allocator.Persistent);
            for (int i = 1; i < fingerBones.Length; i++)
                _job.gripFingers[i - 1] = _animator.BindStreamTransform(fingerBones[i]);

            _playable = AnimationScriptPlayable.Create(_animator.playableGraph, _job, 1);
            _playable.SetProcessInputs(false);

            _tacOutput = AnimationPlayableOutput.Create(_animator.playableGraph,
                "TacticalShooterOutput", _animator);
            _tacOutput.SetSourcePlayable(_playable);
            _tacOutput.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
        }

        protected void Update()
        {
            if (!_playable.IsValid() || _job._weaponSettings == null) return;
            _ikMotionPlayer.ProcessIkMotion();

            float aimAlpha = _job._weaponSettings.scopeSwitchSpeed <= 0f ? 1f
                : KMath.ExpDecayAlpha(_job._weaponSettings.scopeSwitchSpeed, Time.deltaTime);
            _job.aimPointTransform = KTransform.Lerp(_job.aimPointTransform, _targetAimPoint, aimAlpha);
            float gripAlpha = _job._weaponSettings.gripBlendSpeed <= 0f ? 1f
                : KMath.ExpDecayAlpha(_job._weaponSettings.gripBlendSpeed, Time.deltaTime);
            _job.gripWeight = Mathf.Lerp(_job.gripWeight, _targetGripWeight, gripAlpha);
            _job.gripTransform = KTransform.Lerp(_job.gripTransform, _targetGripTransform, gripAlpha);
            _job.hasGripPose = _gripPlayable.IsValid();

            _job.aimingWeight = aimingWeight;
            _job.spineStabilization = spineStabilization;

            Vector3 prevLookInput = _job.lookInput;
            _job.lookInput = new Vector3(yawInput, pitchInput, leanInput);
            _job.deltaLookInput = _job.lookInput - prevLookInput;

            _job._ikMotion = _ikMotionPlayer.IkMotion;
            _job.moveInput = moveInput;

            _job.UpdateJob(_playable);
            _playable.SetJobData(_job);
        }

        protected void OnDestroy()
        {
            if (_job.gripFingers.IsCreated) _job.gripFingers.Dispose();
            if (_job.lookUpBones.IsCreated) _job.lookUpBones.Dispose();
            if (_job.lookRightBones.IsCreated) _job.lookRightBones.Dispose();
        }

        protected void ApplyAngleDistribution(List<SpineBone> spineBones)
        {
            int count = spineBones.Count;
            int adjustStartIndex = 0;

            Vector2 angleToDistribute = Vector2.zero;

            bool bShallDistribute = false;
            bool bDistributeForX = false;

            for (int i = 0; i < count; i++)
            {
                var element = spineBones[i];

                angleToDistribute.x += Mathf.Abs(element.clampedAngle.x);
                angleToDistribute.y += Mathf.Abs(element.clampedAngle.y);

                if (!Mathf.Approximately(element.cachedClampedAngle.x,element.clampedAngle.x))
                {
                    adjustStartIndex = i + 1;
                    bShallDistribute = true;
                    bDistributeForX = true;
                    break;
                }

                if (!Mathf.Approximately(element.cachedClampedAngle.y, element.clampedAngle.y))
                {
                    adjustStartIndex = i + 1;
                    bShallDistribute = true;
                    break;
                }
            }

            if (bShallDistribute)
            {
                for (int i = adjustStartIndex; i < count; i++)
                {
                    var element = spineBones[i];

                    if (bDistributeForX)
                    {
                        element.clampedAngle.x = (90f - angleToDistribute.x) / (count - adjustStartIndex);
                    }
                    else
                    {
                        element.clampedAngle.y = (90f - angleToDistribute.y) / (count - adjustStartIndex);
                    }

                    spineBones[i] = element;
                }
            }

            for (int i = 0; i < count; i++)
            {
                var element = spineBones[i];
                element.cachedClampedAngle.x = element.clampedAngle.x;
                element.cachedClampedAngle.y = element.clampedAngle.y;
                spineBones[i] = element;
            }
        }

        protected void OnValidate()
        {
            ApplyAngleDistribution(bones.lookUpBones);
            ApplyAngleDistribution(bones.lookRightBones);
        }
    }
}
