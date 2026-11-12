using KINEMATION.FPSAnimationFramework.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The job of CheekWeldLayerSettings. Before Ads: where the butt is at the hip (the shoulder pocket) is kept; aimed, Ads
/// would put the sight on the eyes with the gun facing the body's way, the butt then lying a set way behind the sight; so
/// the eyes must go to the pocket less that. The neck, upper chest and chest turn toward it (a few passes, each bone within
/// its limit), and the head turns back to how it faced.
/// </summary>
public struct CheekWeldLayerJob : IAnimationJob, IAnimationLayerJob
{
    // A gun whose back end reaches this far behind its sight has a stock (m); a pistol's doesn't.
    private const float StockBehindSight = .2f;

    private CheekWeldLayerSettings settings;
    private LayerJobData jobData;
    private TransformStreamHandle head, neck, upperChest, chest, socket, weapon;
    private int aimingIndex;
    private float aiming, share;
    private Vector3 limits, buttLocal, buttFromSight;
    private bool stocked;

    public void ProcessAnimation(AnimationStream stream)
    {
        float weight = aiming * share * jobData.weight;
        if (!stocked || weight <= .001f) return;
        Vector3 eye = socket.GetPosition(stream), start = eye;
        Quaternion facing = jobData.rootHandle.GetRotation(stream);
        // The pocket is on the chest: it goes with the chest as the chest bends, rather than staying where it was in the
        // world (the butt then came out over the top of the shoulder, user 2026-11-08).
        Vector3 pocket = weapon.GetPosition(stream) + weapon.GetRotation(stream) * buttLocal;
        Vector3 pocketOnChest = Quaternion.Inverse(chest.GetRotation(stream)) * (pocket - chest.GetPosition(stream));
        Quaternion neckFrom = neck.GetRotation(stream), upperFrom = upperChest.GetRotation(stream), chestFrom = chest.GetRotation(stream);
        for (int pass = 0; pass < 3; pass++)
        {
            Bend(stream, neck, neckFrom, limits.x, ref eye, Goal(stream, start, pocketOnChest, facing, weight));
            Bend(stream, upperChest, upperFrom, limits.y, ref eye, Goal(stream, start, pocketOnChest, facing, weight));
            Bend(stream, chest, chestFrom, limits.z, ref eye, Goal(stream, start, pocketOnChest, facing, weight));
        }
        // The head goes down with the neck onto the stock, tipped toward it, as a shooter's does: turned back to look level
        // it was pushed out ahead like a turtle's (user, 2026-11-08). The view is the mouse's and the gun is lined up on the
        // body's facing (Ads), so neither tips with it.
    }

    // Where the eyes go for the butt to sit in the pocket as the chest is now, weight of the way from where they began.
    private Vector3 Goal(AnimationStream stream, Vector3 start, Vector3 pocketOnChest, Quaternion facing, float weight)
    {
        Vector3 onChestNow = chest.GetPosition(stream) + chest.GetRotation(stream) * pocketOnChest;
        return Vector3.Lerp(start, onChestNow - facing * buttFromSight, weight);
    }

    // Turns the bone so the eyes swing toward the goal about it, no further than limit from where it started.
    private void Bend(AnimationStream stream, TransformStreamHandle bone, Quaternion from, float limit, ref Vector3 eye, Vector3 goal)
    {
        Vector3 pivot = bone.GetPosition(stream);
        Vector3 toEye = eye - pivot, toGoal = goal - pivot;
        if (toEye.sqrMagnitude < 1e-8f || toGoal.sqrMagnitude < 1e-8f) return;
        Quaternion next = Quaternion.FromToRotation(toEye, toGoal) * bone.GetRotation(stream);
        float angle = Quaternion.Angle(from, next);
        if (angle > limit) next = Quaternion.Slerp(from, next, limit / angle);
        bone.SetRotation(stream, next);
        eye = socket.GetPosition(stream);
    }

    public void ProcessRootMotion(AnimationStream stream) { }

    public void Initialize(LayerJobData newJobData, FPSAnimatorLayerSettings newSettings)
    {
        jobData = newJobData;
        settings = (CheekWeldLayerSettings)newSettings;
        var rig = jobData.rigComponent;
        head = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.head));
        neck = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.neck));
        upperChest = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.upperChest));
        chest = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.chest));
        socket = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.cameraSocket));
        weapon = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.weaponIkBone));
        aimingIndex = jobData.inputController.GetPropertyIndex(FPSANames.AimingWeight);
    }

    public AnimationScriptPlayable CreatePlayable(PlayableGraph graph) => AnimationScriptPlayable.Create(graph, this);

    public FPSAnimatorLayerSettings GetSettingAsset() => settings;

    public void OnLayerLinked(FPSAnimatorLayerSettings newSettings) { }

    // The gun held (its model hangs from the IK weapon bone as it is): its butt and where that sits from its sight.
    public void UpdateEntity(FPSAnimatorEntity newEntity)
    {
        stocked = false;
        if (newEntity == null || newEntity.defaultAimPoint == null) return;
        Transform gun = newEntity.transform, aim = newEntity.defaultAimPoint;
        Vector3 aimLocal = gun.InverseTransformPoint(aim.position);
        Quaternion aimRotation = Quaternion.Inverse(gun.rotation) * aim.rotation;
        GunMuzzle.Back(gun, aim, .015f, out buttLocal, out _);
        buttFromSight = Quaternion.Inverse(aimRotation) * (buttLocal - aimLocal);
        stocked = buttFromSight.z < -StockBehindSight;
    }

    public void OnPreGameThreadUpdate() { }

    public void UpdatePlayableJobData(AnimationScriptPlayable playable, float weight)
    {
        jobData.weight = weight;
        aiming = aimingIndex >= 0 ? Mathf.Clamp01(jobData.inputController.GetValue<float>(aimingIndex)) : 0f;
        share = Mathf.Clamp01(settings.share);
        limits = settings.limits;
        playable.SetJobData(this);
    }

    public void LateUpdate() { }

    public void Destroy() { }
}
