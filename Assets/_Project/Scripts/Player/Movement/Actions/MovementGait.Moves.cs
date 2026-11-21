using System.Collections.Generic;
using UnityEngine;

// MovementGait: starts and stops. One part of the class; the rest is in MovementGait.cs.
public sealed partial class MovementGait
{
    // Setting off from standing, the legs play their gait's start clip and the body speeds up as that clip's root does;
    // letting go of the keys at speed, they play its stop clip and the body runs on and slows as that clip's does (the
    // user, 2026-11-21, asked how: "걍 자연스럽고 모션 부드럽게 좋은 대로 ㄱㄱ"). The clips' speeds through them are baked
    // from their roots as shares of their gait's cycle (Tables/Player/GaitMoves.csv, GaitMoveBake), so a start or a stop
    // scales to whatever speed the game gives its gait; the legs show the very clip (PlayerBody.GaitMoves) and every PC
    // plays the same one (MoveIndex in PlayerPose). A key pressed again ends a stop at once, a turn of the way off ahead
    // or a key let go ends a start, and anything that leaves the standing gait (a jump, a slide, a dodge) ends either.
    // A gait's row in Gaits.csv turns its starts and stops off (starts, stops: 0).
    private const string MovesTable = "GaitMoves";
    // The clips set off and stop straight ahead: they play only while the way the body goes is this near ahead (degrees).
    private const float AheadWithin = 45f;
    // A stop begins from at least this share of its gait's speed; slower than that the legs just stand.
    private const float StopFrom = .5f;
    // Below this the body stands (m/s), as PlayerBody.Locomotion reads it.
    private const float Still = .3f;

    private sealed class Move
    {
        public string Clip;
        public int Gait;
        public bool Stop;
        public float Length, Phase;
        public float[] Profile;
    }

    private readonly List<Move> moves = new List<Move>();
    private readonly bool[] starts = { true, true, true, true }, stops = { true, true, true, true };
    private Move playing;
    private Vector3 coastWay;
    private float moveTime, coastSpeed, handedPhase;
    private bool wasStill = true, wasPushing;
    private int startCount, handed;

    /// <summary>The start or stop clip the legs play now, null for none.</summary>
    internal string MoveClip => playing?.Clip;
    /// <summary>How far into it (s).</summary>
    internal float MoveTime => moveTime;
    /// <summary>The move as it goes to the other PCs: 0 for none, else its row in GaitMoves.csv counted from 1.</summary>
    internal byte MoveIndex => playing != null ? (byte)(moves.IndexOf(playing) + 1) : (byte)0;
    /// <summary>Counts the starts that ran to their end, and the share of the cycle's stride each ended at: the legs carry
    /// on into the cycle there, in step (PlayerBody).</summary>
    internal int Handed => handed;
    internal float HandedPhase => handedPhase;

    /// <summary>Every start and stop clip the legs may play, for the body to make ready.</summary>
    internal IEnumerable<string> MoveClips
    {
        get { foreach (var move in moves) yield return move.Clip; }
    }

    // A start holds the gait's speed to its clip's root speed at this moment; nothing else does.
    private float StartShare => playing != null && !playing.Stop ? At(playing, moveTime) : 1f;

    /// <summary>
    /// The velocity a stop carries the body at now, false when no stop plays: on along the way it was going, slowing as the
    /// stop clip's root does from the speed the stop began at.
    /// </summary>
    internal bool Coasting(out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (playing == null || !playing.Stop) return false;
        float first = Mathf.Max(.05f, playing.Profile[0]);
        velocity = coastWay * (coastSpeed * At(playing, moveTime) / first);
        return true;
    }

    private void TickMoves(float dt, Vector2 input)
    {
        Vector3 flat = Vector3.ProjectOnPlane(ctx.ActualVelocity, Vector3.up);
        float speed = flat.magnitude;
        bool standing = ctx.CurrentState == PlayerMovement.MovementState.Standing && ctx.Motor.isGrounded;
        bool pushing = ctx.InputAvailable && input.sqrMagnitude > .01f;
        if (playing != null)
        {
            moveTime += dt;
            bool over = !standing || moveTime >= playing.Length
                || (playing.Stop ? pushing : !pushing || AngleOf(input) > AheadWithin);
            if (over)
            {
                if (!playing.Stop && moveTime >= playing.Length) { handed++; handedPhase = playing.Phase; }
                playing = null;
            }
        }
        if (playing == null && standing && moves.Count > 0)
        {
            int gait = GaitNow();
            if (pushing && wasStill && AngleOf(input) <= AheadWithin && starts[gait]) Begin(Pick(gait, false), flat, speed);
            else if (!pushing && wasPushing && AheadOfBody(flat))
            {
                int near = Nearest(speed);
                if (near >= 0 && stops[near] && speed >= rows[near, 0] * StopFrom) Begin(Pick(near, true), flat, speed);
            }
        }
        wasStill = speed < Still;
        wasPushing = pushing;
    }

