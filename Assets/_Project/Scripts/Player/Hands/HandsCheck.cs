#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// The editor's check of whose a player's hands are, without playing: the game's own rule (HandBoard) run over the
/// game's own table as the game reads it (Tables/Player/HandUses.csv through HandUses), for the rows the game names by
/// its own constants, so a row renamed or dropped stops this file compiling. Tools/Checks/hands_check.py runs a copy
/// of the rule outside Unity; this runs the rule itself, and the two say the same things in the same order, so a
/// difference between them is a mistake in one.
/// Logs/hands-check.txt has a line per rule with how many cases kept it. Whoever changes these rules cannot play the
/// game, so the rules say for themselves whether they hold; the first of them is the user's own example (2026-11-18:
/// "오른손은 조준한 채로 왼손은 수류탄 대기하고 있으면 되잖아요"): a reload asked for with a grenade in the left hand
/// does not start at all.
/// It runs by itself once after every script reload and after a play, from ENFILADE → 입력 → 손의 주인 규칙 점검, and
/// when Temp/enfilade-handscheck.request is made (CheckOnRequest).
/// </summary>
[InitializeOnLoad]
internal static class HandsCheck
{
    private const string Request = "Temp/enfilade-handscheck.request", ReportPath = "Logs/hands-check.txt";

    // Whoever asks or states in a scene of the check: only that they are different things matters.
    private static readonly object Gun = "gun", Grenades = "grenades", Syringe = "syringe", Tool = "tool", Body = "body", Knife = "knife",
        Gesture = "gesture";

    // The rows the game states as facts (never asks for), by its own names.
    private static readonly string[] Facts =
    {
        HandUses.Dead, HandUses.Downed, HandUses.ZombieHold, HandUses.Execution, HandUses.Sheath, HandUses.GuardBreak, HandUses.Counter,
        HandUses.Bandage, HandUses.Climb, HandUses.Vault, HandUses.Roll, HandUses.Can,
    };
    private static readonly string[] Asks =
    {
        HandUses.Grenade, HandUses.Dose, HandUses.Repair, HandUses.GuardPose, HandUses.PointPing, HandUses.PointHold, HandUses.CalloutArm,
        HandUses.GunHold, HandUses.GunAim, HandUses.GunReload, HandUses.GunInspect, HandUses.GunSwap, HandUses.GunBash,
    };

    static HandsCheck() => new CheckOnRequest(Request, Run);

    [MenuItem("ENFILADE/입력/손의 주인 규칙 점검")]
    private static void Run()
    {
        var report = new CheckReport();
        try
        {
            HandUses.Forget();
            Table(report);
            Example(report);
            Decisions(report);
            Order(report);
            Pairs(report);
            Runs(report);
        }
        catch (Exception problem)
        {
            report.Stopped(problem);
        }
        report.Write(ReportPath, "hand owner rules check, " + HandUses.All.Count + " uses");
    }

    private static HandUse U(string id) => HandUses.Of(id);

    private static string Of(HandBoard board, HandSide hand) => board.Holder(hand)?.Use.Id;

    private static bool Take(HandBoard board, string id, object who, int frame) => board.Take(U(id), who, frame, out _);

    private static bool Could(HandBoard board, string id, object who, int frame) => board.Could(U(id), who, frame, out _);

    private static void Withdraw(HandBoard board, object who, string id)
    {
        foreach (var entry in board.Entries)
            if (entry.Fact && ReferenceEquals(entry.Who, who) && entry.Use.Id == id)
            {
                board.Withdraw(entry);
                return;
            }
    }

    private static HandBoard GunInHand()
    {
        var board = new HandBoard();
        Take(board, HandUses.GunHold, Gun, 0);
        return board;
    }

