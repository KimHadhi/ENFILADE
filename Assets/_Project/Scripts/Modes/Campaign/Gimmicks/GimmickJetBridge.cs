using UnityEngine;

/// <summary>
/// A jet bridge that reaches out to a parked airliner's door (the map gimmick card "탑승교 운전"; the user's note "우리가
/// 운전 기능이 없지 않아?", answered "견인차처럼 누가 조종석 자리에 서 있는 동안 탑승교가 저절로 비행기 쪽으로
/// 움직여요"): while anyone stands in its cab (the host's count of who stands where) its telescoping end stretches toward
/// the plane, and stops when nobody does; docked, the plane's door opens on the way through its galley to the slide on
/// its far side, a short cut down to the apron by the control tower instead of the service stairs. Done once, it stays
/// done (a wipe leaves it docked). States: how far out, in twentieths (20 docked).
/// How it moves is authored by the builder (SetPieceTrack: telescoping lengths sliding out of one another, each a rigid
/// part on a kinematic body) and only sampled here, and only while the length shown is changing: before, during and
/// after, a bridge that stands still writes nothing.
/// How far out it shows is paced (SetPiecePace): the state comes in twentieths, one every half second of driving, and
/// shown as it came the bridge went out in twenty jerks, a third of a second of moving and a fifth of standing each
/// (the user, 2026-11-18: "탑승교 발판이 하나씩 생길 때마다 렉이 걸렸습니다"). It now goes on at the driving's pace
/// while the twentieths keep coming; the host, whose own count is exact, shows that.
/// </summary>
public sealed class GimmickJetBridge : GimmickSwitch
{
    private const byte Docked = 20;

    [SerializeField, Tooltip("Where standing drives it (world box): its cab.")]
    private Bounds cab;
    [SerializeField, Tooltip("Its telescoping lengths and how far each slides out (the builder's).")]
    private SetPieceTrack track = new SetPieceTrack();
    [SerializeField, Tooltip("A map built before 2026-11-18: its end as one holder stretched along x. Unused once the map is rebuilt.")]
    private Transform stretch;
    [SerializeField, Min(.01f), Tooltip("With stretch: the end's length in when it starts (a share of the full length).")]
    private float shortest = .05f;
    [SerializeField, Min(1f), Tooltip("How long it must be driven to dock (s).")]
    private float seconds = 10f;
    [SerializeField, Tooltip("The plane's door, gone once it docks.")]
    private GameObject door;

    private readonly SetPiecePace pace = new SetPiecePace("jet bridge");
    private float driven;
    private float stretched = float.NaN;

    internal override Vector3 Handle => cab.center;

    internal override string Offer(PlayerMovement mine)
        => State >= Docked ? null : Texts.Format("gimmick.bridge.drive", Mathf.RoundToInt(State * 100f / Docked));

    internal override void Tick(float dt, bool host)
    {
        // The host's own count never lags the state everyone has (a PC that became the host part of the way).
        if (host) driven = Mathf.Max(driven, State * seconds / Docked);
        if (host && State < Docked && AnyoneIn(cab))
        {
            driven += dt;
            byte reached = (byte)Mathf.Min(Docked, Mathf.FloorToInt(driven / seconds * Docked));
            if (reached > State) Set(reached);
        }
        // How far out it really is, by what this PC knows: the host its own count, anyone else the state's twentieth.
        float shown = pace.Follow(host ? Mathf.Clamp01(driven / seconds) : State / (float)Docked, 1f / seconds, dt);
        if (track.Any) track.Show(shown);
        // A map not rebuilt yet has only the stretched holder: as it was, but written only when it changes.
        else if (stretch != null && !Mathf.Approximately(stretched, shown))
        {
            stretched = shown;
            stretch.localScale = new Vector3(Mathf.Lerp(shortest, 1f, shown), 1f, 1f);
        }
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
            pace.Put(state / (float)Docked);
            return;
        }
        // For a play's record of long frames (Logs/hitches.txt): whether one falls on a step of the bridge.
        FrameTags.Mark("jet bridge " + state + "/" + Docked);
        if (state == 1) GimmickSounds.PlayAt(GimmickSounds.Cue.Lever, cab.center, .8f, 3f, 25f);
        if (state < Docked) return;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Clunk, cab.center, 1f, 4f, 40f);
        if (door != null) GimmickSounds.PlayAt(GimmickSounds.Cue.Unlock, door.transform.position, .8f, 3f, 25f);
        var mine = Mine;
        if (mine != null && (mine.transform.position - cab.center).sqrMagnitude < 40f * 40f) AlertHUD.Self(Texts.Get("gimmick.bridge.docked"));
    }
}
