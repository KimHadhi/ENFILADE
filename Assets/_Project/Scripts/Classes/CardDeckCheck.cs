#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The editor's check of the card decks, without playing: what a class change, the hub's practice and the title screen do
/// to a player's cards, run on the game's own classes (CardDecks for what is kept, PlayerPerks.InDeck for which deck a
/// card is in) over the game's own tables (Perks.csv, Classes.csv, ClassColours.csv), for every class and every pair of
/// classes, and how a class shows: one colour for its cards, its ultimate's band and its kill feed flairs.
/// Logs/card-decks-check.txt has a line per rule with how many cases kept it, and a FAIL line with the cards for each
/// that did not. Whoever changes these rules cannot play the game, so the rules say for themselves whether they
/// hold (the user, 2026-11-18, heading the whole list: "문제를 근본적으로 생각하고 뿌리를 고치며"), and the user's own
/// question of that day is one of them: after a class change everyone's cards must still be in play and the first class
/// must find its own again ("현재 클래스를 바꾸면 가지고 있던 카드가 다 사라집니다").
/// It runs by itself once after every script reload and after a play, from ENFILADE → 클래스 → 카드 덱 규칙 점검, and when
/// Temp/enfilade-deckcheck.request is made (CheckOnRequest).
/// </summary>
[InitializeOnLoad]
internal static class CardDeckCheck
{
    private const string Request = "Temp/enfilade-deckcheck.request", ReportPath = "Logs/card-decks-check.txt";

    static CardDeckCheck() => new CheckOnRequest(Request, Run);

    [MenuItem("ENFILADE/클래스/카드 덱 규칙 점검")]
    private static void Run()
    {
        var report = new CheckReport();
        var cards = PlayerPerks.Catalog;
        var classes = PlayerClass.Catalog;
        try
        {
            report.Rule("the tables hold cards and classes to check");
            report.Expect(cards.Count > 0 && classes.Count > 0, cards.Count + " cards, " + classes.Count + " classes");
            if (cards.Count > 0 && classes.Count > 0)
            {
                Decks(cards, classes, report);
                ClassChange(cards, classes, report);
                Practice(cards, classes, report);
                Travel(cards, classes, report);
                Looks(cards, classes, report);
            }
        }
        catch (Exception problem)
        {
            report.Stopped(problem);
        }
        report.Write(ReportPath, "card deck rules check, " + cards.Count + " cards, " + classes.Count + " classes");
    }

    private static bool Common(PlayerPerks.Perk card) => string.IsNullOrEmpty(card.Class);

    // A card of this class's own deck (a variant plays its base's cards).
    private static bool Own(PlayerPerks.Perk card, PlayerClass.Info of) => string.Equals(card.Class, of.Family, StringComparison.OrdinalIgnoreCase);

    // What a player of this class has in play of what is kept, as the game makes it.
    private static Dictionary<string, int> InPlay(CardDecks decks, string played)
    {
        var view = new Dictionary<string, int>();
        decks.InPlay(id => PlayerPerks.InDeck(PlayerPerks.Find(id), played), view);
        return view;
    }

    private static string Listed(Dictionary<string, int> view)
    {
        var text = new StringBuilder();
        foreach (var pair in view) text.Append(pair.Key).Append(' ').Append(pair.Value).Append("; ");
        return text.Length > 0 ? text.ToString() : "nothing";
    }

    // Which deck a card is in.
    private static void Decks(IReadOnlyList<PlayerPerks.Perk> cards, IReadOnlyList<PlayerClass.Info> classes, CheckReport report)
    {
        report.Rule("a class's deck holds its own cards and everyone's, never another class's");
        foreach (var played in classes)
            foreach (var card in cards)
                report.Expect(PlayerPerks.InDeck(card, played.Id) == (Common(card) || Own(card, played)),
                    played.Id + " and " + card.Id + " (class '" + card.Class + "'): in its deck " + PlayerPerks.InDeck(card, played.Id));

        report.Rule("every card's class is a class of Classes.csv");
        foreach (var card in cards)
            report.Expect(Common(card) || PlayerClass.Find(card.Class) != null, card.Id + " names class '" + card.Class + "'");

        report.Rule("the cards whose effects are asked by name are in play for their own class alone");
        foreach (string id in new[] { PlayerPerks.ExecutionHeal, PlayerPerks.Rescue, PlayerPerks.Heriot, PlayerPerks.Momentum })
        {
            var card = PlayerPerks.Find(id);
            if (card == null) { report.Expect(false, id + " is not in Perks.csv"); continue; }
            foreach (var played in classes)
                report.Expect(PlayerPerks.InDeck(card, played.Id) == Own(card, played), id + " for " + played.Id + ": in play " + PlayerPerks.InDeck(card, played.Id));
        }
    }

