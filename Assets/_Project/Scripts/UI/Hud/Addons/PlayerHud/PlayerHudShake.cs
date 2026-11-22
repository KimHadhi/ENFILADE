using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// HUD Mk II's damage shake (cl_effects.lua DoViewShake, a CalcView hook): for a moment after a hit the view rolls and
    /// turns on a fast sine, sin(CurTime() x 100) x intensity degrees of roll and cos(...) x 0.25 x intensity of yaw, the
    /// moment running out in shake_fade seconds. Garry's Mod adds it to the angles it draws with and nothing else; here it
    /// is added to the camera after every other look has been laid (after CameraViewPunch), and PlayerHudShakeRestore
    /// takes it off again first thing next frame, before gameplay reads the camera: aiming and shots keep the steady view.
    /// On the local player's view camera (PlayerHud adds it).
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ViewPunch + 1)]
    public sealed class PlayerHudShake : MonoBehaviour
    {
        private Quaternion steady, shaken;
        private bool applied;

        /// <summary>This frame's yaw and roll (degrees, Garry's Mod's sense: yaw left, roll right), set by PlayerHud.</summary>
        public float Yaw { get; set; }
        public float Roll { get; set; }

        private void LateUpdate()
        {
            if (Yaw == 0f && Roll == 0f) return;
            steady = transform.localRotation;
            // Unity turns the other way on both: its yaw is to the right, its roll counter-clockwise.
            shaken = steady * Quaternion.Euler(0f, -Yaw, -Roll);
            transform.localRotation = shaken;
            applied = true;
        }

        /// <summary>Takes the shake back off, unless something set the camera since (then that rotation stands).</summary>
        internal void Restore()
        {
            if (!applied) return;
            applied = false;
            if (transform.localRotation == shaken) transform.localRotation = steady;
        }

        private void OnDisable() => Restore();
    }
}
