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

    // Begin has set it up already.
    protected override void Start() { }

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
