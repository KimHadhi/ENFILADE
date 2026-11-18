using System.Collections.Generic;
using System.Text;

/// <summary>
/// Which spawn point the next zombie comes from, and why each of the others does not: one pure function of what was
/// measured of every point (Point), where the squad is on the mission's flow (Squad) and the step's rule (SpawnRule).
/// It makes nothing and asks the world nothing, so the rule can be read and checked without a map (Cases, the
/// console's /spawns cases), and a refusal can always be named (/spawns, Logs/spawns.txt).
/// <para>The model (Left 4 Dead's director, on what the maps already hold): the mission has a route, the steps' spots in
/// order laid over the walk mesh, and every place has a flow, how far along that route it lies (SpawnRoute). The squad's
/// front and rear are flows too. Zombies are not made out of nothing behind the players: they come out of where the
/// squad has not been (ahead on the flow), a room or passage made for them before open ground, hidden from every
/// player. A stretch the squad has been through and left empty is cleared and sends nothing again (SpawnGround). Where
/// the horde must come from all round (a hold, an escape, a search for cans) the step's rule says so; that is a row of
/// Tables/SpawnRules, never a case in code.</para>
/// <para>It was "any authored point 15-60 m from the nearest player that one ray says is hidden": no ahead or behind, no
/// cleared, so a doorway right behind the squad qualified, and a room it had emptied ten minutes before (user,
/// 2026-11-18: "플레이어 시야 밖 직후방에서 급습하듯 스폰되는 불합리한 판정 제거", "이미 소탕(클리어)한 구역에서 미션 진행 후
/// 적이 다시 리스폰되지 않도록").</para>
/// </summary>
internal static class SpawnPick
{
    /// <summary>What became of a point at a pick, in the order the reasons are weighed.</summary>
    public enum Verdict : byte
    {
        /// <summary>The one taken.</summary>
        Picked,
        /// <summary>Would do: one of its equals was taken.</summary>
        Fit,
        /// <summary>Would do, but a better kind of place did too (a room before open ground, a hidden place before a seen one).</summary>
        Outranked,
        /// <summary>No walk mesh there, no room for a body, or under a Support's searchlight.</summary>
        NoGround,
        /// <summary>Its place on the route is not worked out yet (the route was just laid).</summary>
        NoPlace,
        /// <summary>Not ahead of the squad's front, under a rule that sends from ahead only.</summary>
        NotAhead,
        /// <summary>On cleared ground, under a rule that keeps cleared ground shut.</summary>
        Cleared,
        /// <summary>Nearer to someone than the rule's nearest.</summary>
        TooNear,
        /// <summary>Inside someone's view from near.</summary>
        InView,
        /// <summary>No way over the walk mesh to the player it would head for.</summary>
        NoWay,
        /// <summary>On ground behind the squad's rear and nearer by walking than the rule's behind.</summary>
        RightBehind,
        /// <summary>Someone has a line of sight to a body standing there, and it is nearer than the rule lets a seen place be.</summary>
        Seen,
        /// <summary>Would do but for being farther than the rule's farthest: taken only when the rule says "farther" and nothing nearer does.</summary>
        TooFar
    }

    /// <summary>What was measured of one spawn point for a pick.</summary>
    public struct Point
    {
        /// <summary>It has walk mesh, room for a body and no searchlight over it.</summary>
        public bool Ground;
        /// <summary>Its place on the route is known: Flow, and whether its stretch is cleared.</summary>
        public bool Placed, OnCleared;
        /// <summary>A spawn room (Tables/SpawnPoints).</summary>
        public bool Room;
        /// <summary>How far along the mission's route it lies (m).</summary>
        public float Flow;
        /// <summary>The straight distance to the nearest player (m).</summary>
        public float Near;
        /// <summary>It is inside someone's view, nearer than the rule's viewNear.</summary>
        public bool InView;
        /// <summary>Someone has a line of sight to a body standing there. Only looked at when Early passed it.</summary>
        public bool Seen;
        /// <summary>The walk to the player it would head for (m); negative: no way. Only measured when Early passed it.</summary>
        public float Walk;
    }

