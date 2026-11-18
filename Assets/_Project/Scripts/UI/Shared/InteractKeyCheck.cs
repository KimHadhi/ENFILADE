#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// The editor's check of who gets the interact key, without playing: the game's own rule (InteractKey.Before, Lead,
/// Shown, Step, HeldBy) run over the game's own table as the game reads it (Tables/Player/InteractKey.csv through
/// GameTables), for everything the game itself offers F to, named here by each feature's own constant so a name
/// changed or dropped stops this file compiling. Tools/Checks/interact_key_check.py runs a copy of the rule outside
/// Unity and reads the scripts; this runs the rule itself, so the two together leave nothing of it to be found by play.
/// Logs/interact-key-check.txt has a line per rule with how many cases kept it. Whoever changes these rules cannot play
/// the game, so the rules say for themselves whether they hold (the user, 2026-11-18, heading the whole list: "문제를
/// 근본적으로 생각하고 뿌리를 고치며"), and what the user reported that day is two of them: a pickup that refuses (a
/// medkit, health full) must not keep F from the barricade behind it, and while a Support plans a structure F must be
/// the plan's alone.
/// It runs by itself once after every script reload and after a play, from ENFILADE → 입력 → F 키 규칙 점검, and when
/// Temp/enfilade-interactcheck.request is made (CheckOnRequest).
/// </summary>
[InitializeOnLoad]
internal static class InteractKeyCheck
{
    private const string Request = "Temp/enfilade-interactcheck.request", ReportPath = "Logs/interact-key-check.txt";

    // Everything the game itself offers F to, by its own name; and of those, what is done for as long as the key is held.
    private static readonly string[] Offers =
    {
        SupportKit.PlanOffer, PlayerExecution.OfferId, GhostCards.ChainOffer, PlayerRevive.OfferId, DetenteKit.AnswerOffer,
        FieldPickups.OfferId, ClassKit.CrateOffer, MovementFixtures.TopOffer, CampaignRun.ButtonOffer, CampaignRun.CanInHandOffer,
        MapGimmicks.OfferId, CampaignRun.DoorOffer, CampaignRun.ShopOffer, CampaignRun.CanOnFloorOffer, TowerRun.ScannerOffer,
        TowerRun.ShopOffer, HubLobby.StationOffer, DetenteKit.RaiseOffer, DetenteKit.TreatOffer, SupportKit.GiveOffer,
        SupportKit.MendOffer, PlayerTestInteraction.OfferId, PlayerPing.OfferId,
    };
    private static readonly HashSet<string> HeldOffers = new HashSet<string>
    {
        PlayerRevive.OfferId, TowerRun.ScannerOffer, DetenteKit.RaiseOffer, SupportKit.MendOffer, PlayerPing.OfferId,
    };

    static InteractKeyCheck() => new CheckOnRequest(Request, Run);

    [MenuItem("ENFILADE/입력/F 키 규칙 점검")]
    private static void Run()
    {
        var report = new CheckReport();
        try
        {
            InteractKey.Forget();
            Table(report);
            Refusals(report);
            Plan(report);
            Order(report);
            Presses(report);
            Every(report);
        }
        catch (Exception problem)
        {
            report.Stopped(problem);
        }
        report.Write(ReportPath, "interact key rules check, " + Offers.Length + " offers");
    }

    private static InteractKey.Entry Offer(string id, string refusal = null)
        => new InteractKey.Entry { Id = id, Order = Place(id), Held = HeldOffers.Contains(id), Refusal = refusal };

    // An offer's place as the game gives it, asked quietly: one with no row stands where the row "other" does (the game
    // says so once in the log when such an offer is really made; a check's made-up one need not).
    private static int Place(string id) => InteractKey.OrderOf(InteractKey.HasRow(id) ? id : InteractKey.OtherRow);

    private static string Leader(List<InteractKey.Entry> standing, out bool held)
    {
        int lead = InteractKey.Lead(standing);
        held = lead >= 0 && standing[lead].Held;
        return lead >= 0 ? standing[lead].Id : null;
    }

    private static string Shown(List<InteractKey.Entry> standing)
    {
        var names = new List<string>();
        for (int i = 0; i < standing.Count; i++) if (InteractKey.Shown(standing, i)) names.Add(standing[i].Id);
        names.Sort(string.CompareOrdinal);
        return string.Join("+", names);
    }

    private static bool Before(string a, string b) => Place(a) < Place(b);

