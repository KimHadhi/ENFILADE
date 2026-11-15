using System;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// An FPS Animation Framework layer of ours, just after the turn onto the crosshair (GunConvergeLayerSettings): what the
/// gun's own action playing now does to the gun (a reload, a draw, a holster, a look over it, its action worked after a
/// shot), put onto the gun where this body holds it. The move and the turn are the action's track's (GunTrackSet), in the
/// gun's own frame from where the gun's pose holds it; FPSBody gives them every frame with how much of the upper body the
/// action has in the animation's own mix (FPSBody.FeedAction), and holds the gun on the body meanwhile
/// (FPSBodyPlayables.GunOnBody), so the base they go onto is the hold itself, the same place the gun is in once the action
/// is over. Sway, recoil and the wall's pose go on top, as on the hold.
/// It is the one place an action moves the gun. Until 0.9.118 the animation left the gun in the right hand of the clip
/// remade for the body, and after the animation the body put the gun on a place it had remembered from the eyes, moved as
/// the track says, by a weight and a clock of its own (FPSBody.FollowContacts, FPSGun.contactStart). Once the track's
/// motion was over the gun stood on that remembered place until the clip ended, and then slid to where the animation
/// holds it and turned onto the crosshair (the user, 2026-11-15: "샷건이랑 smg 들 때 처음에 멈췄다가 원자리로 돌아옴").
/// </summary>
public sealed class GunActionLayerSettings : FPSAnimatorLayerSettings
{
    // By the rig layout's index (FPSBodyRig), as the turn onto the crosshair's, never by name.
    public KRigElement weaponIkBone = new KRigElement(FPSBodyRig.IkWeaponBoneIndex, FPSANames.IkWeaponBone);
    public KRigElement ikRightHand = new KRigElement(FPSBodyRig.IkRightHandIndex, FPSANames.IkRightHand);
    public KRigElement ikRightElbow = new KRigElement(FPSBodyRig.IkRightElbowIndex, FPSANames.IkRightElbow);
    public KRigElement ikLeftHand = new KRigElement(FPSBodyRig.IkLeftHandIndex, FPSANames.IkLeftHand);
    public KRigElement ikLeftElbow = new KRigElement(FPSBodyRig.IkLeftElbowIndex, FPSANames.IkLeftElbow);

    /// <summary>How the action has moved the gun from its hold (m, the gun's own frame) and turned it (FPSBody, every frame).</summary>
    [NonSerialized] public Vector3 move;
    [NonSerialized] public Quaternion turn = Quaternion.identity;
    /// <summary>How much of it goes on, 0 to 1: the action's share of the upper body in the animation's mix.</summary>
    [NonSerialized] public float weight;

    public override IAnimationLayerJob CreateAnimationJob() => new GunActionLayerJob();
}
