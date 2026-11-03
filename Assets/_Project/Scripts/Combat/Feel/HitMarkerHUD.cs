using UnityEngine;
using UnityEngine.UI;

public enum HitMarkerKind { Body, Head, BodyKill, HeadKill, Blocked }

/// <summary>
/// Crosshair hit confirmation. Four diagonal ticks around the centre:
/// body hit = white; headshot = gold, a little bolder, with a four-point glint flashing at the aim point (head kills keep
/// the glint on their red); any kill (body or head) = red with a shock ring; no effect (guard/parry/invulnerable) = thin
/// grey. Body and head markers grow with the share of health the hit took.
/// Back 4 Blood timing, in frames at GunFeel's marker frame rate: the marker holds wide (7), the ticks slide in to the
/// shrunk gap (9), show faded for one frame and vanish; every new hit restarts it wide, a little bigger at first and
/// popping back, so each hit of a burst reads on its own. A posture break adds its own burst on top: an orange ring and
/// four shards flying out (<see cref="ShowBreak"/>). All of it is drawn at the steady aim point projected through the
/// kicked camera, so it jumps with the view punch and settles back with it (runs after CameraViewPunch). Created on first
/// use during Play; colours and sizes in GunFeel. The sounds that go with it: <see cref="HitConfirm"/>.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10010)]
public sealed class HitMarkerHUD : MonoBehaviour
{
    private static HitMarkerHUD instance;
    private readonly Image[] ticks = new Image[4], shards = new Image[4];
    private RectTransform root, glintRoot, breakRoot;
    private RawImage ring, glint, breakRing;
    private HitMarkerKind kind;
    private float age = 99f, lifetime = .2f, scale = 1f, glintAge = 99f, breakAge = 99f;

    public HitMarkerKind LastKind => kind;
    private bool IsShowing => age < lifetime;

    /// <summary>share: the part of the target's health the hit took, which sizes body and head markers.</summary>
    public static void Show(HitMarkerKind kind, float share = 1f)
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.hitMarkers) return;
        Prepare();
        instance.Trigger(kind, share);
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
        root.gameObject.SetActive(false);

        // Its own root, so the glint can stay longer than the ticks.
        glintRoot = FeelUI.Rect("Glint", transform);
        glint = glintRoot.gameObject.AddComponent<RawImage>();
        glint.texture = FeelAssets.Glint; glint.raycastTarget = false;
        var rim = glint.gameObject.AddComponent<Outline>(); // keeps the gold apart from a bright sky or a green crosshair
        rim.effectColor = new Color(0f, 0f, 0f, .5f); rim.effectDistance = new Vector2(1f, -1f);
        glintRoot.gameObject.SetActive(false);

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

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Trigger(HitMarkerKind next, float share)
    {
        // A fresh kill marker is not overwritten by the next body hit of the same burst.
        if (IsShowing && IsKill(kind) && !IsKill(next) && age < .18f) return;
        kind = next; age = 0f;
        var settings = GunFeelSettings.Instance;
        scale = IsKill(next) || next == HitMarkerKind.Blocked ? 1f
            : Mathf.Lerp(settings.markerSmallScale, settings.markerLargeScale, Mathf.Clamp01(share / settings.markerFullDamage));
        int frames = Mathf.Max(1, settings.markerHoldFrames + settings.markerShrinkFrames + settings.markerFadeFrames);
        lifetime = frames / settings.markerFrameRate;
        root.gameObject.SetActive(true);
        Layout(settings);
        if ((next == HitMarkerKind.Head || next == HitMarkerKind.HeadKill) && settings.headGlintSize > 0f)
        {
            glintAge = 0f;
            glintRoot.gameObject.SetActive(true);
            LayoutGlint(settings);
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
        // Hold wide, slide the ticks in, one faded frame at the shrunk size, gone.
        float frame = age * settings.markerFrameRate;
        int hold = settings.markerHoldFrames, shrinkFrames = settings.markerShrinkFrames;
        float shrink = shrinkFrames > 0 ? Mathf.Clamp01((frame - hold) / shrinkFrames) : frame >= hold ? 1f : 0f;
        float alpha = frame >= hold + shrinkFrames ? settings.markerFadeAlpha : 1f;
        // A little bigger at first, popping back, so every hit of a burst reads as its own.
        float pop = 1f + settings.markerPop * (1f - Smooth(Mathf.Clamp01(age / settings.markerPopSeconds)));
        // Body and head hits share one shape; a headshot is gold and a little bolder, and the damage sets the size.
        bool bold = head && !kill;
        float length = settings.markerLength * (kill ? 1.45f : blocked ? .8f : bold ? settings.headLength : 1f) * scale * pop;
        float thickness = settings.markerThickness * (kill ? 1.5f : blocked ? .75f : bold ? settings.headThickness : 1f);
        float gap = Mathf.Lerp(settings.markerGap, settings.markerShrunkGap, Smooth(shrink)) * pop;
        Color color = kill ? settings.killColor : head ? settings.headHitColor : blocked ? settings.blockedColor : settings.bodyHitColor;
        if (!blocked) color = Color.Lerp(Color.white, color, Mathf.Clamp01(age / .05f)); // white-hot first frame
        color.a *= alpha;

        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Place(ticks[i], direction * (gap + length * .5f), new Vector2(length, thickness), angle, color);
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

    // The headshot glint: snaps open past full size, settles, holds for a beat so it registers, then shrinks away. Turning
    // as it goes sweeps the rays off the crosshair's lines, which would otherwise hide them.
    private void LayoutGlint(GunFeelSettings settings)
    {
        glintRoot.anchoredPosition = AimPoint();
        float total = settings.headGlintSeconds, fadeFrom = Mathf.Max(.12f, total * .45f);
        float spark = glintAge < .06f ? glintAge / .06f * 1.25f
            : glintAge < .12f ? Mathf.Lerp(1.25f, 1f, (glintAge - .06f) / .06f)
            : glintAge < fadeFrom ? 1f : Mathf.Clamp01(1f - (glintAge - fadeFrom) / Mathf.Max(.01f, total - fadeFrom));
        glintRoot.sizeDelta = Vector2.one * (settings.headGlintSize * 2f * spark);
        glintRoot.localRotation = Quaternion.Euler(0f, 0f, settings.headGlintTurn * Smooth(Mathf.Clamp01(glintAge / total)));
        Color glow = Color.Lerp(Color.white, settings.headHitColor, Mathf.Clamp01(glintAge / .05f));
        glow.a = Mathf.Clamp01(spark);
        glint.color = glow;
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
