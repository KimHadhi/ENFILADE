using UnityEngine;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunReferenceChecks()
    {
        Check("VManip lerp coefficients reproduce supplied reference samples", ReferenceProgress);
        Check("diagonal wall approach no longer attaches, head-on climb remains", WallEntryAngles);
        Check("torso wall detection and brief contact loss tolerance", WallContactTolerance);
        Check("vault FOV ignores forced speed and posture eases gradually", VaultCamera);
        Check("fast dodge preserves distance across frame rates and retains timed immunity", FastDodgeDistance);
        Check("fast dodge stops at a thin wall", FastDodgeWall);
        Check("Vault2 root follows the supplied straight LerpVector samples", ReferenceRootMotion);
        Check("Vault1 onto motion retains standing eye height", ReferenceOntoMotion);
        Check("reference traversal interruption never leaves a hull embedded", ReferenceInterruption);
        Check("reference traversal blocks unrelated geometry and dynamic obstruction", ReferenceObstruction);
    }
    private static void ReferenceProgress()
    {
        float p = 0f;
        foreach (float sample in new[] { 0.1f, 0.19f, 0.271f, 0.3439f, 0.40951f })
        { p = TraversalTiming.Advance(p, 0.1f, 1f / 66.66f); Near(p, sample, "Source Vault2 progress", 0.00001f); }
        p = TraversalTiming.Advance(0f, 0.075f, 1f / 66.66f);
        Near(p, 0.075f, "Source Vault1 first tick", 0.00001f);
        Near(TraversalTiming.Advance(p, 0.075f, 1f / 66.66f), 0.144375f, "Source Vault1 second tick", 0.00001f);
        Near(TraversalTiming.EndProgress(true), 0.75f, "Source Vault2 threshold");
        Near(TraversalTiming.EndProgress(false), 0.8f, "Source Vault1 threshold");
        Near(TraversalTiming.EndDistanceSquared, 0.175f, "280 source units squared in metres", 0.00001f);
    }
    private static void WallEntryAngles()
    {
        foreach (float angle in new[] { 5f, 15f, 30f, 50f, 70f })
        foreach (float distance in new[] { 0.45f, 0.7f, 0.92f })
        using (var f = new Fixture())
        {
            f.Box(new Vector3(distance, 3f, 5f), new Vector3(0.2f, 12f, 30f));
            f.P.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            f.P.HorizontalVelocity = Vector3.forward * 7f; f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(!f.P.IsParkour, "Diagonal approach stays airborne: " + angle + " / " + distance + " got " + f.P.CurrentState);
        }
        using (var f = new Fixture())
        {
            f.Box(new Vector3(0f, 3f, 0.7f), new Vector3(4f, 6f, 0.2f));
            f.MoveInput(Vector2.up); f.P.HorizontalVelocity = Vector3.forward * 7f; Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Head-on held Space retains climb");
        }
    }
    private static void WallContactTolerance()
    {
        using (var f = new Fixture())
        {
            var wall = f.Box(new Vector3(0f, 2.6f, 0.7f), new Vector3(4f, 2.8f, 0.2f));
            f.MoveInput(Vector2.up); Input(f, "JumpHeld", true); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Torso probe finds wall above chest ray");
            Vector3 position = wall.transform.position; wall.transform.position += Vector3.forward * 3f; Physics.SyncTransforms();
            f.Step(); f.Step(); Require(f.P.CurrentState == State.WallClimb, "Two missing frames do not cancel climb");
            wall.transform.position = position; Physics.SyncTransforms(); f.Step();
            Require(f.P.CurrentState == State.WallClimb, "Recovered contact continues climb");
            wall.transform.position += Vector3.forward * 3f; Physics.SyncTransforms();
            for (int i = 0; i < 8; i++) f.Step();
            Require(f.P.CurrentState != State.WallClimb, "Real wall end releases after grace");
        }
    }
    private static void VaultCamera()
    {
        using (var f = new Fixture(true, true))
        {
            var camera = f.Player.GetComponentInChildren<Camera>();
            var effects = f.Player.AddComponent<PlayerCameraEffects>(); Call(effects, "Awake");
            float initialFov = camera.fieldOfView;
            Transform pivot = f.Player.transform.Find("CrouchPivot"); float startY = pivot.localPosition.y;
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            Require(f.P.CurrentState == State.Vault && f.P.HorizontalVelocity.magnitude > 1f, "Moving scripted vault");
            Call(effects, "UpdateDynamicFov");
            Near(camera.fieldOfView, initialFov, "Stationary approach must not receive scripted sprint FOV", 0.001f);
            Require(startY - pivot.localPosition.y < 0.1f, "First-frame camera crouch is eased rather than slammed");
        }
    }

    private static void FastDodgeDistance()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f, 0.023f })
        using (var f = new Fixture(true))
        {
            Vector3 start = f.P.transform.position;
            Input(f, "DodgePressed", true);
            int frames = Mathf.CeilToInt(f.P.AbilitySettings.stepDuration / dt);
            for (int i = 0; i < frames; i++) f.Step(dt);
            Near(Vector3.ProjectOnPlane(f.P.transform.position - start, Vector3.up).magnitude,
                3.2f, "Dodge distance at dt=" + dt, 0.015f);
            Require(frames * dt < 0.18f, "Fast dodge completes travel under 180ms");
        }
        using (var f = new Fixture(true))
        {
            Input(f, "DodgePressed", true);
            for (int i = 0; i < 16; i++) f.Step(0.01f);
            Require(f.P.CurrentState == State.Standing && f.P.IsDodging, "140ms movement does not truncate 180ms immunity");
            for (int i = 0; i < 5; i++) f.Step(0.01f);
            Require(!f.P.IsDodging, "Timed window still expires");
        }
    }
    private static void FastDodgeWall()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 1f, -1f), new Vector3(4f, 2f, 0.05f));
            Input(f, "DodgePressed", true);
            for (int i = 0; i < 6; i++) f.Step(1f / 30f);
            Require(f.P.transform.position.z > f.Origin.z - 0.75f && !f.P.IsDodging, "Fast sweep stops and cancels on thin wall: z="
                + (f.P.transform.position.z - f.Origin.z) + " dodging=" + f.P.IsDodging + " state=" + f.P.CurrentState);
        }
    }
    private static void ReferenceRootMotion()
    {
        foreach (float speed in new[] { 0f, 9f, 20f })
        using (var f = new Fixture(true, true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            Vector3 start = f.P.transform.position;
            float eye = f.Player.GetComponentInChildren<Camera>().transform.position.y - start.y;
            f.P.HorizontalVelocity = Vector3.forward * speed;
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true);
            foreach (float p in new[] { 0.1f, 0.19f, 0.271f, 0.3439f, 0.40951f, 0.468559f })
            {
                f.Step(1f / 66.66f);
                Require(f.P.CurrentState == State.Vault, "Reference vault stays active");
                Vector3 offset = f.P.transform.position - start;
                Near(offset.z, 1.75f * p, "Source forward interpolation", 0.002f);
                Near(offset.y, 0.8f * p, "Source vertical interpolation", 0.002f);
                Near(f.Player.GetComponentInChildren<Camera>().transform.position.y - f.P.transform.position.y,
                    eye, "No forced crouch camera dip", 0.002f);
            }
            // Keep source pacing/speed, but prevent looking back from reversing the handoff.
            f.MoveInput(Vector2.zero); f.P.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            for (int i = 0; i < 30 && f.P.CurrentState == State.Vault; i++) f.Step(1f / 66.66f);
            Require(!f.P.IsParkour, "Reference traversal completes");
            // State handoff is observed after the next ordinary air-drag tick.
            Near(f.P.HorizontalVelocity.magnitude, 5f, "Reference 200 units/sec followed by ordinary drag", 0.5f);
            Require(Vector3.Dot(f.P.HorizontalVelocity.normalized, Vector3.forward) > 0.99f, "Exit follows traversal despite changed view");
        }
    }
    private static void ReferenceOntoMotion()
    {
        using (var f = new Fixture(true, true))
        {
            f.Box(new Vector3(0f, 0.5f, 2.6f), new Vector3(3f, 1f, 3f));
            Vector3 start = f.P.transform.position;
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step(1f / 66.66f);
            Require(f.P.CurrentState == State.Mantle && ((ObstacleTraversalState)f.Machine.Current).UsesReferenceMotion, "Low broad tread uses Vault1");
            Near(f.P.transform.position.z - start.z, 1.25f * 0.075f, "Vault1 forward50 first sample", 0.002f);
            Near(f.P.transform.position.y - start.y, (f.Origin.y + 1.4f - start.y) * 0.075f, "Vault1 top +16 units", 0.002f);
            f.MoveInput(Vector2.zero);
            for (int i = 0; i < 50 && f.P.IsParkour; i++) f.Step();
            Require(!f.P.IsParkour && f.P.transform.position.y > f.Origin.y + 0.95f, "Vault1 hands off on tread");
            Near(f.P.HorizontalVelocity.magnitude, 0f, "Vault1 has no added launch", 0.05f);
        }
    }
    private static void ReferenceInterruption()
    {
        foreach (bool reset in new[] { false, true })
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true);
            for (int i = 0; i < 7; i++) f.Step(1f / 66.66f);
            var detector = (ParkourDetector)Service(Service(f.Abilities, "Traversal"), "Detector");
            Require(!detector.ClearCapsule(f.P.transform.position, f.P.StandingColliderHeight), "Test interrupts while passing through selected obstacle");
            if (reset) f.P.ResetVelocity();
            else { Input(f, "CrouchPressed", true); f.Step(); }
            Require(!f.P.IsParkour && detector.ClearCapsule(f.P.transform.position, f.P.StandingColliderHeight), "Cancellation restores free body");
            Require(f.Player.GetComponent<CharacterController>().enabled, "Controller is never disabled");
            if (reset)
            {
                f.MoveInput(Vector2.zero); f.Step();
                Require(f.P.ActualVelocity.magnitude < 1f, "Reset recovery does not produce a spurious velocity spike: " + f.P.ActualVelocity);
            }
        }
    }
    private static void ReferenceObstruction()
    {
        using (var f = new Fixture(true))
        {
            var box = f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            var detector = (ParkourDetector)Service(Service(f.Abilities, "Traversal"), "Detector");
            f.Box(new Vector3(0f, 1.7f, 0.7f), new Vector3(3f, 0.15f, 0.1f));
            Require(!detector.ClearReferencePath(f.P.transform.position, f.P.transform.position + new Vector3(0f, 0.8f, 1.75f),
                box.GetComponent<Collider>()), "Unrelated thin obstacle blocks full standing sweep");
        }
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true);
            for (int i = 0; i < 6; i++) f.Step(1f / 66.66f);
            f.Box(new Vector3(0f, 1.7f, 1.3f), new Vector3(3f, 0.15f, 0.1f));
            for (int i = 0; i < 3; i++) f.Step();
            var detector = (ParkourDetector)Service(Service(f.Abilities, "Traversal"), "Detector");
            Require(!f.P.IsParkour && detector.ClearCapsule(f.P.transform.position, f.P.StandingColliderHeight), "Mid-vault obstruction returns to safe position: "
                + f.P.CurrentState + " offset=" + (f.P.transform.position - f.Origin));
        }
    }
}
