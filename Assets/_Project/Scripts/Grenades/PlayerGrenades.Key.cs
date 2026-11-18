using UnityEngine;

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
    /// 1. Something must have the left hand (<see cref="HandGone"/>: the hands' owner says so): the pin goes back,
    ///    nothing spent.
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
        if (info == null || key == null || HandGone())
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
        if (!Hands.Stands(HandUses.Grenade)) return;
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
        // (the throw asks for the left hand alone: HandOwner). GrenadeRules guardPutsBack 1 makes the key put the pin
        // back again.
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

    // ---- The left hand (HandOwner) ----

    private HandOwner hands;
    // The throw asked for the left hand and has it.
    private bool handMine;

    // Whose each of this player's hands is: the throw asks it, and keeps no list of its own of what else has a hand.
    private HandOwner Hands => hands != null ? hands : hands = HandOwner.Of(movement);

    // The belt is not on the body: the guns and the kit ride an x-ray tray (GimmickTray: through the scanner
    // bare-handed). A fact of what is carried, not of the hands.
    private bool BeltAway() => TryGetComponent(out PlayerWeaponSlots slots) && slots.GunsAway;

    /// <summary>
    /// The left hand is not the throw's to have: something that must have it has it. The hands' owner says so
    /// (HandOwner, by the ranks of Tables/Player/HandUses.csv): a fact of the body (dead, down, a zombie's grip, a hand
    /// on the world, an execution, a guard break, a counter, a treatment, a fuel can) outranks a grenade in the fist,
    /// and nothing else does, so whatever else would like the hand waits for the grenade (the user, 2026-11-18: "일단
    /// 왼손을 강제로 써야 하는 상황이 아니라면 수류탄을 든 손이 우선 순위를 가져가게 해서 던질 수 있게 하고 싶군요").
    /// A grenade held goes back on the belt for these (the pin put back, nothing spent), and none can be taken out
    /// while one lasts. Until 2026-11-18 this was a list of its own here (HandForced), which asked "any parkour" where
    /// only a hand on the world takes the hand: a step up, which uses no hand, put the pin back too.
    /// </summary>
    private bool HandGone()
    {
        if (BeltAway() || Hands == null) return true;
        if (handMine) return !Hands.Has(this);
        return arm != Arm.None || !Hands.Could(HandUses.Grenade, this);
    }

    // The throw asks for the left hand. False: refused, and nothing begins.
    private bool TakeHand() => handMine = Hands != null && Hands.Take(HandUses.Grenade, this);

    // The left hand is the throw's from the moment it goes for the throwable until the hand is back at rest: said to the
    // hands' owner every frame of it, on this PC's player and on a friend's copy alike (the copy's arm is posed by the
    // same answer). Lost to something that must have the hand, it is not asked for again while the arm only goes back;
    // a flashbang left burning in the hand asks again, and has the hand back when what took it is over.
    private void KeepHand()
    {
        if (Hands == null) return;
        if (arm == Arm.None)
        {
            if (handMine) Hands.Give(this);
            handMine = false;
            return;
        }
        // Taken since it was last said: over (HandGone has put the pin back, or a throw already let go flies on).
        if (handMine && !Hands.Has(this)) handMine = false;
        if (arm == Arm.Lower && !handMine) return;
        // Said again, not asked anew: a no is no news for the record.
        handMine = Hands.Take(HandUses.Grenade, this, false);
    }

    /// <summary>
    /// G pressed: the off hand goes for the throwable. It asks the hands' owner for the left hand and for nothing else
    /// (HandOwner; Tables/Player/HandUses.csv grenade.throw): the gun keeps the right hand, so it stays aimed, and fires,
    /// with the grenade held ready in the left (the user, 2026-11-18: "오른손은 조준한 채로 왼손은 수류탄 대기하고 있으면
    /// 되잖아요"). What the gun was doing with both hands is over by the gun's own word (a reload under way loses the
    /// hand it needs and stops: WeaponController.MindHands), and a reload asked for while the grenade is held does not
    /// start at all. Until 2026-11-18 this reached into the gun and stopped its reload itself, again every frame.
    /// </summary>
    private void Begin()
    {
        if (!TakeHand()) return;
        StartArm(kind);
        pressedAt = Time.time;
        holding = letGo = keyLost = false;
        if (kind == Throwable.Knife) KnifeOut(HandPoint());
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

    // Something must have the hand (HandGone), or the class changed: the pin goes back in and the hand puts it back on
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
