using UnityEngine;

/// <summary>
/// Where a Collect step's fuel cans lie this run, and the board that says so (the map gimmick card "밤 차량기지": "연료통
/// 자리는 판마다 바뀌고 입구 게시판이 알려 줘요"): the run's seed picks as many of its spots as there are cans, and the host
/// moves the cans there before anyone sees them (CampaignRun.RelocateCan; the cans' places go to the others with their
/// state). [F] at the board, or the station's cameras that look over the yard (GimmickCamera), tells everyone the places
/// plainly and marks the cans a while (AlertHUD.Find, as many as it shows at once). States: how many times it was read.
/// </summary>
public sealed class GimmickCanSpots : GimmickSwitch
{
    private const float MarkSeconds = 45f;

    [SerializeField, Tooltip("Where the cans may lie; the seed picks as many as the run has cans.")]
    private Transform[] spots = new Transform[0];
    [SerializeField, Tooltip("Texts ids naming each spot, for the board (\"3번 선로 열차 밑\").")]
    private string[] places = new string[0];
    [SerializeField, Min(1), Tooltip("How many cans the run has (CampaignRun's, in order).")]
    private int cans = 4;
    [SerializeField, Tooltip("Its roll of the run's seed.")]
    private int salt = 41;

    private int[] chosen = new int[0];

    internal override string Offer(PlayerMovement mine) => Texts.Get("gimmick.board.read");

    internal override int Use(PlayerMovement mine) => (byte)(State + 1);

    internal override bool Accepts(byte now, byte proposed) => proposed == (byte)(now + 1);

    internal override void Seed(ushort seed)
    {
        int count = Mathf.Min(cans, spots.Length);
        var order = new int[spots.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = Mathf.Min(i, Mathf.FloorToInt(MapGimmicks.Mix(seed, (uint)(salt * 131 + i)) * (i + 1)));
            (order[i], order[j]) = (order[j], order[i]);
        }
        chosen = new int[count];
        for (int i = 0; i < count; i++) chosen[i] = order[i];
        // The host lays the cans out; the others see them where the host's word puts them.
        if (!MapGimmicks.IsHost) return;
        for (int i = 0; i < count; i++)
            if (spots[chosen[i]] != null) CampaignRun.RelocateCan(i, spots[chosen[i]].position);
    }

    internal override void Show(byte state, bool fresh)
    {
        if (fresh) Tell();
    }

    /// <summary>Every PC: the places said, and the cans still lying marked a while (the board read, or the cameras).</summary>
    internal void Tell()
    {
        if (chosen.Length == 0) return;
        var words = new System.Text.StringBuilder();
        for (int i = 0; i < chosen.Length; i++)
        {
            if (i > 0) words.Append(" · ");
            words.Append(chosen[i] < places.Length ? Texts.Get(places[chosen[i]]) : "?");
        }
        AlertHUD.Self(Texts.Format("gimmick.board.cans", words.ToString()));
        for (int i = 0; i < chosen.Length; i++)
        {
            var at = spots[chosen[i]];
            if (at != null) AlertHUD.Place(at, MarkSeconds);
        }
    }
}