    // A campaign's cards through a class change and back (the user's question of 2026-11-18).
    private static void ClassChange(IReadOnlyList<PlayerPerks.Perk> cards, IReadOnlyList<PlayerClass.Info> classes, CheckReport report)
    {
        int commons = 0;
        foreach (var card in cards) if (Common(card)) commons++;
        report.Rule("after a class change everyone's cards are still in play, the old class's are not, and nothing was thrown away");
        foreach (var first in classes)
        {
            var decks = new CardDecks();
            foreach (var card in cards)
                if (PlayerPerks.InDeck(card, first.Id)) decks.Raise(card.Id, card.Levels);
            int held = decks.Count;
            foreach (var next in classes)
            {
                if (string.Equals(next.Family, first.Family, StringComparison.OrdinalIgnoreCase)) continue;
                var view = InPlay(decks, next.Id);
                bool onlyCommon = view.Count == commons;
                foreach (var pair in view) if (!Common(PlayerPerks.Find(pair.Key))) onlyCommon = false;
                report.Expect(onlyCommon && decks.Count == held, first.Id + " → " + next.Id + ": in play " + Listed(view) + " kept " + decks.Count + " of " + held);
            }
        }

        report.Rule("a class picked again finds its own cards as they were");
        foreach (var first in classes)
        {
            var decks = new CardDecks();
            foreach (var card in cards)
                if (PlayerPerks.InDeck(card, first.Id)) decks.Raise(card.Id, card.Levels);
            var before = InPlay(decks, first.Id);
            foreach (var next in classes)
            {
                // The other class picks a card of its own meanwhile.
                foreach (var card in cards)
                    if (Own(card, next) && !Own(card, first)) { decks.Raise(card.Id, card.Levels); break; }
                var back = InPlay(decks, first.Id);
                bool same = back.Count == before.Count;
                foreach (var pair in before) if (!back.TryGetValue(pair.Key, out int level) || level != pair.Value) same = false;
                report.Expect(same, first.Id + " → " + next.Id + " → " + first.Id + ": had " + before.Count + " cards, has " + back.Count);
            }
        }
    }

    // The hub's practice: its picks work, a class picked there throws nothing away, and none of them reach a chapter.
    private static void Practice(IReadOnlyList<PlayerPerks.Perk> cards, IReadOnlyList<PlayerClass.Info> classes, CheckReport report)
    {
        report.Rule("a class picked in the hub keeps everyone's practice cards in play and gives the old class its own back");
        var left = new List<(CardDecks decks, Dictionary<string, int> campaign, string id)>();
        foreach (var first in classes)
        {
            PlayerPerks.Perk ownOne = null, ownTwo = null, commonOne = null, commonTwo = null;
            foreach (var card in cards)
            {
                if (Own(card, first)) { if (ownOne == null) ownOne = card; else if (ownTwo == null) ownTwo = card; }
                else if (Common(card)) { if (commonOne == null) commonOne = card; else if (commonTwo == null) commonTwo = card; }
            }
            if (ownOne == null || ownTwo == null || commonOne == null || commonTwo == null) { report.Expect(false, first.Id + ": too few cards to try"); continue; }
            var decks = new CardDecks();
            decks.Raise(ownOne.Id, ownOne.Levels);
            decks.Raise(commonOne.Id, commonOne.Levels);
            var campaign = new Dictionary<string, int>(InPlay(decks, first.Id));
            decks.BeginPractice();
            decks.Raise(ownTwo.Id, ownTwo.Levels);
            decks.Raise(commonTwo.Id, commonTwo.Levels);
            foreach (var next in classes)
            {
                if (string.Equals(next.Family, first.Family, StringComparison.OrdinalIgnoreCase)) continue;
                var there = InPlay(decks, next.Id);
                report.Expect(there.Count == 2 && there.ContainsKey(commonOne.Id) && there.ContainsKey(commonTwo.Id),
                    "hub, " + first.Id + " → " + next.Id + ": in play " + Listed(there));
                var back = InPlay(decks, first.Id);
                report.Expect(back.Count == 4 && back.ContainsKey(ownOne.Id) && back.ContainsKey(ownTwo.Id),
                    "hub, " + first.Id + " → " + next.Id + " → " + first.Id + ": in play " + Listed(back));
            }
            left.Add((decks, campaign, first.Id));
        }

        report.Rule("leaving the hub takes the practice cards and leaves the campaign's as they were");
        foreach (var (decks, campaign, id) in left)
        {
            decks.EndPractice();
            var after = InPlay(decks, id);
            bool same = after.Count == campaign.Count && decks.Count == campaign.Count && !decks.IsPractising;
            foreach (var pair in campaign) if (!after.TryGetValue(pair.Key, out int level) || level != pair.Value) same = false;
            report.Expect(same, id + ": campaign " + Listed(campaign) + " after the hub " + Listed(after));
        }

        report.Rule("the title screen leaves nothing, in practice or out of it");
        foreach (bool practising in new[] { false, true })
        {
            var decks = new CardDecks();
            decks.Raise(cards[0].Id, cards[0].Levels);
            if (practising) decks.BeginPractice();
            decks.Raise(cards[cards.Count - 1].Id, cards[cards.Count - 1].Levels);
            decks.Clear();
            if (practising) decks.EndPractice();
            report.Expect(decks.Count == 0, (practising ? "practising" : "not practising") + ": kept " + decks.Count);
        }
    }

