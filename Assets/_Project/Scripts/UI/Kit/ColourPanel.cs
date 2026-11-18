using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The colour board: the one way a colour is made in a menu, whatever the colour is for (the user, 2026-11-18: "색상
/// 코드로 하는 사람이 몇이나 되겠습니까? 물론 색상 코드나 RGB 값을 조정하는 기능은 있어야 하지만 일단 색상 설정 패널로
/// 했으면 좋겠습니다 그 원하는 색깔 쪽으로 마우스 움직여서 색 결정하는 유명한 거"). It opens under whatever asked for it
/// (above, where there is no room below) and holds:
/// - the square: how strong the colour is across, how bright upward, in the hue chosen. Press or drag on it and the colour
///   is the one under the pointer;
/// - the hue strip beside it, red at the bottom round to red at the top;
/// - what the colour looks like: words written in it on the HUD's dark (so it can be judged as a name), and a chip of it;
/// - its code, #RRGGBB, to read or type, and R, G and B each as a bar to drag and a number to type.
/// Whoever opens it is told the colour as it moves (changing) and once it has settled (chosen: the pointer let go, a code
/// or a number typed, the board closed in the middle of a drag), so a setting is kept once, not at every pixel.
/// A caller whose colour must read on something dark hands the least it may weigh (ColourWeight; a player's colour on
/// the HUD's near-black plates, PlayerColours.Dim). The board then holds only colours that weigh as much: its square
/// runs, for each strength, from the dimmest brightness allowed up to the brightest, and across to the strongest the hue
/// allows, so every place on it is a colour that may be used and nothing is refused or greyed out; a code typed darker
/// than that is shown as the nearest colour allowed.
/// It closes on its 닫기, on a press anywhere off it and off what opened it, when what opened it goes away, and when
/// another board opens: there is one at a time.
/// Every length is in Tables/Hud/ColourPanel.csv. The pads are plain rectangles, so they take the pointer exactly where
/// they are drawn (KitPad), and the plate takes it inside its cut corners (KitHitShape). Nothing in it moves by itself
/// or blinks but the caret of the box being typed in.
/// </summary>
[DisallowMultipleComponent]
public sealed class ColourPanel : MonoBehaviour
{
    public const string TableName = "ColourPanel";

    private static readonly string[] Letters = { "R", "G", "B" };
    private static readonly Vector3[] Corners = new Vector3[4];
    private static ColourPanel open;

    private RectTransform anchor, panel, canvasRect;
    private Canvas canvas;
    private object whose;
    private Action<Color32> changing, chosen;
    // The least the colour may weigh (ColourWeight), 0 for any colour.
    private float least;

    // The hue, and the place on the square (0 to 1 across and up). The square is the hue's allowed colours stretched
    // over it: across is the strength, up to the strongest the hue allows; up is the brightness, from the dimmest
    // allowed at that strength. The place is what is kept, so a hue dragged round leaves the square's mark where it is.
    private float hue, across, up;
    private Color32 colour;
    // A pad is held: the colour is still moving, and is told as chosen when the pointer lets go.
    private bool moving;

    private Texture2D boardPicture, huePicture;
    private readonly Texture2D[] channelPictures = new Texture2D[3];
    private Color32[] boardTexels, channelTexels;
    private float boardDrawnHue = -1f;
    private RectTransform boardMark, hueMark;
    private Image boardMarkInside, chip;
    private readonly RectTransform[] channelMarks = new RectTransform[3], channelBars = new RectTransform[3];
    private readonly TMP_InputField[] channelBoxes = new TMP_InputField[3];
    private TMP_InputField codeBox;
    private TMP_Text sample;
    private float boardSize, hueWidth, rise, edge;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        open = null;
        escapeFrame = -1;
    }

    private static float N(string row, string key, float fallback) => GameTables.Number(TableName, row, key, fallback);

    // ---- Opening and closing ----

    /// <summary>
    /// Opens the board under <paramref name="anchor"/> on <paramref name="colour"/>, closing any board already open.
    /// <paramref name="changing"/> hears the colour at every move and <paramref name="chosen"/> once it has settled.
    /// <paramref name="least"/>: the least the colour may weigh, for a colour that must read on something dark (0
    /// takes every colour). <paramref name="words"/>: what is written in the colour beside the chip (a name), or nothing.
    /// <paramref name="whose"/>: anything that says what it was opened for, asked back by IsOpenFor. Null when the anchor
    /// is on no canvas.
    /// </summary>
    public static ColourPanel Open(RectTransform anchor, Color32 colour, Action<Color32> changing, Action<Color32> chosen,
        float least = 0f, string words = null, object whose = null)
    {
        Close();
        var canvas = anchor != null ? anchor.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return null;
        // On the anchor's own canvas, last, so it is drawn over everything there and taken by the same raycaster, and
        // outside whatever masks the anchor (a scrolling list's viewport).
        var host = W.Rect("Colour Panel", canvas.transform);
        var made = host.gameObject.AddComponent<ColourPanel>();
        made.anchor = anchor;
        made.canvas = canvas;
        made.canvasRect = (RectTransform)canvas.transform;
        made.whose = whose;
        made.changing = changing;
        made.chosen = chosen;
        made.least = Mathf.Clamp01(least);
        made.Build(host, words);
        made.Take(colour);
        made.Show();
        made.Place();
        open = made;
        return made;
    }

    /// <summary>A board is open for this (the <c>whose</c> it was opened with).</summary>
    public static bool IsOpenFor(object whose) => open != null && whose != null && ReferenceEquals(open.whose, whose);

    /// <summary>Closes the board, if one is open. A colour still being dragged is told as chosen first.</summary>
    public static void Close()
    {
        if (open == null) return;
        var was = open;
        open = null;
        was.Settle();
        Destroy(was.gameObject);
    }

    // The frame an open board took Esc on, for every menu that reads the key that frame to hear the same answer.
    private static int escapeFrame = -1;

    /// <summary>
    /// Esc was pressed: an open board takes it and closes, and the window under it stays (as the key window does for the
    /// Esc menu). True then, and to whoever else asks in the same frame, since each menu reads the key for itself.
    /// </summary>
    public static bool TakeEscape()
    {
        if (open != null)
        {
            Close();
            escapeFrame = Time.frameCount;
        }
        return Time.frameCount == escapeFrame;
    }

    private void OnDestroy()
    {
        if (open == this) open = null;
        if (boardPicture != null) Destroy(boardPicture);
        if (huePicture != null) Destroy(huePicture);
        foreach (var picture in channelPictures) if (picture != null) Destroy(picture);
    }

    private void Update()
    {
        if (open != this) return;
        // What opened it went away (its window closed, its tab was built again): the board goes with it.
        if (anchor == null || !anchor.gameObject.activeInHierarchy) { Close(); return; }
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        // A press off the board and off what opened it puts the board away. The press itself still goes where it landed.
        Vector2 at = mouse.position.ReadValue();
        var root = canvas.rootCanvas;
        var eye = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        if (!RectTransformUtility.RectangleContainsScreenPoint(panel, at, eye) && !RectTransformUtility.RectangleContainsScreenPoint(anchor, at, eye))
            Close();
    }

    // After the layout and any scrolling of the frame: the board stays by what opened it.
    private void LateUpdate()
    {
        if (open == this && anchor != null) Place();
    }

    // Under the anchor's left end; above it where the canvas has no room below; kept inside the canvas.
    private void Place()
    {
        anchor.GetWorldCorners(Corners); // bottom left, top left, top right, bottom right
        Vector2 below = canvasRect.InverseTransformPoint(Corners[0]), above = canvasRect.InverseTransformPoint(Corners[1]);
        var room = canvasRect.rect;
        var size = panel.sizeDelta;
        float top = below.y - rise;
        if (top - size.y < room.yMin + edge && above.y + rise + size.y <= room.yMax - edge) top = above.y + rise + size.y;
        float left = Mathf.Clamp(below.x, room.xMin + edge, Mathf.Max(room.xMin + edge, room.xMax - edge - size.x));
        panel.localPosition = new Vector3(left, top, 0f);
    }

    // ---- Building ----

    private void Build(RectTransform host, string words)
    {
        float pad = N("Panel", "pad", 14f), gap = N("Panel", "gap", 12f), side = N("Side", "width", 204f), sideGap = N("Side", "gap", 6f);
        float textSize = N("Text", "size", 18f), mark = N("Hue", "mark", 6f);
        boardSize = N("Board", "size", 208f);
        hueWidth = N("Hue", "width", 22f);
        rise = N("Panel", "rise", 8f);
        edge = N("Panel", "edge", 12f);

        panel = host;
        host.anchorMin = host.anchorMax = new Vector2(.5f, .5f);
        host.pivot = new Vector2(0f, 1f);
        host.localRotation = Quaternion.identity;
        host.localScale = Vector3.one;
        host.sizeDelta = new Vector2(pad + boardSize + gap + hueWidth + gap + side + pad, pad + boardSize + pad);
        W.Panel(host);
        // The plate takes the pointer where it is drawn (its cut corners left out, KitHitShape), so a press on the board
        // between its pieces never reaches the row or the window under it.
        if (host.TryGetComponent(out Image plate)) plate.raycastTarget = true;

        // The square: strength across, brightness upward.
        int boardSteps = Mathf.Clamp(Mathf.RoundToInt(N("Board", "texels", 64f)), 8, 256);
        boardPicture = Picture("Colour Board", boardSteps, boardSteps);
        boardTexels = new Color32[boardSteps * boardSteps];
        var board = KitStyle.TopLeft("Board", host, new Vector2(pad, pad), new Vector2(boardSize, boardSize));
        Draw(board, boardPicture);
        var boardPad = board.gameObject.AddComponent<KitPad>();
        boardPad.Moved += at => { moving = true; Ask(hue, at.x, at.y); };
        boardPad.Released += Settle;
        float cursor = N("Board", "cursor", 14f);
        boardMark = Mark(board, new Vector2(cursor, cursor), out boardMarkInside);

        // The hues: red at the bottom, round to red at the top.
        int hueSteps = Mathf.Clamp(Mathf.RoundToInt(N("Hue", "texels", 128f)), 8, 512);
        huePicture = Picture("Colour Hues", 1, hueSteps);
        var hues = new Color32[hueSteps];
        for (int i = 0; i < hueSteps; i++) hues[i] = Solid(Color.HSVToRGB(i / (hueSteps - 1f), 1f, 1f));
        huePicture.SetPixels32(hues);
        huePicture.Apply(false);
        var strip = KitStyle.TopLeft("Hue", host, new Vector2(pad + boardSize + gap, pad), new Vector2(hueWidth, boardSize));
        Draw(strip, huePicture);
        var huePad = strip.gameObject.AddComponent<KitPad>();
        huePad.Moved += at => { moving = true; Ask(at.y, across, up); };
        huePad.Released += Settle;
        hueMark = Mark(strip, new Vector2(hueWidth + 6f, mark), out _);

        // The side: how it looks, its code, its three channels, the way out.
        float left = pad + boardSize + gap + hueWidth + gap, down = pad;
        float previewHeight = N("Preview", "height", 40f), chipWidth = N("Preview", "chip", 56f);
        var preview = KitStyle.TopLeft("Preview", host, new Vector2(left, down), new Vector2(side, previewHeight));
        KitStyle.Fill(preview, KitStyle.Night);
        var chipRect = W.Rect("Chip", preview);
        if (string.IsNullOrEmpty(words)) KitStyle.Stretch(chipRect);
        else
        {
            chipRect.anchorMin = new Vector2(1f, 0f);
            chipRect.anchorMax = Vector2.one;
            chipRect.pivot = new Vector2(1f, .5f);
            chipRect.anchoredPosition = Vector2.zero;
            chipRect.sizeDelta = new Vector2(chipWidth, 0f);
            // A name is written as it is: its own tags are letters here, not looks.
            var written = W.Label(preview, words, textSize + 2f, Color.white);
            written.richText = false;
            written.fontStyle = FontStyles.Bold;
            written.overflowMode = TextOverflowModes.Ellipsis;
            KitStyle.Stretch(written.rectTransform);
            written.rectTransform.offsetMin = new Vector2(10f, 0f);
            written.rectTransform.offsetMax = new Vector2(-(chipWidth + 8f), 0f);
            sample = written;
        }
        chip = KitStyle.Fill(chipRect, Color.white);
        down += previewHeight + sideGap;

        float rowHeight = N("Row", "height", 30f), letterWidth = N("Channel", "label", 22f);
        var codeRow = KitStyle.TopLeft("Code", host, new Vector2(left, down), new Vector2(side, rowHeight));
        var hash = Letter(codeRow, "#", letterWidth, textSize);
        codeBox = W.TextInput(codeRow, "", _ => { });
        var codeRect = (RectTransform)codeBox.transform;
        KitStyle.Stretch(codeRect);
        codeRect.offsetMin = new Vector2(letterWidth, 0f);
        codeBox.characterLimit = 7;
        codeBox.onFocusSelectAll = true;
        TypingLook.On(codeBox, hash);
        // Six hex digits is a colour; anything else leaves the colour as it was and the box showing it again.
        codeBox.onEndEdit.AddListener(typed =>
        {
            if (ColourCode.TryRead(typed, out Color32 read) && !Same(read, colour))
            {
                Take(read);
                Tell(true);
            }
            else Show();
        });
        down += rowHeight + sideGap;

        float channelHeight = N("Channel", "height", 28f), bar = N("Channel", "bar", 16f), box = N("Channel", "box", 58f);
        int channelSteps = Mathf.Clamp(Mathf.RoundToInt(N("Channel", "texels", 64f)), 2, 256);
        channelTexels = new Color32[channelSteps];
        for (int c = 0; c < 3; c++)
        {
            int channel = c;
            var row = KitStyle.TopLeft(Letters[c], host, new Vector2(left, down), new Vector2(side, channelHeight));
            // The letter first: the number box takes the row's first child for its name and turns it pink while typed in.
            Letter(row, Letters[c], letterWidth, textSize);
            channelPictures[c] = Picture("Colour " + Letters[c], channelSteps, 1);
            var barRect = W.Rect("Bar", row);
            barRect.anchorMin = new Vector2(0f, .5f);
            barRect.anchorMax = new Vector2(1f, .5f);
            barRect.offsetMin = new Vector2(letterWidth, -bar * .5f);
            barRect.offsetMax = new Vector2(-(box + 8f), bar * .5f);
            Draw(barRect, channelPictures[c]);
            var barPad = barRect.gameObject.AddComponent<KitPad>();
            barPad.Moved += at => { moving = true; SetChannel(channel, Mathf.RoundToInt(at.x * 255f), false); };
            barPad.Released += Settle;
            channelBars[c] = barRect;
            channelMarks[c] = Mark(barRect, new Vector2(mark, bar + 6f), out _);
            var field = W.NumberBox(row, () => Level(channel), () => Level(channel), typed => SetChannel(channel, Mathf.RoundToInt(typed), true));
            var fieldRect = (RectTransform)field.transform;
            fieldRect.anchorMin = new Vector2(1f, 0f);
            fieldRect.anchorMax = Vector2.one;
            fieldRect.pivot = new Vector2(1f, .5f);
            fieldRect.offsetMin = new Vector2(-box, 1f);
            fieldRect.offsetMax = new Vector2(0f, -1f);
            field.characterLimit = 3;
            channelBoxes[c] = field;
            down += channelHeight + sideGap * .5f;
        }

        float closeWidth = Mathf.Min(side, N("Side", "close", 96f));
        var closeBox = KitStyle.TopLeft("Close", host, new Vector2(left + side - closeWidth, pad + boardSize - rowHeight), new Vector2(closeWidth, rowHeight));
        var close = W.ActionButton(closeBox, Texts.Get("common.close"), false, Close);
        KitStyle.Stretch((RectTransform)close.transform);
    }

    // A picture drawn here, texel by texel in the colours shown (never blended by the screen's own colour space).
    private static Texture2D Picture(string name, int width, int height) => new Texture2D(width, height, TextureFormat.RGBA32, false)
    {
        name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
    };

    // The picture over the whole rect, taking the pointer. From edge to edge it runs from its first texel's middle to its
    // last one's, so the rect's very edge shows the first and the last value and a place on the rect is that value.
    private static void Draw(RectTransform rect, Texture2D picture)
    {
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = picture;
        image.uvRect = new Rect(.5f / picture.width, .5f / picture.height, 1f - 1f / picture.width, 1f - 1f / picture.height);
    }

    // What says where on a pad the colour is: white inside a dark edge, seen on any colour, placed from the pad's bottom
    // left. inside is its middle, for the square's mark to wear the colour itself.
    private static RectTransform Mark(RectTransform pad, Vector2 size, out Image inside)
    {
        var mark = W.Rect("Mark", pad);
        mark.anchorMin = mark.anchorMax = Vector2.zero;
        mark.sizeDelta = size + new Vector2(2f, 2f);
        KitStyle.Fill(mark, KitStyle.Ink);
        var white = W.Rect("White", mark);
        KitStyle.Stretch(white);
        white.offsetMin = new Vector2(1f, 1f);
        white.offsetMax = new Vector2(-1f, -1f);
        KitStyle.Fill(white, KitStyle.Text);
        var middle = W.Rect("Inside", mark);
        KitStyle.Stretch(middle);
        float rim = Mathf.Min(3f, Mathf.Min(size.x, size.y) * .5f);
        middle.offsetMin = new Vector2(rim, rim);
        middle.offsetMax = new Vector2(-rim, -rim);
        inside = KitStyle.Fill(middle, KitStyle.Text);
        return mark;
    }

    private static TextMeshProUGUI Letter(RectTransform row, string letter, float width, float size)
    {
        var label = W.Label(row, letter, size, W.Muted, TextAlignmentOptions.Midline);
        label.richText = false; // "#" and a letter are themselves
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, 0f);
        return label;
    }

    // ---- The colour ----

    private static Color32 Solid(Color32 colour)
    {
        colour.a = 255;
        return colour;
    }

    private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

    private int Channel(int channel) => channel == 0 ? colour.r : channel == 1 ? colour.g : colour.b;

    private string Level(int channel) => Channel(channel).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The colour at a place on the square (<paramref name="across"/> and <paramref name="up"/>, 0 to 1) in a hue, for a
    /// least weight: across is the strength, up to the strongest the hue allows; up is the brightness, from the dimmest
    /// allowed at that strength to the brightest. Every place is a colour that weighs enough. The square is painted by
    /// this and the pointer is read by this, so the colour under the pointer is the colour given (PlayerColourCheck
    /// tries it over the whole square without a screen).
    /// </summary>
    public static Color32 At(float hue, float across, float up, float least)
    {
        float strength = Mathf.Clamp01(across) * ColourWeight.Strongest(hue, least);
        return ColourWeight.Colour(hue, strength, Mathf.Lerp(ColourWeight.Dimmest(hue, strength, least), 1f, Mathf.Clamp01(up)));
    }

    /// <summary>
    /// Where on the square a colour lies, At turned round: its hue, and its place across and up. A grey has no hue and
    /// black no strength, so <paramref name="hue"/> and <paramref name="across"/> come in holding what the board showed
    /// and keep it then, and the marks do not jump to red and to the left edge.
    /// </summary>
    public static void Place(Color32 colour, float least, ref float hue, ref float across, out float up)
    {
        Color.RGBToHSV(colour, out float h, out float strength, out float bright);
        if (strength > 0f && bright > 0f) hue = h;
        float strongest = ColourWeight.Strongest(hue, least);
        if (bright > 0f) across = strongest > 0f ? Mathf.Clamp01(strength / strongest) : 0f;
        float dimmest = ColourWeight.Dimmest(hue, across * strongest, least);
        up = dimmest < 1f ? Mathf.Clamp01((bright - dimmest) / (1f - dimmest)) : 1f;
    }

    // A hue and a place on the square asked for by the pointer: the colour is the one there.
    private void Ask(float askedHue, float x, float y)
    {
        hue = Mathf.Clamp01(askedHue);
        across = Mathf.Clamp01(x);
        up = Mathf.Clamp01(y);
        colour = At(hue, across, up, least);
        Tell(false);
    }

    // A colour given whole (the one it opens on, a code typed, a channel set): the nearest allowed to it, and its place.
    private void Take(Color32 given)
    {
        colour = ColourWeight.AtLeast(Solid(given), least);
        Place(colour, least, ref hue, ref across, out up);
    }

    private void SetChannel(int channel, int level, bool settled)
    {
        byte to = (byte)Mathf.Clamp(level, 0, 255);
        Color32 next = colour;
        if (channel == 0) next.r = to; else if (channel == 1) next.g = to; else next.b = to;
        Take(next);
        Tell(settled);
    }

    // Shows the colour, and tells whoever opened the board: at every move, and as chosen once it is where it stays.
    private void Tell(bool settled)
    {
        Show();
        if (!settled) { changing?.Invoke(colour); return; }
        moving = false;
        chosen?.Invoke(colour);
    }

    // The pointer let go of a pad (or the board closes while one is held): the colour it left is the one chosen.
    private void Settle()
    {
        if (!moving) return;
        moving = false;
        chosen?.Invoke(colour);
    }

    // ---- Showing it ----

    private void Show()
    {
        if (!Mathf.Approximately(boardDrawnHue, hue)) PaintBoard();
        boardMark.anchoredPosition = new Vector2(across * boardSize, up * boardSize);
        boardMarkInside.color = colour;
        hueMark.anchoredPosition = new Vector2(hueWidth * .5f, hue * boardSize);
        for (int c = 0; c < 3; c++)
        {
            PaintChannel(c);
            var bar = channelBars[c].rect;
            channelMarks[c].anchoredPosition = new Vector2(Channel(c) / 255f * bar.width, bar.height * .5f);
            if (!channelBoxes[c].isFocused) channelBoxes[c].SetTextWithoutNotify(Level(c));
        }
        if (!codeBox.isFocused) codeBox.SetTextWithoutNotify(ColourCode.Of(colour));
        chip.color = colour;
        if (sample != null) sample.color = colour;
    }

    // The square in the hue shown: every colour of that hue that may be used and no other, each where At says it is. A
    // column is one strength, from the dimmest brightness allowed there at the bottom to the brightest at the top.
    private void PaintBoard()
    {
        boardDrawnHue = hue;
        int steps = boardPicture.width;
        float strongest = ColourWeight.Strongest(hue, least);
        for (int x = 0; x < steps; x++)
        {
            float strength = x / (steps - 1f) * strongest, dimmest = ColourWeight.Dimmest(hue, strength, least);
            for (int y = 0; y < steps; y++)
                boardTexels[y * steps + x] = ColourWeight.Colour(hue, strength, Mathf.Lerp(dimmest, 1f, y / (steps - 1f)));
        }
        boardPicture.SetPixels32(boardTexels);
        boardPicture.Apply(false);
    }

    // A channel's bar: the colour shown with that channel alone run from none to full.
    private void PaintChannel(int channel)
    {
        int steps = channelTexels.Length;
        for (int i = 0; i < steps; i++)
        {
            byte level = (byte)Mathf.RoundToInt(i / (steps - 1f) * 255f);
            Color32 here = colour;
            if (channel == 0) here.r = level; else if (channel == 1) here.g = level; else here.b = level;
            channelTexels[i] = here;
        }
        channelPictures[channel].SetPixels32(channelTexels);
        channelPictures[channel].Apply(false);
    }
}
