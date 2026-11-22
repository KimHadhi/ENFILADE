using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Runs before everything in the frame, CameraViewPunchRestore included, and takes last frame's damage shake
    /// (PlayerHudShake) off the camera, so gameplay reads the steady view and the view punch puts back what it found.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ViewPunchRestore - 1)]
    public sealed class PlayerHudShakeRestore : MonoBehaviour
    {
        private PlayerHudShake shake;

        private void Update()
        {
            if (shake == null) shake = GetComponent<PlayerHudShake>();
            if (shake != null) shake.Restore();
        }
    }
}
