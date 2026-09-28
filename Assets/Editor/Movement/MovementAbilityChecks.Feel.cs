using UnityEngine;
using Object = UnityEngine.Object;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunFeelChecks()
    {
        Check("downhill slide support and acceleration at 30/60/120 Hz", DownhillSlide);
        Check("slide drops from cliff and jump detaches", SlideDetach);
        Check("falling catch waits, fresh W mantles a narrow wall", FallingLedge);
        Check("climb five-metre wall then hang, Space mantles", ClimbLedge);
        Check("ledge drop cooldown and destroyed support", LedgeRelease);
        Check("blocked mantle remains hanging", BlockedLedge);
        Check("side walls no longer start a wall run", RemovedWallRun);
        Check("rounded vault and mantle routes have continuous progress", SmoothVault);
        Check("real short drops safe; high fall damages and roll prevents it", RealFalls);
        Check("wall actions ignore release, view and crouch cancellation", CommittedWallActions);
        Check("fast traversal completes safely across speeds and frame rates", FastTraversal);
    }
    private static void StartSlide(Fixture f)
    {
        f.P.ChangeState(State.Sliding); f.P.SlideDirection = Vector3.forward;
        f.P.CurrentSlideSpeed = 10f; f.P.HorizontalVelocity = Vector3.forward * 10f;
    }
    private static void DownhillSlide()
    {
        foreach (float angle in new[] { 25f, 45f, 55f })
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        using (var f = new Fixture())
        {
            var ramp = f.Box(Vector3.zero, new Vector3(20f, 1f, 100f));
            ramp.transform.rotation = Quaternion.Euler(angle, 0f, 0f);
            ramp.transform.position = f.Origin - ramp.transform.up * 0.5f;
            f.P.transform.position += Vector3.up * 0.3f; Physics.SyncTransforms();
            f.Player.GetComponent<CharacterController>().Move(Vector3.down * 0.7f);
            f.P.VerticalVelocity = -2f; Call(f.Motor, "CheckGroundedAndSlope");
            StartSlide(f);
            // Steep support is evaluated using the slide-specific slope limit.
            Set(f.Motor, "isGrounded", true);
            int airborne = 0;
            for (int i = 0; i < Mathf.RoundToInt(0.8f / dt); i++)
            {
                f.Step(dt); if (!f.P.IsGrounded) airborne++;
                Vector3 p = f.P.transform.position - f.Origin;
                float gap = p.y + p.z * Mathf.Tan(angle * Mathf.Deg2Rad);
                Require(gap < 0.35f, "Slide floats above ramp: " + angle + " / " + dt + " gap=" + gap);
            }
            Require(airborne == 0 && f.P.IsSliding, "Slide loses support: " + angle + " / " + dt + " airborne=" + airborne);
            Require(f.P.CurrentSlideSpeed > 11f && f.P.transform.position.y < f.Origin.y - 2f, "Downhill should accelerate and descend");
        }
    }
    private static void SlideDetach()
    {
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, -0.2f, -1f), new Vector3(10f, 0.4f, 4f)); f.Ground(); StartSlide(f);
            for (int i = 0; i < 20; i++) f.Step();
            Require(!f.P.IsGrounded && f.P.transform.position.y < f.Origin.y - 0.1f, "Cliff must release ground adhesion");
        }
        using (var f = new Fixture(true))
        {
            StartSlide(f); for (int i = 0; i < 30; i++) f.Step();
            Input(f, "JumpPressed", true); f.Step();
            Require(!f.P.IsGrounded && f.P.VerticalVelocity > 0f, "Slide jump must detach from floor");
        }
    }
    private static void FallingLedge()
    {
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, -0.9f, 0.85f), new Vector3(4f, 5f, 0.5f));
            f.P.VerticalVelocity = -5f; f.MoveInput(Vector2.up); f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Descending player should auto catch reachable lip: " + f.P.CurrentState);
            for (int i = 0; i < 30; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Holding W from entry must not auto mantle");
            f.MoveInput(Vector2.zero); f.Step(); f.MoveInput(Vector2.up); f.Step();
            Require(f.P.CurrentState == State.Mantle, "Fresh W should mantle");
            f.MoveInput(Vector2.zero); for (int i = 0; i < 45; i++) f.Step();
            Require(!f.P.IsParkour && f.P.transform.position.y > f.Origin.y + 1.5f, "Narrow wall mantle should finish on top");
        }
    }
    private static void ClimbLedge()
    {
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, 2.5f, 0.85f), new Vector3(15f, 5f, 0.5f));
            f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Start climb");
            for (int i = 0; i < 75 && f.P.CurrentState != State.LedgeHang; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Five metre lane should reach hang: " + f.P.CurrentState);
            for (int i = 0; i < 15; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Climbing must stop at lip until new input");
            Input(f, "JumpHeld", false); f.Step(); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.Mantle, "Space mantles from hang");
            f.MoveInput(Vector2.zero); for (int i = 0; i < 45; i++) f.Step();
            Require(!f.P.IsParkour && f.P.transform.position.y > f.Origin.y + 4.9f, "Climb + mantle reaches wall top");
        }
    }
    private static void LedgeRelease()
    {
        using (var f = new Fixture())
        {
            var wall = f.Box(new Vector3(0f, -0.9f, 0.85f), new Vector3(4f, 5f, 0.5f));
            f.P.VerticalVelocity = -5f; f.Step(); for (int i = 0; i < 10; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Catch before release");
            Input(f, "CrouchPressed", true); f.Step();
            for (int i = 0; i < 5; i++) f.Step();
            Require(!f.P.IsParkour && f.P.VerticalVelocity < 0f, "Drop does not immediately regrab");
            f.P.ResetVelocity(); f.P.transform.position = f.Origin; Physics.SyncTransforms();
            Input(f, "CrouchHeld", true); f.Step();
            Require(!f.P.IsParkour, "Holding Ctrl suppresses automatic ledge catching");
            Input(f, "CrouchHeld", false);
            f.P.ResetVelocity(); f.P.transform.position = f.Origin; Physics.SyncTransforms();
            f.Step(); Require(f.P.CurrentState == State.LedgeHang, "Reset allows new catch");
            Object.DestroyImmediate(wall); f.Step(); Require(!f.P.IsParkour, "Deleted ledge releases safely");
        }
    }
    private static void BlockedLedge()
    {
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, -0.9f, 0.85f), new Vector3(4f, 5f, 0.5f)); f.Step();
            for (int i = 0; i < 10; i++) f.Step();
            f.Box(new Vector3(0f, 2.3f, 0.9f), new Vector3(4f, 0.3f, 0.6f));
            Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Blocked headroom refuses mantle without dropping grip");
        }
    }
    private static void RemovedWallRun()
    {
        foreach (bool grounded in new[] { false, true })
        using (var f = new Fixture(grounded))
        {
            f.Box(new Vector3(0.7f, 2f, 3f), new Vector3(0.2f, 10f, 20f));
            f.P.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            f.MoveInput(Vector2.up); f.P.HorizontalVelocity = Vector3.forward * 7f; f.Step();
            Require(!f.P.IsParkour, "Airborne/grounded W near a side wall never starts wall movement");
        }
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, 3f, 0.7f), new Vector3(4f, 10f, 0.2f));
            f.MoveInput(Vector2.up); f.Step();
            Require(!f.P.IsParkour, "Head-on W alone does not attach");
            Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Head-on W + held Space still climbs");
        }
    }
    private static void SmoothVault()
    {
        foreach (bool mantle in new[] { false, true })
        using (var f = new Fixture(true))
        {
            if (mantle) f.Box(new Vector3(0f, 0.8f, 2.6f), new Vector3(3f, 1.6f, 3f));
            else f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            Vector3 previous = f.P.transform.position;
            float minimumSpeed = float.MaxValue;
            State expected = mantle ? State.Mantle : State.Vault;
            Require(f.P.CurrentState == expected, "Smooth route must start selected action");
            int frames = 12;
            for (int i = 0; i < frames && f.P.CurrentState == expected; i++)
            {
                f.Step(); float speed = Vector3.Distance(previous, f.P.transform.position) / 0.02f;
                if (i > 2 && f.P.CurrentState == expected) minimumSpeed = Mathf.Min(minimumSpeed, speed);
                previous = f.P.transform.position;
            }
            Require(minimumSpeed > 1.5f, expected + " must not stop at internal phase boundaries: " + minimumSpeed);
        }
    }
    private static void CommittedWallActions()
    {
        using (var f = new Fixture())
        {
            var wall = f.Box(new Vector3(0f, 3f, 0.7f), new Vector3(4f, 12f, 0.2f));
            f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Climb starts");
            f.MoveInput(Vector2.zero); Input(f, "JumpHeld", false);
            f.P.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Input(f, "CrouchHeld", true); Input(f, "CrouchPressed", true);
            for (int i = 0; i < 20; i++) f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Release/turn/Ctrl keeps climb");
            Object.DestroyImmediate(wall); f.Step();
            Require(!f.P.IsParkour, "Missing wall releases safely");
        }
    }
    private static void FastTraversal()
    {
        foreach (bool mantle in new[] { false, true })
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        foreach (float speed in new[] { 0f, 12f, 20f })
        foreach (float approach in new[] { 0f, 0.55f })
        using (var f = new Fixture(true))
        {
            if (mantle) f.Box(new Vector3(0f, 0.8f, 2.6f), new Vector3(3f, 1.6f, 3f));
            else f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.P.transform.position += Vector3.forward * approach; Physics.SyncTransforms();
            f.P.HorizontalVelocity = Vector3.forward * speed;
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step(dt);
            State expected = mantle ? State.Mantle : State.Vault;
            string label = expected + " dt=" + dt + " speed=" + speed + " approach=" + approach;
            Require(f.P.CurrentState == expected, "Traversal starts: " + label);
            Require(f.P.Velocity.magnitude > 1f, "Scripted movement reports real velocity for camera: " + label);
            if (!mantle && approach == 0f)
                Require(f.P.transform.position.z > f.Origin.z + 0.002f, "Vault moves forward immediately: " + label);
            float time = dt;
            float maximum = mantle ? 1.2f : 0.75f;
            while (f.P.CurrentState == expected && time < maximum + 3f * dt)
            { f.Step(dt); time += dt; }
            Require(!f.P.IsParkour && time >= 0.12f && time <= maximum + 2.1f * dt, "Traversal respects reference pacing: " + label + " time=" + time);
            if (mantle && speed > 0f) Require(f.P.HorizontalVelocity.magnitude >= speed * 0.85f, "High mantle retains momentum: " + label);
            if (!mantle) Near(f.P.HorizontalVelocity.magnitude, 5f, "Vault2 reference exit speed: " + label, 0.5f);
            if (!mantle) for (int i = 0; i < 10; i++) f.Step(dt);
            Require(mantle ? f.P.transform.position.y > f.Origin.y + 1.45f : f.P.transform.position.z > f.Origin.z + 1.4f,
                "Traversal reaches destination, not an early abort: " + label);
        }
    }
    private static void RealFalls()
    {
        foreach (float height in new[] { 2f, 4f, 8f })
        foreach (bool roll in new[] { false, true })
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f));
            f.P.transform.position += Vector3.up * height; Physics.SyncTransforms();
            for (int i = 0; i < 160 && !f.P.IsGrounded && f.P.CurrentState != State.LandingRoll; i++)
            {
                if (roll && f.P.transform.position.y < f.Origin.y + 1.5f) Input(f, "CrouchPressed", true);
                f.Step();
            }
            if (height <= 4f || roll) Near(f.P.DamageReceiver.Health, 100f, "Safe drop or timed roll: " + height);
            else Require(f.P.DamageReceiver.Health > 0f && f.P.DamageReceiver.Health < 100f, "Eight metre fall hurts without instant death");
        }
    }
}
