using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// What stands behind the character on the title screen, drawn on a canvas the menu camera sees far behind the
/// character (so the character stands in front of it): shafts of light from above, a glow, the game's name huge and
/// faint drifting sideways, the horizon with its distance ticks, and a rangefinder round the character (a ticked pink
/// ring and a dashed white ring turning slowly against each other, with the bearing the character faces). Kept quiet
/// on purpose (the user's "too mobile" note, 2026-11-09): no dust, no pool of light or ping at the feet, faint rings.
/// Every layer shifts a little against the mouse, the nearer ones more, so the screen has depth.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuBackdrop : MonoBehaviour
{
    // How far behind the character the canvas stands (m), and how far each layer shifts at the screen's edge (px).
    private const float Behind = 6f, FarShift = 10f, MidShift = 18f, NearShift = 34f;
    private const float WordSpeed = 22f, WordGap = 260f;

    private MainMenuPreview preview;
    private Canvas canvas;
    private RectTransform root, far, mid, near, glow, ring, dashes, marks, horizon, words, bearingBox;
    private RawImage ringImage;
    private readonly RectTransform[] beams = new RectTransform[3];
    private readonly RawImage[] beamImages = new RawImage[3];
    private readonly TMP_Text[] word = new TMP_Text[2];
    private readonly RectTransform[] wordBoxes = new RectTransform[2];
    private TMP_Text bearing;
    private Vector2 pointer;
    private float wordX, wordWidth;
    private int shownFacing = -1;

    public static MainMenuBackdrop Make(Transform parent, MainMenuPreview preview)
    {
        var host = new GameObject("Backdrop", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.transform.SetParent(parent, false);
        var backdrop = host.AddComponent<MainMenuBackdrop>();
        backdrop.preview = preview;
        backdrop.Build();
        return backdrop;
    }

    private void Build()
    {
        root = (RectTransform)transform;
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = preview.View;
        canvas.planeDistance = 20f;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        far = Layer("Far");
        mid = Layer("Mid");
        near = Layer("Near");

        for (int i = 0; i < beams.Length; i++)
        {
            beams[i] = FeelUI.Rect("Beam", far);
            beams[i].sizeDelta = new Vector2(i == 1 ? 340f : 220f, 1400f);
            beamImages[i] = Raw(beams[i], KitShapes.Beam, KitStyle.With(KitStyle.Ice, .07f));
        }
        glow = FeelUI.Rect("Glow", far);
        glow.sizeDelta = new Vector2(1500f, 1500f);
        Raw(glow, KitShapes.Glow, KitStyle.With(KitStyle.Ice, .13f));

        // The name, twice end to end, so it can drift forever.
        words = FeelUI.Rect("Name", far);
        for (int i = 0; i < word.Length; i++)
        {
            var box = FeelUI.Rect("Word", words);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, .5f);
            box.sizeDelta = new Vector2(10f, 400f);
            wordBoxes[i] = box;
            word[i] = KitStyle.Flat(box, "ENFILADE", 380f, TextAlignmentOptions.MidlineLeft, KitStyle.With(KitStyle.Ice, .045f));
            word[i].characterSpacing = 30f;
        }

        horizon = FeelUI.Rect("Horizon", mid);
        horizon.sizeDelta = new Vector2(2600f, 1f);
        KitStyle.Fill(horizon, KitStyle.With(KitStyle.Ice, .2f));
        for (int i = -20; i <= 20; i++)
        {
            var tick = FeelUI.Rect("Tick", horizon);
            tick.anchorMin = tick.anchorMax = new Vector2(.5f, .5f);
            tick.pivot = new Vector2(.5f, 1f);
            bool major = i % 5 == 0;
            tick.anchoredPosition = new Vector2(i * 64f, 0f);
            tick.sizeDelta = new Vector2(1f, major ? 14f : 6f);
            KitStyle.Fill(tick, KitStyle.With(KitStyle.Ice, major ? .3f : .16f));
        }

        ring = FeelUI.Rect("Ring", mid);
        ringImage = Raw(ring, KitShapes.Ring, KitStyle.With(KitStyle.Accent, .26f));
        dashes = FeelUI.Rect("Dashes", mid);
        Raw(dashes, KitShapes.Dashes, KitStyle.With(KitStyle.Ice, .14f));
        // Four short marks out from the ring at its top, bottom and sides, which stay put while it turns.
        marks = FeelUI.Rect("Marks", mid);
        for (int i = 0; i < 4; i++)
        {
            var mark = FeelUI.Rect("Mark", marks);
            float angle = i * 90f;
            mark.anchorMin = mark.anchorMax = new Vector2(.5f + .5f * Mathf.Sin(angle * Mathf.Deg2Rad), .5f + .5f * Mathf.Cos(angle * Mathf.Deg2Rad));
            mark.localRotation = Quaternion.Euler(0f, 0f, -angle);
            mark.sizeDelta = new Vector2(2f, 26f);
            mark.pivot = new Vector2(.5f, 0f);
            KitStyle.Fill(mark, KitStyle.With(KitStyle.Accent, .45f));
        }
        bearingBox = FeelUI.Rect("Bearing", mid);
        bearingBox.pivot = new Vector2(0f, .5f);
        bearingBox.sizeDelta = new Vector2(220f, 44f);
        bearing = KitStyle.Flat(bearingBox, "", 15f, TextAlignmentOptions.MidlineLeft, KitStyle.With(KitStyle.Ice, .75f));
        bearing.characterSpacing = 2f;
    }

    private RectTransform Layer(string name) => KitStyle.Stretch(name, root);

    private static RawImage Raw(RectTransform rect, Texture texture, Color color)
    {
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void LateUpdate()
    {
        // A script reload during Play forgets what Build made; nothing to move then.
        if (preview == null || preview.View == null || canvas == null || ring == null) return;
        float dt = Time.unscaledDeltaTime, time = Time.unscaledTime;
        if (canvas.worldCamera != preview.View) canvas.worldCamera = preview.View;
        if (preview.HasModel) canvas.planeDistance = preview.Distance + Behind;

        // Where the character stands on this canvas, and how tall it is there.
        Vector2 feet = new Vector2(0f, -380f), head = new Vector2(0f, 420f);
        if (preview.HasModel)
        {
            feet = OnCanvas(preview.Feet);
            head = OnCanvas(preview.Head);
        }
        float tall = Mathf.Max(100f, head.y - feet.y);
        Vector2 middle = new Vector2(feet.x, feet.y + tall * .5f);

        // The mouse from -1 to 1 across the screen, eased, moves the layers against it.
        var mouse = Mouse.current;
        Vector2 aim = Vector2.zero;
        if (mouse != null && Screen.width > 0 && Screen.height > 0)
        {
            var at = mouse.position.ReadValue();
            aim = new Vector2(Mathf.Clamp(at.x / Screen.width * 2f - 1f, -1f, 1f), Mathf.Clamp(at.y / Screen.height * 2f - 1f, -1f, 1f));
        }
        pointer = Vector2.Lerp(pointer, aim, 1f - Mathf.Exp(-4f * dt));
        far.anchoredPosition = -pointer * FarShift;
        mid.anchoredPosition = -pointer * MidShift;
        near.anchoredPosition = -pointer * NearShift;

        for (int i = 0; i < beams.Length; i++)
        {
            float side = (i - 1) * 430f;
            beams[i].anchoredPosition = new Vector2(middle.x + side + Mathf.Sin(time * .17f + i * 2.1f) * 40f, 120f);
            beams[i].localRotation = Quaternion.Euler(0f, 0f, (i - 1) * -9f + Mathf.Sin(time * .13f + i) * 2f);
            beamImages[i].color = KitStyle.With(KitStyle.Ice, .05f + .03f * Mathf.Sin(time * .6f + i * 1.7f));
        }
        glow.anchoredPosition = middle + new Vector2(0f, tall * .1f);

        if (wordWidth <= 0f && word[0] != null) wordWidth = word[0].preferredWidth;
        if (wordWidth > 0f)
        {
            float span = wordWidth + WordGap;
            wordX = Mathf.Repeat(wordX - WordSpeed * dt, span);
            words.anchoredPosition = new Vector2(0f, middle.y + tall * .08f);
            for (int i = 0; i < word.Length; i++)
                wordBoxes[i].anchoredPosition = new Vector2(-1100f + wordX - span + i * span, 0f);
        }

        horizon.anchoredPosition = new Vector2(middle.x, feet.y);

        float across = tall * 1.04f;
        ring.anchoredPosition = dashes.anchoredPosition = marks.anchoredPosition = middle;
        ring.sizeDelta = marks.sizeDelta = new Vector2(across, across);
        dashes.sizeDelta = new Vector2(across * .86f, across * .86f);
        ring.localRotation = Quaternion.Euler(0f, 0f, time * 4f);
        dashes.localRotation = Quaternion.Euler(0f, 0f, -time * 7f);
        ringImage.color = KitStyle.With(KitStyle.Accent, .13f + .03f * Mathf.Sin(time * 1.3f));

        float radius = across * .5f;
        bearingBox.anchoredPosition = middle + new Vector2(radius * .74f + 16f, radius * .74f);
        int facing = Mathf.RoundToInt(preview.Facing) % 360;
        if (facing != shownFacing)
        {
            shownFacing = facing;
            bearing.text = facing.ToString("000") + "°";
        }
    }

    private Vector2 OnCanvas(Vector3 world)
    {
        var view = preview.View;
        Vector3 screen = view.WorldToScreenPoint(world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, view, out var local);
        return local;
    }
}
