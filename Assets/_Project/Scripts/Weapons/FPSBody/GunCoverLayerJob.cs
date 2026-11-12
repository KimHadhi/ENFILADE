using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>The job of GunCoverLayerSettings: FPSAF's Collision layer pose, by our wall rule's weight.</summary>
public struct GunCoverLayerJob : IAnimationJob, IAnimationLayerJob
{
    private GunCoverLayerSettings settings;
    private LayerJobData jobData;
    private TransformStreamHandle weaponHandle;
    private int coverIndex, mouseIndex;
    private float cover, pitch;

    public void ProcessAnimation(AnimationStream stream)
    {
        if (cover <= 0f || !KAnimationMath.IsWeightRelevant(jobData.weight)) return;
        // As FPSAF's Collision layer: looking down takes the secondary pose.
        var blocking = settings.useSecondaryPose && pitch > 0f ? settings.secondaryPose : settings.primaryPose;
        var pose = new KPose
        {
            modifyMode = EModifyMode.Add,
            pose = KTransform.Lerp(KTransform.Identity, blocking, cover),
            space = settings.targetSpace
        };
        AnimLayerJobUtility.ModifyTransform(stream, jobData.rootHandle, weaponHandle, pose, jobData.weight);
    }

    public void ProcessRootMotion(AnimationStream stream) { }

    public void Initialize(LayerJobData newJobData, FPSAnimatorLayerSettings newSettings)
    {
        jobData = newJobData;
        settings = (GunCoverLayerSettings)newSettings;
        weaponHandle = jobData.animator.BindStreamTransform(jobData.rigComponent.GetRigTransform(settings.weaponIkBone));
        coverIndex = jobData.inputController.GetPropertyIndex(settings.coverInputProperty);
        mouseIndex = jobData.inputController.GetPropertyIndex(FPSANames.MouseInput);
    }

    public AnimationScriptPlayable CreatePlayable(PlayableGraph graph) => AnimationScriptPlayable.Create(graph, this);

    public FPSAnimatorLayerSettings GetSettingAsset() => settings;

    public void OnLayerLinked(FPSAnimatorLayerSettings newSettings) { }

    public void UpdateEntity(FPSAnimatorEntity newEntity) { }

    public void OnPreGameThreadUpdate() { }

    public void UpdatePlayableJobData(AnimationScriptPlayable playable, float weight)
    {
        jobData.weight = weight;
        cover = coverIndex >= 0 ? Mathf.Clamp01(jobData.inputController.GetValue<float>(coverIndex)) : 0f;
        pitch = mouseIndex >= 0 ? jobData.inputController.GetValue<Vector4>(mouseIndex).y : 0f;
        playable.SetJobData(this);
    }

    public void LateUpdate() { }

    public void Destroy() { }
}
