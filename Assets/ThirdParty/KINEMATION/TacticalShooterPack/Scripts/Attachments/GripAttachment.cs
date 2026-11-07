// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Attachments
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Attachments/Grip")]
    public class GripAttachment : WeaponAttachment
    {
        public GripAttachmentData attachmentData;
        public virtual GripAttachmentData GetGripData() => attachmentData;
    }
}
