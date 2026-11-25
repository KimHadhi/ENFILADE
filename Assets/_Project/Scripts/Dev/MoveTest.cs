using System.Collections.Generic;
using System.IO;
using UnityEngine;

// dev-text: file (a test's report for whoever fixes the legs; nothing here is shown in the game)

/// <summary>
/// The legs tested without anyone at the keys (Logs/move-test.txt). Started by the editor on request
/// (MoveTestOnRequest: Temp/enfilade-movetest.request), it waits for this PC's player to stand in the scene, then drives the
/// keys and the mouse itself (ScriptedInput) through the moves the user found the legs worst at: a jog, keys stepped round
/// in a diamond (a key every 0.12 s and every 0.25 s, at a jog and a sprint), A and D in turn (every 0.2 s and 0.5 s), D and
/// W tapped, W then D at a jog and a sprint, and standing, the view flicked 180 degrees either way, 90, 270 and turned slowly
/// round, each move from where the player first stood. For each it writes one line of what the legs did, from the drawn body after this frame's leg IK:
/// how fast a sole on the ground went over it (median and the tenth worst, m/s) and how often faster than 3 m/s, how often
/// a knee swung over 45 degrees or an ankle went over 25 cm in a frame, how often the body's facing turned over 20 degrees in a
/// frame, the most the view stood off the chest (the neck's turn) and the chest off the hips (the spine's), and how far the
/// view and the body's facing turned and how long until the facing was within 30 degrees of the view. Then it ends the play.
/// The user, 2026-11-25: "모든 걸 고쳐. 네가 알아서 테스팅까지 다 해서".
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Body + 1)]
internal sealed class MoveTest : MonoBehaviour
{
    /// <summary>The marker the editor leaves while it plays for a test (MoveTestOnRequest).</summary>
    internal const string Running = "Temp/enfilade-movetest.running";
    private const string Log = "move-test";
    private const string Heading = "/movetest: one line a move. slide = how fast a sole on the ground went over it (median, tenth worst, m/s), "
        + "skates = frames faster than 3 m/s; knees = a knee swung over 45° in a frame; ankles = an ankle went over 25 cm in a frame; "
        + "pops = the body's facing turned over 20° in a frame; neck = the most the view stood off the chest, spine = the chest off the hips (°); "
        + "react = the longest the body took to go within 20° of the keys' new way (s); "
        + "view, body = how far each turned (°, + right); settle = until the facing was within 30° of the view (s).";
    // Ready this long after the player stands, and given up this long after the play began with no player.
    private const float Settle = 1.5f, GiveUp = 60f;

    private sealed class Phase
    {
        public string Name;
        public float Seconds;
        public System.Action<float, float> Drive;
    }

    private sealed class Tally
    {
        public readonly List<float> Slides = new List<float>(), Carried = new List<float>(), Speeds = new List<float>();
        public int Frames, Skates, Knees, Ankles, Pops;
        public float Neck, Spine, View, Body, Settled, React;
    }

    private readonly List<Phase> phases = new List<Phase>();
    private Tally tally;
    private int at = -1;
    private float clock, waited, ready;
    private PlayerMovement player;
    private PlayerBody body;
    private Animator animator;
    private Transform leftShoulder, rightShoulder, leftHip, rightHip;
    private float viewWas, facingWas;
    private readonly Vector3[] soleWas = new Vector3[2], ankleWas = new Vector3[2], soleOffWas = new Vector3[2];
    private readonly int[] endWas = new int[2];
    private readonly float[] kneeWas = new float[2];
    private bool known;
    // Where the player stood as the test began: each move starts there, so none runs into a wall the last one led to.
    private Vector3 home;
    // When the keys last changed in this phase (s), and whether the body has gone their way since.
    private float keysChanged;
    private bool keysMet = true;
    private Vector2 keysWas;

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!File.Exists(Running)) return;
        var host = new GameObject("Move Test");
        DontDestroyOnLoad(host);
        host.AddComponent<MoveTest>();
    }
