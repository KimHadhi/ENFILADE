using UnityEngine;
using UnityEngine.UI;

public enum HitMarkerKind { Body, Head, BodyKill, HeadKill, Blocked }

/// <summary>
/// Crosshair hit confirmation, in the look the player picks (Settings → HUD → 히트 마커, <see cref="HitMarkerStyle"/>).
/// Clean, the default (the user's pick of PAYDAY 3's marker, 2026-11-12): four hair lines at 45° that start a little big,
/// snap to rest, hold a moment and fade sliding a few pixels outward, in .2 s; white, a pale gold headshot that pops twice,
/// a red kill, a quiet grey for no effect, numbers in HitMarkers.csv (<see cref="CleanLook"/>, <see cref="LayoutClean"/>).
/// Bold, the earlier look, described below, numbers in GunFeel. Both get the headshot's burst, the posture break's burst
/// and the pierced tails, and under the aim point the kill chain: every kill of a run adds its picture (a skull, a
/// knife, a burst ... by how it happened, <see cref="KillIcons"/>) to one row (<see cref="ChainLook"/>).
/// Over either look lies Black Ops 6's hit marker (the Garry's Mod addon Black Ops VI Hitmarker) as seven switches in
/// Settings → HUD, all on by default (the user, 2026-11-22: "히트마커는 다 설정으로 넣어서 적용하고"; <see cref="Bo6Look"/>,
/// HitMarkers.csv BO6.*): the lines fade by the same share every moment from each hit (about .9 s) in place of the look's
/// hold and fade, start 1.8 times their size and shrink in, sit .3 s where they start (on the aim point aimed, at the
/// gun's spread from the hip) and then move out, turn a little at random (a kill more), split in two on a headshot, and
/// shake a little, smoothly. Its sounds are HitConfirm's.
/// A headshot's burst keeps the middle clear (<see cref="HeadLook"/>, HitMarkers.csv Head.*): a hollow eight-point star,
/// a thin gold ring snapping out through the lines with a fainter echo, eight sparks from outside the lines, and a lens
/// streak split down the middle and fired out both ways with a short split rise and two diamonds on a slant, all gone in
/// .16 s. It replaced a solid star over a bloom disc, two rings, twelve sparks and a full lens streak that hid the whole
/// head of a 10 m zombie, and the next one behind it, for .15 s (the user: "헤드샷 이펙트가 좀 너무 과해서 시야를 가리는
/// 것 같은데", 2026-11-12; of six drafts they liked the ring, the split flare, the trimmed old burst and the double-pop
/// lines, built together here).
/// Bold: four diagonal ticks around the centre: body hit = white; headshot = gold, a little bolder and quicker; any kill
/// (body or head) = red with a shock ring; no effect (guard/parry/invulnerable) = thin grey. Body and head markers grow
/// with the share of health the hit took. A bullet that came through or off something on the way (DamageHit.Pierced)
/// gives each tick a fading tail outward in its colour, growing a little while the marker shows, as if the shot carried
/// on through (the user's pick of four mockups).
/// Back 4 Blood timing, in frames at GunFeel's marker frame rate: the marker holds wide (7), the ticks slide in to the
/// shrunk gap (9), show faded for one frame and vanish; every new hit restarts it wide, a little bigger at first and
/// popping back, so each hit of a burst reads on its own. A posture break adds its own burst on top: an orange ring and
/// four shards flying out (<see cref="ShowBreak"/>). All of it is drawn at the steady aim point projected through the
/// kicked camera, so it jumps with the view punch and settles back with it (runs after CameraViewPunch). Created on first
/// use during Play. The sounds that go with it: <see cref="HitConfirm"/>.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HitMarkers)]
public sealed class HitMarkerHUD : MonoBehaviour
{
    public const string TableName = "HitMarkers", CleanRow = "Clean", HeadRow = "Head", ChainRow = "Chain", Bo6Row = "BO6";

    /// <summary>
    /// The kill chain under the screen's middle: Cool Killchains (a Garry's Mod addon), the user's pick on 2026-11-22
    /// ("좋아"), its numbers read off the addon's code at 1080p (HitMarkers.csv rows Chain.*, 1920x1080 canvas units,
    /// seconds). It is one row: every kill of a run adds its picture (<see cref="KillIcons"/>, tinted by its row) on the
    /// right, and the row slides to stay centred. A run lasts as long as kills keep coming within its window, which grows
    /// with each kill already in it; when the window ends the whole row fades over its last moment and is gone. A new
    /// picture fades in from bigger over a share of the window. It took the place of the single picture that popped under
    /// the aim for each kill (one owner for kill pictures under the aim).
    /// </summary>
    [System.Serializable]
    public sealed class ChainLook
    {
        [Tooltip("A picture's side (px). 0 turns the chain off.")]
        public float size = 30f;
        [Tooltip("From one picture's middle to the next (px).")]
        public float spacing = 36f;
        [Tooltip("From the screen's middle down to the row's middle (px).")]
        public float below = 216f;
        [Tooltip("A new picture starts this much bigger (.75 = 1.75 times) and settles as it fades in.")]
        public float pop = .75f;
        [Tooltip("The share of the run's window a new picture takes to fade in and settle.")]
        public float popShare = 1f / 30f;
        [Tooltip("The pictures' strength, 0 to 1.")]
        public float alpha = .784f;
        [Tooltip("Seconds the row waits for the next kill when it holds one picture.")]
        public float windowSeconds = 4f;
        [Tooltip("Seconds added to the window for every picture already in the row.")]
        public float perKill = .33f;
        [Tooltip("The row fades over this many seconds divided by the window (2 / 4 s = the last .5 s).")]
        public float fade = 2f;
        [Tooltip("Most pictures in the row; 0 = as many as fit in half the screen's width.")]
        public float most;
        [Tooltip("How fast the row slides to stay centred as pictures join (per second).")]
        public float slide = 5f;
    }

    /// <summary>
    /// The clean marker's numbers: HitMarkers.csv, rows Clean.* (1920x1080 canvas units). Measured off the user's PAYDAY 3
    /// screenshot (2026-11-12, a bullseye in the shooting range): four strokes at exactly 45°, the inner end about 13 px
    /// and the outer about 34 px from the aim point at 1080p, about 2 px thick, a pale gold. The motion is not in a still,
    /// so it follows the user's words for it ("간결하고 심플하며 애니메이션도 깔끔해. 시야를 너무 가리지도 않으면서 타격감도
    /// 있고") and their rule that a hit bangs and is over (.2 s).
    /// </summary>
    [System.Serializable]
    public sealed class CleanLook
    {
        [Tooltip("Distance from the aim point to each line's inner end at rest (px).")]
        public float gap = 13f;
        [Tooltip("Line length at rest (px), before the damage sizing and each kind's multiplier.")]
        public float length = 21f;
        [Tooltip("How thick each line looks (px).")]
        public float thickness = 2f;
        [Tooltip("A soft dark edge under each line, so a white line still reads on a bright wall or sky: its opacity at full " +
            "strength (0 turns it off). It fades faster than the line, so a fading line doesn't turn grey.")]
        public float edgeAlpha = .35f;
        [Tooltip("How far the dark edge reaches past the line on each side and at the ends (px).")]
        public float edgeWidth = 1f;
        [Tooltip("A fresh marker starts this much bigger (.3 = 30%) and snaps back over Pop Seconds, so every hit of a burst " +
            "lands on its own.")]
        public float pop = .3f;
        [Tooltip("Seconds the pop takes to settle.")]
        public float popSeconds = .06f;
        [Tooltip("Seconds the marker shows at full strength, the pop included, before it fades.")]
        public float holdSeconds = .07f;
        [Tooltip("Seconds it then takes to fade out.")]
        public float fadeSeconds = .13f;
        [Tooltip("How far the lines slide outward while they fade (px).")]
        public float drift = 3f;
        [Tooltip("Seconds a coloured marker (headshot, kill) takes to turn from white to its colour: a white-hot first frame " +
            "or two. 0 turns it off.")]
        public float flashSeconds = .03f;
        [Tooltip("Body and head lines grow with the share of the target's health the hit took (GunFeel's Marker Full " +
            "Damage): a graze at Small Scale, that share or more at Large Scale. Kills and no-effect hits keep their size.")]
        public float smallScale = .9f, largeScale = 1.15f;
        public Color bodyColor = Color.white;
        [Tooltip("The pale gold PAYDAY 3 draws on a bullseye, softer than the Bold marker's gold.")]
        public Color headColor = new Color(.96f, .86f, .43f);
        public Color killColor = new Color(1f, .23f, .19f);
        public Color blockedColor = new Color(.62f, .65f, .69f);
        [Tooltip("A headshot's lines are this much longer, and start this much bigger in place of Pop.")]
        public float headLength = 1.25f, headPop = .45f;
        [Tooltip("A headshot's lines pop a second time, Head Pop Again At seconds after the first, by this much: two beats " +
            "(\"빵-똮\") where a body hit has one, so the headshot reads without anything over the target. 0 turns it off. " +
            "Head kills get it too.")]
        public float headPopAgain = .22f, headPopAgainAt = .045f;
        [Tooltip("A headshot's lines start this many times thicker and thin back to Thickness over Pop Seconds once the " +
            "white flash is over. 1 turns it off.")]
        public float headThickness = 1.3f;
        [Tooltip("A kill's lines: longer, thicker, a bigger pop, and a little more time to hold and fade so the kill reads.")]
        public float killLength = 1.3f, killThickness = 1.25f, killPop = .45f, killHoldSeconds = .1f, killFadeSeconds = .18f;
        [Tooltip("A hit that did nothing (guard, parry, invulnerable): shorter, thinner and quieter, with no pop.")]
        public float blockedLength = .75f, blockedThickness = .8f, blockedAlpha = .8f;
    }

