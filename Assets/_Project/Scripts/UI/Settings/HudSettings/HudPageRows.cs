using System;
using System.Globalization;
using HudAddons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The rows of one page of the HUD settings (HudSettingsPages): settings rows, and the kinds only the HUD needs: a colour
/// with swatches, the colour board and how strong it is; a place with its two numbers and the way back; a choice with
/// pictures. Every change is applied at once, so it shows on the HUD behind.
/// </summary>
public sealed class HudPageRows
{
    // The addon's own colours, offered beside the colour board.
    private static readonly Color32[] Swatches =
    {
        new Color32(255, 255, 255, 255), new Color32(223, 255, 143, 255), new Color32(255, 233, 85, 255), new Color32(255, 150, 0, 255),
        new Color32(255, 58, 58, 255), new Color32(150, 150, 255, 255),
    };

    private readonly HudSettingsView view;
    public SettingsRows Rows { get; }

    public HudPageRows(HudSettingsView view, Transform parent)
    {
        this.view = view;
        Rows = new SettingsRows(parent, view.Changed);
    }

    public static string T(string id) => Texts.Get("settings.hudedit." + id);

    public static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

    public static string Two(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public static string One(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    public static string Whole(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);

    public void Header(string id) => Rows.Header(T(id));

    public void Note(string id) => Rows.Note(T(id));

    public RectTransform Toggle(string id, bool value, Action<bool> set) => Rows.Toggle(T(id), value, set);

    public Slider Slider(string id, float min, float max, float value, Action<float> set, Func<float, string> format = null, bool whole = false,
        float shown = 1f) => Rows.Slider(T(id), min, max, value, set, format ?? Two, whole, shown);

    /// <summary>A share or a size told in hundredths.</summary>
    public Slider Share(string id, float min, float max, float value, Action<float> set)
        => Rows.Slider(T(id), min, max, value, set, Percent, shownScale: 100f);

    /// <summary>The part's place: its two numbers, and back to where it was.</summary>
    public void Place(HudPlace place)
    {
        if (place == null) return;
        TMP_InputField x = null, y = null;
        var head = W.Row(Rows.Parent, T("place"));
        W.Size(W.Rect("Gap", head), flexibleWidth: 1f);
        W.Size(W.ActionButton(head, T("place.back"), false, () => Put(place.Home)), 110f, 34f);
        var row = W.Rect("Place", Rows.Parent);
        W.Size(row, height: 42f, flexibleHeight: 0f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        W.Size(W.Label(row, T("place.x"), 17f, HudMenu.Muted), width: 40f, height: 34f);
        x = W.NumberBox(row, () => Whole(place.Get().x), () => W.Bare(place.Get().x), v => Put(new Vector2(v, place.Get().y)));
        W.Size(W.Label(row, T("place.y"), 17f, HudMenu.Muted), width: 40f, height: 34f);
        y = W.NumberBox(row, () => Whole(place.Get().y), () => W.Bare(place.Get().y), v => Put(new Vector2(place.Get().x, v)));
        W.Size(x, width: 80f, height: 34f, flexibleWidth: 1f);
        W.Size(y, width: 80f, height: 34f, flexibleWidth: 1f);
        void Show()
        {
            if (x == null || y == null) return;
            if (!x.isFocused) x.SetTextWithoutNotify(Whole(place.Get().x));
            if (!y.isFocused) y.SetTextWithoutNotify(Whole(place.Get().y));
        }
        void Put(Vector2 to)
        {
            place.Set(to);
            GameSettings.Apply();
            Show();
        }
        view.PlaceShown = Show;
        Note("place.note");
    }

    /// <summary>A colour: its swatch opens the colour board, the addon's own colours beside it, how strong it is under it.</summary>
    public void Colour(Func<Color32> get, Action<Color32> set)
    {
        var row = W.Row(Rows.Parent, T("colour"));
        var strip = W.Rect("Colours", row);
        W.Size(strip, flexibleWidth: 1f, height: 30f);
        var layout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var own = W.Rect("Own", strip);
        W.Size(own, 46f, 30f);
        var chip = HudMenu.Box(own, Solid(get()), true, 4f);
        void Put(Color32 colour)
        {
            colour.a = get().a;
            set(colour);
            GameSettings.Apply();
            chip.color = Solid(colour);
        }
        var open = own.gameObject.AddComponent<Button>();
        open.targetGraphic = chip;
        open.onClick.AddListener(() =>
        {
            if (ColourPanel.IsOpenFor(chip)) ColourPanel.Close();
            else ColourPanel.Open(own, Solid(get()), Put, Put, 0f, null, chip);
        });
        foreach (var colour in Swatches)
        {
            var swatch = W.Rect("Swatch", strip);
            W.Size(swatch, 22f, 22f);
            var image = HudMenu.Box(swatch, colour, true, 4f);
            var button = swatch.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Put(colour));
        }
        Rows.Slider(T("opacity"), 0f, 255f, get().a, v =>
        {
            var colour = get();
            colour.a = (byte)Mathf.RoundToInt(v);
            set(colour);
        }, v => Percent(v / 255f), shownScale: 100f / 255f);
    }

    private static Color32 Solid(Color32 colour)
    {
        colour.a = 255;
        return colour;
    }

    /// <summary>A choice shown as pictures (HudPictures), the chosen one framed in white.</summary>
    public void Pictures(string id, int selected, Action<int> set, params (string name, Action<RectTransform> draw)[] options)
    {
        var name = W.Row(Rows.Parent, T(id));
        W.Size(name, height: 34f);
        var row = W.Rect("Pictures", Rows.Parent);
        W.Size(row, height: HudPictures.Tall + 40f, flexibleHeight: 0f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var hovers = new HudHover[options.Length];
        var labels = new TextMeshProUGUI[options.Length];
        void Show(int index)
        {
            for (int i = 0; i < hovers.Length; i++)
            {
                if (i == index) hovers[i].Set(HudMenu.Lit, HudMenu.LitOver);
                else hovers[i].Set(HudMenu.Line, HudMenu.Over);
                labels[i].color = i == index ? HudMenu.Text : HudMenu.Muted;
                labels[i].fontStyle = i == index ? FontStyles.Bold : FontStyles.Normal;
            }
        }
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var tile = W.Rect(options[i].name, row);
            W.Size(tile, HudPictures.Wide + 4f, HudPictures.Tall + 36f);
            W.Fill(tile, Color.clear);
            var frame = KitStyle.TopLeft("Frame", tile, Vector2.zero, new Vector2(HudPictures.Wide + 4f, HudPictures.Tall + 4f));
            hovers[i] = HudHover.On(HudMenu.Box(frame, HudMenu.Line, true), HudMenu.Line, HudMenu.Over);
            var picture = KitStyle.TopLeft("Picture", frame, new Vector2(2f, 2f), new Vector2(HudPictures.Wide, HudPictures.Tall));
            HudMenu.Box(picture, HudMenu.Field, false, 5f);
            options[i].draw(picture);
            labels[i] = W.Label(KitStyle.TopLeft("Name", tile, new Vector2(0f, HudPictures.Tall + 6f), new Vector2(HudPictures.Wide + 4f, 28f)), options[i].name, 17f,
                HudMenu.Muted, TextAlignmentOptions.Center);
            KitStyle.Stretch(labels[i].rectTransform);
            var button = tile.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                Show(index);
                set(index);
                GameSettings.Apply();
                view.Changed();
            });
        }
        Show(selected);
    }

    /// <summary>A button of the page's own under its rows.</summary>
    public void Button(string id, Action pressed)
    {
        var row = W.Rect("Buttons", Rows.Parent);
        W.Size(row, height: 52f, flexibleHeight: 0f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 6, 4);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        W.Size(W.ActionButton(row, T(id), false, pressed), 120f, 40f, 1f);
    }
}

/// <summary>Where a part of the HUD stands, as the settings move it: an offset in the HUD's units, and the one it starts with.</summary>
public sealed class HudPlace
{
    public Func<Vector2> Get;
    public Action<Vector2> Set;
    public Vector2 Home;
}
