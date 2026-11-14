using UnityEngine;

/// <summary>
/// A clue that says plainly where an item lies this run (the map gimmick card "열쇠·키패드와 단서": the caretaker's log, a
/// note, a screen; the user's research notes: players hate vague clues and searching blind, PAYDAY 2's Big Oil). [F]
/// reads it, for the whole team: every PC is told what it says, with the place the run's seed put the item in, and a mark
/// stands on the item a while until someone takes it (AlertHUD.Find). Read once, it stays read. States: 0 unread, 1 read.
/// </summary>
public sealed class GimmickClue : GimmickSwitch
{
    private const byte Unread = 0, Read = 1;
    // How long the item's mark stays after the clue is read (s).
    private const float MarkSeconds = 40f;

    [SerializeField, Tooltip("The item it tells of (GimmickItem.Id).")]
    private string item = "";
    [SerializeField, Tooltip("Texts id of what it says; {0} is the place the item lies this run.")]
    private string line = "";
    [SerializeField, Tooltip("Texts id of the [F] prompt (\"[F] 관리 일지 읽기\").")]
    private string prompt = "gimmick.clue.read";

    internal override string Offer(PlayerMovement mine) => State == Unread ? Texts.Get(prompt) : null;

    internal override int Use(PlayerMovement mine) => State == Unread ? Read : -1;

    internal override bool Accepts(byte now, byte proposed) => proposed > now;

    internal override void Show(byte state, bool fresh)
    {
        if (state != Read || !fresh) return;
        var found = MapGimmicks.ItemCalled(item);
        string place = found != null ? found.Place : "";
        string words = Texts.Format(line, place);
        if (found != null && found.Lies) AlertHUD.Find(words, found.transform, MarkSeconds, () => found == null || !found.Lies);
        else AlertHUD.Self(words);
    }
}
