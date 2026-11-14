using UnityEngine;

/// <summary>
/// The transport's cargo door in the finale (the map gimmick card "자원자 피날레", after Guts &amp; Blackpowder's San
/// Sebastian, where one volunteer works the lift and still wins; the user's note "근데 솔로 플레이면 어떡함?", answered "레버
/// 옆에 쐐기가 있어요"): when its step (the ride's last stretch) begins the crew shuts the door against the horde, and it
/// stands open only while someone stands at the lever by the ramp holding it, or for wedgeSeconds after a wedge is
/// knocked into the lever there ([F]: alone, wedge it and run aboard). Whoever holds it as the ride leaves, the others
/// aboard, counts as escaped and gets the "남은 사람" title (CampaignRun.Stays). Before its step the door is open. States:
/// the host's word, bits: 1 held, 2 wedged, 4 its step runs (shut unless held or wedged).
/// </summary>
public sealed class GimmickCargoDoor : GimmickSwitch
{
    private const byte Held = 1, Wedged = 2, Closing = 4;

    [SerializeField, Tooltip("The step that shuts it (the escape's CampaignSteps id).")]
    private string step = "";
    [SerializeField, Tooltip("The door's solid part, shown while shut.")]
    private GameObject door;
    [SerializeField, Tooltip("Where standing holds the lever (world box).")]
    private Bounds lever;
    [SerializeField, Min(1f), Tooltip("How long a wedge holds it open (s).")]
    private float wedgeSeconds = 12f;

    private float wedgedUntil = -1f;

    /// <summary>Shut now on this PC: nobody gets in or out.</summary>
    internal bool Shut => (State & Closing) != 0 && (State & (Held | Wedged)) == 0;

    internal override Vector3 Handle => lever.center;

    private void OnEnable() => CampaignRun.Stays(Holding);

    private void OnDisable() => CampaignRun.Stays(null);

    // Standing at the lever is holding it (the ride's escape: the one left outside who kept it open).
    private bool Holding(PlayerMovement someone) => someone != null && lever.Contains(someone.transform.position + Vector3.up * .9f);

    internal override string Offer(PlayerMovement mine)
    {
        if ((State & Closing) == 0) return Texts.Get("gimmick.cargodoor.open");
        if ((State & Wedged) != 0) return Texts.Format("gimmick.cargodoor.wedged", Mathf.CeilToInt(Mathf.Max(0f, wedgedUntil - Time.time)));
        return Texts.Get("gimmick.cargodoor.wedge");
    }

    internal override int Use(PlayerMovement mine) => (State & Closing) != 0 && (State & Wedged) == 0 ? (byte)(State | Wedged) : -1;

    internal override bool Accepts(byte now, byte proposed) => (now & Closing) != 0 && (proposed & Wedged) != 0;

    internal override byte Merge(byte now, byte proposed) => (byte)(now | Wedged);

    internal override void Tick(float dt, bool host)
    {
        if (!host) return;
        bool closing = CampaignRun.IsRunning(step);
        bool held = closing && AnyoneAt();
        bool wedged = closing && (State & Wedged) != 0 && Time.time < wedgedUntil;
        byte want = (byte)((closing ? Closing : 0) | (held ? Held : 0) | (wedged ? Wedged : 0));
        if (want != State) Set(want);
    }

    private bool AnyoneAt()
    {
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.IsInitialized && !someone.IsDead && Holding(someone)) return true;
        return false;
    }

    internal override void Show(byte state, bool fresh)
    {
        bool shut = (state & Closing) != 0 && (state & (Held | Wedged)) == 0;
        bool wasShut = door != null && door.activeSelf;
        if (door != null) door.SetActive(shut);
        // A wedge knocked in now: every PC times it from when it heard (the host's own clock pulls it).
        if ((state & Wedged) != 0 && wedgedUntil < Time.time) wedgedUntil = Time.time + wedgeSeconds;
        if (!fresh || door == null || shut == wasShut) return;
        GimmickSounds.PlayAt(shut ? GimmickSounds.Cue.Shutter : GimmickSounds.Cue.Lever, door.transform.position, .9f, 4f, 40f);
        var mine = Mine;
        if (mine != null && (mine.transform.position - door.transform.position).sqrMagnitude < 50f * 50f)
            AlertHUD.Self(Texts.Get(shut ? "gimmick.cargodoor.shut" : "gimmick.cargodoor.opened"));
    }
}
