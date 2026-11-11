using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What a callout looks like (PlayerCallouts): a teammate's words in a short black plate with a pink edge over their
/// head for a few seconds (held at the screen's edge with their name when they are off it, so the words still say who
/// and which way); the player's own words small under the crosshair, so they see what they shouted; and the pin
/// 이쪽으로! leaves on the spot looked at, a pink diamond with the caller's name and how far it is, one per player.
/// Teammates' plates can be turned off (Settings → 조작 → 동료 콜아웃 말풍선): their voice and pins stay. One plate per
/// speaker, a new line taking the old one's place. Built on first use; real time.
/// </summary>
[DisallowMultipleComponent]
public sealed class CalloutBubbles : MonoBehaviour
{
    // How far over the head a plate floats (m); the margin kept from the screen's edges and the own line's place under
    // the middle (px at 1080p); lives and fades (real s); the words' size, the plate's padding and widest.
    private const float OverHead = .62f, EdgeMargin = 60f, OwnBelow = 150f, Life = 2.6f, OwnLife = 1.8f, InSeconds = .12f,
        OutSeconds = .3f, PinLife = 7f, PinFade = .4f, TextSize = 19f, OwnTextSize = 16f, Pad = 14f, MostWidth = 380f,
        PlateHeight = 34f, PinSize = 20f;

    private sealed class Bubble
    {
        public PlayerMovement Speaker;
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Name;
        public float Since, Life;
        public bool Own;
    }

    private sealed class Pin
    {
        public PlayerMovement By;
        public Vector3 Point;
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Distance;
        public float Since;
    }

    private static CalloutBubbles instance;
    private readonly List<Bubble> bubbles = new List<Bubble>();
    private readonly List<Pin> pins = new List<Pin>();
    private RectTransform canvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Speaker's words: over their head, or under the crosshair when they are this PC's player.</summary>
    public static void Say(PlayerMovement speaker, string words)
    {
        if (!Application.isPlaying || speaker == null || string.IsNullOrEmpty(words)) return;
        bool own = speaker.IsLocal;
        if (!own && !GameSettings.Current.calloutBubbles) return;
        Ensure().Add(speaker, words, own);
    }

    /// <summary>A pin from <paramref name="by"/> on this spot; their earlier pin goes.</summary>
    public static void Pin(PlayerMovement by, Vector3 point)
    {
        if (!Application.isPlaying || by == null) return;
        Ensure().AddPin(by, point);
    }

    private static CalloutBubbles Ensure()
    {
        if (instance == null) instance = new GameObject("Callout Bubbles").AddComponent<CalloutBubbles>();
        return instance;
    }

