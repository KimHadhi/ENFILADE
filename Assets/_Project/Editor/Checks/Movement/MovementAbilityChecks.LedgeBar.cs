using UnityEngine;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunLedgeBarChecks()
    {
        Check("ledge A/D shimmy follows support and stops at its end", LedgeShimmy);
        Check("ledge shimmy cannot pass an obstruction", ShimmyObstruction);
        Check("bar owns its colliders and wins over generic ledge catching", BarPriority);
        Check("neutral Space mantles a real bar; movement + Space releases", BarMantleControls);
        Check("blocked bar mantle preserves the grip", BlockedBarMantle);
        Check("airborne W directly traverses reachable obstacles", AirTraversal);
        Check("vault hands back control above edge without forced descent", VaultHandoff);
        Check("ledge Space selects mantle or view-directed jump at 42 degrees", LedgeJump);
        Check("vault follows reference pacing before air handoff", VaultBurst);
    }
    private static void CatchTestLedge(Fixture f)
    {
        f.Box(new Vector3(0f, -0.9f, 0.85f), new Vector3(4f, 5f, 0.5f));
        f.P.VerticalVelocity = -5f; f.Step();
        for (int i = 0; i < 10; i++) f.Step();
        Require(f.P.CurrentState == State.LedgeHang, "Fixture caught ledge");
    }
    private static void LedgeShimmy()
    {
        using (var f = new Fixture())
        {
            CatchTestLedge(f); Vector3 start = f.P.transform.position;
            f.MoveInput(Vector2.right); for (int i = 0; i < 25; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang && f.P.transform.position.x > start.x + 0.65f, "D shimmies right");
            Near(f.P.transform.position.y, start.y, "Shimmy keeps grip height");
            f.MoveInput(Vector2.left); for (int i = 0; i < 25; i++) f.Step();
            Near(f.P.transform.position.x, start.x, "A shimmies back", 0.1f);
            f.MoveInput(Vector2.right); for (int i = 0; i < 110; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang && f.P.transform.position.x < f.Origin.x + 2.1f, "Cannot shimmy across missing support");
            f.MoveInput(Vector2.zero); f.Step(); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.Mantle, "Can mantle from shifted grip");
        }
    }
    private static void ShimmyObstruction()
    {
        using (var f = new Fixture())
        {
            CatchTestLedge(f);
            f.Box(new Vector3(0.9f, 0.6f, 0.1f), new Vector3(0.2f, 2f, 1f));
            f.MoveInput(Vector2.right); for (int i = 0; i < 40; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang && f.P.transform.position.x < f.Origin.x + 0.6f, "Side pillar stops shimmy without dropping");
        }
    }
    private static SwingBar PhysicalBar(Fixture f)
    {
        SwingBar bar = MakeBar(f);
        var rail = f.Box(Vector3.up * 2f, new Vector3(2.5f, 0.12f, 0.12f));
        rail.transform.SetParent(bar.transform, true); Physics.SyncTransforms();
        return bar;
    }
    private static void BarPriority()
    {
        using (var f = new Fixture())
        {
            var bar = MakeBar(f); bar.transform.position = f.Origin + new Vector3(0f, 1.6f, 0.85f);
            var rail = f.Box(new Vector3(0f, 1.25f, 0.85f), new Vector3(3f, 0.7f, 0.5f)); rail.transform.SetParent(bar.transform, true);
            Physics.SyncTransforms();
            var detector = (ParkourDetector)Service(Service(f.Abilities, "Traversal"), "Detector");
            Require(!detector.TryLedge(Vector3.forward, out _), "Bar collider must never become a generic ledge");
            f.MoveInput(Vector2.up); f.P.VerticalVelocity = -2f; f.Step();
            Require(f.P.CurrentState == State.BarGrab, "Approaching bar prefers swing grab without held Space");
        }
    }
    private static void SettleOnBar(Fixture f)
    {
        PhysicalBar(f); Input(f, "JumpHeld", true); f.Step();
        for (int i = 0; i < 12; i++) f.Step();
        Input(f, "JumpHeld", false);
        Require(f.P.CurrentState == State.BarSwing, "Settled on physical rail");
    }
    private static void BarMantleControls()
    {
        using (var f = new Fixture())
        {
            SettleOnBar(f); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.Mantle, "Neutral Space climbs onto bar");
            for (int i = 0; i < 65; i++) f.Step();
            Require(!f.P.IsParkour && f.P.IsGrounded && f.P.transform.position.y > f.Origin.y + 2f, "Standing on real rail after mantle");
        }
        using (var f = new Fixture())
        {
            SettleOnBar(f); f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.LacheRelease && f.P.VerticalVelocity > 0f, "Move + Space retains lache");
        }
    }
    private static void BlockedBarMantle()
    {
        using (var f = new Fixture())
        {
            SettleOnBar(f); f.Box(new Vector3(0f, 3f, 0f), new Vector3(3f, 0.2f, 2f));
            Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.BarSwing, "Blocked neutral-Space mantle must not accidentally launch");
        }
    }
    private static void AirTraversal()
    {
        foreach (bool broad in new[] { false, true })
        foreach (float vertical in new[] { -3f, 4f })
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f));
            f.Box(broad ? new Vector3(0f, 0.7f, 2.6f) : new Vector3(0f, 0.7f, 1.2f),
                broad ? new Vector3(3f, 1.4f, 3f) : new Vector3(3f, 1.4f, 0.4f));
            f.P.transform.position += Vector3.up * 0.65f; f.P.VerticalVelocity = vertical;
            f.MoveInput(Vector2.up); Physics.SyncTransforms(); f.Step();
            Require(f.P.CurrentState == (broad ? State.Mantle : State.Vault), "Air W skips hang for a low reachable top: " + f.P.CurrentState);
        }
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, -0.9f, 0.85f), new Vector3(4f, 5f, 0.5f));
            f.P.VerticalVelocity = -4f; f.MoveInput(Vector2.up); f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Hand-height ledges still catch rather than auto climb");
        }
    }
    private static void VaultHandoff()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.P.HorizontalVelocity = Vector3.forward * 9f; f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            float previousY = f.P.transform.position.y;
            for (int i = 0; i < 25 && f.P.CurrentState == State.Vault; i++)
            {
                f.Step();
                if (f.P.CurrentState == State.Vault)
                {
                    Require(f.P.transform.position.y >= previousY - 0.005f, "No scripted down-arc during vault");
                    Require(f.P.transform.position.y < f.Origin.y + 1.08f, "No exaggerated apex above clearance");
                }
                previousY = f.P.transform.position.y;
            }
            Require(!f.P.IsParkour && f.P.transform.position.y > f.Origin.y + 0.7f, "Hand control back before landing");
            Near(f.P.HorizontalVelocity.magnitude, 5f, "Vault2 hands back reference forward speed", 0.3f);
        }
    }
    private static void LedgeJump()
    {
        foreach (float angle in new[] { 0f, 41f, 43f, 90f, 180f })
        using (var f = new Fixture())
        {
            CatchTestLedge(f); Vector3 start = f.P.transform.position;
            f.P.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            Input(f, "JumpPressed", true); f.Step();
            if (angle <= 42f)
            {
                Require(f.P.CurrentState == State.Mantle, "Facing ledge Space still mantles: " + angle);
                continue;
            }
            Require(f.P.CurrentState == State.LedgeJump && f.P.VerticalVelocity > 5f, "Looking away Space launches upward: " + angle);
            Require(f.P.HorizontalVelocity.z < -0.1f && f.P.HorizontalVelocity.magnitude > 8f, "Jump separates from wall with momentum");
            if (angle < 180f) Require(f.P.HorizontalVelocity.x > 5f, "Side jump follows view direction");
            for (int i = 0; i < 8; i++) f.Step();
            Require(f.P.CurrentState != State.LedgeHang && f.P.transform.position.y > start.y + 0.4f, "Jump does not immediately regrab");
            f.P.ResetVelocity(); Require(f.P.Velocity == Vector3.zero && !f.P.IsParkour, "Reset clears ledge launch");
        }
    }
    private static void VaultBurst()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.P.HorizontalVelocity = Vector3.forward * 9f;
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            float initialSpeed = f.P.Velocity.magnitude;
            Require(initialSpeed > 1f && initialSpeed < 25f, "Vault no longer has exaggerated initial acceleration: " + initialSpeed);
            int frames = 1;
            while (f.P.CurrentState == State.Vault && frames < 25) { f.Step(); frames++; }
            Require(frames * 0.02f >= 0.18f && frames * 0.02f <= 0.5f, "Reference vault timing: " + frames * 0.02f);
            Require(f.P.transform.position.y > f.Origin.y + 0.7f, "Reference handoff occurs before landing");
            for (int i = 0; i < 6; i++) f.Step();
            Require(f.P.transform.position.z > f.Origin.z + 1.6f, "Returned momentum carries player past obstacle");
        }
    }
}
