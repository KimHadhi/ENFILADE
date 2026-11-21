using UnityEngine;

/// <summary>
/// With a gun held (aiming, and since 0.9.296 at the hip too: gunHipSteady), the eyes, the gun and its sight stay steady
/// through the gait; the gun hung from a head the gait bobbed bounced at every step (the user, 2026-11-21: "걸을 때나 달릴 때나
/// 튀는 거요. 너무 어색하거든요"). Aiming, the eyes, the gun and its sight stay steady through the gait: the hips' bob in the player's frame is smoothed
/// (a lag of aimSteadyTime) by as much as the player aims, right after the animation pass and before the feet are planted
/// and the camera is placed. The whole upper body moves as one, the head the camera rides, the gun FPS Animation Framework
/// hangs from it and the sight it lined up on the eyes, and the planted legs take up the difference. Up to 0.7.34 the
/// camera itself left the eyes for a steady spot while aiming; with the sight lined up on the eyes that would only shake
/// the gun in the view (user picked a steady aim back, 2026-11-08).
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Min(.01f), Tooltip("Aiming, how slowly the hips follow the gait's bob (s): longer is steadier. A step's bob "
        + "comes about twice a second, so 0.3 s takes out about three quarters of it.")]
    private float aimSteadyTime = .45f;
    [SerializeField, Range(0f, .2f), Tooltip("The most the steadied hips may sit off the clip's, as a share of the hips' height "
        + "over the feet. A gait's bob is a few hundredths; a crouch or a stand is far more and passes through at once.")]
    private float aimSteadyReach = .08f;
    [SerializeField, Range(0f, 1f), Tooltip("With a gun held at the hip, the share of the aim's steadiness the hips keep: 1 as steady "
        + "as aiming. 0 lets the gait bob the gun again, as before 0.9.296.")]
    private float gunHipSteady = 1f;

    private Vector3 steadyHips;
    private bool hasSteadyHips;

    private void SetUpAimSteady() => hasSteadyHips = false;

    private void SteadyAim(float dt)
    {
        if (hips == null || dt <= 0f) return;
        Vector3 now = transform.InverseTransformPoint(hips.position);
        // Followed all the time, so an aim begins from where the hips have been, not with a jump.
        if (!hasSteadyHips) { steadyHips = now; hasSteadyHips = true; }
        steadyHips = Vector3.Lerp(steadyHips, now, 1f - Mathf.Exp(-dt / Mathf.Max(.01f, aimSteadyTime)));
        // Only a gait's bob is taken out: the steadied hips never trail the clip's by more than that, so crouching or
        // standing up while aiming goes at its own speed. Unbounded, a crouch hung in the air and came down slowly over
        // the lag (user, 2026-11-08).
        float reach = Mathf.Max(0f, now.y) * aimSteadyReach;
        steadyHips = now + Vector3.ClampMagnitude(steadyHips - now, reach);
        // Only while a gun is held up: by FPS Animation Framework (at the hip as steady as gunHipSteady keeps it, aiming
        // fully) or on the gun's aim clips (AimsOnClip). The moves' own clips (a vault, a slide) keep their motion.
        float aim = dead ? 0f : UpperBodyFromFramework ? Mathf.Max(AimBlend(), gunHipSteady) : AimsOnClip;
        if (aim > 0f) hips.position = transform.TransformPoint(Vector3.Lerp(now, steadyHips, aim));
    }
}
