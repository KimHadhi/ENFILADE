using System;
using System.Collections.Generic;

/// <summary>
/// Whose each of a player's two hands is: the rule by itself, with no scene and no clock. HandOwner keeps one board for
/// a player and feeds it; the editor's HandsCheck and Tools/Checks/hands_check.py run scenes through the same rule
/// without a play, which is how whoever changes it sees a mistake before a player does.
/// <para>
/// A hand has one owner. Uses come to the board in two ways. Asked for (<see cref="Take"/>): the use gets every hand it
/// needs or nothing at all, and an ask that later loses a hand it needs is over (its owner hears so the next time it
/// asks <see cref="Has"/>, and puts its own thing away: a pin back in, a reload stopped where it stands). Stated
/// (<see cref="State"/>): what the body is doing already (dead, grabbed, hanging from a ledge, rolling) is not the
/// board's to refuse, so a fact always stands and has whichever of its hands its rank can take.
/// </para>
/// <para>
/// Between two uses that want one hand, in this order: the bigger rank has it; with one rank, a hand that is needed
/// beats a hand only used while free; otherwise whoever was there first keeps it (the user, 2026-11-18: "먼저 하는 게
/// 일단 짱"), and two that came in the very same frame go by name, so the answer never depends on whose Update ran
/// first. A hand let go of goes to whoever still wants it most: the left hand goes back to the gun by itself.
/// </para>
/// <para>
/// One thing more than the hands: a use can let only some families of uses be asked for while it stands (HandUse.Lets;
/// a body gone down lets only its gun's). It takes no hand for that. An ask of another family, by a rank no bigger
/// than its own, is refused, and one standing when it comes is over. Facts are never refused and never ended.
/// </para>
/// The ranks are the table's (Tables/Player/HandUses.csv); nothing here knows any use by name.
/// </summary>
public sealed class HandBoard
{
    /// <summary>One use standing on the board.</summary>
    public sealed class Entry
    {
        /// <summary>Whose it is: the component that asked or stated it.</summary>
        public object Who { get; internal set; }
        public HandUse Use { get; internal set; }
        /// <summary>Stated as a fact rather than asked for.</summary>
        public bool Fact { get; internal set; }
        /// <summary>The hands it has now.</summary>
        public HandSide Owns { get; internal set; }
        /// <summary>The frame it was last said in (HandOwner lets a fact go that is not said again).</summary>
        public int Said { get; internal set; }
        // Its place among the others (earlier keeps a tie) and the frame it took its row in.
        internal long Arrived;
        internal int Came;
    }

    /// <summary>
    /// Why an ask got nothing: the hand it could not have and the use that has it; or, with no hand named, the use
    /// standing that lets nothing of its family be asked for.
    /// </summary>
    public readonly struct Refusal
    {
        public readonly HandUse Asked, Holder;
        public readonly HandSide Hand;
        /// <summary>What stood in its way is a fact of the body, not something else that was asked for.</summary>
        public readonly bool ByFact;

        public Refusal(HandUse asked, HandSide hand, HandUse holder, bool byFact = false)
        {
            Asked = asked;
            Hand = hand;
            Holder = holder;
            ByFact = byFact;
        }

        public override string ToString() => Asked == null ? ""
            : Hand == HandSide.None ? $"{Asked.Id} ({Asked.Rank}): {Holder.Id} ({Holder.Rank}) lets only {Holder.Only} be asked for"
            : $"{Asked.Id} ({Asked.Rank}): the {Name(Hand)} hand is {Holder.Id}'s ({Holder.Rank})";
    }

    private static readonly HandSide[] Each = { HandSide.Left, HandSide.Right };

    private readonly List<Entry> entries = new List<Entry>();
    private Entry left, right;
    private long arrivals;

    /// <summary>
    /// A hand's use changed: which hand, the use that had it and the use that has it now (either may be null: nobody),
    /// and whether both are the same owner's (one owner going from a row of its own to another: no change of owner).
    /// Raised once per hand at the end of the call that changed it.
    /// </summary>
    public event Action<HandSide, HandUse, HandUse, bool> Changed;

    /// <summary>Every use standing, in the order they came.</summary>
    public IReadOnlyList<Entry> Entries => entries;

    public static string Name(HandSide hand) => hand == HandSide.Left ? "left" : hand == HandSide.Right ? "right" : hand == HandSide.Both ? "both" : "no";

    /// <summary>Who has this one hand now; null: nobody.</summary>
    public Entry Holder(HandSide one) => one == HandSide.Left ? left : one == HandSide.Right ? right : null;

    /// <summary>What <paramref name="who"/> asked for and still has; null when it asked for nothing or lost it.</summary>
    public Entry Asked(object who)
    {
        for (int i = 0; i < entries.Count; i++)
            if (!entries[i].Fact && ReferenceEquals(entries[i].Who, who)) return entries[i];
        return null;
    }