    /// <summary>
    /// The headshot's burst, in either look: HitMarkers.csv rows Head.* (1920x1080 canvas units, seconds). From the second
    /// drafts the user liked (2026-11-12: B the ring, C the split flare, F the old burst trimmed; E's double pop is in
    /// CleanLook). Nothing is drawn within about 12 px of the aim point, so the head that was hit, and the next one lined
    /// up behind it, stay in sight: on the comparison page the old burst covered all of a 10 m zombie's head (a 10 px
    /// radius) for .15 s, this one about 5% of it at most. Every part starts white-hot and turns gold within a few frames.
    /// A size of 0 turns that part off.
    /// </summary>
    [System.Serializable]
    public sealed class HeadLook
    {
        public Color color = new Color(1f, .82f, .2f);
        [Tooltip("The star's glow once it has cooled from white: a deeper ember than the gold, for a warm edge.")]
        public Color glowColor = new Color(1f, .7f, .16f);
        [Tooltip("Ray length of the hollow star (px). 0 turns the star off.")]
        public float starSize = 22f;
        [Tooltip("The star blows open to this size in its first frames, settles back, then shrinks a little as it fades.")]
        public float starOpen = 1.3f;
        public float starSeconds = .14f;
        [Tooltip("Degrees the star turns while it shows, so its rays sweep off the lines.")]
        public float starTurn = 30f;
        [Tooltip("Share of the star's half-size left empty in its middle. The smaller star turned 45° over it (Cross Scale " +
            "of its size) is cut out to Cross Hole of its own half-size.")]
        public float starHole = .42f, crossScale = .62f, crossHole = .62f;
        [Tooltip("The star's glow, added onto the picture: its size as a multiple of the star's, its strength, and the " +
            "share of it left empty in the middle.")]
        public float glowScale = 1.5f, glowStrength = .55f, glowHole = .3f;
        [Tooltip("The thin ring: its radius opens from Ring From to Ring To (px) over most of Ring Seconds while it thins " +
            "from Ring Width to Ring End Width (px); it holds full strength Ring Hold seconds, then fades. 0 for Ring To " +
            "turns it off.")]
        public float ringFrom = 16f, ringTo = 46f, ringSeconds = .14f, ringHold = .04f, ringWidth = 3.2f, ringEndWidth = 1f;
        [Tooltip("A soft dark ring under it, as under the lines, so it reads on a bright sky: its opacity at full strength.")]
        public float ringEdgeAlpha = .3f;
        [Tooltip("A fainter ring Echo Delay seconds behind, opening from Ring From to Echo To (px). 0 for Echo To turns it off.")]
        public float echoDelay = .03f, echoTo = 60f, echoSeconds = .11f, echoWidth = 1.6f, echoEndWidth = .6f, echoAlpha = .55f;
        [Tooltip("Sparks flying out between the lines' directions: how many (read when the HUD is made), from where to " +
            "where (px), long and thick at first, short and thin at the end. 0 turns them off.")]
        public int sparks = 8;
        public float sparkFrom = 16f, sparkTo = 50f, sparkLength = 16f, sparkEndLength = 3f, sparkWidth = 2f, sparkEndWidth = 1f, sparkSeconds = .12f;
        [Tooltip("The split lens streak: each half's outer end shoots from Flare From to Flare To (px) within Flare Open " +
            "seconds while its inner end drifts out to Flare Drift, so the middle opens up as it goes. Added onto the " +
            "picture. 0 for Flare To turns it off, the diamonds with it.")]
        public float flareFrom = 24f, flareTo = 210f, flareDrift = 70f, flareOpen = .05f, flareSeconds = .16f, flareWidth = 4f, flareEndWidth = 1.5f;
        [Tooltip("A softer streak under each half, this many times as thick, at this opacity.")]
        public float flareGlowWidth = 3f, flareGlowAlpha = .35f;
        [Tooltip("The short split rise up and down: outer ends from Rise From to Rise To (px) within Rise Open seconds, inner " +
            "ends drifting out to Rise Drift. 0 for Rise To turns it off.")]
        public float riseFrom = 18f, riseTo = 66f, riseDrift = 40f, riseOpen = .04f, riseSeconds = .1f, riseWidth = 3f, riseEndWidth = 1f, riseAlpha = .8f;
        [Tooltip("Brightness of the two diamonds the old lens flare left on its slant (1 as drafted, 0 turns them off).")]
        public float diamonds = 1f;
    }

    /// <summary>
    /// Black Ops 6's hit marker (the Garry's Mod addon Black Ops VI Hitmarker, lua/autorun/blackops6_hitmarker.lua) as
    /// seven switches in Settings → HUD, each on by default and each laid over either look: the user, 2026-11-22, on the
    /// seven things it does that ours did not, "히트마커는 다 설정으로 넣어서 적용하고". Its numbers are read off the Lua at
    /// 1080p (its ScreenScale(0.256) is .768 at 1920 wide) into HitMarkers.csv rows BO6.*. Its Lerp(FrameTime()*k, x, 0)
    /// steps, which take k·dt of x away every frame, are written as e^-k a second, so they run alike at any frame rate.
    /// Its arm pictures are not used: our lines stay, and what the addon does with its arms is done to them. The sixth
    /// switch, its sounds, is HitConfirm's.
    /// </summary>
    [System.Serializable]
    public sealed class Bo6Look
    {
        [Tooltip("Fade: the marker's strength falls to e^-rate in a second and is full again at every hit (the addon: life = " +
            "Lerp(FrameTime()*10/2, life, 0)).")]
        public float fadeRate = 5f;
        [Tooltip("Below this strength the marker is put away (.01 = .92 s at rate 5); the addon's never quite ends.")]
        public float fadeEnd = .01f;
        [Tooltip("Pop: the lines (length, gap and width) start this much bigger (.8 = 1.8 times, the addon's 90 then 50 px) " +
            "and shrink back at Pop Rate (e^-rate a second), in place of the look's own first pop.")]
        public float pop = .8f, popRate = 5f;
        [Tooltip("Spread: seconds the lines stay where they start (the addon's 0.3 s timer), then how far they move out (px; " +
            "the addon's 15.36 px on each axis, 21.7 px on the slant) and how fast (e^-rate a second). Takes the place of the " +
            "look's own drift out (Clean) or slide in (Bold).")]
        public float holdSeconds = .3f, spreadOut = 21.7f, spreadRate = 10f;
        [Tooltip("From the hip the lines start out from the aim point by the gun's spread (as far as the crosshair's lines " +
            "open) times this, nothing down the sights, and follow it at Hip Rate (e^-rate a second). 0 keeps them on the aim " +
            "point. The addon stands them a fixed 21.7 px out from the hip.")]
        public float hipScale = 1f, hipRate = 10f;
        [Tooltip("1: a kill's marker stands at the hip spread too. 0, the addon's: a kill sits on the aim point always (its " +
            "kill message sends hipfire false).")]
        public float killHip;
        [Tooltip("Turn: each hit turns the whole marker by up to this many degrees either way, a kill by up to Turn Kill, in " +
            "whole-degree steps as LuaJIT's math.random(-2.5, 2.5) and math.random(-5, 5) give them.")]
        public float turnHit = 2.5f, turnKill = 5f;
        [Tooltip("Twin: a headshot's line splits in two, each half turned this many degrees off the line, their outer ends " +
            "this share of the line's length apart, so they open toward the aim point (measured off the addon's head " +
            "pictures: two strokes 10° apart, 12 px apart outside and 25 px inside on a 76 px stroke).")]
        public float twinTurn = 5f, twinApart = .16f;
        [Tooltip("Shake: how far the marker shakes (px; ScrW × .0025), the share it leans up and left by (the addon's " +
            "random − .8), the seconds it runs down in, the speed of its noise (Hz) and how fast the marker follows that " +
            "(e^-rate a second). The addon jumps to a new random offset every frame; ours is a smooth noise of at most " +
            "1.6 Hz, the user's rule for anything on screen that moves by itself (never a flicker).")]
        public float shake = 4.8f, shakeBias = .8f, shakeSeconds = .5f, shakeHz = 1.6f, shakeFollow = 10.5f;
    }

