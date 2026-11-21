using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// Turning round on the run (0.9.295): running at a jog or a sprint with W on, the view swung back further than
/// Move.groundKeepPaceTurn, so the player has turned round to run the other way. The body brakes, plants a foot and turns
/// round on BIPER Female Runner's turn (Jog_TurnL/R, Sprint_TurnL/R), and goes where that clip's root goes, its turn
/// stretched to the view's, so the feet stay where they come down and the body ends facing the view. Until then the
/// velocity blended through a stop and the legs ran backwards a moment under the turned view before running ahead (the
/// user, 2026-11-21: "뒤로 돌 때는 turn 애니메이션 있는 게 좋을 듯요 ... 앞으로 가려다 뒤로 가는 모션이라"; for the
/// mouse only: "마우스로 뒤돌 때"). S held is a run backwards, not a turn: the body keeps facing the view.
/// The clips' root paths and turns are baked by ClipBench (Tables/Player/GaitMoves.csv kind turn); PlayerBody plays the
/// clip on the same clock and faces the body so its own turn, stretched, ends on the view. Another PC is told by the pose
/// (MovementMirror) and only plays it; its place comes with the pose.
/// </summary>
public sealed class MovementTurnBack
{
    private readonly PlayerMovement ctx;
    // The turn's clip baked: where its root is (m, in the frame it began facing, ahead +z) and how far it has turned (deg,
    // + to the right) at evenly spaced moments; its length (s) and whole turn (deg).
    private Vector2[] path;
    private float[] turned;
    private float length, clipTurn, elapsed, plantTime;
    // How the body ran into the turn: the turn eases from it to the clip's own pace by the plant.
    private Vector3 entry;

    /// <summary>The clip of the turn under way, or null.</summary>
    public string Clip { get; private set; }
    public bool Active => Clip != null;
    /// <summary>The way the body ran as the turn began (world yaw, deg).</summary>
    public float FromYaw { get; private set; }
    /// <summary>How far round the turn goes (deg, + to the right): the view's.</summary>
    public float Turn { get; private set; }
    /// <summary>When the turn began (Time.time): the body's clip runs from here on every PC.</summary>
    public float StartedAt { get; private set; }
    /// <summary>The view's turn over the clip's own: the clip's turn is stretched by this.</summary>
    public float Stretch => Mathf.Abs(clipTurn) > 1f ? Turn / clipTurn : 1f;
    /// <summary>Which clip, for the pose: 1 jog left, 2 jog right, 3 sprint left, 4 sprint right; 0 none.</summary>
    public int Code { get; private set; }

    internal MovementTurnBack(PlayerMovement context) => ctx = context;

    /// <summary>How far round the clip itself has turned this long into it (deg).</summary>
    public float ClipTurnAt(float time) => Sample(turned, time);

    /// <summary>
    /// Begins a turn when this is one: the body runs (<paramref name="velocity"/>) at a jog or faster and the keys, pointing
    /// ahead of the view, want it the other way (<paramref name="wanted"/>). False, and nothing begun, otherwise.
    /// </summary>
    internal bool TryBegin(Vector3 velocity, Vector3 wanted, Vector2 input)
    {
        var settings = ctx.AbilitySettings;
        if (Active || ctx.CurrentState != State.Standing || !ctx.IsGrounded) return false;
        if (input.y <= .1f || Mathf.Abs(Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg) > KeysAhead) return false;
        Vector3 from = Vector3.ProjectOnPlane(velocity, Vector3.up), to = Vector3.ProjectOnPlane(wanted, Vector3.up);
        if (from.sqrMagnitude < .01f || to.sqrMagnitude < .01f) return false;
        float turn = Vector3.SignedAngle(from, to, Vector3.up);
        if (Mathf.Abs(turn) <= ctx.GroundKeepPaceTurn) return false;
        float gait = ctx.Actions.Gait.Position;
        if (gait < settings.turnBackFromGait) return false;
        bool sprint = gait >= 1.5f, right = turn > 0f;
        int code = (sprint ? 3 : 1) + (right ? 1 : 0);
        if (!Load(ClipFor(code))) return false;
        Clip = ClipFor(code);
        Code = code;
        FromYaw = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg;
        Turn = turn;
        StartedAt = Time.time;
        elapsed = 0f;
        entry = from;
        return true;
    }

