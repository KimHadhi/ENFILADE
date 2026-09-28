using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunLowPassageChecks()
    {
        Check("one-metre passage lowers early, traverses without posture flicker and permits exit", LowPassageEntry);
        Check("tight crouch clearance blocks jump and Ctrl get-up while taller passages permit crouching", LowPassageClearance);
        Check("vent entry rejects solid walls and blocked prone routes, including thin lintels", LowPassageObstructions);
        Check("rising checks crouch width instead of the current prone capsule radius", CrouchWidthClearance);
    }

    private static InputAction HoldTestCrouch(Fixture f)
    {
        var action = new InputAction("TestCrouch", InputActionType.Button, "<Gamepad>/leftTrigger");
        action.Enable(); Set(f.Input, "crouchAction", action);
        InputState.Change(f.Pad.leftTrigger, 1f);
        return action;
    }

    private static void CrouchForPassage(Fixture f)
    {
        f.P.ChangeState(State.Crouching); f.P.UpdateHeightsAndTransform(0f);
        f.Ground(); Physics.SyncTransforms();
    }

    private static void LowPassageEntry()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        using (var f = new Fixture(true))
        using (var crouch = HoldTestCrouch(f))
        {
            // Opening starts at z=.75 and ends at 2.75, with a 1m clear height.
            f.Box(new Vector3(0f, 1.15f, 1.75f), new Vector3(3f, 0.3f, 2f));
            CrouchForPassage(f); f.MoveInput(Vector2.up); f.Step(dt);
            Require(f.P.CurrentState == State.Proning && f.P.transform.position.z < f.Origin.z + 0.2f,
                "Prone starts before the head reaches the entrance");
            float elapsed = 0f;
            while (f.P.transform.position.z < f.Origin.z + 2.85f && elapsed < 4f)
            {
                f.Step(dt); elapsed += dt;
                Require(f.P.CurrentState == State.Proning, "Hold Ctrl does not oscillate between prone and crouch inside");
            }
            Require(elapsed < 4f, "Passage entry and transit cannot stick");
            for (int i = 0; i < Mathf.CeilToInt(1f / dt); i++) f.Step(dt);
            Require(f.P.CurrentState == State.Crouching && f.P.transform.position.z > f.Origin.z + 3.1f,
                "Once the full crouch capsule clears the exit, held Ctrl restores crouch");
        }
    }

    private static void LowPassageClearance()
    {
        foreach (float clearance in new[] { 0.8f, 1f, 1.12f })
        using (var f = new Fixture(true))
        using (var crouch = HoldTestCrouch(f))
        {
            f.Box(new Vector3(0f, clearance + 0.15f, 0f), new Vector3(4f, 0.3f, 4f));
            f.P.ChangeState(State.Proning); f.P.UpdateHeightsAndTransform(0f); f.Ground();
            bool blocked = clearance <= 1f;
            Require(f.P.HasCeilingAbove(f.P.CrouchColliderHeight) == blocked, "Headroom policy: " + clearance);
            Require(f.P.HasCeilingAhead(Vector3.forward, 0.35f, f.P.CrouchColliderHeight) == blocked,
                "Ahead and current clearance must agree");
            if (blocked)
            {
                Require(!f.Machine.Current.TryJump(), "Space cannot stand into a tight ceiling");
                for (int i = 0; i < 20; i++) f.Step();
                Require(f.P.CurrentState == State.Proning, "Ctrl cannot restore unsafe crouch");
                f.P.ChangeState(State.Crouching); f.P.UpdateHeightsAndTransform(0f);
                f.Step(); Require(f.P.CurrentState == State.Proning, "Already crouched under a tight ceiling lowers even without movement");
            }
            else
            {
                Require(f.Machine.Current.TryJump() && f.P.CurrentState == State.Crouching,
                    "Comfortably fitting passages still allow crouch get-up");
                for (int i = 0; i < 15; i++) f.Step();
                Require(f.P.CurrentState == State.Crouching, "Comfortable headroom does not force prone");
            }
        }
    }

    private static void LowPassageObstructions()
    {
        foreach (bool solidWall in new[] { true, false })
        using (var f = new Fixture(true))
        using (var crouch = HoldTestCrouch(f))
        {
            f.Box(solidWall ? new Vector3(0f, 1f, 0.8f) : new Vector3(0f, 0.45f, 0.8f),
                new Vector3(3f, solidWall ? 2f : 0.3f, 0.2f));
            CrouchForPassage(f);
            Require(!f.P.HasVentCeilingAhead(Vector3.forward, 0.6f), "Solid/too-low entrance cannot be called traversable");
            f.MoveInput(Vector2.up); f.Step();
            Require(f.P.CurrentState == State.Crouching, "Cannot auto-prone at a blocked passage");
        }
        using (var f = new Fixture(true))
        {
            // Both endpoint probes can be clear while the upper body crosses a lintel.
            f.Box(new Vector3(0f, 1.075f, 0.6f), new Vector3(3f, 0.1f, 0.03f));
            CrouchForPassage(f);
            Require(f.P.HasVentCeilingAhead(Vector3.forward, 1.2f), "Sweep catches a thin lintel between endpoint samples");
        }
    }

    private static void CrouchWidthClearance()
    {
        using (var f = new Fixture(true))
        {
            f.P.ChangeState(State.Proning); f.P.UpdateHeightsAndTransform(0f); f.Ground();
            // Shoulder obstruction outside the prone radius, inside the target crouch radius.
            f.Box(new Vector3(0.29f, 0.62f, 0f), new Vector3(0.08f, 0.25f, 1f));
            Require(f.P.HasCeilingAbove(f.P.CrouchColliderHeight), "Crouch query restores the wider target capsule");
            Require(!f.Machine.Current.TryJump() && f.P.CurrentState == State.Proning,
                "Prone get-up cannot expand sideways into the obstruction");
        }
    }
}
