using KINEMATION.FPSAnimationFramework.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The job of GunConvergeLayerSettings: the IK weapon bone turned about the point the body holds the gun by (its muzzle
/// when none is given) so the barrel points at the point the given distance ahead of the camera socket, the way the body
/// faces (the view, once Look has turned both).
/// </summary>
public struct GunConvergeLayerJob : IAnimationJob, IAnimationLayerJob
{
    private GunConvergeLayerSettings settings;
    private LayerJobData jobData;
    private TransformStreamHandle socket, weapon;
    private Vector3 muzzleLocal, muzzleForwardLocal, pivotLocal;
    private bool hasMuzzle, hasPivot;
    private float weight, distance, nearestAhead, present;
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
        Vector3 eyesAt = socket.GetPosition(stream), ahead = facing * Vector3.forward;
        Vector3 gunAt = weapon.GetPosition(stream);
        Quaternion gunTurn = weapon.GetRotation(stream);
        // The point is never taken nearer than nearestAhead beyond the muzzle as the gun is held now. The muzzle sits beside
        // the eyes' line, so the turn onto a point on that line grows without bound as the point comes to the muzzle's own
        // distance, and past it there is nothing to point at (below), where the turn fell to none in one frame. A long gun's
        // muzzle is about a metre ahead of the eyes and the point was only kept half a metre from them (FPSBody.NearestAim),
        // so walking up to a wall swung the gun aside faster and faster, threw it across for a few frames and dropped it
        // back before the wall's pose had taken over (Logs/gun-actions.txt, 2026-11-17, read with this code: a Herrington
        // 20 cm to the left in 0.14 s at up to 390 cm/s, then 44 cm to the right in one frame, back three frames later).
        float muzzleAhead = Vector3.Dot(gunAt + gunTurn * muzzleLocal - eyesAt, ahead);
        Vector3 target = eyesAt + ahead * Mathf.Max(distance, muzzleAhead + nearestAhead);
        Vector3 pivot = gunAt + gunTurn * (hasPivot ? pivotLocal : muzzleLocal);
        Quaternion turn = Quaternion.identity;
        // Twice: turning about the pivot carries the muzzle with it, and the way from the muzzle to the point changes.
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 muzzle = pivot + turn * (gunAt + gunTurn * muzzleLocal - pivot);
            Vector3 barrel = turn * (gunTurn * muzzleForwardLocal), toTarget = target - muzzle;
            // A point at or behind the muzzle (a wall right in front) has no way to point at: the wall's pose takes over there.
            if (toTarget.sqrMagnitude < 1e-4f || Vector3.Dot(toTarget, barrel) <= 0f)
            {
                if (pass == 0) return;
                break;
            }
            turn = Quaternion.FromToRotation(barrel, toTarget.normalized) * turn;
        }
        turn = Quaternion.Slerp(Quaternion.identity, turn, w);
        weapon.SetRotation(stream, turn * gunTurn);
        weapon.SetPosition(stream, pivot + turn * (gunAt - pivot));
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
        nearestAhead = Mathf.Max(0f, settings.nearestAhead);
        // The gun's muzzle as FPSGun measured it, for whatever is on it now.
        hasMuzzle = settings.hasMuzzle && settings.muzzleForward.sqrMagnitude > .5f;
        muzzleLocal = settings.muzzle;
        muzzleForwardLocal = settings.muzzleForward;
        hasPivot = settings.hasPivot;
        pivotLocal = settings.pivot;
        present = Mathf.Clamp01(settings.present);
        displayAt = settings.displayAt;
        displayTurn = settings.displayTurn;
        playable.SetJobData(this);
    }

    public void LateUpdate() { }

    public void Destroy() { }
}
