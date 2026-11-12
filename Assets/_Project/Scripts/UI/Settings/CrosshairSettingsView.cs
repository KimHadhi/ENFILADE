using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The crosshair tab: pick the gun or the melee crosshair, keep the designed one or draw your own, with a live preview
/// that stays in view (hold its corner button to see the real HUD instead). Drawing starts from a preset or a share code
/// (<see cref="CrosshairPresetGallery"/>) and is split into a few short sections, so nothing needs scrolling. The last
/// section puts a picture of the player's own at the middle (<see cref="CrosshairImages"/>).
/// Drawing writes <see cref="CrosshairSettings"/>; the HUD follows through <see cref="WeaponCrosshairSelector"/>.
/// </summary>
public sealed class CrosshairSettingsView
{
    // Built when asked rather than held in static fields, so reloaded or translated words show at the next build.
    private static string[] Kinds => new[] { Texts.Get("crosshair.kind.gun"), Texts.Get("crosshair.kind.melee") };
    private static string[] Modes => new[] { Texts.Get("crosshair.mode.design"), Texts.Get("crosshair.mode.custom") };
    private static string[] Sections => new[]
    {
        Texts.Get("crosshair.section.presets"), Texts.Get("crosshair.section.dot"), Texts.Get("crosshair.section.lines"),
        Texts.Get("crosshair.section.color"), Texts.Get("crosshair.section.spin"), Texts.Get("crosshair.section.image"),
    };
    private static string[] OwnOrTint => new[] { Texts.Get("crosshair.image.tint.own"), Texts.Get("crosshair.image.tint.color") };
    private static string[] WithOrOnly => new[] { Texts.Get("crosshair.image.shape.with"), Texts.Get("crosshair.image.shape.only") };
    private static string[] SquareOrCircle => new[] { Texts.Get("crosshair.dot.square"), Texts.Get("crosshair.dot.circle") };
    private static string[] BarOrSprite => new[] { Texts.Get("crosshair.lines.bar"), Texts.Get("crosshair.lines.sprite") };
    private static string[] SolidOrRainbow => new[] { Texts.Get("crosshair.color.solid"), Texts.Get("crosshair.color.rainbow") };
    private static string[] OffOn => new[] { Texts.Get("settings.off"), Texts.Get("settings.on") };
    private static string[] FixedOrSpread => new[] { Texts.Get("crosshair.spread.fixed"), Texts.Get("crosshair.spread.follows") };
    private static readonly Color[] Swatches =
    {
        Color.white, new Color(1f, .2f, .25f), new Color(1f, .55f, .1f), new Color(1f, .92f, .2f), Color.green,
        new Color(.25f, .9f, .9f), new Color(.3f, .55f, 1f), new Color(.7f, .4f, 1f), new Color(1f, .45f, .75f), Color.black,
    };
    private static CrosshairKind kind; // reopening shows the last crosshair and section
    private static int section;
    private readonly RectTransform root;
    private RectTransform sectionRoot;
    private CrosshairPreview preview;
    private TMP_InputField hex;

    public CrosshairSettingsView(RectTransform root)
    {
        this.root = root;
        Build();
    }

