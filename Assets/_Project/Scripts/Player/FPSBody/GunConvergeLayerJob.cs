using KINEMATION.FPSAnimationFramework.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The job of GunConvergeLayerSettings: the IK weapon bone turned about the gun's muzzle so the barrel points at the point
/// the given distance ahead of the camera socket, the way the body faces (the view, once Look has turned both).
/// </summary>
public struct GunConvergeLayerJob : IAnimationJob, IAnimationLayerJob
{
    private GunConvergeLayerSettings settings;
    private LayerJobData jobData;
    private TransformStreamHandle socket, weapon;
    private Vector3 muzzleLocal, muzzleForwardLocal;
    private bool hasMuzzle;
    private float weight, distance, present;
    private Vector3 displayAt, displayTurn;

    public void ProcessAnimation(AnimationStream stream)
    {
        Converge(stream);
        Present(stream);
    }

    // The customize menu's view (ArcCW's): the gun's middle (part way to its muzzle) held before the eyes, turned to show its
    // side, present of the way from its hold. Like the rest it is in the body's facing before Look, so it stays before the view.
    private void Present(AnimationStream stream)
    {
        float w = present * jobData.weight;
        if (!hasMuzzle || w <= .001f) return;
        Quaternion facing = jobData.rootHandle.GetRotation(stream);
        Vector3 gunAt = weapon.GetPosition(stream);
        Quaternion gunTurn = weapon.GetRotation(stream);
        Vector3 middle = muzzleLocal * .45f;
        Quaternion turn = Quaternion.Slerp(gunTurn, facing * Quaternion.Euler(displayTurn), w);
        Vector3 shown = Vector3.Lerp(gunAt + gunTurn * middle, socket.GetPosition(stream) + facing * displayAt, w);
        weapon.SetRotation(stream, turn);
        weapon.SetPosition(stream, shown - turn * middle);
    }

    private void Converge(AnimationStream stream)
    {
        float w = weight * jobData.weight;
        if (!hasMuzzle || w <= .001f) return;
        Quaternion facing = jobData.rootHandle.GetRotation(stream);
        Vector3 target = socket.GetPosition(stream) + facing * Vector3.forward * distance;
        Vector3 gunAt = weapon.GetPosition(stream);
        Quaternion gunTurn = weapon.GetRotation(stream);
        Vector3 muzzle = gunAt + gunTurn * muzzleLocal;
        Vector3 barrel = gunTurn * muzzleForwardLocal, toTarget = target - muzzle;
        // A point at or behind the muzzle (a wall right in front) has no way to point at: the wall's pose takes over there.
        if (toTarget.sqrMagnitude < 1e-4f || Vector3.Dot(toTarget, barrel) <= 0f) return;
        Quaternion turn = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(barrel, toTarget.normalized), w);
        weapon.SetRotation(stream, turn * gunTurn);
        weapon.SetPosition(stream, muzzle + turn * (gunAt - muzzle));
    }

    public void ProcessRootMotion(AnimationStream stream) { }

    public void Initialize(LayerJobData newJobData, FPSAnimatorLayerSettings newSettings)
    {
        jobData = newJobData;
        settings = (GunConvergeLayerSettings)newSettings;
        var rig = jobData.rigComponent;
        socket = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.cameraSocket));
        weapon = jobData.animator.BindStreamTransform(rig.GetRigTransform(settings.weaponIkBone));
    }

    public AnimationScriptPlayable CreatePlayable(PlayableGraph graph) => AnimationScriptPlayable.Create(graph, this);

    public FPSAnimatorLayerSettings GetSettingAsset() => settings;

    public void OnLayerLinked(FPSAnimatorLayerSettings newSettings) { }

    public void UpdateEntity(FPSAnimatorEntity newEntity) { }

    public void OnPreGameThreadUpdate() { }

    public void UpdatePlayableJobData(AnimationScriptPlayable playable, float layerWeight)
    {
        jobData.weight = layerWeight;
        weight = Mathf.Clamp01(settings.weight);
        distance = Mathf.Max(.1f, settings.distance);
        // The gun's muzzle as FPSGun measured it, for whatever is on it now.
        hasMuzzle = settings.hasMuzzle && settings.muzzleForward.sqrMagnitude > .5f;
        muzzleLocal = settings.muzzle;
        muzzleForwardLocal = settings.muzzleForward;
        present = Mathf.Clamp01(settings.present);
        displayAt = settings.displayAt;
        displayTurn = settings.displayTurn;
        playable.SetJobData(this);
    }

    public void LateUpdate() { }

    public void Destroy() { }
}