    /// <summary>Whether <paramref name="who"/> still has what it asked for.</summary>
    public bool Has(object who) => Asked(who) != null;

    /// <summary>The first use of this id standing with every hand it needs; null when none does.</summary>
    public Entry Standing(string id)
    {
        for (int i = 0; i < entries.Count; i++)
            if (Same(entries[i].Use, id) && (entries[i].Owns & entries[i].Use.Needs) == entries[i].Use.Needs) return entries[i];
        return null;
    }

    /// <summary>Something that locks the hands stands (HandUse.Locks): nothing else of the hands may start.</summary>
    public bool Locked
    {
        get
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Use.Locks) return true;
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="who"/> would get <paramref name="use"/> if it asked now; nothing changes. What it already
    /// holds itself is not in its own way (the gun going from its hold to a reload).
    /// </summary>
    public bool Could(HandUse use, object who, int frame, out Refusal why)
    {
        why = default;
        if (use == null) return false;
        var mine = who != null ? Asked(who) : null;
        var limit = Limiting(use, mine);
        if (limit != null)
        {
            why = new Refusal(use, HandSide.None, limit.Use, limit.Fact);
            return false;
        }
        foreach (var hand in Each)
        {
            if ((use.Needs & hand) == 0) continue;
            var holder = Holder(hand);
            if (holder == null || holder == mine || Beats(use, true, frame, holder, hand)) continue;
            why = new Refusal(use, hand, holder.Use, holder.Fact);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Whether what keeps <paramref name="who"/> from <paramref name="use"/> is something else that was asked for and
    /// has a hand of it (a grenade held ready, a syringe): the hand is busy elsewhere. Not so when it could have the use,
    /// nor when a fact of the body has the hand or limits what may be asked (a zombie's grip, a ledge): what can be done
    /// in such a state is that state's own rule, as it was before the hands had an owner.
    /// </summary>
    public bool Kept(HandUse use, object who, int frame) => use != null && !Could(use, who, frame, out var why) && why.Hand != HandSide.None && !why.ByFact;

    /// <summary>
    /// <paramref name="who"/> asks for <paramref name="use"/>: every hand it needs, or nothing. Asking again for the same
    /// use costs nothing; asking for another is a change of row (the gun: hold, aim, reload), refused as a whole when the
    /// new row's hands can't be had, the old row then standing as it was.
    /// </summary>
    public bool Take(HandUse use, object who, int frame, out Refusal why)
    {
        why = default;
        if (use == null || who == null) return false;
        var mine = Asked(who);
        if (mine != null && Same(mine.Use, use.Id))
        {
            mine.Use = use;
            mine.Said = frame;
            return true;
        }
        if (!Could(use, who, frame, out why)) return false;
        Begin();
        if (mine == null)
        {
            mine = new Entry { Who = who, Arrived = ++arrivals };
            entries.Add(mine);
        }
        else
        {
            // The new row has no use for a hand the old one had: it is let go.
            foreach (var hand in Each)
                if ((mine.Owns & hand) != 0 && ((use.Needs | use.Also) & hand) == 0) Free(mine, hand);
        }
        mine.Use = use;
        mine.Fact = false;
        mine.Came = mine.Said = frame;
        Limit(mine);
        Claim(mine, frame);
        End();
        return true;
    }

    /// <summary>
    /// <paramref name="who"/> states a fact: <paramref name="use"/> is so, and has whichever of its hands its rank can
    /// take. Said again every frame it holds; HandOwner lets go of one that is not (<see cref="Withdraw"/>).
    /// </summary>
    public void State(HandUse use, object who, int frame)
    {
        if (use == null || who == null) return;
        Begin();
        Entry mine = null;
        for (int i = 0; i < entries.Count && mine == null; i++)
            if (entries[i].Fact && ReferenceEquals(entries[i].Who, who) && Same(entries[i].Use, use.Id)) mine = entries[i];
        if (mine == null)
        {
            mine = new Entry { Who = who, Fact = true, Arrived = ++arrivals, Came = frame };
            entries.Add(mine);
        }
        mine.Use = use;
        mine.Said = frame;
        Limit(mine);
        Claim(mine, frame);
        End();
    }

    /// <summary>What <paramref name="who"/> asked for is given back. False when it had nothing.</summary>
    public bool Give(object who)
    {
        var mine = Asked(who);
        if (mine == null) return false;
        Begin();
        Remove(mine);
        End();
        return true;
    }

    /// <summary>A fact is no longer so.</summary>
    public void Withdraw(Entry fact)
    {
        if (fact == null || !entries.Contains(fact)) return;
        Begin();
        Remove(fact);
        End();
    }

    /// <summary>Everything is let go (a player gone, a check starting over).</summary>
    public void Clear()
    {
        Begin();
        entries.Clear();
        left = right = null;
        End();
    }

    // ---- The rule ----

    // Whether a use that wants `hand` (needing it, or only using it while free) takes it from who has it.
    private static bool Beats(HandUse asker, bool needs, int frame, Entry holder, HandSide hand)
    {
        if (asker.Rank != holder.Use.Rank) return asker.Rank > holder.Use.Rank;
        bool holderNeeds = (holder.Use.Needs & hand) != 0;
        if (needs != holderNeeds) return needs;
        // One rank and the same kind of want: who was there first keeps it. Two that came in the very same frame go by
        // name instead, whoever's Update happened to run first.
        return holder.Came == frame && string.CompareOrdinal(asker.Id, holder.Use.Id) < 0;
    }

    private static bool Same(HandUse use, string id) => use != null && string.Equals(use.Id, id, StringComparison.OrdinalIgnoreCase);

    // What stands and lets nothing of this use's family be asked for (HandUse.Lets), its rank no smaller than the
    // use's: a body gone down, for anything but its gun. Null: nothing does.
    private Entry Limiting(HandUse use, Entry but)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != but && entries[i].Use.Rank >= use.Rank && !entries[i].Use.Lets(use)) return entries[i];
        return null;
    }