    /// <summary>
    /// A frame of the turn: the body's velocity along the clip's root (world), true while it goes on. It ends with its clip,
    /// or at once off the ground or in any move but standing (a jump, a slide), handing over to them as they are.
    /// </summary>
    internal bool Advance(float dt, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!Active) return false;
        if (!ctx.IsGrounded || ctx.CurrentState != State.Standing || dt <= 0f)
        {
            Stop();
            return false;
        }
        float before = elapsed;
        elapsed = Mathf.Min(length, elapsed + dt);
        // The clip's root moved this much in the frame it began facing; in its own frame that is its step, which goes the
        // way the body faces now: the clip's turn stretched to the view's.
        Vector2 step = Sample(path, elapsed) - Sample(path, before);
        float clipYaw = (Sample(turned, before) + Sample(turned, elapsed)) * .5f;
        Vector2 own = Rotate(step, -clipYaw);
        Vector2 world = Rotate(own, FromYaw + clipYaw * Stretch);
        velocity = new Vector3(world.x, 0f, world.y) / dt;
        // The clips run into the turn slower than the game's gaits (the jog's 1.6 m/s to the game's 3.0, the sprint's
        // 0.4-3.8 to its 6.0: ClipBench, kind turn), so the body eases from the pace it ran in at to the clip's own by the
        // plant, where the clip turns on its foot; from there the feet and the ground go together.
        velocity = Vector3.Lerp(entry, velocity, Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(.01f, plantTime)));
        if (elapsed >= length) Stop();
        return true;
    }

    internal void Stop()
    {
        Clip = null;
        Code = 0;
    }

    /// <summary>Another PC's turn as its pose tells it: the clip by its code, the way it began, how far it goes.</summary>
    internal void Mirror(int code, float fromYaw, float turn)
    {
        if (code == Code) return;
        if (code <= 0 || !Load(ClipFor(code)))
        {
            Stop();
            return;
        }
        Clip = ClipFor(code);
        Code = code;
        FromYaw = fromYaw;
        Turn = turn;
        StartedAt = Time.time;
        elapsed = 0f;
    }

    private string ClipFor(int code)
    {
        var settings = ctx.AbilitySettings;
        switch (code)
        {
            case 1: return settings.turnBackJogLeft;
            case 2: return settings.turnBackJogRight;
            case 3: return settings.turnBackSprintLeft;
            case 4: return settings.turnBackSprintRight;
            default: return null;
        }
    }

    // A turn's clip as ClipBench baked it (kind turn): each sample "x:z:turn", its length and its whole turn.
    private bool Load(string id)
    {
        if (string.IsNullOrEmpty(id) || !GameTables.TryGetRow("GaitMoves", id, out var row) || row.Text("kind") != "turn") return false;
        var points = new List<Vector2>();
        var turns = new List<float>();
        foreach (string sample in row.Text("profile").Split(';'))
        {
            string[] parts = sample.Split(':');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw)) return false;
            points.Add(new Vector2(x, z));
            turns.Add(yaw);
        }
        length = row.Number("length", 0f);
        clipTurn = row.Number("top", 0f);
        if (points.Count < 2 || length <= 0f) return false;
        path = points.ToArray();
        turned = turns.ToArray();
        // The plant: the slowest moment of its root between a sixth and three quarters of the clip, where it turns on its foot.
        float step = length / (points.Count - 1), slowest = float.MaxValue;
        plantTime = length * .5f;
        for (int i = Mathf.Max(1, points.Count / 6); i < points.Count * 3 / 4; i++)
        {
            float speed = (points[i] - points[i - 1]).magnitude / step;
            if (speed < slowest) { slowest = speed; plantTime = i * step; }
        }
        return true;
    }

    private Vector2 Sample(Vector2[] samples, float time)
    {
        if (samples == null || samples.Length < 2 || length <= 0f) return Vector2.zero;
        float at = Mathf.Clamp01(time / length) * (samples.Length - 1);
        int low = Mathf.Min(samples.Length - 2, Mathf.FloorToInt(at));
        return Vector2.Lerp(samples[low], samples[low + 1], at - low);
    }

    private float Sample(float[] samples, float time)
    {
        if (samples == null || samples.Length < 2 || length <= 0f) return 0f;
        float at = Mathf.Clamp01(time / length) * (samples.Length - 1);
        int low = Mathf.Min(samples.Length - 2, Mathf.FloorToInt(at));
        return Mathf.Lerp(samples[low], samples[low + 1], at - low);
    }

    // A ground vector (x right, y ahead) turned by a yaw (deg, + to the right).
    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
    }

    // The keys must point within this of ahead (deg): W, or W with A or D.
    private const float KeysAhead = 50f;
}
