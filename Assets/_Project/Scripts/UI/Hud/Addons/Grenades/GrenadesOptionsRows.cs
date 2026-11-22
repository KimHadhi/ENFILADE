namespace HudAddons
{
    /// <summary>
    /// The Grenades module's rows on the settings menu's HUD tab: SettingsMenuView.HudTab calls BuildRows with the saved
    /// options (GameSettings.addonGrenades), and a row's change is saved and applied by the menu like every other row.
    /// Row names and notes come from Texts.csv.
    /// <para>The addons' own panels, in Korean and without a game's name: the quick grenade's client panel ("Grenade
    /// indicator", "Fuse time indicator") and Arctic's menu (its list of what else counts as a grenade, and its reset,
    /// which is this tab's reset).</para>
    /// </summary>
    public static class GrenadesOptionsRows
    {
        public static void BuildRows(SettingsRows rows, GrenadesOptions o)
        {
            if (rows == null || o == null) return;
            string[] offOn = { Texts.Get("settings.off"), Texts.Get("settings.on") };
            rows.Header(Texts.Get("settings.hud.grenades.quick"));
            rows.Choice(Texts.Get("settings.hud.grenades.indicator"), offOn, o.indicator ? 1 : 0, i => o.indicator = i == 1);
            rows.Choice(Texts.Get("settings.hud.grenades.fuse_indicator"), offOn, o.fuseIndicator ? 1 : 0, i => o.fuseIndicator = i == 1);
            rows.Header(Texts.Get("settings.hud.grenades.list"));
            rows.Choice(Texts.Get("settings.hud.grenades.list.claymore"), offOn, o.listClaymores ? 1 : 0, i => o.listClaymores = i == 1);
            rows.Note(Texts.Get("settings.hud.grenades.list.note"));
        }
    }
}
