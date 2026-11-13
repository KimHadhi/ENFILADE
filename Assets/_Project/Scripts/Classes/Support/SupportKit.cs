using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Stage 4 of the classes, the Support (the user's design, 2026-11-11: "아군을 위한 클래스 … Guts &amp; Blackpowder의
/// Sapper처럼 … 임시 바리케이드 … KF2의 서포트처럼 아군에게 총알이나 보급품을 보충 … 걸어다니는 간이 상점"), with their old
/// memo's Resupply and Fully Loaded. A supply bag that fills back by itself pays for both its jobs:
/// - Resupply: Interact (F) on a friend in reach, when F has nothing else to do there, hands them ammo: each of their guns
///   some magazines more, up to a quarter past what it starts with (Fully Loaded), and a Détente's syringe some more.
///   Their own PC fills it (PlayerNetworkCombat). KF2's Support hands ammo over with the use key too.
/// - Barricade: the class key held shows where one would go up, a see-through blue barricade exactly where and how it
///   would stand (BarricadeGhost), red where it can't, and let go it goes up there (the user, 2026-11-12: "능력 키 꾹
///   누르고 있으면 … 미리 보기 뜨면 좋을 듯. 설치 불가능하면 빨간색으로 뜨고 키 놓으면 거기에 배치"). A tap puts it up at
///   once where the preview stood, as a tap of the stone's T throws at once; the guard key (right click) while holding
///   calls it off. It stands where the view comes down to the ground, between the nearest and the farthest reach ahead,
///   across the look. At most a few stand at once; a new one past that takes the oldest down, which the preview shows
///   blinking amber. The host's or a lone PC's goes up at once; a client's PC asks the host, which checks the spot again
///   against its own world (Barricade.Judge) and puts it up for everyone, or says why not and the bag gets its share back.
/// - Mending: F held on a worn barricade in reach, anyone's, puts health back into it, its planks nailed back on (the
///   user, 2026-11-12: "G&amp;B 공병처럼 수리하고 유지 보수할 수 있으면 재밌을 듯"); a resupply goes first. It costs the
///   bag nothing: a hammer does it, and the welding torch card does it faster on a fuel of its own that fills back
///   ("처음에는 망치로 고쳐 … 나중에 카드 업그레이드하면 토치로 더 빠르게"), the tool in hand as it works (RepairLooks).
///   Any barricade under the crosshair, whole too, has its line at the foot of the screen (the tool, F held, its health)
///   and F held on it takes the tool out, so the Support finds the hammer before anything is worn (the user, 2026-11-12:
///   "서포트는 용접 토치나 망치 어디 있어? 어떻게 쓰는지 모르겠네").
/// - Other structures (the user, 2026-11-12: "서포트 다른 건축물 아이디어 지어도 괜찮을 듯"), each from its card: barbed wire,
///   an ammo crate and a searchlight (Barricade.Kind). While the class key is held, F goes through the ones held; each
///   has its own cost and most at once.
/// A class picked starts it afresh (Fresh, from ClassKit on PlayerClass.Picked): the bag as it starts, its barricades down.
/// This PC's own player only. Numbers: Tables/ClassAbilities, row Support.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class SupportKit : MonoBehaviour
{
    private const string Row = "Support";
    // How long a client waits for the host's word on a barricade it asked for before it may ask for another (s), and how
    // long the preview stays red where one was turned down (s).
    private const float AnswerWait = 3f, RefusedShow = .35f;
    // How often a client's hammer tells the host what it put in (s).
    private const float MendEvery = .2f;

    /// <summary>This PC's own player's, while they play a Support.</summary>
    public static SupportKit Local { get; private set; }

    /// <summary>
    /// This PC's Support put barricade id up at this spot, facing this way (on the host or alone, where it decides that
    /// itself): the others are told (PlayerNetworkCombat).
    /// </summary>
    public event Action<ushort, Vector3, float> Built;
    /// <summary>This PC's Support took barricade id down (for another past its most, or a class picked): the others are told.</summary>
    public event Action<ushort> TookDown;

    private PlayerMovement movement;
    private InputAction interact;
    private float supply = -1f, lastGive = -99f, lastBuild = -99f;
    private int made;
    // The preview while the class key is held: where and how it would stand now and whether it may; and the one to come
    // down for it. Both drawn by BarricadeGhost, made on first use.
    private BarricadeGhost ghost, doomed;
    private bool planning;
    // The kind the key puts up (F goes through those held while it is down).
    private Barricade.Kind planKind;
    private Barricade.Fit planFit = Barricade.Fit.Ok;
    private Vector3 planAt;
    private float planYaw;
    // A client's barricade asked of the host and not answered yet (0: none), what it took from the bag and when; one asked
    // for before a class pick, to come down again should it still go up; where the key was last let go (the preview stays
    // there while the host answers, or red a moment when turned down), and when one was last turned down.
    private ushort pendingId, dropped;
    private float pendingAt, pendingCost, refusedAt = -99f;
    private Vector3 heldSpot;
    private float heldYaw;
    private readonly List<ushort> taken = new List<ushort>();
    private int worldMask;
    // A client's mending not yet told the host: how much, on which barricade, and when it may tell next.
    private float mendOwed, nextMendSend;
    private ushort mendId;
    // The welding torch's fuel (-1: full at the next look), and the tool shown in hand.
    private float torchFuel = -1f;
    // Run dry: the hammer until the torch is full again (the user, 2026-11-12: "토치 바닥나면 완충되기 전까지 망치 쓰는 거 좋다").
    private bool torchDry;
    private RepairLooks.Tool shownTool;
    // Whether this Support's barricade line is up at the foot of the screen; the look's hits (AimedBarricade).
    private bool lineShown;
    private static readonly RaycastHit[] aimHits = new RaycastHit[16];

    /// <summary>The bag now and the most it holds; what a resupply and a barricade take from it.</summary>
    public float Supply => Mathf.Max(0f, supply);
    public float SupplyMax => Mathf.Max(1f, Number("supplyMax", 100f));
    public float GiveCost => Mathf.Max(1f, Number("resupplyCost", 25f));
    public float BuildCost => CostOf(planKind);
    public bool CanBuild => Supply >= BuildCost - .001f;
    /// <summary>How many of the chosen kind stand now, and the most at once.</summary>
    public int Standing => movement != null ? Barricade.CountOf(movement, planKind) : 0;
    public int MostStanding => MostOf(planKind);
    /// <summary>The kind the key puts up now.</summary>
    public Barricade.Kind PlanKind => planKind;

    /// <summary>What a kind takes from the bag, and the most of it at once (Support.barricade*, wire*, crate*, light*).</summary>
    public float CostOf(Barricade.Kind kind) => Mathf.Max(1f, kind switch
    {
        Barricade.Kind.Wire => Number("wireCost", 30f),
        Barricade.Kind.Crate => Number("crateCost", 50f),
        Barricade.Kind.Light => Number("lightCost", 60f),
        _ => Number("barricadeCost", 50f),
    });
    public int MostOf(Barricade.Kind kind) => Mathf.Max(1, Mathf.RoundToInt(kind switch
    {
        Barricade.Kind.Wire => Number("wireMax", 2f),
        Barricade.Kind.Crate => Number("crateMax", 1f),
        Barricade.Kind.Light => Number("lightMax", 1f),
        _ => Number("barricadeMax", 2f),
    }));

    // Whether this Support may put up this kind: the planks always, the rest with their cards.
    private static bool Holds(Barricade.Kind kind) => kind switch
    {
        Barricade.Kind.Wire => PlayerPerks.Has(PlayerPerks.SupportWire),
        Barricade.Kind.Crate => PlayerPerks.Has(PlayerPerks.SupportCrate),
        Barricade.Kind.Light => PlayerPerks.Has(PlayerPerks.SupportLight),
        _ => true,
    };
    /// <summary>Seconds since it last handed ammo over, and since it last put a barricade up (the HUD's flash).</summary>
    public float SinceGive => Time.time - lastGive;
    /// <summary>The class key is held to place a barricade (the preview is up), and whether one may go where it stands now.</summary>
    public bool Planning => planning;
    public Barricade.Fit PlanFit => planFit;

    /// <summary>The ultimate (every card taken, a full gauge: ClassCards.Ultimate): the bag full at once.</summary>
    internal void Restock()
    {
        supply = SupplyMax;
        lastGive = Time.time;
    }
    public float SinceBuild => Time.time - lastBuild;
    /// <summary>The friend F would resupply now (standing, in reach, under the crosshair), or null.</summary>
    public PlayerMovement Offered { get; private set; }
    /// <summary>
    /// The barricade under the crosshair in reach, worn or whole, anyone's (not an ammo crate, whose F is its own): its line
    /// shows at the foot of the screen, and F held there takes the tool out. Null when there is none.
    /// </summary>
    public Barricade AimedAt { get; private set; }
    /// <summary>That barricade while it is worn (F held mends it), or null; and whether F is held on it now, mending.</summary>
    public Barricade Mendable { get; private set; }
    public bool Mending { get; private set; }
    /// <summary>The welding torch card held; its fuel now and the most; whether it does the mending now (else the hammer).</summary>
    public bool HasTorch => PlayerPerks.Has(PlayerPerks.SupportTorch);
    public float TorchFuel => Mathf.Max(0f, torchFuel);
    public float TorchFuelMax => Mathf.Max(1f, Number("torchFuel", 100f));
    public bool Torching => HasTorch && !torchDry && TorchFuel > .5f;
    /// <summary>The torch ran dry and is filling back to full, the hammer working meanwhile.</summary>
    public bool TorchDry => HasTorch && torchDry;
    /// <summary>The tool in hand changed (0 none, 1 hammer, 2 torch): the others see it (PlayerNetworkCombat).</summary>
    public event Action<byte> ToolHeld;

    private void Awake() => movement = GetComponent<PlayerMovement>();

    private void OnDisable()
    {
        if (Local == this) Local = null;
        planning = false;
        HidePreview();
        AimedAt = null;
        Line();
    }

    private void OnDestroy()
    {
        if (ghost != null) Destroy(ghost.gameObject);
        if (doomed != null) Destroy(doomed.gameObject);
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        if (!PlayerClass.Is(PlayerClass.Support))
        {
            if (Local == this) Local = null;
            Offered = null;
            AimedAt = Mendable = null;
            Mending = false;
            Show(RepairLooks.Tool.None);
            Line();
            planning = false;
            HidePreview();
            return;
        }
        Local = this;
        if (supply < 0f) supply = Mathf.Clamp(Number("supplyStart", 100f), 0f, SupplyMax);
        supply = Mathf.Min(SupplyMax, supply + Mathf.Max(0f, Number("supplyRegen", 1.5f)) * Time.deltaTime);
        Offered = movement.IsDead || movement.IsDowned || !HandsFree() ? null : Aimed();
        if (interact == null && InputSystem.actions != null) interact = InputSystem.actions.FindAction("Player/Interact", false);
        // While the class key is held F chooses what to put up (Plan), so it neither resupplies nor mends then.
        if (!planning && Offered != null && interact != null && interact.WasPressedThisFrame() && movement.InputAvailable) Give(Offered);
        // No friend there: a barricade under the crosshair, worn or whole. F held takes the tool out on either, so the
        // Support finds what it mends with before anything is worn (the user, 2026-11-12: "서포트는 용접 토치나 망치 어디
        // 있어? 어떻게 쓰는지 모르겠네"), and mends while it is worn.
        AimedAt = !planning && Offered == null && !movement.IsDead && !movement.IsDowned && HandsFree() ? AimedBarricade() : null;
        Mendable = AimedAt != null && AimedAt.Worn ? AimedAt : null;
        bool held = AimedAt != null && interact != null && interact.IsPressed() && movement.InputAvailable && !movement.IsHeld;
        Mending = held && Mendable != null;
        bool torching = held && Torching;
        if (Mending) Mend(Mendable);
        if (!Mending || Time.time >= nextMendSend) FlushMend();
        // The torch's fuel fills back while it rests; the tool in hand is the one at work.
        if (torchFuel < 0f) torchFuel = TorchFuelMax;
        if (!torching) torchFuel = Mathf.Min(TorchFuelMax, TorchFuel + Mathf.Max(0f, Number("torchRegen", 8f)) * Time.deltaTime);
        if (torchDry && TorchFuel >= TorchFuelMax - .01f) torchDry = false;
        Show(!held ? RepairLooks.Tool.None : torching ? RepairLooks.Tool.Torch : RepairLooks.Tool.Hammer);
        Line();
        Plan();
        Preview();
    }

    /// <summary>
    /// A class was picked (PlayerClass.Picked, by ClassKit): a fresh start, as a chapter's (the user, 2026-11-12: "서포트로
    /// 바리케이드 설치하고 다른 거 바꿨다가 다시 서포트로 돌아오면 다시 바리케이드를 설치할 수 있게"). The bag as it starts,
    /// no preview, and every barricade of the last pick down, the others told; one asked of the host and not yet answered
    /// comes down as soon as it goes up.
    /// </summary>
    internal void Fresh()
    {
        planning = false;
        if (pendingId != 0) dropped = pendingId;
        pendingId = 0;
        refusedAt = -99f;
        AimedAt = Mendable = null;
        Mending = false;
        Line();
        mendOwed = 0f;
        torchFuel = -1f;
        torchDry = false;
        Show(RepairLooks.Tool.None);
        supply = -1f;
        lastGive = lastBuild = -99f;
        HidePreview();
        if (movement == null) return;
        foreach (ushort id in Barricade.StandingOf(movement))
        {
            Barricade.Collapse(id);
            TookDown?.Invoke(id);
        }
    }

    // Whatever else Interact would do here goes first (an execution, a pickup, getting a downed friend up, a ladder, a
    // mission's button, a door, a shop).
}
