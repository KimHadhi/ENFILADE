using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using State = PlayerMovement.MovementState;

/// <summary>Repeatable editor checks; runs in a disposable additive scene.</summary>
public static class MovementRefactorChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 TestOrigin = new Vector3(10000f, 10000f, 10000f);
    private static int passed;
    private static Scene testScene;

    [MenuItem("Tools/Movement/Run Refactor Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run movement checks outside Play Mode.");
        passed = 0;
        Check("hierarchy lifecycle and transitions", CheckHierarchy);
        Scene previous = SceneManager.GetActiveScene();
        // A fresh batch project has an untitled scene. Save that disposable host
        // so Unity permits an additive physics scene; never save the user's scene.
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path))
            EditorSceneManager.SaveScene(previous, "Assets/MovementCheckHost.unity");
        testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            Check("serialized component settings", () => WithPlayer(CheckSerialization));
            Check("all posture collider heights", () => WithPlayer(CheckPostures));
            Check("standing and crouching jump", () => WithPlayer(CheckNormalJump));
            Check("prone jump stands up without launching", () => WithPlayer(CheckProneJump));
            Check("slide jump timing and momentum", () => WithPlayer(CheckSlideJump));
            Check("dive and prone slide cannot jump", () => WithPlayer(CheckBlockedJumps));
            Check("fast dive landing transitions", () => WithPlayer(CheckFastLanding));
            Check("slow dive landing transitions", () => WithPlayer(CheckSlowLanding));
            Check("parent slide friction executes once", () => WithPlayer(CheckSlidePhysics));
            Check("low ceiling blocks standing jump", () => WithPlayer(CheckCeiling));
            Check("ground support and landing detection", () => WithPlayer(CheckGround));
            Check("reset clears buffered movement", () => WithPlayer(CheckReset));
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(testScene, true);
        }
        Debug.Log($"MOVEMENT_CHECKS_PASSED: {passed}");
    }

    private static void Check(string name, Action action)
    {
        action();
        passed++;
        Debug.Log("PASS: " + name);
    }

    private static void WithPlayer(Action<PlayerMovement> action)
    {
        var go = new GameObject("Movement check player");
        SceneManager.MoveGameObjectToScene(go, testScene);
        go.layer = 31;
        go.transform.position = TestOrigin;
        go.AddComponent<CharacterController>();
        var player = go.AddComponent<PlayerMovement>();
        // MonoBehaviour.Awake is not automatically invoked in Edit Mode.
        typeof(PlayerMovement).GetMethod("Awake", Hidden).Invoke(player, null);
        try { action(player); }
        finally { Object.DestroyImmediate(go); }
    }

    private static MovementStateMachine Machine(PlayerMovement p) =>
        (MovementStateMachine)typeof(PlayerMovement).GetProperty("StateMachine", Hidden).GetValue(p);

    private static object Service(PlayerMovement p, string name) =>
        typeof(PlayerMovement).GetProperty(name, Hidden).GetValue(p);

    private static void Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Hidden).Invoke(target, args);

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Hidden).SetValue(target, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Near(float actual, float expected, string message) =>
        Require(Mathf.Abs(actual - expected) < 0.001f, $"{message}: expected {expected}, got {actual}");

    private static void CheckSerialization(PlayerMovement p)
    {
        JsonUtility.FromJsonOverwrite("{\"walkSpeed\":6.7,\"crouchColliderHeight\":1.1,\"groundDiveLaunchSpeed\":9.2}", p);
        Near(p.WalkSpeed, 6.7f, "Serialized walk speed");
        Near(p.CrouchColliderHeight, 1.1f, "Serialized crouch height");
        p.LaunchDive(Vector2.up);
        Near(p.HorizontalVelocity.magnitude, 9.2f, "Serialized dive speed");
        Require(p.IsDiving && p.CurrentStatePath == "Movement/Dive/Diving", "Dive parent is active");
    }

    private static void CheckPostures(PlayerMovement p)
    {
        var heights = new Dictionary<State, float>
        {
            { State.Standing, p.StandingColliderHeight }, { State.Crouching, p.CrouchColliderHeight },
            { State.Sliding, 0.6f }, { State.Proning, p.ProneColliderHeight },
            { State.Supine, p.ProneColliderHeight }, { State.HeadFirstSliding, p.ProneColliderHeight },
            { State.SupineSliding, p.ProneColliderHeight }, { State.Diving, p.ProneColliderHeight },
            { State.SupineDiving, p.ProneColliderHeight }
        };
        var controller = p.GetComponent<CharacterController>();
        foreach (var entry in heights)
        {
            p.ChangeState(entry.Key);
            p.UpdateHeightsAndTransform(0f);
            Near(controller.height, entry.Value, entry.Key + " height");
            Near(controller.center.y, entry.Value / 2f, entry.Key + " feet anchored");
        }
    }

    private static void CheckNormalJump(PlayerMovement p)
    {
        Require(Machine(p).Current.TryJump(), "Standing jump accepted");
        Near(p.VerticalVelocity, Mathf.Sqrt(2f * 1.3f * 24f), "Standing jump speed");
        p.ResetVelocity();
        p.ChangeState(State.Crouching);
        Require(Machine(p).Current.TryJump(), "Crouching jump accepted");
        Require(p.CurrentState == State.Standing && !p.IsGrounded, "Crouching jump stands up and leaves ground");
    }

    private static void CheckProneJump(PlayerMovement p)
    {
        foreach (State state in new[] { State.Proning, State.Supine })
        {
            p.ChangeState(state);
            Require(Machine(p).Current.TryJump(), "Prone get-up accepted");
            Require(p.CurrentState == State.Standing, "Prone parent handles get-up");
            Near(p.VerticalVelocity, 0f, "Get-up does not jump");
        }
    }

    private static void CheckSlideJump(PlayerMovement p)
    {
        p.ChangeState(State.Sliding);
        p.SlideDirection = Vector3.forward;
        p.CurrentSlideSpeed = 8f;
        Require(!Machine(p).Current.TryJump(), "Early slide jump blocked");
        p.CurrentStateDuration = 0.2f;
        Require(Machine(p).Current.TryJump(), "Mature slide jump accepted");
        Require(p.CurrentState == State.Diving && p.IsDiving && !p.IsSliding, "Slide jumps into dive hierarchy");
        Near(p.HorizontalVelocity.z, 8.8f, "Slide momentum plus takeoff bonus");
        Near(p.VerticalVelocity, Mathf.Sqrt(2f * 1.05f * 24f) + 1.056f, "Slide jump pop");
        foreach (float speed in new[] { 0f, 0.5f, 1.5f, 2.2f })
        {
            p.ChangeState(State.Sliding);
            p.SlideDirection = Vector3.forward;
            p.CurrentSlideSpeed = speed;
            p.CurrentStateDuration = 0.2f;
            Require(Machine(p).Current.TryJump(), "Slow slide jump accepted");
            Near(p.HorizontalVelocity.magnitude, 3f, "Slow slide gets minimum takeoff speed");
        }
    }

    private static void CheckBlockedJumps(PlayerMovement p)
    {
        foreach (State state in new[] { State.Diving, State.SupineDiving, State.HeadFirstSliding, State.SupineSliding })
        {
            p.ChangeState(state);
            Require(!Machine(p).Current.TryJump() && p.CurrentState == state, state + " jump blocked");
        }
    }

    private static void CheckFastLanding(PlayerMovement p)
    {
        foreach (State state in new[] { State.Diving, State.SupineDiving })
        {
            p.ChangeState(state);
            p.HorizontalVelocity = Vector3.forward * 8f;
            Machine(p).Current.OnLanding();
            Require(p.CurrentState == (state == State.Diving ? State.HeadFirstSliding : State.SupineSliding), "Dive landing posture");
            Near(p.CurrentSlideSpeed, 8f, "Landing momentum");
        }
    }

    private static void CheckSlowLanding(PlayerMovement p)
    {
        foreach (State state in new[] { State.Diving, State.SupineDiving })
        {
            p.ChangeState(state);
            p.HorizontalVelocity = Vector3.forward;
            Machine(p).Current.OnLanding();
            Require(p.CurrentState == (state == State.Diving ? State.Proning : State.Supine), "Slow landing posture");
            Require(p.IsProning, "Prone parent active");
        }
    }

    private static void CheckSlidePhysics(PlayerMovement p)
    {
        foreach (State state in new[] { State.Sliding, State.HeadFirstSliding, State.SupineSliding })
        {
            p.ChangeState(state);
            p.SlideDirection = Vector3.forward;
            p.CurrentSlideSpeed = 10f;
            float friction = state == State.Sliding ? p.SlideFriction : state == State.HeadFirstSliding ? p.DiveSlideFriction : p.SupineSlideFriction;
            Machine(p).Current.UpdatePhysics(0.1f);
            Near(p.CurrentSlideSpeed, 10f - friction * p.AirSlideFrictionMultiplier * 0.1f, "Parent applies friction once");
        }
    }

    private static GameObject Box(Vector3 position, Vector3 scale)
    {
        var box = new GameObject("Movement check surface");
        SceneManager.MoveGameObjectToScene(box, testScene);
        box.layer = 30;
        box.transform.position = position;
        box.AddComponent<BoxCollider>().size = scale;
        Physics.SyncTransforms();
        return box;
    }

    private static void CheckCeiling(PlayerMovement p)
    {
        p.ChangeState(State.Crouching);
        p.UpdateHeightsAndTransform(0f);
        var ceiling = Box(TestOrigin + Vector3.up * 1.35f, new Vector3(4f, 0.2f, 4f));
        try
        {
            Require(!Machine(p).Current.TryJump(), "Ceiling blocks jump");
            Require(p.CurrentState == State.Crouching, "Blocked crouching jump preserves posture");
        }
        finally { Object.DestroyImmediate(ceiling); }
    }

    private static void CheckGround(PlayerMovement p)
    {
        var ground = Box(TestOrigin - Vector3.up * 0.1f, new Vector3(8f, 0.2f, 8f));
        try
        {
            p.VerticalVelocity = -2f;
            var motor = Service(p, "Motor");
            Call(motor, "Move", 0.02f);
            Call(motor, "CheckGroundedAndSlope");
            Require(p.IsGrounded && p.LandedThisFrame, "Capsule detects landing");
            Call(motor, "CheckGroundedAndSlope");
            Require(p.IsGrounded && !p.LandedThisFrame, "Landing fires only on first supported frame");
        }
        finally { Object.DestroyImmediate(ground); }
    }

    private static void CheckReset(PlayerMovement p)
    {
        p.LaunchDive(Vector2.up);
        var actions = Service(p, "Actions");
        Set(actions, "jumpBufferTimer", 0.1f);
        Set(actions, "coyoteTimer", 0.1f);
        p.ResetVelocity();
        Call(actions, "ProcessJump");
        Require(p.Velocity == Vector3.zero && !p.IsGrounded && p.CurrentState == State.Standing, "Reset clears momentum, support and pending jump");
    }

    private static void CheckHierarchy()
    {
        var events = new List<string>();
        var root = new Probe("root", null, events);
        var a = new Probe("a", root, events);
        var b = new Probe("b", root, events);
        var one = new Probe("one", a, events);
        var two = new Probe("two", a, events);
        var three = new Probe("three", b, events);
        var machine = new MovementStateMachine(new Dictionary<State, MovementBaseState>
        {
            { State.Standing, one }, { State.Crouching, two }, { State.Diving, three }
        }, State.Standing);
        Require(string.Join(",", events) == "+root,+a,+one", "Initial enter order");
        events.Clear();
        machine.Duration = 2f;
        machine.ChangeState(State.Crouching);
        Require(string.Join(",", events) == "-one,+two", "Sibling transition retains shared ancestors");
        Require(machine.PreviousState == State.Standing && machine.Duration == 0f, "Transition metadata");
        events.Clear();
        machine.ChangeState(State.Diving);
        Require(string.Join(",", events) == "-two,-a,+b,+three", "Cross-branch exit/enter order");
        events.Clear();
        machine.Duration = 3f;
        machine.ChangeState(State.Diving);
        Require(events.Count == 0 && machine.Duration == 3f, "Same-state transition is a no-op");
        bool rejected = false;
        try { machine.ChangeState((State)999); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected && machine.Current == three && machine.Duration == 3f, "Invalid transition preserves active branch");
        // Base leaf methods must reach a parent's event implementation once.
        var handler = new EventProbe(events);
        var leaf = new Probe("leaf", handler, events);
        leaf.OnLanding();
        Require(leaf.TryJump() && string.Join(",", events) == "landing,jump", "Events bubble to parent");
    }

    private sealed class Probe : MovementBaseState
    {
        private readonly string label;
        private readonly List<string> events;
        public Probe(string label, MovementBaseState parent, List<string> events) : base(null, parent) { this.label = label; this.events = events; }
        public override void Enter() => events.Add("+" + label);
        public override void Exit() => events.Add("-" + label);
    }

    private sealed class EventProbe : MovementBaseState
    {
        private readonly List<string> events;
        public EventProbe(List<string> events) : base(null) { this.events = events; }
        public override void OnLanding() => events.Add("landing");
        public override bool TryJump() { events.Add("jump"); return true; }
    }
}