    // A use that lets only some families be asked for is on the board: what was asked for of the others, by a rank no
    // bigger than its own, is over (a grenade in the fist of a body going down goes back on the belt). A fact is never
    // ended. The hands it frees go to whoever still wants them (Settle): back to the gun.
    private void Limit(Entry by)
    {
        if (!by.Use.Limits) return;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry != by && !entry.Fact && entry.Use.Rank <= by.Use.Rank && !by.Use.Lets(entry.Use)) Remove(entry);
        }
    }

    // The hands an entry wants, taken where it beats who has them: the ones it needs first, then the ones it only uses.
    private void Claim(Entry entry, int frame)
    {
        foreach (var hand in Each)
            if ((entry.Use.Needs & hand) != 0) Reach(entry, hand, true, frame);
        foreach (var hand in Each)
            if ((entry.Use.Also & hand) != 0) Reach(entry, hand, false, frame);
    }

    private void Reach(Entry entry, HandSide hand, bool needs, int frame)
    {
        var holder = Holder(hand);
        if (holder == entry) return;
        if (holder != null)
        {
            if (!Beats(entry.Use, needs, frame, holder, hand)) return;
            Lose(holder, hand);
        }
        Put(hand, entry);
    }

    // A hand is taken from its holder. An ask that needed it is over, and whatever else it had is free; a fact, or an
    // ask that only used the hand while free, stands on without it.
    private void Lose(Entry holder, HandSide hand)
    {
        Free(holder, hand);
        if (holder.Fact || (holder.Use.Needs & hand) == 0) return;
        Remove(holder);
    }

    private void Remove(Entry entry)
    {
        entries.Remove(entry);
        foreach (var hand in Each)
            if ((entry.Owns & hand) != 0) Free(entry, hand);
    }

    private void Free(Entry entry, HandSide hand)
    {
        entry.Owns &= ~hand;
        if (hand == HandSide.Left && left == entry) left = null;
        else if (hand == HandSide.Right && right == entry) right = null;
    }

    private void Put(HandSide hand, Entry entry)
    {
        if (hand == HandSide.Left) left = entry;
        else right = entry;
        entry.Owns |= hand;
    }

    // A hand nobody has goes to whoever still wants it most: the biggest rank; with one rank, one that needs it before
    // one that only uses it; then whoever came first. Only a fact can still be waiting for a hand it needs (an ask
    // without one is gone), and anyone may be waiting for a hand it uses while free.
    private void Settle()
    {
        foreach (var hand in Each)
        {
            if (Holder(hand) != null) continue;
            Entry best = null;
            bool bestNeeds = false;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool needs = (entry.Use.Needs & hand) != 0;
                if (needs ? !entry.Fact : (entry.Use.Also & hand) == 0) continue;
                if (best == null || entry.Use.Rank > best.Use.Rank
                    || (entry.Use.Rank == best.Use.Rank && (needs != bestNeeds ? needs : entry.Arrived < best.Arrived)))
                {
                    best = entry;
                    bestNeeds = needs;
                }
            }
            if (best != null) Put(hand, best);
        }
    }

    // ---- Telling who listens what changed, once per call ----

    private HandUse leftBefore, rightBefore;
    private object leftWas, rightWas;
    private int depth;

    private void Begin()
    {
        if (depth++ > 0) return;
        leftBefore = left?.Use;
        rightBefore = right?.Use;
        leftWas = left?.Who;
        rightWas = right?.Who;
    }

    private void End()
    {
        if (--depth > 0) return;
        Settle();
        if (Changed == null) return;
        if (!ReferenceEquals(leftBefore, left?.Use)) Changed(HandSide.Left, leftBefore, left?.Use, left != null && ReferenceEquals(leftWas, left.Who));
        if (!ReferenceEquals(rightBefore, right?.Use)) Changed(HandSide.Right, rightBefore, right?.Use, right != null && ReferenceEquals(rightWas, right.Who));
    }
}
