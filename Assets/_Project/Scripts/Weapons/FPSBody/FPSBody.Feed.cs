using KINEMATION.FPSAnimationFramework.Runtime.Core;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

// FPSBody: What the demo's FPSController and FPSMovement feed FPSAF, from our player. One part of the class; the rest is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    private void Feed(float dt)
    {
        Vector3 velocity = movement != null ? Vector3.ProjectOnPlane(movement.ActualVelocity, Vector3.up) : Vector3.zero;
        float speed = velocity.magnitude;
        float walk = Mathf.Max(.1f, movement != null ? movement.WalkSpeed : 1f);
        Vector3 local = transform.InverseTransformDirection(velocity) / walk;
        input.SetValue(FPSANames.MoveInput, (Vector4)Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f));

        // The view: FPSAF's pitch is down-positive; its yaw is how far the view turns from where the body faces. FPSAF's
        // Look spreads that yaw over the spine without a limit (90 degrees of input, 90 of twist), so where the legs can't
        // follow the view (a vault, a slide) the spine twists only as far as a back does; the head and camera take the rest
        // (user, 2026-11-07: looking back mid-vault turned the upper body 180 degrees from the legs).
        LookDelta(out float pitchDown, out Vector2 delta);
        // Standing or crouched the legs turn after the view, and until they do the upper body keeps up all the way (FPSAF's
        // 90): pinned at 60 (probe, 2026-11-08) the gun stopped short whenever the view led the legs further, then swung back.
        var stance = movement != null ? movement.CurrentState : MovementState.Standing;
        float limit = stance == MovementState.Standing || stance == MovementState.Crouching ? 90f : twistLimit;
        float twist = Mathf.Clamp(Mathf.DeltaAngle(animator.transform.eulerAngles.y, transform.eulerAngles.y), -limit, limit);
        input.SetValue(FPSANames.MouseInput, new Vector4(twist, pitchDown));
        // FPSAF's Look twists the spine by this. Its IK layer can also turn the feet back by it (offsetFeetTargets), because
        // the demo turns the whole character with the view; ours already faces the legs, so that is off in our profiles
        // (user, 2026-11-09: turning on the spot with a gun out, the legs slid round instead of stepping).
        input.SetValue(FPSANames.TurnOffset, twist);
        input.SetValue(FPSANames.MouseDeltaInput, new Vector4(delta.x, delta.y));

        // Q/E: FPSAF's roll tips the top to the left for a positive angle, as our view's roll does.
        float share = movement != null && movement.Abilities != null ? movement.Abilities.Lean.Share : 0f;
        // As far as takes the eyes the lean's distance on this body, as FPSAF's Look shares the tip down the spine for this
        // gun (PlayerBody.LeanRollFor); the demo's fixed 25° carried them about 0.25 m of the 0.35 m peek (2026-11-08).
        float roll = Mathf.Abs(share) > .001f && held != null && body != null
            ? Mathf.Sign(share) * body.LeanRollFor(Mathf.Abs(movement.LeanOffset), held.LeanChain) : 0f;
        input.SetValue(FPSANames.LeanInput, -roll);
        float sign = Mathf.Abs(share) > .05f ? Mathf.Sign(share) : 0f;
        // Down the sights a peek keeps the sight where it is, as Siege's does: the lean's little gun motion is the hip's.
        if (sign != leanSign) { leanSign = sign; if (!Sighted) Motion(assets.leanMotion); }

        // Sprinting the spine lets the view go and the upper body is no longer held steady (FPSController.OnSprintStarted).
        float sprint = movement != null ? movement.SprintProgress : 0f;
        bool grounded = movement == null || movement.IsGrounded;
        // The sights coming up end the sprint's hold at once: the body ran on above walking speed for a moment after the
        // aim began, and the spine let the view go and then caught it again under the rising sight (user, 2026-11-11: "줌
        // 할 때 가끔씩 처음에 엄청 흔들리면서").
        bool sighting = HoldsGun && controller.AimBlend > 0f;
        bool sprinting = grounded && sprint >= sprintFrom && speed > walk && !sighting;
        float follow = 1f - Mathf.Exp(-dt / weightTime);
        sprintWeight = Mathf.Lerp(sprintWeight, sprinting ? 1f : 0f, sighting ? 1f - Mathf.Exp(-dt / (weightTime * AimedSprintShare)) : follow);
        // Eased with the sprint pose instead of switched: the spine took the view back in a single frame as a sprint ended.
        input.SetValue(FPSANames.StabilizationWeight, 1f - sprintWeight);
        input.SetValue("LookLayerWeight", Mathf.Lerp(1f, sprintLookWeight, sprintWeight) * Framework);
        input.SetValue(FPSANames.PlayablesWeight, 1f);

        var state = movement != null ? movement.CurrentState : MovementState.Standing;
        bool crouched = state == MovementState.Crouching;
        crouchWeight = Mathf.Lerp(crouchWeight, crouched ? 1f : 0f, follow);
        // Down the sights FPSAF's little moves stay out: they swung the sight off the target (aimedNoMotions).
        bool sighted = Sighted;
        if (crouched != wasCrouched) { wasCrouched = crouched; if (!sighted) Motion(assets.crouchingMotion); }
        if (grounded && !wasGrounded && !sighted) Motion(assets.jumpingMotion);
        wasGrounded = grounded;
        bool moving = speed >= StillSpeed;
        if (wasMoving && !moving && !sighted) Motion(assets.stopMotion);
        wasMoving = moving;

        // The Animator parameters FPSAF's profiles read. The demo's full-body actions take the upper body from FPSAF by
        // FullBodyWeight; ours are the climbs, hangs and rungs where the body's hands leave the gun.
        animator.SetFloat(CrouchWeightId, crouchWeight);
        animator.SetFloat(SprintPoseWeightId, sprintWeight);
        animator.SetFloat(VelocityId, speed);
        animator.SetFloat(FullBodyWeightId, 1f - Framework);

        if (!HoldsGun) return;
        float aim = controller.AimBlend;
        held.Steady(aim, hipSway, aimedKick, gunKick);
        FeedConverge(dt, aim);
        // FPSAF's Ads brings the sight up in our aim time, which an angled grip shortens (WeaponController.AdsTime).
        if (held.Ads != null) held.Ads.aimingSpeed = 1f / Mathf.Max(.01f, controller.AdsTime);
        // KINEMATION's recoil animation kicks per shot or loops by the fire mode, which B changes while the gun is out; it
        // reads the field as each shot comes.
        if (recoil != null) recoil.fireMode = FireModeOf(HeldFireMode);
        // Only this PC's own player looks through a lens; a friend's copy has no view to render it for.
        if (movement == null || movement.IsLocal) held.ShowScope(aim, viewCamera);
        // Aiming in holds until the aim really goes back down. It was read afresh each frame from the aim rising, so a frame
        // where it stood still (the player's pace stopped by a hit stop or slow motion) read as letting go: the leaving
        // motion bobbed the sight just as the zoom began and the sights came up a second time (user, 2026-11-11: "줌 할 때
        // 가끔씩 처음에 엄청 흔들리면서 줌하는데 조준하는 데 불편해").
        bool wantsAim = aim >= .999f || aim > lastAim || (aiming && aim > 0f && aim >= lastAim);
        lastAim = aim;
        if (wantsAim != aiming)
        {
            if (wantsAim) SteadyForSights();
            aiming = wantsAim;
            held.Aim(aiming, input, recoil);
            // KINEMATION's aiming motion bobs the gun as it comes up; down a 4x or 8x sight that bob was the picture
            // shaking just as the player wanted to look (user, 2026-11-10: "바로 흔들림 없이 줌"). It plays only as the gun
            // comes down again.
            if (!aiming) Motion(assets.aimingMotion);
        }
    }
}
