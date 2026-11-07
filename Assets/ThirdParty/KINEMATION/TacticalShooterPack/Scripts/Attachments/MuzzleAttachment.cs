// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Attachments
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Attachments/Muzzle")]
    public class MuzzleAttachment : WeaponAttachment
    {
        public bool isSuppressed = true;
        public Transform muzzlePoint;
    }
}
