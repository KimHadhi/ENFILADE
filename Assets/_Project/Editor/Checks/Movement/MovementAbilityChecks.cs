using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int passed;
    [MenuItem("Tools/Movement/Run Ability Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before running checks.");
        Scene previous = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path)) EditorSceneManager.SaveScene(previous, ProjectAssetPaths.DevelopmentScene("MovementCheckHost.unity"));
        Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        passed = 0;
        try
        {
            Check("shared dodge window, cooldown and one success", SharedDodge);
            Check("Q press does not translate; intercepted hit moves once", MicroDodge);
            Check("micro dodge displacement respects walls", MicroWall);
            Check("fall bypasses dodge; duplicate attack is suppressed", DamageRules);
            Check("Alt step dodge moves and ends", StepDodge);
            Check("vault selection and movement completion", Vault);
            Check("mantle selection and movement completion", Mantle);
            Check("blocked obstacle path falls back to normal jump", BlockedVault);
            Check("wall kick preserves outward launch", WallKick);
            Check("wall climb exhausts its air budget", WallBudget);
            Check("swing bar grab, swing, release and regrab lock", Swing);
            Check("destroyed swing bar releases safely", DestroyedBar);
            Check("fall damage and timed roll", FallRoll);
            Check("shove and sprint tackle selection", Shove);
            Check("attack hitbox resolves a real overlapping hit", Hitbox);
            Check("new input bindings and original GUID", Bindings);
            Check("reset cancels active motion and immunity", Reset);
            RunFeelChecks();
            RunLedgeBarChecks();
            RunReferenceChecks();
            RunDodgeChainChecks();
            RunCameraChecks();
            RunParkourFacingChecks();
            RunLowPassageChecks();
            RunEvasiveDiveChecks();
            RunContinuityChecks();
            RunBalancePassageChecks();
        }
        finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(test, true); }
        Debug.Log("MOVEMENT_ABILITY_CHECKS_PASSED: " + passed);
    }
    public static void RunAll() { MovementRefactorChecks.Run(); Run(); }
    private static void Check(string name, Action test) { test(); passed++; Debug.Log("PASS ABILITY: " + name); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message, float tolerance = 0.03f) => Require(Mathf.Abs(actual - expected) <= tolerance, message + ": " + actual + " != " + expected);
    private static object Service(object target, string name) => target.GetType().GetProperty(name, Any).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Any).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Any).Invoke(target, args);
    private static void Input(Fixture f, string name, object value) => f.Input.GetType().GetProperty(name, Any).SetValue(f.Input, value);

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root, Player;
        public readonly PlayerMovement P;
        public readonly object Motor, Abilities, Input;
        public readonly MovementStateMachine Machine;
        public readonly Gamepad Pad;
        private readonly InputAction move;
        public readonly Vector3 Origin = new Vector3(1000f, 1000f, 1000f);
        public Fixture(bool floor = false, bool camera = false)
        {
            Root = new GameObject("Ability test fixtures");
            Player = new GameObject("Player"); Player.transform.SetParent(Root.transform); Player.layer = 31;
            Player.transform.position = Origin;
            var cc = Player.AddComponent<CharacterController>(); cc.height = 1.75f; cc.radius = 0.3f; cc.skinWidth = 0.03f;
            if (camera)
            {
                var pivot = new GameObject("CrouchPivot"); pivot.transform.SetParent(Player.transform, false); pivot.transform.localPosition = Vector3.up * 1.6f;
                var eye = new GameObject("Camera"); eye.transform.SetParent(pivot.transform, false); eye.AddComponent<Camera>();
            }
            P = Player.AddComponent<PlayerMovement>(); Call(P, "Awake");
            Motor = Service(P, "Motor"); Abilities = Service(P, "Abilities"); Input = Service(P, "Input");
            Machine = (MovementStateMachine)Service(P, "StateMachine");
            Pad = InputSystem.AddDevice<Gamepad>();
            move = new InputAction("TestMove", InputActionType.Value, "<Gamepad>/leftStick");
            move.Enable(); Set(Input, "moveAction", move);
            if (floor) { Box(new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f)); Ground(); }
        }
        public void MoveInput(Vector2 input)
        {
            InputState.Change(Pad.leftStick, input);
        }
        public GameObject Box(Vector3 offset, Vector3 size)
        {
            var go = new GameObject("Test obstacle"); go.transform.SetParent(Root.transform); go.layer = 30;
            go.transform.position = Origin + offset; go.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms(); return go;
        }
        public void Ground() { P.VerticalVelocity = -2f; Call(Motor, "Move", 0.02f); Call(Motor, "CheckGroundedAndSlope"); Require(P.IsGrounded, "Fixture must be grounded"); }
        public void Step(float dt = 0.02f)
        {
            Call(Abilities, "BeginFrame", dt); Call(Service(P, "Actions"), "TickTimers", dt); Call(Motor, "TickTimers", dt); Machine.Duration += dt;
            Call(Motor, "CheckGroundedAndSlope");
            if (!(bool)Call(Abilities, "ProcessInput")) Machine.Current.ProcessInput(P.CurrentInput);
            Machine.Current.UpdateLogic(dt);
            Call(Service(P, "Actions"), "ProcessJump"); Call(Service(Abilities, "Lean"), "Tick", dt);
            Call(Service(P, "Posture"), "UpdateAirCrouch"); Machine.Current.UpdatePhysics(dt);
            if (Machine.Current.CanApplyGravity) Call(Motor, "ApplyGravity", dt);
            P.UpdateHeightsAndTransform(dt); Call(Motor, "Move", dt); Physics.SyncTransforms();
            MovementAbilityChecks.Input(this, "JumpPressed", false); MovementAbilityChecks.Input(this, "CrouchPressed", false);
            MovementAbilityChecks.Input(this, "DodgePressed", false); MovementAbilityChecks.Input(this, "ShovePressed", false);
            MovementAbilityChecks.Input(this, "LeanPressed", false);
        }
        public void Dispose() { move.Dispose(); InputSystem.RemoveDevice(Pad); Object.DestroyImmediate(Root); }
    }

    private static void SharedDodge()
    {
        var dodge = new DodgeController(); int success = 0;
        dodge.Succeeded += (k, side) => success++;
        Require(dodge.TryStart(DodgeKind.Micro, -1, 0.1f, 0.5f), "Begin micro");
        Require(!dodge.TryStart(DodgeKind.Step, 0, 0.2f, 0.5f), "No success means no chain");
        Require(dodge.TryEvade() && dodge.TryEvade() && success == 1, "One success per activation");
        Require(dodge.CanStart && dodge.CooldownRemaining == 0f, "Success immediately refunds cooldown");
        Require(dodge.TryStart(DodgeKind.Step, 0, 0.2f, 0.5f), "Success can chain during the old window");
        dodge.Tick(0.21f); Require(!dodge.TryEvade() && !dodge.CanStart, "Failed dodge keeps cooldown after immunity ends");
        dodge.Tick(0.3f); Require(dodge.CanStart, "Failed dodge cooldown eventually expires");
    }
    private static DamageHit Attack() => new DamageHit(DamageHit.NewAttackId(), 10f, DamageKind.Attack, Vector3.zero, Vector3.forward);
    private static void MicroDodge()
    {
        using (var f = new Fixture(true))
        {
            Vector3 before = f.P.transform.position;
            Input(f, "LeanSide", -1); Input(f, "LeanPressed", true); f.Step();
            Near(f.P.transform.position.x, before.x, "Pressing Q never moves position", 0.001f);
            Require(f.P.IsDodging && f.P.LeanRoll > 0f, "Lean and i-frames begin");
            var hit = Attack(); Require(f.P.DamageReceiver.ReceiveHit(hit) == DamageResult.Evaded, "Real attack intercepted");
            Require(f.P.DamageReceiver.ReceiveHit(hit) == DamageResult.Duplicate, "Repeated hit ignored");
            f.P.DamageReceiver.ReceiveHit(Attack());
            for (int i = 0; i < 6; i++) f.Step();
            Near(f.P.transform.position.x, before.x - f.P.AbilitySettings.microSuccessDistance, "Success displacement exactly once");
            Near(f.P.DamageReceiver.Health, 100f, "Evaded attacks deal no damage");
            float x = f.P.transform.position.x;
            for (int i = 0; i < 12; i++) f.Step();
            Near(f.P.transform.position.x, x, "Holding Q does not repeat movement");
        }
    }
    private static void MicroWall()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(-0.45f, 1f, 0f), new Vector3(0.15f, 2f, 4f));
            Input(f, "LeanSide", -1); Input(f, "LeanPressed", true); f.Step();
            f.P.DamageReceiver.ReceiveHit(Attack());
            for (int i = 0; i < 6; i++) f.Step();
            Require(f.P.transform.position.x > f.Origin.x - 0.15f, "Micro reaction cannot cross wall");
        }
    }
    private static void DamageRules()
    {
        using (var f = new Fixture())
        {
            ((DodgeController)Service(f.Abilities, "Dodge")).TryStart(DodgeKind.Step, 0, 1f, 1f);
            var fall = new DamageHit(DamageHit.NewAttackId(), 20f, DamageKind.Fall, Vector3.zero, Vector3.up);
            Require(f.P.DamageReceiver.ReceiveHit(fall) == DamageResult.Damaged, "Fall bypasses attack immunity");
            Require(f.P.DamageReceiver.ReceiveHit(fall) == DamageResult.Duplicate, "Fall ID applied once");
            Near(f.P.DamageReceiver.Health, 80f, "Single fall damage");
        }
    }
    private static void StepDodge()
    {
        using (var f = new Fixture(true))
        {
            Input(f, "DodgePressed", true); f.Step();
            Require(f.P.CurrentState == State.StepDodge && f.P.IsDodging, "Alt begins step dodge");
            for (int i = 0; i < 20; i++) f.Step();
            Require(f.P.CurrentState == State.Standing && !f.P.IsDodging, "Step ends cleanly");
            Require(f.P.transform.position.z < f.Origin.z - 1.9f, "Short step still moves decisively");
        }
    }
    private static void Vault()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.MoveInput(Vector2.up);
            var detector = (ParkourDetector)Service(Service(f.Abilities, "Traversal"), "Detector");
            bool detected = detector.TryObstacle(Vector3.forward, out ObstacleTarget obstacle);
            Require(detected && obstacle.IsVault, "Valid low obstacle detected");
            Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.Vault, "Move+jump chooses vault: " + f.P.CurrentState);
            for (int i = 0; i < 40; i++) f.Step();
            Require(!f.P.IsParkour && f.P.transform.position.z > f.Origin.z + 1.6f, "Vault crosses obstacle and exits");
        }
    }
    private static void Mantle()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 0.8f, 2.6f), new Vector3(3f, 1.6f, 3f));
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.Mantle, "High ledge chooses mantle: " + f.P.CurrentState);
            for (int i = 0; i < 42; i++) f.Step();
            Require(!f.P.IsParkour && f.P.transform.position.y > f.Origin.y + 1.4f, "Mantle finishes on ledge");
        }
    }
    private static void BlockedVault()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.Box(new Vector3(0f, 1.9f, 1.3f), new Vector3(3f, 0.3f, 1f));
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            Require(!f.P.IsParkour, "Blocked overhead path refuses traversal");
        }
    }
    private static void WallKick()
    {
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, 1.5f, 0.7f), new Vector3(4f, 3f, 0.2f));
            f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Front wall starts climb");
            Input(f, "JumpHeld", false); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Releasing Space allows a later fresh kick press");
            Input(f, "JumpHeld", true);
            Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.WallKick && f.P.VerticalVelocity > 0f && f.P.HorizontalVelocity.z < -1f, "Wall kick pushes out and up");
            f.Step(); Require(!f.P.IsParkour, "Kick returns to air locomotion");
        }
    }
    private static void WallBudget()
    {
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, 10f, 0.7f), new Vector3(6f, 40f, 0.2f));
            f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Front wall starts climb");
            for (int i = 0; i < 110; i++) f.Step();
            Require(!f.P.IsParkour, "Climb budget does not refill by reattaching");
        }
    }
    private static SwingBar MakeBar(Fixture f)
    {
        var go = new GameObject("Swing bar"); go.transform.SetParent(f.Root.transform);
        go.transform.position = f.Origin + Vector3.up * 2f;
        return go.AddComponent<SwingBar>();
    }
    private static void Swing()
    {
        using (var f = new Fixture())
        {
            MakeBar(f); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.BarGrab, "Nearby bar caught");
            for (int i = 0; i < 12; i++) f.Step();
            Require(f.P.CurrentState == State.BarSwing, "Grab settles to swing");
            f.MoveInput(Vector2.up); for (int i = 0; i < 20; i++) f.Step();
            Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.LacheRelease && f.P.Velocity.magnitude > 1f, "Lache inherits swing momentum");
            for (int i = 0; i < 4; i++) f.Step();
            Require(f.P.CurrentState != State.BarGrab, "Immediate same-bar regrab blocked");
        }
    }
    private static void DestroyedBar()
    {
        using (var f = new Fixture())
        {
            var bar = MakeBar(f); Input(f, "JumpHeld", true); f.Step();
            for (int i = 0; i < 12; i++) f.Step();
            Object.DestroyImmediate(bar.gameObject); f.Step(); f.Step();
            Require(!f.P.IsParkour, "Destroyed support releases player");
        }
    }
    private static void FallRoll()
    {
        using (var f = new Fixture())
        {
            var landing = Service(f.Abilities, "Landing");
            Set(landing, "impactSpeed", 20f); Set(landing, "trackingFall", true); Set(landing, "highestFeetY", f.Origin.y + 8f); Call(landing, "OnLanding");
            Near(f.P.DamageReceiver.Health, 84f, "Unrolled fall deals damage");
            f.P.DamageReceiver.RestoreHealth();
            Set(landing, "impactSpeed", 20f); Set(landing, "trackingFall", true); Set(landing, "highestFeetY", f.Origin.y + 8f); Set(landing, "rollBuffer", 0.1f);
            Require((bool)Call(landing, "OnLanding"), "Buffered roll consumes landing");
            Require(f.P.CurrentState == State.LandingRoll, "Landing roll selected");
            Near(f.P.DamageReceiver.Health, 100f, "Roll cancels fall damage before application");
        }
    }
    private static void Shove()
    {
        using (var f = new Fixture(true))
        {
            Input(f, "ShovePressed", true); f.Step(); Require(f.P.CurrentState == State.Standing, "Stationary shove preserves locomotion");
            for (int i = 0; i < 30; i++) f.Step();
            Set(f.Motor, "actualVelocity", Vector3.forward * 7f); f.MoveInput(Vector2.up);
            Input(f, "SprintHeld", true); Input(f, "ShovePressed", true); f.Step();
            Require(f.P.CurrentState == State.Tackle, "Running shove becomes tackle");
        }
    }
    private static void Hitbox()
    {
        using (var f = new Fixture(true))
        {
            Input(f, "LeanSide", 1); Input(f, "LeanPressed", true); f.Step();
            var go = f.Box(Vector3.up, Vector3.one * 0.2f); var attack = go.AddComponent<AttackHitbox>(); attack.BeginAttack();
            Require(attack.TryHit(f.Player.GetComponent<Collider>()) == DamageResult.Evaded, "Hitbox uses common dodge mechanism");
            Require(attack.TryHit(f.Player.GetComponent<Collider>()) == DamageResult.Ignored, "Same swing cannot re-hit");
        }
    }
    private static void Bindings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/Input/Player.inputactions");
        Require(asset != null, "Input asset exists");
        foreach (string name in new[] { "Dodge", "LeanLeft", "LeanRight", "Shove", "Jump", "Dive" })
            Require(asset.FindAction("Player/" + name, false)?.bindings.Count > 0, "Binding exists: " + name);
    }
    private static void Reset()
    {
        using (var f = new Fixture(true))
        {
            Input(f, "DodgePressed", true); f.Step(); f.P.ResetVelocity();
            Require(!f.P.IsDodging && f.P.Velocity == Vector3.zero && f.P.CurrentState == State.Standing, "Reset clears state, velocity and immunity");
        }
    }
}
