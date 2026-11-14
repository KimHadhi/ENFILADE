using UnityEngine;

/// <summary>
/// A security room's monitors (the map gimmick cards "톨게이트 관제실" and "역무실과 CCTV", after PAYDAY 2's Train Heist and
/// Beneath the Mountain): [F] watches the cameras, and for its seconds every zombie within its reach of the place they
/// look over shows through walls on every PC (GhostSight's veil, as a Ghost's ultimate shows them), sleeping crowds and
/// walkers alike, coming in as a wave from where the cameras look. It can be watched again after its wait. States: how
/// many times it has been watched.
/// </summary>
public sealed class GimmickCamera : GimmickSwitch
{
    [SerializeField, Tooltip("The place the cameras look over (its middle).")]
    private Transform watched;
    [SerializeField, Min(1f), Tooltip("How far round it zombies show (m).")]
    private float radius = 25f;
    [SerializeField, Min(1f), Tooltip("How long they show (s).")]
    private float seconds = 30f;
    [SerializeField, Min(0f), Tooltip("How long before it can be watched again (s).")]
    private float wait = 45f;
    [SerializeField, Tooltip("A board whose fuel cans the cameras also show (GimmickCanSpots); may be empty.")]
    private GimmickCanSpots board;

    private float shownAt = -999f;

    internal override string Offer(PlayerMovement mine)
        => Time.time - shownAt >= wait ? Texts.Get("gimmick.camera.watch") : Texts.Format("gimmick.camera.wait", Mathf.CeilToInt(wait - (Time.time - shownAt)));

    internal override int Use(PlayerMovement mine) => Time.time - shownAt >= wait ? (byte)(State + 1) : -1;

    // Two watching at once: one look.
    internal override bool Accepts(byte now, byte proposed) => proposed == (byte)(now + 1);

    internal override void Show(byte state, bool fresh)
    {
        shownAt = Time.time;
        if (!fresh || watched == null) return;
        GhostSight.Phantom(null, watched.position, seconds, radius);
        if (board != null) board.Tell();
        else AlertHUD.Self(Texts.Get("gimmick.camera.shown"));
    }
}
