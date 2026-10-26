using UnityEngine;
using UnityEngine.UI;

public enum HitMarkerKind { Body, Head, BodyKill, HeadKill, Blocked }

/// <summary>
/// Crosshair hit confirmation. Four diagonal ticks around the centre:
/// body hit = white; headshot = gold, same size; any kill (body or head) = red with a shock ring;
/// no effect (guard/parry/invulnerable) = thin grey.
/// Back 4 Blood timing, in frames at GunFeel's marker frame rate: the marker holds wide (7), the ticks slide in to the
/// shrunk gap (9), show faded for one frame and vanish; every new hit restarts it wide. It is drawn at the steady aim
/// point projected through the kicked camera, so it jumps with the view punch and settles back with it (runs after CameraViewPunch). Created on first use during Play; colours and sizes in GunFeel.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10010)]
public sealed class HitMarkerHUD : MonoBehaviour
{
    private static HitMarkerHUD instance;
    private readonly Image[] ticks = new Image[4];
    private RectTransform root;
    private RawImage ring;
    private HitMarkerKind kind;
    private float age = 99f, lifetime = .2f;

    public HitMarkerKind LastKind => kind;
    private bool IsShowing => age < lifetime;

    public static void Show(HitMarkerKind kind)
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.hitMarkers) return;
        if (instance == null)
        {
            var host = new GameObject("Hit Markers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<HitMarkerHUD>();
        }
        instance.Trigger(kind);
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
        for (int i = 0; i < 4; i++) ticks[i] = Tick("Tick " + i);
        root.gameObject.SetActive(false);
    }

    private Image Tick(string name)
    {
        var image = FeelUI.Rect(name, root).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        // A soft dark edge keeps white markers readable on bright walls and skies.
        var outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .55f); outline.effectDistance = new Vector2(1f, -1f);
        return image;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Trigger(HitMarkerKind next)
    {
        // A fresh kill marker is not overwritten by the next body hit of the same burst.
        if (IsShowing && IsKill(kind) && !IsKill(next) && age < .18f) return;
        kind = next; age = 0f;
        var settings = GunFeelSettings.Instance;
        int frames = Mathf.Max(1, settings.markerHoldFrames + settings.markerShrinkFrames + settings.markerFadeFrames);
        lifetime = frames / settings.markerFrameRate;
        root.gameObject.SetActive(true);
        Layout(settings);
    }

    private void LateUpdate()
    {
        if (!root.gameObject.activeSelf) return;
        age += Time.unscaledDeltaTime;
        if (age >= lifetime) { root.gameObject.SetActive(false); return; }
        Layout(GunFeelSettings.Instance);
    }

    /// <summary>Screen position of the steady aim as seen through the punched camera; centre when there is no punch.</summary>
    private void Follow()
    {
        var view = Camera.main;
        var punch = view != null ? view.GetComponent<CameraViewPunch>() : null;
        Vector2 position = Vector2.zero;
        if (punch != null && punch.isActiveAndEnabled)
        {
            Vector3 screen = view.WorldToScreenPoint(view.transform.position + punch.SteadyForward * 20f);
            if (screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out Vector2 local))
                position = local;
        }
        root.anchoredPosition = position;
    }

    private void Layout(GunFeelSettings settings)
    {
        bool kill = IsKill(kind), head = kind == HitMarkerKind.Head || kind == HitMarkerKind.HeadKill, blocked = kind == HitMarkerKind.Blocked;
        Follow();
        // Hold wide, slide the ticks in, one faded frame at the shrunk size, gone.
        float frame = age * settings.markerFrameRate;
        int hold = settings.markerHoldFrames, shrinkFrames = settings.markerShrinkFrames;
        float shrink = shrinkFrames > 0 ? Mathf.Clamp01((frame - hold) / shrinkFrames) : frame >= hold ? 1f : 0f;
        float alpha = frame >= hold + shrinkFrames ? settings.markerFadeAlpha : 1f;
        // Body and head hits share one size and differ only in colour.
        float length = settings.markerLength * (kill ? 1.45f : blocked ? .8f : 1f);
        float thickness = settings.markerThickness * (kill ? 1.5f : blocked ? .75f : 1f);
        float gap = Mathf.Lerp(settings.markerGap, settings.markerShrunkGap, Smooth(shrink));
        Color color = kill ? settings.killColor : head ? settings.headHitColor : blocked ? settings.blockedColor : settings.bodyHitColor;
        if (!blocked) color = Color.Lerp(Color.white, color, Mathf.Clamp01(age / .05f)); // white-hot first frame
        color.a *= alpha;

        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Place(ticks[i], direction * (gap + length * .5f), new Vector2(length, thickness), angle, color);        }

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
