using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The chosen map at the title screen's top right, tilted: its picture in a cut-corner white frame, panning slowly; a
/// small dark chip in the picture's corner with arrows to pick another map ("◀ 1장 · 1 / 5 ▶"); and under it, over the
/// picture's bottom edge, the map's name with its chapter number big in pink. Picking another map flashes the frame and
/// decodes the new name and number. A map with no picture yet shows a map icon instead.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuMapCard : MonoBehaviour
{
    public const float Width = 560f, PictureHeight = 315f, Height = PictureHeight + 76f;
    private const float Border = 3f, Tilt = 2f, PanSpeed = .22f, PanReach = .05f, Zoom = .9f;

    private string[] maps = Array.Empty<string>();
    private Action<int> pick;
    private RawImage picture;
    private RawImage emptyIcon;
    private TMP_Text chip, title, number;
    private bool shownOnce;

    public RectTransform Root { get; private set; }
    /// <summary>The frame's flash, for the screen's opening (KitReveal.Card) and for picking.</summary>
    public Image Lid { get; private set; }

    /// <param name="pick">Called with -1 or +1 when an arrow is pressed.</param>
    public static MainMenuMapCard Make(RectTransform parent, float edge, float top, string[] maps, Action<int> pick)
    {
        var root = FeelUI.Rect("Map Card", parent);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        root.anchoredPosition = new Vector2(-edge, -top);
        root.sizeDelta = new Vector2(Width, Height);
        root.localRotation = Quaternion.Euler(0f, 0f, Tilt);
        var card = root.gameObject.AddComponent<MainMenuMapCard>();
        card.Root = root;
        card.maps = maps ?? Array.Empty<string>();
        card.pick = pick;
        card.Build();
        return card;
    }

    private void Build()
    {
        var frame = KitStyle.TopLeft("Frame", Root, Vector2.zero, new Vector2(Width, PictureHeight));
        KitStyle.Cut(frame, KitStyle.Ice);
        var window = KitStyle.Stretch("Window", frame);
        window.sizeDelta = new Vector2(-Border * 2f, -Border * 2f);
        KitStyle.Cut(window, KitStyle.Card);
        window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        picture = KitStyle.Stretch("Picture", window).gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        emptyIcon = KitButtons.IconImage(window, "Map", 72f, KitStyle.Faint);

        // The picker chip in the picture's top-left corner.
        var chipBox = KitStyle.TopLeft("Picker", frame, new Vector2(16f, 14f), new Vector2(230f, 38f));
        KitStyle.Fill(chipBox, KitStyle.With(KitStyle.Night, .82f));
        chip = KitStyle.Flat(KitStyle.TopLeft("Label", chipBox, new Vector2(38f, 0f), new Vector2(154f, 38f)), "", 15f,
            TextAlignmentOptions.Center);
        if (maps.Length > 1)
        {
            Arrow(chipBox, new Vector2(3f, 3f), "Left", -1);
            Arrow(chipBox, new Vector2(195f, 3f), "Right", 1);
        }

        Lid = KitButtons.Lid(frame);

        // The name and its number, over the picture's bottom edge, right-aligned.
        number = KitStyle.Flat(KitStyle.TopLeft("Number", Root, new Vector2(Width - 200f, PictureHeight - 58f), new Vector2(200f, 130f)), "",
            118f, TextAlignmentOptions.MidlineRight, KitStyle.Accent);
        number.characterSpacing = -4f;
        title = KitStyle.Flat(KitStyle.TopLeft("Title", Root, new Vector2(0f, PictureHeight + 4f), new Vector2(Width - 206f, 64f)), "", 46f,
            TextAlignmentOptions.MidlineRight);
    }

    private void Arrow(RectTransform parent, Vector2 at, string icon, int step)
    {
        var box = KitStyle.TopLeft("Arrow " + icon, parent, at, new Vector2(32f, 32f));
        var back = KitStyle.Fill(box, KitStyle.With(KitStyle.Ice, .1f));
        back.raycastTarget = true;
        KitButtons.IconImage(box, icon, 14f, KitStyle.Text);
        var button = box.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(() => pick?.Invoke(step));
    }

    /// <summary>Shows the map at <paramref name="index"/>; its name and number arrive after <paramref name="delay"/>.</summary>
    public void Show(int index, float delay)
    {
        if (shownOnce) KitPulse.Play(Lid, KitStyle.With(KitStyle.Accent, .35f), .45f);
        shownOnce = true;
        if (maps.Length == 0 || index < 0 || index >= maps.Length)
        {
            picture.enabled = false;
            emptyIcon.enabled = emptyIcon.texture != null;
            chip.text = Texts.Get("menu.map.none");
            KitScramble.On(title).Decode(Texts.Get("menu.map.empty"), delay, .4f);
            KitScramble.On(number).Decode("--", delay, .3f);
            return;
        }
        string map = maps[index];
        Split(MapTable.DisplayName(map), out string chapterNumber, out string name, out string label);
        var texture = MapTable.Picture(map);
        picture.texture = texture;
        picture.enabled = texture != null;
        emptyIcon.enabled = texture == null && emptyIcon.texture != null;
        chip.text = label + "  ·  " + (index + 1) + " / " + maps.Length;
        KitScramble.On(number).Decode(chapterNumber, delay, .35f);
        KitScramble.On(title).Decode(name, delay + .05f, .45f);
    }

    // "1장 · 옥상 탈출" → "01", "옥상 탈출" and "1장". A name with no chapter in front reads "EX", the whole name and "자유 맵".
    private static void Split(string display, out string chapterNumber, out string name, out string label)
    {
        chapterNumber = "EX";
        label = Texts.Get("menu.map.free");
        name = display ?? "";
        int dot = name.IndexOf('·');
        if (dot < 0) return;
        string head = name.Substring(0, dot).Trim();
        string digits = new string(head.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return;
        chapterNumber = int.Parse(digits).ToString("00");
        label = head;
        name = name.Substring(dot + 1).Trim();
    }

    private void Update()
    {
        // The picture drifts slowly from side to side, slightly zoomed in so its edges never show.
        if (picture == null || !picture.enabled) return;
        float drift = Mathf.Sin(Time.unscaledTime * PanSpeed) * PanReach;
        float margin = (1f - Zoom) * .5f;
        picture.uvRect = new Rect(margin + drift, margin, Zoom, Zoom);
    }
}
