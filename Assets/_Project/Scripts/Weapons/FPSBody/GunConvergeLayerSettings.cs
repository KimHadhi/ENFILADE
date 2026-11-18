using System;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// An FPS Animation Framework layer of ours, just after its Ads: the gun turns until the barrel points at what the
/// crosshair is on, as the gun did before KINEMATION (WeaponPresentation, 0.5.0 to 0.7.34). The crosshair stays in the
/// middle of the screen and the gun comes to it, not the other way round (user, 2026-11-09). It turns about the point the
/// body holds it by (pivot: a stock's butt in the shoulder, a handgun's grip in the hand), which stays where it is, as a
/// shooter's gun does. It once turned about its muzzle: the butt then swung out of the shoulder with every near target
/// (21 degrees for one a metre off), and a clip idling with its muzzle up was levelled at the height of its muzzle, across
/// the middle of the view (the Herrington; the user, 2026-11-14: "나 샷건을 왜 이따구로 듦?"). Like Ads it works before
/// Look, with the eyes at the camera socket looking the way the body faces; Look then turns the eyes and the gun together,
/// so the barrel meets the view's line at the crosshair's distance. Sway, recoil and the wall's pose go on top of it, and
/// FPSAF's IK brings the hands after it. Down the sights it lets go (Ads lines the sight up instead). Through a reload, a
/// draw or a look over the gun it stays on: the action moves the gun from its hold in the next layer (GunActionLayer).
/// FPSBody sets the weight and the distance every frame.
/// Its weight is the gun's own (FPSBody.FeedConverge) and nothing of FPSAF's: it once took Ads's blend with it, which
/// fades by FPSAF's share of the upper body, and so by the hands coming onto a gun just drawn (FPSBody.BodyCarries).
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
    /// <summary>The nearest point the barrel is turned onto lies this far ahead of its own muzzle (m, FPSBody).</summary>
    [NonSerialized] public float nearestAhead = .5f;
    /// <summary>
    /// The held gun's muzzle and the way it points, in the IK weapon bone's space (FPSGun's, which the gun model hangs at
    /// rest under); FPSBody gives them every frame, since a suppressor put on moves the muzzle.
    /// </summary>
    [NonSerialized] public Vector3 muzzle, muzzleForward = Vector3.forward;
    [NonSerialized] public bool hasMuzzle;
    /// <summary>
    /// The point of the gun that stays where it is as the barrel turns, in the IK weapon bone's space: what this body holds
    /// the gun by (GunHold.Pivot, through FPSGun.HoldPivot); FPSBody gives it every frame. Without one the gun turns about
    /// its muzzle.
    /// </summary>
    [NonSerialized] public Vector3 pivot;
    [NonSerialized] public bool hasPivot;
    /// <summary>
    /// How far the gun is held out for the customize menu (0-1, FPSBody, eased): before the eyes and turned to show its
    /// side, as ArcCW's customize view holds it, the hands following it there.
    /// </summary>
    [NonSerialized] public float present;

    [Tooltip("Where the middle of the gun is held for the customize menu, from the eyes (m: right, up, ahead).")]
    public Vector3 displayAt = new Vector3(.1f, -.07f, .42f);
    // The muzzle goes to the left (user, 2026-11-10): the right hand stays on the grip, and turning the gun the other way
    // (yaw 62, its right side shown, as it first was) bent that wrist back on itself. This is that pose mirrored.
    [Tooltip("How it is turned there (degrees: pitch, yaw, roll); a yaw to the left (negative) points the muzzle left and shows the gun's left side.")]
    public Vector3 displayTurn = new Vector3(-4f, -62f, 6f);

    public override IAnimationLayerJob CreateAnimationJob() => new GunConvergeLayerJob();
}
