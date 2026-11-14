using UnityEngine;

/// <summary>
/// A clue to which stand's airliner is fueled this run (the map gimmick card "기름 찬 비행기 찾기": the departure board, the
/// pilot's notebook, the tower's terminal; each says it plainly): [F] reads it out to everyone, marks the fueled valve a
/// while (AlertHUD.Find) and points the step's guide there (CampaignRun.Respot of spot), on every PC. Read once, it stays
/// read. States: 0 unread, 1 read.
/// </summary>
public sealed class GimmickFuelClue : GimmickSwitch
{
    private const float MarkSeconds = 40f;

    [SerializeField, Tooltip("Texts id of its [F] prompt (\"[F] 출발 안내판 보기\").")]
    private string prompt = "";
    [SerializeField, Tooltip("Texts id of what it says, {0} the fueled stand (\"출발 안내판 · 급유 완료: {0}\").")]
    private string line = "";
    [SerializeField, Tooltip("The stands' valves (GimmickFuelValve).")]
    private GimmickFuelValve[] valves = new GimmickFuelValve[0];
    [SerializeField, Tooltip("The step's spot moved to the fueled valve once read (the guide's arrow); may be empty.")]
    private string spot = "";

    internal override string Offer(PlayerMovement mine) => Texts.Get(prompt);

    // Read already, it says it again to this PC alone.
    internal override int Use(PlayerMovement mine)
    {
        if (State == 0) return 1;
        Tell();
        return -1;
    }

    internal override void Show(byte state, bool fresh)
    {
        var fueled = Fueled();
        if (state == 0 || fueled == null) return;
        if (!string.IsNullOrEmpty(spot)) CampaignRun.Respot(spot, fueled.transform);
        if (fresh) Tell();
    }

    private void Tell()
    {
        var fueled = Fueled();
        if (fueled == null) return;
        AlertHUD.Find(Texts.Format(line, Texts.Get(fueled.Place)), fueled.transform, MarkSeconds, () => fueled == null || fueled.State != 0);
    }

    private GimmickFuelValve Fueled()
    {
        foreach (var valve in valves) if (valve != null && valve.Fueled) return valve;
        return null;
    }
}
