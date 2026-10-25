using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunContinuityChecks()
    {
        Check("held sprint resumes after step without another press; release still stops sprint", SprintThroughStep);
        Check("step cooldown lasts 1.25 seconds unless a real evade refunds it", StepCooldown);
        Check("lean reveals more of the side without moving the player and clamps the final eye at walls", StrongLean);
        Check("dive drag has no abrupt onset and retains forward travel through descent", ContinuousDiveDrag);
        Check("W gets up only after supine slide ends; supine A/D moves sideways", SupineControls);
    }

    private static void SupineControls()
    {
        foreach (State state in new[] { State.SupineSliding, State.Supine })
        {
            foreach (float side in new[] { -1f, 1f })
            using (var f = new Fixture(true))
            {
                f.P.ChangeState(state);
                f.P.SlideDirection = Vector3.back; f.P.CurrentSlideSpeed = 6f;
                f.MoveInput(new Vector2(side, 0f)); f.Step();
                Require(f.P.CurrentState == state && f.P.HorizontalVelocity.x * side > 0.01f,
                    "A/D moves toward the viewed side without getting up");
                f.MoveInput(Vector2.up); f.Step();
                Require(f.P.CurrentState == (state == State.Supine ? State.Standing : State.SupineSliding),
                    "W stands without Shift only in resting supine, never during sliding");
            }
            foreach (float roofHeight in new[] { 0.7f, 1.3f })
            using (var f = new Fixture(true))
            {
                f.P.ChangeState(state); f.P.SlideDirection = Vector3.back; f.P.CurrentSlideSpeed = 6f;
                f.P.UpdateHeightsAndTransform(0f);
                f.Box(new Vector3(0f, roofHeight + 0.1f, 0f), new Vector3(4f, 0.2f, 4f));
                f.MoveInput(Vector2.up); f.Machine.Current.ProcessInput(Vector2.up);
                Require(f.P.CurrentState == (state == State.SupineSliding || roofHeight < 1f ? state : State.Crouching),
                    "W cannot rise through ceiling and crouches if only crouch fits");
            }
        }
    }

    private static void SprintThroughStep()
    {
        foreach (bool release in new[] { false, true })
        using (var f = new Fixture(true))
        using (var sprint = new InputAction("HeldSprint", InputActionType.Button, "<Gamepad>/leftTrigger"))
        {
            sprint.Enable(); Set(f.Input, "sprintAction", sprint);
            InputState.Change(f.Pad.leftTrigger, 1f); f.MoveInput(Vector2.up);
            f.P.SprintProgress = 1f; f.P.HorizontalVelocity = Vector3.forward * f.P.SprintSpeed;
            Input(f, "DodgePressed", true);
            for (int i = 0; i < 18; i++)
            {
                if (release && i == 2) InputState.Change(f.Pad.leftTrigger, 0f);
                if (!release && i == 3)
                {
                    sprint.Disable();
                    Require(!(bool)Service(f.Input, "SprintButtonHeld"), "Disabled sprint bindings stay disabled");
                    sprint.Enable();
                    Require((bool)Service(f.Input, "SprintButtonHeld"), "Held binding survives an action reset without a fresh press");
                }
                // Include the sprint phase in exactly the same order as PlayerMovement.Update.
                Call(Service(f.P, "Actions"), "ProcessSprintRampUp", 0.02f, f.P.CurrentInput);
                f.Step();
            }
            Require(f.P.CurrentState == State.Standing, "Step returns to normal movement");
            if (release)
                Require(f.P.SprintProgress < 0.05f && f.P.HorizontalVelocity.magnitude < 6f, "Releasing Shift during dodge does not latch sprint");
            else
            {
                Near(f.P.SprintProgress, 1f, "Holding Shift preserves full sprint progress");
                Near(f.P.HorizontalVelocity.z, f.P.SprintSpeed, "Forward sprint continues on exit without repressing Shift", 0.15f);
            }
        }
    }

    private static void StepCooldown()
    {
        using (var f = new Fixture(true))
        {
            Input(f, "DodgePressed", true); f.Step();
            for (int i = 0; i < 30; i++) f.Step();
            Require(!Dodge(f).CanStart && Dodge(f).CooldownRemaining > 0.3f, "A missed step cannot restart at the old 0.55s cooldown");
            Input(f, "DodgePressed", true); f.Step();
            Require(f.P.CurrentState != State.StepDodge, "Early attempt is rejected");
            for (int i = 0; i < 21; i++) f.Step();
            Require(!Dodge(f).CanStart, "Step is still cooling down after one second");
            for (int i = 0; i < 12; i++) f.Step();
            Require(Dodge(f).CanStart, "1.25-second cooldown expires");
            Input(f, "DodgePressed", true); f.Step();
            Require(f.P.DamageReceiver.ReceiveHit(Attack()) == DamageResult.Evaded && Dodge(f).CanStart,
                "Successful step still immediately refunds the shared cooldown");
        }
    }

    private static void StrongLean()
    {
        foreach (int side in new[] { -1, 1 })
        using (var f = new Fixture(true, true))
        {
            var camera = f.Player.GetComponentInChildren<Camera>();
            var effects = f.Player.AddComponent<PlayerCameraEffects>(); Call(effects, "Awake");
            Vector3 body = f.P.transform.position; float eyeHeight = camera.transform.position.y;
            Input(f, "LeanSide", side); Input(f, "LeanPressed", true);
            for (int i = 0; i < 25; i++) f.Step();
            Call(effects, "ApplyCameraRotation"); Call(effects, "ApplyLeanPosition");
            Require(Mathf.Abs(camera.transform.position.x - body.x) > 0.34f && Mathf.Abs(f.P.LeanRoll) > 9f && Mathf.Abs(f.P.LeanRoll) <= 10f,
                "Lean retains side visibility with a restrained roll");
            Near(camera.transform.position.y, eyeHeight, "Rolling the view does not pull the eye down", 0.02f);
            Near(f.P.transform.position.x, body.x, "Pressing Q/E alone never moves the body", 0.001f);
            // Add a wall to an already extended eye; the final camera pass must clamp immediately.
            f.Box(new Vector3(side * 0.32f, 1.2f, 0f), new Vector3(0.04f, 2.4f, 4f));
            Call(effects, "ApplyLeanPosition");
            Require(Mathf.Abs(camera.transform.position.x - body.x) < 0.22f, "Held lean cannot look through a newly approached wall");
        }
    }

    private static void ContinuousDiveDrag()
    {
        using (var f = new Fixture(true, true))
        {
            f.P.LaunchDive(Vector2.up); f.MoveInput(Vector2.zero);
            float previous = f.P.HorizontalVelocity.magnitude, previousLoss = 0f;
            for (int i = 0; i < 20; i++)
            {
                f.Step(); float speed = f.P.HorizontalVelocity.magnitude;
                float loss = previous - speed;
                Require(loss >= -0.01f && loss < 0.5f, "No single flight tick brakes sharply");
                Require(Mathf.Abs(loss - previousLoss) < 0.14f, "Drag grows continuously across the old burst boundary");
                previous = speed; previousLoss = loss;
            }
            Require(f.P.HorizontalVelocity.magnitude > 4f, "Descent still travels forward rather than dropping nearly straight down");
        }
    }
}
