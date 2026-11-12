using System;
using System.Collections.Generic;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// G: the class throwable (Throwables; the user, 2026-11-12: "클래스별로 수류탄이 달랐으면 좋겠고 gmod 호드 모드처럼
/// G키로 퀵 그레네이드 던질 수 있었음 좋겠어"), thrown by the off hand while the gun stays in the other (GrenadeArm, along
/// the path of GMod's VManip quick grenade). The user again: "그냥 G 꾹 누르면 들고 있는 자세로 바뀌면서 쿠킹할 수 있고
/// 걍 바로 누르면 콜오브듀티 퀵그레네이드처럼 바로 던지는 모션만 나오게".
/// A tap (let go before GrenadeRules tapHold) is Call of Duty's quick throw: the hand takes it from the belt and throws it
/// toward the crosshair in one quick motion (tapDraw, then throwSpeed), no arc, no stop. Held, the hand comes up and holds
/// it out (VManip's hold), the pin pulled with its sound; the arc it will fly and the reach of its burst show (ThrowArc,
/// from where the hand lets go, so the arc is the flight), and let go, it is thrown. The guard key while it is held puts
/// the pin back: nothing thrown, nothing spent.
/// One that cooks (the Assault's frag, the Point's flashbang) burns from the pin: its fuse runs down in a ring round the
/// crosshair (GrenadeHUD.Ring), so it can be let go to burst in the air. Never a minus: a frag held to the end is thrown on
/// reflex at the last moment (the Assault counts the fuse and the hand lets go by itself: GrenadeRules reflexLead), and a
/// flashbang held to the end goes off in the hand, which only staggers the zombies round its holder. Knife, smoke,
/// canister and dye don't cook; holding them only aims. The Support's claymore is set down, not thrown (the user: "던지는
/// 느낌보단 설치로 가도 되긴 하겠다"): held, G shows the spot under the crosshair and the fan it will guard
/// (ClaymorePreview); let go (or tapped), the hand puts it there (ClaymoreMine), and the oldest comes back off past
/// GrenadeRules claymoreMost.
/// A few are carried (the table's carry: a fresh kit holds them all, every chapter and every class pick); ammo piles, the
/// shop and a Support give some back, and a Knight walks over its thrown knives to take them back. Horde's 0.35 s guard
/// between throws stays (cooldown). On every player: this PC's own throws, and friends' throws and arm copied here
/// (PlayerNetworkCombat). The HUD shows the count beside the ammo (GrenadeHUD).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerGrenades : MonoBehaviour
{
    /// <summary>What a throw's off hand does next, sent to friends' PCs so the copy's arm does it too.</summary>
    public enum HandPhase : byte { None, Draw, Throw, TapThrow, Lower }

    // Where the off hand is in a throw: going for it (the draw), holding it out, throwing it, or putting it back.
    private enum Arm : byte { None, Draw, Hold, Throw, Lower }

    /// <summary>This PC's player's.</summary>
    public static PlayerGrenades Local { get; private set; }

    /// <summary>This PC's player threw one: what, its number, from where, how fast, its fuse left (s). For friends' PCs.</summary>
    public event Action<Throwable, ushort, Vector3, Vector3, float> Thrown;
    /// <summary>This PC's player's throwable burst there (its fuse ran out in the air, or in the hand): friends' copies burst there too.</summary>
    public event Action<Throwable, ushort, Vector3> Burst;
    /// <summary>This PC's player took a thrown knife back: friends' copies of it go.</summary>
    public event Action<ushort> KnifeTaken;
    /// <summary>This PC's player's off hand went for a throwable, threw it or put it back: friends' copies move their arm so.</summary>
    public event Action<Throwable, HandPhase> Handed;
    /// <summary>This PC's player set a claymore down: its number, where, how it is turned, and whether on a wall.</summary>
    public event Action<Throwable, ushort, Vector3, Quaternion, bool> Placed;

    private PlayerMovement movement;
    private WeaponController weapon;
    private ThrowArc arc;
    private GrenadeArm armBody;
    private Throwable kind, armKind;
    private Arm arm;
    private int count;
    private bool holding, aiming, cooking, wired, letGo, released, pinned;
    private float pressedAt, cookLeft, readyAt, pickLeft, cycle, drawSpeed, lowerLeft;
    private ushort nextId = 1;
    private string classSeen;
    // This player's throwables in the world on this PC: their own, or the copies of a friend's.
    private readonly Dictionary<ushort, ThrownGrenade> flying = new Dictionary<ushort, ThrownGrenade>();
    private readonly Dictionary<ushort, ThrownKnife> knives = new Dictionary<ushort, ThrownKnife>();
    private readonly List<ushort> gone = new List<ushort>();
    // This player's claymores set down, oldest first (theirs on this PC, or the copies of a friend's).
    private readonly List<ClaymoreMine> mines = new List<ClaymoreMine>();
    private ClaymorePreview preview;

    /// <summary>What this player carries (their class's), and how many are left.</summary>
    public Throwable Kind => kind;
    public int Count => count;
    public int Carry => Info != null ? Info.Carry : 0;
    private ThrowableInfo Info => Throwables.Of(kind);
    // The hand motion of what the off hand has now (Tables/ThrowArms: the grenade's, or the knife's).
    private GrenadeArmPath.Motion Motion => GrenadeArmPath.For(armKind);

    /// <summary>
    /// The player's off hand is on a throw (going for it, holding it, throwing it, putting it back): anything else that
    /// would use that hand (a held F's point) waits. On every PC, for the player and for friends' copies.
    /// </summary>
    public static bool HandBusy(PlayerMovement who) => who != null && who.TryGetComponent(out PlayerGrenades grenades) && grenades.arm != Arm.None;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        PlayerClass.Picked += OnPicked;
        wired = true;
    }

    private void OnDestroy()
    {
        if (wired) PlayerClass.Picked -= OnPicked;
        if (Local == this) Local = null;
        if (arc != null) arc.Hide();
        preview?.Hide();
        // Their claymores go with them.
        foreach (var claymore in mines)
            if (claymore != null) Destroy(claymore.gameObject);
        mines.Clear();
    }

    private void OnDisable()
    {
        holding = aiming = cooking = letGo = false;
        arm = Arm.None;
        if (arc != null) arc.Hide();
        preview?.Hide();
        if (Local == this) GrenadeHUD.Ring(-1f);
    }

    // A class picked is a fresh kit: its throwable, all of them.
    private void OnPicked(string before, string now)
    {
        if (movement == null || !movement.IsLocal) return;
        classSeen = now;
        kind = Throwables.ForClass(now);
        count = Carry;
        if (arm == Arm.Draw || arm == Arm.Hold) PutBack();
        cooking = false;
    }

    private void Update()
    {
        if (movement == null) return;
        float dt = Time.deltaTime;
        if (!movement.IsLocal)
        {
            // A friend's copy: only its arm, from the phases their PC sends.
            Run(dt, Throwables.Of(armKind));
            return;
        }
        Local = this;
        // The class's throwable, all of them, as the player first comes (a chapter's start) or the class changes.
        string id = PlayerClass.CurrentId;
        if (id != classSeen)
        {
            classSeen = id;
            var now = Throwables.ForClass(id);
            if (now != kind) { kind = now; count = Carry; }
        }
        var info = Info;
        Hand(info);
        if (cooking) Cook(Throwables.Of(armKind));
        Run(dt, Throwables.Of(armKind));
        KeepHandFree();
        var aimed = Throwables.Of(armKind);
        if (holding && (arm == Arm.Draw || arm == Arm.Hold) && aimed != null)
        {
            if (aimed.Places) Preview();
            else Aim(aimed);
        }
        else StopAim();
        if ((pickLeft -= dt) <= 0f) { pickLeft = .1f; TakeKnivesBack(); }
        var held = Throwables.Of(armKind);
        float fuse = cooking && held != null && held.Fuse > 0f ? cookLeft / held.Fuse : -1f;
        if (info == null || kind == Throwable.None) GrenadeHUD.Hide();
        else GrenadeHUD.Show(kind, count, aiming, fuse);
        // The fuse round the crosshair while one is held cooking (a tap's few tenths go by without it).
        GrenadeHUD.Ring(fuse >= 0f && !released && (holding || arm == Arm.Hold) ? fuse : -1f);
    }

    // ---- The key ----

    private void Hand(ThrowableInfo info)
    {
        var key = KeyBindings.Find("Player/Grenade");
        bool able = info != null && key != null && movement.InputAvailable && TakesInput() && Able();
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

    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard;

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
        StartArm(kind, GrenadeArmPath.For(kind).HoldDraw);
        pressedAt = Time.time;
        holding = letGo = false;
        if (kind == Throwable.Knife) KnifeOut(HandPoint());
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.InterruptForSurvival();
        Handed?.Invoke(kind, HandPhase.Draw);
    }

    // G let go (or the reflex): thrown from the hold at once, or as soon as the draw gets there. A tap's draw runs faster
    // and never stops at the hold.
    private void LetGo(bool tap)
    {
        letGo = true;
        if (tap) drawSpeed = Motion.TapDraw;
        Handed?.Invoke(armKind, tap ? HandPhase.TapThrow : HandPhase.Throw);
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
            var motion = Motion;
            float windup = (motion.Release - motion.Hold) / Mathf.Max(.1f, motion.ThrowSpeed);
            if (arm == Arm.Hold && !letGo && cookLeft <= Throwables.Rule("reflexLead", .5f) + windup) LetGo(false);
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

    // ---- The arm: on this PC's player and on friends' copies ----

    private void StartArm(Throwable throwable, float speed)
    {
        arm = Arm.Draw;
        armKind = throwable;
        cycle = 0f;
        drawSpeed = speed;
        released = pinned = false;
    }

    private void StartThrow()
    {
        arm = Arm.Throw;
        cycle = Mathf.Max(cycle, Motion.Hold);
    }

    // Let go at the hold: thrown, or set down (a claymore: on this PC's player's own PC; a friend's copy waits to be told
    // where, ShowMine, and only lowers its hand).
    private void Release()
    {
        var held = Throwables.Of(armKind);
        if (held == null || !held.Places) { StartThrow(); return; }
        if (movement.IsLocal) Put(held);
        else Lower();
    }

    // ---- Setting a claymore down ----

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

    // Moves the arm along the path, pulls the pin and lets go at their moments, and tells the body's arm (GrenadeArm).
    private void Run(float dt, ThrowableInfo info)
    {
        if (arm == Arm.None) return;
        var motion = Motion;
        float hold = motion.Hold;
        switch (arm)
        {
            case Arm.Draw:
                cycle += dt * drawSpeed;
                if (!pinned && motion.HasPin && cycle >= motion.Pin) Pin(info);
                if (cycle >= hold)
                {
                    cycle = hold;
                    arm = Arm.Hold;
                    if (letGo) Release();
                }
                break;
            case Arm.Throw:
                cycle += dt * Mathf.Max(.1f, motion.ThrowSpeed);
                if (!released && cycle >= motion.Release)
                {
                    released = true;
                    if (movement.IsLocal) Throw(info);
                }
                if (cycle >= motion.End) arm = Arm.None;
                break;
            case Arm.Lower:
                if ((lowerLeft -= dt) <= 0f) arm = Arm.None;
                break;
        }
        if (armBody == null) armBody = GrenadeArm.Of(movement);
        // On the throwable while going for it, holding it and throwing it, until the path's hand heads back to the gun.
        bool on = arm == Arm.Draw || arm == Arm.Hold || (arm == Arm.Throw && cycle < motion.Back);
        if (armBody != null) armBody.Drive(armKind, cycle, on, !released);
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
        if (arm != Arm.Draw && arm != Arm.Hold && !(arm == Arm.Throw && !released)) return;
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null && (weapon.AimBlend > .01f || (weapon.Runtime != null && weapon.Runtime.IsReloading))) weapon.InterruptForSurvival();
    }

    // ---- The throw ----

    private void Throw(ThrowableInfo info)
    {
        var eye = Camera.main;
        if (eye == null || info == null || count <= 0) { cooking = false; return; }
        Launch(eye.transform, info, out Vector3 from, out Vector3 velocity);
        count--;
        readyAt = Time.time + Cooldown;
        ushort id = NextId();
        // A cooked one flies with what is left of its fuse; one thrown on reflex keeps at least half the reflex's lead, so
        // even a stalled frame can't make it burst at the hand.
        float fuse = !info.Cooks ? info.Fuse
            : info.ThrowsOut ? Mathf.Max(cookLeft, Throwables.Rule("reflexLead", .5f) * .5f) : Mathf.Max(0f, cookLeft);
        cooking = holding = letGo = false;
        StopAim();
        Throwables.NoteThrower(movement, armKind);
        if (armKind == Throwable.Knife)
        {
            var knife = ThrownKnife.Throw(id, from, velocity, movement, true);
            if (knife != null) knives[id] = knife;
            Throwables.Play("KnifeThrow", from, .9f, 1.5f, 20f);
            Throwables.Play("KnifeThrowCloth", from, .8f, 1.5f, 15f);
        }
        else
        {
            var grenade = ThrownGrenade.Throw(armKind, id, from, velocity, fuse, movement, true, OnOwnBurst);
            if (grenade != null) flying[id] = grenade;
            Throwables.Play("Throw", from, .8f, 1.5f, 20f);
        }
        Thrown?.Invoke(armKind, id, from, velocity, fuse);
        Feel();
        PlayerCallouts.Thrown(info.Callout);
    }

    private void OnOwnBurst(ThrownGrenade grenade)
    {
        if (grenade == null) return;
        flying.Remove(grenade.Id);
        Burst?.Invoke(grenade.Kind, grenade.Id, grenade.Position);
    }

    private static float Cooldown => Mathf.Max(0f, Throwables.Rule("cooldown", .35f));

    private ushort NextId()
    {
        if (nextId == 0) nextId = 1;
        return nextId++;
    }

    // From where the hand lets go (GrenadeArm: the path's release on this body), toward what the crosshair is on, so it goes
    // where the player looks; a knife nearly straight. Without an arm (no humanoid body), from beside the eyes.
    private void Launch(Transform eye, ThrowableInfo info, out Vector3 from, out Vector3 velocity)
    {
        if (armBody == null) armBody = GrenadeArm.Of(movement);
        if (armBody == null || !armBody.TryReleasePoint(out from))
            from = eye.position + eye.forward * .35f - eye.right * .16f - eye.up * .12f;
        if (Physics.Linecast(eye.position, from, out RaycastHit wall, Throwables.WorldMask, QueryTriggerInteraction.Ignore))
            from = wall.point - (from - eye.position).normalized * .08f;
        Vector3 target = Physics.Raycast(eye.position, eye.forward, out RaycastHit seen, 80f, Throwables.WorldMask, QueryTriggerInteraction.Ignore)
            ? seen.point : eye.position + eye.forward * 80f;
        Vector3 way = (target - eye.position).sqrMagnitude > 4f ? (target - from).normalized : eye.forward;
        Vector3 carried = movement.ActualVelocity;
        carried.y = 0f;
        velocity = way * info.Speed + Vector3.up * info.Lift + carried * .5f;
    }

    private Vector3 HandPoint()
    {
        var eye = Camera.main;
        if (eye == null) return transform.position + Vector3.up * 1.3f;
        var view = eye.transform;
        return view.position + view.forward * .45f - view.right * .16f - view.up * .25f;
    }

    // The throw felt: the gun nods as the left hand goes, and the view gives a small kick that way; a knife's flick
    // dips the view and rolls it a little, as GMod's [MW] Quick Throwing Knife does (ViewPunch 2 down, a short roll).
    private void Feel()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) FPSBody.Holding(weapon)?.Point();
        var eye = Camera.main;
        if (eye == null) return;
        if (armKind == Throwable.Knife) CameraViewPunch.For(eye)?.Punch(new Vector3(2f, 0f, -1.5f));
        else CameraViewPunch.For(eye)?.Punch(new Vector3(-1.2f, -.9f, 2f));
    }

    // A knife drawn for the throw: the blade and the sleeve's sounds together, as the addon's pull-out.
    private static void KnifeOut(Vector3 at)
    {
        Throwables.Play("KnifeDraw", at, .8f, 1.2f, 12f);
        Throwables.Play("KnifeDrawCloth", at, .7f, 1.2f, 10f);
    }

    // ---- Aiming ----

    // Held: the arc it will fly from where the hand lets go, and where it lands the reach of its burst; a cooking one also
    // shows where its fuse runs out along the way, if that comes first (an air burst).
    private void Aim(ThrowableInfo info)
    {
        var eye = Camera.main;
        if (eye == null || info == null) return;
        aiming = true;
        Launch(eye.transform, info, out Vector3 from, out Vector3 velocity);
        if (arc == null) arc = new ThrowArc(transform);
        arc.Trace(from, velocity, Mathf.Max(.01f, info.Size * .5f), info.Gravity);
        bool ready = Time.time >= readyAt;
        arc.Draw(ready ? KitStyle.Ice : KitStyle.With(KitStyle.Ice, .35f), ready ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f), ready ? .85f : .4f);
        if (info.Radius <= 0f) { arc.HideRing(); return; }
        Color reach = KitStyle.With(info.Tint, .8f);
        // The fuse left when it leaves the hand: what is left now less the swing still to come.
        var motion = Motion;
        float swing = (motion.Release - Mathf.Max(cycle, motion.Hold)) / Mathf.Max(.1f, motion.ThrowSpeed);
        float left = cookLeft - Mathf.Max(0f, swing);
        if (cooking && left < arc.FlightSeconds)
        {
            float t = Mathf.Max(0f, left);
            Vector3 air = from + velocity * t + .5f * t * t * Physics.gravity * info.Gravity;
            arc.Ring(air, Vector3.up, info.Radius, KitStyle.With(KitStyle.Accent, .85f));
            return;
        }
        if (arc.Landed) arc.Ring(arc.Landing, arc.LandingNormal, info.Radius, reach);
        else arc.HideRing();
    }

    private void StopAim()
    {
        aiming = false;
        arc?.Hide();
        preview?.Hide();
    }

    // ---- Knives back ----

    // A Knight's own knives that have come to rest, within reach: back in the belt, while there is room.
    private void TakeKnivesBack()
    {
        if (knives.Count == 0) return;
        gone.Clear();
        float reach = Mathf.Max(.3f, Throwables.Rule("pickupReach", 1.6f));
        Vector3 chest = transform.position + Vector3.up;
        foreach (var pair in knives)
        {
            var knife = pair.Value;
            if (knife == null) { gone.Add(pair.Key); continue; }
            if (!knife.Landed || kind != Throwable.Knife || count >= Carry || movement.IsDead) continue;
            if ((knife.transform.position - chest).sqrMagnitude > reach * reach) continue;
            gone.Add(pair.Key);
            count++;
            knife.Take();
            KnifeTaken?.Invoke(pair.Key);
            KitSound.Play(KitSound.Cue.PickAmmo, .6f);
            GrenadeHUD.Blink(KitStyle.Ice);
        }
        foreach (ushort id in gone) knives.Remove(id);
    }

    // ---- Friends' throws, here ----

    /// <summary>
    /// A friend's off hand went for a throwable, threw it or put it back: this PC's copy of them moves its arm so (a tap's
    /// draw runs at the tap's speed). The throwable itself comes with ShowThrow.
    /// </summary>
    internal void ShowHand(Throwable handKind, HandPhase phase)
    {
        switch (phase)
        {
            case HandPhase.Draw:
                StartArm(handKind, GrenadeArmPath.For(handKind).HoldDraw);
                letGo = false;
                if (handKind == Throwable.Knife) KnifeOut(transform.position + Vector3.up * 1.3f);
                break;
            case HandPhase.Throw:
            case HandPhase.TapThrow:
                if (phase == HandPhase.TapThrow) drawSpeed = GrenadeArmPath.For(handKind).TapDraw;
                // A claymore is set down, not thrown: the copy's hand lowers when it is told where it went (ShowMine).
                if (Throwables.Of(handKind) is ThrowableInfo setDown && setDown.Places) { letGo = true; break; }
                if (arm == Arm.None || arm == Arm.Lower)
                {
                    // Its draw never came: straight into the throw from the hold.
                    StartArm(handKind, drawSpeed);
                    arm = Arm.Hold;
                    cycle = GrenadeArmPath.For(handKind).Hold;
                }
                letGo = true;
                if (arm == Arm.Hold) StartThrow();
                break;
            case HandPhase.Lower:
                if (arm == Arm.Draw || arm == Arm.Hold) Lower();
                break;
        }
    }

    /// <summary>A friend threw one: this PC throws its own copy (their PC's decides where it bursts).</summary>
    internal void ShowThrow(Throwable thrownKind, ushort id, Vector3 from, Vector3 velocity, float fuse)
    {
        Throwables.NoteThrower(movement, thrownKind);
        // It has left their hand: the copy's hand lets go of it too, wherever its arm has got to.
        released = true;
        if (thrownKind == Throwable.Knife)
        {
            var knife = ThrownKnife.Throw(id, from, velocity, movement, false);
            if (knife != null) knives[id] = knife;
            Throwables.Play("KnifeThrow", from, .8f, 1.5f, 20f);
            Throwables.Play("KnifeThrowCloth", from, .7f, 1.5f, 15f);
            return;
        }
        var grenade = ThrownGrenade.Throw(thrownKind, id, from, velocity, fuse, movement, false, null);
        if (grenade != null) flying[id] = grenade;
        Throwables.Play("Throw", from, .7f, 1.5f, 20f);
    }

    /// <summary>A friend set a claymore down there: this PC sets its own copy down (their PC fires it, ShowBurst).</summary>
    internal void ShowMine(ushort id, Vector3 at, Quaternion facing, bool wall)
    {
        Keep(ClaymoreMine.Place(id, at, facing, wall, movement, false, null));
        Throwables.Play("Bounce", at, .6f, .7f, 15f);
        if (arm == Arm.Draw || arm == Arm.Hold) Lower();
    }

    /// <summary>A friend's throwable burst there: this PC's copy bursts in that spot (or, held too long, in their hand).</summary>
    internal void ShowBurst(Throwable thrownKind, ushort id, Vector3 at)
    {
        Throwables.NoteThrower(movement, thrownKind);
        // Their claymore fired: its copy here fires the same way from where it lies.
        if (thrownKind == Throwable.Claymore)
        {
            var claymore = mines.Find(m => m != null && m.Id == id);
            if (claymore != null)
            {
                mines.Remove(claymore);
                claymore.BurstAt(at);
                return;
            }
        }
        if (flying.TryGetValue(id, out var grenade) && grenade != null)
        {
            flying.Remove(id);
            grenade.BurstAt(at);
            return;
        }
        // Gone off in their hand: the copy's hand has nothing left to hold.
        if (arm == Arm.Draw || arm == Arm.Hold)
        {
            released = true;
            Lower();
        }
        GrenadeBursts.Play(thrownKind, at, movement, false);
    }

    /// <summary>A friend took a thrown knife back: this PC's copy of it goes.</summary>
    internal void ShowKnifeTaken(ushort id)
    {
        if (knives.TryGetValue(id, out var knife) && knife != null) knife.Take();
        knives.Remove(id);
    }

    // ---- Some back ----

    /// <summary>
    /// Gives this player's own PC that many more, up to what is carried; returns how many it took. The HUD blinks for any.
    /// </summary>
    public static int Refill(PlayerMovement who, int amount)
    {
        if (who == null || amount <= 0 || !who.TryGetComponent(out PlayerGrenades grenades) || grenades.kind == Throwable.None) return 0;
        int room = Mathf.Max(0, grenades.Carry - grenades.count), given = Mathf.Min(room, amount);
        if (given <= 0) return 0;
        grenades.count += given;
        if (who.IsLocal) GrenadeHUD.Blink(KitStyle.Ice);
        return given;
    }

    /// <summary>Whether this player has room for more of their throwable (an ammo pile may go for it alone).</summary>
    public static bool HasRoom(PlayerMovement who)
        => who != null && who.TryGetComponent(out PlayerGrenades grenades) && grenades.kind != Throwable.None && grenades.count < grenades.Carry;

    /// <summary>An ammo pile was taken on the field (FieldPickups): its table's refill.</summary>
    public static int FromAmmoPile(PlayerMovement who)
    {
        var mine = who != null && who.TryGetComponent(out PlayerGrenades grenades) ? grenades.Info : null;
        return mine != null ? Refill(who, mine.Refill) : 0;
    }

    /// <summary>
    /// A Support's resupply reached this player (SupportKit.Receive, on their own PC): GrenadeRules resupply of them, as
    /// the walking shop hands out magazines.
    /// </summary>
    public static int Resupplied(PlayerMovement who) => Refill(who, Mathf.Max(0, Mathf.RoundToInt(Throwables.Rule("resupply", 1f))));
}