#endif

    private void Awake()
    {
        var keys = new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
        Add("jog W", 2f, (t, dt) => ScriptedInput.Move = Vector2.up);
        Add("diamond jog", 4f, (t, dt) => ScriptedInput.Move = keys[(int)(t / .12f) % 4]);
        Add("diamond .25", 4f, (t, dt) => ScriptedInput.Move = keys[(int)(t / .25f) % 4]);
        Add("diamond sprint", 3f, (t, dt) => { ScriptedInput.Sprint = true; ScriptedInput.Move = keys[(int)(t / .12f) % 4]; });
        Add("A/D", 3f, (t, dt) => { ScriptedInput.Sprint = false; ScriptedInput.Move = (int)(t / .2f) % 2 == 0 ? Vector2.left : Vector2.right; });
        Add("A/D .5", 3f, (t, dt) => ScriptedInput.Move = (int)(t / .5f) % 2 == 0 ? Vector2.left : Vector2.right);
        Add("tap D", 3f, (t, dt) => ScriptedInput.Move = (int)(t / .4f) % 2 == 0 ? Vector2.right : Vector2.zero);
        Add("tap W", 3f, (t, dt) => ScriptedInput.Move = (int)(t / .4f) % 2 == 0 ? Vector2.up : Vector2.zero);
        Add("W then D", 2.5f, (t, dt) => ScriptedInput.Move = t < 1.2f ? Vector2.up : Vector2.right);
        Add("sprint W then D", 2.5f, (t, dt) => { ScriptedInput.Sprint = true; ScriptedInput.Move = t < 1.5f ? Vector2.up : Vector2.right; });
        Add("stop", 1.5f, (t, dt) => { ScriptedInput.Sprint = false; ScriptedInput.Move = Vector2.zero; });
        Add("flick R180", 2.5f, Flick(180f, .1f));
        Add("flick L180", 2.5f, Flick(-180f, .1f));
        Add("flick R90", 2f, Flick(90f, .1f));
        Add("flick L270", 3f, Flick(-270f, .2f));
        Add("slow R360", 4f, Flick(360f, 3f));
        Add("jog W again", 1.5f, (t, dt) => ScriptedInput.Move = Vector2.up);
    }

    private void Add(string name, float seconds, System.Action<float, float> drive)
        => phases.Add(new Phase { Name = name, Seconds = seconds, Drive = drive });

    // The view turned by `degrees` over `over` seconds from the phase's start.
    private static System.Action<float, float> Flick(float degrees, float over) => (t, dt) =>
    {
        ScriptedInput.Move = Vector2.zero;
        float before = Mathf.Clamp01((t - dt) / over), now = Mathf.Clamp01(t / over);
        if (now > before) ScriptedInput.AddYaw(degrees * (now - before));
    };

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        waited += dt;
        if (at < 0)
        {
            if (!FindPlayer())
            {
                if (waited > GiveUp) Finish("no player came into the scene within a minute");
                return;
            }
            ready += dt;
            if (ready < Settle) return;
            ScriptedInput.Off();
            ScriptedInput.On = true;
            Begin(0);
        }
        var phase = phases[at];
        clock += dt;
        Measure(dt);
        if (clock >= phase.Seconds)
        {
            Write(phase.Name, tally);
            if (at + 1 >= phases.Count) { Finish(null); return; }
            Begin(at + 1);
            phase = phases[at];
        }
        phase.Drive(clock, dt);
        if (ScriptedInput.Move != keysWas)
        {
            keysWas = ScriptedInput.Move;
            keysChanged = clock;
            keysMet = keysWas == Vector2.zero;
        }
    }

    private bool FindPlayer()
    {
        if (player == null)
        {
            player = PlayerRoster.Local;
            if (player == null || !player.TryGetComponent(out body)) { player = null; return false; }
            animator = body.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) { player = null; return false; }
            leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        }
        bool standing = player.IsGrounded && player.CurrentState == PlayerMovement.MovementState.Standing && !LoadingScreen.Visible;
        if (!standing) ready = 0f;
        return standing;
    }

    private void Begin(int index)
    {
        if (index == 0) home = player.transform.position;
        else player.MoveTo(home + Vector3.up * .05f);
        at = index;
        clock = 0f;
        tally = new Tally();
        known = false;
        keysChanged = 0f;
    }

    private void Measure(float dt)
    {
        if (body == null || !body.TryReadLegs(out var left, out var right)) return;
        float view = player.transform.eulerAngles.y, facing = body.FacingYaw;
        var t = tally;
        t.Frames++;
        Vector3 root = animator.transform.position, velocity = player.ActualVelocity;
        t.Speeds.Add(new Vector2(velocity.x, velocity.z).magnitude);
        if (known)
        {
            float viewStep = Mathf.DeltaAngle(viewWas, view), facingStep = Mathf.DeltaAngle(facingWas, facing);
            t.View += viewStep;
            t.Body += facingStep;
            if (Mathf.Abs(facingStep) > 20f) t.Pops++;
        }
        if (Mathf.Abs(Mathf.DeltaAngle(facing, view)) > 30f) t.Settled = clock;
        // How long the body took to go the keys' new way (within 20 degrees).
        if (!keysMet)
        {
            Vector3 keys = player.transform.right * keysWas.x + player.transform.forward * keysWas.y;
            if (velocity.sqrMagnitude > .25f && Vector3.Angle(new Vector3(velocity.x, 0f, velocity.z), new Vector3(keys.x, 0f, keys.z)) <= 20f) keysMet = true;
            t.React = Mathf.Max(t.React, clock - keysChanged);
        }
        // The chest's and the hips' facing from the shoulders and the hip joints, and the view off the chest.
        float chest = Yaw(rightShoulder.position - leftShoulder.position), hips = Yaw(rightHip.position - leftHip.position);
        t.Neck = Mathf.Max(t.Neck, Mathf.Abs(Mathf.DeltaAngle(chest, view)));
        t.Spine = Mathf.Max(t.Spine, Mathf.Abs(Mathf.DeltaAngle(hips, chest)));
        Leg(0, left);
        Leg(1, right);
        viewWas = view;
        facingWas = facing;
        known = true;

        void Leg(int side, FootPlanting.Leg leg)
        {
            Vector3 heel = leg.Foot.TransformPoint(leg.HeelSole), tip = leg.Foot.TransformPoint(leg.TipSole);
            int end = heel.y <= tip.y ? 0 : 1;
            Vector3 sole = end == 0 ? heel : tip;
            bool down = sole.y - leg.Ground <= .02f;
            float knee = Vector3.Angle(leg.Lower.position - leg.Upper.position, leg.Foot.position - leg.Lower.position);
            if (known)
            {
                if (down && endWas[side] == end)
                {
                    float slide = new Vector2(sole.x - soleWas[side].x, sole.z - soleWas[side].z).magnitude / dt;
                    t.Slides.Add(slide);
                    if (slide > 3f) t.Skates++;
                    // How fast the pose carries that sole back under the body: what the body's own pace must match.
                    Vector3 off = sole - root, moved = off - soleOffWas[side];
                    t.Carried.Add(new Vector2(moved.x, moved.z).magnitude / dt);
                }
                if (Mathf.Abs(knee - kneeWas[side]) > 45f) t.Knees++;
                if ((leg.Foot.position - ankleWas[side]).magnitude > .25f) t.Ankles++;
            }
            soleWas[side] = sole;
            soleOffWas[side] = sole - root;
            endWas[side] = down ? end : -1;
            kneeWas[side] = knee;
            ankleWas[side] = leg.Foot.position;
        }
    }

    // The facing of a line drawn from the left to the right (degrees): the way ahead of it.
    private static float Yaw(Vector3 leftToRight)
    {
        Vector3 ahead = Vector3.Cross(leftToRight, Vector3.up);
        return Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
    }

    private static void Write(string name, Tally t)
    {
        t.Slides.Sort();
        float median = t.Slides.Count > 0 ? t.Slides[t.Slides.Count / 2] : 0f;
        float worst = t.Slides.Count > 0 ? t.Slides[Mathf.Min(t.Slides.Count - 1, t.Slides.Count * 9 / 10)] : 0f;
        t.Carried.Sort();
        t.Speeds.Sort();
        float carried = t.Carried.Count > 0 ? t.Carried[t.Carried.Count / 2] : 0f, speed = t.Speeds.Count > 0 ? t.Speeds[t.Speeds.Count / 2] : 0f;
        PlayRecord.Line(Log, Heading, $"{name,-15} n {t.Frames,4} | speed {speed:0.00} pose-carry {carried:0.00} | slide {median:0.00} {worst:0.00} skates {t.Skates,3} | knees {t.Knees,2} ankles {t.Ankles,2} pops {t.Pops,2} "
            + $"| neck {t.Neck:0}° spine {t.Spine:0}° | react {t.React:0.00}s | view {t.View:+0;-0} body {t.Body:+0;-0} settle {t.Settled:0.00}s");
    }

    private void Finish(string trouble)
    {
        if (trouble != null) PlayRecord.Line(Log, Heading, "-- " + trouble);
        else PlayRecord.Line(Log, Heading, "-- done");
        ScriptedInput.Off();
        try { File.Delete(Running); } catch (IOException) { }
        enabled = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void OnDestroy() => ScriptedInput.Off();
}
