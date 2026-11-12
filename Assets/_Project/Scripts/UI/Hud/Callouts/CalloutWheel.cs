using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The callout wheel (PlayerCallouts; memo 4, after Guts &amp; Blackpowder's): eight slices round the middle of the
/// screen while the callout key is held, in the HUD's black, white and pink. The mouse moves a pointer out from the
/// middle (the view holds still meanwhile, as G&amp;B's does: the wheel has the hands, PlayerMovement.CombatInputLocked)
/// and the slice it points into lights pink and reaches out a little. Each slice reads its name and, small under it,
/// the line it would say right now; the middle reads the lit slice's line large, so the player knows what they'll
/// shout before they let go. 말 turns the wheel to a page of short words. Built on first use; real time, so slow motion
/// doesn't slow it.
/// </summary>
[DisallowMultipleComponent]
public sealed class CalloutWheel : MonoBehaviour
{
    private const int Count = 8;
    // At 1080p (px): the slices' inner and outer radius, how much farther out the lit one reaches, the gap between two
    // slices (°), the radius the names sit on, the pointer's reach and the dead middle where nothing is lit.
    private const float Inner = 78f, Outer = 216f, LitGrow = 14f, GapDegrees = 2.4f, LabelRadius = 148f, Reach = 175f, DeadZone = 42f;
    // How long it takes to open (real s), how big it starts, and how far the pointer goes per pixel of mouse.
    private const float OpenSeconds = .12f, OpenScale = .86f, PointerScale = .9f;
    private static readonly Color Slice = new Color(.07f, .07f, .08f, .86f), Middle = new Color(.055f, .055f, .065f, .94f);

    private static CalloutWheel instance;
    private static int closedFrame = -1;
    private static bool swallowClick;

    private readonly WheelSliceGraphic[] slices = new WheelSliceGraphic[Count];
    private readonly TextMeshProUGUI[] names = new TextMeshProUGUI[Count], lines = new TextMeshProUGUI[Count];
    private readonly string[] said = new string[Count];
    private RectTransform canvas, root, pointer;
    private CanvasGroup group;
    private TextMeshProUGUI title, middle, hint;
    private Vector2 aim;
    private int hovered = -1, shownHovered = -2;
    private bool open;
    private float openedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        closedFrame = -1;
        swallowClick = false;
    }

    /// <summary>Whether the wheel is up.</summary>
    public static bool IsOpen => instance != null && instance.open;

    /// <summary>
    /// The wheel has the player's hands: it is up, it closed this frame, or the click that picked from it is still held
    /// down (that click mustn't fire the gun once the wheel lets go).
    /// </summary>
    public static bool HoldsHands
    {
        get
        {
            if (IsOpen || Time.frameCount == closedFrame) return true;
            if (swallowClick && (Mouse.current == null || !Mouse.current.leftButton.isPressed)) swallowClick = false;
            return swallowClick;
        }
    }

    /// <summary>The slice the pointer is in, clockwise from the top; -1 in the middle.</summary>
    public static int Hovered => IsOpen ? instance.hovered : -1;

    /// <summary>Opens the wheel with each slice's name and what it would say now; the hint goes under it.</summary>
    public static void Open(string[] names, string[] lines, string hint, string title)
    {
        if (!Application.isPlaying) return;
        if (instance == null) instance = new GameObject("Callout Wheel").AddComponent<CalloutWheel>();
        instance.Begin(title);
        instance.Fill(names, lines, hint);
    }

    /// <summary>This frame's names and lines (what fits changes as the player looks about).</summary>
    public static void Show(string[] names, string[] lines, string hint)
    {
        if (IsOpen) instance.Fill(names, lines, hint);
    }

    /// <summary>Turns the open wheel to another page (말's words): the pointer back in the middle, a title in it.</summary>
    public static void Page(string[] names, string[] lines, string hint, string title)
    {
        if (!IsOpen) return;
        instance.Begin(title);
        instance.Fill(names, lines, hint);
    }

    /// <summary>Moves the pointer by this frame's mouse movement (screen px).</summary>
    public static void Steer(Vector2 mouseDelta)
    {
        if (IsOpen) instance.Move(mouseDelta);
    }

    /// <summary>Closes it; clicked when a left click picked, so that click is kept from the gun until it is let go.</summary>
    public static void Close(bool clicked)
    {
        if (!IsOpen) return;
        instance.open = false;
        instance.root.gameObject.SetActive(false);
        closedFrame = Time.frameCount;
        if (clicked) swallowClick = true;
    }

    private void Awake()
    {
        var host = new GameObject("Callout wheel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.transform.SetParent(transform, false);
        var overlay = host.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = 90;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas = (RectTransform)host.transform;
        Build();
        root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Build()
    {
        float size = 2f * (Outer + LitGrow) + 40f;
        root = FeelUI.Rect("Wheel", canvas);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.sizeDelta = new Vector2(size, size);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        for (int i = 0; i < Count; i++)
        {
            float centre = i * 360f / Count, half = 180f / Count - GapDegrees * .5f;
            var slice = HudShapes.Make<WheelSliceGraphic>("Slice " + i, root, Slice);
            slice.rectTransform.sizeDelta = new Vector2(size, size);
            slice.Shape(Inner, Outer, centre - half, centre + half);
            slices[i] = slice;
            Vector2 at = Direction(centre) * LabelRadius;
            var box = FeelUI.Rect("Label " + i, root);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f, .5f);
            box.sizeDelta = new Vector2(150f, 70f);
            box.anchoredPosition = at;
            names[i] = KitStyle.Label(Part(box, "Name", 0f, 30f), "", 22f, TextAlignmentOptions.Bottom);
            lines[i] = KitStyle.Flat(Part(box, "Line", 32f, 38f), "", 13f, TextAlignmentOptions.Top, KitStyle.Soft);
            lines[i].textWrappingMode = TextWrappingModes.Normal;
            lines[i].fontStyle = FontStyles.Normal;
        }
        var disk = HudShapes.Make<WheelSliceGraphic>("Middle", root, Middle);
        disk.rectTransform.sizeDelta = new Vector2(size, size);
        disk.Shape(0f, Inner - 8f, 0f, 360f);
        var rim = HudShapes.Make<WheelSliceGraphic>("Rim", root, KitStyle.With(KitStyle.Accent, .9f));
        rim.rectTransform.sizeDelta = new Vector2(size, size);
        rim.Shape(Inner - 8f, Inner - 5f, 0f, 360f);
        var centreBox = FeelUI.Rect("Said", root);
        centreBox.anchorMin = centreBox.anchorMax = centreBox.pivot = new Vector2(.5f, .5f);
        centreBox.sizeDelta = new Vector2(2f * (Inner - 16f), 2f * (Inner - 16f));
        title = KitStyle.Flat(Part(centreBox, "Title", 0f, 20f), "", 13f, TextAlignmentOptions.Top, KitStyle.Accent);
        middle = KitStyle.Label(Part(centreBox, "Line", 18f, 2f * (Inner - 16f) - 18f), "", 17f, TextAlignmentOptions.Center);
        middle.textWrappingMode = TextWrappingModes.Normal;
        var hintBox = FeelUI.Rect("Hint", root);
        hintBox.anchorMin = hintBox.anchorMax = hintBox.pivot = new Vector2(.5f, .5f);
        hintBox.sizeDelta = new Vector2(480f, 24f);
        hintBox.anchoredPosition = new Vector2(0f, -(Outer + LitGrow + 22f));
        hint = KitStyle.Flat(hintBox, "", 14f, TextAlignmentOptions.Center, KitStyle.Soft);
        var dot = HudShapes.Make<DiamondGraphic>("Pointer", root, KitStyle.Accent);
        pointer = dot.rectTransform;
        pointer.sizeDelta = new Vector2(16f, 16f);
    }

    // A strip of the label box, from its top (px).
    private static RectTransform Part(RectTransform box, string name, float top, float height)
    {
        var part = FeelUI.Rect(name, box);
        part.anchorMin = new Vector2(0f, 1f);
        part.anchorMax = new Vector2(1f, 1f);
        part.pivot = new Vector2(.5f, 1f);
        part.sizeDelta = new Vector2(0f, height);
        part.anchoredPosition = new Vector2(0f, -top);
        return part;
    }

    // Up is 0°, clockwise.
    private static Vector2 Direction(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
    }

    private void Begin(string heading)
    {
        open = true;
        openedAt = Time.unscaledTime;
        aim = Vector2.zero;
        hovered = -1;
        shownHovered = -2;
        root.gameObject.SetActive(true);
        title.text = heading ?? "";
    }

    private void Fill(string[] newNames, string[] newLines, string newHint)
    {
        for (int i = 0; i < Count; i++)
        {
            string name = newNames != null && i < newNames.Length ? newNames[i] ?? "" : "";
            string line = newLines != null && i < newLines.Length ? newLines[i] ?? "" : "";
            if (names[i].text != name) names[i].text = name;
            if (lines[i].text != line) lines[i].text = line;
            said[i] = line;
        }
        newHint ??= "";
        if (hint.text != newHint) hint.text = newHint;
        shownHovered = -2;
    }

    private void Move(Vector2 mouseDelta)
    {
        float scale = canvas.rect.height / Mathf.Max(1f, Screen.height);
        aim += mouseDelta * scale * PointerScale;
        if (aim.magnitude > Reach) aim = aim.normalized * Reach;
        if (aim.magnitude < DeadZone)
        {
            hovered = -1;
            return;
        }
        float degrees = Mathf.Repeat(Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg, 360f);
        hovered = Mathf.RoundToInt(degrees / (360f / Count)) % Count;
    }

    private void LateUpdate()
    {
        if (!open) return;
        float opened = Mathf.Clamp01((Time.unscaledTime - openedAt) / OpenSeconds);
        group.alpha = opened;
        root.localScale = Vector3.one * Mathf.Lerp(OpenScale, 1f, KitReveal.EaseOut(opened));
        pointer.anchoredPosition = aim;
        pointer.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 90f);
        if (shownHovered == hovered) return;
        shownHovered = hovered;
        for (int i = 0; i < Count; i++)
        {
            bool lit = i == hovered;
            float centre = i * 360f / Count, half = 180f / Count - GapDegrees * .5f;
            slices[i].color = lit ? KitStyle.Accent : Slice;
            slices[i].Shape(Inner, lit ? Outer + LitGrow : Outer, centre - half, centre + half);
            names[i].color = lit ? KitStyle.Ink : KitStyle.Text;
            lines[i].color = lit ? KitStyle.With(KitStyle.Ink, .85f) : KitStyle.Soft;
        }
        string line = hovered >= 0 ? said[hovered] : Texts.Get("callout.hint.none");
        if (middle.text != line) middle.text = line;
        middle.color = hovered >= 0 ? KitStyle.Text : KitStyle.Soft;
    }
}

