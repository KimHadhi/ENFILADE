using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The EnemyBars module: the enemies' health bars from the Black Ops 6 Zombies Healthbars addon, drawn on the shared layer (Overlay, Painter, HudFont, Pictures, Sound, Smooth).
    /// Boot.Install calls Install when the local player's HUD comes up, again for every new local player, so it must be
    /// safe to call more than once. The bars themselves are EnemyBarsHud's (Docs/Features/EnemyBars.md); it takes the
    /// enemies from EnemyHealth.Living and the hits from CombatDamageEvents.Damaged, so nothing in the game calls in.
    /// </summary>
    public static class EnemyBarsModule
    {
        // Under the hit markers, the kill feed and the blood on the screen (the HUD's pieces use 24 and up): the bar
        // belongs to the world it hangs in.
        private const int SortOrder = 22;

        private static EnemyBarsHud hud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => hud = null;

        public static void Install()
        {
            if (!Application.isPlaying || hud != null) return;
            var overlay = Overlay.Create("Enemy Bars", SortOrder);
            hud = overlay.gameObject.AddComponent<EnemyBarsHud>();
            hud.Setup(overlay);
        }

        /// <summary>
        /// Console: froze_bo6hbar_add_boss &lt;class&gt;, with a zombie kind (its name or its words): that kind wears the
        /// boss icon from now on, saved with the settings (the addon's data/bo6hbar/bosses.json). Empty: the list.
        /// </summary>
        public static string AddBoss(string args) => ChangeBoss(args, true);

        /// <summary>Console: froze_bo6hbar_remove_boss &lt;class&gt;: the kind is off the boss list again. Empty: the list.</summary>
        public static string RemoveBoss(string args) => ChangeBoss(args, false);

        private static string ChangeBoss(string args, bool add)
        {
            var options = GameSettings.Current.addonEnemyBars;
            int mask = EnemyBarsBosses.Mask(options.bossKinds);
            // The addon's commands do nothing without an argument; the console answers with the list as it stands.
            if (string.IsNullOrWhiteSpace(args)) return Texts.Format("addon.enemybars.cmd.list", EnemyBarsBosses.Names(mask));
            if (!EnemyBarsBosses.TryKind(args, out var kind))
                return Texts.Format("addon.enemybars.cmd.unknown", args.Trim(), EnemyBarsBosses.Names(EnemyBarsBosses.All));
            mask = add ? mask | EnemyBarsBosses.Bit(kind) : mask & ~EnemyBarsBosses.Bit(kind);
            options.bossKinds = EnemyBarsBosses.Write(mask);
            GameSettings.Apply();
            GameSettings.Save();
            return Texts.Format("addon.enemybars.cmd.list", EnemyBarsBosses.Names(mask));
        }
    }
}
