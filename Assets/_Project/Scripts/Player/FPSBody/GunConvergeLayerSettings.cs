using System;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// An FPS Animation Framework layer of ours, just after its Ads: the gun turns about its muzzle until the barrel points at
/// what the crosshair is on, as the gun did before KINEMATION (WeaponPresentation, 0.5.0 to 0.7.34). The crosshair stays in
/// the middle of the screen and the gun comes to it, not the other way round (user, 2026-11-09). Like Ads it works before
/// Look, with the eyes at the camera socket looking the way the body faces; Look then turns the eyes and the gun together,
/// so the barrel meets the view's line at the crosshair's distance. Sway, recoil and the wall's pose go on top of it, and
/// FPSAF's IK brings the hands after it. Down the sights it lets go (Ads lines the sight up instead), and mid-reload,
/// drawing or looking the gun over the clip has the gun. FPSBody sets the weight and the distance every frame.
/// </summary>
public sealed class GunConvergeLayerSettings : FPSAnimatorLayerSettings
{
    // By the rig layout's index (FPSBodyRig), as the cheek weld's, never by name.
    public KRigElement cameraSocket = new KRigElement(FPSBodyRig.CameraSocketIndex, "CameraSocket");
    public KRigElement weaponIkBone = new KRigElement(FPSBodyRig.IkWeaponBoneIndex, FPSANames.IkWeaponBone);

    /// <summary>How much the barrel turns onto the crosshair's point, 0 to 1 (FPSBody, every frame).</summary>
    [NonSerialized] public float weight;
    /// <summary>How far ahead of the eyes the crosshair's point is (m, FPSBody, eased).</summary>
    [NonSerialized] public float distance = 20f;
    /// <summary>
    /// The held gun's muzzle and the way it points, in the IK weapon bone's space (FPSGun's, which the gun model hangs at
    /// rest under); FPSBody gives them every frame, since a suppressor put on moves the muzzle.
    /// </summary>
    [NonSerialized] public Vector3 muzzle, muzzleForward = Vector3.forward;
    [NonSerialized] public bool hasMuzzle;
    /// <summary>
    /// How far the gun is held out for the customize menu (0-1, FPSBody, eased): before the eyes and turned to show its
    /// side, as ArcCW's customize view holds it, the hands following it there.
    /// </summary>
    [NonSerialized] public float present;

    [Tooltip("Where the middle of the gun is held for the customize menu, from the eyes (m: right, up, ahead).")]
    public Vector3 displayAt = new Vector3(.1f, -.07f, .42f);
    [Tooltip("How it is turned there (degrees: pitch, yaw, roll); a yaw to the right shows its right side.")]
    public Vector3 displayTurn = new Vector3(-4f, 62f, -6f);

    public override IAnimationLayerJob CreateAnimationJob() => new GunConvergeLayerJob();
}