    // The table as the game reads it.
    private static void Table(CheckReport report)
    {
        report.Rule("every cell of HandUses.csv says something the game reads, and every row the game names is there");
        var problems = new List<string>();
        foreach (string id in GameTables.RowIds(HandUses.TableName))
            if (!string.IsNullOrEmpty(id) && id[0] != '#' && GameTables.TryGetRow(HandUses.TableName, id, out var row)) HandUses.Read(id.Trim(), row, problems);
        report.Expect(problems.Count == 0, string.Join("; ", problems));
        foreach (string id in Facts) report.Expect(HandUses.Has(id), id + " has no row");
        foreach (string id in Asks) report.Expect(HandUses.Has(id), id + " has no row");
        report.Expect(HandUses.Has(HandUses.GunFire), HandUses.GunFire + " has no row");
        report.Expect(HandUses.All.Count == Facts.Length + Asks.Length + 1, "the table has " + HandUses.All.Count + " rows and the check knows "
            + (Facts.Length + Asks.Length + 1) + ": a row was added without a scene here (a mod's row is no part of this check)");
        report.Note(HandUses.All.Count + " rows read");

        report.Rule("a row that locks the hands is a fact of both hands; one that takes the right hand as a fact says what becomes of the gun; a fact outranks anything held in the fist but a landing roll");
        var locking = new List<string>();
        foreach (var use in HandUses.All)
        {
            if (use.Locks)
            {
                locking.Add(use.Id);
                report.Expect(use.Needs == HandSide.Both && use.Rank >= 100, use.Id + " locks the hands without being a fact of both");
            }
            if (use.Gun != GunFate.Leaves) report.Expect((use.Needs & HandSide.Right) != 0, use.Id + " says what becomes of the gun without taking the right hand");
            if (use.Rank >= 100 && (use.Needs & HandSide.Right) != 0) report.Expect(use.Gun != GunFate.Leaves, use.Id + " takes the right hand as a fact and leaves the gun unsaid");
        }
        foreach (string id in Facts)
            if (id != HandUses.Roll) report.Expect(U(id).Rank > U(HandUses.Grenade).Rank, id + " does not outrank a grenade in the fist");
        locking.Sort(string.CompareOrdinal);
        report.Expect(string.Join(",", locking) == "detente.bandage,melee.counter,melee.execution,melee.guardbreak",
            "the rows that lock the hands are " + string.Join(",", locking) + ", not the four PlayerMovement.BodyLocked is made of");
        report.Expect(U(HandUses.GunFire).Rank == HandUse.NoRank, "the trigger holds a hand (it only asks whether the right hand is the gun's)");
    }

    // The user's own example.
    private static void Example(CheckReport report)
    {
        report.Rule("the user's example: the right hand aims and fires while the left holds a grenade ready, and a reload asked for meanwhile does not start");
        var board = GunInHand();
        report.Expect(Of(board, HandSide.Left) == HandUses.GunHold && Of(board, HandSide.Right) == HandUses.GunHold, "a gun at rest does not have both hands");
        report.Expect(Take(board, HandUses.Grenade, Grenades, 1), "G did not get the left hand from a gun at rest");
        report.Expect(Of(board, HandSide.Left) == HandUses.Grenade && Of(board, HandSide.Right) == HandUses.GunHold && board.Has(Gun), "the grenade took more than the left hand");
        report.Expect(Take(board, HandUses.GunAim, Gun, 2), "the right hand could not aim with a grenade in the left");
        report.Expect(Of(board, HandSide.Left) == HandUses.Grenade && Of(board, HandSide.Right) == HandUses.GunAim, "aiming moved the grenade's hand");
        report.Expect(Could(board, HandUses.GunFire, Gun, 3), "the trigger is not the gun's with a grenade in the left hand");
        bool reload = board.Could(U(HandUses.GunReload), Gun, 4, out var why);
        report.Expect(!reload && why.Hand == HandSide.Left && why.Holder != null && why.Holder.Id == HandUses.Grenade,
            "a reload asked with a grenade held was not refused for the left hand: " + why);
        report.Expect(!Take(board, HandUses.GunReload, Gun, 4) && Of(board, HandSide.Right) == HandUses.GunAim && board.Has(Gun), "a refused reload changed what the gun has");
        report.Expect(board.Give(Grenades) && Of(board, HandSide.Left) == HandUses.GunAim, "the left hand did not go back under the aimed gun after the throw");
        report.Expect(Take(board, HandUses.GunReload, Gun, 5) && Of(board, HandSide.Left) == HandUses.GunReload && Of(board, HandSide.Right) == HandUses.GunReload,
            "a reload did not start once the hand was back");
        report.Note("refused as: " + why);
    }

