using KINEMATION.RetargetPro.Runtime.Features;
using UnityEngine;

/// <summary>
/// KINEMATION Retarget Pro feature: the body stands to a shouldered gun as a rifleman does, the chest turned toward the
/// gun side so the support shoulder comes forward, the head turned back to look straight ahead. It runs after Retarget
/// Pro's mapping and before its FPS feature, which then puts both hands on the gun from the turned shoulders. A body that
/// faces square to the gun can't reach the handguard with its support hand once the stock is in its shoulder (user,
/// 2026-11-07: the left arm bent up off the gun in the aim). GunClipRetarget finds how far per body and gun (blade).
/// </summary>
public sealed class ShootingStanceFeature : RetargetFeature
{
    [Tooltip("How far the chest turns toward the gun side (degrees). 0 leaves the body as Retarget Pro maps it.")]
    public float blade;

    // How the turn is shared up the spine (Spine, Chest, UpperChest); a bone the body lacks gives its share to the rest.
    internal static readonly float[] Shares = { .3f, .35f, .35f };

    public override RetargetFeatureState CreateFeatureState() => new ShootingStanceState();

    public override string GetDisplayName() => $"Shooting stance ({blade:0.#}°)";
}

internal sealed class ShootingStanceState : RetargetFeatureState
{
    private ShootingStanceFeature asset;
    private readonly Transform[] spine = new Transform[3];
    private Transform neck, head;

    protected override void Initialize(RetargetFeature newAsset)
    {
        asset = newAsset as ShootingStanceFeature;
        Transform root = GetTargetRoot();
        spine[0] = PreviewHumanoid.Bone(root, HumanBodyBones.Spine);
        spine[1] = PreviewHumanoid.Bone(root, HumanBodyBones.Chest);
        spine[2] = PreviewHumanoid.Bone(root, HumanBodyBones.UpperChest);
        neck = PreviewHumanoid.Bone(root, HumanBodyBones.Neck);
        head = PreviewHumanoid.Bone(root, HumanBodyBones.Head);
    }

    public override bool IsValid() => asset != null && head != null && (spine[0] != null || spine[1] != null || spine[2] != null);

    public override void Retarget(float time = 0f)
    {
        float blade = asset.blade * asset.GetFeatureWeight(time);
        if (Mathf.Abs(blade) < .01f) return;
        Vector3 up = GetTargetRoot().up;
        float total = 0f;
        for (int i = 0; i < spine.Length; i++) if (spine[i] != null) total += ShootingStanceFeature.Shares[i];
        Quaternion looked = head.rotation;
        // Turning a bone about the body's up axis at its own pivot; each turn carries the bones above it.
        for (int i = 0; i < spine.Length; i++)
            if (spine[i] != null) spine[i].rotation = Quaternion.AngleAxis(blade * ShootingStanceFeature.Shares[i] / total, up) * spine[i].rotation;
        // The neck turns the head back to where it looked.
        Transform back = neck != null ? neck : head;
        back.rotation = looked * Quaternion.Inverse(head.rotation) * back.rotation;
    }
}
