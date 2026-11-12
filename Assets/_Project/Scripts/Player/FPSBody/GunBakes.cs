using System;
using UnityEngine;

/// <summary>
/// KINEMATION's gun clips made for each of our bodies by KINEMATION Retarget Pro (Editor/Import/GunClipRetarget):
/// per skin and gun, the pose and the two reloads as humanoid clips, and where the gun sits in that body's pose, in the
/// body's own model space. Retarget Pro's FPS feature keeps both hands on the gun as KINEMATION's arms hold it, and puts
/// the gun where KINEMATION's camera sees it, relative to this body's eyes; a gun with a stock then sits with its butt in
/// the body's shoulder pocket. FPS Animation Framework's Pose Sampler starts the gun there (FPSBody). Filled in the
/// editor; loaded from Resources/Combat/GunBakes.
/// </summary>
public sealed class GunBakes : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        [Tooltip("The skin's model (PlayerBody profile) the clips are made for.")]
        public GameObject skin;
        public WeaponData weapon;
        public AnimationClip pose;
        public AnimationClip reloadTactical;
        public AnimationClip reloadEmpty;
        [Tooltip("A shell reload's shell and end (its two starts are Reload Tactical and Reload Empty).")]
        public AnimationClip reloadShell;
        public AnimationClip reloadEnd;
        [Tooltip("Drawing, putting away and looking the gun over.")]
        public AnimationClip draw;
        public AnimationClip holster;
        public AnimationClip inspect;
        [Tooltip("The action worked after every shot: a bolt, a pump (FPSBodyAssets.Gun.sourceFire).")]
        public AnimationClip fire;
        [Tooltip("Where the gun (KINEMATION's ik_hand_gun) sits in the pose, in the model's own space.")]
        public Vector3 gunPosition;
        public Quaternion gunRotation;
        [Tooltip("Where the right hand sits on the gun in the pose, as Retarget Pro's FPS feature put it there: in the gun bone's "
            + "own axes, in metres of the model at scale 1.")]
        public Vector3 rightGripPosition;
        [Tooltip("Where the left hand sits on the gun in the pose: in the gun bone's own axes, in metres of the model at scale 1.")]
        public Vector3 leftGripPosition;
        [Tooltip("The grip was measured (bakes from version 5 on).")]
        public bool hasGrip;
        [Tooltip("Where the gun sits from the body's eyes in the pose, in metres in game, in the frame of the eyes facing the way the "
            + "hips do (z ahead, y up): any other humanoid has the gun put there from its own eyes (FPSBody.FittedGunPose).")]
        public Vector3 gunFromEyes;
        public Quaternion gunTurnFromEyes;
        public bool hasEyeFrame;
        [Tooltip("What the clips were made from; a change makes the editor make them again.")]
        public string source;
    }

    public Entry[] entries = Array.Empty<Entry>();

    public const string ResourcePath = "Combat/GunBakes";
    private static GunBakes cached;

    public static GunBakes Instance => cached != null ? cached : cached = Resources.Load<GunBakes>(ResourcePath);

    /// <summary>
    /// The clips made for <paramref name="skin"/>, or else the reference body's: humanoid clips play on any humanoid, and the
    /// gun is fitted to the body's own eyes (FPSBody.FittedGunPose). Only the default skin is baked now.
    /// </summary>
    public bool TryGet(GameObject skin, WeaponData weapon, out Entry entry, out bool exact)
    {
        entry = default;
        exact = false;
        bool found = false;
        foreach (var candidate in entries)
        {
            if (candidate.weapon != weapon || candidate.pose == null) continue;
            if (candidate.skin == skin) { entry = candidate; exact = true; return true; }
            if (!found) { entry = candidate; found = true; }
        }
        return found;
    }
}