    /// <summary>Where the squad is on the mission's flow.</summary>
    public struct Squad
    {
        /// <summary>The map has a route and someone has a place on it. Without: no flow to judge by, and the rules of
        /// flow (ahead, cleared, behind) say nothing; distance and sight still decide.</summary>
        public bool Known;
        /// <summary>The flow of the player farthest along, and of the one least far (m).</summary>
        public float Front, Rear;
    }

    /// <summary>
    /// The reasons that cost nothing to weigh. True with the verdict when one of them refuses the point: whoever
    /// measures need not look at it or search a path for it (the measuring's Seen and Walk are then never read).
    /// </summary>
    public static bool Early(in Point point, in Squad squad, SpawnRule rule, out Verdict verdict)
    {
        verdict = Verdict.Fit;
        if (!point.Ground) verdict = Verdict.NoGround;
        else if (squad.Known && !point.Placed) verdict = Verdict.NoPlace;
        // Travelling, they come from where the squad has not been yet: past its front.
        else if (squad.Known && rule.From == SpawnSide.Ahead && point.Flow <= squad.Front) verdict = Verdict.NotAhead;
        else if (squad.Known && rule.Cleared == ClearedGround.Shut && point.OnCleared) verdict = Verdict.Cleared;
        else if (point.Near < rule.Nearest) verdict = Verdict.TooNear;
        else if (point.InView) verdict = Verdict.InView;
        else return false;
        return true;
    }

    /// <summary>What one point is to this pick, before any is chosen: a refusal, TooFar, or Fit.</summary>
    public static Verdict Judge(in Point point, in Squad squad, SpawnRule rule)
    {
        if (Early(point, squad, rule, out var verdict)) return verdict;
        if (point.Walk < 0f) return Verdict.NoWay;
        if (squad.Known && point.Flow < squad.Rear && point.Walk < rule.Behind) return Verdict.RightBehind;
        if (point.Seen && (rule.SeenFrom <= 0f || point.Near < rule.SeenFrom)) return Verdict.Seen;
        return point.Walk > rule.Farthest ? Verdict.TooFar : Verdict.Fit;
    }

    // The kinds of place that will do, the best first: a hidden room, hidden open ground, then (far enough to be let) a
    // seen room and seen open ground.
    private static int Kind(in Point point) => (point.Seen ? 2 : 0) + (point.Room ? 0 : 1);

