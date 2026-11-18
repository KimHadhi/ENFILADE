#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// InjectBench: the run to a friend out of the arm's reach (InjectApproach on the body's surge, GroundSurge), and whether
// the way to them is clear (SurgeWay). One part of the class; the rest is in InjectBench.cs.
// The user, 2026-11-18: "닿는 거리로 빠르게 이동하죠, 그 써지 기능처럼요. 물론 충돌 판정이나 벽을 뚫는 그런 방지는 대비를 해야
// 할 것입니다." So what is checked without playing is what must never go wrong: that a wall, a shut door, a pit, a drop
// or a step too high is no way, that a stair is one, that a pane between two bodies in reach still keeps the hand from
// reaching through; first on worlds made up for it (after every script reload, Logs/InjectBench/way.txt), then on the
// game's own physics with a body's capsule on boxes laid out for it (with the bench's run); and that from where a run
// ends, and from the furthest that still counts as in reach, the arm does reach a friend whichever way they stand.
// The run itself (the velocity LocomotionState keeps, the capsule stopping at what it meets) happens only in play.
internal static partial class InjectBench
{
    private const string WayPath = Folder + "/way.txt";
    private static bool wayRan;

    // The walk's numbers for the made-up cases, written out here so the cases say the same whatever the table holds.
    private static readonly SurgeWay.Rules CaseRules = new SurgeWay.Rules { Stride = .25f, Rise = .35f, Drop = .45f, Level = .6f };
    private const float CaseNear = .5f, CaseMost = 3.5f, CaseReachAt = 1.1f;