    // What the user decided on 2026-11-18.
    private static void Decisions(CheckReport report)
    {
        report.Rule("a grenade taken out during a reload has the left hand and the reload is over; the gun goes on in the right");
        var board = GunInHand();
        Take(board, HandUses.GunReload, Gun, 1);
        report.Expect(Take(board, HandUses.Grenade, Grenades, 2) && !board.Has(Gun), "G during a reload did not end the reload");
        report.Expect(Take(board, HandUses.GunHold, Gun, 3) && Of(board, HandSide.Right) == HandUses.GunHold && Of(board, HandSide.Left) == HandUses.Grenade,
            "the gun did not go on in the right hand");

        report.Rule("first come keeps the hand between a reload and a dose (\"먼저 하는 게 일단 짱\")");
        board = GunInHand();
        Take(board, HandUses.GunReload, Gun, 1);
        report.Expect(!Take(board, HandUses.Dose, Syringe, 2) && Of(board, HandSide.Left) == HandUses.GunReload, "a dose took the hand from a reload under way");
        board = GunInHand();
        report.Expect(Take(board, HandUses.Dose, Syringe, 1) && board.Has(Gun), "a dose could not have the left hand of a gun at rest");
        report.Expect(!Could(board, HandUses.GunReload, Gun, 2), "a reload could start with the syringe in the left hand");
        board.Give(Syringe);
        report.Expect(Could(board, HandUses.GunReload, Gun, 3), "a reload could not start after the dose");

        report.Rule("a repair gives way to a reload, and a repair never starts during one (\"수리 중엔 장전으로 넘어갈 수 있게 … 그 역은 안 되겠죠\")");
        board = GunInHand();
        report.Expect(Take(board, HandUses.Repair, Tool, 1), "the tool could not have the left hand of a gun at rest");
        report.Expect(Take(board, HandUses.GunReload, Gun, 2) && !board.Has(Tool), "a reload could not take over from a repair");
        report.Expect(!Take(board, HandUses.Repair, Tool, 3), "a repair started during a reload");
        Take(board, HandUses.GunHold, Gun, 4);
        report.Expect(Take(board, HandUses.Repair, Tool, 5), "the tool did not come back after the reload");

        report.Rule("V is the gun's bash only with both hands on the gun (\"발차기로 하죠\")");
        board = GunInHand();
        report.Expect(Could(board, HandUses.GunBash, Gun, 1), "the gun could not bash with both hands on it");
        Take(board, HandUses.Grenade, Grenades, 1);
        report.Expect(!Could(board, HandUses.GunBash, Gun, 2), "the gun could bash with a grenade in the left hand");
        board = GunInHand();
        Take(board, HandUses.Dose, Syringe, 1);
        report.Expect(!Could(board, HandUses.GunBash, Gun, 2), "the gun could bash with the syringe in the left hand");
        board = GunInHand();
        Take(board, HandUses.Repair, Tool, 1);
        report.Expect(Take(board, HandUses.GunBash, Gun, 2) && !board.Has(Tool), "a bash did not take the hand from a repair");

        report.Rule("a grenade in the fist stays through a landing roll (the gun is put away) and goes back for a vault, a grab, an execution, a climb, a can, a treatment, going down, dying");
        board = GunInHand();
        Take(board, HandUses.Grenade, Grenades, 1);
        board.State(U(HandUses.Roll), Body, 2);
        report.Expect(board.Has(Grenades) && Of(board, HandSide.Left) == HandUses.Grenade && Of(board, HandSide.Right) == HandUses.Roll && !board.Has(Gun),
            "a landing roll did not leave the grenade in the fist and take the gun's hand");
        Withdraw(board, Body, HandUses.Roll);
        report.Expect(Take(board, HandUses.GunHold, Gun, 3) && Of(board, HandSide.Left) == HandUses.Grenade, "the gun did not come back after the roll");
        foreach (string fact in new[] { HandUses.Vault, HandUses.ZombieHold, HandUses.Execution, HandUses.Climb, HandUses.Dead, HandUses.Downed, HandUses.Can, HandUses.Bandage })
        {
            board = GunInHand();
            Take(board, HandUses.Grenade, Grenades, 1);
            board.State(U(fact), Body, 2);
            report.Expect(!board.Has(Grenades) && Of(board, HandSide.Left) == fact, fact + " did not take the grenade's hand");
            report.Expect(!Take(board, HandUses.Grenade, Grenades, 3), "a grenade could be taken out during " + fact);
        }
        board = new HandBoard();
        board.State(U(HandUses.Sheath), Knife, 1);
        report.Expect(Take(board, HandUses.Grenade, Grenades, 2) && !Could(board, HandUses.GunHold, Gun, 2), "the knife going back did not leave the left hand free and keep the right");

        report.Rule("an execution locks the hands for as long as it is so, and takes the gun's");
        board = GunInHand();
        board.State(U(HandUses.Execution), Body, 1);
        report.Expect(board.Locked && !board.Has(Gun), "an execution did not lock the hands and take the gun's");
        Withdraw(board, Body, HandUses.Execution);
        report.Expect(!board.Locked, "the lock outlived the execution");
    }