    private void Build()
    {
        W.Clear(root);
        var s = PlayerCrosshairs.Settings(kind);
        var top = new SettingsRows(root, Build);
        top.Choice(Texts.Get("crosshair.kind"), Kinds, (int)kind, i => kind = (CrosshairKind)i);
        var modeRow = top.Choice(Texts.Get("crosshair.mode"), Modes, s.custom ? 1 : 0, i => s.custom = i == 1);
        if (s.custom)
            W.Size(W.ActionButton(modeRow, Texts.Get("crosshair.reset"), false, () =>
            {
                PlayerCrosshairs.ResetDrawing(kind);
                GameSettings.Apply();
                Build();
            }), 160f, 34f);
        // Only a gun's lines follow spread. Shown in both modes, since it holds the designed crosshair still too.
        if (kind == CrosshairKind.Gun)
        {
            var settings = GameSettings.Current;
            top.Choice(Texts.Get("crosshair.spread"), FixedOrSpread, settings.steadyCrosshair ? 0 : 1,
                i => settings.steadyCrosshair = i == 0);
        }

        preview = new CrosshairPreview(root, 160f) { Kicks = kind == CrosshairKind.Gun };
        if (!MainMenuScreen.IsOpen && DynamicCrosshairView.FindHud() != null) CrosshairPeek.Add(preview.Box, kind);
        ShowPreview();
        if (!s.custom)
        {
            // The way in: a preset or a code starts drawing, opened on the presets so the pick shows.
            new CrosshairPresetGallery(root, kind, () => { section = 0; Build(); });
            return;
        }

        W.Size(W.Choice(root, Sections, section, i => { section = i; BuildSection(); }), height: 40f, flexibleWidth: 1f,
            flexibleHeight: 0f);
        sectionRoot = W.Rect("Section", root);
        W.Size(sectionRoot, flexibleWidth: 1f, flexibleHeight: 1f);
        var layout = sectionRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        BuildSection();
    }

    private void ShowPreview()
    {
        preview.HoldShape = GameSettings.Current.steadyCrosshair;
        preview.Show(PlayerCrosshairs.For(kind) ?? PlayerCrosshairs.Design(kind));
    }

    // Six rows at most, so a section always fits under the preview.
    private void BuildSection()
    {
        W.Clear(sectionRoot);
        hex = null;
        if (section == 0)
        {
            new CrosshairPresetGallery(sectionRoot, kind, Build);
            return;
        }
        var rows = new SettingsRows(sectionRoot, Changed);
        var s = PlayerCrosshairs.Settings(kind);
        switch (section)
        {
            case 1:
                // Typed in the pixels it shows: the dot is drawn at twice its size.
                rows.Slider(Texts.Get("crosshair.dot.size"), 0f, CrosshairSettings.MaxDotSize, s.dotSize, v => s.dotSize = Mathf.RoundToInt(v),
                    v => v < .5f ? Texts.Get("settings.off") : Pixels(v * 2f), wholeNumbers: true, shownScale: 2f);
                rows.Choice(Texts.Get("crosshair.dot.shape"), SquareOrCircle, s.roundDot ? 1 : 0, i => s.roundDot = i == 1);
                Slider outlineOpacity = null; // shown only while there is an outline, without rebuilding under the drag
                rows.Slider(Texts.Get("crosshair.outline"), 0f, CrosshairSettings.MaxOutline, s.outline, v =>
                {
                    s.outline = v;
                    outlineOpacity.transform.parent.gameObject.SetActive(v >= .5f);
                }, v => v < .5f ? Texts.Get("settings.off") : Pixels(v), wholeNumbers: true);
                outlineOpacity = rows.Slider(Texts.Get("crosshair.outline.opacity"), CrosshairSettings.MinOpacity, 1f, s.outlineOpacity,
                    v => s.outlineOpacity = v, SettingsRows.Percent, shownScale: 100f);
                outlineOpacity.transform.parent.gameObject.SetActive(s.outline >= .5f);
                break;
            case 2:
                rows.Slider(Texts.Get("crosshair.lines.count"), 0f, CrosshairSettings.MaxLines, s.lineCount, v => s.lineCount = Mathf.RoundToInt(v),
                    v => v < .5f ? Texts.Get("crosshair.lines.none") : Texts.Format("crosshair.lines.count.value", Mathf.RoundToInt(v)),
                    wholeNumbers: true);
                rows.Choice(Texts.Get("crosshair.lines.shape"), BarOrSprite, s.spriteLines ? 1 : 0, i => s.spriteLines = i == 1);
                rows.Slider(Texts.Get("crosshair.lines.length"), 1f, CrosshairSettings.MaxLineLength, s.lineLength, v => s.lineLength = v, Pixels,
                    wholeNumbers: true);
                rows.Slider(Texts.Get("crosshair.lines.thickness"), 1f, CrosshairSettings.MaxLineThickness, s.lineThickness, v => s.lineThickness = v,
                    Pixels, wholeNumbers: true);
                rows.Slider(Texts.Get("crosshair.lines.gap"), 0f, CrosshairSettings.MaxGap, s.gap, v => s.gap = v, Pixels, wholeNumbers: true);
                // CS2's T style; the angle went to the turning section to keep this one at six rows.
                rows.Choice(Texts.Get("crosshair.lines.t"), OffOn, s.tStyle ? 1 : 0, i => s.tStyle = i == 1);
                break;
            case 3:
                Colour(rows, s);
                break;
            case 4:
                rows.Slider(Texts.Get("crosshair.lines.angle"), 0f, CrosshairSettings.MaxAngle, s.angle, v => s.angle = Step(v), v => Step(v) + "°");
                rows.Choice(Texts.Get("crosshair.spin"), OffOn, s.spinning ? 1 : 0, i => { s.spinning = i == 1; BuildSection(); });
                if (s.spinning)
                    rows.Slider(Texts.Get("crosshair.spin.speed"), CrosshairSettings.MinSpin, CrosshairSettings.MaxSpin, s.spin, v => s.spin = Step(v),
                        v => Step(v) + "°");
                break;
            default:
                Picture(rows, s);
                break;
        }
    }

