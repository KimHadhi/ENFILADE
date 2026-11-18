using UnityEngine;
using MovementState = PlayerMovement.MovementState;

// PlayerGrenades: The key. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // The key was taken (a screen, another window) while it held a grenade back. Nobody saw whether it was let go
    // meanwhile, so the grenade waits in the hand until the key is seen held again: only a release the game sees throws.
    private bool keyLost;

    /// <summary>
    /// The key, and what becomes of a grenade in the hand. The hand that holds one keeps it and the throw stays possible,
    /// unless something must have that hand (the user, 2026-11-18: "일단 왼손을 강제로 써야 하는 상황이 아니라면 수류탄을
    /// 든 손이 우선 순위를 가져가게 해서 던질 수 있게 하고 싶군요"). Three cases, asked in this order:
    /// 1. Something must have the left hand (<see cref="HandForced"/>, the one list): the pin goes back, nothing spent.
    /// 2. The key is not the player's to read (a screen has the body's keys, the window is behind another, a board's
    ///    pointer has the mouse a grenade key sits on): the grenade waits in the hand.
    /// 3. The body is in a move of its own that needs no hand (a dodge, a dive, a roll, lying down): the arm is not up,
    ///    so nothing starts and nothing leaves; the grenade waits in the hand.
    /// Until that day the pin went back in all three, and for the guard key too, which with a gun is the aim: opening the
    /// chat, dodging or aiming each cost the player the grenade they held.
    /// </summary>
    private void Hand(ThrowableInfo info)
    {
        var key = KeyBindings.Find("Player/Grenade");
        bool drawn = arm == Arm.Draw || arm == Arm.Hold, inHand = drawn && !letGo;
        if (info == null || key == null || HandForced())
        {
            // Going for it or holding it: the pin goes back; but a flashbang already burning keeps burning in the hand (a
            // grab bursts it round its holder: Cook).
            var held = Throwables.Of(armKind);
            if (drawn && !(cooking && held != null && !held.ThrowsOut)) PutBack();
            return;
        }
        // KeyBindings reads a taken key as not held, which below means "let go": taken is told apart here first. A throw
        // the player has already let go is theirs and goes on (Run).
        bool keys = movement.InputAvailable && InputFocus.TakesInput() && InputFocus.Takes(key);
        if (!keys)
        {
            // Held out now, whatever the press began as (a tap cut short by the chat): its arc shows it is still there.
            if (inHand) keyLost = holding = true;
            return;
        }
        // Let go meanwhile, it leaves as the body comes out of the move (the key reads as let go then).
        if (!BodyThrows()) return;
        if (arm == Arm.None && KeyBindings.Pressed(key))
        {
            if (count <= 0) { GrenadeHUD.Refused(); return; }
            if (Time.time < readyAt) return;
            Begin();
            return;
        }
        if (!inHand) return;
        // The guard key put the pin back until 2026-11-18. With a gun that key is the aim, and aiming is no reason to lose
        // the grenade (the user: "조준을 하면 취소한단 뜻인가요?"): the left hand keeps it and the right hand aims
        // (TakeHandFromGun). GrenadeRules guardPutsBack 1 makes the key put the pin back again.
        if (Throwables.Rule("guardPutsBack", 0f) > .5f && KeyBindings.Pressed("Combat/Guard"))
        {
            PutBack();
            return;
        }
        if (KeyBindings.Held(key))
        {
            keyLost = false;
            if (Time.time - pressedAt >= Mathf.Max(0f, Throwables.Rule("tapHold", .16f))) holding = true;
            return;
        }
        if (keyLost) return;
        LetGo(!holding);
    }

    // A can in the hands, or every weapon on an x-ray belt with the guns (GimmickTray: through the scanner bare-handed).
    private bool HandsFull() => TryGetComponent(out PlayerWeaponSlots slots) && (slots.HandsFull || slots.GunsAway);

    /// <summary>
    /// What must have the left hand: the one list. A grenade held in that hand goes back on the belt for these (the pin
    /// put back, nothing spent) and for nothing else, and none can be taken out while one of them lasts. Everything else
    /// that would like the hand waits for the grenade instead (PlayerGrenades.HandBusy: the Détente's syringe, the
    /// Support's tool, the guard's arm, a point; KeepHandFree: the gun's reload). What the right hand does goes on.
    /// </summary>
    private bool HandForced() =>
        movement.IsDead || movement.IsDowned    // the body holds nothing
        || movement.IsHeld                      // a zombie has hold of them (grabbed, pinned, hooked): both hands are on it
        || movement.IsParkour                   // the hands are on the world: a ledge, a vault, a wall, a ladder, a cable, a bar
        || movement.CombatInputLocked           // a move of both hands under way: an execution, a guard break, a counter, a treatment
        || HandsFull();                         // a fuel can carried, or the guns put away with the belt

    // The body can go for a throwable and throw it: standing or crouched (walking and in the air too) or sliding, the
    // states GrenadeArm lifts the arm in.
    private bool BodyThrows()
    {
        var state = movement.CurrentState;
        return state == MovementState.Standing || state == MovementState.Crouching || state == MovementState.Sliding;
    }

    // G pressed: the off hand goes for it, and the gun gives that hand up (TakeHandFromGun).
    private void Begin()
    {
        StartArm(kind);
        pressedAt = Time.time;
        holding = letGo = keyLost = false;
        if (kind == Throwable.Knife) KnifeOut(HandPoint());
        TakeHandFromGun();
        Handed?.Invoke(kind, HandPhase.Draw);
    }

    /// <summary>
    /// The left hand is on a throw: what the gun was doing with that hand stops, and that is its reload. The sights and
    /// the trigger are the right hand's and go on: the gun stays aimed, and fires, with the grenade held ready in the
    /// left (the user, 2026-11-18: "오른손은 조준한 채로 왼손은 수류탄 대기하고 있으면 되잖아요"). Until then the whole gun
    /// was interrupted here as a shove interrupts it (WeaponController.InterruptForSurvival), which drops the sights, and
    /// they were pulled down again every frame the hand was on the throw.
    /// </summary>
    private void TakeHandFromGun()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null && weapon.Runtime != null && weapon.Runtime.IsReloading) weapon.Runtime.Interrupt();
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

    // Something must have the hand (HandForced), or the class changed: the pin goes back in and the hand puts it back on
    // the belt.
    private void PutBack()
    {
        cooking = holding = letGo = keyLost = false;
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
