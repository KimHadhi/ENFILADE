using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The crosshair tab: pick the gun or the melee crosshair, keep the designed one or draw your own, with a live preview
/// that stays in view (hold its corner button to see the real HUD instead). Drawing starts from a preset or a share code
/// (<see cref="CrosshairPresetGallery"/>) and is split into a few short sections, so nothing needs scrolling.
/// Drawing writes <see cref="CrosshairSettings"/>; the HUD follows through <see cref="WeaponCrosshairSelector"/>.
/// </summary>
public sealed class CrosshairSettingsView
{
    private static readonly string[] Kinds = { "총", "근접" };
    private static readonly string[] Modes = { "기본", "직접 꾸미기" };
    private static readonly string[] Sections = { "프리셋", "점·테두리", "선", "색", "회전" };
    private static readonly string[] SquareOrCircle = { "네모", "동그라미" };
    private static readonly string[] BarOrSprite = { "막대", "기본 그림" };
    private static readonly string[] SolidOrRainbow = { "단색", "무지개" };
    private static readonly string[] OffOn = { "끔", "켬" };
    private static readonly string[] FixedOrSpread = { "끔 (항상 같은 모양)", "켬 (점프·무기마다 다름)" };
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
        top.Choice("대상", Kinds, (int)kind, i => kind = (CrosshairKind)i);
        var modeRow = top.Choice("모양", Modes, s.custom ? 1 : 0, i => s.custom = i == 1);
        if (s.custom)
            W.Size(W.ActionButton(modeRow, "처음 모양으로", false, () =>
            {
                PlayerCrosshairs.ResetDrawing(kind);
                GameSettings.Apply();
                Build();
            }), 160f, 34f);
        // Only a gun's lines follow spread. Shown in both modes, since it holds the designed crosshair still too.
        if (kind == CrosshairKind.Gun)
        {
            var settings = GameSettings.Current;
            top.Choice("크로스헤어 벌어짐", FixedOrSpread, settings.steadyCrosshair ? 0 : 1,
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
                rows.Slider("점 크기", 0f, CrosshairSettings.MaxDotSize, s.dotSize, v => s.dotSize = Mathf.RoundToInt(v),
                    v => v < .5f ? "끔" : Pixels(v * 2f), wholeNumbers: true);
                rows.Choice("점 모양", SquareOrCircle, s.roundDot ? 1 : 0, i => s.roundDot = i == 1);
                Slider outlineOpacity = null; // shown only while there is an outline, without rebuilding under the drag
                rows.Slider("테두리", 0f, CrosshairSettings.MaxOutline, s.outline, v =>
                {
                    s.outline = v;
                    outlineOpacity.transform.parent.gameObject.SetActive(v >= .5f);
                }, v => v < .5f ? "끔" : Pixels(v), wholeNumbers: true);
                outlineOpacity = rows.Slider("테두리 불투명도", CrosshairSettings.MinOpacity, 1f, s.outlineOpacity,
                    v => s.outlineOpacity = v, SettingsRows.Percent);
                outlineOpacity.transform.parent.gameObject.SetActive(s.outline >= .5f);
                break;
            case 2:
                rows.Slider("선 개수", 0f, CrosshairSettings.MaxLines, s.lineCount, v => s.lineCount = Mathf.RoundToInt(v),
                    v => v < .5f ? "없음" : Mathf.RoundToInt(v) + "개", wholeNumbers: true);
                rows.Choice("선 모양", BarOrSprite, s.spriteLines ? 1 : 0, i => s.spriteLines = i == 1);
                rows.Slider("길이", 1f, CrosshairSettings.MaxLineLength, s.lineLength, v => s.lineLength = v, Pixels,
                    wholeNumbers: true);
                rows.Slider("두께", 1f, CrosshairSettings.MaxLineThickness, s.lineThickness, v => s.lineThickness = v, Pixels,
                    wholeNumbers: true);
                rows.Slider("간격", 0f, CrosshairSettings.MaxGap, s.gap, v => s.gap = v, Pixels, wholeNumbers: true);
                rows.Slider("기울기", 0f, CrosshairSettings.MaxAngle, s.angle, v => s.angle = Step(v), v => Step(v) + "°");
                break;
            case 3:
                Colour(rows, s);
                break;
            default:
                rows.Choice("회전", OffOn, s.spinning ? 1 : 0, i => { s.spinning = i == 1; BuildSection(); });
                if (s.spinning)
                    rows.Slider("초당 회전", CrosshairSettings.MinSpin, CrosshairSettings.MaxSpin, s.spin, v => s.spin = Step(v),
                        v => Step(v) + "°");
                break;
        }
    }

    private void Changed()
    {
        ShowPreview();
        if (hex != null) hex.SetTextWithoutNotify(Hex(PlayerCrosshairs.Settings(kind).color));
    }

    private void Colour(SettingsRows rows, CrosshairSettings s)
    {
        rows.Choice("방식", SolidOrRainbow, s.rainbow ? 1 : 0, i => { s.rainbow = i == 1; BuildSection(); });
        if (s.rainbow)
        {
            rows.Slider("한 바퀴", CrosshairSettings.MinRainbowCycle, CrosshairSettings.MaxRainbowCycle, 1f / s.rainbowSpeed,
                v => s.rainbowSpeed = 1f / v, v => v.ToString("0.0") + "초");
            rows.Volume("채도", s.rainbowSaturation, v => s.rainbowSaturation = v);
        }
        else
        {
            var swatches = W.Row(rows.Parent, "견본");
            var red = rows.Slider("빨강", 0f, 255f, Byte(s.color.r), v => s.color.r = v / 255f, ByteText, wholeNumbers: true);
            var green = rows.Slider("초록", 0f, 255f, Byte(s.color.g), v => s.color.g = v / 255f, ByteText, wholeNumbers: true);
            var blue = rows.Slider("파랑", 0f, 255f, Byte(s.color.b), v => s.color.b = v / 255f, ByteText, wholeNumbers: true);
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
        rows.Slider("불투명도", CrosshairSettings.MinOpacity, 1f, s.color.a, v => s.color.a = v, SettingsRows.Percent);
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