/// <summary>
/// A slice of a ring (an annular sector) round the rect's middle: radii in px, from and to in degrees clockwise from the
/// top. Inner 0 and a full turn make a disk. A mesh, so it stays sharp at any size and needs no sprite.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WheelSliceGraphic : MaskableGraphic
{
    private float inner, outer = 100f, from, to = 360f;

    public void Shape(float innerRadius, float outerRadius, float fromDegrees, float toDegrees)
    {
        if (Mathf.Approximately(inner, innerRadius) && Mathf.Approximately(outer, outerRadius) && Mathf.Approximately(from, fromDegrees)
            && Mathf.Approximately(to, toDegrees)) return;
        inner = Mathf.Max(0f, innerRadius);
        outer = Mathf.Max(inner, outerRadius);
        from = fromDegrees;
        to = toDegrees;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float span = to - from;
        if (outer <= inner || span <= 0f) return;
        int steps = Mathf.Max(2, Mathf.CeilToInt(span / 4f));
        Vector2 middle = GetPixelAdjustedRect().center;
        Color32 tint = color;
        for (int i = 0; i < steps; i++)
        {
            float a = (from + span * i / steps) * Mathf.Deg2Rad, b = (from + span * (i + 1) / steps) * Mathf.Deg2Rad;
            Vector2 da = new Vector2(Mathf.Sin(a), Mathf.Cos(a)), db = new Vector2(Mathf.Sin(b), Mathf.Cos(b));
            HudShapes.Quad(mesh, middle + da * inner, middle + da * outer, middle + db * outer, middle + db * inner, tint);
        }
    }
}
