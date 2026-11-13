using System;
using UnityEngine;

// PlayerGrenades: Setting a claymore down. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // Where the claymore would go now: under the crosshair within GrenadeRules claymoreReach of the chest, on a floor
    // (phone up, guarding the way the player looks) or a wall (its back to it, guarding the room); else on the floor a
    // step ahead. False when there is nowhere.
    private bool Spot(out Vector3 at, out Quaternion facing, out bool wall)
    {
        at = default;
        facing = Quaternion.identity;
        wall = false;
        var eye = Camera.main;
        if (eye == null) return false;
        var view = eye.transform;
        float reach = Mathf.Max(.5f, Throwables.Rule("claymoreReach", 3f));
        Vector3 chest = transform.position + Vector3.up * 1.2f;
        Vector3 look = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        if (look.sqrMagnitude < 1e-4f) look = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        look = look.sqrMagnitude > 1e-4f ? look.normalized : Vector3.forward;
        if (Physics.Raycast(view.position, view.forward, out RaycastHit hit, reach + 6f, Throwables.WorldMask, QueryTriggerInteraction.Ignore)
            && Vector3.Distance(hit.point, chest) <= reach + .3f)
        {
            if (hit.normal.y >= .6f)
            {
                at = hit.point;
                facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(look, hit.normal).normalized, hit.normal);
                return true;
            }
            if (Mathf.Abs(hit.normal.y) < .6f)
            {
                at = hit.point;
                facing = Quaternion.LookRotation(hit.normal, Vector3.up);
                wall = true;
                return true;
            }
        }
        // Nothing to set it on under the crosshair: on the floor a step ahead.
        Vector3 ahead = transform.position + Vector3.up * 1f + look * Mathf.Min(1.2f, reach);
        if (Physics.Raycast(ahead, Vector3.down, out RaycastHit ground, 2.5f, Throwables.WorldMask, QueryTriggerInteraction.Ignore)
            && ground.normal.y >= .6f)
        {
            at = ground.point;
            facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(look, ground.normal).normalized, ground.normal);
            return true;
        }
        return false;
    }

    // Held: the spot and the fan it would guard.
    private void Preview()
    {
        aiming = true;
        if (preview == null) preview = new ClaymorePreview(transform);
        if (Spot(out Vector3 at, out Quaternion facing, out bool wall)) preview.Show(at, facing, wall);
        else preview.Hide();
    }

    // The hand puts it down where Spot says; nowhere to put it, the hand comes back with it (nothing spent).
    private void Put(ThrowableInfo info)
    {
        holding = letGo = false;
        StopAim();
        if (info == null || count <= 0 || !Spot(out Vector3 at, out Quaternion facing, out bool wall))
        {
            GrenadeHUD.Refused();
            Lower();
            Handed?.Invoke(armKind, HandPhase.Lower);
            return;
        }
        count--;
        readyAt = Time.time + Cooldown;
        ushort id = NextId();
        Keep(ClaymoreMine.Place(id, at, facing, wall, movement, true, OnMineFired));
        Throwables.Play("Bounce", at, .8f, .7f, 15f);
        Placed?.Invoke(armKind, id, at, facing, wall);
        PlayerCallouts.Thrown(info.Callout);
        released = true;
        Lower();
        Handed?.Invoke(armKind, HandPhase.Lower);
    }

    // A new claymore out; past GrenadeRules claymoreMost the oldest comes back off (every PC keeps the same order).
    private void Keep(ClaymoreMine claymore)
    {
        mines.RemoveAll(m => m == null);
        if (claymore == null) return;
        mines.Add(claymore);
        int most = Mathf.Max(1, Mathf.RoundToInt(Throwables.Rule("claymoreMost", 2f)));
        while (mines.Count > most)
        {
            var oldest = mines[0];
            mines.RemoveAt(0);
            if (oldest != null) oldest.Retire();
        }
    }

    // This PC's player's claymore fired: friends' copies fire where it did.
    private void OnMineFired(ClaymoreMine claymore)
    {
        if (claymore == null) return;
        mines.Remove(claymore);
        Burst?.Invoke(Throwable.Claymore, claymore.Id, claymore.Heart);
    }

    private void Lower()
    {
        if (arm == Arm.None) return;
        arm = Arm.Lower;
        lowerLeft = Mathf.Max(.01f, Throwables.Rule("armOut", .14f));
    }

    // Plays the arm's clips, pulls the pin and lets go at their moments, and tells the body's arm (GrenadeArm).
    private void Run(float dt, ThrowableInfo info)
    {
        if (arm == Arm.None) return;
        var motion = Motion;
        var playing = GrenadeArmPath.ClipOf(clip);
        float length = playing != null ? playing.Length : 0f;
        switch (arm)
        {
            case Arm.Draw:
                clipTime += dt * speed;
                if (!pinned && playing != null && playing.HasPin && clipTime >= playing.PinAt) Pin(info);
                if (clipTime >= length)
                {
                    // Up and held: a draw without a pin moment pulls it here, so a cooking one always cooks. The hold
                    // loop plays from its start (the draw's last pose stays where a motion has none).
                    if (!pinned) Pin(info);
                    arm = Arm.Hold;
                    if (motion.HoldClip != null)
                    {
                        clip = motion.HoldTable;
                        clipTime = 0f;
                    }
                    else clipTime = length;
                    if (letGo) Release();
                }
                break;
            case Arm.Hold:
                if (playing != null && length > 0f && string.Equals(clip, motion.HoldTable, StringComparison.OrdinalIgnoreCase))
                    clipTime = Mathf.Repeat(clipTime + dt, length);
                break;
            case Arm.Throw:
            case Arm.Tap:
                clipTime += dt * speed;
                if (arm == Arm.Tap && !pinned && playing != null && playing.HasPin && clipTime >= playing.PinAt) Pin(info);
                float releaseAt = playing != null && playing.HasRelease ? playing.ReleaseAt : length * .4f;
                if (!released && clipTime >= releaseAt)
                {
                    // Never out of the hand with its pin in (a clip whose pin moment was moved away).
                    if (!pinned) Pin(info);
                    released = true;
                    if (movement.IsLocal) Throw(info);
                }
                // The clip ends with the hand back on the gun; the arm lets go of it over GrenadeRules armOut.
                if (clipTime >= length) arm = Arm.None;
                break;
            case Arm.Lower:
                if ((lowerLeft -= dt) <= 0f) arm = Arm.None;
                break;
        }
        if (armBody == null) armBody = GrenadeArm.Of(movement);
        bool on = arm == Arm.Draw || arm == Arm.Hold || arm == Arm.Throw || arm == Arm.Tap;
        if (armBody != null) armBody.Drive(armKind, clip, clipTime, on, !released);
    }

    // The pin comes out as the path's hand brings it up: its sound, and the fuse of one that cooks starts burning.
    private void Pin(ThrowableInfo info)
    {
        pinned = true;
        // A knife has no pin, nor does a claymore (its phone comes on once it is down).
        if (armKind == Throwable.Knife || (info != null && info.Places)) return;
        Vector3 at = armBody != null ? armBody.HeldPoint : HandPoint();
        Throwables.Play("Pin", at, movement.IsLocal ? .7f : .5f, 1f, 15f);
        if (!movement.IsLocal || info == null || !info.Cooks) return;
        cooking = true;
        cookLeft = info.Fuse;
    }

    // While the hand is on a throw the gun can't use it: a reload that starts stops, the sights stay down.
    private void KeepHandFree()
    {
        if (arm != Arm.Draw && arm != Arm.Hold && !((arm == Arm.Throw || arm == Arm.Tap) && !released)) return;
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null && (weapon.AimBlend > .01f || (weapon.Runtime != null && weapon.Runtime.IsReloading))) weapon.InterruptForSurvival();
    }
}
