using UnityEngine;

/// <summary>
/// Something the team picks up for a door or a device (the map gimmick card "열쇠·키패드와 단서"): a master key, a keycard, a
/// code read off a note, a can of disinfectant gas. Where it lies changes from run to run (spots, picked by the run's
/// seed: MapGimmicks.Roll), and a clue on the map says plainly where (GimmickClue): no searching blind, no vague hints.
/// Picked up, it is the team's: whoever stands at the door it fits can use it (MapGimmicks.Has), and a can used up is gone.
/// Its look is its children, hidden once it is taken. States: 0 lying, 1 held, 2 used up.
/// </summary>
public sealed class GimmickItem : GimmickSwitch
{
    private const byte Lying = 0, Held_ = 1, UsedUp = 2;

    [SerializeField, Tooltip("What it is called between switches: a lock's or a vent's item names it.")]
    private string id = "";
    [SerializeField, Tooltip("Texts id of its name (\"마스터키\").")]
    private string title = "";
    [SerializeField, Tooltip("Where it may lie; the run's seed picks one. Empty: where it was built.")]
    private Transform[] spots = new Transform[0];
    [SerializeField, Tooltip("Texts ids naming each of spots, for its clue (\"402호\").")]
    private string[] places = new string[0];
    [SerializeField, Tooltip("Its roll of the run's seed: items with different salts lie independently.")]
    private int salt;

    private int spot = -1;

    internal string Id => id;

    /// <summary>Its name as players read it.</summary>
    internal string Title => Texts.Get(title);

    /// <summary>Picked up and not used up.</summary>
    internal bool Held => State == Held_;

    /// <summary>Lying where it is, not yet found.</summary>
    internal bool Lies => State == Lying;

    /// <summary>The name of the place it lies this run, as a clue says it; empty before the seed is known.</summary>
    internal string Place => spot >= 0 && spot < places.Length ? Texts.Get(places[spot]) : "";

    internal override string Offer(PlayerMovement mine) => State == Lying ? Texts.Format("gimmick.item.take", Title) : null;

    internal override int Use(PlayerMovement mine) => State == Lying ? Held_ : -1;

    // Taken twice at once, it is taken once.
    internal override bool Accepts(byte now, byte proposed) => proposed > now;

    internal override void Seed(ushort seed)
    {
        if (spots == null || spots.Length == 0) return;
        spot = Mathf.Min(spots.Length - 1, Mathf.FloorToInt(MapGimmicks.Mix(seed, (uint)(salt * 977 + 31)) * spots.Length));
        var at = spots[spot];
        if (at != null) transform.SetPositionAndRotation(at.position, at.rotation);
    }

    internal override void Show(byte state, bool fresh)
    {
        bool lying = state == Lying;
        for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(lying);
        if (state == Held_ && fresh)
        {
            GimmickSounds.PlayAt(GimmickSounds.Cue.Jingle, transform.position, .9f, 2f, 18f);
            AlertHUD.Self(Texts.Format("gimmick.item.taken", Title));
        }
    }

    /// <summary>Used up (the gas let into a vent): the acting PC.</summary>
    internal void UseUp()
    {
        if (State == Held_) Set(UsedUp);
    }
}
