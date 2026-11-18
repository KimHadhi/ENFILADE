using UnityEngine;

/// <summary>
/// Holding Interact beside a downed friend gets them up. Letting go or stepping away starts it over. The key is shared:
/// whether the held key is this move's is the interact key's to say (InteractKey, row revive), where an execution on
/// offer comes first and a pickup after.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerRevive : MonoBehaviour
{
    /// <summary>Getting a downed friend up, as the interact key's one owner knows it (InteractKey).</summary>
    public const string OfferId = "revive";

    [SerializeField, Min(.1f), Tooltip("How long the key is held to get someone up (s).")]
    private float reviveSeconds = 4f;
    [SerializeField, Min(.5f), Tooltip("How close someone must be to reach them (m).")]
    private float reviveReach = 2f;

    private PlayerMovement player;

    /// <summary>The downed friend in reach, or null.</summary>
    public PlayerMovement Candidate { get; private set; }
    /// <summary>How far along getting them up is (0-1).</summary>
    public float Progress { get; private set; }
    /// <summary>How long getting someone up takes from start to finish (s).</summary>
    public float Seconds => Mathf.Max(.1f, reviveSeconds * PlayerPerks.ReviveFactor); // quicker with the rescue perk

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void Update()
    {
        var found = FindDowned();
        if (found != Candidate) Progress = 0f;
        Candidate = found;
        // Getting someone up is holding the key, and letting go starts it over: that is this move's own rule. It says
        // what it would do (Offer) and goes on only while the key's owner says the held key is its own (InteractKey.Held):
        // not while a screen has the keys (they read as let go, like every other key), not while something before it in
        // the key's order is on offer (a takedown), and not on a press already spent on something else. A key held on
        // the way here counts, as it always did. The friend in reach stays the friend in reach meanwhile: who has the
        // key doesn't unmake the offer.
        if (Candidate != null) InteractKey.Offer(OfferId, held: true, target: Candidate);
        if (Candidate == null || !InteractKey.Held(OfferId)) { Progress = 0f; return; }
        Progress += Time.deltaTime / Seconds;
        if (Progress < 1f) return;
        Progress = 0f;
        if (!Candidate.TryGetComponent(out PlayerNetworkCombat network)) return;
        network.Revive();
        PlayerRecords.Revived();
        // Getting a friend up fills the gauge, as fighting does (UltimateCharge.AwardHelp).
        UltimateCharge.AwardHelp(UltimateCharge.Help.Revived);
    }

    private PlayerMovement FindDowned()
    {
        // Reasons of the game only: this player can act and has their hands. An execution on offer for the same key no
        // longer hides the friend here: that it comes first is the key's order (InteractKey).
        if (!player.IsInitialized || !player.CanAct || player.IsDowned || player.CombatInputLocked || player.IsHeld) return null;
        PlayerMovement nearest = null;
        float best = reviveReach * reviveReach;
        foreach (var other in PlayerRoster.All)
        {
            if (other == player || !other.IsInitialized || !other.IsDowned) continue;
            float distance = (other.transform.position - transform.position).sqrMagnitude;
            if (distance <= best) { best = distance; nearest = other; }
        }
        return nearest;
    }
}
