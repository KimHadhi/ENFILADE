// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Attachments
{
    [DisallowMultipleComponent]
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Weapon Attachments")]
    public class TacticalWeaponAttachments : MonoBehaviour
    {
        public WeaponAttachmentGroup<SightAttachmentSelection> scopes;
        public WeaponAttachmentGroup<GripAttachment> grips;
        public WeaponAttachmentGroup<MuzzleAttachment> muzzle;
        public WeaponAttachmentGroup<WeaponAttachment> underbarrel;
        public GameObject[] ironSightObjects;

        public SightAttachment ActiveSight => scopes.ActiveAttachment as SightAttachment;
        public GripAttachment ActiveGrip => grips.ActiveAttachment as GripAttachment;
        public MuzzleAttachment ActiveMuzzle => muzzle.ActiveAttachment as MuzzleAttachment;
        public WeaponAttachment ActiveUnderbarrel => underbarrel.ActiveAttachment;
        public bool HasModularMuzzle => muzzle.Count > 0;

        private Camera _viewCamera;
        private int _firstPersonLayer;
        private bool _initialized;

        public virtual void Initialize(Camera viewCamera)
        {
            if (_initialized) return;
            _initialized = true;
            _viewCamera = viewCamera;
            _firstPersonLayer = LayerMask.NameToLayer("TacticalFirstPerson");
            if (_firstPersonLayer < 0) _firstPersonLayer = gameObject.layer;

            scopes.Initialize(CreateSight);
            grips.Initialize((prefab, parent) => CreateAttachment(prefab, parent));
            muzzle.Initialize((prefab, parent) => CreateAttachment(prefab, parent));
            underbarrel.Initialize((prefab, parent) => CreateAttachment(prefab, parent));
            UpdateIronSights();
        }

        private T CreateAttachment<T>(T prefab, Transform parent) where T : WeaponAttachment
        {
            if (prefab == null) return null;
            var instance = Instantiate(prefab, parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = _firstPersonLayer;
            return instance;
        }

        private WeaponAttachment CreateSight(SightAttachmentSelection selection, Transform parent)
        {
            var sight = CreateAttachment(selection.prefab, parent);
            if (sight is ScopeAttachment scope)
                scope.Initialize(_viewCamera, _firstPersonLayer, selection.secondaryScopes);
            return sight;
        }

        public virtual bool SelectAttachment(AttachmentType type, int index)
        {
            bool changed;
            switch (type)
            {
                case AttachmentType.Scope: changed = scopes.SelectAttachment(index); break;
                case AttachmentType.Grip: changed = grips.SelectAttachment(index); break;
                case AttachmentType.Muzzle: changed = muzzle.SelectAttachment(index); break;
                case AttachmentType.Underbarrel: changed = underbarrel.SelectAttachment(index); break;
                default: return false;
            }
            if (changed) UpdateIronSights();
            return changed;
        }

        public virtual bool CycleAttachments(AttachmentType type, bool forward = true)
        {
            bool changed;
            switch (type)
            {
                case AttachmentType.Scope: changed = scopes.CycleAttachments(forward); break;
                case AttachmentType.Grip: changed = grips.CycleAttachments(forward); break;
                case AttachmentType.Muzzle: changed = muzzle.CycleAttachments(forward); break;
                case AttachmentType.Underbarrel: changed = underbarrel.CycleAttachments(forward); break;
                default: return false;
            }
            if (changed) UpdateIronSights();
            return changed;
        }

        public virtual bool CycleScopes(bool forward = true)
            => ActiveSight is ScopeAttachment scope && scope.CycleScopes(forward);
        public virtual void ToggleUnderbarrel() => ActiveUnderbarrel?.UseAttachment();
        public virtual void UpdateScope(float aimingWeight)
        {
            if (ActiveSight is ScopeAttachment scope) scope.UpdateScope(aimingWeight);
        }

        public virtual void RestoreSelection()
        {
            scopes.RestoreSelection();
            grips.RestoreSelection();
            muzzle.RestoreSelection();
            underbarrel.RestoreSelection();
            UpdateIronSights();
        }

        private void UpdateIronSights()
        {
            if (ironSightObjects == null) return;
            bool visible = ActiveSight == null || ActiveSight.isIronSight;
            foreach (var sight in ironSightObjects)
                if (sight != null) sight.SetActive(visible);
        }
    }
}