    /// <summary>
    /// The point the next zombie comes from, or -1 for none now. <paramref name="verdicts"/> (as long as the points)
    /// is filled with what became of each. Of the points that fit, the best kind is taken (Kind) and among its equals
    /// <paramref name="roll"/> (0..1) chooses. With none that fits, the rule's "otherwise": wait (-1), or the nearest
    /// by walking of those that only lie too far, a better kind before a nearer place.
    /// </summary>
    public static int Pick(IReadOnlyList<Point> points, in Squad squad, SpawnRule rule, float roll, Verdict[] verdicts)
    {
        int best = int.MaxValue, equals = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            verdicts[i] = Judge(point, squad, rule);
            if (verdicts[i] != Verdict.Fit) continue;
            int kind = Kind(point);
            if (kind < best) { best = kind; equals = 1; }
            else if (kind == best) equals++;
        }
        if (equals > 0)
        {
            int take = (int)((roll < 0f ? 0f : roll > 1f ? 1f : roll) * equals), picked = -1;
            if (take >= equals) take = equals - 1;
            for (int i = 0; i < points.Count; i++)
            {
                if (verdicts[i] != Verdict.Fit) continue;
                if (Kind(points[i]) != best) verdicts[i] = Verdict.Outranked;
                else if (take-- == 0) picked = i;
            }
            verdicts[picked] = Verdict.Picked;
            return picked;
        }
        if (rule.Otherwise != SpawnOtherwise.Farther) return -1;
        int far = -1;
        for (int i = 0; i < points.Count; i++)
        {
            if (verdicts[i] != Verdict.TooFar) continue;
            if (far < 0) { far = i; continue; }
            int kind = Kind(points[i]), kept = Kind(points[far]);
            if (kind < kept || kind == kept && points[i].Walk < points[far].Walk) far = i;
        }
        if (far >= 0) verdicts[far] = Verdict.Picked;
        return far;
    }

    /// <summary>
    /// /spawns cases: Pick over the cases the rules are for, what each must give written beside what it gives. No map and
    /// no zombie needed.
    /// </summary>
    public static string Cases()
    {
        var roam = new SpawnRule { From = SpawnSide.Ahead, Cleared = ClearedGround.Shut, Otherwise = SpawnOtherwise.Farther };
        var roamWait = new SpawnRule { From = SpawnSide.Ahead, Cleared = ClearedGround.Shut, Otherwise = SpawnOtherwise.Wait };
        var hold = new SpawnRule { From = SpawnSide.Around, Cleared = ClearedGround.Through, Otherwise = SpawnOtherwise.Wait };
        var search = new SpawnRule { From = SpawnSide.Around, Cleared = ClearedGround.Shut, Otherwise = SpawnOtherwise.Wait };
        var holdBack = new SpawnRule { From = SpawnSide.Around, Cleared = ClearedGround.Through, Otherwise = SpawnOtherwise.Wait, Behind = 25f };
        var unseenOnly = new SpawnRule { From = SpawnSide.Ahead, Cleared = ClearedGround.Shut, Otherwise = SpawnOtherwise.Wait, SeenFrom = 0f };
        var squad = new Squad { Known = true, Front = 100f, Rear = 90f };
        var lost = new Squad();
        // A point that would do under every rule unless a case changes it: on the ground, placed, hidden, 30 m off.
        Point At(float flow) => new Point { Ground = true, Placed = true, Flow = flow, Near = 30f, Walk = 35f };
        Point With(Point point, System.Action<PointBox> change)
        {
            var box = new PointBox { Value = point };
            change(box);
            return box.Value;
        }
        var cases = new (string Name, SpawnRule Rule, Squad Squad, Point[] Points, int Picked, Verdict[] Verdicts)[]
        {
            ("travelling: the place behind is refused, the place ahead taken", roam, squad,
                new[] { At(50f), At(130f) }, 1, new[] { Verdict.NotAhead, Verdict.Picked }),
            ("travelling: a place between the rear and the front is not ahead", roam, squad,
                new[] { At(95f) }, -1, new[] { Verdict.NotAhead }),
            ("travelling: nothing ahead will do and nothing lies farther on: nobody comes", roam, squad,
                new[] { At(40f), At(80f) }, -1, new[] { Verdict.NotAhead, Verdict.NotAhead }),
            ("travelling: only places too far ahead; the rule says farther: the nearest of them", roam, squad,
                new[] { With(At(300f), p => p.Value.Walk = 180f), With(At(200f), p => p.Value.Walk = 90f) }, 1,
                new[] { Verdict.TooFar, Verdict.Picked }),
            ("travelling: only places too far ahead; the rule says wait: nobody comes", roamWait, squad,
                new[] { With(At(200f), p => p.Value.Walk = 90f) }, -1, new[] { Verdict.TooFar }),
            ("a room goes before open ground", roam, squad,
                new[] { At(130f), With(At(140f), p => p.Value.Room = true) }, 1, new[] { Verdict.Outranked, Verdict.Picked }),
            ("a hidden place goes before a seen one, a room though it be", roam, squad,
                new[] { With(At(130f), p => { p.Value.Seen = true; p.Value.Room = true; p.Value.Near = 40f; }), At(140f) }, 1,
                new[] { Verdict.Outranked, Verdict.Picked }),
            ("no hidden place: a seen one far enough off is taken, a nearer seen one never", roam, squad,
                new[] { With(At(130f), p => { p.Value.Seen = true; p.Value.Near = 40f; }), With(At(120f), p => { p.Value.Seen = true; p.Value.Near = 20f; }) }, 0,
                new[] { Verdict.Picked, Verdict.Seen }),
            ("seenFrom 0: a seen place is never taken", unseenOnly, squad,
                new[] { With(At(130f), p => { p.Value.Seen = true; p.Value.Near = 55f; }) }, -1, new[] { Verdict.Seen }),
            ("a hold: from behind too, cleared ground lets them through", hold, squad,
                new[] { With(At(40f), p => p.Value.OnCleared = true) }, 0, new[] { Verdict.Picked }),
            ("a search for cans: all round, but cleared ground stays shut", search, squad,
                new[] { With(At(40f), p => p.Value.OnCleared = true), At(60f) }, 1, new[] { Verdict.Cleared, Verdict.Picked }),
            ("a hold with behind 25: right behind the rear is refused, farther back taken", holdBack, squad,
                new[] { With(At(80f), p => p.Value.Walk = 18f), With(At(60f), p => p.Value.Walk = 30f) }, 1,
                new[] { Verdict.RightBehind, Verdict.Picked }),
            ("a hold with behind 25: a place ahead is not behind, however near by walking", holdBack, squad,
                new[] { With(At(110f), p => p.Value.Walk = 18f) }, 0, new[] { Verdict.Picked }),
            ("too near, inside a view, no way, no ground, no place yet: each refused by its own name", hold, squad,
                new[]
                {
                    With(At(130f), p => p.Value.Near = 10f), With(At(130f), p => p.Value.InView = true), With(At(130f), p => p.Value.Walk = -1f),
                    With(At(130f), p => p.Value.Ground = false), With(At(130f), p => p.Value.Placed = false)
                }, -1, new[] { Verdict.TooNear, Verdict.InView, Verdict.NoWay, Verdict.NoGround, Verdict.NoPlace }),
            ("a map without a route: the flow says nothing, distance and sight decide", roam, lost,
                new[] { With(At(0f), p => p.Value.Placed = false), With(At(0f), p => { p.Value.Placed = false; p.Value.Near = 5f; }) }, 0,
                new[] { Verdict.Picked, Verdict.TooNear }),
            ("equals: the roll chooses among them (the last at 0.99)", roam, squad,
                new[] { At(130f), At(140f), At(150f) }, 2, new[] { Verdict.Fit, Verdict.Fit, Verdict.Picked }),
            ("no points at all", roam, squad, new Point[0], -1, new Verdict[0])
        };
        var text = new StringBuilder();
        int wrong = 0;
        foreach (var one in cases)
        {
            var verdicts = new Verdict[one.Points.Length];
            int picked = Pick(one.Points, one.Squad, one.Rule, .99f, verdicts);
            bool right = picked == one.Picked;
            for (int i = 0; i < verdicts.Length && right; i++) right = verdicts[i] == one.Verdicts[i];
            if (!right) wrong++;
            text.Append(right ? "ok    " : "WRONG ").Append(one.Name).Append(": picked ").Append(picked).Append(" (").Append(one.Picked).Append(")");
            for (int i = 0; i < verdicts.Length; i++)
                text.Append(i == 0 ? " · " : ", ").Append(verdicts[i]).Append(verdicts[i] == one.Verdicts[i] ? "" : " (" + one.Verdicts[i] + ")");
            text.Append('\n');
        }
        text.Append("wrong: ").Append(wrong).Append('\n');
        return text.ToString();
    }

    // A point held so a case can change one thing of it in a line.
    private sealed class PointBox
    {
        public Point Value;
    }
}
