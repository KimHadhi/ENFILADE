using UnityEngine;

/// <summary>
/// Whether a surge (GroundSurge) can take a body from where it stands to within a distance of a spot, asked before the
/// run is: a run along the ground passes no wall, no shut door and no drop, and where the way is not clear it is not
/// offered at all (the user, 2026-11-18, on a Détente running to the friend it doses: "물론 충돌 판정이나 벽을 뚫는 그런
/// 방지는 대비를 해야 할 것입니다"). The run itself needs no such care, the capsule's own move stops it; this is so
/// nobody is sent running at a wall, and so what is offered is what can be done.
/// The way is walked in strides along the ground: under each next stride there must be ground a body can stand on, no
/// further up or down from the last than a body steps (so a stair is a way and a ledge is not), and a standing body
/// moved from one to the next must meet nothing. Where it ends, the spot it was run for must be in the open at a height
/// (a hand reaches over a counter, not through a wall or a pane), and about level with it.
/// It asks the world two things and knows nothing else of it (IWorld), so the same walk is checked on worlds made up
/// for it, without playing (the editor's InjectBench: a floor, a wall, a pit, a stair, a pane), and on the game's
/// physics (Walker). Numbers: Tables/Player rows Surge.*.
/// </summary>
public static class SurgeWay
{
    /// <summary>What the walk asks of the world.</summary>
    public interface IWorld
    {
        /// <summary>
        /// The ground a body can stand on at a spot, looked for from <paramref name="rise"/> above it down to
        /// <paramref name="drop"/> below it. False when there is none.
        /// </summary>
        bool Ground(Vector3 at, float rise, float drop, out float height);
        /// <summary>Whether a standing body moved from one spot of ground to another meets anything.</summary>
        bool Blocked(Vector3 from, Vector3 to);
        /// <summary>Whether nothing stands between two points (a line: what a hand reaches along).</summary>
        bool Open(Vector3 from, Vector3 to);
    }

    public enum Answer
    {
        /// <summary>The way is clear: run to Spot.</summary>
        Clear,
        /// <summary>Near enough already: no run is needed.</summary>
        There,
        /// <summary>Further than a surge goes.</summary>
        TooFar,
        /// <summary>Something stands in the way (a wall, a shut door, a body), or between the way's end and the spot.</summary>
        Blocked,
        /// <summary>The ground gives out on the way (a drop, a step too high), or the spot is on another level.</summary>
        NoGround,
    }

    /// <summary>What the walk found: the answer, where the run ends (or got to before it was stopped) and how long it is (m).</summary>
    public readonly struct Plan
    {
        public readonly Answer Answer;
        public readonly Vector3 Spot;
        public readonly float Run;

        public Plan(Answer answer, Vector3 spot, float run)
        {
            Answer = answer;
            Spot = spot;
            Run = run;
        }
    }

    /// <summary>
    /// How the way is walked: how far apart the ground is looked at (m), how far up and down from one stride to the
    /// next a body steps (m), and how far above or below the way's end the spot may be (m).
    /// </summary>
    public struct Rules
    {
        public float Stride, Rise, Drop, Level;

        private static Rules? read;
        private static bool listening;

        /// <summary>The table's (Tables/Player rows Surge.*), kept until the tables are read again.</summary>
        public static Rules Read()
        {
            if (read.HasValue) return read.Value;
            if (!listening)
            {
                listening = true;
                GameTables.Reloaded += () => read = null;
            }
            read = new Rules
            {
                Stride = Mathf.Max(.05f, GameTables.Number(PlayerMovement.TableName, "Surge", "stride", .25f)),
                Rise = Mathf.Max(0f, GameTables.Number(PlayerMovement.TableName, "Surge", "rise", .35f)),
                Drop = Mathf.Max(0f, GameTables.Number(PlayerMovement.TableName, "Surge", "drop", .45f)),
                Level = Mathf.Max(0f, GameTables.Number(PlayerMovement.TableName, "Surge", "level", .6f)),
            };
            return read.Value;
        }
    }

