// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Attachments
{
    public enum AttachmentType { Scope, Grip, Muzzle, Underbarrel }

    [Serializable]
    public struct GripAttachmentData
    {
        public AnimationClip pose;
        public Transform ikTarget;
    }

    [Serializable]
    public struct SightAttachmentData
    {
        public Transform aimPoint;
        [Tooltip("Zero uses the weapon's aiming FOV.")]
        [Min(0f)] public float aimFov;
    }

    [Serializable]
    public struct SecondaryScopeSelection
    {
        public SightAttachment prefab;
        [Min(0)] public int mountIndex;
    }

    [Serializable]
    public struct SightAttachmentSelection
    {
        public SightAttachment prefab;
        public List<SecondaryScopeSelection> secondaryScopes;
    }

    [Serializable]
    public struct WeaponAttachmentGroup<T>
    {
        public Transform attachmentPoint;
        public List<T> attachments;
        [Min(0)] public int initialIndex;

        private List<WeaponAttachment> _instances;
        private int _activeIndex;

        public WeaponAttachment ActiveAttachment => _instances == null || _instances.Count == 0
            ? null : _instances[_activeIndex];
        public int ActiveIndex => _activeIndex;
        public int Count => _instances == null ? 0 : _instances.Count;

        public void Initialize(Func<T, Transform, WeaponAttachment> createAttachment)
        {
            if (_instances != null) return;
            _instances = new List<WeaponAttachment>();
            if (attachments == null || attachments.Count == 0) return;
            if (attachmentPoint == null)
            {
                Debug.LogWarning("Attachment group has prefab choices but no mounting point.");
                return;
            }

            foreach (T selection in attachments)
            {
                WeaponAttachment instance = createAttachment(selection, attachmentPoint);
                if (instance != null) instance.DisableAttachment();
                _instances.Add(instance);
            }

            _activeIndex = Mathf.Clamp(initialIndex, 0, _instances.Count - 1);
            ActiveAttachment?.EnableAttachment();
        }

        public bool SelectAttachment(int index)
        {
            if (_instances == null || index < 0 || index >= _instances.Count || index == _activeIndex)
                return false;

            ActiveAttachment?.DisableAttachment();
            _activeIndex = index;
            ActiveAttachment?.EnableAttachment();
            return true;
        }

        public bool CycleAttachments(bool forward)
        {
            if (Count < 2) return false;
            return SelectAttachment((_activeIndex + (forward ? 1 : Count - 1)) % Count);
        }

        public void RestoreSelection()
        {
            if (_instances == null) return;
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] == null) continue;
                if (i == _activeIndex) _instances[i].EnableAttachment();
                else _instances[i].DisableAttachment();
            }
        }
    }

    public class WeaponAttachment : MonoBehaviour
    {
        public virtual void EnableAttachment() => gameObject.SetActive(true);
        public virtual void DisableAttachment() => gameObject.SetActive(false);
        public virtual void UseAttachment() { }
    }
}
