using UnityEngine;

// FPSBody: The gun's pose for WeaponController (IWeaponPose). One part of the class; the rest is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    public bool IsHighReady => highReady;

    // Our gun root (the muzzle under it) is on while a gun of ours is out: WeaponController.ShowsWeapon reads it, and with
    // it the body's hands (the gun hold FPSAF's upper body is weighed by), the armed clips and the HUD's gun anchor. Off,
    // FPSAF's layer stayed at no weight and the body walked with empty arms beside the gun (probe, 2026-11-07).
    public void SetVisible(bool visible)
    {
        shown = visible;
        bool rootOn = visible && held != null;
        if (weaponRoot != null && weaponRoot.gameObject.activeSelf != rootOn) weaponRoot.gameObject.SetActive(rootOn);
        if (held == null || framework == null) return;
        if (visible) Draw();
        else PutAway();
    }

    public void Reset()
    {
        highReady = false;
        cover = 0f;
        if (input != null) input.SetValue(CoverProperty, 0f);
        ForgetCloseHold();
        StopFiring();
    }

    public void OnShot(WeaponData data, Vector2 shotRecoil)
    {
        if (!HoldsGun) return;
        CutDraw();
        // A shot fired mid-bash (the shove landed, so the gun is free at once) goes straight: the gun comes back first.
        CutBash();
        firing = true;
        lastShot = Time.time;
        // A bolt or pump gun's shots come at its action's pace when that is slower (WeaponCycle).
        shotInterval = WeaponCycle.IntervalOf(data);
        held.Fire(controller.LastShot.AmmoAfterShot == 0, recoil, bodyCamera, !kinemationShake || controller.AimBlend >= .5f);
        // A gun worked by hand: the body works its bolt or pump now, done by the time the trigger may fire again (FPSGun.Cycle).
        held.Cycle(playables, bodyCamera);
    }

    // The wall rule probes the gun as it is held clean, never the gun as it is (raised, swung aside or on its way in): while
    // aiming is asked for or under way (ads over 0) the muzzle as held down the sights, learnt, else one on the sight line
    // at the gun's own length (bone to muzzle); at the hip the learnt hip muzzle, else the same. Aiming over cover the sight
    // line clears never lifts the gun: the hip muzzle under a waist-high barricade's top did, every time the player aimed
    // (the user, 2026-11-12: "서포트 바리케이드 때문에 high ready돼서 조준하는데 불편하니까 이거 좀 개선해 줘").
    public Vector3 Prepare(Transform probeView, WeaponData data, float ads)
    {
        if (!HoldsGun || eyes == null) return muzzle != null ? muzzle.position : probeView.position;
        Vector3 onSight = eyes.position + eyes.forward * held.MuzzlePosition.magnitude;
        if (ads > .001f) return held.HasAimedMuzzle ? eyes.TransformPoint(held.AimedMuzzle) : onSight;
        if (held.HasHipMuzzle) return eyes.TransformPoint(held.HipMuzzle);
        return onSight;
    }

    // Our wall rule says whether the gun may fire and how far it is held back, eased at the weapon's high-ready speeds;
    // FPSAF's blocking pose shows it (GunCoverLayer). The gun counts as held up until it is nearly back, so it can't
    // fire on the way down; when the player wants to fire or aim and the way is clear, it comes down at QuickLowerHalfLife.
    public bool Pose(GameObject owner, Transform poseView, Vector3 target, bool obstructed, WeaponRuntime runtime, float dt, int layers, bool quickLower)
    {
        float halfLife = obstructed ? runtime.Data.HighReadyRaiseHalfLife
            : quickLower ? Mathf.Min(QuickLowerHalfLife, runtime.Data.HighReadyLowerHalfLife) : runtime.Data.HighReadyLowerHalfLife;
        cover = Mathf.Lerp(cover, obstructed ? 1f : 0f, 1f - Mathf.Exp(-Mathf.Log(2f) * Mathf.Max(0f, dt) / Mathf.Max(.001f, halfLife)));
        if (!obstructed && cover < .001f) cover = 0f;
        if (input != null) input.SetValue(CoverProperty, cover);
        highReady = obstructed || cover > RaisedFiring;
        return highReady;
    }
}
