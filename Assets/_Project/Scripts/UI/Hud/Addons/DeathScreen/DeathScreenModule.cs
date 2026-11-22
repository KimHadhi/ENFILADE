using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The DeathScreen module: dying, from the MW3 Death Screen addon (the game's death card merged into it).
    /// Boot.Install calls Install when the local player's HUD comes up, again for every new local player, so it must be
    /// safe to call more than once.
    /// <para>The addon's screen as it is (lua/autorun/mw3_deathscreen.lua): the world's colour drained to a red-brown,
    /// blurred, a dark flecked vignette closing in, its blood fading in a moment later and its whistle once; coming back,
    /// the screen clears in .3 s and the whistle is cut. Drawn by DeathScreenPass into the world camera's picture, under
    /// the HUD as the addon's HUDPaintBackground drew it (not on an Overlay: DeathScreenPass says why), with the numbers
    /// and the whistle kept by DeathScreenDriver.</para>
    /// <para>Our death card (global::DeathCard: died or wiped, what killed you, time survived, kills, money, what comes
    /// next) shows over it, as the user asked on 2026-11-22: "여기에 우리 데스 정보 보여주는 거 합치면 돼". The game's death
    /// flow (global::DeathScreen: a room, alone, a wipe, spectating, coming back, /death) decides when and calls the entry
    /// points below at those moments, so one owner says whether this player's death is showing; it stands where the
    /// addon's Think hook watched ply:Alive().</para>
    /// </summary>
    public static class DeathScreenModule
    {
        private static readonly DeathScreenLook Defaults = new DeathScreenLook();

        /// <summary>The addon's "Enable the death screen" (mw3death_enabled): the one switch for this screen and the card.</summary>
        public static bool Enabled
        {
            get
            {
                var options = GameSettings.Current.addonDeathScreen;
                return options == null || options.enabled;
            }
        }

        /// <summary>Seconds the screen takes to clear (the addon's FADE_OUT_SCREEN, AddonDeathScreen.csv), for a card
        /// that wants to leave with it.</summary>
        public static float ClearSeconds
        {
            get
            {
                var driver = DeathScreenDriver.Existing;
                return (driver != null ? driver.Look : Defaults).clearSeconds;
            }
        }

        public static void Install()
        {
            var driver = DeathScreenDriver.Get();
            if (driver == null) return;
            DeathScreenPass.Hook();
            // Read now, while the HUD comes up: the whistle, the shader and the two pictures.
            driver.Prepare();
            DeathScreenPass.Prepare();
        }

        /// <summary>
        /// The addon's onDeath: the screen starts now (each part by its own clock) and the whistle plays once.
        /// <paramref name="preview"/>: a showing without dying (the addon's Preview button, our /death), which the
        /// Enable switch does not stop. Called again while it shows, it starts over, as the addon's does when a preview
        /// turns into a real death.
        /// </summary>
        public static void Die(bool preview = false)
        {
            Install();
            var driver = DeathScreenDriver.Existing;
            if (driver != null) driver.Die(preview);
        }

        /// <summary>The addon's onRespawn: the screen clears from wherever it had got (.3 s) and the whistle is cut (.06 s).</summary>
        public static void Respawn()
        {
            var driver = DeathScreenDriver.Existing;
            if (driver != null) driver.Respawn(true);
        }

        /// <summary>
        /// The screen clears as on onRespawn, but the whistle plays out: the player is still dead and the view hands over
        /// to spectating (our flow; the addon has no spectating). A preview's end is the addon's onRespawn (Respawn).
        /// </summary>
        public static void Leave()
        {
            var driver = DeathScreenDriver.Existing;
            if (driver != null) driver.Respawn(false);
        }

        /// <summary>Everything off at once, the whistle too (the addon's ShutDown hook): the player is going, or the card
        /// is held up for the HUD overlap check without the screen.</summary>
        public static void Stop()
        {
            var driver = DeathScreenDriver.Existing;
            if (driver != null) driver.Stop();
        }
    }
}
