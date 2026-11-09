using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The thin bar along the bottom of the title screen, as FPS lobbies have it: key hints at the left ("Esc 설정",
/// "끌기 캐릭터 돌리기"), then a blinking receiving light and radio calls, alerts and tips from Tables/MenuTicker running
/// right to left without end (their edges fading out), and the real local time at the right. It rises in from below
/// when the screen opens. The radio calls follow the campaign's story, so the menu already tells it.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuTicker : MonoBehaviour
{
    public const string TableName = "MenuTicker";
    public const float Height = 44f;
    private const float Speed = 70f, Gap = 120f, RiseSeconds = .35f, HintSize = 14f;

    private RectTransform band;
    private readonly RectTransform[] copies = new RectTransform[2];
    private TMP_Text first, clock;
    private RawImage signal;
    private float x, width, openAt = float.PositiveInfinity;
    private int shownSecond = -1;

    /// <param name="hints">Each hint's key and what it does, left to right.</param>
    public static MainMenuTicker Make(RectTransform canvas, float edge, IList<(string key, string does)> hints)
    {
        var band = FeelUI.Rect("Bottom Bar", canvas);
        band.anchorMin = Vector2.zero;
        band.anchorMax = new Vector2(1f, 0f);
        band.pivot = new Vector2(.5f, 0f);
        band.sizeDelta = new Vector2(0f, Height);
        KitStyle.Fill(band, KitStyle.With(KitStyle.Night, .88f));
        var over = FeelUI.Rect("Line", band);
        over.anchorMin = new Vector2(0f, 1f);
        over.anchorMax = Vector2.one;
        over.pivot = new Vector2(.5f, 1f);
        over.sizeDelta = new Vector2(0f, 1f);
        KitStyle.Fill(over, KitStyle.With(KitStyle.Accent, .3f));

        var ticker = band.gameObject.AddComponent<MainMenuTicker>();
        ticker.band = band;
        ticker.Build(edge, hints);
        return ticker;
    }

    private void Build(float edge, IList<(string key, string does)> hints)
    {
        // The hints: each key in a small box, then what it does.
        float at = edge;
        foreach (var (key, does) in hints)
        {
            var keyBox = KitStyle.TopLeft("Key " + key, band, new Vector2(at, (Height - 24f) * .5f), new Vector2(10f, 24f));
            KitStyle.Fill(keyBox, KitStyle.With(KitStyle.Ice, .12f));
            var keyLabel = KitStyle.Flat(keyBox, key, HintSize - 1f, TextAlignmentOptions.Center);
            float keyWidth = keyLabel.GetPreferredValues(key).x + 14f;
            keyBox.sizeDelta = new Vector2(keyWidth, 24f);
            at += keyWidth + 8f;
            var doesBox = KitStyle.TopLeft("Does " + key, band, new Vector2(at, 0f), new Vector2(10f, Height));
            var doesLabel = KitStyle.Flat(doesBox, does, HintSize, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
            doesLabel.fontStyle = FontStyles.Normal;
            float doesWidth = doesLabel.GetPreferredValues(does).x + 4f;
            doesBox.sizeDelta = new Vector2(doesWidth, Height);
            at += doesWidth + 26f;
        }

        var clockBox = FeelUI.Rect("Clock", band);
        clockBox.anchorMin = clockBox.anchorMax = clockBox.pivot = new Vector2(1f, .5f);
        clockBox.anchoredPosition = new Vector2(-edge, 0f);
        clockBox.sizeDelta = new Vector2(120f, Height);
        clock = KitStyle.Flat(clockBox, "", 15f, TextAlignmentOptions.MidlineRight, KitStyle.Ice);
        clock.characterSpacing = 3f;

        // The receiving light, then the running lines inside a window whose ends fade.
        var lightBox = KitStyle.TopLeft("Light", band, new Vector2(at + 10f, (Height - 8f) * .5f), new Vector2(8f, 8f));
        signal = lightBox.gameObject.AddComponent<RawImage>();
        signal.texture = KitShapes.Dot;
        signal.color = KitStyle.Accent;
        signal.raycastTarget = false;
        var window = FeelUI.Rect("Window", band);
        window.anchorMin = Vector2.zero;
        window.anchorMax = Vector2.one;
        window.offsetMin = new Vector2(at + 26f, 0f);
        window.offsetMax = new Vector2(-edge - 150f, 0f);
        window.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(60, 0);
        string text = Lines();
        for (int i = 0; i < copies.Length; i++)
        {
            copies[i] = FeelUI.Rect("Run", window);
            copies[i].anchorMin = copies[i].anchorMax = copies[i].pivot = new Vector2(0f, .5f);
            copies[i].sizeDelta = new Vector2(10f, Height);
            var label = KitStyle.Flat(copies[i], text, HintSize, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
            label.fontStyle = FontStyles.Normal;
            if (i == 0) first = label;
        }
        x = 30f;
    }

    // Every row of the table as "꼬리표  말", one after another, with a faint slash between.
    private static string Lines()
    {
        string accent = "#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent), ice = "#" + ColorUtility.ToHtmlStringRGB(KitStyle.Ice);
        var text = new StringBuilder();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !row.TryGetValue("text", out string line) || string.IsNullOrWhiteSpace(line))
                continue;
            if (text.Length > 0) text.Append("        <color=").Append(ice).Append("55>/</color>        ");
            if (row.TryGetValue("label", out string label) && !string.IsNullOrWhiteSpace(label))
                text.Append("<b><color=").Append(accent).Append('>').Append(label.Trim()).Append("</color></b>   ");
            text.Append(line.Trim());
        }
        return text.Length > 0 ? text.ToString() : "<color=" + accent + ">무전</color>   ...";
    }

    /// <summary>Raises the bar in from below after <paramref name="delay"/> (s).</summary>
    public void Open(float delay)
    {
        openAt = Time.unscaledTime + delay;
        band.anchoredPosition = new Vector2(0f, -Height);
    }

    private void Update()
    {
        // A script reload during Play forgets what Build made; nothing to move then.
        if (band == null || clock == null || signal == null) return;
        float time = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        if (!float.IsPositiveInfinity(openAt))
            band.anchoredPosition = new Vector2(0f, -Height * (1f - KitReveal.EaseOut(Mathf.Clamp01((time - openAt) / RiseSeconds))));

        if (width <= 0f && first != null) width = first.preferredWidth;
        if (width > 0f)
        {
            float span = width + Gap;
            x -= Speed * dt;
            if (x <= -span) x += span;
            copies[0].anchoredPosition = new Vector2(x, 0f);
            copies[1].anchoredPosition = new Vector2(x + span, 0f);
        }

        var now = DateTime.Now;
        if (now.Second != shownSecond)
        {
            shownSecond = now.Second;
            clock.text = now.ToString("HH:mm:ss");
        }
        // The receiving light: two quick blinks, then a rest.
        float beat = Mathf.Repeat(time, 1.6f);
        signal.color = KitStyle.With(KitStyle.Accent, beat < .1f || (beat > .22f && beat < .32f) ? 1f : .3f);
    }
}
