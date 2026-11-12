/// <summary>
/// A HUD piece that can show one of its passing states on demand, so Tools → HUD → 겹침 검사 can measure it there without
/// waiting for the game to get into it (an assault's time row, a new objective's alert, a callout's window). The check
/// calls Preview(true) on every piece it finds, measures, and calls Preview(false); a piece shows its state with sample
/// content while asked and goes back to the game's own state after. Nothing in a preview may change the game.
/// </summary>
public interface IHudPreview
{
    void Preview(bool show);
}
