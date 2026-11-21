using System;
using System.Collections.Generic;

/// <summary>
/// The cards one player has picked, and nothing else: which card at which level, and the hub's practice on top of a
/// campaign's own picks. It knows no table, no class and no scene, so the rules about what a class change, the hub and
/// the title screen do to a player's cards can be run on it by themselves (CardDeckCheck does, on this very class,
/// without a play). PlayerPerks holds this PC's player's one and asks the tables which deck a card belongs to.
/// What it keeps and what it never does:
/// - A pick is kept under its card's id for the whole campaign. Nothing here ever drops a card because a class was
///   picked: which cards are in play is a question of the class played (InPlay), not of what is kept. So a player who
///   tries another class keeps everyone's cards, and finds their first class's cards again when they pick it again (the
///   user, 2026-11-18, of cards after a class change: "가지고 있던 카드가 다 사라집니다. OOP 방식으로 최선의 방식 …을
///   따르면 될 것 같습니다").
/// - Practice (the hub): picks made between BeginPractice and EndPractice go when it ends, and the campaign's picks are
///   what was held when it began, so nothing picked at the hub's range reaches a chapter.
/// - Clear forgets everything: a new campaign starts bare.
/// </summary>
public sealed class CardDecks
{
    private readonly Dictionary<string, int> picked = new Dictionary<string, int>();
    // The campaign's own picks as they were when practice began, or null when not practising.
    private Dictionary<string, int> kept;

    /// <summary>How many different cards are kept, whatever deck they are in.</summary>
    public int Count => picked.Count;

    /// <summary>Picks are practice now: they go when it ends.</summary>
    public bool IsPractising => kept != null;

    /// <summary>The card's level as kept, 0 for a card never picked. Whether it is in play is not asked here.</summary>
    public int Level(string id) => id != null && picked.TryGetValue(id, out int level) ? level : 0;

    /// <summary>Raises the card a level, never past <paramref name="top"/>; true when it went up.</summary>
    public bool Raise(string id, int top)
    {
        if (string.IsNullOrEmpty(id)) return false;
        int now = Level(id), next = Math.Min(Math.Max(1, top), now + 1);
        if (next == now) return false;
        picked[id] = next;
        return true;
    }

    /// <summary>
    /// Forgets every card, the campaign's under a practice too (the title screen: whichever of leaving the hub and this
    /// comes first, nothing comes back). True when a card was held.
    /// </summary>
    public bool Clear()
    {
        kept?.Clear();
        if (picked.Count == 0) return false;
        picked.Clear();
        return true;
    }

    /// <summary>From now on picks are practice; what is held now is what practice goes back to.</summary>
    public void BeginPractice() => kept = new Dictionary<string, int>(picked);

    /// <summary>Practice is over: its picks go and the campaign's are held as they were. True when anything changed.</summary>
    public bool EndPractice()
    {
        if (kept == null) return false;
        var back = kept;
        kept = null;
        bool same = picked.Count == back.Count;
        if (same)
            foreach (var pair in picked)
                if (!back.TryGetValue(pair.Key, out int level) || level != pair.Value) { same = false; break; }
        if (same) return false;
        picked.Clear();
        foreach (var pair in back) picked[pair.Key] = pair.Value;
        return true;
    }

    /// <summary>
    /// The deck in play into <paramref name="into"/> (emptied first): every kept card that <paramref name="inDeck"/> says
    /// is of the deck played now, with its level. The rule of which card is in which deck is the caller's (the tables').
    /// </summary>
    public void InPlay(Func<string, bool> inDeck, Dictionary<string, int> into)
    {
        into.Clear();
        foreach (var pair in picked)
            if (inDeck(pair.Key)) into[pair.Key] = pair.Value;
    }
}
