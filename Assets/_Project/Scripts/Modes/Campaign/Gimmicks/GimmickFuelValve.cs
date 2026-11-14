using UnityEngine;

/// <summary>
/// A parked airliner's fuel valve (the map gimmick card "기름 찬 비행기 찾기", after PAYDAY 2's Big Oil, whose engine guess
/// players called unclear, so here every clue says it plainly): the run's seed fills one of the stands' airliners, the
/// same pick on every PC (Pick); [F] at its valve, while its step runs, sends its fuel down the apron's hydrant line to the
/// transport and finishes the step. At an empty one [F] only says so: a wrong guess costs a walk, nothing more. The clues
/// (GimmickFuelClue) say which and mark it. States: 0 shut, 1 open (the fueled one only).
/// </summary>
public sealed class GimmickFuelValve : GimmickSwitch
{
    [SerializeField, Tooltip("Which stand it is (0 for the first).")]
    private int stand;
    [SerializeField, Min(1), Tooltip("How many stands share the pick.")]
    private int stands = 3;
    [SerializeField, Tooltip("The pick's roll of the run's seed: the same for every valve and clue of one pick.")]
    private int salt = 270;
    [SerializeField, Tooltip("The Gimmick step it finishes (CampaignSteps id).")]
    private string step = "";
    [SerializeField, Tooltip("Texts id of its stand's name (\"2번 계류장\").")]
    private string place = "";

    private bool seeded;
    private int picked = -1;

    /// <summary>Its stand's airliner holds the fuel this run (known once the seed is).</summary>
    internal bool Fueled => seeded && picked == stand;

    internal string Place => place;

    /// <summary>The stand the run's seed fills.</summary>
    internal static int Pick(ushort seed, int salt, int stands)
        => Mathf.Clamp(Mathf.FloorToInt(MapGimmicks.Mix(seed, (uint)salt) * stands), 0, Mathf.Max(0, stands - 1));

    internal override void Seed(ushort seed)
    {
        seeded = true;
        picked = Pick(seed, salt, stands);
    }

    internal override string Offer(PlayerMovement mine)
        => State == 0 && CampaignRun.IsRunning(step) ? Texts.Get("gimmick.fuelvalve.open") : null;

    internal override int Use(PlayerMovement mine)
    {
        if (State != 0 || !CampaignRun.IsRunning(step)) return -1;
        if (Fueled) return 1;
        AlertHUD.Self(Texts.Format("gimmick.fuelvalve.empty", Texts.Get(place)));
        GimmickSounds.PlayAt(GimmickSounds.Cue.Clunk, transform.position, .5f, 2f, 12f);
        return -1;
    }

    internal override void Used(PlayerMovement mine)
    {
        if (Fueled) CampaignRun.FinishStep(step);
    }

    internal override void Show(byte state, bool fresh)
    {
        if (!fresh || state != 1) return;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Hiss, transform.position, .9f, 3f, 30f);
        AlertHUD.Self(Texts.Get("gimmick.fuelvalve.flowing"));
    }
}
