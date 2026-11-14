using UnityEngine;

/// <summary>
/// Supplies to load into the transport before it goes (the map gimmick card "수송기에 더 싣기", after PAYDAY 3's Fear &amp;
/// Greed; the user's note on "더 있으면 더 받기": "돈은 부족할 일이 없어서 경험치 같은 걸로 하면 좋을 듯?"): during its
/// steps (the finale's hold and its escape), while the cargo door is open, [F] held at the stack by the ramp loads a
/// crate. Each one grows everyone's experience (Wallet.AddExperience: class growth, never money: Money.csv
/// greedExperience) and the horde with it (CampaignRun.Greed): load more and the reward and the danger climb together,
/// and the team decides when it is enough. Loaded stays loaded. States: crates loaded.
/// </summary>
public sealed class GimmickCargoLoad : GimmickSwitch
{
    [SerializeField, Tooltip("The steps it can be loaded in (CampaignSteps ids).")]
    private string[] steps = new string[0];
    [SerializeField, Tooltip("The stack's crates, each gone as it is loaded.")]
    private GameObject[] pile = new GameObject[0];
    [SerializeField, Tooltip("The hold's crates, each shown as it is loaded.")]
    private GameObject[] loaded = new GameObject[0];
    [SerializeField, Min(.2f), Tooltip("How long [F] is held to load one (s).")]
    private float loadSeconds = 2.5f;
    [SerializeField, Tooltip("The cargo door: nothing goes in while it is shut; may be empty.")]
    private GimmickCargoDoor door;
    [SerializeField, Min(0), Tooltip("More of the horde at once for each crate loaded.")]
    private int hordeMore = 2;
    [SerializeField, Min(0f), Tooltip("How much faster the horde comes for each crate loaded (a share).")]
    private float rateMore = .12f;

    private int Crates => Mathf.Max(pile.Length, 1);

    internal override float HoldSeconds => loadSeconds;

    private bool Open()
    {
        bool running = false;
        foreach (var id in steps) if (CampaignRun.IsRunning(id)) { running = true; break; }
        return running && (door == null || !door.Shut);
    }

    internal override string Offer(PlayerMovement mine)
    {
        if (State >= Crates) return Texts.Get("gimmick.cargo.full");
        if (!Open()) return null;
        return Texts.Format("gimmick.cargo.load", State, Crates, Wallet.GreedExperience);
    }

    internal override int Use(PlayerMovement mine) => State < Crates && Open() ? (byte)(State + 1) : -1;

    internal override bool Accepts(byte now, byte proposed) => proposed == now + 1;

    internal override void Show(byte state, bool fresh)
    {
        for (int i = 0; i < pile.Length; i++) if (pile[i] != null) pile[i].SetActive(i >= state);
        for (int i = 0; i < loaded.Length; i++) if (loaded[i] != null) loaded[i].SetActive(i < state);
        CampaignRun.Greed(state * hordeMore, 1f + state * rateMore);
        if (!fresh || state == 0) return;
        // Everyone's own experience on their own PC, the same for all: the crates are the team's.
        long experience = Wallet.GreedExperience;
        Wallet.AddExperience(experience);
        AlertHUD.Self(Texts.Format("gimmick.cargo.loaded", state, Crates, experience));
        GimmickSounds.PlayAt(GimmickSounds.Cue.Clunk, transform.position, .8f, 3f, 30f);
    }
}
