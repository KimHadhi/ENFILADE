using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The posture break's burst round the aim point: an orange ring bursting out and four shards on the axes flying off,
/// white-hot for a moment, when a hit of the local player's breaks a target's posture (HitConfirm, with its Break sound).
/// It is ours and the only marker left of the old hit marker (HitMarkerHUD until 2026-11-22): every hit and kill marker is
/// the hit feedback addon's (HudAddons.HitFeedbackModule), which has no element for a posture break. Drawn at the steady
/// aim point projected through the kicked camera, so it jumps with the view punch and settles back with it (runs after
/// CameraViewPunch). Numbers in GunFeel: Break Seconds and Break Color, sized by the marker's gap, length and thickness.
/// Made ahead by CombatWarmup, or on first use during Play.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HitMarkers)]
public sealed class PostureBreakHUD : MonoBehaviour
{
    private static PostureBreakHUD instance;
    private readonly Image[] shards = new Image[4];
    private RectTransform root;
    private RawImage ring;
    private float age = 99f;

    /// <summary>The player's hit broke the target's posture.</summary>
    public static void Show()
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.hitMarkers) return;
        Prepare();
        instance.age = 0f;
        instance.root.gameObject.SetActive(true);
        instance.Layout(GunFeelSettings.Instance);
    }

    public static void Prepare()
    {
        if (!Application.isPlaying || instance != null) return;
        var host = new GameObject("Posture Break", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        instance = host.AddComponent<PostureBreakHUD>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupOverlay(gameObject, 26);
        root = FeelUI.Rect("Break", transform);
        ring = FeelUI.Rect("Ring", root).gameObject.AddComponent<RawImage>();
        ring.texture = FeelAssets.Ring; ring.raycastTarget = false;
        for (int i = 0; i < 4; i++) shards[i] = Shard("Shard " + i, root);
        root.gameObject.SetActive(false);
    }

    private static Image Shard(string name, Transform parent)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        // A soft dark edge keeps the white-hot shards readable on bright walls and skies.
        var outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .55f); outline.effectDistance = new Vector2(1f, -1f);
        return image;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void LateUpdate()
    {
        if (!root.gameObject.activeSelf) return;
        var settings = GunFeelSettings.Instance;
        age += Time.unscaledDeltaTime;
        if (age >= settings.breakSeconds) root.gameObject.SetActive(false);
        else Layout(settings);
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

    // The ring bursting out and the four shards flying off the axes, white-hot for the first moment, fading as they go.
    private void Layout(GunFeelSettings settings)
    {
        root.anchoredPosition = AimPoint();
        float t = Mathf.Clamp01(age / settings.breakSeconds);
        float burst = 1f - (1f - t) * (1f - t) * (1f - t), fade = 1f - t;
        Color color = Color.Lerp(Color.white, settings.breakColor, Mathf.Clamp01(age / .05f));
        float reach = settings.markerGap + settings.markerLength;
        float ringSize = Mathf.Lerp(settings.markerShrunkGap * 2f, reach * 2.6f, burst);
        ring.rectTransform.sizeDelta = new Vector2(ringSize, ringSize);
        Color ringColor = color;
        ringColor.a = .9f * fade * fade;
        ring.color = ringColor;

        float distance = Mathf.Lerp(settings.markerGap * .8f, reach * 1.15f, burst);
        float length = settings.markerLength * .7f * (1f - .4f * t), thickness = settings.markerThickness * 1.2f;
        Color shardColor = color;
        shardColor.a = fade;
        for (int i = 0; i < 4; i++)
        {
            float angle = 90f * i;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var rect = shards[i].rectTransform;
            rect.anchoredPosition = direction * (distance + length * .5f);
            rect.sizeDelta = new Vector2(length, thickness);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            shards[i].color = shardColor;
        }
    }
}