    // Today's order between the left hand's uses, as the code had it before there was an owner.
    private static void Order(CheckReport report)
    {
        report.Rule("the order the left hand's uses had before there was an owner still holds, against each other and against the gun");
        string[][] cases =
        {
            new[] { HandUses.PointHold, HandUses.GunAim, HandUses.GunAim },
            new[] { HandUses.GunAim, HandUses.PointHold, HandUses.GunAim },
            new[] { HandUses.GunAim, HandUses.PointPing, HandUses.PointPing },
            new[] { HandUses.GunReload, HandUses.PointPing, HandUses.GunReload },
            new[] { HandUses.GunSwap, HandUses.PointPing, HandUses.GunSwap },
            new[] { HandUses.GunInspect, HandUses.PointPing, HandUses.GunInspect },
            new[] { HandUses.GunReload, HandUses.PointHold, HandUses.GunReload },
            new[] { HandUses.PointHold, HandUses.GunReload, HandUses.GunReload },
            new[] { HandUses.GunAim, HandUses.GuardPose, HandUses.GuardPose },
            new[] { HandUses.GunSwap, HandUses.GuardPose, HandUses.GuardPose },
            new[] { HandUses.GunReload, HandUses.GuardPose, HandUses.GunReload },
            new[] { HandUses.Grenade, HandUses.GuardPose, HandUses.Grenade },
            new[] { HandUses.Grenade, HandUses.PointHold, HandUses.Grenade },
            new[] { HandUses.Grenade, HandUses.Repair, HandUses.Grenade },
            new[] { HandUses.Grenade, HandUses.Dose, HandUses.Grenade },
            new[] { HandUses.PointHold, HandUses.Grenade, HandUses.Grenade },
            new[] { HandUses.Repair, HandUses.Grenade, HandUses.Grenade },
            new[] { HandUses.Dose, HandUses.Grenade, HandUses.Grenade },
            new[] { HandUses.GunInspect, HandUses.Grenade, HandUses.Grenade },
            new[] { HandUses.GunInspect, HandUses.Dose, HandUses.Dose },
            new[] { HandUses.GunSwap, HandUses.Dose, HandUses.Dose },
            new[] { HandUses.GunSwap, HandUses.Repair, HandUses.Repair },
            new[] { HandUses.GunSwap, HandUses.Grenade, HandUses.Grenade },
            new[] { HandUses.GunAim, HandUses.Repair, HandUses.Repair },
            new[] { HandUses.GunAim, HandUses.Dose, HandUses.Dose },
            new[] { HandUses.GunHold, HandUses.CalloutArm, HandUses.GunHold },
        };
        foreach (var one in cases)
        {
            var board = GunInHand();
            for (int at = 0; at < 2; at++) Take(board, one[at], one[at].StartsWith("gun.", StringComparison.Ordinal) ? Gun : one[at], at + 1);
            report.Expect(Of(board, HandSide.Left) == one[2], "with " + one[0] + " standing, " + one[1] + " asked: the left hand is "
                + (Of(board, HandSide.Left) ?? "nobody") + "'s, not " + one[2] + "'s");
        }
        report.Expect(Take(new HandBoard(), HandUses.CalloutArm, Gesture, 1), "a callout's arm could not move with no gun in the hands");
    }

    private static bool Stated(HandUse use) => Array.IndexOf(Facts, use.Id) >= 0;

    // What a use brings to a hand: its rank, and whether it needs the hand; false when it has no want of it.
    private static bool Wants(HandUse use, HandSide hand, out bool needs)
    {
        needs = (use.Needs & hand) != 0;
        return needs || (use.Also & hand) != 0;
    }

    private static bool Wins(HandUse asker, HandUse holder, HandSide hand, bool sameFrame)
    {
        Wants(asker, hand, out bool needs);
        Wants(holder, hand, out bool holderNeeds);
        if (asker.Rank != holder.Rank) return asker.Rank > holder.Rank;
        if (needs != holderNeeds) return needs;
        return sameFrame && string.CompareOrdinal(asker.Id, holder.Id) < 0;
    }

    private static readonly HandSide[] Each = { HandSide.Left, HandSide.Right };

