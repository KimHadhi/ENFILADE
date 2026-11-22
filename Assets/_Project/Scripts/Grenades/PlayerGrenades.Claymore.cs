using System;
using UnityEngine;

// PlayerGrenades: Setting a claymore down. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // The charge's own size, for the placement record and its room (m): two bricks and a phone.
    private static readonly Vector3 MineSize = new Vector3(.24f, .14f, .14f);
    // What stood in the way of the last spot looked for (a structure under the crosshair or round the spot), and where
    // that was: the refusal's record (Placement.Record).
    private Collider spotBy;
    private Vector3 spotTried;

    // Where the claymore would go now: under the crosshair within GrenadeRules claymoreReach of the chest, on a floor
    // (phone up, guarding the way the player looks) or a wall (its back to it, guarding the room); else on the floor a
    // step ahead. False when there is nowhere.
    // What it may lie on is the one placement rule's to say, the same as for a barricade (Placement): the built world
    // only. The dead and whatever lies loose are looked past, so it goes on the floor under a corpse; a living body is
    // looked past too (it lies at a zombie's feet as well as anywhere); and what another player built is no ground, since
    // it would be left hanging in the air when that came down (it used to stick to a barricade and to a rolling crate).
    private bool Spot(out Vector3 at, out Quaternion facing, out bool wall)
    {
        at = default;
        facing = Quaternion.identity;
        wall = false;
        spotBy = null;
        var eye = Camera.main;
        if (eye == null) return false;
        var view = eye.transform;
        float reach = Mathf.Max(.5f, Throwables.Rule("claymoreReach", 3f));
        Vector3 chest = transform.position + Vector3.up * 1.2f;
        Vector3 look = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        if (look.sqrMagnitude < 1e-4f) look = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        look = look.sqrMagnitude > 1e-4f ? look.normalized : Vector3.forward;
        spotTried = chest + look * reach;
        var met = Placement.Ray(view.position, view.forward, reach + 6f, transform, Placement.Built, out RaycastHit hit);
        if (met != Placement.Thing.None && Vector3.Distance(hit.point, chest) <= reach + .3f)
        {
            spotTried = hit.point;
            if (met != Placement.Thing.World) spotBy = hit.collider;
            else if (hit.normal.y >= .6f)
            {
                at = hit.point;
                facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(look, hit.normal).normalized, hit.normal);
                return Clear(at, hit.normal);
            }
            else if (Mathf.Abs(hit.normal.y) < .6f)
            {
                at = hit.point;
                facing = Quaternion.LookRotation(hit.normal, Vector3.up);
                wall = true;
                return Clear(at, hit.normal);
            }
        }
        // Nothing to set it on under the crosshair: on the floor a step ahead.
        Vector3 ahead = transform.position + Vector3.up * 1f + look * Mathf.Min(1.2f, reach);
        var under = Placement.Ray(ahead, Vector3.down, 2.5f, transform, Placement.Built, out RaycastHit ground);
        if (under == Placement.Thing.None) return false;
        spotTried = ground.point;
        if (under != Placement.Thing.World) { spotBy = ground.collider; return false; }
        if (ground.normal.y < .6f) return false;
        at = ground.point;
        facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(look, ground.normal).normalized, ground.normal);
        return Clear(at, ground.normal);
    }

    // A claymore needs no room of its own, but it is never set down inside what another player built (a step ahead of a
    // Support standing behind its own planks is under them).
    private bool Clear(Vector3 at, Vector3 normal)
        => Placement.Box(at + normal * .08f, Vector3.one * .07f, Quaternion.identity, transform, Placement.Thing.Structure, out spotBy)
            == Placement.Thing.None;

    // Held: the spot, the fan it would guard, and lines to the claymores it would go off with.
    private void Preview()
    {
        aiming = true;
        if (preview == null) preview = new ClaymorePreview(transform);
        if (Spot(out Vector3 at, out Quaternion facing, out bool wall)) preview.Show(at, facing, wall, Leaving());
        else preview.Hide();
    }

    // The hand puts it down where Spot says; nowhere to put it, the hand comes back with it (nothing spent).
    private void Put(ThrowableInfo info)
    {
        holding = letGo = false;
        StopAim();
        bool found = Spot(out Vector3 at, out Quaternion facing, out bool wall);
        // What the placement rule met goes on record either way (Logs/placement.txt), as a barricade's does.
        if (info != null && count > 0)
            Placement.Record("Claymore", found ? (wall ? "Wall" : "Floor") : "Nowhere", found, found ? at : spotTried,
                facing.eulerAngles.y, MineSize, spotBy, transform);
        if (info == null || count <= 0 || !found)
        {
            GrenadeHUD.Refused();
            Lower();
            Handed?.Invoke(armKind, HandPhase.Lower);
            return;
        }
        count--;
        readyAt = Time.time + Cooldown;
        ushort id = NextId();
        // Past this player's most their oldest comes back off first. How many may be out and which goes is the one
        // count's to say (Deployables), with this PC's own cards; the copies on the other PCs take off the ones named.
        Deployables.MakeRoom(movement, ClaymoreMine.KindName, retiredMines);
        var takenBack = new ushort[retiredMines.Count];
        for (int i = 0; i < takenBack.Length; i++) takenBack[i] = retiredMines[i] is ClaymoreMine old ? old.Id : (ushort)0;
        Hold(ClaymoreMine.Place(id, at, facing, wall, movement, true, OnMineFired));
        Throwables.Play("Bounce", at, .8f, .7f, 15f);
        SetDown?.Invoke(id, at, facing, wall, takenBack);
        PlayerCallouts.Thrown(info.Callout);
        released = true;
        Lower();
        Handed?.Invoke(armKind, HandPhase.Lower);
    }

    // The claymores that came back off for the one just set down (Deployables.MakeRoom), for the others to be told.
    private readonly System.Collections.Generic.List<IPlaced> retiredMines = new System.Collections.Generic.List<IPlaced>();

    // A new claymore of this player's, theirs or a friend's copy: kept by its number, so a burst or a word to take it
    // back finds it. How many may be out is not counted here (Deployables); the ones gone off or taken back are dropped.
    private void Hold(ClaymoreMine claymore)
    {
        mines.RemoveAll(m => m == null || !m.Live);
        if (claymore != null) mines.Add(claymore);
    }

    // The claymore setting another down would take back: this player's oldest, once as many are out as they may keep
    // (the one count's answer, which the HUD's line shows too: Deployables.Next).
    private ClaymoreMine Leaving() => Deployables.Next(movement, ClaymoreMine.KindName) as ClaymoreMine;

    // This PC's player's claymore saw a zombie: it goes off, and with it every claymore set down near it, anyone's
    // (ClaymoreChain); friends' copies go off as this PC says.
    private void OnMineFired(ClaymoreMine claymore)
    {
        if (claymore == null) return;
        mines.Remove(claymore);
        var chain = ClaymoreChain.Gather(claymore);
        if (chain.Count <= 1)
        {
            Vector3 at = claymore.Heart;
            claymore.FireAlone();
            Burst?.Invoke(Throwable.Claymore, claymore.Id, at);
            return;
        }
        Vector3 middle = ClaymoreChain.Middle(chain);
        ClaymoreChain.Name(chain, out ulong[] owners, out ushort[] ids);
        ClaymoreChain.GoOff(chain, chain.Count, middle);
        Chained?.Invoke(owners, ids, middle);
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
        // A stone has no pin: this is the hand closing on it, and the thrower hears it come into the hand (the user,
        // 2026-11-21: "Rock ready는 손에 돌 들었을 때"). Only on their own PC, as PlayerThrow had it.
        if (armKind == Throwable.Stone)
        {
            if (movement.IsLocal) ThrownStone.PlayReady();
            return;
        }
        // A knife has no pin, nor does a claymore (its phone comes on once it is down).
        if (armKind == Throwable.Knife || (info != null && info.Places)) return;
        Vector3 at = armBody != null ? armBody.HeldPoint : HandPoint();
        // The thrower hears the quick grenade addon's pin sounds (HudAddons.GrenadesModule) instead of the world's pin;
        // everyone else hears the world's, at the hand.
        if (movement.IsLocal) HudAddons.GrenadesModule.PinPulled();
        else Throwables.Play("Pin", at, .5f, 1f, 15f);
        if (!movement.IsLocal || info == null || !info.Cooks) return;
        cooking = true;
        cookLeft = info.Fuse;
    }
}
