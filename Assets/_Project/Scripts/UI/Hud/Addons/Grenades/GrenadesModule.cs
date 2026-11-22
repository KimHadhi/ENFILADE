using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The Grenades module: one incoming-grenade system from the MW Quick Grenade and Grenade Indicator addon (cook ring, incoming direction) and Arctic's Grenade Indicators (triangle, hologram), drawn on the shared layer (Overlay, Painter, HudFont, Pictures, Sound, Smooth).
    /// Boot.Install calls Install when the local player's HUD comes up, again for every new local player, so it must be
    /// safe to call more than once.
    /// <para>The game tells it what only the game knows: the cooking grenade in hand every frame (<see cref="Cook"/>, from
    /// PlayerGrenades), and the hand's draw, pin, throw and putting back (<see cref="PullOut"/>, <see cref="PinPulled"/>,
    /// <see cref="Throw"/>, <see cref="PutBack"/>) for the quick grenade's sounds. The live grenades it reads itself
    /// (ThrownGrenade.Live).
    /// The gun's crosshair steps aside while the fuse ring shows (<see cref="CrosshairHidden"/>), as the quick grenade, a
    /// weapon of its own, took the gun and its crosshair out of the hand.</para>
    /// </summary>
    public static class GrenadesModule
    {
        // Among the HUD's canvases (24-95), just under the hit markers' (26), where the old indicators sat.
        private const int SortOrder = 25;

        private static GrenadesHud hud;

        /// <summary>How long the cooking grenade in hand has burnt (s), below 0 for none; and its whole fuse (s).</summary>
        internal static float CookHeld { get; private set; } = -1f;
        internal static float CookFuse { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            hud = null;
            CookHeld = -1f;
            CookFuse = 0f;
        }

        public static void Install()
        {
            if (!Application.isPlaying || hud != null) return;
            var overlay = Overlay.Create("HUD addon: Grenades", SortOrder);
            hud = overlay.gameObject.AddComponent<GrenadesHud>();
            hud.Build(overlay);
        }

        /// <summary>
        /// Every frame from this PC's player's grenades: how long the cooking one in hand has burnt and how long its fuse
        /// is (s); held below 0 when none is held cooking (the ring then fades at the size it had, as the original's
        /// m_FragSafeTime stopped at the throw). The ring itself shows from half a second in (Ring.showAfter).
        /// </summary>
        public static void Cook(float held, float fuse)
        {
            CookHeld = held;
            if (held >= 0f) CookFuse = fuse;
        }

        /// <summary>
        /// The fuse ring is on the screen: the gun's crosshair steps aside meanwhile (WeaponController.UpdateCrosshair).
        /// </summary>
        public static bool CrosshairHidden => hud != null && hud.RingShown;

        /// <summary>
        /// This PC's player's off hand starts taking a grenade out (a held draw or a tap): the quick grenade's pull-out
        /// opens with a rattle of gear.
        /// </summary>
        public static void PullOut()
        {
            if (hud != null) hud.PullOut();
        }

        /// <summary>This PC's player's hand pulls the pin now: the quick grenade's pin sounds and the rattles after them.</summary>
        public static void PinPulled()
        {
            if (hud != null) hud.PinPulled();
        }

        /// <summary>
        /// This PC's player's off hand starts throwing, the grenade leaving it <paramref name="secondsToRelease"/> from now:
        /// the quick grenade's throw sounds, its whoosh 0.1 s before the release.
        /// </summary>
        public static void Throw(float secondsToRelease)
        {
            if (hud != null) hud.Throw(secondsToRelease);
        }

        /// <summary>The hand put the grenade back (the pin back in): the sounds still to come stay silent.</summary>
        public static void PutBack()
        {
            if (hud != null) hud.PutBack();
        }
    }
}
