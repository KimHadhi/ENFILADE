using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunBalancePassageChecks()
    {
        Check("crouching beside or diagonally into walls never auto-prones", SideWallIsNotTunnel);
        Check("too-narrow or blocked prone routes cannot trigger automatic prone", BlockedProneRoute);
        Check("moving props above the head never trigger automatic prone", MovingRoofIsNotTunnel);
        Check("tackle completion under an obstruction never forces prone", TacklePosture);
        Check("high entry momentum cannot amplify dive travel indefinitely", DiveMomentumCap);
        Check("alternating step and dive remains slower than held sprint at 30/60/120Hz", EvadeTravelRace);
    }

    private static void SideWallIsNotTunnel()
    {
        foreach (Vector2 input in new[] { Vector2.up, new Vector2(0.3f, 1f), Vector2.right, Vector2.zero })
        using (var f = new Fixture(true))
        using (var crouch = HoldTestCrouch(f))
        {
            f.Box(new Vector3(0.3f, 1f, 0f), new Vector3(0.06f, 2f, 8f));
            CrouchForPassage(f); f.MoveInput(input);
            for (int i = 0; i < 20; i++) f.Step();
            Require(f.P.CurrentState == State.Crouching, "Side contact is not overhead clearance");
        }
    }
    private static void BlockedProneRoute()
    {
        using (var f = new Fixture(true))
        using (var crouch = HoldTestCrouch(f))
        {
            // Low roof but an opening narrower than the actual prone capsule.
            f.Box(new Vector3(0f, 1.1f, 0.6f), new Vector3(2f, 0.2f, 0.4f));
            foreach (int side in new[] { -1, 1 })
                f.Box(new Vector3(side * 0.245f, 0.5f, 0.6f), new Vector3(0.1f, 1f, 0.4f));
            CrouchForPassage(f); f.MoveInput(Vector2.up);
            Require(!f.P.HasVentCeilingAhead(Vector3.forward, 0.6f), "Prone path needs full body clearance");
            f.Step(); Require(f.P.CurrentState == State.Crouching, "Blocked low opening cannot force prone");
        }
    }
    private static void MovingRoofIsNotTunnel()
    {
        foreach (bool kinematic in new[] { false, true })
        using (var f = new Fixture(true))
        using (var crouch = HoldTestCrouch(f))
        {
            var prop = f.Box(new Vector3(0f, 1.05f, 0f), new Vector3(2f, 0.1f, 2f));
            var rb = prop.AddComponent<Rigidbody>(); rb.isKinematic = kinematic; rb.useGravity = false;
            CrouchForPassage(f); f.MoveInput(Vector2.up);
            for (int i = 0; i < 10; i++) f.Step();
            Require(f.P.CurrentState == State.Crouching, "A movable overhead prop never forces prone");
        }
    }
    private static void TacklePosture()
    {
        foreach (bool movingProp in new[] { false, true })
        using (var f = new Fixture(true))
        {
            var roof = f.Box(new Vector3(0f, 1.05f, 0f), new Vector3(3f, 0.1f, 3f));
            if (movingProp) { var rb = roof.AddComponent<Rigidbody>(); rb.useGravity = false; }
            f.P.ChangeState(State.Tackle); f.P.UpdateHeightsAndTransform(0f);
            Require(!f.P.HasVentCeilingAhead(Vector3.forward, 0f), "Auto-prone is disabled during tackle");
            f.P.CurrentStateDuration = 1f; f.Machine.Current.UpdateLogic(0.02f);
            Require(f.P.CurrentState == State.Crouching, "Tackle exits into crouch when its headroom is blocked");
            if (movingProp)
            {
                for (int i = 0; i < 10; i++) f.Step();
                Require(!f.P.IsProning, "Flying tackle target cannot force prone after the tackle ends either");
            }
        }
    }
    private static void DiveMomentumCap()
    {
        using (var f = new Fixture(true))
        {
            f.P.HorizontalVelocity = Vector3.forward * 30f;
            f.P.LaunchDive(Vector2.up);
            Require(f.P.HorizontalVelocity.magnitude <= 9.91f, "Dodge/impulse carry cannot inflate the next dive");
        }
    }
    private static void EvadeTravelRace()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        {
            float sprintDistance = 0f;
            foreach (bool combo in new[] { false, true })
            using (var f = new Fixture())
            using (var sprint = new InputAction("RaceSprint", InputActionType.Button, "<Gamepad>/leftTrigger"))
            {
                f.Box(new Vector3(0f, -0.1f, 50f), new Vector3(20f, 0.2f, 140f)); f.Ground();
                sprint.Enable(); Set(f.Input, "sprintAction", sprint); InputState.Change(f.Pad.leftTrigger, 1f);
                f.MoveInput(Vector2.up); f.P.SprintProgress = 1f; f.P.HorizontalVelocity = Vector3.forward * f.P.SprintSpeed;
                int steps = 0, dives = 0;
                for (float time = 0f; time < 8f - dt * 0.5f; time += dt)
                {
                    if (combo && f.P.IsGrounded && f.P.CurrentState == State.Standing)
                    {
                        if (Dodge(f).CanStart) { Input(f, "DodgePressed", true); steps++; }
                        else if ((bool)Call(Dive(f), "TryLaunch", Vector2.up, true)) dives++;
                    }
                    Call(Service(f.P, "Actions"), "ProcessSprintRampUp", dt, f.P.CurrentInput);
                    f.Step(dt);
                }
                float distance = f.P.transform.position.z - f.Origin.z;
                if (!combo) sprintDistance = distance;
                else
                {
                    Debug.Log("EVADE_COMBO_RACE: Hz=" + 1f / dt + " sprint=" + sprintDistance + " combo=" + distance + " steps=" + steps + " dives=" + dives);
                    Require(steps >= 3 && dives >= 3 && distance < sprintDistance * 0.95f, "Step/dive spam must lose to ordinary sprinting");
                }
            }
        }
    }
}
