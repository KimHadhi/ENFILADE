using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The EnemyBars module's rows on the settings menu's HUD tab: SettingsMenuView.HudTab calls BuildRows with the saved
    /// options (GameSettings.addonEnemyBars), and a row's change is saved and applied by the menu like every other row.
    /// Row names and notes come from Texts.csv. One row per convar of cl_bo6healthbar.lua (the addon has no panel; the
    /// server's and the player's on/off are one switch here, EnemyBarsOptions.enabled). The boss list is the console's,
    /// as the addon's is (EnemyBarsModule.AddBoss / RemoveBoss).
    /// </summary>
    public static class EnemyBarsOptionsRows
    {
        private static string[] OffOn => new[] { Texts.Get("settings.off"), Texts.Get("settings.on") };

        public static void BuildRows(SettingsRows rows, EnemyBarsOptions o)
        {
            if (rows == null || o == null) return;
            rows.Header(Texts.Get("addon.enemybars.header"));
            rows.Choice(Texts.Get("addon.enemybars.enabled"), OffOn, o.enabled ? 1 : 0, i => o.enabled = i == 1);
            rows.Choice(Texts.Get("addon.enemybars.name"), OffOn, o.name ? 1 : 0, i => o.name = i == 1);
            rows.Choice(Texts.Get("addon.enemybars.name_shadow"), OffOn, o.nameShadow ? 1 : 0, i => o.nameShadow = i == 1);
            rows.Choice(Texts.Get("addon.enemybars.damage_numbers"), OffOn, o.damageNumbers ? 1 : 0, i => o.damageNumbers = i == 1);
            rows.Choice(Texts.Get("addon.enemybars.alt_background"), OffOn, o.altBackground ? 1 : 0, i => o.altBackground = i == 1);
            // The slider runs to the crosshair ray's own reach (GetEyeTrace's 32768 units): past it no enemy can be aimed
            // at, whatever the convar allows. A larger saved number still shows, and the box takes any up to it.
            float reach = GameTables.Number(EnemyBarsLook.Table, EnemyBarsLook.AimRow, "traceLength", 32768f * EnemyBarsOptions.MetresPerUnit);
            float most = Mathf.Max(reach, o.maxDistance);
            rows.Slider(Texts.Get("addon.enemybars.max_distance"), 0f, most, o.maxDistance, v => o.maxDistance = v,
                v => Texts.Format("addon.enemybars.metres", v));
        }
    }
}
