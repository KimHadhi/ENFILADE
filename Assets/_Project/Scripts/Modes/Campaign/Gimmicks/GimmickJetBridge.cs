using UnityEngine;

/// <summary>
/// A jet bridge that reaches out to a parked airliner's door (the map gimmick card "탑승교 운전"; the user's note "우리가
/// 운전 기능이 없지 않아?", answered "견인차처럼 누가 조종석 자리에 서 있는 동안 탑승교가 저절로 비행기 쪽으로
/// 움직여요"): while anyone stands in its cab (the host's count of who stands where) its telescoping end stretches toward
/// the plane, and stops when nobody does; docked, the plane's door opens on the way through its galley to the slide on
/// its far side, a short cut down to the apron by the control tower instead of the service stairs. Done once, it stays
/// done (a wipe leaves it docked). States: how far out, in twentieths (20 docked).
/// </summary>
public sealed class GimmickJetBridge : GimmickSwitch
{
    private const byte Docked = 20;

    [SerializeField, Tooltip("Where standing drives it (world box): its cab.")]
    private Bounds cab;
    [SerializeField, Tooltip("Its telescoping end: a holder at the cab's edge, its boxes along its local +x, stretched along x as it goes.")]
    private Transform stretch;
    [SerializeField, Min(.01f), Tooltip("Its end's length in when it starts (a share of the full length).")]
    private float shortest = .05f;
    [SerializeField, Min(1f), Tooltip("How long it must be driven to dock (s).")]
    private float seconds = 10f;
    [SerializeField, Tooltip("The plane's door, gone once it docks.")]
    private GameObject door;

    private float driven, shown;

    internal override Vector3 Handle => cab.center;

    internal override string Offer(PlayerMovement mine)
        => State >= Docked ? null : Texts.Format("gimmick.bridge.drive", Mathf.RoundToInt(State * 100f / Docked));

    internal override void Tick(float dt, bool host)
    {
        if (host && State < Docked && AnyoneIn(cab))
        {
            driven += dt;
            byte reached = (byte)Mathf.Min(Docked, Mathf.FloorToInt(driven / seconds * Docked));
            if (reached > State) Set(reached);
        }
        // It moves smoothly toward the state it has reached on this PC.
        shown = Mathf.MoveTowards(shown, State / (float)Docked, dt / seconds * 1.6f);
        if (stretch != null) stretch.localScale = new Vector3(Mathf.Lerp(shortest, 1f, shown), 1f, 1f);
    }

    private static bool AnyoneIn(Bounds box)
    {
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.IsInitialized && !someone.IsDead && box.Contains(someone.transform.position + Vector3.up * .9f)) return true;
        return false;
    }

    internal override void Show(byte state, bool fresh)
    {
        if (door != null) door.SetActive(state < Docked);
        if (!fresh)
        {
            // A late joiner sees it where it is.
            shown = state / (float)Docked;
            return;
        }
        if (state == 1) GimmickSounds.PlayAt(GimmickSounds.Cue.Lever, cab.center, .8f, 3f, 25f);
        if (state < Docked) return;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Clunk, cab.center, 1f, 4f, 40f);
        if (door != null) GimmickSounds.PlayAt(GimmickSounds.Cue.Unlock, door.transform.position, .8f, 3f, 25f);
        var mine = Mine;
        if (mine != null && (mine.transform.position - cab.center).sqrMagnitude < 40f * 40f) AlertHUD.Self(Texts.Get("gimmick.bridge.docked"));
    }
}
