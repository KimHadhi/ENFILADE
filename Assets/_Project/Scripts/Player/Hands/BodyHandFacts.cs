using UnityEngine;

/// <summary>
/// What the body is doing with its hands already, said to the hands' owner every frame it is so (HandOwner.State): dead,
/// down, in a zombie's grip, a hand or both on the world (TraversalHands says which a move takes), rolling out of a
/// landing, and the moves of other features that have the hands: an execution and its knife going back, a guard break,
/// a counter, a treatment by hand, a fuel can carried. These are facts, not requests: the owner never refuses one, and
/// each has whichever of its hands its rank can take (Tables/Player/HandUses.csv). A grenade in the fist is given up for
/// a hand on a ledge and kept through a landing roll by those ranks alone.
/// One place reads the body for all of them, on every PC, right after the movement has settled the frame's state
/// (FrameOrder.Movement + 1), so whoever asks the owner later in the frame is answered by this frame's body.
/// <para>
/// Until the lists it replaces are gone it also checks itself against them, for this PC's own player (Shadow): where
/// the owner's answer differs from what the old code says this frame (the gun's "put away", the movement's "hands
/// busy"), a line goes to Logs/hands.txt, so a difference is seen in a play's record before anything depends on the
/// owner for it. Each comparison goes when its old list does: the grenade's own list ("something must have the left
/// hand") went on 2026-11-18, when the grenade began asking the owner.
/// </para>
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Movement + 1)]
public sealed class BodyHandFacts : MonoBehaviour
{
    private PlayerMovement movement;
    private PlayerWeaponSlots slots;
    private HandOwner hands;
    // What the two comparisons last said (0 agree, 1 the old lists say yes and the owner no, 2 the other way).
    private readonly int[] differed = new int[2];

    private void Awake() => movement = GetComponent<PlayerMovement>();

    private void Update()
    {
        if (movement == null || !movement.IsInitialized) return;
        if (hands == null) hands = HandOwner.Of(movement);
        if (hands == null) return;
        if (slots == null) TryGetComponent(out slots);
        Say();
        if (PlayRecord.On && hands.Decides) Shadow();
    }

    private void Say()
    {
        // The body itself.
        if (movement.IsDead) hands.State(HandUses.Dead, this);
        else if (movement.IsDowned) hands.State(HandUses.Downed, this);
        if (movement.InZombieGrip) hands.State(HandUses.ZombieHold, this);
        switch (TraversalHands.For(movement))
        {
            case TraversalHandUse.LeftHand:
                hands.State(HandUses.Vault, this);
                break;
            case TraversalHandUse.Bare:
                // TraversalHands says "both hands" for a hand on the world and for hands put to the ground alike; they
                // differ for a grenade in the fist, which only the first makes the hand let go of.
                hands.State(movement.IsLandingRecovery ? HandUses.Roll : HandUses.Climb, this);
                break;
        }
        // Moves of other features that have the hands.
        bool executing = movement.Execution != null && movement.Execution.IsExecuting;
        if (executing) hands.State(HandUses.Execution, this);
        else if (ExecutionKnife.HoldsHands(movement)) hands.State(HandUses.Sheath, this);
        if (movement.MeleeCombat != null && movement.MeleeCombat.IsGuardBreaking) hands.State(HandUses.GuardBreak, this);
        if (movement.PerfectDefense != null && movement.PerfectDefense.IsCountering) hands.State(HandUses.Counter, this);
        if (movement.IsLocal && DetenteKit.HoldsHands) hands.State(HandUses.Bandage, this);
        if (slots != null && slots.HandsFull) hands.State(HandUses.Can, this);
    }

    // ---- The comparison with the lists that are going ----

    private void Shadow()
    {
        // 1. WeaponController's bare: is the gun put away? (With a can carried no gun is out at all: left out.)
        if (slots == null || !slots.HandsFull)
        {
            var right = hands.OwnerOf(HandSide.Right);
            bool awayOld = TraversalHands.For(movement) == TraversalHandUse.Bare || ExecutionKnife.HoldsHands(movement);
            Compare(0, "the gun is put away", awayOld, right != null && right.Gun == GunFate.Away);
        }
        // 2. PlayerMovement.CombatInputLocked: are the hands locked?
        Compare(1, "the hands are locked", movement.CombatInputLocked, hands.Locked);
    }

    private void Compare(int which, string what, bool old, bool owner)
    {
        int now = old == owner ? 0 : old ? 1 : 2;
        if (now == differed[which]) return;
        differed[which] = now;
        string where = $"state {movement.CurrentState}, traversal {TraversalHands.For(movement)}, left {Name(HandSide.Left)}, right {Name(HandSide.Right)}";
        hands.Remark(now == 0 ? $"shadow: \"{what}\" agrees again ({where})"
            : $"shadow: \"{what}\": the old lists say {(old ? "yes" : "no")}, the owner says {(owner ? "yes" : "no")} ({where})");
    }

    private string Name(HandSide hand)
    {
        var use = hands.OwnerOf(hand);
        return use != null ? use.Id : "nobody";
    }
}
