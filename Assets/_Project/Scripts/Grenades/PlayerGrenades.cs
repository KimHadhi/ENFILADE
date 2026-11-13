using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// G: the class throwable (Throwables; the user, 2026-11-12: "클래스별로 수류탄이 달랐으면 좋겠고 gmod 호드 모드처럼
/// G키로 퀵 그레네이드 던질 수 있었음 좋겠어"), thrown by the off hand while the gun stays in the other (GrenadeArm, along
/// the path of GMod's VManip quick grenade). The user again: "그냥 G 꾹 누르면 들고 있는 자세로 바뀌면서 쿠킹할 수 있고
/// 걍 바로 누르면 콜오브듀티 퀵그레네이드처럼 바로 던지는 모션만 나오게".
/// The hand plays its kind's clips (GrenadeArmPath; the user, 2026-11-12: "드는 모션, 대기 모션, 던지는 모션 내가 블렌더로
/// 수정할 수 있게 만들어서 적용해 봐": each a table the Blender throw workspace ArtSource/Grenades sends). A tap (let go
/// before GrenadeRules tapHold) is Call of Duty's quick throw: the tap clip, the hand taking it from the belt and throwing
/// it toward the crosshair in one quick motion, no arc, no stop (the knife's tap is its throw, as the addon throws at once).
/// Held, the draw brings it up, the pin pulled at the draw's pin moment with its sound, and the hold clip plays round; the
/// arc it will fly and the reach of its burst show (ThrowArc, from where the hand lets go, so the arc is the flight), and
/// let go, the throw clip lets it go at its release moment. The guard key while it is held puts the pin back: nothing
/// thrown, nothing spent.
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
public sealed partial class PlayerGrenades : MonoBehaviour
{
    /// <summary>What a throw's off hand does next, sent to friends' PCs so the copy's arm does it too.</summary>
    public enum HandPhase : byte { None, Draw, Throw, TapThrow, Lower }

    // Where the off hand is in a throw: going for it (the draw), holding it out, throwing it, or putting it back.
    private enum Arm : byte { None, Draw, Hold, Throw, Tap, Lower }

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
    // clip: the arm's clip playing (a table of GrenadeArmPath); clipTime: how far into it (s at its own speed); speed: how
    // fast it plays (its motion's draw, tap or throw speed).
    private float pressedAt, cookLeft, readyAt, pickLeft, clipTime, speed, lowerLeft, knifeShakeAt;
    private string clip = "";
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
        // The knife's roll comes back a moment after the throw, a short shake as the addon's (Feel).
        if (knifeShakeAt > 0f && Time.time >= knifeShakeAt)
        {
            knifeShakeAt = 0f;
            var view = Camera.main;
            if (view != null) CameraViewPunch.For(view).OrNull()?.Punch(new Vector3(0f, 0f, -.6f * Throwables.Rule("knifeRoll", 2f)));
        }
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
}
