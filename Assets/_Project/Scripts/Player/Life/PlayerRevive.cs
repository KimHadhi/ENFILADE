using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Holding Interact beside a downed friend gets them up. Letting go or stepping away starts it over. An execution on
/// offer comes first, because both use the same key.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerRevive : MonoBehaviour
{
    [SerializeField, Min(.1f), Tooltip("How long the key is held to get someone up (s).")]
    private float reviveSeconds = 4f;
    [SerializeField, Min(.5f), Tooltip("How close someone must be to reach them (m).")]
    private float reviveReach = 2f;

    private PlayerMovement player;
    private InputAction interact;

    /// <summary>The downed friend in reach, or null.</summary>
    public PlayerMovement Candidate { get; private set; }
    /// <summary>How far along getting them up is (0-1).</summary>
    public float Progress { get; private set; }
    /// <summary>How long getting someone up takes from start to finish (s).</summary>
    public float Seconds => Mathf.Max(.1f, reviveSeconds * PlayerPerks.ReviveFactor); // quicker with the rescue perk

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void Start()
    {
        var input = GetComponent<PlayerInput>();
        interact = (input != null ? input.actions : InputSystem.actions).OrNull()?.FindAction("Player/Interact", false);
    }

    private void Update()
    {
        var found = FindDowned();
        if (found != Candidate) Progress = 0f;
        Candidate = found;
        // Getting someone up is holding the key, and letting go starts it over: that is this move's own rule. A key a
        // screen has taken reads as let go like every other key then (PlayerMovement.InputAvailable is asked for the key
        // alone). The friend in reach stays the friend in reach meanwhile: who has the keys doesn't unmake the offer.
        if (Candidate == null || !player.InputAvailable || !interact.IsPressed()) { Progress = 0f; return; }
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
        // Reasons of the game only: this player can act and has their hands, and no execution is on offer for the same key.
        if (interact == null || !player.IsInitialized || !player.CanAct || player.IsDowned
            || player.CombatInputLocked || player.IsHeld || player.Execution.Candidate != null) return null;
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
