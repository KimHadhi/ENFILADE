using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

// FPSBody: Drawing and putting away (the demo's EquipWeapon / UnequipWeapon). One part of the class; the rest is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    private FPSGun Find(WeaponData data)
    {
        if (data == null) return null;
        foreach (var gun in guns) if (gun.Data == data) return gun;
        return null;
    }

    private void Equip(WeaponData data)
    {
        shownData = data;
        StopFiring();
        var next = Find(data);
        if (held != null && held != next) PutAway();
        held = next;
        if (held == null)
        {
            if (muzzle != null) muzzle.SetLocalPositionAndRotation(muzzleHomePosition, muzzleHomeRotation);
            return;
        }
        if (shown) Draw();
    }

    private void Draw()
    {
        if (held == null || linked) return;
        held.gameObject.SetActive(true);
        aiming = false;
        held.Equip(framework, recoil, assets, 60f / Mathf.Max(.001f, held.Data.ShotInterval), FireModeOf(HeldFireMode), bodyCamera,
            aimedRecoilTurn, aimedRecoilLift);
        drawnAt = Time.time;
        drawSettle = LengthOf(assets.equipMotion);
        convergeWeight = 0f;
        // The pack's own draw at its own pace (WeaponController holds the trigger back for the gun's Draw Duration).
        float drawing = held.PlayDraw(playables, bodyCamera);
        if (drawing > 0f) drawSettle = drawing;
        linked = true;
        // And its sound as it comes into the hands (WeaponFeedback, the gun's own or 3D on a friend's copy).
        if (feedback == null && controller != null) feedback = controller.GetComponent<WeaponFeedback>();
        if (feedback != null && held.Data != null) feedback.Drawn(held.Data.SoundProfile);
    }

    private WeaponFeedback feedback;

    private void PutAway()
    {
        if (held == null || !linked) return;
        held.StopAction(playables, 0f, bodyCamera);
        held.StopReload(playables, 0f, bodyCamera);
        held.Unequip(framework, recoil, assets);
        held.gameObject.SetActive(false);
        framework.UnlinkAnimatorProfile();
        linked = false;
    }

    // A fanned revolver kicks as an automatic does, round after round.
    private static FireMode FireModeOf(WeaponFireMode mode)
        => mode == WeaponFireMode.Automatic || mode == WeaponFireMode.Fanning ? FireMode.Auto
            : mode == WeaponFireMode.Burst ? FireMode.Burst : FireMode.Semi;

    // The held gun's fire mode as B left it (WeaponRuntime.FireMode; a friend's copy has theirs), else the gun's own.
    private WeaponFireMode HeldFireMode
        => controller.Runtime != null && controller.Runtime.Data == held.Data ? controller.Runtime.FireMode : held.Data.FireMode;
}