    // Between a tick's outer end and its pierced tail (canvas units).
    private const float TailGap = 1.5f;
    // The most sparks a headshot can have, whatever Head.sparks says.
    private const int MaxSparks = 24;
    private static HitMarkerHUD instance;
    // The two diamonds of the split flare: where along the slant (px; negative is the other side of the aim point), size
    // (px) and brightness. Uneven, as a lens throws them.
    private static readonly Vector3[] Diamonds = { new Vector3(-112f, 8f, .45f), new Vector3(126f, 11f, .35f) };
    // The slant they lie along: right and a little down.
    private static readonly Vector2 GhostSlant = new Vector2(.924f, -.383f);
    private readonly Image[] ticks = new Image[4], tails = new Image[4], tailEnds = new Image[4], shards = new Image[4];
    // The clean marker's lines, the soft dark edges drawn under them, and their pierced tails.
    private readonly RawImage[] cleanLines = new RawImage[4], cleanEdges = new RawImage[4], cleanTails = new RawImage[4],
        cleanTailEnds = new RawImage[4];
    // The headshot's split streak (a glow and a core per half), its rise and its diamonds.
    private readonly RawImage[] flareHalves = new RawImage[2], flareGlows = new RawImage[2], riseHalves = new RawImage[2];
    private readonly Graphic[] flareDiamonds = new Graphic[2];
    // A headshot's second line per arm under the Black Ops 6 twin switch: the Bold look's, and the clean look's with its
    // dark edge.
    private readonly Image[] tickTwins = new Image[4];
    private readonly RawImage[] cleanTwins = new RawImage[4], cleanTwinEdges = new RawImage[4];
    private readonly CleanLook look = new CleanLook();
    private readonly HeadLook headLook = new HeadLook();
    private readonly ChainLook chainLook = new ChainLook();
    private readonly Bo6Look bo6 = new Bo6Look();
    // The Black Ops 6 switches as they stood at the showing marker's hit (a change in the settings shows from the next hit,
    // as the look's does), whether its lines stand at the hip spread, the turn it drew, and the moving parts: the hip
    // place now (px), the shake left (1 down to 0), the shake noise's clock and where the marker has followed it to.
    private bool bo6Fade, bo6Pop, bo6Spread, bo6Turn, bo6Twin, bo6Shake, bo6Hip;
    private float turn, hipNow, shakeLeft, shakeClock;
    private Vector2 shakeAt;
    // The local player and the gun whose spread the hip place follows.
    private PlayerMovement spreadOwner;
    private WeaponController spreadGun;
    // The look's own span for this hit: the Bold kill ring keeps its pace while the Black Ops 6 fade keeps the lines longer.
    private float baseLifetime = .2f;
    private Image[] sparks;
    // The kill chain: one link per kill of the run, oldest first (its picture, its row's colour, when it joined), the
    // pictures no longer shown kept for the next run, when the run ends, its window, and the count the row is centred on.
    private sealed class ChainLink { public RawImage Image; public Color Colour; public float Born; }
    private readonly System.Collections.Generic.List<ChainLink> chain = new System.Collections.Generic.List<ChainLink>();
    private readonly System.Collections.Generic.Stack<RawImage> spareLinks = new System.Collections.Generic.Stack<RawImage>();
    private RectTransform chainRoot;
    private float chainEnd, chainWindow, chainCentred;
    private RectTransform root, cleanRoot, headRoot, starRoot, breakRoot;
    // The look the showing marker started in: a change in the settings takes effect from the next hit.
    private HitMarkerStyle style;
    private RawImage ring, star, starCross, starGlow, breakRing;
    private SoftRingGraphic headRing, headRingEdge, echoRing;
    private HitMarkerKind kind;
    private bool pierced;
    private float age = 99f, lifetime = .2f, scale = 1f, headAge = 99f, breakAge = 99f, pace = 1f;

    private bool IsShowing => age < lifetime;