    private void Begin(Move move, Vector3 flat, float speed)
    {
        if (move == null) return;
        playing = move;
        moveTime = 0f;
        if (move.Stop)
        {
            coastWay = flat.sqrMagnitude > 1e-6f ? flat.normalized : ctx.transform.forward;
            coastSpeed = speed;
        }
        else startCount++;
    }

    /// <summary>A copy of another PC's player: the move its PC reports, timed here from when it began.</summary>
    internal void Mirror(byte index, float dt)
    {
        var next = index > 0 && index <= moves.Count ? moves[index - 1] : null;
        if (next == playing)
        {
            if (playing != null) moveTime += dt;
            return;
        }
        if (playing != null && !playing.Stop && moveTime >= playing.Length - .1f) { handed++; handedPhase = playing.Phase; }
        playing = next;
        moveTime = 0f;
    }

    private void ResetMoves()
    {
        playing = null;
        moveTime = 0f;
        wasStill = true;
        wasPushing = false;
    }

    // The gait the keys ask for now: walk mode's walk or jog, else the run or the sprint.
    private int GaitNow() => ctx.Input.WalkMode ? ctx.Input.SprintButtonHeld ? Jog : Walk : ctx.Input.SprintButtonHeld ? Sprint : Run;

    // The gait whose speed ahead is nearest this one, of those with stops.
    private int Nearest(float speed)
    {
        int best = -1;
        float gap = float.PositiveInfinity;
        for (int g = Walk; g <= Sprint; g++)
        {
            if (!stops[g]) continue;
            float off = Mathf.Abs(rows[g, 0] - speed);
            if (off < gap) { gap = off; best = g; }
        }
        return best;
    }

    // The body goes the way it faces, near enough for a clip that goes straight ahead.
    private bool AheadOfBody(Vector3 flat)
    {
        if (flat.sqrMagnitude < Still * Still) return false;
        Vector3 local = ctx.transform.InverseTransformDirection(flat);
        return Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg) <= AheadWithin;
    }

    // A start takes the left foot and the right in turn; a stop is the one whose first stride is where the legs are now.
    private Move Pick(int gait, bool stop)
    {
        Move best = null;
        float gap = float.PositiveInfinity;
        float now = StridePhase();
        int turn = 0;
        foreach (var move in moves)
        {
            if (move.Gait != gait || move.Stop != stop) continue;
            if (!stop)
            {
                if (turn++ == startCount % 2 || best == null) best = move;
                continue;
            }
            float off = Mathf.Abs(Mathf.Repeat(move.Phase - now + .5f, 1f) - .5f);
            if (off < gap) { gap = off; best = move; }
        }
        return best;
    }

    private float StridePhase()
    {
        if (body == null) body = ctx.GetComponent<PlayerBody>();
        return body != null ? body.Step / 256f : 0f;
    }

    private PlayerBody body;

    // A move's speed share at a moment: its samples joined in between.
    private static float At(Move move, float time)
    {
        var profile = move.Profile;
        if (profile == null || profile.Length == 0) return 1f;
        float at = Mathf.Clamp01(time / Mathf.Max(.01f, move.Length)) * profile.Length - .5f;
        int i = Mathf.Clamp(Mathf.FloorToInt(at), 0, profile.Length - 1), j = Mathf.Min(profile.Length - 1, i + 1);
        return Mathf.Max(0f, Mathf.Lerp(profile[i], profile[j], Mathf.Clamp01(at - i)));
    }

    private void ReadMoves()
    {
        moves.Clear();
        for (int g = Walk; g <= Sprint; g++)
        {
            if (!GameTables.TryGetRow(Table, Rows[g], out var row)) continue;
            starts[g] = row.Number("starts", 1f) > .5f;
            stops[g] = row.Number("stops", 1f) > .5f;
        }
        foreach (string id in GameTables.RowIds(MovesTable))
        {
            if (id.StartsWith("#", System.StringComparison.Ordinal) || !GameTables.TryGetRow(MovesTable, id, out var row)) continue;
            int gait = System.Array.IndexOf(Rows, row.Text("gait"));
            if (gait < Walk || gait > Sprint) continue;
            var samples = new List<float>();
            foreach (string cell in row.Text("profile").Split(';'))
                if (float.TryParse(cell, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                    samples.Add(value);
            moves.Add(new Move
            {
                Clip = id, Gait = gait, Stop = row.Text("kind") == "stop", Length = row.Number("length", 1f),
                Phase = row.Number("phase", 0f), Profile = samples.ToArray()
            });
        }
    }
}
