using System.Collections.Generic;
using System.IO;
using UnityEngine;

// dev-text: file (a test's report for whoever fixes the legs; nothing here is shown in the game)

/// <summary>
/// The legs tested without anyone at the keys (Logs/move-test.txt). Started by the editor on request
/// (MoveTestOnRequest: Temp/enfilade-movetest.request), it waits for this PC's player to stand in the scene, then drives the
/// keys and the mouse itself (ScriptedInput) through the moves the user found the legs worst at: a jog, keys stepped round
/// in a diamond (a key every 0.12 s and every 0.25 s, at a jog and a sprint), A and D in turn (every 0.2 s and 0.5 s), D and
/// W tapped, W then D at a jog and a sprint, the view flicked round on the move (180 and 90 at a jog, 180 sprinting and
/// walking), and standing, the view flicked 180 degrees either way, 90, 270 and turned slowly round; each move from where the
/// player first stood, a move with keys from a stop and facing the way it first faced. For each it writes one line of what the legs did, from the drawn body after this frame's leg IK:
/// how fast a sole on the ground went over it (median and the tenth worst, m/s) and how often faster than 3 m/s, how often
/// a knee swung over 45 degrees or an ankle went over 25 cm in a frame, how often the body's facing turned over 20 degrees in a
/// frame, the most the view stood off the chest (the neck's turn) and the chest off the hips (the spine's), and how far the
/// view and the body's facing turned and how long until the facing was within 30 degrees of the view. Last it jogs at the
/// Hub's wall a climb went up through in a frame or two (climb), jumps and holds W, and writes how long the climb took and
/// the fastest the capsule went in it (Logs/climbs.txt has the climb's own lines); then it puts a ladder up that wall for the
/// move (ladder), climbs it with W and off its top, and writes the same. Then it ends the play. A slide (slide: a sprint,
/// then the crouch key, W held on; slide sprint: Shift held through it too; slide let go: the keys let go as it slides; slide
/// crouched: the crouch key held through it) writes how fast the eyes dropped against the
/// capsule and the biggest change of their pace from one frame to the next (eye drop, jolt), and from its getting up the
/// biggest change of the body's speed from one frame to the next (end jump); Logs/slide-trace.txt has it a frame at a time.
/// After the climb it jogs at boxes it puts up ahead (vault over, vault onto, vault over high) and walks at two (clamber low,
/// clamber high), and writes how long each took
/// and how a hand on the box's top went over it (hands on top: how long, how fast it slid, middle and most, and how far);
/// Logs/vault-trace.txt has both hands against the top a frame at a time. The moves the request's film= line names are
/// pictured (MoveFilm).
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
        // It starts facing the way the player first faced (a move with keys), not where the last left the view (a flick).
        public bool FaceHome;
        public System.Action<float, float> Drive;
        // Stands the player where the move starts instead of where the test began; false when there is no such place.
        public System.Func<bool> Place;
    }

    private sealed class Tally
    {
        public readonly List<float> Slides = new List<float>(), Carried = new List<float>(), Speeds = new List<float>(), OffClip = new List<float>();
        public int All, Frames, Skates, Knees, Ankles, Pops, RunTurns, StartsFrom = -1, Starts, Dances, HeldBack;
        public float Slowest = float.MaxValue;
        public float Neck, Spine, View, Body, Settled, React;
        // In a vault or a climb: how long, and the fastest the capsule went (m/s); hanging from a ledge: how long.
        public float ClimbTime, ClimbFastest, HangTime, LadderTime;
        // Over a box (the vaults): how long a hand was on its top and how far and fast it went over the top meanwhile.
        public readonly List<float> TopSlides = new List<float>();
        public float OnTop, TopPath;
        // The fastest the eyes went down against the capsule, and the most their pace against it changed in a frame (m/s).
        public float EyeDrop, EyeJolt;
        // Out of a slide's getting up, the most the body's speed changed from one frame to the next (m/s); -1 no slide.
        public float EndJump = -1f;
    }

    private readonly List<Phase> phases = new List<Phase>();
    private Tally tally;
    private int at = -1;
    private float clock, waited, ready;
    private PlayerMovement player;
    private PlayerBody body;
    private Animator animator;
    private Transform leftShoulder, rightShoulder, leftHip, rightHip, leftHand, rightHand;
    private float viewWas, facingWas;
    private readonly Vector3[] soleWas = new Vector3[2], ankleWas = new Vector3[2], soleOffWas = new Vector3[2];
    private readonly int[] endWas = new int[2];
    private readonly float[] kneeWas = new float[2];
    private bool known;
    // Where the player stood and faced as the test began: each move starts there, so none runs into a wall the last one led to.
    private Vector3 home;
    private float homeYaw;
    // When the keys last changed in this phase (s), and whether the body has gone their way since.
    private float keysChanged;
    private bool keysMet = true;
    private Vector2 keysWas;
    // The eyes against the capsule last frame and how fast they went then (EyeDrop, EyeJolt).
    private Vector3 eyeWas, eyePaceWas;
    private int eyeFrames;
    private MoveFilm film;

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
        Add("diamond .18", 4f, (t, dt) => ScriptedInput.Move = keys[(int)(t / .18f) % 4]);
        Add("diamond .25", 4f, (t, dt) => ScriptedInput.Move = keys[(int)(t / .25f) % 4]);
        // Out of keys swept round into a jog ahead: the legs from standing to the stride (the user, 2026-11-26: "모션으로 나갈 때").
        Add("diamond to W", 3.2f, (t, dt) => ScriptedInput.Move = t < 1.6f ? keys[(int)(t / .18f) % 4] : Vector2.up);
        Add("diamond sprint", 3f, (t, dt) => { ScriptedInput.Sprint = true; ScriptedInput.Move = keys[(int)(t / .12f) % 4]; });
        Add("A/D", 3f, (t, dt) => { ScriptedInput.Sprint = false; ScriptedInput.Move = (int)(t / .2f) % 2 == 0 ? Vector2.left : Vector2.right; });
        Add("A/D .5", 3f, (t, dt) => ScriptedInput.Move = (int)(t / .5f) % 2 == 0 ? Vector2.left : Vector2.right);
        Add("tap D", 3f, (t, dt) => ScriptedInput.Move = (int)(t / .4f) % 2 == 0 ? Vector2.right : Vector2.zero);
        Add("tap W", 3f, (t, dt) => ScriptedInput.Move = (int)(t / .4f) % 2 == 0 ? Vector2.up : Vector2.zero);
        Add("W then D", 2.5f, (t, dt) => ScriptedInput.Move = t < 1.2f ? Vector2.up : Vector2.right);
        Add("sprint W then D", 2.5f, (t, dt) => { ScriptedInput.Sprint = true; ScriptedInput.Move = t < 1.5f ? Vector2.up : Vector2.right; });
        Add("jog flick R180", 3f, OnTheRun(false, 180f, .8f, .15f));
        Add("sprint flick L180", 3.5f, OnTheRun(true, -180f, 1.2f, .15f));
        Add("jog flick R90", 2.5f, OnTheRun(false, 90f, .8f, .1f));
        Add("walk flick L180", 3f, OnTheRun(false, -180f, .8f, .15f, .5f));
        Add("stop", 1.5f, (t, dt) => { ScriptedInput.Sprint = false; ScriptedInput.Move = Vector2.zero; });
        Add("flick R180", 2.5f, Flick(180f, .1f), false);
        Add("flick L180", 2.5f, Flick(-180f, .1f), false);
        Add("flick R90", 2f, Flick(90f, .1f), false);
        Add("flick L270", 3f, Flick(-270f, .2f), false);
        Add("slow R360", 4f, Flick(360f, 3f), false);
        Add("jog W again", 1.5f, (t, dt) => ScriptedInput.Move = Vector2.up);
        // Up out of the slide into a jog (W) and into a sprint (Shift held through it): the moment it gets up and the walk or
        // run takes over (the user, 2026-11-26: "슬라이딩 끝날 때 모션이 갑자기 바뀌어서 불편").
        Add("slide", 5.6f, Slide(false));
        Add("slide sprint", 5.6f, Slide(true));
        // The other ends: the keys let go as it slides (it slows to a stop and gets up), and the crouch key held through it.
        Add("slide let go", 6f, Slide(false, false));
        Add("slide crouched", 5.6f, Slide(false, true, true));
        phases.Add(new Phase { Name = "climb", Seconds = 3f, Drive = Climb, Place = PlaceAtWall });
        // Jogged at a box and jumped: a rail gone over, a deep one climbed onto, a wall too high for the rail's vault and thin
        // enough to land behind. How long each took, and whether the hand on the top stays where it went down or slides over it
        // (the user, 2026-11-26: "손 짚기 해결법은 그게 더 자연스러운 거면"; Logs/vault-trace.txt).
        Vault("vault over", 2f, 1f, .3f, .8f);
        Vault("vault onto", 2f, 1f, 6f, ClimbJumpAt);
        Vault("vault over high", 2f, 1.7f, .3f, ClimbJumpAt);
        // Walked at (W half way, under Ability.clamberMaxSpeed) and jumped close: clambered onto, at the hips and at the head.
        Vault("clamber low", 2f, 1f, 6f, ClimbJumpAt, .5f, 5.5f);
        Vault("clamber high", 2f, 1.9f, 6f, ClimbJumpAt, .5f, 6.5f);
        phases.Add(new Phase { Name = "ladder", Seconds = 8f, Drive = (t, dt) => ScriptedInput.Move = t < WallSettle ? Vector2.zero : Vector2.up,
            Place = PutUpLadder });
        // Only the moves the request named (only=climb,jog W; MoveTestOnRequest carries the request on in the marker).
        var only = Only();
        if (only.Count > 0) phases.RemoveAll(phase => !only.Contains(phase.Name));
        film = MoveFilm.From(Running);
    }

    // The slide: a sprint this long, then the crouch key down this long (s); traced from its start to SlideAfter after it ends.
    private const float SlideAt = 1.4f, SlideHeld = .2f, SlideAfter = .8f;
    private const string SlideTraceHeading = "slide trace (MoveTest): from the slide's start to 0.8 s after it ends, a line a frame: "
        + "the move, time, where in its clip (SlideMove phase, clip, moment s) or after it the state and the legs' gait (1 jog, 2 sprint), "
        + "the capsule's speed, the eyes over the capsule's floor";
    // When the slide began and ended in this move (s, -1 not yet), and the capsule's ground speed last frame (EndJump).
    private float slidFrom = -1f, slidTo = -1f, paceWas = -1f;

    // A sprint, then the crouch key down from SlideAt, for SlideHeld or on to the end when `crouched`; W held on, or let go with
    // the crouch key unless `keys` (let go with it, the crouch key alone was a crouch, no slide); Shift held through the slide
    // too when `sprint`.
    private static System.Action<float, float> Slide(bool sprint, bool keys = true, bool crouched = false) => (t, dt) =>
    {
        ScriptedInput.Sprint = sprint || t < SlideAt;
        ScriptedInput.Move = keys || t < SlideAt + SlideHeld ? Vector2.up : Vector2.zero;
        if (t >= SlideAt && t - dt < SlideAt) ScriptedInput.PressCrouch();
        else if (t >= SlideAt + SlideHeld && !crouched) ScriptedInput.Crouch = false;
    };

    private static HashSet<string> Only()
    {
        var names = new HashSet<string>();
        try
        {
            foreach (string line in File.ReadAllLines(Running))
                if (line.StartsWith("only=", System.StringComparison.OrdinalIgnoreCase))
                    foreach (string name in line.Substring(5).Split(','))
                        if (name.Trim().Length > 0) names.Add(name.Trim());
        }
        catch (IOException) { }
        return names;
    }

    private void Add(string name, float seconds, System.Action<float, float> drive, bool faceHome = true)
        => phases.Add(new Phase { Name = name, Seconds = seconds, Drive = drive, FaceHome = faceHome });

    // The wall a climb went up through in a frame or two (the ledge record of 2026-11-26 10:43: the Hub's Stairs, the top's
    // edge at (7.28, 2.85, -16.21), 2.19 m over the feet of a jump; the user: "벽 위로 mantling 할 때 텔포하는데"). The
    // player stands ClimbBack out from its face, jogs at it with W and jumps ClimbJumpAt from it, as that climb went.
    private static readonly Vector3 ClimbEdge = new Vector3(7.28f, 2.85f, -16.21f);
    // Jumped from 0.9 m the ledge came within reach only on the way down, too fast for the climb (VaultFinder.OntoHigh), and
    // the test hung from it instead; the user's jump caught it 0.09 s after leaving the ground (the gait record of 10:43).
    private const float ClimbBack = 2.5f, ClimbJumpAt = .5f, ClimbJumpHeld = .4f;
    // Both wall moves stand this long first (s): set going at once after the move before, the legs still came round from it
    // and the run reached the wall late, and the ledge was caught falling and hung from (the full test of 2026-11-26).
    private const float WallSettle = .4f;
    private Vector3 wallFace, wallNormal, climbWas;
    private bool climbJumped, climbKnown;
    private float climbJumpedAt;

    // The same wall with a ladder up it to the top (Ladder: at the foot, in the plane of its rungs, Z out at the climber), its
    // rungs LadderOff out from the face as a ladder's stand (flush, the climber held Ability.ladderStandOff off them was in the
    // wall and never took it), the player standing LadderBack out from them, facing it: W climbs it and off its top
    // (LadderState, WarpedTraversal on the climb off's hands). The Hub has no ladder of its own; the move's one is taken down
    // as the move ends.
    private const float LadderOff = .12f, LadderBack = .6f;
    private Ladder ladder;

    private bool PutUpLadder()
    {
        if (!FindWall(out Vector3 face, out Vector3 normal, out float floor)) return false;
        var post = new GameObject("Move Test Ladder");
        Vector3 foot = new Vector3(face.x, floor, face.z) + normal * LadderOff;
        post.transform.SetPositionAndRotation(foot, Quaternion.LookRotation(normal, Vector3.up));
        ladder = post.AddComponent<Ladder>();
        // Up to the top's height (its rails end flush with the floor up there), the rest of its look as it comes.
        typeof(Ladder).GetField("height", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(ladder, ClimbEdge.y - floor);
        player.MoveTo(foot + normal * LadderBack + Vector3.up * .05f);
        player.HorizontalVelocity = Vector3.zero;
        if (player.TryGetComponent(out PlayerLook look)) look.FaceYaw(Mathf.Atan2(-normal.x, -normal.z) * Mathf.Rad2Deg);
        return true;
    }

    private void TakeDownLadder()
    {
        if (ladder != null) Destroy(ladder.gameObject);
        ladder = null;
        if (box != null) Destroy(box);
        box = null;
    }

    // A box BoxAhead ahead of where the test began, as wide, high and deep as asked (m), on the floor's own layer, so the moves
    // see it as they see the floor: the player jogs at it with W from a stop and jumps `jumpAt` from its near face (further
    // off, a deep box was jumped onto with no move: the onto move looks down from the eyes, and over the top they were too
    // high by then). Taken down with the ladder as the move ends.
    private const float BoxAhead = 3f, VaultJumpHeld = .2f;
    private GameObject box;
    private float boxJumpAt, boxKeys;

    // `keys`: how far W is held (1 a jog, .5 a walk); `seconds`: how long the move runs.
    private void Vault(string name, float width, float height, float depth, float jumpAt, float keys = 1f, float seconds = 3f)
        => phases.Add(new Phase { Name = name, Seconds = seconds, FaceHome = true, Drive = VaultDrive, Place = () =>
        {
            Vector3 ahead = Quaternion.Euler(0f, homeYaw, 0f) * Vector3.forward;
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Move Test Box";
            if (Physics.Raycast(home + Vector3.up * .5f, Vector3.down, out RaycastHit floor, 2f, ~0, QueryTriggerInteraction.Ignore))
                box.layer = floor.collider.gameObject.layer;
            box.transform.SetPositionAndRotation(home + ahead * (BoxAhead + depth * .5f) + Vector3.up * (height * .5f), Quaternion.Euler(0f, homeYaw, 0f));
            box.transform.localScale = new Vector3(width, height, depth);
            boxJumpAt = jumpAt;
            boxKeys = keys;
            climbJumped = false;
            return true;
        } });

    private void VaultDrive(float t, float dt)
    {
        ScriptedInput.Sprint = false;
        ScriptedInput.Move = Vector2.up * boxKeys;
        if (box == null) return;
        Vector3 local = box.transform.InverseTransformPoint(player.transform.position);
        float toFace = (-.5f - local.z) * box.transform.localScale.z;
        if (!climbJumped && toFace <= boxJumpAt)
        {
            climbJumped = true;
            climbJumpedAt = t;
            ScriptedInput.PressJump();
        }
        if (climbJumped && t - climbJumpedAt > VaultJumpHeld) ScriptedInput.Jump = false;
    }

    // A hand on the box's top (its wrist within a hand's height over it, over the top), and how far it went over the top since
    // the frame before.
    private bool HandOnTop(Transform hand, int side, float dt)
    {
        if (box == null || hand == null || tally == null) return false;
        Vector3 local = box.transform.InverseTransformPoint(hand.position), size = box.transform.localScale;
        float over = hand.position.y - (box.transform.position.y + size.y * .5f);
        bool on = Mathf.Abs(local.x) <= .5f && Mathf.Abs(local.z) <= .5f + .15f / size.z && over >= -.05f && over <= .15f;
        if (on && handWasOn[side])
        {
            float moved = new Vector2(hand.position.x - handWas[side].x, hand.position.z - handWas[side].z).magnitude;
            tally.TopSlides.Add(moved / dt);
            tally.TopPath += moved;
        }
        handWasOn[side] = on;
        handWas[side] = hand.position;
        return on;
    }
    private readonly bool[] handWasOn = new bool[2];
    private readonly Vector3[] handWas = new Vector3[2];

    // A vault over a box a line a frame (Logs/vault-trace.txt), from the move's start to VaultAfter after it: the time into it
    // (its clip plays from there at its own pace), the state, and each hand's wrist against the box's top: how high over it,
    // how far past its near face, and how fast it went over the ground (cm, cm, m/s). A hand down on the top keeps its place
    // while the body goes over it.
    private const float VaultAfter = .3f;
    private const string VaultTraceHeading = "vault trace (MoveTest): a vault over a box a line a frame from its start to 0.3 s after: "
        + "the move, time into it (its clip plays from there at its own pace), the state, then each hand's wrist: over the top (cm), "
        + "past the near face (cm) and its speed over the ground (m/s)";
    private float vaultFrom = -1f, vaultTo = -1f;
    private readonly Vector3[] wristWas = new Vector3[2];

    private void TraceVault()
    {
        if (box == null || leftHand == null || rightHand == null) return;
        bool moving = player.CurrentState == PlayerMovement.MovementState.Vault || player.CurrentState == PlayerMovement.MovementState.Mantle;
        if (moving && vaultFrom < 0f) vaultFrom = clock;
        if (moving) vaultTo = -1f;
        else if (vaultFrom >= 0f && vaultTo < 0f) vaultTo = clock;
        float dt = Time.deltaTime;
        if (vaultFrom >= 0f && (moving || clock - vaultTo <= VaultAfter) && dt > 0f)
        {
            Vector3 size = box.transform.localScale;
            float top = box.transform.position.y + size.y * .5f;
            string Wrist(Transform hand, int side)
            {
                Vector3 local = box.transform.InverseTransformPoint(hand.position);
                float speed = new Vector2(hand.position.x - wristWas[side].x, hand.position.z - wristWas[side].z).magnitude / dt;
                return $"{(hand.position.y - top) * 100f,5:0} {(local.z + .5f) * size.z * 100f,5:0} {speed,5:0.00}";
            }
            PlayRecord.Line("vault-trace", VaultTraceHeading, $"{phases[at].Name} {clock - vaultFrom:0.000}s {player.CurrentState,-8} "
                + $"L {Wrist(leftHand, 0)} | R {Wrist(rightHand, 1)}");
        }
        wristWas[0] = leftHand.position;
        wristWas[1] = rightHand.position;
    }

    // The wall's face under ClimbEdge, which way it faces and the floor out in front of it, a climb's height under the top.
    private static bool FindWall(out Vector3 face, out Vector3 normal, out float floor)
    {
        face = normal = Vector3.zero;
        floor = 0f;
        for (int i = 0; i < 16; i++)
        {
            Vector3 way = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward, from = ClimbEdge + way * 1.5f + Vector3.down * .5f;
            if (Physics.CheckSphere(from, .05f, ~0, QueryTriggerInteraction.Ignore)
                || !Physics.Raycast(from, -way, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore)) continue;
            Vector3 flat = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
            if (flat.sqrMagnitude < .8f || Vector3.Dot(flat.normalized, way) < .7f
                || Vector3.ProjectOnPlane(hit.point - ClimbEdge, Vector3.up).magnitude > .6f) continue;
            flat.Normalize();
            if (!Physics.Raycast(hit.point + flat * .3f + Vector3.up, Vector3.down, out RaycastHit ground, 4f, ~0, QueryTriggerInteraction.Ignore)
                || ClimbEdge.y - ground.point.y < 2f) continue;
            face = hit.point;
            normal = flat;
            floor = ground.point.y;
            return true;
        }
        return false;
    }

    private bool PlaceAtWall()
    {
        if (!FindWall(out Vector3 face, out Vector3 normal, out _)
            || !Physics.Raycast(face + normal * ClimbBack + Vector3.up, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        wallFace = face;
        wallNormal = normal;
        player.MoveTo(floor.point + Vector3.up * .05f);
        player.HorizontalVelocity = Vector3.zero;
        if (player.TryGetComponent(out PlayerLook look)) look.FaceYaw(Mathf.Atan2(-normal.x, -normal.z) * Mathf.Rad2Deg);
        climbJumped = climbKnown = false;
        return true;
    }

    private void Climb(float t, float dt)
    {
        ScriptedInput.Sprint = false;
        ScriptedInput.Move = t < WallSettle ? Vector2.zero : Vector2.up;
        if (t < WallSettle) return;
        if (!climbJumped && Vector3.Dot(player.transform.position - wallFace, wallNormal) <= ClimbJumpAt)
        {
            climbJumped = true;
            climbJumpedAt = t;
            ScriptedInput.PressJump();
        }
        if (climbJumped && t - climbJumpedAt > ClimbJumpHeld) ScriptedInput.Jump = false;
    }

    // W held (half way for a walk, with the sprint key or not) and the view turned by `degrees` over `over` seconds from `at`.
    private static System.Action<float, float> OnTheRun(bool sprint, float degrees, float at, float over, float keys = 1f) => (t, dt) =>
    {
        ScriptedInput.Sprint = sprint;
        ScriptedInput.Move = Vector2.up * keys;
        float before = Mathf.Clamp01((t - dt - at) / over), now = Mathf.Clamp01((t - at) / over);
        if (now > before) ScriptedInput.AddYaw(degrees * (now - before));
    };

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
            if (phases.Count == 0)
            {
                Finish("the request named no move this test has");
                return;
            }
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
        film?.Shoot(player.transform, clock);
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
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        bool standing = player.IsGrounded && player.CurrentState == PlayerMovement.MovementState.Standing && !LoadingScreen.Visible;
        if (!standing) ready = 0f;
        return standing;
    }

    private void Begin(int index)
    {
        if (index == 0)
        {
            home = player.transform.position;
            homeYaw = player.transform.eulerAngles.y;
        }
        else
        {
            player.MoveTo(home + Vector3.up * .05f);
            player.HorizontalVelocity = Vector3.zero;
            if (phases[index].FaceHome && player.TryGetComponent(out PlayerLook look)) look.FaceYaw(homeYaw);
        }
        TakeDownLadder();
        // A crouch key a move held on (slide crouched) is let go before the next.
        ScriptedInput.Crouch = false;
        if (phases[index].Place != null && !phases[index].Place())
            PlayRecord.Line(Log, Heading, $"-- {phases[index].Name}: no place for it in this scene, run from where the test began");
        at = index;
        clock = 0f;
        climbKnown = false;
        handWasOn[0] = handWasOn[1] = false;
        vaultFrom = vaultTo = -1f;
        eyeFrames = 0;
        slidFrom = slidTo = paceWas = -1f;
        film?.Begin(phases[index].Name, player.transform);
        tally = new Tally();
        known = false;
        keysChanged = 0f;
    }

    private void Measure(float dt)
    {
        if (tally != null) tally.All++;
        // A vault or a climb: how long it took and the fastest the capsule went in it.
        Vector3 at = player.transform.position;
        if (tally != null && (player.CurrentState == PlayerMovement.MovementState.Mantle || player.CurrentState == PlayerMovement.MovementState.Vault))
        {
            tally.ClimbTime += dt;
            if (climbKnown) tally.ClimbFastest = Mathf.Max(tally.ClimbFastest, (at - climbWas).magnitude / dt);
        }
        if (tally != null && player.CurrentState == PlayerMovement.MovementState.LedgeHang) tally.HangTime += dt;
        if (tally != null && player.CurrentState == PlayerMovement.MovementState.Ladder) tally.LadderTime += dt;
        bool leftOn = HandOnTop(leftHand, 0, dt), rightOn = HandOnTop(rightHand, 1, dt);
        if (tally != null && (leftOn || rightOn)) tally.OnTop += dt;
        TraceVault();
        // On the ground the body going under 60% of the speed the keys ask (over 1 m/s): the legs holding it back.
        Vector3 asked = Vector3.ProjectOnPlane(player.Stride, Vector3.up), went = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
        if (tally != null && player.IsGrounded && player.StrideFrame == Time.frameCount && asked.magnitude > 1f
            && went.magnitude < .6f * asked.magnitude) tally.HeldBack++;
        climbWas = at;
        climbKnown = true;
        // A slide a line a frame (Logs/slide-trace.txt) from its start to SlideAfter after it ends: where its clip is, or after
        // it the walk or run that took over, how fast the body goes and how high the eyes are over the floor. From its getting up
        // on, the biggest change of the body's speed from one frame to the next (EndJump): the hand-over to the legs.
        var slideMove = player.Actions.SlideMove;
        bool sliding = player.CurrentState == PlayerMovement.MovementState.Sliding;
        if (sliding && slidFrom < 0f) slidFrom = clock;
        if (sliding) slidTo = -1f;
        else if (slidFrom >= 0f && slidTo < 0f) slidTo = clock;
        float ground = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        if (slidFrom >= 0f && (sliding || clock - slidTo <= SlideAfter))
        {
            var eyes = Camera.main;
            string where = sliding ? $"{slideMove.Now} {slideMove.ClipName} @{slideMove.Moment:0.000}"
                : $"{player.CurrentState} gait {player.Actions.Gait.Position:0.00}";
            PlayRecord.Line("slide-trace", SlideTraceHeading, $"{phases[this.at].Name} {clock - slidFrom:0.000}s {where} {ground:0.00}m/s "
                + $"eyes {(eyes != null ? eyes.transform.position.y - at.y : 0f):0.000}m");
            if (tally != null && paceWas >= 0f && (!sliding || slideMove.Now == SlideMove.Phase.Up))
                tally.EndJump = Mathf.Max(tally.EndJump, Mathf.Abs(ground - paceWas));
        }
        paceWas = ground;
        // The eyes against the capsule: how fast they drop, and the biggest change of their pace in a frame.
        var lens = Camera.main;
        if (tally != null && lens != null)
        {
            Vector3 eye = lens.transform.position - at, pace = (eye - eyeWas) / dt;
            if (eyeFrames >= 1) tally.EyeDrop = Mathf.Max(tally.EyeDrop, -pace.y);
            if (eyeFrames >= 2) tally.EyeJolt = Mathf.Max(tally.EyeJolt, (pace - eyePaceWas).magnitude);
            eyeWas = eye;
            eyePaceWas = pace;
            eyeFrames++;
        }
        if (body == null || !body.TryReadLegs(out var left, out var right)) return;
        float view = player.transform.eulerAngles.y, facing = body.FacingYaw;
        var t = tally;
        t.Frames++;
        Vector3 root = animator.transform.position, velocity = player.ActualVelocity;
        t.Speeds.Add(new Vector2(velocity.x, velocity.z).magnitude);
        t.Slowest = Mathf.Min(t.Slowest, new Vector2(velocity.x, velocity.z).magnitude);
        if (player.Actions != null && player.Actions.RunTurn.Turning) t.RunTurns++;
        if (body.Dancing) t.Dances++;
        // How often the clips' switches were eased over, and how far each ankle drawn stands off where the clips have it (m,
        // level): the legs going their own way, off the animation (the experts' review of 2026-11-26).
        if (t.StartsFrom < 0) t.StartsFrom = body.InertiaStarts;
        t.Starts = body.InertiaStarts - t.StartsFrom;
        if (body.TryReadClipAnkles(out Vector3 clipLeft, out Vector3 clipRight))
        {
            t.OffClip.Add(new Vector2(left.Foot.position.x - clipLeft.x, left.Foot.position.z - clipLeft.z).magnitude);
            t.OffClip.Add(new Vector2(right.Foot.position.x - clipRight.x, right.Foot.position.z - clipRight.z).magnitude);
        }
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

    private static float Middle(List<float> values)
    {
        values.Sort();
        return values.Count > 0 ? values[values.Count / 2] : 0f;
    }

    private static float Most(List<float> values)
    {
        float most = 0f;
        foreach (float value in values) most = Mathf.Max(most, value);
        return most;
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
        t.OffClip.Sort();
        float offMid = t.OffClip.Count > 0 ? t.OffClip[t.OffClip.Count / 2] : 0f;
        float offWorst = t.OffClip.Count > 0 ? t.OffClip[Mathf.Min(t.OffClip.Count - 1, t.OffClip.Count * 9 / 10)] : 0f;
        float offMost = t.OffClip.Count > 0 ? t.OffClip[t.OffClip.Count - 1] : 0f;
        float carried = t.Carried.Count > 0 ? t.Carried[t.Carried.Count / 2] : 0f, speed = t.Speeds.Count > 0 ? t.Speeds[t.Speeds.Count / 2] : 0f;
        PlayRecord.Line(Log, Heading, $"{name,-15} n {t.Frames,4}/{t.All,-4} | speed {speed:0.00} pose-carry {carried:0.00} | slide {median:0.00} {worst:0.00} skates {t.Skates,3} | knees {t.Knees,2} ankles {t.Ankles,2} pops {t.Pops,2} "
            + $"| neck {t.Neck:0}° spine {t.Spine:0}° | react {t.React:0.00}s | slowest {t.Slowest:0.0} run-turn {t.RunTurns,3} | view {t.View:+0;-0} body {t.Body:+0;-0} settle {t.Settled:0.00}s "
            + $"| off-clip {offMid * 100f:0} {offWorst * 100f:0} {offMost * 100f:0}cm eases {t.Starts,3} dance {(t.Frames > 0 ? 100f * t.Dances / t.Frames : 0f),3:0}% held {t.HeldBack,3}"
            + (t.ClimbTime > 0f ? $" | climb {t.ClimbTime:0.00}s fastest {t.ClimbFastest:0.0}m/s" : "")
            + (t.HangTime > 0f ? $" | hung {t.HangTime:0.00}s" : "")
            + (t.LadderTime > 0f ? $" | on the ladder {t.LadderTime:0.00}s" : "")
            + (t.TopSlides.Count > 0 ? $" | hands on top {t.OnTop:0.00}s slide {Middle(t.TopSlides):0.00} most {Most(t.TopSlides):0.00}m/s path {t.TopPath * 100f:0}cm" : "")
            + $" | eye drop {t.EyeDrop:0.0} jolt {t.EyeJolt:0.0}m/s"
            + (t.EndJump >= 0f ? $" | end jump {t.EndJump:0.00}m/s" : ""));
    }

    private void Finish(string trouble)
    {
        TakeDownLadder();
        film?.Dispose();
        if (trouble != null) PlayRecord.Line(Log, Heading, "-- " + trouble);
        else PlayRecord.Line(Log, Heading, "-- done");
        ScriptedInput.Off();
        try { File.Delete(Running); } catch (IOException) { }
        enabled = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void OnDestroy()
    {
        ScriptedInput.Off();
        film?.Dispose();
    }
}
