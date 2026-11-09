using UnityEngine;
using UnityEngine.UI;

public enum HitMarkerKind { Body, Head, BodyKill, HeadKill, Blocked }

/// <summary>
/// Crosshair hit confirmation. Four diagonal ticks around the centre:
/// body hit = white; headshot = gold, a little bolder and quicker, with a glowing eight-point glint, a bloom, a white and a gold ring and twelve sparks
/// banging out at the aim point and gone within .2 s (head kills keep
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
    // Between a tick's outer end and its pierced tail (canvas units).
    private const float TailGap = 1.5f;
    private static HitMarkerHUD instance;
    // A headshot's sparks, set between the ticks and the glint's rays so all three read at once.
    private const int SparkCount = 12;
    private readonly Image[] ticks = new Image[4], tails = new Image[4], tailEnds = new Image[4], shards = new Image[4],
        sparks = new Image[SparkCount];
    private RectTransform root, glintRoot, breakRoot, burstRoot;
    private RawImage ring, glintGlow, glintGlowCross, glint, glintCross, glintCore, breakRing, burstRing, burstFlashRing, burstBloom;
    private HitMarkerKind kind;
    private bool pierced;
    private float age = 99f, lifetime = .2f, scale = 1f, glintAge = 99f, breakAge = 99f, burstAge = 99f, pace = 1f;

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
        root = FeelUI.Rect("Marker", transform);
        ring = FeelUI.Rect("Ring", root).gameObject.AddComponent<RawImage>();
        ring.texture = FeelAssets.Ring; ring.raycastTarget = false;
        for (int i = 0; i < 4; i++) ticks[i] = Tick("Tick " + i, root);
        for (int i = 0; i < 4; i++) { tails[i] = Tail("Pierced Tail " + i, root); tailEnds[i] = Tail("Pierced Tail End " + i, root); }
        root.gameObject.SetActive(false);

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

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Trigger(HitMarkerKind next, float share, bool throughSomething)
    {
        // A fresh kill marker is not overwritten by the next body hit of the same burst.
        if (IsShowing && IsKill(kind) && !IsKill(next) && age < .18f) return;
        kind = next; age = 0f;
        pierced = throughSomething;
        var settings = GunFeelSettings.Instance;
        scale = IsKill(next) || next == HitMarkerKind.Blocked ? 1f
            : Mathf.Lerp(settings.markerSmallScale, settings.markerLargeScale, Mathf.Clamp01(share / settings.markerFullDamage));
        // A headshot's ticks run through their frames faster, so the bang is short; kills keep their own time.
        pace = next == HitMarkerKind.Head ? Mathf.Max(1f, settings.headMarkerPace) : 1f;
        int frames = Mathf.Max(1, settings.markerHoldFrames + settings.markerShrinkFrames + settings.markerFadeFrames);
        lifetime = frames / settings.markerFrameRate / pace;
        root.gameObject.SetActive(true);
        Layout(settings);
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
    }

    private void LateUpdate()
    {
        var settings = GunFeelSettings.Instance;
        if (root.gameObject.activeSelf)
        {
            age += Time.unscaledDeltaTime;
            if (age >= lifetime) root.gameObject.SetActive(false);
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

    private static void Place(Image image, Vector2 position, Vector2 size, float angle, Color color)
    {
        var rect = image.rectTransform;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        image.color = color;
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);
}