    /// <summary>
    /// The player's own picture (user, 2026-11-11: "크로스헤어에 커스텀 이미지 넣을 수 있도록"): one of the PNG and JPG
    /// files in Documents\ENFILADE\Crosshairs, stepped through like resolutions, or one picked anywhere, which is copied
    /// there. Then its size, its colours or the crosshair's, and whether the dot and the lines stay with it. It turns with
    /// the turning section and takes the colour section's opacity.
    /// </summary>
    private void Picture(SettingsRows rows, CrosshairSettings s)
    {
        var names = CrosshairImages.List();
        // One gone from the folder still shows, so it can be stepped off.
        if (!string.IsNullOrEmpty(s.image) && !names.Contains(s.image)) names.Insert(0, s.image);
        var options = new string[names.Count + 1];
        options[0] = Texts.Get("crosshair.image.none");
        for (int i = 0; i < names.Count; i++) options[i + 1] = names[i];
        int selected = string.IsNullOrEmpty(s.image) ? 0 : names.IndexOf(s.image) + 1;
        var row = rows.Stepper(Texts.Get("crosshair.image"), options, selected, i => s.image = i > 0 ? names[i - 1] : "");
        W.Size(W.ActionButton(row, Texts.Get("crosshair.image.pick"), false, () =>
        {
            string path = FilePicker.Open(Texts.Get("crosshair.image.dialog"), Texts.Get("crosshair.image.kind"), "png", "jpg", "jpeg");
            string name = string.IsNullOrEmpty(path) ? null : CrosshairImages.Import(path);
            if (name == null) return; // cancelled, or not a picture
            s.image = name;
            GameSettings.Apply();
            BuildSection();
            ShowPreview();
        }), 128f, 34f);
        W.Size(W.ActionButton(row, Texts.Get("crosshair.image.folder"), false, CrosshairImages.OpenFolder), 112f, 34f);
        rows.Slider(Texts.Get("crosshair.image.size"), CrosshairSettings.MinImageSize, CrosshairSettings.MaxImageSize, s.imageSize,
            v => s.imageSize = Mathf.Round(v), Pixels, wholeNumbers: true);
        rows.Choice(Texts.Get("crosshair.image.tint"), OwnOrTint, s.imageTint ? 1 : 0, i => s.imageTint = i == 1);
        rows.Choice(Texts.Get("crosshair.image.shape"), WithOrOnly, s.imageOnly ? 1 : 0, i => s.imageOnly = i == 1);
        rows.Note(Texts.Get(names.Count == 0 ? "crosshair.image.empty" : "crosshair.image.note"));
    }

    private void Changed()
    {
        ShowPreview();
        if (hex != null) hex.SetTextWithoutNotify(Hex(PlayerCrosshairs.Settings(kind).color));
    }

