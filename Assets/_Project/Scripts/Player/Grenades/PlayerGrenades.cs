using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// G: the class throwable (Throwables; the user, 2026-11-12: "클래스별로 수류탄이 달랐으면 좋겠고 gmod 호드 모드처럼
/// G키로 퀵 그레네이드 던질 수 있었음 좋겠어"). As GMod Horde's quick grenade: the gun stays in hand and the throwable goes
/// from the left hand toward the crosshair the moment G is tapped (Horde throws it on the press; here a tap shorter than
/// GrenadeRules tapHold throws on its release, so holding can aim). Held longer, G aims as T aims the stone: the arc it
/// will fly (ThrowArc) and, where it lands, the reach of its burst; let go, it is thrown. The guard key while holding puts
/// the pin back: nothing is thrown, nothing spent. One that cooks (the Point's flashbang) starts its fuse at the press:
/// let go late and it bursts in the air; hold it out and it bursts in the hand, round the Point. A few are carried (the
/// table's carry: a fresh kit holds them all, every chapter and every class pick); ammo piles, the shop and a Support
/// give some back, and a Knight walks over its thrown knives to take them back. Horde's 0.35 s guard between throws
/// stays (GrenadeRules cooldown). On every player: this PC's own throws, and friends' throws copied here
/// (PlayerNetworkCombat). The HUD shows the count beside the ammo (GrenadeHUD).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerGrenades : MonoBehaviour
{
    /// <summary>This PC's player's.</summary>
    public static PlayerGrenades Local { get; private set; }

    /// <summary>This PC's player threw one: what, its number, from where, how fast, its fuse left (s). For friends' PCs.</summary>
    public event Action<Throwable, ushort, Vector3, Vector3, float> Thrown;
    /// <summary>This PC's player's throwable burst there (its fuse ran out in the air, or in the hand): friends' copies burst there too.</summary>
    public event Action<Throwable, ushort, Vector3> Burst;
    /// <summary>This PC's player took a thrown knife back: friends' copies of it go.</summary>
    public event Action<ushort> KnifeTaken;

    private PlayerMovement movement;
    private WeaponController weapon;
    private ThrowArc arc;
    private Throwable kind;
    private int count;
    private bool pressed, aiming, cooking, wired;
    private float pressedAt, cookLeft, readyAt, pickLeft;
    private ushort nextId = 1;
    private string classSeen;
    // This player's throwables in the world on this PC: their own, or the copies of a friend's.
    private readonly Dictionary<ushort, ThrownGrenade> flying = new Dictionary<ushort, ThrownGrenade>();
    private readonly Dictionary<ushort, ThrownKnife> knives = new Dictionary<ushort, ThrownKnife>();
    private readonly List<ushort> gone = new List<ushort>();

    /// <summary>What this player carries (their class's), and how many are left.</summary>
    public Throwable Kind => kind;
    public int Count => count;
    public int Carry => Info != null ? Info.Carry : 0;
    private ThrowableInfo Info => Throwables.Of(kind);

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
    }

    private void OnDisable()
    {
        pressed = aiming = false;
        if (arc != null) arc.Hide();
    }

    // A class picked is a fresh kit: its throwable, all of them.
    private void OnPicked(string before, string now)
    {
        if (movement == null || !movement.IsLocal) return;
        classSeen = now;
        kind = Throwables.ForClass(now);
        count = Carry;
        pressed = aiming = cooking = false;
        if (arc != null) arc.Hide();
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
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
        if (cooking) Cook(info);
        if ((pickLeft -= Time.deltaTime) <= 0f) { pickLeft = .1f; TakeKnivesBack(); }
        if (info == null || kind == Throwable.None) GrenadeHUD.Hide();
        else GrenadeHUD.Show(kind, count, aiming, cooking && info.Fuse > 0f ? cookLeft / info.Fuse : -1f);
    }

    // ---- The key ----

    private void Hand(ThrowableInfo info)
    {
        var key = KeyBindings.Find("Player/Grenade");
        bool able = info != null && key != null && movement.InputAvailable && TakesInput() && !movement.IsDead && !movement.IsHeld
            && !movement.CombatInputLocked && !HandsFull();
        if (!able)
        {
            // A cooked flashbang keeps burning in the hand whatever takes the hands (a grab bursts it round its holder).
            if (!cooking) { pressed = false; StopAim(); }
            return;
        }
        if (!pressed && KeyBindings.Pressed(key))
        {
            if (count <= 0) { GrenadeHUD.Refused(); return; }
            if (Time.time < readyAt) return;
            pressed = true;
            pressedAt = Time.time;
            if (kind != Throwable.Knife) Throwables.Play("Pin", HandPoint(), .7f, 1f, 15f);
            if (info.Cooks) { cooking = true; cookLeft = info.Fuse; }
        }
        if (!pressed) return;
        // The guard key puts the pin back: nothing thrown, nothing spent.
        if (KeyBindings.Pressed("Combat/Guard"))
        {
            pressed = cooking = false;
            StopAim();
            return;
        }
        if (KeyBindings.Held(key))
        {
            if (Time.time - pressedAt >= Mathf.Max(0f, Throwables.Rule("tapHold", .16f))) Aim(info);
            return;
        }
        pressed = false;
        StopAim();
        Throw(info);
    }

    private bool HandsFull() => TryGetComponent(out PlayerWeaponSlots slots) && slots.HandsFull;

    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard;

    // A cooking flashbang's fuse: out in the hand, it bursts there.
    private void Cook(ThrowableInfo info)
    {
        if (movement.IsDead) { cooking = pressed = false; StopAim(); return; }
        cookLeft -= Time.deltaTime;
        if (cookLeft > 0f) return;
        cooking = pressed = false;
        StopAim();
        if (count <= 0 || info == null) return;
        count--;
        readyAt = Time.time + Cooldown;
        ushort id = NextId();
        Vector3 at = HandPoint();
        Throwables.NoteThrower(movement, kind);
        GrenadeBursts.Play(kind, at, movement, true);
        Burst?.Invoke(kind, id, at);
        PlayerCallouts.Thrown(info.Callout);
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
        float fuse = info.Cooks ? Mathf.Max(0f, cookLeft) : info.Fuse;
        cooking = false;
        Throwables.NoteThrower(movement, kind);
        if (kind == Throwable.Knife)
        {
            var knife = ThrownKnife.Throw(id, from, velocity, movement, true);
            if (knife != null) knives[id] = knife;
            Throwables.Play("KnifeThrow", from, .9f, 1.5f, 20f);
        }
        else
        {
            var grenade = ThrownGrenade.Throw(kind, id, from, velocity, fuse, movement, true, OnOwnBurst);
            if (grenade != null) flying[id] = grenade;
            Throwables.Play("Throw", from, .8f, 1.5f, 20f);
        }
        Thrown?.Invoke(kind, id, from, velocity, fuse);
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

    // Out of the left hand, toward what the crosshair is on, so it goes where the player looks; a knife nearly straight.
    private void Launch(Transform eye, ThrowableInfo info, out Vector3 from, out Vector3 velocity)
    {
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

    // The throw felt: the gun nods as the left hand goes, and the view gives a small kick that way.
    private void Feel()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) FPSBody.Holding(weapon)?.Point();
        var eye = Camera.main;
        if (eye != null) CameraViewPunch.For(eye)?.Punch(new Vector3(-1.2f, -.9f, 2f));
    }

    // ---- Aiming ----

    // Held: the arc it will fly, and where it lands the reach of its burst; a cooking one also shows where its fuse runs
    // out along the way, if that comes first (an air burst).
    private void Aim(ThrowableInfo info)
    {
        var eye = Camera.main;
        if (eye == null) return;
        aiming = true;
        Launch(eye.transform, info, out Vector3 from, out Vector3 velocity);
        if (arc == null) arc = new ThrowArc(transform);
        arc.Trace(from, velocity, Mathf.Max(.01f, info.Size * .5f), info.Gravity);
        bool ready = Time.time >= readyAt;
        arc.Draw(ready ? KitStyle.Ice : KitStyle.With(KitStyle.Ice, .35f), ready ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f), ready ? .85f : .4f);
        if (info.Radius <= 0f) { arc.HideRing(); return; }
        Color reach = KitStyle.With(info.Tint, .8f);
        if (cooking && cookLeft < arc.FlightSeconds)
        {
            float t = Mathf.Max(0f, cookLeft);
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

    /// <summary>A friend threw one: this PC throws its own copy (their PC's decides where it bursts).</summary>
    internal void ShowThrow(Throwable thrownKind, ushort id, Vector3 from, Vector3 velocity, float fuse)
    {
        Throwables.NoteThrower(movement, thrownKind);
        if (thrownKind == Throwable.Knife)
        {
            var knife = ThrownKnife.Throw(id, from, velocity, movement, false);
            if (knife != null) knives[id] = knife;
            Throwables.Play("KnifeThrow", from, .8f, 1.5f, 20f);
            return;
        }
        var grenade = ThrownGrenade.Throw(thrownKind, id, from, velocity, fuse, movement, false, null);
        if (grenade != null) flying[id] = grenade;
        Throwables.Play("Throw", from, .7f, 1.5f, 20f);
    }

    /// <summary>A friend's throwable burst there: this PC's copy bursts in that spot (or, held too long, in their hand).</summary>
    internal void ShowBurst(Throwable thrownKind, ushort id, Vector3 at)
    {
        Throwables.NoteThrower(movement, thrownKind);
        if (flying.TryGetValue(id, out var grenade) && grenade != null)
        {
            flying.Remove(id);
            grenade.BurstAt(at);
            return;
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
