using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static object Dive(Fixture f) => Service(Service(f.P, "Actions"), "Dive");
    private static void RunEvasiveDiveChecks()
    {
        Check("evasive dive launches in eight directions and keeps backward body orientation", DiveDirections);
        Check("dive burst decays without being refreshed by free look or posture flips", DiveBurstDecay);
        Check("dive holds speed before smoothly braking at every frame rate", DiveSpeedHold);
        Check("air apex and a stopped airborne body never end dive prematurely", DiveApex);
        Check("short dive distance and landing recovery agree at 30/60/120Hz", DiveDistance);
        Check("repeating dives loses a distance race to sprinting", DiveVersusSprint);
        Check("dive eye rises before folding and lands smoothly without writing mouse aim", DiveCamera);
        Check("dive keeps its takeoff body height before folding and does not restart on turning", DivePoseTiming);
        Check("crouched and tucked air entries preserve camera position at launch", DiveEntryPose);
        Check("dive remains blocked by a thin wall and still lands", DiveCollision);
        Check("air-dive shares backward launch and forbids a second airborne burst", AirDiveLaunch);
        Check("low ceiling cuts upward dive velocity and permits a safe landing", DiveCeiling);
    }
    private static void DiveDirections()
    {
        foreach (Vector2 input in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right,
            new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        using (var f = new Fixture(true))
        {
            f.P.LaunchDive(input);
            Vector3 direction = new Vector3(input.x, 0f, input.y).normalized;
            Require(Vector3.Dot(f.P.HorizontalVelocity.normalized, direction) > 0.999f, "Dive follows chosen input");
            Near(f.P.HorizontalVelocity.magnitude, 9f, "Initial burst speed");
            f.MoveInput(Vector2.zero); f.Step();
            Require(f.P.CurrentState == (input.y < -0.2f ? State.SupineDiving : State.Diving), "Backwards dive does not instantly flip forward");
            Require(!f.P.IsDodging, "Dive remains physical evasion, not shared dodge immunity");
        }
    }
    private static void DiveBurstDecay()
    {
        using (var f = new Fixture())
        {
            f.P.LaunchDive(Vector2.up); f.MoveInput(Vector2.zero);
            for (int i = 0; i < 5; i++) f.Step();
            Require(f.P.HorizontalVelocity.magnitude > 8f, "First tenth of a second keeps punch while braking ramps in");
            f.P.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            for (int i = 0; i < 15; i++) f.Step();
            Require(f.P.CurrentState == State.SupineDiving, "Free look can turn onto the back");
            Require(f.P.HorizontalVelocity.z > 0f && f.P.HorizontalVelocity.magnitude < 8.5f,
                "Looking back neither reverses nor replenishes burst");
            Require(!(bool)Call(Dive(f), "TryLaunch", Vector2.up, false), "No second impulse while in the air");
            f.P.ResetVelocity();
            Require((bool)Call(Dive(f), "TryLaunch", Vector2.up, false), "Reset removes old dive restrictions");
        }
    }
    private static void DiveSpeedHold()
    {
        float shortest = float.MaxValue, longest = 0f;
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        using (var f = new Fixture())
        {
            f.P.LaunchDive(Vector2.up); f.MoveInput(Vector2.zero);
            float time = 0f;
            while (time + dt < 0.25f)
            {
                Call(Dive(f), "UpdateFlight", dt); time += dt;
                Near(f.P.HorizontalVelocity.magnitude, 9f, "No horizontal drag in takeoff hold");
            }
            Call(Dive(f), "UpdateFlight", 0.25f - time);
            Near(f.P.HorizontalVelocity.magnitude, 9f, "Hold boundary retains speed");
            for (int i = 0; i < Mathf.RoundToInt(0.5f / dt); i++) Call(Dive(f), "UpdateFlight", dt);
            float speed = f.P.HorizontalVelocity.magnitude;
            Require(speed < 6f && speed > 2.2f, "Drag resumes after hold");
            shortest = Mathf.Min(shortest, speed); longest = Mathf.Max(longest, speed);
        }
        Require(longest - shortest < 0.02f, "Hold and braking agree across frame rates");
    }
    private static void DiveApex()
    {
        foreach (State state in new[] { State.Diving, State.SupineDiving })
        using (var f = new Fixture())
        {
            f.P.ChangeState(state); f.P.HorizontalVelocity = Vector3.zero; f.P.VerticalVelocity = 0f;
            for (int i = 0; i < 12; i++) f.Machine.Current.UpdateLogic(0.02f);
            Require(f.P.IsDiving, "Zero vertical/horizontal velocity without ground is not a landing");
        }
    }
    private static void DiveDistance()
    {
        float shortest = float.MaxValue, longest = 0f;
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        using (var f = new Fixture(true))
        {
            f.P.LaunchDive(Vector2.up); f.MoveInput(Vector2.zero);
            float time = 0f;
            while (f.P.IsDiving && time < 1f) { f.Step(dt); time += dt; }
            Require(f.P.IsSliding && f.P.IsGrounded, "Real landing continues into low slide");
            float distance = f.P.transform.position.z - f.Origin.z;
            Debug.Log("DIVE_MEASURE: Hz=" + (1f / dt) + " flight=" + time + " distance=" + distance);
            Require(time > 0.25f && time < 0.65f && distance > 2f && distance < 4.5f, "Short evasive flight envelope");
            shortest = Mathf.Min(shortest, distance); longest = Mathf.Max(longest, distance);
            float cooldown = (float)Service(Dive(f), "RecoveryRemaining");
            Require(cooldown > 0.3f && !(bool)Call(Dive(f), "TryLaunch", Vector2.up, true), "Landing cannot immediately chain a fresh burst");
            while (time < 1.2f) { f.Step(dt); time += dt; }
            Debug.Log("DIVE_SETTLE: Hz=" + (1f / dt) + " distance=" + (f.P.transform.position.z - f.Origin.z) + " state=" + f.P.CurrentState);
            Require(f.P.IsProning && f.P.transform.position.z - f.Origin.z < 7f, "The takeoff hold still settles within the bounded dive envelope");
        }
        Require(longest - shortest < 0.35f, "Frame rate does not substantially change dodge range");
    }
    private static void DiveVersusSprint()
    {
        float sprintDistance;
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, -0.1f, 40f), new Vector3(20f, 0.2f, 120f)); f.Ground();
            f.P.SprintProgress = 1f; f.P.HorizontalVelocity = Vector3.forward * f.P.SprintSpeed;
            f.MoveInput(Vector2.up);
            for (int i = 0; i < 300; i++) f.Step();
            sprintDistance = f.P.transform.position.z - f.Origin.z;
        }
        using (var f = new Fixture())
        using (var sprint = new InputAction("TestSprint", InputActionType.Button, "<Gamepad>/leftTrigger"))
        {
            f.Box(new Vector3(0f, -0.1f, 40f), new Vector3(20f, 0.2f, 120f)); f.Ground();
            sprint.Enable(); Set(f.Input, "sprintAction", sprint); InputState.Change(f.Pad.leftTrigger, 1f);
            f.P.SprintProgress = 1f; f.P.HorizontalVelocity = Vector3.forward * f.P.SprintSpeed;
            f.MoveInput(Vector2.up); int launches = 0;
            for (int i = 0; i < 300; i++)
            {
                if (f.P.IsProning) f.Machine.Current.TryJump();
                if (f.P.CurrentState == State.Standing && f.P.IsGrounded)
                {
                    f.P.SprintProgress = 1f;
                    if ((bool)Call(Dive(f), "TryLaunch", Vector2.up, true)) launches++;
                }
                f.Step();
            }
            float distance = f.P.transform.position.z - f.Origin.z;
            Debug.Log("DIVE_RACE: 6 seconds sprint=" + sprintDistance + " repeatedDive=" + distance + " launches=" + launches);
            Require(launches >= 4 && distance < sprintDistance * 0.8f, "Even with the takeoff hold, repeated dive must remain slower than sprint");
        }
    }
    private static void DiveCamera()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        foreach (Vector2 direction in new[] { Vector2.up, Vector2.down, Vector2.right, Vector2.left })
        using (var f = new Fixture(true, true))
        {
            var cam = f.Player.GetComponentInChildren<Camera>(); Transform aim = cam.transform.parent;
            var effects = f.Player.AddComponent<PlayerCameraEffects>(); Call(effects, "Awake");
            f.P.LaunchDive(direction); f.MoveInput(Vector2.zero);
            float previous = cam.transform.position.y, initial = previous, highest = previous, peakTime = 0f;
            for (int i = 0; i < Mathf.CeilToInt(1.2f / dt); i++)
            {
                Quaternion look = Quaternion.Euler(25f, 0f, 0f); aim.localRotation = look;
                f.Step(dt); Call(effects, "UpdateDivePresentation", dt); Call(effects, "ApplyCameraRotation");
                Require(Mathf.Abs(cam.transform.position.y - previous) < 0.38f, "Dive/landing height is continuous");
                if ((i + 1) * dt <= 0.1f)
                    Require(cam.transform.position.y > previous, "First takeoff frames rise instead of dropping into prone");
                if (cam.transform.position.y > highest) { highest = cam.transform.position.y; peakTime = (i + 1) * dt; }
                Near(Quaternion.Angle(aim.localRotation, look), 0f, "Dive effects never write aim", 0.001f);
                previous = cam.transform.position.y;
            }
            Debug.Log("DIVE_EYE_ARC: Hz=" + (1f / dt) + " direction=" + direction + " rise=" + (highest - initial) + " peakTime=" + peakTime);
            Require(highest - initial > 0.25f && peakTime >= 0.1f && peakTime < 0.3f, "Visible upward arc before the fall");
            Require(cam.transform.position.y - f.P.transform.position.y < 0.6f, "Landing retains low eye height");
        }
    }
    private static void DivePoseTiming()
    {
        using (var f = new Fixture(true, true))
        {
            var cc = f.Player.GetComponent<CharacterController>();
            f.P.LaunchDive(Vector2.up); f.MoveInput(Vector2.zero);
            for (int i = 0; i < 5; i++) f.Step();
            Require(cc.height > 1.65f, "Takeoff begins gradually without instantly shrinking the capsule");
            for (int i = 0; i < 9; i++) f.Step();
            Require(cc.height > 0.5f && cc.height < 1.5f, "Body folds progressively in flight");
            float before = cc.height; int serial = (int)Service(Dive(f), "EntrySerial");
            f.P.transform.rotation = Quaternion.Euler(0f, 180f, 0f); f.Step();
            Require(f.P.CurrentState == State.SupineDiving && cc.height < before, "Look reversal does not restart the takeoff pose");
            Require((int)Service(Dive(f), "EntrySerial") == serial, "Turning remains one dive");
        }
    }
    private static void DiveEntryPose()
    {
        foreach (bool tucked in new[] { false, true })
        using (var f = new Fixture(!tucked, true))
        {
            f.P.ChangeState(State.Crouching); f.P.UpdateHeightsAndTransform(0f);
            if (tucked)
            {
                var posture = Service(f.P, "Posture");
                Set(posture, "airBodyTucked", true); Set(posture, "airCrouchTop", 1.75f);
                Set(posture, "airCameraOffset", -0.63f); f.P.UpdateHeightsAndTransform(0f);
            }
            var camera = f.Player.GetComponentInChildren<Camera>(); float before = camera.transform.position.y;
            Require((bool)Call(Dive(f), "TryLaunch", Vector2.up, !tucked), "Entry accepted");
            Near(camera.transform.position.y, before, "Beginning dive does not teleport the camera", 0.001f);
            f.Step(1f / 60f);
            Require(Mathf.Abs(camera.transform.position.y - before) < 0.2f, "Alternate entry pose remains continuous");
        }
    }
    private static void DiveCollision()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 1f, 1.25f), new Vector3(4f, 2f, 0.04f));
            f.P.LaunchDive(Vector2.up); f.MoveInput(Vector2.zero);
            for (int i = 0; i < 70; i++) f.Step();
            Require(f.P.transform.position.z < f.Origin.z + 1.15f && f.P.IsGrounded && !f.P.IsDiving,
                "A dive cannot tunnel through a thin wall or remain stuck airborne");
        }
    }
    private static void AirDiveLaunch()
    {
        using (var f = new Fixture())
        {
            f.MoveInput(Vector2.down);
            Require((bool)Call(Dive(f), "TryLaunch", Vector2.down, false), "First air dive is accepted");
            f.Step();
            Require(f.P.CurrentState == State.SupineDiving && f.P.HorizontalVelocity.z < -8f,
                "Air Z uses the same directional burst and correct backward pose");
            f.P.ChangeState(State.Standing);
            Require(!(bool)Call(Dive(f), "TryLaunch", Vector2.up, false),
                "Returning to an ordinary air state does not replenish the burst");
        }
    }
    private static void DiveCeiling()
    {
        using (var f = new Fixture(true, true))
        {
            // Begin below a low roof in a small test capsule; the launch must not tunnel upward.
            f.P.ChangeState(State.Crouching); f.P.UpdateHeightsAndTransform(0f);
            f.Box(new Vector3(0f, 1.05f, 1f), new Vector3(6f, 0.1f, 8f));
            f.P.LaunchDive(Vector2.up); f.MoveInput(Vector2.zero);
            float highest = f.P.transform.position.y;
            var eye = f.Player.GetComponentInChildren<Camera>();
            for (int i = 0; i < 70; i++)
            {
                f.Step(); highest = Mathf.Max(highest, f.P.transform.position.y);
                Require(eye.transform.position.y < f.Origin.y + 1.02f, "Dive eye stays below the low roof");
            }
            Require(highest - f.Origin.y < 0.65f && f.P.IsGrounded && !f.P.IsDiving,
                "Ceiling contact stops ascent without losing ground recovery");
        }
    }
}
