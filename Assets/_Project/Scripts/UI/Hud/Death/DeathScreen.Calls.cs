
// DeathScreen: What the rest of the game calls. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    /// <summary>
    /// The card, the film and the sting made, and the blur drawn once at a strength nobody sees, behind the loading screen
    /// (SceneWarmup), so the first death never waits on them.
    /// </summary>
    public static void Prepare()
    {
        _ = DeathSting.Clip;
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out DeathScreen screen)) screen.Warm();
    }

    /// <summary>/death: the death screen without dying. Words: a zombie kind (brute, exploder …), fall, bleed or friend for
    /// the cause, wipe for 전멸, short for the short one.</summary>
    public static string Command(string args)
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.TryGetComponent(out DeathScreen screen)) return "이 PC의 플레이어가 아직 없습니다."; // dev-text
        return screen.StartPreview(args);
    }

    /// <summary>ENFILADE → HUD → 겹침 검사 (IHudPreview): the card in its settled place with sample words, no film or sound.</summary>
    public void Preview(bool show)
    {
        if (show)
        {
            if (phase != Phase.Hidden) Finish();
            held = true;
            card.Hold(Sample());
            return;
        }
        held = false;
        card.Hide();
    }
}
