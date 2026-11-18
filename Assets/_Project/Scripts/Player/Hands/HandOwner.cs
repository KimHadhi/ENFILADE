using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Whose each of a player's two hands is, right now: the one place that says so. A hand has one owner, and whatever
/// uses a hand asks here by the name of its use (a row of Tables/Player/HandUses.csv) instead of working out for itself
/// whether the hand is free. Until 2026-11-18 every user kept a list of its own (a grenade's, a point's, a syringe's,
/// a tool's, a guard pose's, the gun's), the lists disagreed, and the gun did not know its left hand was away: a reload
/// asked for with a grenade held began and was stopped a frame later. The user's question that day: "오른손은 조준한 채로
/// 왼손은 수류탄 대기하고 있으면 되잖아요. 이런 왼손 오른손 기능 따로 쓰려고 하는데 구조 설계 잘 돼 있죠?", and, told
/// it was only half so, "문제들 다 근본적으로 고칠 수 있게 시작해요". The design is Docs/Features/Hands.md.
/// <para>
/// The rule itself is HandBoard's and the ranks are the table's; this keeps one board for one player, on every PC (a
/// friend's copy poses arms too, from what their PC says, so its hands are arbitrated the same way and show the same).
/// A use is said every frame it lasts: an ask with <see cref="Take"/>, a fact of the body with <see cref="State"/>.
/// One not said again is over a frame later, so nothing is ever taken and forgotten (as InteractKey's offers are); a
/// paused game, or a player whose own time stands still (PlayerMovement.Pace), says nothing and loses nothing.
/// <see cref="Give"/> hands a hand back at once.
/// </para>
/// <para>
/// What an owner of a hand asks back: <see cref="Has"/> (is it still mine: an ask that lost a hand it needs is over,
/// and its owner puts its own thing away), <see cref="Up"/> (mine, and the body is in a state my arm can be up in: a
/// grenade in the fist waits through a dodge), <see cref="Arm"/> (the same, asked by the arm that poses the left
/// hand: saying yes lends that hand off the gun through PlayerBodyHands.LendOffHand, which nothing else calls).
/// The acting PC decides: only this PC's own player is ever refused anything that matters (a reload, a dose); a copy's
/// board only picks which arm pose shows.
/// </para>
/// Checked without playing: HandsCheck and Tools/Checks/hands_check.py run the rule; the console's /hands shows the
/// owners now; and every change of owner and every refusal of this PC's player goes to Logs/hands.txt.
/// </summary>
[DisallowMultipleComponent]
public sealed class HandOwner : MonoBehaviour
{
    public const string RecordName = "hands";

    private const string RecordHeading = "whose hand (HandOwner): a hand changed owner: hand: from → to (rank) | refused: the use asked (rank): the hand it "
        + "could not have and whose it is (rank). The ranks are Tables/Player/HandUses.csv; the same refusal is written once until something changes";
    // How many of the last lines the console's /hands shows.
    private const int LinesKept = 12;

    private readonly HandBoard board = new HandBoard();
    private readonly List<HandBoard.Entry> stale = new List<HandBoard.Entry>();
    private readonly List<string> recent = new List<string>();
    private PlayerMovement movement;
    private PlayerBodyHands bodyHands;
    // tick: the frames of the game that ran (a paused frame is none); turned: the frame of the engine last looked at;
    // armAsked: the tick an arm last asked for the left hand off the gun, and armRow the use it asked for.
    private int tick, turned = -1, armAsked = -1;
    private string armRow;
    private string lastRefusal;

    /// <summary>A player's hand owner, made the first time it is asked for; null without a player.</summary>
    public static HandOwner Of(PlayerMovement who)
    {
        if (who == null) return null;
        return who.TryGetComponent(out HandOwner owner) ? owner : who.gameObject.AddComponent<HandOwner>();
    }

    /// <summary>This PC decides for this player: its own, not a copy of a friend's.</summary>
    public bool Decides => movement != null && movement.IsLocal && !movement.IsNetworkDriven;

    /// <summary>The uses standing now, in the order they came (the console's /hands, the checks).</summary>
    public IReadOnlyList<HandBoard.Entry> Standing
    {
        get
        {
            Turn();
            return board.Entries;
        }
    }

