using System.Collections.Generic;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

// FPSBody: Drawing and putting away (the demo's EquipWeapon / UnequipWeapon). One part of the class; the rest is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    // What this body was asked to build a gun for outside a loading screen, each once: a gun without clips for this
    // body, or a knife, is not looked up among KINEMATION's guns again at every frame.
    private readonly HashSet<WeaponData> askedFor = new HashSet<WeaponData>();

    private FPSGun Find(WeaponData data)
    {
        if (data == null) return null;
        foreach (var gun in guns) if (gun.Data == data) return gun;
        return BuildWhenAsked(data);
    }

    // This body's copy of a gun the first time anything asks for it: the gun drawn, the guns worn on the body
    // (HolsteredGuns asks for each one carried), a part put on one in the customize menu. Install builds every gun only
    // behind a loading screen. Null before the body is set up, and for what is no gun of KINEMATION's.
    private FPSGun BuildWhenAsked(WeaponData data)
    {
        if (rig == null || framework == null || assets == null || !askedFor.Add(data)) return null;
        foreach (var entry in assets.guns)
        {
            if (entry.weapon != data) continue;
            int before = guns.Count;
            AddGun(entry);
            return guns.Count > before ? guns[guns.Count - 1] : null;
        }
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
        // The recoil animation is told too: its aimed share stayed on from the last gun when the swap was made down the sights.
        held.Aim(false, recoil);
        held.Equip(framework, recoil, assets, 60f / Mathf.Max(.001f, held.Data.ShotInterval), FireModeOf(HeldFireMode), bodyCamera,
            aimedRecoilTurn, aimedRecoilLift);
        drawnAt = Time.time;
        drawSettle = LengthOf(assets.equipMotion);
        // Where the body carries the gun, the hold is on the crosshair's point from the gun's first frame: before it was
        // drawn the gun was nowhere, so there is no turn to ease in. Eased in from 0 it turned the gun through the first
        // 0.2 s of every draw (Logs/gun-actions.txt, 2026-11-15: 0.94 of the turn 0.21 s into the Herrington's draw). By
        // the body's carrying, not by the hands: after bare hands they are not on the gun yet (BodyCarries).
        convergeWeight = BodyCarries >= .999f ? 1f : 0f;
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
        // No gun is on the body now (FeedAction says it again for the next one).
        if (playables is FPSBodyPlayables mixer) mixer.GunOnBody = 0f;
        held.gameObject.SetActive(false);
        framework.LetGo();
        linked = false;
    }

    // Every gun built behind the loading screen has its FPSAF layers set up on this body there too
    // (FPSBodyBoneController.Prepare): a draw then only puts kept layers into play. A gun built later gets its own at its
    // first draw, once.
    private void HoldAhead()
    {
        if (framework == null) return;
        foreach (var gun in guns)
        {
            // One gun whose layers can't be set up still lets the others be; its draw sets it up and shows the error again.
            try { framework.Prepare(gun.gameObject); }
            catch (System.Exception e) { Debug.LogException(e, gun); }
        }
    }

    // A fanned revolver kicks as an automatic does, round after round.
    private static FireMode FireModeOf(WeaponFireMode mode)
        => mode == WeaponFireMode.Automatic || mode == WeaponFireMode.Fanning ? FireMode.Auto
            : mode == WeaponFireMode.Burst ? FireMode.Burst : FireMode.Semi;

    // The held gun's fire mode as B left it (WeaponRuntime.FireMode; a friend's copy has theirs), else the gun's own.
    private WeaponFireMode HeldFireMode
        => controller.Runtime != null && controller.Runtime.Data == held.Data ? controller.Runtime.FireMode : held.Data.FireMode;
}
