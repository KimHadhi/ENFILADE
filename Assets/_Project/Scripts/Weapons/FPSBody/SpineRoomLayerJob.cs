using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The job of SpineRoomLayerSettings: of the bend Look gave the spine for a look down, what goes past the limit is turned
/// back, each bone by its share of it about the axis Look bent it about, and the head is left turned as Look had it.
/// </summary>
public struct SpineRoomLayerJob : IAnimationJob, IAnimationLayerJob
{
    private SpineRoomLayerSettings settings;
    private LayerJobData jobData;
    private NativeArray<TransformStreamHandle> spine;
    private NativeArray<float> angles;
    private TransformStreamHandle head;
    private bool hasHead;
    private int mouseIndex;
    private Vector4 look;
    private float most;

    public void ProcessAnimation(AnimationStream stream)
    {
        // As Look reads it: down is positive, a quarter turn is 1.
        float fraction = look.y / 90f, weight = jobData.weight;
        if (fraction <= 0f || weight <= .001f || !spine.IsCreated || spine.Length == 0) return;
        float bend = 0f;
        for (int i = 0; i < angles.Length; i++) bend += angles[i];
        bend *= fraction * weight;
        float back = bend - SpineRoom.Soft(bend, most);
        if (back < .01f || bend < 1e-3f) return;
        float share = back / bend;
        Quaternion headTurn = hasHead ? head.GetRotation(stream) : Quaternion.identity;
        // Look's own space for the pitch: the body's facing turned by the look's turn.
        Quaternion space = jobData.rootHandle.GetRotation(stream) * Quaternion.Euler(0f, look.x, 0f);
        for (int i = 0; i < spine.Length; i++)
        {
            var bone = spine[i];
            bone.SetRotation(stream, KAnimationMath.RotateInSpace(space, bone.GetRotation(stream),
                Quaternion.Euler(-angles[i] * fraction * weight * share, 0f, 0f), 1f));
        }
        if (hasHead) head.SetRotation(stream, headTurn);
    }

    public void ProcessRootMotion(AnimationStream stream) { }

    public void Initialize(LayerJobData newJobData, FPSAnimatorLayerSettings newSettings)
    {
        jobData = newJobData;
        settings = (SpineRoomLayerSettings)newSettings;
        var rig = jobData.rigComponent;
        int count = Mathf.Min(settings.spine.Count, settings.angles.Count);
        spine = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent);
        angles = new NativeArray<float>(count, Allocator.Persistent);
        for (int i = 0; i < count; i++)
        {
            spine[i] = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.spine[i]));
            angles[i] = settings.angles[i];
        }
        var headBone = settings.hasHead ? rig.GetRigTransform(settings.head) : null;
        hasHead = headBone != null;
        if (hasHead) head = jobData.animator.BindStreamTransform(headBone);
        mouseIndex = jobData.inputController.GetPropertyIndex(FPSANames.MouseInput);
    }

    public AnimationScriptPlayable CreatePlayable(PlayableGraph graph) => AnimationScriptPlayable.Create(graph, this);

    public FPSAnimatorLayerSettings GetSettingAsset() => settings;

    public void OnLayerLinked(FPSAnimatorLayerSettings newSettings) { }

    public void UpdateEntity(FPSAnimatorEntity newEntity) { }

    public void OnPreGameThreadUpdate() { }

    public void UpdatePlayableJobData(AnimationScriptPlayable playable, float layerWeight)
    {
        jobData.weight = layerWeight;
        look = mouseIndex >= 0 ? jobData.inputController.GetValue<Vector4>(mouseIndex) : Vector4.zero;
        most = settings.most;
        playable.SetJobData(this);
    }

    public void LateUpdate() { }

    public void Destroy()
    {
        if (spine.IsCreated) spine.Dispose();
        if (angles.IsCreated) angles.Dispose();
    }
}