    // Every two rows, the one standing and the other asking or being stated: the board answers as their ranks say.
    private static void Pairs(CheckReport report)
    {
        report.Rule("for every two uses, one standing and the other asked or stated after it: the answer is the one their ranks give, a refusal changes nothing, and an ask that lost a hand it needs is gone");
        foreach (var holder in HandUses.All)
        {
            if (holder.Rank == HandUse.NoRank) continue;
            foreach (var asker in HandUses.All)
            {
                if (asker == holder || asker.Rank == HandUse.NoRank) continue;
                var board = new HandBoard();
                if (Stated(holder)) board.State(holder, "A", 1);
                else board.Take(holder, "A", 1, out _);
                string leftBefore = Of(board, HandSide.Left), rightBefore = Of(board, HandSide.Right);
                bool all = true, any = false, lostNeeded = false;
                foreach (var hand in Each)
                {
                    if ((asker.Needs & hand) == 0 || Of(board, hand) != holder.Id) continue;
                    bool wins = Wins(asker, holder, hand, false);
                    all &= wins;
                    any |= wins;
                    lostNeeded |= wins && (holder.Needs & hand) != 0;
                }
                string pair = asker.Id + " over " + holder.Id;
                if (Stated(asker))
                {
                    board.State(asker, "B", 2);
                    foreach (var hand in Each)
                        if ((asker.Needs & hand) != 0 && (hand == HandSide.Left ? leftBefore : rightBefore) == holder.Id)
                            report.Expect((Of(board, hand) == asker.Id) == Wins(asker, holder, hand, false), pair + ": the " + HandBoard.Name(hand) + " hand went wrong");
                }
                else
                {
                    bool granted = board.Take(asker, "B", 2, out _);
                    report.Expect(granted == all, pair + ": granted " + granted + ", the ranks say " + all);
                    if (granted)
                    {
                        foreach (var hand in Each)
                            if ((asker.Needs & hand) != 0) report.Expect(Of(board, hand) == asker.Id, pair + ": granted without its " + HandBoard.Name(hand) + " hand");
                    }
                    else
                    {
                        report.Expect(Of(board, HandSide.Left) == leftBefore && Of(board, HandSide.Right) == rightBefore, pair + ": refused, and something still changed");
                        continue;
                    }
                }
                if (!Stated(holder) && any) report.Expect(board.Has("A") != lostNeeded, pair + ": the one standing " + (board.Has("A") ? "stands" : "is gone")
                    + " after " + (lostNeeded ? "losing a hand it needs" : "losing none it needs"));
            }
        }
    }

    // Random runs: whatever is asked, stated, given back and withdrawn, in whatever order, the board keeps its word.
    private static void Runs(CheckReport report)
    {
        report.Rule("through 4000 random runs: no ask stands without a hand it needs, no hand lies free while something standing wants it, nothing weaker holds a hand over something stronger that wants it");
        var random = new System.Random(20261118);
        var all = HandUses.All;
        for (int run = 0; run < 4000; run++)
        {
            var board = new HandBoard();
            int frame = 0, steps = random.Next(3, 15);
            for (int step = 0; step < steps; step++)
            {
                frame += random.Next(4) == 0 ? 0 : 1;
                var use = all[random.Next(all.Count)];
                if (use.Rank == HandUse.NoRank) continue;
                bool put = random.NextDouble() < .72;
                if (Stated(use))
                {
                    if (put) board.State(use, Body, frame);
                    else Withdraw(board, Body, use.Id);
                }
                else
                {
                    object owner = use.Id.StartsWith("gun.", StringComparison.Ordinal) ? Gun : use.Id;
                    if (put) board.Take(use, owner, frame, out _);
                    else board.Give(owner);
                }
                foreach (var entry in board.Entries)
                {
                    if (!entry.Fact) report.Expect((entry.Owns & entry.Use.Needs) == entry.Use.Needs, "run " + run + ": " + entry.Use.Id + " stands without a hand it needs");
                    foreach (var hand in Each)
                        if ((entry.Owns & hand) != 0) report.Expect(board.Holder(hand) == entry, "run " + run + ": " + entry.Use.Id + " thinks it has a hand that is another's");
                }
                foreach (var hand in Each)
                {
                    var holder = board.Holder(hand);
                    foreach (var entry in board.Entries)
                    {
                        if (entry == holder || !Wants(entry.Use, hand, out bool needs) || (needs && !entry.Fact)) continue;
                        report.Expect(holder != null, "run " + run + ": the " + HandBoard.Name(hand) + " hand lies free while " + entry.Use.Id + " wants it");
                        if (holder != null) report.Expect(entry.Use.Rank <= holder.Use.Rank, "run " + run + ": " + holder.Use.Id + " holds the "
                            + HandBoard.Name(hand) + " hand over " + entry.Use.Id + ", which is stronger");
                    }
                }
            }
        }
    }
}
#endif
