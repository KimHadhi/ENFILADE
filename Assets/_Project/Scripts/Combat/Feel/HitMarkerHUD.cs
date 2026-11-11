using UnityEngine;
using UnityEngine.UI;

public enum HitMarkerKind { Body, Head, BodyKill, HeadKill, Blocked }

/// <summary>
/// Crosshair hit confirmation, in the look the player picks (Settings → HUD → 히트 마커, <see cref="HitMarkerStyle"/>).
/// Clean, the default (the user's pick of PAYDAY 3's marker, 2026-11-12): four hair lines at 45° that start a little big,
/// snap to rest, hold a moment and fade sliding a few pixels outward, in .2 s; white, a pale gold headshot, a red kill,
/// a quiet grey for no effect, numbers in HitMarkers.csv (<see cref="CleanLook"/>, <see cref="LayoutClean"/>).
/// Bold, the earlier look, described below, numbers in GunFeel. Both get the headshot's glint, burst and flare, the
/// posture break's burst and the pierced tails. Bold: four diagonal ticks around the centre:
/// body hit = white; headshot = gold, a little bolder and quicker, with a glowing eight-point glint, a bloom, a white and a gold ring and twelve sparks
/// banging out at the aim point and gone within .2 s, and a lens's flare across it, thin: a streak of light, a shorter
/// one up and down it and four ghosts along a slant (head kills keep
/// the glint on their red); any kill (body or head) = red with a shock ring; no effect (guard/parry/invulnerable) = thin
/// grey. Body and head markers grow with the share of health the hit took. A bullet that came through or off something on
/// the way (DamageHit.Pierced) gives each tick a fading tail outward in its colour, growing a little while the marker
/// shows, as if the shot carried on through (the user's pick of four mockups).
/// Back 4 Blood timing, in frames at GunFeel's marker frame rate: the marker holds wide (7), the ticks slide in to the
/// shrunk gap (9), show faded for one frame and vanish; every new hit restarts it wide, a little bigger at first and
/// popping back, so each hit of a burst reads on its own. A posture break adds its own burst on top: an orange ring and
/// four shards flying out (<see cref="ShowBreak"/>). All of it is drawn at the steady aim point projected through the
/// kicked camera, so it jumps with the view punch and settles back with it (runs after CameraViewPunch). Created on first
/// use during Play; colours and sizes in GunFeel. The sounds that go with it: <see cref="HitConfirm"/>.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HitMarkers)]
public sealed class HitMarkerHUD : MonoBehaviour
{
    public const string TableName = "HitMarkers", CleanRow = "Clean";

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
        public float headLength = 1.12f, headPop = .4f;
        [Tooltip("A kill's lines: longer, thicker, a bigger pop, and a little more time to hold and fade so the kill reads.")]
        public float killLength = 1.3f, killThickness = 1.25f, killPop = .45f, killHoldSeconds = .1f, killFadeSeconds = .18f;
        [Tooltip("A hit that did nothing (guard, parry, invulnerable): shorter, thinner and quieter, with no pop.")]
        public float blockedLength = .75f, blockedThickness = .8f, blockedAlpha = .8f;
    }

    // Between a tick's outer end and its pierced tail (canvas units).
    private const float TailGap = 1.5f;
    private static HitMarkerHUD instance;
    // A headshot's sparks, set between the ticks and the glint's rays so all three read at once.
    private const int SparkCount = 12;
    // The flare's ghosts: where along the slant each lies (a share of the streak's reach; negative is the other side of the
    // aim point), its size (px) and how bright it is. Uneven, as a lens throws them.
    private static readonly Vector3[] Ghosts = { new Vector3(-.21f, 8f, .5f), new Vector3(.16f, 12f, .35f), new Vector3(.35f, 6f, .6f), new Vector3(.58f, 17f, .22f) };
    // The slant they lie along: right and a little down.
    private static readonly Vector2 GhostSlant = new Vector2(.924f, -.383f);
    private readonly Image[] ticks = new Image[4], tails = new Image[4], tailEnds = new Image[4], shards = new Image[4],
        sparks = new Image[SparkCount];
    private readonly Graphic[] ghosts = new Graphic[Ghosts.Length];
    // The clean marker's lines, the soft dark edges drawn under them, and their pierced tails.
    private readonly RawImage[] cleanLines = new RawImage[4], cleanEdges = new RawImage[4], cleanTails = new RawImage[4],
        cleanTailEnds = new RawImage[4];
    private readonly CleanLook look = new CleanLook();
    private RectTransform root, cleanRoot, glintRoot, breakRoot, burstRoot, flareRoot;
    // The look the showing marker started in: a change in the settings takes effect from the next hit.
    private HitMarkerStyle style;
    private RawImage ring, glintGlow, glintGlowCross, glint, glintCross, glintCore, breakRing, burstRing, burstFlashRing, burstBloom, flareStreak, flareRise;
    private HitMarkerKind kind;
    private bool pierced;
    private float age = 99f, lifetime = .2f, scale = 1f, glintAge = 99f, breakAge = 99f, burstAge = 99f, flareAge = 99f, pace = 1f;

    public HitMarkerKind LastKind => kind;
    /// <summary>The last marker came through or off something on the way, so its ticks trail tails.</summary>
    public bool LastPierced => pierced;
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
        root = FeelUI.Rect("Marker", transform);
        ring = FeelUI.Rect("Ring", root).gameObject.AddComponent<RawImage>();
        ring.texture = FeelAssets.Ring; ring.raycastTarget = false;
        for (int i = 0; i < 4; i++) ticks[i] = Tick("Tick " + i, root);
        for (int i = 0; i < 4; i++) { tails[i] = Tail("Pierced Tail " + i, root); tailEnds[i] = Tail("Pierced Tail End " + i, root); }
        root.gameObject.SetActive(false);

        // The clean marker, in the same place under the headshot layers: the dark edges first so every line covers them.
        cleanRoot = FeelUI.Rect("Clean Marker", transform);
        for (int i = 0; i < 4; i++) cleanEdges[i] = HairLine("Edge " + i, cleanRoot);
        for (int i = 0; i < 4; i++) cleanLines[i] = HairLine("Line " + i, cleanRoot);
        for (int i = 0; i < 4; i++)
        {
            cleanTails[i] = HairLine("Pierced Tail " + i, cleanRoot);
            cleanTailEnds[i] = HairLine("Pierced Tail End " + i, cleanRoot);
        }
        cleanRoot.gameObject.SetActive(false);

        // Its own root, so the glint can stay longer than the ticks: an empty turning frame, its layers stretched over it,
        // bottom to top: the glow, the star blurred and added onto the burst's light (the turned second star gets a smaller
        // one); the gold star; a smaller star turned 45° for eight points; a white-hot core. The glow replaced a black
        // outline that set the star on the light like a sticker (the user's pick of four, 2026-11-09).
        glintRoot = FeelUI.Rect("Glint", transform);
        var additive = FeelAssets.UiAdditive;
        glintGlow = GlintLayer("Glow", glintRoot, FeelAssets.GlintGlow, 0f, 1f, additive);
        glintGlowCross = GlintLayer("Glow Cross", glintRoot, FeelAssets.GlintGlow, 45f, 1f, additive);
        glint = GlintLayer("Star", glintRoot, FeelAssets.Glint, 0f, 1f, null);
        glintCross = GlintLayer("Star Cross", glintRoot, FeelAssets.Glint, 45f, .6f, null);
        glintCore = GlintLayer("Core", glintRoot, FeelAssets.Glint, 0f, 1f, null);
        glintRoot.gameObject.SetActive(false);

        // A headshot's bang: a bloom of light, a white ring flashing open inside a gold one and sparks flying out, gone in a
        // moment. Its own root, unturned, under the glint.
        burstRoot = FeelUI.Rect("Head Burst", transform);
        burstRoot.SetSiblingIndex(glintRoot.GetSiblingIndex());
        burstBloom = FeelUI.Rect("Bloom", burstRoot).gameObject.AddComponent<RawImage>();
        burstBloom.texture = FeelAssets.SoftDot; burstBloom.raycastTarget = false;
        burstRing = FeelUI.Rect("Ring", burstRoot).gameObject.AddComponent<RawImage>();
        burstRing.texture = FeelAssets.Ring; burstRing.raycastTarget = false;
        burstFlashRing = FeelUI.Rect("Flash Ring", burstRoot).gameObject.AddComponent<RawImage>();
        burstFlashRing.texture = FeelAssets.Ring; burstFlashRing.raycastTarget = false;
        for (int i = 0; i < SparkCount; i++) sparks[i] = Tail("Spark " + i, burstRoot);
        burstRoot.gameObject.SetActive(false);

        // A headshot's lens flare, under the burst: two soft streaks and the ghosts, all added onto the picture so they
        // light it without covering it. Every other ghost is the HUD's diamond.
        flareRoot = FeelUI.Rect("Head Flare", transform);
        flareRoot.SetSiblingIndex(burstRoot.GetSiblingIndex());
        flareStreak = SoftLight("Streak", flareRoot, additive);
        flareRise = SoftLight("Rise", flareRoot, additive);
        for (int i = 0; i < Ghosts.Length; i++)
        {
            if (i % 2 == 0) ghosts[i] = SoftLight("Ghost " + i, flareRoot, additive);
            else
            {
                ghosts[i] = HudShapes.Make<DiamondGraphic>("Ghost " + i, flareRoot, Color.white);
                if (additive != null) ghosts[i].material = additive;
            }
        }
        flareRoot.gameObject.SetActive(false);

        breakRoot = FeelUI.Rect("Break", transform);
        breakRing = FeelUI.Rect("Ring", breakRoot).gameObject.AddComponent<RawImage>();
        breakRing.texture = FeelAssets.Ring; breakRing.raycastTarget = false;
        for (int i = 0; i < 4; i++) shards[i] = Tick("Shard " + i, breakRoot);
        breakRoot.gameObject.SetActive(false);
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

    // One layer of the glint, stretched over its root and turned and scaled within it. material null: alpha blended.
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

    // A soft blob of light: stretched long and thin it is a streak, left round a ghost. material null: alpha blended.
    private static RawImage SoftLight(string name, Transform parent, Material material)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.texture = FeelAssets.SoftDot; image.raycastTarget = false;
        if (material != null) image.material = material;
        return image;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Trigger(HitMarkerKind next, float share, bool throughSomething)
    {
        // A fresh kill marker is not overwritten by the next body hit of the same burst.
        if (IsShowing && IsKill(kind) && !IsKill(next) && age < .18f) return;
        kind = next; age = 0f;
        pierced = throughSomething;
        var settings = GunFeelSettings.Instance;
        style = GameSettings.Current.hitMarkerStyle;
        bool clean = style == HitMarkerStyle.Clean, sized = !IsKill(next) && next != HitMarkerKind.Blocked;
        float hurt = Mathf.Clamp01(share / settings.markerFullDamage);
        // Only one look shows at a time; the other may still be up from before the setting changed.
        (clean ? root : cleanRoot).gameObject.SetActive(false);
        if (clean)
        {
            scale = sized ? Mathf.Lerp(look.smallScale, look.largeScale, hurt) : 1f;
            pace = 1f;
            lifetime = Mathf.Max(.02f, CleanHold() + CleanFade());
            cleanRoot.gameObject.SetActive(true);
            LayoutClean(settings);
        }
        else
        {
            scale = sized ? Mathf.Lerp(settings.markerSmallScale, settings.markerLargeScale, hurt) : 1f;
            // A headshot's ticks run through their frames faster, so the bang is short; kills keep their own time.
            pace = next == HitMarkerKind.Head ? Mathf.Max(1f, settings.headMarkerPace) : 1f;
            int frames = Mathf.Max(1, settings.markerHoldFrames + settings.markerShrinkFrames + settings.markerFadeFrames);
            lifetime = frames / settings.markerFrameRate / pace;
            root.gameObject.SetActive(true);
            Layout(settings);
        }
        bool head = next == HitMarkerKind.Head || next == HitMarkerKind.HeadKill;
        if (head && settings.headGlintSize > 0f)
        {
            glintAge = 0f;
            glintRoot.gameObject.SetActive(true);
            LayoutGlint(settings);
        }
        if (head && settings.headBurstReach > 0f)
        {
            burstAge = 0f;
            burstRoot.gameObject.SetActive(true);
            LayoutBurst(settings);
        }
        if (head && settings.headFlareReach > 0f)
        {
            flareAge = 0f;
            flareRoot.gameObject.SetActive(true);
            LayoutFlare(settings);
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
            else if (clean) LayoutClean(settings);
            else Layout(settings);
        }
        if (glintRoot.gameObject.activeSelf)
        {
            glintAge += Time.unscaledDeltaTime;
            if (glintAge >= settings.headGlintSeconds) glintRoot.gameObject.SetActive(false);
            else LayoutGlint(settings);
        }
        if (burstRoot.gameObject.activeSelf)
        {
            burstAge += Time.unscaledDeltaTime;
            if (burstAge >= settings.headBurstSeconds) burstRoot.gameObject.SetActive(false);
            else LayoutBurst(settings);
        }
        if (flareRoot.gameObject.activeSelf)
        {
            flareAge += Time.unscaledDeltaTime;
            if (flareAge >= settings.headFlareSeconds) flareRoot.gameObject.SetActive(false);
            else LayoutFlare(settings);
        }
        if (breakRoot.gameObject.activeSelf)
        {
            breakAge += Time.unscaledDeltaTime;
            if (breakAge >= settings.breakSeconds) breakRoot.gameObject.SetActive(false);
            else LayoutBreak(settings);
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
        root.anchoredPosition = AimPoint();
        // Hold wide, slide the ticks in, one faded frame at the shrunk size, gone. A headshot runs it faster (pace).
        float frame = age * pace * settings.markerFrameRate;
        int hold = settings.markerHoldFrames, shrinkFrames = settings.markerShrinkFrames;
        float shrink = shrinkFrames > 0 ? Mathf.Clamp01((frame - hold) / shrinkFrames) : frame >= hold ? 1f : 0f;
        float alpha = frame >= hold + shrinkFrames ? settings.markerFadeAlpha : 1f;
        // A little bigger at first, popping back, so every hit of a burst reads as its own; a headshot pops harder.
        float popBy = kind == HitMarkerKind.Head ? settings.headPop : settings.markerPop;
        float pop = 1f + popBy * (1f - Smooth(Mathf.Clamp01(age / settings.markerPopSeconds)));
        // Body and head hits share one shape; a headshot is gold and a little bolder, and the damage sets the size.
        bool bold = head && !kill;
        float length = settings.markerLength * (kill ? 1.45f : blocked ? .8f : bold ? settings.headLength : 1f) * scale * pop;
        float thickness = settings.markerThickness * (kill ? 1.5f : blocked ? .75f : bold ? settings.headThickness : 1f);
        float gap = Mathf.Lerp(settings.markerGap, settings.markerShrunkGap, Smooth(shrink)) * pop;
        Color color = kill ? settings.killColor : head ? settings.headHitColor : blocked ? settings.blockedColor : settings.bodyHitColor;
        if (!blocked) color = Color.Lerp(Color.white, color, Mathf.Clamp01(age / .05f)); // white-hot first frame
        color.a *= alpha;

        // Through or off something on the way: each tick trails a tail outward, near half brighter, growing as the marker shows.
        float tail = length * settings.piercedTailLength * Mathf.Lerp(.6f, 1f, Smooth(Mathf.Clamp01(age / lifetime)));
        float nearTail = tail * .55f, farTail = tail * .45f, tailStart = gap + length + TailGap;
        Color near = color, far = color;
        near.a *= settings.piercedTailAlpha;
        far.a *= settings.piercedTailAlpha * .42f;
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Place(ticks[i], direction * (gap + length * .5f), new Vector2(length, thickness), angle, color);
            tails[i].enabled = tailEnds[i].enabled = pierced;
            if (!pierced) continue;
            Place(tails[i], direction * (tailStart + nearTail * .5f), new Vector2(nearTail, thickness * .7f), angle, near);
            Place(tailEnds[i], direction * (tailStart + nearTail + farTail * .5f), new Vector2(farTail, thickness * .5f), angle, far);
        }

        ring.enabled = kill;
        if (kill)
        {
            float wave = Mathf.Clamp01(age / lifetime);
            float size = Mathf.Lerp(gap * 2f, (settings.markerGap + length) * 2.8f, 1f - Mathf.Pow(1f - wave, 2f));
            ring.rectTransform.sizeDelta = new Vector2(size, size);
            Color ringColor = settings.killColor;
            ringColor.a = .85f * (1f - wave);
            ring.color = ringColor;
        }
    }

    private float CleanHold() => IsKill(kind) ? look.killHoldSeconds : look.holdSeconds;
    private float CleanFade() => IsKill(kind) ? look.killFadeSeconds : look.fadeSeconds;

    // The clean marker (PAYDAY 3's, the user's pick): four hair lines at 45° that start a little big and snap to rest,
    // hold a moment at full strength, then fade while sliding a few pixels outward. No ring and no slide-in: the lines are
    // thin and gone in .2 s, so they never hide the target, and the snap carries the hit. A new hit restarts the snap.
    private void LayoutClean(GunFeelSettings settings)
    {
        bool kill = IsKill(kind), head = kind == HitMarkerKind.Head || kind == HitMarkerKind.HeadKill, blocked = kind == HitMarkerKind.Blocked;
        cleanRoot.anchoredPosition = AimPoint();
        float hold = CleanHold(), fadeTime = CleanFade();
        float popBy = blocked ? 0f : kill ? look.killPop : head ? look.headPop : look.pop;
        float settle = 1f - Mathf.Clamp01(age / Mathf.Max(.01f, look.popSeconds));
        float pop = 1f + popBy * settle * settle * settle;
        float fade = fadeTime > 0f ? Mathf.Clamp01((age - hold) / fadeTime) : age >= hold ? 1f : 0f;
        float alpha = 1f - Smooth(fade), drift = look.drift * (1f - (1f - fade) * (1f - fade));
        float length = look.length * (kill ? look.killLength : blocked ? look.blockedLength : head ? look.headLength : 1f) * scale * pop;
        float thickness = look.thickness * (kill ? look.killThickness : blocked ? look.blockedThickness : 1f);
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
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector2 middle = direction * (gap + length * .5f);
            cleanEdges[i].enabled = edgeWidth > 0f;
            if (edgeWidth > 0f)
                Place(cleanEdges[i], middle, new Vector2(length + edgeWidth * 2f, (thickness + edgeWidth * 2f) * span), angle, edge);
            Place(cleanLines[i], middle, new Vector2(length, thickness * span), angle, color);
            cleanTails[i].enabled = cleanTailEnds[i].enabled = pierced;
            if (!pierced) continue;
            Place(cleanTails[i], direction * (tailStart + nearTail * .5f), new Vector2(nearTail, thickness * .7f * span), angle, near);
            Place(cleanTailEnds[i], direction * (tailStart + nearTail + farTail * .5f), new Vector2(farTail, thickness * .5f * span), angle, far);
        }
    }

    // The headshot glint, a bang and gone: it blows open white well past full size in a frame or two, snaps back to gold,
    // and is out within headGlintSeconds (.2), fading fast while it draws in and spins its rays off the crosshair's lines.
    // The user found the first one (.45 s, a held beat) lingering; a headshot should land and be over. A glow added behind
    // the star lets it shine into the burst's light, and a white core keeps its heart hot while its tips turn gold.
    private void LayoutGlint(GunFeelSettings settings)
    {
        glintRoot.anchoredPosition = AimPoint();
        float total = settings.headGlintSeconds, open = Mathf.Min(.025f, total * .15f), settle = Mathf.Min(.06f, total * .35f);
        float size = glintAge < open ? 1.5f * (1f - (1f - glintAge / open) * (1f - glintAge / open))
            : glintAge < settle ? Mathf.Lerp(1.5f, 1f, Smooth((glintAge - open) / (settle - open))) : 1f;
        float fade = glintAge < settle ? 0f : Mathf.Clamp01((glintAge - settle) / Mathf.Max(.01f, total - settle));
        size *= Mathf.Lerp(1f, .7f, fade);
        glintRoot.sizeDelta = Vector2.one * (settings.headGlintSize * 2f * size);
        float spin = 1f - (1f - Mathf.Clamp01(glintAge / total)) * (1f - Mathf.Clamp01(glintAge / total));
        glintRoot.localRotation = Quaternion.Euler(0f, 0f, settings.headGlintTurn * spin);
        float heat = Mathf.Clamp01((glintAge - open) / Mathf.Max(.01f, settle - open)), alpha = (1f - fade) * (1f - fade);
        Color glow = Color.Lerp(Color.white, settings.headHitColor, heat);
        glow.a = alpha;
        glint.color = glow;
        // The turned second star stays whiter, so the eight points read as two layers of light.
        Color cross = Color.Lerp(glow, Color.white, .55f);
        cross.a = alpha * .9f;
        glintCross.color = cross;
        glintCore.rectTransform.localScale = Vector3.one * settings.headCoreScale;
        glintCore.color = new Color(1f, 1f, 1f, alpha);

        // White while the star bangs open, a deeper ember than the gold once it settles, so the light has a warm edge.
        Color light = Color.Lerp(Color.white, settings.headGlowColor, heat);
        glintGlow.rectTransform.localScale = Vector3.one * settings.headGlowScale;
        light.a = alpha * settings.headGlowStrength;
        glintGlow.color = light;
        glintGlowCross.rectTransform.localScale = Vector3.one * (settings.headGlowScale * .6f);
        light.a = alpha * settings.headGlowStrength * .6f;
        glintGlowCross.color = light;
    }

    // The headshot's bang around the glint, gone in headBurstSeconds: a bloom of warm light behind it, a white ring flashing
    // open fast inside a gold one that opens to headBurstReach, and twelve sparks shooting out, long and white-hot first,
    // then short and gold. The sparks come in three lengths so the burst isn't a perfect wheel.
    private void LayoutBurst(GunFeelSettings settings)
    {
        burstRoot.anchoredPosition = AimPoint();
        float t = Mathf.Clamp01(burstAge / settings.headBurstSeconds);
        float burst = 1f - (1f - t) * (1f - t) * (1f - t), fade = 1f - t;
        float reach = settings.headBurstReach, glintSize = settings.headGlintSize;
        Color color = Color.Lerp(Color.white, settings.headHitColor, Mathf.Clamp01(burstAge / .04f));

        float bloomSize = Mathf.Lerp(glintSize * 1.6f, glintSize * 3.4f, burst);
        burstBloom.rectTransform.sizeDelta = new Vector2(bloomSize, bloomSize);
        Color bloom = Color.Lerp(new Color(1f, .97f, .85f), settings.headHitColor, t);
        bloom.a = .75f * fade * fade;
        burstBloom.color = bloom;

        float ringSize = Mathf.Lerp(glintSize * .6f, reach * 2.2f, burst);
        burstRing.rectTransform.sizeDelta = new Vector2(ringSize, ringSize);
        Color ringColor = color;
        ringColor.a = .95f * fade * fade;
        burstRing.color = ringColor;

        // The flash ring runs at twice the pace: open and gone in the first half.
        float quick = Mathf.Clamp01(t * 2f), quickBurst = 1f - (1f - quick) * (1f - quick);
        float flashSize = Mathf.Lerp(glintSize * .5f, reach * 1.3f, quickBurst);
        burstFlashRing.rectTransform.sizeDelta = new Vector2(flashSize, flashSize);
        burstFlashRing.color = new Color(1f, 1f, 1f, (1f - quick) * (1f - quick));

        float distance = Mathf.Lerp(glintSize * .4f, reach, burst);
        float length = Mathf.Lerp(26f, 4f, t), thickness = Mathf.Lerp(3.4f, 1.2f, t);
        Color spark = Color.Lerp(Color.white, color, Mathf.Clamp01(t * 1.6f));
        spark.a = fade;
        for (int i = 0; i < SparkCount; i++)
        {
            float angle = 15f + 360f / SparkCount * i;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            float own = i % 3 == 0 ? 1f : i % 3 == 1 ? .8f : .65f;
            sparks[i].enabled = true;
            Place(sparks[i], direction * (distance * own + length * own * .5f), new Vector2(length * own, thickness), angle, spark);
        }
    }

    // The headshot's lens flare (the user's pick of the second drafts, 2026-11-10, draft B: "B가 좋은데 너무 화면 가리게끔
    // 하진 말고. 지금 헤드샷 이펙트 크기도 살짝 부족한 거 빼면 적당 … 그 크기를 이번 게 늘려주는 시도"): one thin streak of
    // light across the aim point out to headFlareReach, a shorter one up and down it, and four ghosts strung along a slant
    // as a lens throws them. It widens the hit to several times the burst's size, but in lines a few pixels thick and
    // added onto the picture, so nothing behind it is hidden; open in the first sixth of headFlareSeconds, then thinning out.
    private void LayoutFlare(GunFeelSettings settings)
    {
        flareRoot.anchoredPosition = AimPoint();
        float t = Mathf.Clamp01(flareAge / settings.headFlareSeconds), reach = settings.headFlareReach;
        float open = 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 6f), 3f), fade = (1f - t) * (1f - t);
        // White-hot as it opens, the headshot's gold as it goes.
        Color light = Color.Lerp(Color.white, settings.headHitColor, Mathf.Clamp01(t * 2.5f));
        light.a = fade;
        flareStreak.rectTransform.sizeDelta = new Vector2(reach * 2f * open, Mathf.Lerp(7f, 2.5f, t));
        flareStreak.color = light;
        light.a = fade * .8f;
        flareRise.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(5f, 2f, t), reach * .9f * open);
        flareRise.color = light;
        for (int i = 0; i < Ghosts.Length; i++)
        {
            var rect = ghosts[i].rectTransform;
            // They slide out along the slant as the streak opens, and stay there.
            rect.anchoredPosition = GhostSlant * (Ghosts[i].x * reach * Mathf.Lerp(.7f, 1f, open));
            rect.sizeDelta = Vector2.one * (Ghosts[i].y * Mathf.Lerp(1.3f, 1f, open));
            Color ghost = settings.headHitColor;
            ghost.a = Ghosts[i].z * fade * open;
            ghosts[i].color = ghost;
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

    private static float Smooth(float x) => x * x * (3f - 2f * x);
}
