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

    private float driven, shown;
    private float stretched = float.NaN;

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
