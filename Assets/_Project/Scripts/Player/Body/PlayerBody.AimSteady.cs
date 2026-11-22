using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// With a gun held up (at the hip by FPS Animation Framework, down the sights on the gun's aim clips), the eyes, the gun and
/// its sight stay steady through the gait; the gun hung from a head the gait bobbed bounced at every step (the user, 2026-11-21: "걸을 때나 달릴 때나
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
    private Vector3 steadyHips;
    // How steady the hips are kept now (0-1): eased in and out as a gun's run, a vault or a slide takes the body and gives
    // it back.
    private float gunSteady;
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
        // Only while a gun is held up in a shooting stance, whoever holds the upper body: FPS Animation Framework at the
        // hip, the gun's aim clips down the sights. Taken from who held it (FPSAF, or the aim clip's share) it went out
        // for a moment as the sights came down and the hips jumped (the user, 2026-11-22: "줌을 풀면 팔이 띠용한다"). The
        // moves' own clips (a gun's run, a vault, a slide, a dodge) keep their motion.
        bool gunUp = !dead && HoldsGun && !GunRuns && (state == MovementState.Standing || state == MovementState.Crouching);
        gunSteady = Mathf.MoveTowards(gunSteady, gunUp ? 1f : 0f, dt / Mathf.Max(.01f, blendTime));
        if (gunSteady > 0f) hips.position = transform.TransformPoint(Vector3.Lerp(now, steadyHips, gunSteady));
    }
}
