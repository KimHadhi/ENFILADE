// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;
using UnityEngine.Serialization;

namespace KINEMATION.TacticalShooterPack.Scripts.Attachments
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Attachments/Sight")]
    public class SightAttachment : WeaponAttachment
    {
        [FormerlySerializedAs("scopeData")]
        public SightAttachmentData sightData;
        public bool isIronSight;

        [Tooltip("Player camera FOV while aiming. Zero uses the sight data or weapon's aiming FOV.")]
        [Range(0f, 179f)] public float cameraAimFov;

        public virtual SightAttachmentData GetSightData()
        {
            var data = sightData;
            if (cameraAimFov > 0f) data.aimFov = Mathf.Clamp(cameraAimFov, 1f, 179f);
            return data;
        }
    }
}