    /// <summary>The last changes of owner and refusals, oldest first.</summary>
    public IReadOnlyList<string> Recent => recent;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        bodyHands = GetComponent<PlayerBodyHands>();
        board.Changed += OnChanged;
    }

    private void OnDestroy() => board.Changed -= OnChanged;

    private void OnDisable()
    {
        board.Clear();
        if (bodyHands != null) bodyHands.LendOffHand(this, 0f);
        armAsked = -1;
    }

    // Every frame, before the hands are placed (PlayerBodyHands, LateUpdate): the left hand is off the gun for as long as
    // an arm asked for it in the frame before (the arms pose after the hands, so their word is a frame old here, as it
    // was when each of them called LendOffHand for itself).
    private void Update()
    {
        Turn();
        if (bodyHands == null) bodyHands = GetComponent<PlayerBodyHands>();
        if (bodyHands != null) bodyHands.LendOffHand(this, armAsked >= tick - 1 && armAsked >= 0 ? 1f : 0f);
    }

    // ---- Asking ----

    /// <summary>
    /// <paramref name="who"/> asks for the use <paramref name="id"/>: every hand it needs, or nothing. Said every frame
    /// the use lasts (asking again costs nothing); not said for a frame, it is over. Asking for another use is a change
    /// of row (the gun going from its hold to a reload): refused, the row it had stands. False: refused, and for this
    /// PC's own player the record says which hand it could not have and whose that is. <paramref name="noted"/> false:
    /// a refusal is not written (what is only said again every frame, the gun's own row, a throw already under way:
    /// the player asked for nothing new).
    /// </summary>
    public bool Take(string id, Object who, bool noted = true)
    {
        var use = HandUses.Of(id);
        if (use == null || who == null) return false;
        Turn();
        if (board.Take(use, who, tick, out var why)) return true;
        if (noted) Refused(why);
        return false;
    }

    /// <summary>
    /// Whether <paramref name="who"/> would get <paramref name="id"/> if it asked now; nothing changes. What it holds
    /// itself is not in its own way. <paramref name="noted"/>: a no goes to the record as a refusal (a key the player
    /// pressed for it), not for a question asked every frame.
    /// </summary>
    public bool Could(string id, Object who, bool noted = false)
    {
        var use = HandUses.Of(id);
        if (use == null) return false;
        Turn();
        if (board.Could(use, who, tick, out var why)) return true;
        if (noted) Refused(why);
        return false;
    }

    /// <summary>
    /// Whether what keeps <paramref name="who"/> from <paramref name="id"/> now is something else that asked for a hand
    /// and has it (a grenade held ready, a syringe): the hand is busy elsewhere, so whoever asks does the other thing it
    /// can (V kicks instead of swinging the gun). False when it could have the use, and false when a fact of the body
    /// has the hand (a zombie's grip, a ledge): what V is in such a state is that state's own rule.
    /// </summary>
    public bool Kept(string id, Object who)
    {
        var use = HandUses.Of(id);
        if (use == null) return false;
        Turn();
        return board.Kept(use, who, tick);
    }

    /// <summary>
    /// A fact of the body, said by whoever it is a fact of, every frame it is so (dead, grabbed, on a ledge, rolling):
    /// never refused, and it has whichever of its hands its rank can take. Not said for a frame, it is over.
    /// </summary>
    public void State(string id, Object who)
    {
        var use = HandUses.Of(id);
        if (use == null || who == null) return;
        Turn();
        board.State(use, who, tick);
    }

    /// <summary>What <paramref name="who"/> asked for is given back now (the hand goes to whoever still wants it most).</summary>
    public void Give(Object who)
    {
        if (who == null) return;
        Turn();
        board.Give(who);
    }

    /// <summary>What <paramref name="who"/> asked for is still its own. False once it lost a hand it needs, or never had it.</summary>
    public bool Has(Object who)
    {
        Turn();
        return board.Has(who);
    }

    /// <summary>
    /// The use <paramref name="id"/> stands with every hand it needs, and the body is in a state its arm can be up in
    /// (HandUses.csv stances). False in another state while the hand is still its own: what it holds waits.
    /// </summary>
    public bool Up(string id)
    {
        Turn();
        var entry = board.Standing(id);
        return entry != null && (movement == null || entry.Use.StandsIn(movement.CurrentState));
    }

    /// <summary>The body is in a state the arm of <paramref name="id"/> can be up in, whoever has the hand.</summary>
    public bool Stands(string id)
    {
        var use = HandUses.Of(id);
        return use != null && (movement == null || use.StandsIn(movement.CurrentState));
    }

    /// <summary>
    /// Asked by the arm that poses the left hand for <paramref name="id"/>, every frame it would pose: <see cref="Up"/>,
    /// and a yes takes the left hand off the gun (PlayerBodyHands.LendOffHand, called from here alone), which the arm
    /// follows by PlayerBodyHands.OffHandLent as before.
    /// </summary>
    public bool Arm(string id)
    {
        if (!Up(id)) return false;
        armAsked = tick;
        armRow = id;
        return true;
    }

    /// <summary>
    /// The use whose arm has the left hand off the gun now: the last to ask <see cref="Arm"/> and be told yes, within a
    /// frame; null: none. An arm on its way back to the gun asks it, to be out of the way at once when another arm
    /// has the hand (a point when a throw takes it), instead of two arms posing one hand while it eases out.
    /// </summary>
    public string ArmUp
    {
        get
        {
            Turn();
            return armAsked >= 0 && armAsked >= tick - 1 ? armRow : null;
        }
    }

    // ---- What others ask about the hands ----

    /// <summary>The use that has this one hand now; null: nobody.</summary>
    public HandUse OwnerOf(HandSide one)
    {
        Turn();
        return board.Holder(one)?.Use;
    }

    /// <summary>This one hand is <paramref name="who"/>'s (the gun asks: is the right hand still mine?).</summary>
    public bool Mine(HandSide one, Object who)
    {
        Turn();
        var holder = board.Holder(one);
        return holder != null && ReferenceEquals(holder.Who, who);
    }

    /// <summary>Something that locks the hands stands (HandUses.csv locks): nothing else of the hands may start.</summary>
    public bool Locked
    {
        get
        {
            Turn();
            return board.Locked;
        }
    }

    // ---- The frame ----

    // Once a frame, at its first word or question: what was not said in the frame before is over. A paused game says
    // nothing, so its frames are not counted and nothing ages in them; nor do the frames in which this player's own
    // time stands still (PlayerMovement.Pace: the gun, for one, does nothing in such a frame and says nothing, and a
    // reload under way must find its hands still its own when time moves again).
    private void Turn()
    {
        int now = Time.frameCount;
        if (now == turned) return;
        turned = now;
        if (Time.timeScale <= 0f || GamePause.IsPaused || (movement != null && movement.DeltaTime <= 0f)) return;
        tick++;
        stale.Clear();
        for (int i = 0; i < board.Entries.Count; i++)
            if (board.Entries[i].Said < tick - 1) stale.Add(board.Entries[i]);
        foreach (var entry in stale)
        {
            if (entry.Fact) board.Withdraw(entry);
            else if (ReferenceEquals(board.Asked(entry.Who), entry)) board.Give(entry.Who);
        }
    }

    // ---- The record ----

    // A hand's use changed. The record is of whose the hand is: the same owner going from one of its rows to another
    // (the gun from its hold to its aim and back, many times a minute) is no change of owner and is not written.
    private void OnChanged(HandSide hand, HandUse from, HandUse to, bool sameOwner)
    {
        // Something changed: the same refusal after it is news again.
        lastRefusal = null;
        if (!sameOwner) Note($"{HandBoard.Name(hand)}: {Label(from)} → {Label(to)}");
    }

    private void Refused(in HandBoard.Refusal why)
    {
        if (why.Asked == null) return;
        string line = "refused " + why;
        if (line == lastRefusal) return;
        lastRefusal = line;
        Note(line);
    }

    /// <summary>A line of someone else's for this player's record, among the changes and refusals it belongs with.</summary>
    internal void Remark(string line) => Note(line);

    private static string Label(HandUse use) => use == null ? "nobody" : use.Id + " (" + use.Rank + ")";

    private void Note(string line)
    {
        if (!Decides) return;
        recent.Add($"{Time.time:0.00}s {line}");
        if (recent.Count > LinesKept) recent.RemoveAt(0);
        PlayRecord.Line(RecordName, RecordHeading, line);
    }
}