    private void Colour(SettingsRows rows, CrosshairSettings s)
    {
        rows.Choice(Texts.Get("crosshair.color.mode"), SolidOrRainbow, s.rainbow ? 1 : 0, i => { s.rainbow = i == 1; BuildSection(); });
        if (s.rainbow)
        {
            rows.Slider(Texts.Get("crosshair.color.cycle"), CrosshairSettings.MinRainbowCycle, CrosshairSettings.MaxRainbowCycle, 1f / s.rainbowSpeed,
                v => s.rainbowSpeed = 1f / v, v => Texts.Format("crosshair.color.cycle.value", v.ToString("0.0")));
            rows.Volume(Texts.Get("crosshair.color.saturation"), s.rainbowSaturation, v => s.rainbowSaturation = v);
        }
        else
        {
            var swatches = W.Row(rows.Parent, Texts.Get("crosshair.color.swatches"));
            var red = rows.Slider(Texts.Get("crosshair.color.red"), 0f, 255f, Byte(s.color.r), v => s.color.r = v / 255f, ByteText, wholeNumbers: true);
            var green = rows.Slider(Texts.Get("crosshair.color.green"), 0f, 255f, Byte(s.color.g), v => s.color.g = v / 255f, ByteText,
                wholeNumbers: true);
            var blue = rows.Slider(Texts.Get("crosshair.color.blue"), 0f, 255f, Byte(s.color.b), v => s.color.b = v / 255f, ByteText,
                wholeNumbers: true);
            // A swatch or a typed colour moves the sliders, which write the colour and refresh everything like a drag would.
            void Pick(Color color)
            {
                red.value = Byte(color.r);
                green.value = Byte(color.g);
                blue.value = Byte(color.b);
            }
            Swatch(swatches, Pick);
            hex = HexInput(swatches, () => s.color, Pick);
        }
        rows.Slider(Texts.Get("crosshair.color.opacity"), CrosshairSettings.MinOpacity, 1f, s.color.a, v => s.color.a = v, SettingsRows.Percent,
            shownScale: 100f);
    }

    private static void Swatch(RectTransform row, Action<Color> picked)
    {
        var strip = W.Rect("Swatches", row);
        W.Size(strip, flexibleWidth: 1f);
        var layout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        foreach (var color in Swatches)
        {
            var swatch = W.Rect("Swatch", strip);
            var image = W.Fill(swatch, color);
            var edge = swatch.gameObject.AddComponent<Outline>();
            edge.effectColor = W.Track; edge.effectDistance = new Vector2(1f, -1f);
            var button = swatch.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => picked(color));
        }
    }

    // Valorant-style "#RRGGBB" field at the end of the swatch row. Colour names such as "red" work too.
    private static TMP_InputField HexInput(RectTransform row, Func<Color> current, Action<Color> picked)
    {
        var field = W.TextInput(row, Hex(current()), _ => { });
        W.Size(field, width: 118f, height: 34f, flexibleWidth: 0f);
        field.characterLimit = 9;
        // Plain to see while typed in, as the number boxes are.
        TypingLook.On(field, row.childCount > 0 ? row.GetChild(0).GetComponent<TMP_Text>() : null);
        field.onEndEdit.AddListener(text =>
        {
            if (TryParseColour(text, out var color)) picked(color);
            field.SetTextWithoutNotify(Hex(current()));
        });
        return field;
    }

    private static bool TryParseColour(string text, out Color color)
    {
        text = text.Trim();
        if (!text.StartsWith("#") && ColorUtility.TryParseHtmlString("#" + text, out color)) return true;
        return ColorUtility.TryParseHtmlString(text, out color);
    }

    private static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);
    private static string Pixels(float value) => Mathf.RoundToInt(value) + "px";
    private static float Step(float value) => Mathf.Round(value / 5f) * 5f;
    private static float Byte(float channel) => Mathf.Round(channel * 255f);
    private static string ByteText(float value) => Mathf.RoundToInt(value).ToString();
}
