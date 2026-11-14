using KINEMATION.FPSAnimationFramework.Runtime.Core;
using UnityEngine;

/// <summary>
/// KINEMATION FPS Animation Framework's central component (FPSAnimator) on our body. It runs after our player has fed
/// FPSAF's inputs (FPSBody, 295) and, late in the frame, after the body's own hands (PlayerBodyHands, 320), so the
/// camera's FPSAF controller (FPSBodyCamera) adds its shake and gun camera motion to the camera last.
/// It starts on an empty profile (nothing held) and with our camera's FPSAF controller, since our camera is not under
/// the body as FPSAF's demo camera is under its head.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework)]
public sealed class FPSBodyAnimator : FPSAnimator
{
    private System.Action afterLateUpdate;

    /// <summary>
    /// Sets FPSAF up at once on the profile it starts on, with our camera's controller and what runs after its late update.
    /// </summary>
    internal void Begin(FPSAnimatorProfile idle, FPSBodyCamera camera, System.Action onLateUpdated)
    {
        animatorProfile = idle;
        afterLateUpdate = onLateUpdated;
        Initialize();
        if (_cameraController == null && camera != null)
        {
            camera.Initialize();
            _cameraController = camera;
        }
    }

    /// <summary>
    /// A gun drawn (FPSAnimator.LinkAnimatorProfile with the gun's entity). Our bone controller keeps each gun's layers
    /// (FPSBodyBoneController), so a draw makes nothing anew and the body's Animator never re-binds for it.
    /// </summary>
    internal void Hold(GameObject gun)
    {
        if (gun == null || _boneController == null) return;
        if (_boneController is not FPSBodyBoneController bones)
        {
            LinkAnimatorProfile(gun);
            return;
        }
        var entity = gun.GetComponent<FPSAnimatorEntity>();
        if (entity == null) return;
        bones.UpdateEntity(entity);
        bones.Hold(entity.animatorProfile);
    }

    /// <summary>The held gun put away (FPSAnimator.UnlinkAnimatorProfile); its layers stay kept for its next draw.</summary>
    internal void LetGo()
    {
        if (_boneController is FPSBodyBoneController bones) bones.LetGo();
        else UnlinkAnimatorProfile();
    }

    /// <summary>
    /// Sets <paramref name="gun"/>'s layers up on this body ahead of its draw, changing nothing now: behind a loading screen,
    /// so its first draw sets nothing up either.
    /// </summary>
    internal void Prepare(GameObject gun)
    {
        if (gun == null || _boneController is not FPSBodyBoneController bones) return;
        var entity = gun.GetComponent<FPSAnimatorEntity>();
        if (entity != null) bones.Prepare(entity.animatorProfile);
    }

    // Begin has set it up already.
    protected override void Start() { }

    // The kept layers' native data is freed first; FPSAF's own Dispose then has only its own to free.
    protected override void OnDestroy()
    {
        if (_boneController is FPSBodyBoneController bones) bones.Release();
        base.OnDestroy();
    }

    protected override void Update()
    {
        if (!_isInitialized) return;
        base.Update();
    }

    protected override void LateUpdate()
    {
        if (!_isInitialized) return;
        base.LateUpdate();
        afterLateUpdate?.Invoke();
    }
}
