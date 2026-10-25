using UnityEngine;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunCameraChecks()
    {
        Check("vault handoff has no late-frame camera teleport at 30/60/120Hz", SmoothVaultCameraHandoff);
        Check("camera punch is additive, aim stays independent and recovers without drift", IndependentCameraPitch);
    }
    private static void SmoothVaultCameraHandoff()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
        using (var f = new Fixture(true, true))
        {
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            var camera = f.Player.GetComponentInChildren<Camera>();
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true);
            Vector3 previous = camera.transform.position;
            float elapsed = 0f, latePeak = 0f;
            do
            {
                f.Step(dt); elapsed += dt;
                float speed = Vector3.Distance(camera.transform.position, previous) / dt;
                if (elapsed > 0.15f) latePeak = Mathf.Max(latePeak, speed);
                previous = camera.transform.position;
            } while (f.P.CurrentState == State.Vault && elapsed < 0.7f);
            Require(!f.P.IsParkour && f.P.transform.position.z > f.Origin.z + 1.7f, "Vault finishes beyond obstacle");
            Require(latePeak < 7f, "No snap across remaining end gap: dt=" + dt + " peak=" + latePeak);
            Require(elapsed < 0.5f, "Smoothing does not turn vault into a slow climb");
        }
    }
    private static void IndependentCameraPitch()
    {
        using (var f = new Fixture(true, true))
        {
            var camera = f.Player.GetComponentInChildren<Camera>();
            Transform aim = camera.transform.parent;
            Quaternion authoredCameraRotation = Quaternion.Euler(2f, 3f, 0f);
            camera.transform.localRotation = authoredCameraRotation;
            var effects = f.Player.AddComponent<PlayerCameraEffects>(); Call(effects, "Awake");
            Transform pivot = camera.transform.parent;
            Require(pivot != aim && pivot.name == "CameraEffectsPivot", "Effects own a separate additive pivot");
            f.Box(new Vector3(0f, 0.45f, 1.2f), new Vector3(3f, 0.9f, 0.4f));
            f.MoveInput(Vector2.up); Input(f, "JumpPressed", true); f.Step();
            float previous = 0f;
            for (int i = 0; i < 130; i++)
            {
                Quaternion look = Quaternion.Euler(i % 2 == 0 ? 70f : -65f, 15f, 0f);
                aim.localRotation = look;
                Call(effects, "UpdateTraversalPunch", 1f / 60f); Call(effects, "ApplyCameraRotation");
                float pitch = Mathf.DeltaAngle(0f, pivot.localEulerAngles.x);
                Require(Mathf.Abs(pitch - previous) < 0.4f, "Pitch changes continuously including return to neutral");
                Near(Quaternion.Angle(aim.localRotation, look), 0f, "Camera effect never writes mouse aim", 0.001f);
                Near(Quaternion.Angle(camera.transform.localRotation, authoredCameraRotation), 0f, "Authored camera rotation is preserved", 0.001f);
                previous = pitch;
            }
            Near(previous, 0f, "No accumulated pitch drift", 0.001f);
            Call(effects, "OnDisable"); Near(Quaternion.Angle(pivot.localRotation, Quaternion.identity), 0f, "Disabling effects restores neutral offset");
        }
    }
}
