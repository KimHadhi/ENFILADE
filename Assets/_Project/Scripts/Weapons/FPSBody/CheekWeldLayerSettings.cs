using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// An FPS Animation Framework layer of ours, just before its Ads: aiming a gun with a stock, the head comes down and
/// forward onto the gun, a cheek weld, instead of the gun coming up to a head that stays put. FPSAF hangs the gun from the
/// head and lines its sight up on the eyes, which with the stock 20-odd cm behind the sight put the stock out through the
/// back of the head in the F5 view (user picked the cheek weld, 2026-11-08). At the hip the butt rests in the shoulder
/// pocket (GunClipRetarget), so where it is there, this frame, is where it must stay: the neck and chest bend, within
/// their limits, until the eyes are where the sight would be with the butt there, and the head turns back to look as it
/// did, so the view never tips. Then Ads lines the sight up on the eyes as ever. FPSBody adds it to guns with a stock.
/// </summary>
public sealed class CheekWeldLayerSettings : FPSAnimatorLayerSettings
{
    // By the rig layout's index (FPSBodyRig), never by name: a body's own bones keep their own names (Hiro's and Sil's
    // aren't "Head" or "Chest"), and a lookup by name threw and stopped FPSAF linking every layer after this one (2026-11-08).
    public KRigElement head = new KRigElement(FPSBodyRig.HeadIndex, "Head");
    public KRigElement neck = new KRigElement(FPSBodyRig.NeckIndex, "Neck");
    public KRigElement upperChest = new KRigElement(FPSBodyRig.UpperChestIndex, "UpperChest");
    public KRigElement chest = new KRigElement(FPSBodyRig.ChestIndex, "Chest");
    public KRigElement cameraSocket = new KRigElement(FPSBodyRig.CameraSocketIndex, "CameraSocket");
    public KRigElement weaponIkBone = new KRigElement(FPSBodyRig.IkWeaponBoneIndex, FPSANames.IkWeaponBone);
    [Range(0f, 1f), Tooltip("How much of the way the eyes come to the gun (1: the butt stays in the shoulder).")]
    public float share = 1f;
    [Tooltip("The most each bone bends for it (degrees): neck, upper chest, chest.")]
    public Vector3 limits = new Vector3(40f, 20f, 12f);

    public override IAnimationLayerJob CreateAnimationJob() => new CheekWeldLayerJob();
}