    // A deck as it is sent to the other PCs and read there.
    private static void Travel(IReadOnlyList<PlayerPerks.Perk> cards, IReadOnlyList<PlayerClass.Info> classes, CheckReport report)
    {
        report.Rule("a deck sent as words reads back the same cards and levels, and fits the string it travels in");
        var read = new List<(PlayerPerks.Perk perk, int level)>();
        foreach (var played in classes)
        {
            var sent = new StringBuilder();
            int count = 0;
            foreach (var card in cards)
            {
                if (!PlayerPerks.InDeck(card, played.Id)) continue;
                if (sent.Length > 0) sent.Append(';');
                sent.Append(card.Id).Append(':').Append(card.Levels);
                count++;
            }
            PlayerPerks.Read(sent.ToString(), read);
            bool same = read.Count == count;
            foreach (var (perk, level) in read) if (!PlayerPerks.InDeck(perk, played.Id) || level != perk.Levels) same = false;
            report.Expect(same && Encoding.UTF8.GetByteCount(sent.ToString()) <= PlayerNetworkScore.DeckBytes,
                played.Id + ": sent " + count + " cards in " + Encoding.UTF8.GetByteCount(sent.ToString()) + " of "
                + PlayerNetworkScore.DeckBytes + " bytes, read " + read.Count);
        }
    }

    // How a class and its cards show.
    private static void Looks(IReadOnlyList<PlayerPerks.Perk> cards, IReadOnlyList<PlayerClass.Info> classes, CheckReport report)
    {
        report.Rule("every class has its colour (ClassColours.csv), and everyone's cards have theirs (the common row)");
        foreach (var played in classes)
            report.Expect(ClassLook.Colour(played.Id).a > 0f, played.Id + " has no colour row");
        report.Expect(ClassLook.Colour(ClassLook.Common).a > 0f, "no '" + ClassLook.Common + "' row");

        // The user, 2026-11-18, of a class whose ultimate's band and cards wore two colours: "카드 쪽이 그 클래스의 색을
        // 써야 합니다". Whatever names a class asks ClassLook, so the three ways in must give one colour.
        report.Rule("a class wears one colour everywhere: its cards, its ultimate's band and moments, its kill feed flairs");
        foreach (var played in classes)
        {
            // A variant plays its base's cards and may have a colour row of its own: its base is what is checked.
            if (played.IsVariant) continue;
            Color card = Colour(played, cards), moment = MomentLook.ColorOf(played.Id), flair = KillFlairs.ClassColourOf(played.Id);
            report.Expect(Same(card, moment) && Same(card, flair), played.Id + ": cards #" + ColorUtility.ToHtmlStringRGB(card)
                + ", moments #" + ColorUtility.ToHtmlStringRGB(moment) + ", kill feed #" + ColorUtility.ToHtmlStringRGB(flair));
        }

        report.Rule("no second table holds a class's colour (MomentFx.csv has no Class row)");
        foreach (var played in classes)
            report.Expect(MomentLook.Colour("Class", played.Id, Color.clear).a <= 0f, "MomentFx.csv Class." + played.Id + " is back");
    }

    // The colour a class's own cards wear (the class's, by any card of its deck; the class's row where it has no card yet).
    private static Color Colour(PlayerClass.Info of, IReadOnlyList<PlayerPerks.Perk> cards)
    {
        foreach (var card in cards)
            if (Own(card, of)) return ClassLook.CardColour(card.Class);
        return ClassLook.Colour(of.Id);
    }

    private static bool Same(Color a, Color b)
    {
        Color32 x = a, y = b;
        return x.r == y.r && x.g == y.g && x.b == y.b;
    }
}
#endif
