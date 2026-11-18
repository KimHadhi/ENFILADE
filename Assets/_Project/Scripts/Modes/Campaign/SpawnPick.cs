using System.Collections.Generic;
using System.Text;

/// <summary>
/// Which spawn point the next zombie comes from, and why each of the others does not: one pure function of what was
/// measured of every point (Point), where the squad is on the mission's flow (Squad) and the step's rule (SpawnRule).
/// It makes nothing and asks the world nothing, so the rule can be read and checked without a map (Cases, the
/// console's /spawns cases), and a refusal can always be named (/spawns, Logs/spawns.txt).
/// <para>The model (Left 4 Dead's director, on what the maps already hold): the mission has a route, the steps' spots in
/// order laid over the walk mesh, and every place has a flow, how far along that route it lies (SpawnRoute). Zombies are
/// not made out of nothing behind the players. Travelling, they come out of where the squad has not been yet: ahead on
/// the flow. A place the squad has cleared sends nothing again (SpawnGround); a spawn room, which the squad cannot
/// clear, goes on sending and goes before open ground. Nothing ever comes from a place someone can see, nor from behind
/// the back of the player it would go for. Where the horde must come from all round (a hold, the finale, a search for
/// cans) the step's rule says so; that is a row of Tables/SpawnRules, never a case in code.</para>
/// <para>It was "any authored point 15-60 m from the nearest player that one ray says is hidden": no ahead or behind, no
/// cleared, so a doorway right behind the squad qualified, and a room it had emptied ten minutes before (user,
/// 2026-11-18: "플레이어 시야 밖 직후방에서 급습하듯 스폰되는 불합리한 판정 제거", "이미 소탕(클리어)한 구역에서 미션 진행 후
/// 적이 다시 리스폰되지 않도록"). The user on the first build of this model, the same day: a seen place is never used,
/// however far and whatever else there is ("아니요"); behind is never used ("뒤는 안 됩니다"); the finale may draw on
/// cleared ground ("피날레 구간을 말하는 것이라면 소탕한 곳에서 나와도 될 것 같습니다").</para>
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
        /// <summary>Would do, but a spawn room did too, and a room goes before open ground.</summary>
        Outranked,
        /// <summary>No walk mesh there, no room for a body, or under a Support's searchlight.</summary>
        NoGround,
        /// <summary>Its place on the route is not worked out yet (the route was just laid).</summary>
        NoPlace,
        /// <summary>Not ahead of the squad's front, under a rule that sends from ahead only.</summary>
        NotAhead,
        /// <summary>Its ground is cleared, under a rule that keeps cleared ground shut.</summary>
        Cleared,
        /// <summary>Nearer to someone than the rule's nearest.</summary>
        TooNear,
        /// <summary>Inside someone's view from near.</summary>
        InView,
        /// <summary>No way over the walk mesh to the player it would go for.</summary>
        NoWay,
        /// <summary>Behind the back of the player it would go for, with less to walk to them than the rule's behind.</summary>
        Behind,
        /// <summary>Someone has a line of sight to a body standing there.</summary>
        Seen,
        /// <summary>Would do but for being farther than the rule's farthest: taken only when the rule says "farther" and nothing nearer does.</summary>
        TooFar
    }

    /// <summary>What was measured of one spawn point for a pick.</summary>
    public struct Point
    {
        /// <summary>It has walk mesh, room for a body and no searchlight over it.</summary>
        public bool Ground;
        /// <summary>Its place on the route is known: Flow.</summary>
        public bool Placed;
        /// <summary>The squad has cleared its ground (SpawnGround); never so for a room.</summary>
        public bool OnCleared;
        /// <summary>A spawn room (Tables/SpawnPoints): a place the squad cannot clear.</summary>
        public bool Room;
        /// <summary>How far along the mission's route it lies (m).</summary>
        public float Flow;
        /// <summary>The straight distance to the nearest player (m): the one a zombie made there would go for.</summary>
        public float Near;
        /// <summary>It is inside someone's view, nearer than the rule's viewNear.</summary>
        public bool InView;
        /// <summary>It lies behind the back of the player it would go for: more than a quarter turn off the way they face.</summary>
        public bool AtBack;
        /// <summary>Someone has a line of sight to a body standing there. Only looked at when Early passed it.</summary>
        public bool Seen;
        /// <summary>The walk to the player it would go for (m); negative: no way. Only measured when Early passed it.</summary>
        public float Walk;
    }

    /// <summary>Where the squad is on the mission's flow.</summary>
    public struct Squad
    {
        /// <summary>The map has a route and someone has a place on it. Without: no flow to judge by, and the rule of
        /// flow (ahead) says nothing; everything else still decides.</summary>
        public bool Known;
        /// <summary>The flow of the player farthest along (m).</summary>
        public float Front;
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
        else if (rule.Cleared == ClearedGround.Shut && point.OnCleared) verdict = Verdict.Cleared;
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
        // Nobody gets one that appears behind them and is on them in a moment.
        if (point.AtBack && point.Walk < rule.Behind) return Verdict.Behind;
        // A body does not come into being where someone can see it, however far off.
        if (point.Seen) return Verdict.Seen;
        return point.Walk > rule.Farthest ? Verdict.TooFar : Verdict.Fit;
    }

    /// <summary>
    /// The point the next zombie comes from, or -1 for none now. <paramref name="verdicts"/> (as long as the points)
    /// is filled with what became of each. Of the points that fit, a spawn room goes before open ground, and among
    /// equals <paramref name="roll"/> (0..1) chooses. With none that fits, the rule's "otherwise": wait (-1), or the
    /// nearest by walking of those that only lie too far, a room before open ground.
    /// </summary>
    public static int Pick(IReadOnlyList<Point> points, in Squad squad, SpawnRule rule, float roll, Verdict[] verdicts)
    {
        bool room = false;
        int equals = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            verdicts[i] = Judge(point, squad, rule);
            if (verdicts[i] != Verdict.Fit) continue;
            if (point.Room && !room) { room = true; equals = 1; }
            else if (point.Room == room) equals++;
        }
        if (equals > 0)
        {
            int take = (int)((roll < 0f ? 0f : roll > 1f ? 1f : roll) * equals), picked = -1;
            if (take >= equals) take = equals - 1;
            for (int i = 0; i < points.Count; i++)
            {
                if (verdicts[i] != Verdict.Fit) continue;
                if (points[i].Room != room) verdicts[i] = Verdict.Outranked;
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
            if (far < 0 || points[i].Room && !points[far].Room || points[i].Room == points[far].Room && points[i].Walk < points[far].Walk) far = i;
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
        var hold = new SpawnRule { From = SpawnSide.Around, Cleared = ClearedGround.Shut, Otherwise = SpawnOtherwise.Wait };
        var finale = new SpawnRule { From = SpawnSide.Around, Cleared = ClearedGround.Through, Otherwise = SpawnOtherwise.Wait };
        var squad = new Squad { Known = true, Front = 100f };
        var lost = new Squad();
        // A point that would do under every rule unless a case changes it: on the ground, placed, hidden, in front of the
        // player it would go for, 30 m off and 35 m to walk.
        Point At(float flow) => new Point { Ground = true, Placed = true, Flow = flow, Near = 30f, Walk = 35f };
        Point With(Point point, System.Action<PointBox> change)
        {
            var box = new PointBox { Value = point };
            change(box);
            return box.Value;
        }
        var cases = new (string Name, SpawnRule Rule, Squad Squad, Point[] Points, int Picked, Verdict[] Verdicts)[]
        {
            ("travelling: the place behind on the route is refused, the place ahead taken", roam, squad,
                new[] { At(50f), At(130f) }, 1, new[] { Verdict.NotAhead, Verdict.Picked }),
            ("travelling: nothing ahead will do and nothing lies farther on: nobody comes", roam, squad,
                new[] { At(40f), At(80f) }, -1, new[] { Verdict.NotAhead, Verdict.NotAhead }),
            ("travelling: only places too far ahead; the rule says farther: the nearest of them", roam, squad,
                new[] { With(At(300f), p => p.Value.Walk = 180f), With(At(200f), p => p.Value.Walk = 90f) }, 1,
                new[] { Verdict.TooFar, Verdict.Picked }),
            ("travelling: only places too far ahead; the rule says wait: nobody comes", roamWait, squad,
                new[] { With(At(200f), p => p.Value.Walk = 90f) }, -1, new[] { Verdict.TooFar }),
            ("a room goes before open ground", roam, squad,
                new[] { At(130f), With(At(140f), p => p.Value.Room = true) }, 1, new[] { Verdict.Outranked, Verdict.Picked }),
            ("too far: a room goes before a nearer open place", roam, squad,
                new[] { With(At(200f), p => p.Value.Walk = 90f), With(At(300f), p => { p.Value.Walk = 180f; p.Value.Room = true; }) }, 1,
                new[] { Verdict.TooFar, Verdict.Picked }),
            ("a seen place is never taken: not far off, not a room, not when nothing else will do", roam, squad,
                new[] { With(At(130f), p => { p.Value.Seen = true; p.Value.Near = 55f; }), With(At(140f), p => { p.Value.Seen = true; p.Value.Room = true; }) }, -1,
                new[] { Verdict.Seen, Verdict.Seen }),
            ("a hold: all round, but cleared ground stays shut and a room is never cleared", hold, squad,
                new[] { With(At(40f), p => p.Value.OnCleared = true), With(At(60f), p => p.Value.Room = true), At(70f) }, 1,
                new[] { Verdict.Cleared, Verdict.Picked, Verdict.Outranked }),
            ("the finale: cleared ground lets them through", finale, squad,
                new[] { With(At(40f), p => p.Value.OnCleared = true) }, 0, new[] { Verdict.Picked }),
            ("behind the back of the one it would go for, near by walking: refused under every rule", finale, squad,
                new[] { With(At(80f), p => { p.Value.AtBack = true; p.Value.Walk = 18f; }) }, -1, new[] { Verdict.Behind }),
            ("behind their back but a long walk off (the ramps' foot under the roof they stand on): taken", finale, squad,
                new[] { With(At(80f), p => { p.Value.AtBack = true; p.Value.Walk = 55f; p.Value.Near = 22f; }) }, 0, new[] { Verdict.Picked }),
            ("in front of them and near by walking: taken", hold, squad,
                new[] { With(At(110f), p => p.Value.Walk = 18f) }, 0, new[] { Verdict.Picked }),
            ("too near, inside a view, no way, no ground, no place yet: each refused by its own name", finale, squad,
                new[]
                {
                    With(At(130f), p => p.Value.Near = 10f), With(At(130f), p => p.Value.InView = true), With(At(130f), p => p.Value.Walk = -1f),
                    With(At(130f), p => p.Value.Ground = false), With(At(130f), p => p.Value.Placed = false)
                }, -1, new[] { Verdict.TooNear, Verdict.InView, Verdict.NoWay, Verdict.NoGround, Verdict.NoPlace }),
            ("a map without a route: ahead says nothing; cleared, distance, sight and backs still decide", roam, lost,
                new[]
                {
                    With(At(0f), p => p.Value.Placed = false), With(At(0f), p => { p.Value.Placed = false; p.Value.OnCleared = true; }),
                    With(At(0f), p => { p.Value.Placed = false; p.Value.Near = 5f; })
                }, 0, new[] { Verdict.Picked, Verdict.Cleared, Verdict.TooNear }),
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
