using UnityEngine;
using MovementState = PlayerMovement.MovementState;

// PlayerGrenades: The key. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    private void Hand(ThrowableInfo info)
    {
        var key = KeyBindings.Find("Player/Grenade");
        bool able = info != null && key != null && movement.InputAvailable && InputFocus.TakesInput() && Able();
        if (!able)
        {
            // Hands taken going for it or holding it: the pin goes back, nothing spent; but a flashbang already burning
            // keeps burning in the hand (a grab bursts it round its holder: Cook).
            var held = Throwables.Of(armKind);
            if ((arm == Arm.Draw || arm == Arm.Hold) && !(cooking && held != null && !held.ThrowsOut)) PutBack();
            return;
        }
        if (arm == Arm.None && KeyBindings.Pressed(key))
        {
            if (count <= 0) { GrenadeHUD.Refused(); return; }
            if (Time.time < readyAt) return;
            Begin(info);
            return;
        }
        if ((arm != Arm.Draw && arm != Arm.Hold) || letGo) return;
        // The guard key puts the pin back: nothing thrown, nothing spent.
        if (KeyBindings.Pressed("Combat/Guard"))
        {
            PutBack();
            return;
        }
        if (KeyBindings.Held(key))
        {
            if (Time.time - pressedAt >= Mathf.Max(0f, Throwables.Rule("tapHold", .16f))) holding = true;
            return;
        }
        LetGo(!holding);
    }

    private bool HandsFull() => TryGetComponent(out PlayerWeaponSlots slots) && slots.HandsFull;

    // The body can throw: alive and up, not held, not climbing, hanging or vaulting, the hands not full; standing or
    // crouched (walking and in the air too) or sliding (GrenadeArm lifts the arm in the same states).
    private bool Able()
    {
        if (movement.IsDead || movement.IsDowned || movement.IsHeld || movement.IsParkour || movement.CombatInputLocked || HandsFull())
            return false;
        var state = movement.CurrentState;
        return state == MovementState.Standing || state == MovementState.Crouching || state == MovementState.Sliding;
    }

    // G pressed: the off hand goes for it. Whatever the gun was doing with that hand stops (a reload, the sights), as a
    // shove stops them.
    private void Begin(ThrowableInfo info)
    {
        StartArm(kind);
        pressedAt = Time.time;
        holding = letGo = false;
        if (kind == Throwable.Knife) KnifeOut(HandPoint());
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.InterruptForSurvival();
        Handed?.Invoke(kind, HandPhase.Draw);
    }

    // G let go (or the reflex): thrown from the hold at once, or as soon as the draw gets there. A tap plays the tap clip;
    // without one the draw runs at the tap's speed and never stops at the hold.
    private void LetGo(bool tap)
    {
        letGo = true;
        Handed?.Invoke(armKind, tap ? HandPhase.TapThrow : HandPhase.Throw);
        if (tap && StartTap()) return;
        if (tap) speed = Motion.TapSpeed;
        if (arm == Arm.Hold) Release();
    }

    // The guard key, or the hands taken: the pin goes back in and the hand puts it back on the belt.
    private void PutBack()
    {
        cooking = holding = letGo = false;
        StopAim();
        Lower();
        Handed?.Invoke(armKind, HandPhase.Lower);
    }

    // The fuse from the pin. A frag near its end is thrown on reflex (cookOut throw); a flashbang that runs out goes off
    // in the hand (cookOut burst).
    private void Cook(ThrowableInfo info)
    {
        if (info == null || movement.IsDead) { cooking = false; return; }
        cookLeft -= Time.deltaTime;
        if (info.ThrowsOut)
        {
            if (arm == Arm.Hold && !letGo && cookLeft <= Throwables.Rule("reflexLead", .5f) + SwingLeft()) LetGo(false);
            // On its way out of the hand it waits for the release (Throw keeps a little fuse for the flight).
            if (cookLeft < 0f) cookLeft = 0f;
            return;
        }
        if (cookLeft > 0f) return;
        cooking = holding = letGo = false;
        StopAim();
        if (count <= 0) return;
        count--;
        readyAt = Time.time + Cooldown;
        ushort id = NextId();
        Vector3 at = armBody != null && arm != Arm.None ? armBody.HeldPoint : HandPoint();
        Throwables.NoteThrower(movement, armKind);
        GrenadeBursts.Play(armKind, at, movement, true);
        Burst?.Invoke(armKind, id, at);
        PlayerCallouts.Thrown(info.Callout);
        released = true;
        Lower();
        Handed?.Invoke(armKind, HandPhase.Lower);
    }
}
