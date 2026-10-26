using UnityEngine;

/// <summary>
/// Visual-only camera kick and trauma shake for shots, kills and impacts. The offset is added to the camera after
/// every gameplay LateUpdate and removed again at the start of the next frame (CameraViewPunchRestore), so aiming,
/// hitscan, cover probes and the weapon pose always use the steady view: bullets go to the crosshair, the world kicks.
/// The gun is a child of the camera and kicks with it. Hit markers and damage numbers run after this (order 10010) and
/// ride the kick too, like Call of Duty: the marker jumps with the view and settles back as the punch recovers.
/// Added on demand by WeaponFeedback.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10000)]
public sealed class CameraViewPunch : MonoBehaviour
{
    // x = pitch up, y = yaw right, z = roll; degrees. Critically damped: a kick peaks at 1/response and settles.
    private readonly RecoilSpring rotation = new RecoilSpring();
    private readonly RecoilSpring fov = new RecoilSpring();
    private readonly RecoilSpring damageRotation = new RecoilSpring();
    private Camera view;
    private float trauma, noiseTime, seed;
    private Quaternion steadyRotation;
    private Quaternion appliedGunRotation;
    private float appliedFov;
    private bool applied;
    private float damageTrauma, damageNoiseTime;
    public float DamageTrauma => damageTrauma;

    /// <summary>
    /// Forward of the steady (unpunched) view this frame, valid from this component's LateUpdate on. Screen-space UI that
    /// should ride the kick (hit markers, damage numbers) projects a point along it through the punched camera.
    /// </summary>
    public Vector3 SteadyForward { get; private set; } = Vector3.forward;

    public static CameraViewPunch For(Camera camera)
    {
        if (camera == null) return null;
        var punch = camera.GetComponent<CameraViewPunch>();
        return punch != null ? punch : camera.gameObject.AddComponent<CameraViewPunch>();
    }

    private void Awake()
    {
        view = GetComponent<Camera>();
        seed = Random.value * 100f;
        if (GetComponent<CameraViewPunchRestore>() == null) gameObject.AddComponent<CameraViewPunchRestore>();
    }

    /// <summary>degrees: x up, y yaw, z roll. fovDegrees widens the view briefly. trauma (0..1) adds decaying shake.</summary>
    public void Punch(Vector3 degrees, float fovDegrees = 0f, float addTrauma = 0f)
    {
        if (!GunFeelSettings.Instance.viewPunch) return;
        float response = GunFeelSettings.Instance.viewPunchResponse;
        rotation.Kick(degrees, response);
        fov.Kick(new Vector3(fovDegrees, 0f, 0f), response * .7f);
        trauma = Mathf.Clamp01(trauma + addTrauma);
    }

    /// <summary>Separate incoming-damage channel, so clearing damage feedback cannot cancel weapon recoil.</summary>
    public void DamagePunch(Vector3 degrees, float addTrauma)
    {
        if (!PlayerDamageFeedbackSettings.Instance.cameraShake) return;
        damageRotation.Kick(degrees, 24f);
        damageTrauma = Mathf.Clamp01(damageTrauma + addTrauma);
    }

    public void ClearDamage()
    {
        damageRotation.Reset(); damageTrauma = damageNoiseTime = 0f;
        // This can run after LateUpdate (death, revive or a layer toggle). Preserve the already-rendered gun kick/FOV.
        if (applied) transform.localRotation = steadyRotation * appliedGunRotation;
    }

    private void LateUpdate()
    {
        Restore();
        SteadyForward = transform.forward;
        var settings = GunFeelSettings.Instance;
        float dt = Time.deltaTime, response = settings.viewPunchResponse;
        rotation.Tick(dt, response);
        fov.Tick(dt, response * .7f);
        trauma = Mathf.MoveTowards(trauma, 0f, settings.shakeRecovery * dt);
        noiseTime += dt * settings.shakeFrequency;

        // Trauma squared keeps light fire subtle while big events still shake clearly.
        float shake = trauma * trauma * settings.shakeMaxAngle;
        Vector3 noise = shake <= 0f ? Vector3.zero : new Vector3(
            Mathf.PerlinNoise(seed, noiseTime) - .5f,
            Mathf.PerlinNoise(seed + 31.7f, noiseTime) - .5f,
            Mathf.PerlinNoise(seed + 63.1f, noiseTime) - .5f) * (2f * shake);
        Vector3 kick = rotation.Value;
        Vector3 euler = new Vector3(-kick.x, kick.y, kick.z) + new Vector3(noise.x, noise.y, noise.z * 1.4f);
        appliedGunRotation = Quaternion.Euler(euler);
        var damageSettings = PlayerDamageFeedbackSettings.Instance;
        if (!damageSettings.cameraShake) { damageRotation.Reset(); damageTrauma = 0f; }
        float damageDt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
        damageRotation.Tick(damageDt, 24f);
        damageTrauma = Mathf.MoveTowards(damageTrauma, 0f, Mathf.Max(.1f, damageSettings.shakeRecovery) * damageDt);
        damageNoiseTime += damageDt * Mathf.Max(1f, damageSettings.shakeFrequency);
        float damageShake = damageTrauma * damageTrauma * damageSettings.shakeMaxAngle;
        Vector3 damageKick = damageRotation.Value;
        euler += new Vector3(-damageKick.x, damageKick.y, damageKick.z) + new Vector3(
            Mathf.PerlinNoise(seed + 90f, damageNoiseTime) - .5f,
            Mathf.PerlinNoise(seed + 120f, damageNoiseTime) - .5f,
            Mathf.PerlinNoise(seed + 150f, damageNoiseTime) - .5f) * (2f * damageShake);
        if (euler.sqrMagnitude < 1e-8f && Mathf.Abs(fov.Value.x) < 1e-4f) return;

        steadyRotation = transform.localRotation;
        transform.localRotation = steadyRotation * Quaternion.Euler(euler);
        appliedFov = view != null ? fov.Value.x : 0f;
        if (view != null) view.fieldOfView += appliedFov;
        applied = true;
    }

    /// <summary>Put the steady view back. Called before any gameplay reads the camera.</summary>
    internal void Restore()
    {
        if (!applied) return;
        applied = false;
        transform.localRotation = steadyRotation;
        if (view != null) view.fieldOfView -= appliedFov;
        appliedFov = 0f;
    }

    private void OnDisable()
    {
        Restore();
        rotation.Reset(); fov.Reset(); trauma = 0f;
        damageRotation.Reset(); damageTrauma = damageNoiseTime = 0f;
    }
}