    private static void RunWay()
    {
        var report = new Report();
        try
        {
            WayCases(report);
        }
        catch (Exception problem)
        {
            report.Failed++;
            report.Line("  FAIL the check itself stopped: " + problem);
        }
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(WayPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} the way of a run to a friend, on worlds made up for it, {report.Looked} cases: "
                + (report.Failed == 0 ? "ALL KEPT" : report.Failed + " BROKEN") + "\n"
                + $"A body at z 0 runs ahead (+z) to within {CaseNear} m of a friend; a friend further than {CaseMost} m is out of range; strides of {CaseRules.Stride} m,\n"
                + $"a step up to {CaseRules.Rise} m or down to {CaseRules.Drop} m is a way, the friend within {CaseRules.Level} m of the run's end in height, the hand's line {CaseReachAt} m up.\n\n"
                + report.Text, new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("InjectBench: " + WayPath + " could not be written: " + error.Message);
        }
        if (report.Failed > 0) Debug.LogWarning($"InjectBench: {report.Failed} cases of a run's way are broken, see {WayPath}.");
    }

    // A made-up world along one line, ahead (+z): the ground's height at each z (NaN where there is none), and things
    // standing across the way at a z, each from a height to a height.
    private sealed class Made : SurgeWay.IWorld
    {
        public Func<float, float> Floor = z => 0f;
        public readonly List<(float z, float bottom, float top)> Across = new List<(float, float, float)>();
        // A body 1.8 m tall that steps over 0.3 m.
        private const float Tall = 1.8f, Step = .3f;

        public Made With(float z, float bottom, float top)
        {
            Across.Add((z, bottom, top));
            return this;
        }

        public bool Ground(Vector3 at, float rise, float drop, out float height)
        {
            height = Floor(at.z);
            return !float.IsNaN(height) && height <= at.y + rise && height >= at.y - drop;
        }

        public bool Blocked(Vector3 from, Vector3 to)
        {
            foreach (var (z, bottom, top) in Across)
                if (z > Mathf.Min(from.z, to.z) && z <= Mathf.Max(from.z, to.z) && top > from.y + Step && bottom < from.y + Tall) return true;
            return false;
        }

        public bool Open(Vector3 from, Vector3 to)
        {
            foreach (var (z, bottom, top) in Across)
            {
                if (z <= Mathf.Min(from.z, to.z) || z > Mathf.Max(from.z, to.z)) continue;
                float y = Mathf.Lerp(from.y, to.y, Mathf.InverseLerp(from.z, to.z, z));
                if (y > bottom && y < top) return false;
            }
            return true;
        }
    }

    // One case: the friend at a z and a height, what the walk must answer, and how long the run must be when it is one.
    private static void Case(Report report, string name, SurgeWay.IWorld world, Vector3 feet, Vector3 friend, float near, float run, params SurgeWay.Answer[] right)
    {
        var plan = SurgeWay.To(world, feet, friend, near, CaseMost, CaseReachAt, CaseRules);
        bool answered = Array.IndexOf(right, plan.Answer) >= 0;
        bool ran = run < 0f || plan.Answer != SurgeWay.Answer.Clear || Mathf.Abs(plan.Run - run) < .02f;
        report.Line($"  {name}: {plan.Answer}" + (plan.Answer == SurgeWay.Answer.Clear ? $", a run of {plan.Run:0.00} m"
            : plan.Answer == SurgeWay.Answer.There ? "" : $", stopped after {plan.Run:0.00} m"));
        report.Expect(answered && ran, name, $"it must be {string.Join(" or ", right)}" + (run >= 0f ? $" with a run of {run:0.00} m" : "")
            + $"; the walk said {plan.Answer} with {plan.Run:0.00} m");
    }

    private static void WayCases(Report report)
    {
        Vector3 Friend(float z, float height = 0f) => new Vector3(0f, height, z);
        // Eight stairs of 0.18 m, each 0.25 m deep, from z 0.6, and the landing at their top.
        float Stairs(float z) => z < .6f ? 0f : Mathf.Min(8f, Mathf.Floor((z - .6f) / .25f) + 1f) * .18f;
        var clear = SurgeWay.Answer.Clear;
        var blocked = SurgeWay.Answer.Blocked;
        var noGround = SurgeWay.Answer.NoGround;
        Case(report, "an open floor, the friend 3 m ahead", new Made(), Vector3.zero, Friend(3f), CaseNear, 2.5f, clear);
        Case(report, "the friend already within the arm's reach", new Made(), Vector3.zero, Friend(.45f), CaseNear, -1f, SurgeWay.Answer.There);
        Case(report, "the friend 5 m ahead, out of range", new Made(), Vector3.zero, Friend(5f), CaseNear, -1f, SurgeWay.Answer.TooFar);
        Case(report, "a wall across the way at 1.5 m", new Made().With(1.5f, 0f, 3f), Vector3.zero, Friend(3f), CaseNear, -1f, blocked);
        Case(report, "a shut door right before the friend (2.8 m)", new Made().With(2.8f, 0f, 2.2f), Vector3.zero, Friend(3f), CaseNear, -1f, blocked);
        Case(report, "a pit in the floor from 1 m to 2 m", new Made { Floor = z => z > 1f && z < 2f ? float.NaN : 0f }, Vector3.zero, Friend(3f), CaseNear, -1f, noGround);
        Case(report, "the floor drops 1 m at 1.5 m, the friend below", new Made { Floor = z => z < 1.5f ? 0f : -1f }, Vector3.zero, Friend(3f, -1f), CaseNear, -1f, noGround);
        Case(report, "stairs up to the friend (eight of 0.18 m)", new Made { Floor = Stairs }, Vector3.zero, Friend(3f, 1.44f), CaseNear, 2.5f, clear);
        Case(report, "stairs down to the friend", new Made { Floor = z => 1.44f - Stairs(z) }, new Vector3(0f, 1.44f, 0f), Friend(3f), CaseNear, 2.5f, clear);
        Case(report, "a kerb of 0.3 m at 1 m", new Made { Floor = z => z < 1f ? 0f : .3f }, Vector3.zero, Friend(3f, .3f), CaseNear, 2.5f, clear);
        Case(report, "a ledge of 0.7 m at 1 m, the friend on it", new Made { Floor = z => z < 1f ? 0f : .7f }, Vector3.zero, Friend(3f, .7f), CaseNear, -1f, noGround);
        Case(report, "a thing 0.2 m high lying across the way", new Made().With(1f, 0f, .2f), Vector3.zero, Friend(3f), CaseNear, 2.5f, clear);
        Case(report, "the friend in reach on a floor 1.2 m higher", new Made { Floor = z => z < .3f ? 0f : 1.2f }, Vector3.zero, Friend(.45f, 1.2f), CaseNear, -1f, noGround);
        Case(report, "the friend in reach behind a pane", new Made().With(.25f, 0f, 3f), Vector3.zero, Friend(.45f), CaseNear, -1f, blocked);
        Case(report, "the friend in reach over a rail 0.9 m high", new Made().With(.25f, 0f, .9f), Vector3.zero, Friend(.45f), CaseNear, -1f, SurgeWay.Answer.There);
        Case(report, "the friend where this body stands", new Made(), Vector3.zero, Friend(0f), CaseNear, -1f, SurgeWay.Answer.There);
    }

    // The same walk on the game's physics: a body's capsule (Player.prefab's) on boxes laid out in lanes, each lane a
    // case. What is tried is SurgeWay.Walker itself: its ray for the ground, its lifted capsule for what is in the way,
    // its line for the hand. The boxes stand in a scene of their own; where that scene's physics world does not answer
    // outside play, they are stood far under the open scene instead, in the editor's own, and taken away again.
    private static void WayOnPhysics(Report report)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            if (Level(report, scene, scene.GetPhysicsScene(), Vector3.zero)) return;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
        if (!Level(report, null, Physics.defaultPhysicsScene, Vector3.down * 4000f))
            report.Expect(false, "the game's physics", "a ray straight down at a floor made for it met nothing, in a scene of its own and in the editor's: the walk cannot be tried on physics here");
    }

    // The lanes built in a physics world (in a scene of their own, or the open one when none is given) and walked.
    // False, with nothing reported, when that world does not answer a ray at all.
    private static bool Level(Report report, Scene? own, PhysicsScene physics, Vector3 origin)
    {
        var made = new List<GameObject>();
        try
        {
            var walker = new SurgeWay.Walker { Scene = physics, Layers = ~0 };
            var player = Resources.Load<GameObject>("Characters/Player");
            if (player != null && player.TryGetComponent(out CharacterController capsule))
            {
                walker.Radius = capsule.radius;
                walker.Height = capsule.height;
                walker.Step = capsule.stepOffset;
                walker.Slope = capsule.slopeLimit;
            }
            int lanes = 0;
            // A box in a lane: from a z to a z and from a height to a height, 2 m wide; lanes stand 4 m apart.
            void Box(int lane, float zFrom, float zTo, float yFrom, float yTo)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.hideFlags = HideFlags.HideAndDontSave;
                if (own.HasValue) SceneManager.MoveGameObjectToScene(box, own.Value);
                box.transform.position = origin + new Vector3(lane * 4f, (yFrom + yTo) * .5f, (zFrom + zTo) * .5f);
                box.transform.localScale = new Vector3(2f, yTo - yFrom, zTo - zFrom);
                made.Add(box);
            }
            int Lane() => lanes++;
            void Floor(int lane, float zFrom = -1f, float zTo = 5f, float top = 0f) => Box(lane, zFrom, zTo, top - .2f, top);
            Vector3 At(int lane, float z, float height = 0f) => origin + new Vector3(lane * 4f, height, z);

            int open = Lane(), wall = Lane(), pit = Lane(), stairs = Lane(), kerb = Lane(), ledge = Lane(), drop = Lane(), pane = Lane(), rail = Lane();
            Floor(open);
            Floor(wall);
            Box(wall, 1.4f, 1.6f, 0f, 3f);
            Floor(pit, -1f, 1f);
            Floor(pit, 2f, 5f);
            Floor(stairs, -1f, .6f);
            for (int k = 1; k <= 8; k++) Box(stairs, .6f + .25f * (k - 1), .6f + .25f * k, -.2f, .18f * k);
            Box(stairs, 2.6f, 5f, -.2f, 1.44f);
            Floor(kerb, -1f, 1f);
            Box(kerb, 1f, 5f, -.2f, .25f);
            Floor(ledge);
            Box(ledge, 1f, 5f, 0f, .7f);
            Floor(drop, -1f, 1.5f);
            Floor(drop, 1.5f, 5f, -1f);
            Floor(pane);
            Box(pane, .3f, .32f, 0f, 3f);
            Floor(rail);
            Box(rail, .3f, .32f, 0f, .9f);
            Physics.SyncTransforms();
            bool answers = walker.Ground(At(open, 1f), CaseRules.Rise, CaseRules.Drop, out _);
            if (!answers && physics.IsValid() && physics != Physics.defaultPhysicsScene)
            {
                // A physics world of a scene's own takes its colliders in when it is stepped.
                physics.Simulate(.02f);
                answers = walker.Ground(At(open, 1f), CaseRules.Rise, CaseRules.Drop, out _);
            }
            if (!answers) return false;

            report.Line($"  the body: {walker.Radius:0.00} m round, {walker.Height:0.00} m tall, steps over {walker.Step:0.00} m, stands on slopes up to {walker.Slope:0}°; "
                + (own.HasValue ? "the boxes stand in a scene of their own" : "the boxes stand far under the open scene, in the editor's own physics world"));
            var clear = SurgeWay.Answer.Clear;
            var blocked = SurgeWay.Answer.Blocked;
            var noGround = SurgeWay.Answer.NoGround;
            Case(report, "physics: an open floor, the friend 3 m ahead", walker, At(open, 0f), At(open, 3f), CaseNear, 2.5f, clear);
            Case(report, "physics: a wall across the way at 1.5 m", walker, At(wall, 0f), At(wall, 3f), CaseNear, -1f, blocked);
            Case(report, "physics: a pit in the floor from 1 m to 2 m", walker, At(pit, 0f), At(pit, 3f), CaseNear, -1f, noGround);
            Case(report, "physics: stairs up to the friend (eight of 0.18 m)", walker, At(stairs, 0f), At(stairs, 3f, 1.44f), CaseNear, 2.5f, clear);
            Case(report, "physics: a kerb of 0.25 m at 1 m", walker, At(kerb, 0f), At(kerb, 3f, .25f), CaseNear, 2.5f, clear);
            Case(report, "physics: a ledge of 0.7 m at 1 m, the friend on it", walker, At(ledge, 0f), At(ledge, 3f, .7f), CaseNear, -1f, blocked, noGround);
            Case(report, "physics: the floor drops 1 m at 1.5 m, the friend below", walker, At(drop, 0f), At(drop, 3f, -1f), CaseNear, -1f, noGround);
            Case(report, "physics: the friend in reach behind a pane", walker, At(pane, 0f), At(pane, .62f), .65f, -1f, blocked);
            Case(report, "physics: the friend in reach over a rail 0.9 m high", walker, At(rail, 0f), At(rail, .62f), .65f, -1f, SurgeWay.Answer.There);
            return true;
        }
        finally
        {
            foreach (var thing in made) Object.DestroyImmediate(thing);
            if (!own.HasValue) Physics.SyncTransforms();
        }
    }

    // A body's arms let hang at its sides, as a friend stands: its file has them out (a T or an A), where an upper arm is
    // further from the body's middle than a standing friend's ever is.
    private static void ArmsDown(Animator body)
    {
        Transform left = body.GetBoneTransform(HumanBodyBones.LeftUpperArm), right = body.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (left == null || right == null) return;
        Vector3 across = Vector3.ProjectOnPlane(right.position - left.position, Vector3.up).normalized;
        foreach (var (upper, lower, outward) in new[] { (left, HumanBodyBones.LeftLowerArm, -across), (right, HumanBodyBones.RightLowerArm, across) })
        {
            var elbow = body.GetBoneTransform(lower);
            if (elbow != null) ArmBones.Turn(upper, elbow.position - upper.position, Vector3.down + outward * .15f);
        }
    }

    // From where a run to a friend ends (the spot on their arm at the motion's near from this shoulder) and from the
    // furthest that needs no run (its reach): the needle's tip must end on the friend's arm before the arm is at full
    // stretch, whichever way the friend stands (facing this body, away from it, either side to it). The friend is
    // brought nearer from afar, as a run brings this body to them, till the spot on them is that far from the shoulder,
    // and no nearer than a run goes (0.3 m, body to body).
    private static void AtRunsEnd(Report report, string who, ArmBones arm, Quaternion facing, Vector3 shoulder, Vector3 ahead,
        Transform friend, Animator theirs, InjectArm.Move dose, in GrenadeArmPath.Key key)
    {
        float[] turns = { 180f, 0f, 90f, 270f };
        foreach (bool edge in new[] { false, true })
        {
            float far = (edge ? dose.Reach : dose.Near) * arm.ArmLength;
            float most = 0f, least = float.MaxValue, worst = 0f, nearest = float.MaxValue, furthest = 0f, spotFar = 0f;
            bool reached = true;
            foreach (float turn in turns)
            {
                bool found = false;
                Vector3 spot = default, inward = default;
                float apart = 3f;
                for (; apart >= .3f; apart -= .01f)
                {
                    friend.SetPositionAndRotation(ahead * apart, Quaternion.Euler(0f, turn, 0f));
                    found = InjectArm.SpotOn(dose, theirs, shoulder, null, null, out spot, out inward);
                    if (!found || Vector3.Distance(spot, shoulder) <= far) break;
                }
                if (!found) continue;
                Quaternion frame = InjectArm.FrameFor(dose, facing, shoulder, true, spot);
                InjectArm.Stand(arm, dose, key, frame, 1f, spot + inward * dose.Depth, out float left, out float reach);
                most = Mathf.Max(most, reach);
                least = Mathf.Min(least, reach);
                worst = Mathf.Max(worst, left);
                nearest = Mathf.Min(nearest, Mathf.Max(.3f, apart));
                furthest = Mathf.Max(furthest, Mathf.Max(.3f, apart));
                spotFar = Mathf.Max(spotFar, Vector3.Distance(spot, shoulder));
                reached &= left >= 0f && left <= TipMissed && reach < ReachSpent;
            }
            string what = $"{who} · " + (edge ? $"at the furthest that needs no run (the spot {dose.Reach:0.##} arm lengths from the shoulder)"
                : $"where a run to a friend ends (the spot {dose.Near:0.##} arm lengths from the shoulder)");
            report.Line($"{what}: the friend facing, turned away or side on, the bodies {nearest:0.00} to {furthest:0.00} m apart, the spot at most {spotFar:0.000} m from the shoulder; "
                + $"the tip ends {worst * 100f:0.0} cm off at worst, the wrist at {least * 100f:0} to {most * 100f:0}% of the arm");
            report.Expect(reached, what, $"the arm does not reach a friend who stands there: {worst * 100f:0.0} cm short, the wrist at {most * 100f:0}% of the arm");
        }
    }
}
#endif