    private void Awake()
    {
        var host = new GameObject("Callout bubbles", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.transform.SetParent(transform, false);
        var overlay = host.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = 73;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas = (RectTransform)host.transform;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Add(PlayerMovement speaker, string words, bool own)
    {
        for (int i = bubbles.Count - 1; i >= 0; i--)
        {
            if (bubbles[i].Speaker != speaker) continue;
            if (bubbles[i].Root != null) Destroy(bubbles[i].Root.gameObject);
            bubbles.RemoveAt(i);
        }
        var bubble = new Bubble { Speaker = speaker, Since = Time.unscaledTime, Life = own ? OwnLife : Life, Own = own };
        bubble.Root = FeelUI.Rect(own ? "Own callout" : "Callout", canvas);
        bubble.Root.anchorMin = bubble.Root.anchorMax = own ? new Vector2(.5f, .5f) : Vector2.zero;
        bubble.Root.pivot = new Vector2(.5f, own ? 1f : 0f);
        bubble.Group = bubble.Root.gameObject.AddComponent<CanvasGroup>();
        bubble.Group.interactable = bubble.Group.blocksRaycasts = false;
        KitStyle.Cut(KitStyle.Stretch("Plate", bubble.Root), own ? KitStyle.With(KitStyle.Panel, .7f) : KitStyle.Panel);
        var edge = KitStyle.Fill(FeelUI.Rect("Edge", bubble.Root), KitStyle.Accent).rectTransform;
        edge.anchorMin = Vector2.zero;
        edge.anchorMax = new Vector2(0f, 1f);
        edge.pivot = new Vector2(0f, .5f);
        edge.sizeDelta = new Vector2(4f, 0f);
        var label = KitStyle.Label(KitStyle.Stretch("Words", bubble.Root), words, own ? OwnTextSize : TextSize, TextAlignmentOptions.Center,
            own ? KitStyle.Ice : KitStyle.Text);
        float width = Mathf.Min(MostWidth, label.GetPreferredValues(words).x + 2f * Pad);
        bubble.Root.sizeDelta = new Vector2(width, own ? PlateHeight - 6f : PlateHeight);
        if (!own)
        {
            // Their name, shown only while the plate is held at the screen's edge (over their head their tag says it).
            var nameBox = FeelUI.Rect("Name", bubble.Root);
            nameBox.anchorMin = new Vector2(0f, 1f);
            nameBox.anchorMax = Vector2.one;
            nameBox.pivot = new Vector2(.5f, 0f);
            nameBox.sizeDelta = new Vector2(0f, 18f);
            nameBox.anchoredPosition = new Vector2(0f, 2f);
            bool named = speaker.TryGetComponent(out PlayerTally tally);
            bubble.Name = KitStyle.Label(nameBox, named ? tally.DisplayName : speaker.name, 13f, TextAlignmentOptions.Bottom,
                named ? tally.Color : KitStyle.Soft);
            bubble.Name.gameObject.SetActive(false);
        }
        bubbles.Add(bubble);
    }

    private void AddPin(PlayerMovement by, Vector3 point)
    {
        for (int i = pins.Count - 1; i >= 0; i--)
        {
            if (pins[i].By != by) continue;
            if (pins[i].Root != null) Destroy(pins[i].Root.gameObject);
            pins.RemoveAt(i);
        }
        var pin = new Pin { By = by, Point = point, Since = Time.unscaledTime };
        pin.Root = FeelUI.Rect("Callout pin", canvas);
        pin.Root.anchorMin = pin.Root.anchorMax = Vector2.zero;
        pin.Root.pivot = new Vector2(.5f, 0f);
        pin.Root.sizeDelta = new Vector2(160f, 64f);
        pin.Group = pin.Root.gameObject.AddComponent<CanvasGroup>();
        pin.Group.interactable = pin.Group.blocksRaycasts = false;
        var diamond = HudShapes.Make<DiamondGraphic>("Diamond", pin.Root, KitStyle.Accent).rectTransform;
        diamond.anchorMin = diamond.anchorMax = new Vector2(.5f, 0f);
        diamond.sizeDelta = Vector2.one * PinSize;
        diamond.anchoredPosition = new Vector2(0f, PinSize * .5f);
        var labelBox = FeelUI.Rect("Label", pin.Root);
        labelBox.anchorMin = labelBox.anchorMax = new Vector2(.5f, 0f);
        labelBox.pivot = new Vector2(.5f, 0f);
        labelBox.sizeDelta = new Vector2(160f, 20f);
        labelBox.anchoredPosition = new Vector2(0f, PinSize + 4f);
        pin.Distance = KitStyle.Label(labelBox, "", 14f, TextAlignmentOptions.Center, KitStyle.Ice);
        var nameBox = FeelUI.Rect("Name", pin.Root);
        nameBox.anchorMin = nameBox.anchorMax = new Vector2(.5f, 0f);
        nameBox.pivot = new Vector2(.5f, 0f);
        nameBox.sizeDelta = new Vector2(160f, 16f);
        nameBox.anchoredPosition = new Vector2(0f, PinSize + 24f);
        bool named = by.TryGetComponent(out PlayerTally tally);
        KitStyle.Label(nameBox, named ? tally.DisplayName : by.name, 12f, TextAlignmentOptions.Center, named ? tally.Color : KitStyle.Soft);
        pins.Add(pin);
    }

    private void LateUpdate()
    {
        var view = Camera.main;
        float now = Time.unscaledTime;
        for (int i = bubbles.Count - 1; i >= 0; i--)
        {
            var bubble = bubbles[i];
            float age = now - bubble.Since;
            if (bubble.Root == null || bubble.Speaker == null || age >= bubble.Life || (!bubble.Own && bubble.Speaker.IsDead))
            {
                if (bubble.Root != null) Destroy(bubble.Root.gameObject);
                bubbles.RemoveAt(i);
                continue;
            }
            float shown = Mathf.Clamp01(age / InSeconds) * Mathf.Clamp01((bubble.Life - age) / OutSeconds);
            bubble.Group.alpha = shown;
            bubble.Root.localScale = Vector3.one * Mathf.Lerp(1.2f, 1f, KitReveal.EaseOut(Mathf.Clamp01(age / InSeconds)));
            if (bubble.Own)
            {
                bubble.Root.anchoredPosition = new Vector2(0f, -OwnBelow);
                continue;
            }
            float height = bubble.Speaker.TryGetComponent(out CharacterController body) ? body.height : 1.8f;
            bool edge = Place(bubble.Root, view, bubble.Speaker.transform.position + Vector3.up * (height + OverHead), 40f);
            if (bubble.Name != null && bubble.Name.gameObject.activeSelf != edge) bubble.Name.gameObject.SetActive(edge);
        }
        var local = PlayerRoster.Local;
        for (int i = pins.Count - 1; i >= 0; i--)
        {
            var pin = pins[i];
            float age = now - pin.Since;
            if (pin.Root == null || age >= PinLife)
            {
                if (pin.Root != null) Destroy(pin.Root.gameObject);
                pins.RemoveAt(i);
                continue;
            }
            pin.Group.alpha = Mathf.Clamp01((PinLife - age) / PinFade);
            pin.Root.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, KitReveal.EaseOut(Mathf.Clamp01(age / .18f)));
            Place(pin.Root, view, pin.Point, 0f);
            float metres = local != null ? Vector3.Distance(local.transform.position, pin.Point) : 0f;
            string text = Texts.Get("callout.pin") + "  " + Mathf.RoundToInt(metres) + "m";
            if (pin.Distance.text != text) pin.Distance.text = text;
        }
    }

    // Puts the rect over this world point, kept inside the screen's edges (behind the view, at the bottom edge on the
    // side the point is). Returns whether it had to be held at an edge.
    private bool Place(RectTransform rect, Camera view, Vector3 world, float topRoom)
    {
        if (view == null)
        {
            rect.gameObject.SetActive(false);
            return false;
        }
        if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);
        Vector3 screen = view.WorldToScreenPoint(world);
        bool behind = screen.z < 0f;
        if (behind)
        {
            screen.x = Screen.width - screen.x;
            screen.y = 0f;
        }
        float scale = canvas.rect.width / Mathf.Max(1f, Screen.width);
        Vector2 at = new Vector2(screen.x * scale, screen.y * scale);
        Vector2 held = new Vector2(Mathf.Clamp(at.x, EdgeMargin, canvas.rect.width - EdgeMargin),
            Mathf.Clamp(at.y, EdgeMargin, canvas.rect.height - EdgeMargin - topRoom));
        rect.anchoredPosition = held;
        return behind || (held - at).sqrMagnitude > 1f;
    }
}
