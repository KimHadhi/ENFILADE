using UnityEngine;

/// <summary>
/// Through a reload both palms go where KINEMATION's own clip has them on the gun (FPSBody.HandContact): the arm bends until
/// the middle of the palm is there, the fingers pointing and the palm facing as the clip's hand does, the elbow staying on
/// the side the clip bends it to. So the support hand is on the magazine and the trigger hand on the grip or the charging
/// handle at every moment, on any character, which the remade clip's joint angles alone could only do for the reference
/// body. A character's palms move by its row in Tables/GunHands.csv. Fingers stay the clip's (GunFingers).
/// </summary>
public sealed partial class PlayerBodyHands
{
    private void HoldGunContacts()
    {
        var gunBody = FPSBody.Holding(weapons);
        if (gunBody == null) return;
        string skin = body.Skin != null ? body.Skin.Id : null;
        Follow(right, false);
        Follow(left, true);

        void Follow(ArmIK arm, bool isLeft)
        {
            if (arm == null || !gunBody.HandContact(isLeft, out Vector3 palm, out Vector3 fingers, out Vector3 facing, out float weight)) return;
            // As far as the hand is on the gun at all: a one-handed vault takes the left off it.
            weight *= weights[(int)Hold.Gun] * (isLeft ? 1f - leftOffGun : 1f);
            if (weight <= 0f) return;
            palm = GunHands.Palm(skin, isLeft, palm, fingers, facing);
            Quaternion rotation = arm.HandRotation(fingers, facing);
            arm.Solve(arm.WristAtPalm(palm, rotation), rotation, arm.Elbow, weight, 1f, shoulderReach);
        }
    }
}
