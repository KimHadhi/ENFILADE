
// DeathScreen: What the rest of the game calls. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    /// <summary>
    /// The card made behind the loading screen (SceneWarmup), so the first death never waits on it. The addon's screen
    /// reads its own whistle, shader and pictures when it comes up (HudAddons.DeathScreenModule.Install).
    /// </summary>
    public static void Prepare()
    {
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out DeathScreen screen)) screen.card.Build();
    }

    /// <summary>/death: the death screen without dying. Words: a zombie kind (brute, exploder …), fall, bleed or friend for
    /// the cause, wipe for 전멸.</summary>
    public static string Command(string args)
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.TryGetComponent(out DeathScreen screen)) return "이 PC의 플레이어가 아직 없습니다."; // dev-text
        return screen.StartPreview(args);
    }

    /// <summary>ENFILADE → HUD → 겹침 검사 (IHudPreview): the card in its settled place with sample words, without the
    /// addon's screen or its whistle.</summary>
    public void Preview(bool show)
    {
        if (show)
        {
            if (phase != Phase.Hidden) Finish();
            HudAddons.DeathScreenModule.Stop();
            held = true;
            card.Hold(Sample());
            return;
        }
        held = false;
        card.Hide();
    }
}