    /// <summary>share: the part of the target's health the hit took, which sizes body and head markers. pierced: the bullet
    /// went through or off something on the way.</summary>
    public static void Show(HitMarkerKind kind, float share = 1f, bool pierced = false)
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.hitMarkers) return;
        Prepare();
        instance.Trigger(kind, share, pierced);
    }

    /// <summary>The player's hit broke the target's posture.</summary>
    public static void ShowBreak()
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.hitMarkers) return;
        Prepare();
        instance.breakAge = 0f;
        instance.breakRoot.gameObject.SetActive(true);
        instance.LayoutBreak(GunFeelSettings.Instance);
    }

    /// <summary>The local player's kill: its picture (a KillIcons row, chosen by how it happened) joins the kill chain.</summary>
    public static void ShowKill(KillIcons.Row row)
    {
        if (!Application.isPlaying || row == null || !GunFeelSettings.Instance.hitMarkers) return;
        Prepare();
        instance.AddLink(row);
    }

    public static void Prepare()
    {
        if (!Application.isPlaying || instance != null) return;
        var host = new GameObject("Hit Markers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        instance = host.AddComponent<HitMarkerHUD>();
    }

    private static bool IsKill(HitMarkerKind kind) => kind == HitMarkerKind.BodyKill || kind == HitMarkerKind.HeadKill;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupOverlay(gameObject, 26);
        GameTables.BindNested(TableName, CleanRow, this, look);
        GameTables.BindNested(TableName, HeadRow, this, headLook);
        GameTables.BindNested(TableName, ChainRow, this, chainLook);
        GameTables.BindNested(TableName, Bo6Row, this, bo6);
        root = FeelUI.Rect("Marker", transform);
        ring = FeelUI.Rect("Ring", root).gameObject.AddComponent<RawImage>();
        ring.texture = FeelAssets.Ring; ring.raycastTarget = false;
        for (int i = 0; i < 4; i++) ticks[i] = Tick("Tick " + i, root);
        for (int i = 0; i < 4; i++) { tickTwins[i] = Tick("Tick Twin " + i, root); tickTwins[i].enabled = false; }
        for (int i = 0; i < 4; i++) { tails[i] = Tail("Pierced Tail " + i, root); tailEnds[i] = Tail("Pierced Tail End " + i, root); }
        root.gameObject.SetActive(false);

        // The clean marker, in the same place under the headshot burst: the dark edges first so every line covers them.
        cleanRoot = FeelUI.Rect("Clean Marker", transform);
        for (int i = 0; i < 4; i++) cleanEdges[i] = HairLine("Edge " + i, cleanRoot);
        for (int i = 0; i < 4; i++) { cleanTwinEdges[i] = HairLine("Twin Edge " + i, cleanRoot); cleanTwinEdges[i].enabled = false; }
        for (int i = 0; i < 4; i++) cleanLines[i] = HairLine("Line " + i, cleanRoot);
        for (int i = 0; i < 4; i++) { cleanTwins[i] = HairLine("Twin " + i, cleanRoot); cleanTwins[i].enabled = false; }
        for (int i = 0; i < 4; i++)
        {
            cleanTails[i] = HairLine("Pierced Tail " + i, cleanRoot);
            cleanTailEnds[i] = HairLine("Pierced Tail End " + i, cleanRoot);
        }
        cleanRoot.gameObject.SetActive(false);

        // The headshot's burst over both looks' lines, bottom to top: the split streak with its rise and diamonds, added
        // onto the picture so they light it without covering it; the dark edge under the ring, the ring and its echo; the
        // sparks; the hollow star in its own turning frame (glow, star, the smaller star turned 45° for eight points).
        var additive = FeelAssets.UiAdditive;
        headRoot = FeelUI.Rect("Head Burst", transform);
        for (int i = 0; i < 2; i++) flareGlows[i] = Streak("Flare Glow " + i, headRoot, additive);
        for (int i = 0; i < 2; i++) flareHalves[i] = Streak("Flare " + i, headRoot, additive);
        for (int i = 0; i < 2; i++) riseHalves[i] = Streak("Rise " + i, headRoot, additive);
        for (int i = 0; i < 2; i++)
        {
            flareDiamonds[i] = HudShapes.Make<DiamondGraphic>("Diamond " + i, headRoot, Color.white);
            if (additive != null) flareDiamonds[i].material = additive;
        }
        headRingEdge = SoftRing("Ring Edge", headRoot);
        headRing = SoftRing("Ring", headRoot);
        echoRing = SoftRing("Echo Ring", headRoot);
        sparks = new Image[Mathf.Clamp(headLook.sparks, 0, MaxSparks)];
        for (int i = 0; i < sparks.Length; i++) sparks[i] = Tail("Spark " + i, headRoot);
        starRoot = FeelUI.Rect("Star", headRoot);
        starGlow = GlintLayer("Glow", starRoot, FeelAssets.HollowGlintGlow(headLook.glowHole), 0f, headLook.glowScale, additive);
        star = GlintLayer("Star", starRoot, FeelAssets.HollowGlint(headLook.starHole), 0f, 1f, null);
        starCross = GlintLayer("Star Cross", starRoot, FeelAssets.HollowGlint(headLook.crossHole), 45f, headLook.crossScale, null);
        headRoot.gameObject.SetActive(false);

        breakRoot = FeelUI.Rect("Break", transform);
        breakRing = FeelUI.Rect("Ring", breakRoot).gameObject.AddComponent<RawImage>();
        breakRing.texture = FeelAssets.Ring; breakRing.raycastTarget = false;
        for (int i = 0; i < 4; i++) shards[i] = Tick("Shard " + i, breakRoot);
        breakRoot.gameObject.SetActive(false);

        // The kill chain over everything: the pictures are white with their own shadow drawn in, tinted by their rows' colours.
        chainRoot = FeelUI.Rect("Kill Chain", transform);
        // The table and its pictures now, so the first kill loads nothing (this runs from CombatWarmup's Prepare).
        KillIcons.Prewarm();
    }

    private static Image Tick(string name, Transform parent)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        // A soft dark edge keeps white markers readable on bright walls and skies.
        var outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .55f); outline.effectDistance = new Vector2(1f, -1f);
        return image;
    }

    // A pierced tail stays soft: no dark edge, so it reads as an afterimage of the tick rather than a second marker.
    private static Image Tail(string name, Transform parent)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    // One of the clean marker's lines: a soft-edged texture rather than a plain quad, which steps at 45° and two pixels.
    private static RawImage HairLine(string name, Transform parent)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.texture = FeelAssets.HairLine; image.raycastTarget = false;
        return image;
    }

    // One layer of the star, stretched over its root and turned and scaled within it. material null: alpha blended.
    private static RawImage GlintLayer(string name, Transform parent, Texture texture, float turn, float scale, Material material)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.Euler(0f, 0f, turn);
        rect.localScale = Vector3.one * scale;
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture; image.raycastTarget = false;
        if (material != null) image.material = material;
        return image;
    }

    // Half of the split lens streak (FeelAssets.StreakTaper, brightest at its inner end). material null: alpha blended.
    private static RawImage Streak(string name, Transform parent, Material material)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.texture = FeelAssets.StreakTaper; image.raycastTarget = false;
        if (material != null) image.material = material;
        image.enabled = false;
        return image;
    }

    private static SoftRingGraphic SoftRing(string name, Transform parent)
    {
        var line = FeelUI.Rect(name, parent).gameObject.AddComponent<SoftRingGraphic>();
        line.raycastTarget = false;
        line.enabled = false;
        return line;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Trigger(HitMarkerKind next, float share, bool throughSomething)
    {
        // A fresh kill marker is not overwritten by the next body hit of the same burst.
        if (IsShowing && IsKill(kind) && !IsKill(next) && age < .18f) return;
        bool wasShowing = IsShowing;
        kind = next; age = 0f;
        pierced = throughSomething;
        var settings = GunFeelSettings.Instance;
        var game = GameSettings.Current;
        style = game.hitMarkerStyle;
        TakeBo6(game, next, wasShowing);
        bool clean = style == HitMarkerStyle.Clean, sized = !IsKill(next) && next != HitMarkerKind.Blocked;
        float hurt = Mathf.Clamp01(share / settings.markerFullDamage);
        // Only one look shows at a time; the other may still be up from before the setting changed.
        (clean ? root : cleanRoot).gameObject.SetActive(false);
        if (clean)
        {
            scale = sized ? Mathf.Lerp(look.smallScale, look.largeScale, hurt) : 1f;
            pace = 1f;
            lifetime = Mathf.Max(.02f, CleanHold() + CleanFade());
        }
        else
        {
            scale = sized ? Mathf.Lerp(settings.markerSmallScale, settings.markerLargeScale, hurt) : 1f;
            // A headshot's ticks run through their frames faster, so the bang is short; kills keep their own time.
            pace = next == HitMarkerKind.Head ? Mathf.Max(1f, settings.headMarkerPace) : 1f;
            int frames = Mathf.Max(1, settings.markerHoldFrames + settings.markerShrinkFrames + settings.markerFadeFrames);
            lifetime = frames / settings.markerFrameRate / pace;
        }
        // The Black Ops 6 fade lasts until it has all but gone (.92 s against the look's .2); the look's own span still
        // paces what is the look's (the Bold kill ring).
        baseLifetime = lifetime;
        if (bo6Fade) lifetime = Mathf.Max(.02f, Bo6Lifetime());
        if (clean)
        {
            cleanRoot.gameObject.SetActive(true);
            LayoutClean(settings);
        }
        else
        {
            root.gameObject.SetActive(true);
            Layout(settings);
        }
        bool head = next == HitMarkerKind.Head || next == HitMarkerKind.HeadKill;
        if (head && HeadSeconds() > 0f)
        {
            headAge = 0f;
            // The holes come from the table each time, so a reloaded HitMarkers.csv shows on the next headshot.
            starGlow.texture = FeelAssets.HollowGlintGlow(headLook.glowHole);
            star.texture = FeelAssets.HollowGlint(headLook.starHole);
            starCross.texture = FeelAssets.HollowGlint(headLook.crossHole);
            headRoot.gameObject.SetActive(true);
            LayoutHead();
        }
    }

    private void LateUpdate()
    {
        var settings = GunFeelSettings.Instance;
        bool clean = style == HitMarkerStyle.Clean;
        var marker = clean ? cleanRoot : root;
        if (marker.gameObject.activeSelf)
        {
            age += Time.unscaledDeltaTime;
            if (age >= lifetime) marker.gameObject.SetActive(false);
            else
            {
                StepBo6(Time.unscaledDeltaTime);
                if (clean) LayoutClean(settings);
                else Layout(settings);
            }
        }
        if (headRoot.gameObject.activeSelf)
        {
            headAge += Time.unscaledDeltaTime;
            if (headAge >= HeadSeconds()) headRoot.gameObject.SetActive(false);
            else LayoutHead();
        }
        if (breakRoot.gameObject.activeSelf)
        {
            breakAge += Time.unscaledDeltaTime;
            if (breakAge >= settings.breakSeconds) breakRoot.gameObject.SetActive(false);
            else LayoutBreak(settings);
        }
        if (chain.Count > 0) LayoutChain();
    }

    // A kill joins the run (or starts a new one once the last has ended). The window is set from the pictures already in
    // the row, so a long run waits longer for its next kill; a row as wide as allowed lets its oldest picture go.
    private void AddLink(KillIcons.Row row)
    {
        var l = chainLook;
        if (l.size <= 0f) return;
        var picture = KillIcons.Picture(row);
        if (picture == null) return;
        float now = Time.unscaledTime;
        if (now >= chainEnd) ClearChain();
        chainWindow = Mathf.Max(.1f, l.windowSeconds + chain.Count * l.perKill);
        chainEnd = now + chainWindow;
        int most = l.most > 0f ? Mathf.RoundToInt(l.most) : Mathf.CeilToInt(960f / Mathf.Max(1f, l.spacing));
        while (chain.Count >= Mathf.Max(1, most))
        {
            Recycle(chain[0]);
            chain.RemoveAt(0);
            chainCentred = Mathf.Max(0f, chainCentred - 1f);
        }
        var image = spareLinks.Count > 0 ? spareLinks.Pop() : NewLinkImage();
        image.texture = picture;
        image.gameObject.SetActive(true);
        chain.Add(new ChainLink { Image = image, Colour = row.Colour, Born = now });
        LayoutChain();
    }

    private RawImage NewLinkImage()
    {
        var image = FeelUI.Rect("Kill Chain Picture", chainRoot).gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }

    private void Recycle(ChainLink link)
    {
        link.Image.gameObject.SetActive(false);
        spareLinks.Push(link.Image);
    }

    private void ClearChain()
    {
        foreach (var link in chain) Recycle(link);
        chain.Clear();
        chainCentred = 0f;
    }

    // The addon's layout: the row centred on the count it is sliding to, a new picture fading in (quartic) from bigger and
    // a little to the left, the whole row fading over the window's last moment. It sits still under the screen's middle
    // (the addon's place), not on the kicked aim.
    private void LayoutChain()
    {
        var l = chainLook;
        float now = Time.unscaledTime;
        if (now >= chainEnd) { ClearChain(); return; }
        chainCentred = Mathf.Lerp(chainCentred, chain.Count, Mathf.Clamp01(Time.unscaledDeltaTime * l.slide));
        float row = Mathf.Clamp01((chainEnd - now) * chainWindow / Mathf.Max(.01f, l.fade));
        float scale = l.size / 30f;
        for (int k = 0; k < chain.Count; k++)
        {
            var link = chain[k];
            float settle = Mathf.Clamp01((now - link.Born) / Mathf.Max(.001f, chainWindow * l.popShare));
            float fadeIn = settle * settle * settle * settle;
            float grow = 1f + (1f - fadeIn) * l.pop;
            var rect = link.Image.rectTransform;
            rect.anchoredPosition = new Vector2(l.spacing * (k + 1 - (chainCentred + 1f) * .5f) - 3f * scale * (grow - 1f), -l.below);
            rect.sizeDelta = Vector2.one * (l.size * grow);
            link.Image.color = KitStyle.With(link.Colour, link.Colour.a * l.alpha * row * fadeIn);
        }
    }

    /// <summary>Screen position of the steady aim as seen through the punched camera; centre when there is no punch.</summary>
    private Vector2 AimPoint()
    {
        var view = Camera.main;
        var punch = view != null ? view.GetComponent<CameraViewPunch>() : null;
        if (punch != null && punch.isActiveAndEnabled)
        {
            Vector3 screen = view.WorldToScreenPoint(view.transform.position + punch.SteadyForward * 20f);
            if (screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local))
                return local;
        }
        return Vector2.zero;
    }

    private void Layout(GunFeelSettings settings)
    {
        bool kill = IsKill(kind), head = kind == HitMarkerKind.Head || kind == HitMarkerKind.HeadKill, blocked = kind == HitMarkerKind.Blocked;
        // The Black Ops 6 shake and turn move the whole marker about the aim point (both nothing while switched off).
        root.anchoredPosition = AimPoint() + shakeAt;
        root.localRotation = Quaternion.Euler(0f, 0f, turn);
        // Hold wide, slide the ticks in, one faded frame at the shrunk size, gone. A headshot runs it faster (pace).
        float frame = age * pace * settings.markerFrameRate;
        int hold = settings.markerHoldFrames, shrinkFrames = settings.markerShrinkFrames;
        float shrink = shrinkFrames > 0 ? Mathf.Clamp01((frame - hold) / shrinkFrames) : frame >= hold ? 1f : 0f;
        float alpha = frame >= hold + shrinkFrames ? settings.markerFadeAlpha : 1f;
        // Black Ops 6's fade in place of the faded last frame.
        if (bo6Fade) alpha = Bo6Alpha();
        // A little bigger at first, popping back, so every hit of a burst reads as its own; a headshot pops harder.
        // Black Ops 6's pop takes its place, and swells the width with it as the addon's whole arm picture grows.
        float popBy = kind == HitMarkerKind.Head ? settings.headPop : settings.markerPop;
        bool bigPop = bo6Pop && !blocked;
        float pop = bigPop ? Bo6Pop() : 1f + popBy * (1f - Smooth(Mathf.Clamp01(age / settings.markerPopSeconds)));
        // Body and head hits share one shape; a headshot is gold and a little bolder, and the damage sets the size.
        bool bold = head && !kill;
        float length = settings.markerLength * (kill ? 1.45f : blocked ? .8f : bold ? settings.headLength : 1f) * scale * pop;
        float thickness = settings.markerThickness * (kill ? 1.5f : blocked ? .75f : bold ? settings.headThickness : 1f) * (bigPop ? pop : 1f);
        // Black Ops 6's hold, then spread, in place of the slide in: from where the ticks would have slid to.
        float gap = bo6Spread ? settings.markerShrunkGap * pop + Bo6Spread()
            : Mathf.Lerp(settings.markerGap, settings.markerShrunkGap, Smooth(shrink)) * pop;
        Color color = kill ? settings.killColor : head ? settings.headHitColor : blocked ? settings.blockedColor : settings.bodyHitColor;
        if (!blocked) color = Color.Lerp(Color.white, color, Mathf.Clamp01(age / .05f)); // white-hot first frame
        color.a *= alpha;

        // Through or off something on the way: each tick trails a tail outward, near half brighter, growing as the marker shows.
        float tail = length * settings.piercedTailLength * Mathf.Lerp(.6f, 1f, Smooth(Mathf.Clamp01(age / lifetime)));
        float nearTail = tail * .55f, farTail = tail * .45f, tailStart = gap + length + TailGap;
        Color near = color, far = color;
        near.a *= settings.piercedTailAlpha;
        far.a *= settings.piercedTailAlpha * .42f;
        // Black Ops 6's headshot arm: each tick split in two from its outer end (head kills too).
        bool twin = bo6Twin && head;
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 direction = Dir(angle);
            tickTwins[i].enabled = twin;
            if (twin)
            {
                TwinLine(angle, gap, length, 1f, out Vector2 one, out float oneTurn);
                TwinLine(angle, gap, length, -1f, out Vector2 other, out float otherTurn);
                Place(ticks[i], one, new Vector2(length, thickness), oneTurn, color);
                Place(tickTwins[i], other, new Vector2(length, thickness), otherTurn, color);
            }
            else Place(ticks[i], direction * (gap + length * .5f), new Vector2(length, thickness), angle, color);
            tails[i].enabled = tailEnds[i].enabled = pierced;
            if (!pierced) continue;
            Place(tails[i], direction * (tailStart + nearTail * .5f), new Vector2(nearTail, thickness * .7f), angle, near);
            Place(tailEnds[i], direction * (tailStart + nearTail + farTail * .5f), new Vector2(farTail, thickness * .5f), angle, far);
        }

        // The kill ring is the look's own, on the look's own span however long the Black Ops 6 fade keeps the ticks.
        ring.enabled = kill && age < baseLifetime;
        if (ring.enabled)
        {
            float wave = Mathf.Clamp01(age / baseLifetime);
            float size = Mathf.Lerp(gap * 2f, (settings.markerGap + length) * 2.8f, 1f - Mathf.Pow(1f - wave, 2f));
            ring.rectTransform.sizeDelta = new Vector2(size, size);
            Color ringColor = settings.killColor;
            ringColor.a = .85f * (1f - wave);
            ring.color = ringColor;
        }
    }

    // The Black Ops 6 switches for this hit (read at each hit, as the look is) and what the hit itself sets off: a fresh
    // turn, the shake back to full, and whether the lines stand at the hip spread. A hit that did nothing (Blocked) keeps
    // the look's quiet: no pop, turn or shake, as it has no pop of its own.
    private void TakeBo6(GameSettings game, HitMarkerKind next, bool wasShowing)
    {
        bool kill = IsKill(next), blocked = next == HitMarkerKind.Blocked;
        bo6Fade = game.hitMarkerFade; bo6Pop = game.hitMarkerPop; bo6Spread = game.hitMarkerSpread;
        bo6Turn = game.hitMarkerTurn; bo6Twin = game.hitMarkerTwin; bo6Shake = game.hitMarkerShake;
        // A whole-degree step up from -most, as LuaJIT's math.random(m, n) is floor(r·(n − m + 1)) + m: for ±2.5 one of
        // -2.5, -1.5 … 2.5, for ±5 one of -5 … 5.
        float most = Mathf.Max(0f, kill ? bo6.turnKill : bo6.turnHit);
        turn = bo6Turn && !blocked && most > 0f ? Mathf.Min(most, Mathf.Floor(Random.value * (2f * most + 1f)) - most) : 0f;
        bo6Hip = !kill || bo6.killHip > 0f;
        // The hip place eases from where it was while a marker shows (the addon eases it every frame); a fresh marker
        // starts at it, and still.
        if (!wasShowing)
        {
            hipNow = HipTarget();
            shakeAt = Vector2.zero;
        }
        if (bo6Shake && !blocked) shakeLeft = 1f;
    }

    // Where the lines start out from the aim point (px on this canvas) under the Black Ops 6 spread switch: how far the
    // crosshair's lines open for the gun in hand (WeaponController.UpdateCrosshair: its spread, and a shotgun's pellet
    // circle), down to nothing as the sights come up, so the marker sits on the aim point aimed and around it from the
    // hip, as the addon's two places do (its fixed 21.7 px from the hip made the gun's own).
    private float HipTarget()
    {
        if (!bo6Spread || !bo6Hip || bo6.hipScale <= 0f) return 0f;
        var local = PlayerRoster.Local;
        if (local != spreadOwner)
        {
            spreadOwner = local;
            spreadGun = local != null ? local.GetComponentInChildren<WeaponController>() : null;
        }
        var gun = spreadGun != null && spreadGun.isActiveAndEnabled ? spreadGun : null;
        var data = gun != null ? gun.EquippedData : null;
        var view = Camera.main;
        if (data == null || view == null) return 0f;
        float degrees = gun.CurrentSpread + (data.Pellets > 1 ? data.PelletCone(gun.AimBlend, gun.ChokeShare) : 0f);
        float perTangent = ((RectTransform)transform).rect.height * .5f / Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
        return Mathf.Tan(Mathf.Clamp(degrees, 0f, 60f) * Mathf.Deg2Rad) * perTangent * (1f - Mathf.Clamp01(gun.AimBlend)) * bo6.hipScale;
    }

    // The Black Ops 6 parts that move on their own, each frame a marker shows: the hip place easing to the gun's spread
    // (the addon's scale_rate Lerp), and the shake. The addon jumps the marker to a new random offset every frame, a
    // flicker at the frame rate; here its strength runs down evenly as the addon's does, a smooth noise of at most
    // Shake Hz stands in for the random numbers, and the marker follows it softly (the user's rule since 2026-11-14:
    // nothing blinks or jitters, anything that moves by itself is smooth and slow).
    private void StepBo6(float dt)
    {
        hipNow = Mathf.Lerp(hipNow, HipTarget(), 1f - Mathf.Exp(-Mathf.Max(0f, bo6.hipRate) * dt));
        shakeClock += dt;
        shakeLeft = bo6.shakeSeconds > 0f ? Mathf.MoveTowards(shakeLeft, 0f, dt / bo6.shakeSeconds) : 0f;
        Vector2 target = Vector2.zero;
        if (bo6Shake && shakeLeft > 0f)
        {
            float t = shakeClock * Mathf.Max(0f, bo6.shakeHz);
            // Up and left, as the addon leans: its y grows down the screen, ours up.
            target = shakeLeft * bo6.shake * new Vector2(Mathf.PerlinNoise(t, 3.7f) - bo6.shakeBias, bo6.shakeBias - Mathf.PerlinNoise(11.3f, t));
        }
        shakeAt = Vector2.Lerp(shakeAt, target, 1f - Mathf.Exp(-Mathf.Max(0f, bo6.shakeFollow) * dt));
    }

    // Black Ops 6's strength for the showing marker: full at the hit, e^-rate a second after.
    private float Bo6Alpha() => Mathf.Exp(-Mathf.Max(0f, bo6.fadeRate) * age);

    // How long the Black Ops 6 fade lasts: until it is under Fade End; with no fade, the look's own span.
    private float Bo6Lifetime() =>
        bo6.fadeRate > 0f ? Mathf.Log(1f / Mathf.Clamp(bo6.fadeEnd, .0001f, .99f)) / bo6.fadeRate : baseLifetime;

    // Black Ops 6's size: 1 + Pop at the hit, shrinking back.
    private float Bo6Pop() => 1f + bo6.pop * Mathf.Exp(-Mathf.Max(0f, bo6.popRate) * age);

    // How far past the look's resting gap the lines stand under the Black Ops 6 spread: the hip place, and once the hold
    // is over the move outward.
    private float Bo6Spread()
    {
        float after = age - bo6.holdSeconds;
        return hipNow + (after > 0f ? bo6.spreadOut * (1f - Mathf.Exp(-Mathf.Max(0f, bo6.spreadRate) * after)) : 0f);
    }

    // One of a headshot's two lines under the Black Ops 6 twin switch: the arm's line turned Twin Turn degrees about a point
    // Twin Apart / 2 of its length beside its outer end, toward side (±1), so the pair start close outside and open toward
    // the aim point, as the addon's head pictures draw them. inner: the arm's inner end's distance from the aim point.
    private void TwinLine(float angle, float inner, float length, float side, out Vector2 middle, out float turned)
    {
        Vector2 outer = Dir(angle) * (inner + length) + Dir(angle + 90f) * (side * bo6.twinApart * length * .5f);
        turned = angle - side * bo6.twinTurn;
        middle = outer - Dir(turned) * (length * .5f);
    }

    private static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

    private float CleanHold() => IsKill(kind) ? look.killHoldSeconds : look.holdSeconds;
    private float CleanFade() => IsKill(kind) ? look.killFadeSeconds : look.fadeSeconds;

    // The clean marker (PAYDAY 3's, the user's pick): four hair lines at 45° that start a little big and snap to rest,
    // hold a moment at full strength, then fade while sliding a few pixels outward. No ring and no slide-in: the lines are
    // thin and gone in .2 s, so they never hide the target, and the snap carries the hit. A new hit restarts the snap. A
    // headshot snaps twice and starts thicker (draft E), so it reads as more without drawing over the target.
    private void LayoutClean(GunFeelSettings settings)
    {
        bool kill = IsKill(kind), head = kind == HitMarkerKind.Head || kind == HitMarkerKind.HeadKill, blocked = kind == HitMarkerKind.Blocked;
        // The Black Ops 6 shake and turn move the whole marker about the aim point (both nothing while switched off).
        cleanRoot.anchoredPosition = AimPoint() + shakeAt;
        cleanRoot.localRotation = Quaternion.Euler(0f, 0f, turn);
        float hold = CleanHold(), fadeTime = CleanFade(), popTime = Mathf.Max(.01f, look.popSeconds);
        float popBy = blocked ? 0f : kill ? look.killPop : head ? look.headPop : look.pop;
        float settle = 1f - Mathf.Clamp01(age / popTime);
        // Black Ops 6's pop takes the place of the first snap (a headshot's second still follows), and swells the width
        // with it as the addon's whole arm picture grows.
        bool bigPop = bo6Pop && !blocked;
        float pop = bigPop ? Bo6Pop() : 1f + popBy * settle * settle * settle;
        if (head && look.headPopAgain > 0f && age >= look.headPopAgainAt)
        {
            float again = 1f - Mathf.Clamp01((age - look.headPopAgainAt) / popTime);
            pop += look.headPopAgain * again * again * again;
        }
        float fade = fadeTime > 0f ? Mathf.Clamp01((age - hold) / fadeTime) : age >= hold ? 1f : 0f;
        float alpha = 1f - Smooth(fade), drift = look.drift * (1f - (1f - fade) * (1f - fade));
        // Black Ops 6's fade in place of the hold and fade, the drift going with how far it has faded.
        if (bo6Fade)
        {
            alpha = Bo6Alpha();
            drift = look.drift * (1f - alpha * alpha);
        }
        // Black Ops 6's hold, then spread, in place of the drift.
        if (bo6Spread) drift = Bo6Spread();
        float length = look.length * (kill ? look.killLength : blocked ? look.blockedLength : head ? look.headLength : 1f) * scale * pop;
        float thickness = look.thickness * (kill ? look.killThickness : blocked ? look.blockedThickness : 1f) * (bigPop ? Bo6Pop() : 1f);
        if (head && look.headThickness > 1f)
            thickness *= Mathf.Lerp(look.headThickness, 1f, Smooth(Mathf.Clamp01((age - look.flashSeconds) / popTime)));
        float gap = look.gap * pop + drift;
        Color color = kill ? look.killColor : head ? look.headColor : blocked ? look.blockedColor : look.bodyColor;
        if (!blocked && look.flashSeconds > 0f) color = Color.Lerp(Color.white, color, Mathf.Clamp01(age / look.flashSeconds));
        color.a *= alpha * (blocked ? look.blockedAlpha : 1f);
        // Squared, so the edge is gone before the line and a fading white line never turns grey over it.
        float edgeWidth = look.edgeAlpha > 0f ? Mathf.Max(0f, look.edgeWidth) : 0f;
        var edge = new Color(0f, 0f, 0f, look.edgeAlpha * color.a * color.a);

        // Through or off something on the way: as the Bold ticks, each line trails a fainter tail outward.
        float tail = length * settings.piercedTailLength * Mathf.Lerp(.6f, 1f, Smooth(Mathf.Clamp01(age / lifetime)));
        float nearTail = tail * .55f, farTail = tail * .45f, tailStart = gap + length + TailGap, span = FeelAssets.HairLineSpan;
        Color near = color, far = color;
        near.a *= settings.piercedTailAlpha;
        far.a *= settings.piercedTailAlpha * .42f;
        // Black Ops 6's headshot arm: each line split in two from its outer end (head kills too), each with its own edge.
        bool twin = bo6Twin && head;
        var lineSize = new Vector2(length, thickness * span);
        var edgeSize = new Vector2(length + edgeWidth * 2f, (thickness + edgeWidth * 2f) * span);
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 direction = Dir(angle);
            cleanEdges[i].enabled = edgeWidth > 0f;
            cleanTwins[i].enabled = twin;
            cleanTwinEdges[i].enabled = twin && edgeWidth > 0f;
            if (twin)
            {
                TwinLine(angle, gap, length, 1f, out Vector2 one, out float oneTurn);
                TwinLine(angle, gap, length, -1f, out Vector2 other, out float otherTurn);
                if (edgeWidth > 0f)
                {
                    Place(cleanEdges[i], one, edgeSize, oneTurn, edge);
                    Place(cleanTwinEdges[i], other, edgeSize, otherTurn, edge);
                }
                Place(cleanLines[i], one, lineSize, oneTurn, color);
                Place(cleanTwins[i], other, lineSize, otherTurn, color);
            }
            else
            {
                Vector2 middle = direction * (gap + length * .5f);
                if (edgeWidth > 0f) Place(cleanEdges[i], middle, edgeSize, angle, edge);
                Place(cleanLines[i], middle, lineSize, angle, color);
            }
            cleanTails[i].enabled = cleanTailEnds[i].enabled = pierced;
            if (!pierced) continue;
            Place(cleanTails[i], direction * (tailStart + nearTail * .5f), new Vector2(nearTail, thickness * .7f * span), angle, near);
            Place(cleanTailEnds[i], direction * (tailStart + nearTail + farTail * .5f), new Vector2(farTail, thickness * .5f * span), angle, far);
        }
    }

    /// <summary>How long the headshot's burst shows: its longest part that is switched on.</summary>
    private float HeadSeconds()
    {
        var h = headLook;
        float seconds = 0f;
        if (h.flareTo > h.flareFrom) seconds = Mathf.Max(seconds, h.flareSeconds);
        if (h.riseTo > h.riseFrom) seconds = Mathf.Max(seconds, h.riseSeconds);
        if (h.ringTo > 0f) seconds = Mathf.Max(seconds, h.ringSeconds);
        if (h.echoTo > 0f) seconds = Mathf.Max(seconds, h.echoDelay + h.echoSeconds);
        if (sparks != null && sparks.Length > 0) seconds = Mathf.Max(seconds, h.sparkSeconds);
        if (h.starSize > 0f) seconds = Mathf.Max(seconds, h.starSeconds);
        return seconds;
    }

    // The headshot's burst (HeadLook), each part on its own clock from the hit and hidden once it is over. It bangs and is
    // gone in .16 s, the user's rule for every hit effect, and leaves the middle clear.
    private void LayoutHead()
    {
        var h = headLook;
        float a = headAge;
        headRoot.anchoredPosition = AimPoint();

        // The split lens streak: each half shoots out from just outside the lines while its inner end drifts outward, so
        // the middle only opens wider; white-hot as it opens, gold as it thins and goes.
        float ft = h.flareSeconds > 0f ? Mathf.Clamp01(a / h.flareSeconds) : 1f;
        bool flare = h.flareTo > h.flareFrom && a < h.flareSeconds;
        float flareOpen = 1f - Cube(1f - Mathf.Clamp01(a / Mathf.Max(.01f, h.flareOpen))), flareFade = Square(1f - ft);
        Color light = Color.Lerp(Color.white, h.color, Mathf.Clamp01(ft * 2.5f));
        float inner = Mathf.Lerp(h.flareFrom, h.flareDrift, 1f - Square(1f - ft));
        float length = Mathf.Max(0f, Mathf.Lerp(h.flareFrom, h.flareTo, flareOpen) - inner);
        float width = Mathf.Lerp(h.flareWidth, h.flareEndWidth, ft);
        for (int i = 0; i < 2; i++)
        {
            bool on = flare && length > .5f;
            flareHalves[i].enabled = flareGlows[i].enabled = on;
            if (!on) continue;
            // The texture's bright end faces the aim point: as it is on the right, turned 180° on the left.
            float side = i == 0 ? 1f : -1f, turn = i == 0 ? 0f : 180f;
            var middle = new Vector2(side * (inner + length * .5f), 0f);
            Place(flareGlows[i], middle, new Vector2(length, width * h.flareGlowWidth), turn, KitStyle.With(light, h.flareGlowAlpha * flareFade));
            Place(flareHalves[i], middle, new Vector2(length, width), turn, KitStyle.With(light, flareFade));
        }
        float rt = h.riseSeconds > 0f ? Mathf.Clamp01(a / h.riseSeconds) : 1f;
        float riseIn = Mathf.Lerp(h.riseFrom, h.riseDrift, rt);
        float riseLength = Mathf.Max(0f, Mathf.Lerp(h.riseFrom, h.riseTo, 1f - Cube(1f - Mathf.Clamp01(a / Mathf.Max(.01f, h.riseOpen)))) - riseIn);
        for (int i = 0; i < 2; i++)
        {
            bool on = h.riseTo > h.riseFrom && a < h.riseSeconds && riseLength > .5f;
            riseHalves[i].enabled = on;
            if (!on) continue;
            float side = i == 0 ? 1f : -1f;
            Place(riseHalves[i], new Vector2(0f, side * (riseIn + riseLength * .5f)), new Vector2(riseLength, Mathf.Lerp(h.riseWidth, h.riseEndWidth, rt)),
                i == 0 ? 90f : -90f, KitStyle.With(light, h.riseAlpha * Square(1f - rt)));
        }
        for (int i = 0; i < 2; i++)
        {
            bool on = flare && h.diamonds > 0f;
            flareDiamonds[i].enabled = on;
            if (!on) continue;
            // They slide out along the slant as the streak opens, and stay there.
            var rect = flareDiamonds[i].rectTransform;
            rect.anchoredPosition = GhostSlant * (Diamonds[i].x * Mathf.Lerp(.7f, 1f, flareOpen));
            rect.sizeDelta = Vector2.one * (Diamonds[i].y * Mathf.Lerp(1.3f, 1f, flareOpen));
            flareDiamonds[i].color = KitStyle.With(h.color, Diamonds[i].z * h.diamonds * flareFade * flareOpen);
        }

        // The thin ring snapping out through the lines, white for a frame or two, a soft dark ring under it, and a fainter
        // ring a beat behind that opens further.
        bool ringOn = h.ringTo > 0f && a < h.ringSeconds;
        headRing.enabled = headRingEdge.enabled = ringOn;
        if (ringOn)
        {
            float bt = Mathf.Clamp01(a / Mathf.Max(.01f, h.ringSeconds));
            float radius = Mathf.Lerp(h.ringFrom, h.ringTo, 1f - Cube(1f - Mathf.Clamp01(a / Mathf.Max(.01f, h.ringSeconds * .86f))));
            float lineWidth = Mathf.Lerp(h.ringWidth, h.ringEndWidth, bt);
            float strength = a < h.ringHold ? 1f : Square(1f - Mathf.Clamp01((a - h.ringHold) / Mathf.Max(.01f, h.ringSeconds - h.ringHold)));
            headRingEdge.Set(radius, lineWidth + 2f);
            headRingEdge.color = new Color(0f, 0f, 0f, h.ringEdgeAlpha * strength * strength);
            headRing.Set(radius, lineWidth);
            headRing.color = KitStyle.With(Color.Lerp(Color.white, h.color, Mathf.Clamp01((a - .02f) / .03f)), strength);
        }
        float echo = a - h.echoDelay;
        bool echoOn = h.echoTo > 0f && echo >= 0f && echo < h.echoSeconds;
        echoRing.enabled = echoOn;
        if (echoOn)
        {
            float et = Mathf.Clamp01(echo / Mathf.Max(.01f, h.echoSeconds));
            echoRing.Set(Mathf.Lerp(h.ringFrom, h.echoTo, 1f - Cube(1f - et)), Mathf.Lerp(h.echoWidth, h.echoEndWidth, et));
            echoRing.color = KitStyle.With(h.color, h.echoAlpha * Square(1f - et));
        }

        // Sparks flying out between the lines' directions, from outside the lines; every other one shorter and nearer.
        float st = h.sparkSeconds > 0f ? Mathf.Clamp01(a / h.sparkSeconds) : 1f;
        bool sparkOn = a < h.sparkSeconds;
        float burst = 1f - Cube(1f - st);
        Color hot = Color.Lerp(Color.white, h.color, Mathf.Clamp01(a / .04f));
        Color spark = KitStyle.With(Color.Lerp(Color.white, hot, Mathf.Clamp01(st * 1.6f)), 1f - st);
        float distance = Mathf.Lerp(h.sparkFrom, h.sparkTo, burst), sparkLength = Mathf.Lerp(h.sparkLength, h.sparkEndLength, st);
        float sparkWidth = Mathf.Lerp(h.sparkWidth, h.sparkEndWidth, st);
        for (int i = 0; i < sparks.Length; i++)
        {
            sparks[i].enabled = sparkOn;
            if (!sparkOn) continue;
            float angle = 180f / sparks.Length + 360f / sparks.Length * i, own = i % 2 == 0 ? 1f : .75f;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Place(sparks[i], direction * (distance * own + sparkLength * own * .5f), new Vector2(sparkLength * own, sparkWidth), angle, spark);
        }

        // The hollow star: it blows open white past full size in its first frames, snaps back gold, turns and is gone;
        // its middle is cut out, so the head it hit shows through it.
        bool starOn = h.starSize > 0f && a < h.starSeconds;
        starRoot.gameObject.SetActive(starOn);
        if (starOn)
        {
            float total = h.starSeconds, open = Mathf.Min(.02f, total * .15f), settle = Mathf.Min(.05f, total * .36f);
            float size = a < open ? h.starOpen * (1f - Square(1f - a / open))
                : a < settle ? Mathf.Lerp(h.starOpen, 1f, Smooth((a - open) / (settle - open))) : 1f;
            float fade = a < settle ? 0f : Mathf.Clamp01((a - settle) / Mathf.Max(.01f, total - settle));
            size *= Mathf.Lerp(1f, .75f, fade);
            starRoot.sizeDelta = Vector2.one * (h.starSize * 2f * size);
            starRoot.localRotation = Quaternion.Euler(0f, 0f, h.starTurn * (1f - Square(1f - Mathf.Clamp01(a / total))));
            float heat = Mathf.Clamp01((a - open) / Mathf.Max(.01f, settle - open)), alpha = Square(1f - fade);
            Color gold = Color.Lerp(Color.white, h.color, heat);
            star.color = KitStyle.With(gold, alpha);
            // The turned second star stays whiter, so the eight points read as two layers of light.
            starCross.rectTransform.localScale = Vector3.one * h.crossScale;
            starCross.color = KitStyle.With(Color.Lerp(gold, Color.white, .55f), alpha * .9f);
            starGlow.rectTransform.localScale = Vector3.one * h.glowScale;
            starGlow.color = KitStyle.With(Color.Lerp(Color.white, h.glowColor, heat), alpha * h.glowStrength);
        }
    }

    // The posture break: an orange ring bursting out and four shards on the axes flying off, white-hot for a moment.
    private void LayoutBreak(GunFeelSettings settings)
    {
        breakRoot.anchoredPosition = AimPoint();
        float t = Mathf.Clamp01(breakAge / settings.breakSeconds);
        float burst = 1f - (1f - t) * (1f - t) * (1f - t), fade = 1f - t;
        Color color = Color.Lerp(Color.white, settings.breakColor, Mathf.Clamp01(breakAge / .05f));
        float reach = settings.markerGap + settings.markerLength;
        float ringSize = Mathf.Lerp(settings.markerShrunkGap * 2f, reach * 2.6f, burst);
        breakRing.rectTransform.sizeDelta = new Vector2(ringSize, ringSize);
        Color ringColor = color;
        ringColor.a = .9f * fade * fade;
        breakRing.color = ringColor;

        float distance = Mathf.Lerp(settings.markerGap * .8f, reach * 1.15f, burst);
        float length = settings.markerLength * .7f * (1f - .4f * t), thickness = settings.markerThickness * 1.2f;
        Color shardColor = color;
        shardColor.a = fade;
        for (int i = 0; i < 4; i++)
        {
            float angle = 90f * i;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Place(shards[i], direction * (distance + length * .5f), new Vector2(length, thickness), angle, shardColor);
        }
    }

    private static void Place(Graphic image, Vector2 position, Vector2 size, float angle, Color color)
    {
        var rect = image.rectTransform;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        image.color = color;
    }

    private static float Square(float x) => x * x;
    private static float Cube(float x) => x * x * x;
    private static float Smooth(float x) => x * x * (3f - 2f * x);
}
