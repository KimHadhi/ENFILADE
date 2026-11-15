using KINEMATION.FPSAnimationFramework.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The job of GunActionLayerSettings: the IK weapon bone moved and turned from where it is by the action's move and turn,
/// in its own frame (which is the gun's). FPSAF's own targets for the hands and elbows hang under that bone and stay where
/// they are, as when the Pose Sampler places it: they are the body clip's hands, which the action's clip moves itself.
/// </summary>
public struct GunActionLayerJob : IAnimationJob, IAnimationLayerJob
{
    private GunActionLayerSettings settings;
    private LayerJobData jobData;
    private TransformStreamHandle weapon, rightHand, rightElbow, leftHand, leftElbow;
    private Vector3 move;
    private Quaternion turn;
    private float weight;

    public void ProcessAnimation(AnimationStream stream)
    {
        float w = weight * jobData.weight;
        if (w <= .0001f) return;
        Vector3 rightHandAt = rightHand.GetPosition(stream), rightElbowAt = rightElbow.GetPosition(stream);
        Vector3 leftHandAt = leftHand.GetPosition(stream), leftElbowAt = leftElbow.GetPosition(stream);
        Quaternion rightHandTurn = rightHand.GetRotation(stream), rightElbowTurn = rightElbow.GetRotation(stream);
        Quaternion leftHandTurn = leftHand.GetRotation(stream), leftElbowTurn = leftElbow.GetRotation(stream);

        Vector3 at = weapon.GetPosition(stream);
        Quaternion facing = weapon.GetRotation(stream);
        weapon.SetPosition(stream, at + facing * (move * w));
        weapon.SetRotation(stream, facing * Quaternion.Slerp(Quaternion.identity, turn, w));

        rightHand.SetPosition(stream, rightHandAt);
        rightHand.SetRotation(stream, rightHandTurn);
        rightElbow.SetPosition(stream, rightElbowAt);
        rightElbow.SetRotation(stream, rightElbowTurn);
        leftHand.SetPosition(stream, leftHandAt);
        leftHand.SetRotation(stream, leftHandTurn);
        leftElbow.SetPosition(stream, leftElbowAt);
        leftElbow.SetRotation(stream, leftElbowTurn);
    }

    public void ProcessRootMotion(AnimationStream stream) { }

    public void Initialize(LayerJobData newJobData, FPSAnimatorLayerSettings newSettings)
    {
        jobData = newJobData;
        settings = (GunActionLayerSettings)newSettings;
        var rig = jobData.rigComponent;
        var animator = jobData.animator;
        weapon = animator.BindStreamTransform(rig.GetRigTransform(settings.weaponIkBone));
        rightHand = animator.BindStreamTransform(rig.GetRigTransform(settings.ikRightHand));
        rightElbow = animator.BindStreamTransform(rig.GetRigTransform(settings.ikRightElbow));
        leftHand = animator.BindStreamTransform(rig.GetRigTransform(settings.ikLeftHand));
        leftElbow = animator.BindStreamTransform(rig.GetRigTransform(settings.ikLeftElbow));
        turn = Quaternion.identity;
    }

    public AnimationScriptPlayable CreatePlayable(PlayableGraph graph) => AnimationScriptPlayable.Create(graph, this);

    public FPSAnimatorLayerSettings GetSettingAsset() => settings;

    public void OnLayerLinked(FPSAnimatorLayerSettings newSettings) { }

    public void UpdateEntity(FPSAnimatorEntity newEntity) { }

    public void OnPreGameThreadUpdate() { }

    public void UpdatePlayableJobData(AnimationScriptPlayable playable, float layerWeight)
    {
        jobData.weight = layerWeight;
        move = settings.move;
        turn = settings.turn;
        weight = Mathf.Clamp01(settings.weight);
        playable.SetJobData(this);
    }

    public void LateUpdate() { }

    public void Destroy() { }
}
