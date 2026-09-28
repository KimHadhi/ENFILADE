using UnityEngine;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunParkourFacingChecks()
    {
        Check("turning during vault preserves exit direction and cannot repeat before landing", VaultTurnaround);
        Check("different obstacles remain chainable and reset clears traversal history", VaultDifferentObstacle);
        Check("looking away from ledge stops shimmy and prepares directional jump", LedgeLookAway);
    }

    private static void FinishTurnedVault(Fixture f, float dt)
    {
        f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
        f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step(dt);
        Require(f.P.CurrentState == State.Vault, "Entered vault");
        f.P.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        for (int i = 0; i < 100 && f.P.CurrentState == State.Vault; i++) f.Step(dt);
        Require(!f.P.IsParkour && !f.P.IsGrounded, "Vault handed back airborne control");
        Require(f.P.HorizontalVelocity.z > 0f, "Looking back cannot reverse exit impulse");
        Near(Mathf.Abs(Mathf.DeltaAngle(0f, f.P.transform.eulerAngles.y)), 180f, "View remains free");
    }

    private static void PlaceForReverseVault(Fixture f)
    {
        // A reachable reverse approach to the same collider, without landing/reset.
        f.P.transform.position = f.Origin + new Vector3(0f, 0.25f, 2.15f);
        f.P.HorizontalVelocity = Vector3.back * 2f; f.P.VerticalVelocity = -1f;
        f.P.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Physics.SyncTransforms();
    }

    private static void VaultTurnaround()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        using (var f = new Fixture(true))
        {
            FinishTurnedVault(f, dt); PlaceForReverseVault(f);
            var traversal = Service(f.Abilities, "Traversal");
            var detector = (ParkourDetector)Service(traversal, "Detector");
            Require(detector.TryObstacle(Vector3.back, out _, true), "Reverse obstacle remains geometrically reachable");
            Require(!(bool)Call(traversal, "TryObstacle", true), "Air auto-vault cannot repeat same obstacle");
            Input(f, "JumpPressed", true);
            Require(!(bool)Call(traversal, "TryObstacle", false), "Fresh jump cannot bypass repeat guard");
            Input(f, "JumpPressed", false);
            f.MoveInput(Vector2.zero); f.P.HorizontalVelocity = Vector3.zero;
            for (int i = 0; i < 100 && !f.P.IsGrounded; i++) f.Step(dt);
            Require(f.P.IsGrounded, "Actual landing restores obstacle eligibility");
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step(dt);
            Require(f.P.CurrentState == State.Vault, "Same obstacle is available again after landing");
        }
    }

    private static void VaultDifferentObstacle()
    {
        using (var f = new Fixture(true))
        {
            FinishTurnedVault(f, 0.02f);
            f.Box(new Vector3(0f, 0.45f, 4.5f), new Vector3(3f, 0.9f, 0.4f));
            f.P.transform.position = f.Origin + new Vector3(0f, 0.25f, 3.3f);
            f.P.transform.rotation = Quaternion.identity;
            f.P.VerticalVelocity = -1f; Physics.SyncTransforms(); f.Step();
            Require(f.P.CurrentState == State.Vault, "A distinct obstacle can chain without landing");
            f.P.ResetVelocity(); PlaceForReverseVault(f);
            Require((bool)Call(Service(f.Abilities, "Traversal"), "TryObstacle", true), "Reset clears old obstacle history");
        }
    }

    private static void LedgeLookAway()
    {
        foreach (float angle in new[] { -180f, -90f, -43f, 43f, 90f, 180f })
        using (var f = new Fixture())
        {
            CatchTestLedge(f); Vector3 grip = f.P.transform.position;
            f.P.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            foreach (Vector2 input in new[] { Vector2.left, Vector2.right, Vector2.one })
            {
                f.MoveInput(input); for (int i = 0; i < 15; i++) f.Step();
                Require(f.P.CurrentState == State.LedgeHang, "Looking away cannot auto-mantle");
                Near(Vector3.Distance(grip, f.P.transform.position), 0f, "Jump preparation keeps grip fixed", 0.005f);
            }
            f.P.transform.rotation = Quaternion.Euler(0f, 41f, 0f);
            f.MoveInput(Vector2.right); for (int i = 0; i < 10; i++) f.Step();
            Require(f.P.transform.position.x > grip.x + 0.2f, "Looking back at ledge resumes shimmy");
            f.P.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            f.MoveInput(Vector2.zero); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.LedgeJump && f.P.VerticalVelocity > 5f, "Space launches from jump preparation");
            Require(f.P.HorizontalVelocity.z < -0.1f, "Jump separates from wall");
            if (Mathf.Abs(angle) < 180f)
                Require(Mathf.Sign(f.P.HorizontalVelocity.x) == Mathf.Sign(angle), "Jump follows side of view");
        }
    }
}