    // The table as the game reads it.
    private static void Table(CheckReport report)
    {
        report.Rule("every offer the game makes has a row of its own in InteractKey.csv, and no two rows share a place");
        var places = new Dictionary<int, string>();
        foreach (string id in Offers)
        {
            report.Expect(InteractKey.HasRow(id), id + " has no row");
            int place = Place(id);
            report.Expect(!places.TryGetValue(place, out string other), id + " and " + other + " both stand at " + place);
            places[place] = id;
        }
        report.Note("read as: " + string.Join(", ", Array.ConvertAll(Offers, id => id + " " + Place(id))));

        report.Rule("the ping is the last of all, and an offer with no row stands just before it");
        report.Expect(InteractKey.HasRow(InteractKey.OtherRow), "the row '" + InteractKey.OtherRow + "' is missing");
        foreach (string id in Offers)
            if (id != PlayerPing.OfferId)
            {
                report.Expect(Before(id, PlayerPing.OfferId), id + " does not come before the ping");
                report.Expect(Before(id, InteractKey.OtherRow), id + " does not come before an offer with no row");
            }
        report.Expect(Before(InteractKey.OtherRow, PlayerPing.OfferId), "an offer with no row would come after the ping");
    }

    // The user's item 8, the first half: a pickup looked at that can't be used keeps nothing from F.
    private static void Refusals(CheckReport report)
    {
        report.Rule("a pickup that refuses (health full) leaves F to what stands behind it, shows its refusal beside that one's prompt, and wanted it still comes first");
        foreach (string id in Offers)
        {
            if (!Before(FieldPickups.OfferId, id)) continue;
            var refused = new List<InteractKey.Entry> { Offer(FieldPickups.OfferId, "full"), Offer(id) };
            report.Expect(Leader(refused, out _) == id, "with a refused pickup in view F is not " + id + "'s but " + (Leader(refused, out _) ?? "nobody's"));
            var both = new List<string> { FieldPickups.OfferId, id };
            both.Sort(string.CompareOrdinal);
            report.Expect(Shown(refused) == string.Join("+", both), "with a refused pickup and " + id + " the screen shows " + Shown(refused));
            var wanted = new List<InteractKey.Entry> { Offer(FieldPickups.OfferId), Offer(id) };
            report.Expect(Leader(wanted, out _) == FieldPickups.OfferId && Shown(wanted) == FieldPickups.OfferId,
                "a pickup that is wanted does not come before " + id + ": F is " + Leader(wanted, out _) + "'s, shown " + Shown(wanted));
        }

        report.Rule("a Support at full health mends a barricade and resupplies a friend with a medkit lying in view");
        var mending = new List<InteractKey.Entry> { Offer(FieldPickups.OfferId, "full"), Offer(SupportKit.MendOffer), Offer(PlayerPing.OfferId) };
        string taker = null;
        bool spent = false;
        string leader = Leader(mending, out bool leaderHeld);
        InteractKey.Step(true, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(taker == SupportKit.MendOffer && InteractKey.HeldBy(SupportKit.MendOffer, true, taker, spent, leader, leaderHeld),
            "F held on the barricade is " + (taker ?? "nobody") + "'s, not the hammer's");
        var giving = new List<InteractKey.Entry> { Offer(FieldPickups.OfferId, "full"), Offer(SupportKit.GiveOffer), Offer(PlayerPing.OfferId) };
        report.Expect(Leader(giving, out _) == SupportKit.GiveOffer, "F on the friend is " + Leader(giving, out _) + "'s, not the resupply's");
    }

    // The user's item 8, the second half: while the class key is held F is the plan's and nobody else's.
    private static void Plan(CheckReport report)
    {
        report.Rule("while a Support plans a structure F is the plan's alone: against every one, two and three of the other offers");
        var others = new List<string>();
        foreach (string id in Offers) if (id != SupportKit.PlanOffer) others.Add(id);
        for (int a = 0; a < others.Count; a++)
            for (int b = a; b < others.Count; b++)
                for (int c = b; c < others.Count; c++)
                {
                    var standing = new List<InteractKey.Entry> { Offer(others[a]), Offer(SupportKit.PlanOffer) };
                    if (b != a) standing.Add(Offer(others[b]));
                    if (c != b) standing.Add(Offer(others[c]));
                    string leader = Leader(standing, out bool leaderHeld), taker = null;
                    bool spent = false;
                    InteractKey.Step(true, true, leader, leaderHeld, ref taker, ref spent);
                    bool alone = taker == SupportKit.PlanOffer && spent && Shown(standing) == SupportKit.PlanOffer;
                    foreach (var entry in standing)
                        if (entry.Id != SupportKit.PlanOffer && InteractKey.HeldBy(entry.Id, true, taker, spent, leader, leaderHeld)) alone = false;
                    report.Expect(alone, "with " + others[a] + ", " + others[b] + ", " + others[c] + " offered: the press is " + (taker ?? "nobody")
                        + "'s, shown " + Shown(standing));
                }
    }

    // What was deliberate before the key had an owner, as the lists that are gone had it.
    private static void Order(CheckReport report)
    {
        report.Rule("the order that was deliberate still holds: a takedown before a pickup and a ping, a pickup before the mission's prompts, those before a class's own acts");
        void First(string a, string b) => report.Expect(Before(a, b), a + " does not come before " + b);
        string[] classActs = { SupportKit.GiveOffer, SupportKit.MendOffer, DetenteKit.RaiseOffer, DetenteKit.TreatOffer };
        string[] mission = { CampaignRun.ButtonOffer, CampaignRun.CanInHandOffer, MapGimmicks.OfferId, CampaignRun.DoorOffer, CampaignRun.ShopOffer,
            CampaignRun.CanOnFloorOffer, TowerRun.ScannerOffer, TowerRun.ShopOffer };
        First(PlayerExecution.OfferId, FieldPickups.OfferId);
        First(PlayerExecution.OfferId, PlayerRevive.OfferId);
        First(PlayerExecution.OfferId, PlayerTestInteraction.OfferId);
        First(GhostCards.ChainOffer, FieldPickups.OfferId);
        First(PlayerRevive.OfferId, FieldPickups.OfferId);
        foreach (string act in classActs)
        {
            First(PlayerExecution.OfferId, act);
            First(PlayerRevive.OfferId, act);
            First(FieldPickups.OfferId, act);
            First(MovementFixtures.TopOffer, act);
        }
        foreach (string prompt in mission)
        {
            First(FieldPickups.OfferId, prompt);
            foreach (string act in classActs) First(prompt, act);
        }
        for (int i = 0; i + 1 < 6; i++) First(mission[i], mission[i + 1]);          // CampaignRun.Prompt's own chain
        First(TowerRun.ScannerOffer, TowerRun.ShopOffer);
        First(DetenteKit.RaiseOffer, DetenteKit.TreatOffer);
        First(SupportKit.GiveOffer, SupportKit.MendOffer);
        First(DetenteKit.AnswerOffer, SupportKit.MendOffer);
        First(DetenteKit.AnswerOffer, ClassKit.CrateOffer);
    }

    // One press, one act; a hold taken up.
    private static void Presses(CheckReport report)
    {
        report.Rule("a press spent on a pickup starts no hammer with the key still down, and a new press does");
        var standing = new List<InteractKey.Entry> { Offer(FieldPickups.OfferId), Offer(SupportKit.MendOffer), Offer(PlayerPing.OfferId) };
        string taker = null;
        bool spent = false;
        string leader = Leader(standing, out bool leaderHeld);
        InteractKey.Step(true, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(taker == FieldPickups.OfferId && spent, "the press went to " + (taker ?? "nobody"));
        standing.RemoveAt(0);                                                       // taken: the medkit is gone, the key still down
        leader = Leader(standing, out leaderHeld);
        InteractKey.Step(false, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(!InteractKey.HeldBy(SupportKit.MendOffer, true, taker, spent, leader, leaderHeld), "the hammer came out on the pickup's press");
        InteractKey.Step(false, false, leader, leaderHeld, ref taker, ref spent);
        InteractKey.Step(true, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(InteractKey.HeldBy(SupportKit.MendOffer, true, taker, spent, leader, leaderHeld), "a new press on the barricade is not the hammer's");

        report.Rule("a key held on the way to a downed friend gets them up on arrival; a hammer at work gives way to a friend who goes down and comes back after");
        standing = new List<InteractKey.Entry> { Offer(PlayerPing.OfferId) };
        taker = null;
        spent = false;
        leader = Leader(standing, out leaderHeld);
        InteractKey.Step(true, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(InteractKey.HeldBy(PlayerPing.OfferId, true, taker, spent, leader, leaderHeld), "F held with nothing to do is not the point's");
        standing.Add(Offer(PlayerRevive.OfferId));
        leader = Leader(standing, out leaderHeld);
        InteractKey.Step(false, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(InteractKey.HeldBy(PlayerRevive.OfferId, true, taker, spent, leader, leaderHeld)
            && !InteractKey.HeldBy(PlayerPing.OfferId, true, taker, spent, leader, leaderHeld), "the held key did not go to the friend on arrival");
        standing = new List<InteractKey.Entry> { Offer(SupportKit.MendOffer) };
        taker = null;
        spent = false;
        leader = Leader(standing, out leaderHeld);
        InteractKey.Step(true, true, leader, leaderHeld, ref taker, ref spent);
        standing.Add(Offer(PlayerRevive.OfferId));
        leader = Leader(standing, out leaderHeld);
        InteractKey.Step(false, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(InteractKey.HeldBy(PlayerRevive.OfferId, true, taker, spent, leader, leaderHeld)
            && !InteractKey.HeldBy(SupportKit.MendOffer, true, taker, spent, leader, leaderHeld), "the hammer did not give way to the friend");
        standing.RemoveAt(1);
        leader = Leader(standing, out leaderHeld);
        InteractKey.Step(false, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(InteractKey.HeldBy(SupportKit.MendOffer, true, taker, spent, leader, leaderHeld), "the hammer did not come back");

        report.Rule("a takedown that comes on offer while a key is held needs a press of its own; a tap shorter than a frame is still given; let go, the key is nobody's");
        standing = new List<InteractKey.Entry> { Offer(PlayerExecution.OfferId), Offer(SupportKit.MendOffer) };
        taker = SupportKit.MendOffer;
        spent = false;
        leader = Leader(standing, out leaderHeld);
        InteractKey.Step(false, true, leader, leaderHeld, ref taker, ref spent);
        report.Expect(!InteractKey.HeldBy(PlayerExecution.OfferId, true, taker, spent, leader, leaderHeld)
            && !InteractKey.HeldBy(SupportKit.MendOffer, true, taker, spent, leader, leaderHeld), "a held key began or kept something under a takedown on offer");
        taker = null;
        InteractKey.Step(true, false, FieldPickups.OfferId, false, ref taker, ref spent);
        report.Expect(taker == FieldPickups.OfferId, "a tap within one frame was lost");
        InteractKey.Step(false, false, FieldPickups.OfferId, false, ref taker, ref spent);
        report.Expect(taker == null && !spent, "the key let go is still " + taker + "'s");
    }

    // Every one, two and three of the offers and one with no row, each taking or refusing, said in every order.
    private static void Every(CheckReport report)
    {
        report.Rule("in every standing: one leader exactly when something takes the key, the first in the table's order; shown are its prompt and the refusals before it");
        var names = new List<string>(Offers) { "a.mod.thing" };
        var group = new List<string>();
        var standing = new List<InteractKey.Entry>();
        int[][] orders3 = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        for (int a = 0; a < names.Count; a++)
            for (int b = a; b < names.Count; b++)
                for (int c = b; c < names.Count; c++)
                {
                    group.Clear();
                    group.Add(names[a]);
                    if (b != a) group.Add(names[b]);
                    if (c != b) group.Add(names[c]);
                    if (b == a && c != b) continue;                                 // the same two as a, c, c: counted once
                    for (int refusing = 0; refusing < 1 << group.Count; refusing++)
                        foreach (int[] order in orders3)
                        {
                            standing.Clear();
                            foreach (int at in order)
                                if (at < group.Count) standing.Add(Offer(group[at], (refusing & 1 << at) != 0 ? "no" : null));
                            Case(standing, report);
                        }
                }
    }

    private static void Case(List<InteractKey.Entry> standing, CheckReport report)
    {
        int best = -1;
        for (int i = 0; i < standing.Count; i++)
            if (standing[i].Takes && (best < 0 || InteractKey.Before(standing[i], standing[best]))) best = i;
        int lead = InteractKey.Lead(standing);
        bool right = lead == best;
        for (int i = 0; i < standing.Count && right; i++)
        {
            bool want = i == best || (!standing[i].Takes && (best < 0 || InteractKey.Before(standing[i], standing[best])));
            if (InteractKey.Shown(standing, i) != want) right = false;
        }
        if (right) { report.Expect(true, ""); return; }
        var said = new List<string>();
        foreach (var entry in standing) said.Add(entry.Id + (entry.Takes ? "" : " refusing"));
        report.Expect(false, string.Join(", ", said) + ": F is " + (lead >= 0 ? standing[lead].Id : "nobody") + "'s, shown " + Shown(standing));
    }
}
#endif