    /// <summary>
    /// The way from <paramref name="feet"/> to within <paramref name="near"/> metres (level) of <paramref name="goal"/>,
    /// which may be no further off than <paramref name="most"/>. With <paramref name="reachHeight"/> above 0, the goal
    /// must be in the open from the way's end at that height over the ground.
    /// </summary>
    public static Plan To(IWorld world, Vector3 feet, Vector3 goal, float near, float most, float reachHeight, in Rules rules)
    {
        Vector3 flat = Vector3.ProjectOnPlane(goal - feet, Vector3.up);
        float apart = flat.magnitude;
        if (apart > most) return new Plan(Answer.TooFar, feet, 0f);
        float run = apart - Mathf.Max(0f, near);
        Vector3 at = feet;
        float done = 0f;
        if (run > 1e-3f)
        {
            Vector3 way = flat / apart;
            int strides = Mathf.CeilToInt(run / rules.Stride);
            for (int stride = 1; stride <= strides; stride++)
            {
                float along = Mathf.Min(run, stride * rules.Stride);
                Vector3 next = feet + way * along;
                next.y = at.y;
                if (!world.Ground(next, rules.Rise, rules.Drop, out float height)) return new Plan(Answer.NoGround, at, done);
                next.y = height;
                if (world.Blocked(at, next)) return new Plan(Answer.Blocked, at, done);
                at = next;
                done = along;
            }
        }
        if (Mathf.Abs(goal.y - at.y) > rules.Level) return new Plan(Answer.NoGround, at, done);
        if (reachHeight > 0f && !world.Open(at + Vector3.up * reachHeight, goal + Vector3.up * reachHeight)) return new Plan(Answer.Blocked, at, done);
        return new Plan(done > 0f ? Answer.Clear : Answer.There, at, done);
    }

    /// <summary>
    /// The game's world as a walking body meets it: its capsule's own numbers, the layers it walks on and into (its own
    /// layer left out, so a friend is no wall), the physics scene it stands in.
    /// </summary>
    public sealed class Walker : IWorld
    {
        public float Radius = .3f, Height = 1.8f, Step = .3f, Slope = 45f;
        public LayerMask Layers = ~0;
        public PhysicsScene Scene;

        /// <summary>Takes a body's numbers as they are now (one walker serves a body asked every frame).</summary>
        public void Read(PlayerMovement body)
        {
            Scene = body.gameObject.scene.GetPhysicsScene();
            Layers = body.groundLayers;
            if (!body.TryGetComponent(out CharacterController controller)) return;
            // The capsule as it is in the world: a body drawn at another scale carries its capsule with it.
            float scale = Mathf.Abs(body.transform.lossyScale.y);
            Radius = controller.radius * scale;
            Height = controller.height * scale;
            Step = controller.stepOffset * scale;
            Slope = controller.slopeLimit;
        }

        public bool Ground(Vector3 at, float rise, float drop, out float height)
        {
            height = at.y;
            if (!Scene.Raycast(at + Vector3.up * rise, Vector3.down, out RaycastHit hit, rise + drop, Layers, QueryTriggerInteraction.Ignore)) return false;
            // Too steep to stand on is no ground (the controller slides off it).
            if (Vector3.Angle(hit.normal, Vector3.up) > Slope) return false;
            height = hit.point.y;
            return true;
        }

        public bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 way = to - from;
            float far = way.magnitude;
            if (far < 1e-4f) return false;
            // The capsule as the body walks: lifted by what it steps over, a hair thinner than it is (ParkourTraversal's
            // look ahead of a vault's surge casts it the same way).
            float radius = Mathf.Max(.01f, Radius - .01f);
            float low = Step + radius, high = Mathf.Max(low, Height - radius);
            return Scene.CapsuleCast(from + Vector3.up * low, from + Vector3.up * high, radius, way / far, out _, far, Layers, QueryTriggerInteraction.Ignore);
        }

        public bool Open(Vector3 from, Vector3 to)
        {
            Vector3 way = to - from;
            float far = way.magnitude;
            return far < 1e-4f || !Scene.Raycast(from, way / far, far, Layers, QueryTriggerInteraction.Ignore);
        }
    }
}
