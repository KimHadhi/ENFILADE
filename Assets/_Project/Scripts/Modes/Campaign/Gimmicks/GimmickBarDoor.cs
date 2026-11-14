using UnityEngine;

/// <summary>
/// A doorway the team can bar (the map gimmick card "철창에 갇힌 좀비 창고": "그 전에 창고 문을 막아 둘 수 있어요"): [F]
/// wedges boards across it, a solid block that carves the walk mesh, and [F] again pulls them away. Barred, what is behind
/// it stays there (the cages' crowd when it bursts). States: 0 open, 1 barred (and on round again).
/// </summary>
public sealed class GimmickBarDoor : GimmickSwitch
{
    [SerializeField, Tooltip("The boards across the doorway (a solid box that carves), shown while barred.")]
    private GameObject boards;

    private bool Barred => (State & 1) == 1;

    private void Start()
    {
        if (boards != null) boards.SetActive(Barred);
    }

    internal override Vector3 Handle => boards != null ? boards.transform.position : transform.position;

    internal override string Offer(PlayerMovement mine) => Texts.Get(Barred ? "gimmick.bar.open" : "gimmick.bar.shut");

    internal override int Use(PlayerMovement mine) => (byte)(State + 1);

    internal override bool Accepts(byte now, byte proposed) => proposed == (byte)(now + 1);

    internal override void Show(byte state, bool fresh)
    {
        if (boards != null) boards.SetActive(Barred);
        if (fresh) GimmickSounds.PlayAt(GimmickSounds.Cue.Clunk, Handle, .8f, 2f, 18f);
    }
}
