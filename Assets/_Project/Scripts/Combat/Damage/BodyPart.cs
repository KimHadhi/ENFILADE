using UnityEngine;

/// <summary>
/// Where on a body a hit landed, for gore (ZombieGore): read from the zombie bone hitbox the bullet met (AttackTrace) and
/// carried in DamageHit and EnemyHitMessage, so every PC cuts the same part. None: not known (melee, a plain capsule, a
/// player).
/// </summary>
public enum BodyPart : byte
{
    None, Head, Torso,
    LeftUpperArm, LeftLowerArm, RightUpperArm, RightLowerArm,
    LeftUpperLeg, LeftLowerLeg, RightUpperLeg, RightLowerLeg,
}

public static class BodyParts
{
    public const int Count = 11;

    public static bool IsArm(BodyPart part) => part >= BodyPart.LeftUpperArm && part <= BodyPart.RightLowerArm;
    public static bool IsLeg(BodyPart part) => part >= BodyPart.LeftUpperLeg && part <= BodyPart.RightLowerLeg;
    public static bool IsLimb(BodyPart part) => IsArm(part) || IsLeg(part);

    /// <summary>The bone a part starts at: cutting it takes that bone and everything hanging from it.</summary>
    public static HumanBodyBones Root(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.Head: return HumanBodyBones.Head;
            case BodyPart.LeftUpperArm: return HumanBodyBones.LeftUpperArm;
            case BodyPart.LeftLowerArm: return HumanBodyBones.LeftLowerArm;
            case BodyPart.RightUpperArm: return HumanBodyBones.RightUpperArm;
            case BodyPart.RightLowerArm: return HumanBodyBones.RightLowerArm;
            case BodyPart.LeftUpperLeg: return HumanBodyBones.LeftUpperLeg;
            case BodyPart.LeftLowerLeg: return HumanBodyBones.LeftLowerLeg;
            case BodyPart.RightUpperLeg: return HumanBodyBones.RightUpperLeg;
            case BodyPart.RightLowerLeg: return HumanBodyBones.RightLowerLeg;
            default: return HumanBodyBones.LastBone;
        }
    }

    /// <summary>The part a lower arm or leg hangs from; None for the others.</summary>
    public static BodyPart Upper(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftLowerArm: return BodyPart.LeftUpperArm;
            case BodyPart.RightLowerArm: return BodyPart.RightUpperArm;
            case BodyPart.LeftLowerLeg: return BodyPart.LeftUpperLeg;
            case BodyPart.RightLowerLeg: return BodyPart.RightUpperLeg;
            default: return BodyPart.None;
        }
    }

    /// <summary>The part a human bone belongs to: hands with the forearms, feet with the shins, the jaw and eyes with the head.</summary>
    public static BodyPart OfBone(HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.Head: case HumanBodyBones.Jaw: case HumanBodyBones.LeftEye: case HumanBodyBones.RightEye:
                return BodyPart.Head;
            case HumanBodyBones.LeftUpperArm: return BodyPart.LeftUpperArm;
            case HumanBodyBones.LeftLowerArm: case HumanBodyBones.LeftHand: return BodyPart.LeftLowerArm;
            case HumanBodyBones.RightUpperArm: return BodyPart.RightUpperArm;
            case HumanBodyBones.RightLowerArm: case HumanBodyBones.RightHand: return BodyPart.RightLowerArm;
            case HumanBodyBones.LeftUpperLeg: return BodyPart.LeftUpperLeg;
            case HumanBodyBones.LeftLowerLeg: case HumanBodyBones.LeftFoot: case HumanBodyBones.LeftToes: return BodyPart.LeftLowerLeg;
            case HumanBodyBones.RightUpperLeg: return BodyPart.RightUpperLeg;
            case HumanBodyBones.RightLowerLeg: case HumanBodyBones.RightFoot: case HumanBodyBones.RightToes: return BodyPart.RightLowerLeg;
            case HumanBodyBones.Hips: case HumanBodyBones.Spine: case HumanBodyBones.Chest: case HumanBodyBones.UpperChest:
            case HumanBodyBones.Neck: case HumanBodyBones.LeftShoulder: case HumanBodyBones.RightShoulder:
                return BodyPart.Torso;
            default:
                return bone >= HumanBodyBones.LeftThumbProximal && bone <= HumanBodyBones.LeftLittleDistal ? BodyPart.LeftLowerArm
                    : bone >= HumanBodyBones.RightThumbProximal && bone <= HumanBodyBones.RightLittleDistal ? BodyPart.RightLowerArm
                    : BodyPart.None;
        }
    }

    /// <summary>A rough radius of the cut face (m), for the stump and its spurt.</summary>
    public static float CutRadius(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.Head: return .065f;
            case BodyPart.LeftUpperArm: case BodyPart.RightUpperArm: return .06f;
            case BodyPart.LeftLowerArm: case BodyPart.RightLowerArm: return .045f;
            case BodyPart.LeftUpperLeg: case BodyPart.RightUpperLeg: return .085f;
            case BodyPart.LeftLowerLeg: case BodyPart.RightLowerLeg: return .06f;
            default: return .05f;
        }
    }
}
