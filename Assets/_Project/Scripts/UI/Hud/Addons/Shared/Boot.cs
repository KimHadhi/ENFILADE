using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Brings the Garry's Mod HUD addon modules up when the local player's HUD comes up (PlayerMovement.EnsureInitialized):
    /// one module per topic, overlapping addons merged into one (PlayerHud: HUD Mk II; HitFeedback: the Black Ops VI hit
    /// marker, its score popup and Cool Killchains; EnemyBars: Black Ops 6 Zombies Healthbars; Grenades: MW Quick Grenade
    /// and Arctic's Grenade Indicators; DeathScreen: the MW3 death screen). It runs again for every new local player (a new
    /// map, a new room), so each module's Install() must be safe to call more than once.
    /// <para>They are the game's HUD for these topics (the user, 2026-11-22: "애드온을 넣으라고"): the hand-made copies of
    /// 0.9.304-0.9.317 are gone, and our own pieces the addons have an element for stand aside while they draw
    /// (PlayerHudModule.On).</para>
    /// </summary>
    public static class Boot
    {
        public static void Install()
        {
            if (!Application.isPlaying) return;
            // The addons' sound bank loads every clip it holds when first asked: here, during the load, not on the first hit.
            Sound.Prepare();
            PlayerHudModule.Install();
            HitFeedbackModule.Install();
            EnemyBarsModule.Install();
            GrenadesModule.Install();
            DeathScreenModule.Install();
        }
    }
}
