using UnityEngine;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunEnemySurfaceChecks()
    {
        Check("enemy owners and child colliders cannot start wall climbing", EnemyWallEntry);
        Check("enemy owners and child colliders cannot become ledge grips", EnemyLedgeEntry);
        Check("cached wall loses support when tagged or reparented into an enemy", EnemyWallSupportChange);
        Check("cached ledge loses support when tagged or reparented into an enemy", EnemyLedgeSupportChange);
        Check("enemy head geometry still permits vaulting across", EnemyVaultAcross);
        Check("enemy head treads retain the reference onto-head traversal", EnemyVaultOnto);
    }

    // Identity belongs to the actor, while the queried physical surface is a child collider.
    // -1 creates an unmarked owner for the support-change checks.
    private static GameObject EnemySurfaceOwner(Fixture f, GameObject surface, int identity)
    {
        var owner = new GameObject("Enemy surface owner");
        owner.transform.SetParent(f.Root.transform, false);
        owner.transform.position = surface.transform.position;
        surface.transform.SetParent(owner.transform, true);
        if (identity == 0) owner.tag = "Enemy";
        else if (identity == 1) owner.AddComponent<MeleeCombatTarget>();
        else if (identity == 2) owner.AddComponent<EnemyHealth>();
        Physics.SyncTransforms();
        return owner;
    }

    private static ParkourDetector EnemySurfaceDetector(Fixture f)
        => (ParkourDetector)Service(Service(f.Abilities, "Traversal"), "Detector");

    private static void EnemyWallEntry()
    {
        for (int identity = 0; identity < 3; identity++)
        using (var f = new Fixture())
        {
            var wall = f.Box(new Vector3(0f, 1.5f, .7f), new Vector3(4f, 3f, .2f));
            EnemySurfaceOwner(f, wall, identity);
            var detector = EnemySurfaceDetector(f);
            Require(!detector.TryWall(Vector3.forward, out _) && !detector.TryWallEntry(out _),
                "Enemy child collider is not a wall contact or wall entry: identity=" + identity);
            f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState != State.WallClimb && f.P.CurrentState != State.LedgeHang,
                "Forward and held jump cannot attach to an enemy body: identity=" + identity);
        }
    }

    private static void EnemyLedgeEntry()
    {
        for (int identity = 0; identity < 3; identity++)
        using (var f = new Fixture())
        {
            var ledge = f.Box(new Vector3(0f, -.9f, .85f), new Vector3(4f, 5f, .5f));
            EnemySurfaceOwner(f, ledge, identity);
            Require(!EnemySurfaceDetector(f).TryLedge(Vector3.forward, out _),
                "Enemy child collider is not a ledge candidate: identity=" + identity);
            f.P.VerticalVelocity = -5f; f.Step();
            Require(f.P.CurrentState != State.LedgeHang,
                "Falling past an enemy head cannot auto-catch it: identity=" + identity);
        }
    }

    private static void EnemyWallSupportChange()
    {
        foreach (bool reparent in new[] { false, true })
        using (var f = new Fixture())
        {
            var wall = f.Box(new Vector3(0f, 1.5f, .7f), new Vector3(4f, 3f, .2f));
            var owner = EnemySurfaceOwner(f, wall, -1);
            f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Unmarked geometry still starts a normal wall climb");

            if (reparent) EnemySurfaceOwner(f, wall, 0);
            else owner.tag = "Enemy";
            Physics.SyncTransforms();
            Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState != State.WallClimb && f.P.CurrentState != State.WallKick,
                "Invalidated enemy support drops before a fresh wall-kick input: reparent=" + reparent);
        }
    }

    private static void EnemyLedgeSupportChange()
    {
        foreach (bool reparent in new[] { false, true })
        using (var f = new Fixture())
        {
            var ledge = f.Box(new Vector3(0f, -.9f, .85f), new Vector3(4f, 5f, .5f));
            var owner = EnemySurfaceOwner(f, ledge, -1);
            f.P.VerticalVelocity = -5f; f.Step();
            for (int i = 0; i < 10; i++) f.Step();
            Require(f.P.CurrentState == State.LedgeHang, "Unmarked geometry still catches and holds a normal ledge");

            if (reparent) EnemySurfaceOwner(f, ledge, 0);
            else owner.tag = "Enemy";
            Physics.SyncTransforms();
            Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState != State.LedgeHang && f.P.CurrentState != State.Mantle && f.P.CurrentState != State.LedgeJump,
                "Invalidated enemy grip releases instead of hanging, mantling or ledge-jumping: reparent=" + reparent);
        }
    }

    private static void EnemyVaultAcross()
    {
        for (int identity = 0; identity < 3; identity++)
        using (var f = new Fixture(true))
        {
            var head = f.Box(new Vector3(0f, .45f, 1.2f), new Vector3(3f, .9f, .4f));
            EnemySurfaceOwner(f, head, identity);
            Require(EnemySurfaceDetector(f).TryObstacle(Vector3.forward, out ObstacleTarget target) && target.IsVault,
                "Enemy low-head geometry remains a valid vault-across target: identity=" + identity);
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.Vault, "Enemy head traversal starts the ordinary vault state");
            for (int i = 0; i < 40; i++) f.Step();
            Require(!f.P.IsParkour && f.P.transform.position.z > f.Origin.z + 1.6f,
                "Enemy vault completes across the head without a wall or ledge attachment");
        }
    }

    private static void EnemyVaultOnto()
    {
        for (int identity = 0; identity < 3; identity++)
        using (var f = new Fixture(true, true))
        {
            var head = f.Box(new Vector3(0f, .5f, 2.6f), new Vector3(3f, 1f, 3f));
            EnemySurfaceOwner(f, head, identity);
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step(1f / 66.66f);
            Require(f.P.CurrentState == State.Mantle && ((ObstacleTraversalState)f.Machine.Current).UsesReferenceMotion,
                "Low enemy head tread retains the reference Vault1 onto route: identity=" + identity);
            f.MoveInput(Vector2.zero);
            for (int i = 0; i < 50 && f.P.IsParkour; i++) f.Step();
            Require(!f.P.IsParkour && f.P.transform.position.y > f.Origin.y + .95f,
                "Onto-head traversal completes with normal support on the head");
        }
    }
}
