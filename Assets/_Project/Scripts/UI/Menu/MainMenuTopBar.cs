using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's top bar, as FPS lobbies have it: the game's name at the left, tilted, decoding into place when the
/// screen opens and glitching for a blink now and then (a white and a pink copy split apart); tabs beside it (the
/// screen's own, then windows such as the character screen, records and settings) with a pink slanted underline that
/// glides to the active one; at the right the player's chip (profile picture or the name's first letter, name, room
/// status; it opens the profile), then the settings (a gear) and quit as icon buttons. It drops in from above.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuTopBar : MonoBehaviour
{
    public const float Height = 64f;
    private const string Name = "ENFILADE";
    private const float DropSeconds = .35f, TabPad = 16f, TabSize = 20f, LogoWidth = 240f, GlitchSeconds = .13f, IconBox = 44f, IconGap = 10f;
    private static readonly Color Dim = new Color(1f, 1f, 1f, .5f);

    private sealed class Tab
    {
        public RectTransform Rect;
        public Button Button;
    }

    private RectTransform bar, underline;
    private TMP_Text title, whiteCopy, pinkCopy;
    private readonly List<Tab> tabs = new List<Tab>();
    private int active;
    private float openAt = float.PositiveInfinity, nextGlitch = float.PositiveInfinity, glitchUntil, lineX, lineWidth;
    private bool linePlaced;
    private readonly System.Random random = new System.Random(808);

    /// <summary>The profile picture square, and the letter shown on it when there is no picture (ProfilePicture.Paint).</summary>
    public RawImage Portrait { get; private set; }
    public TMP_Text Initial { get; private set; }
    public TMP_Text PlayerName { get; private set; }
    public TMP_Text Status { get; private set; }

    /// <param name="tabList">Each tab's label and what pressing it does; the first is the screen itself.</param>
    public static MainMenuTopBar Make(RectTransform canvas, float edge, IList<(string label, Action pressed)> tabList, Action profile, Action settings,
        Action quit)
    {
        var bar = FeelUI.Rect("Top Bar", canvas);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(.5f, 1f);
        bar.sizeDelta = new Vector2(0f, Height);
        KitStyle.Fill(bar, KitStyle.With(KitStyle.Night, .82f));
        var line = FeelUI.Rect("Line", bar);
        line.anchorMin = Vector2.zero;
        line.anchorMax = new Vector2(1f, 0f);
        line.pivot = new Vector2(.5f, 0f);
        line.sizeDelta = new Vector2(0f, 1f);
        KitStyle.Fill(line, KitStyle.With(KitStyle.Ice, .14f));
        var top = bar.gameObject.AddComponent<MainMenuTopBar>();
        top.bar = bar;
        top.Build(edge, tabList, profile, settings, quit);
        return top;
    }

    private void Build(float edge, IList<(string label, Action pressed)> tabList, Action profile, Action settings, Action quit)
    {
        // The name, tilted, with its two glitch copies under it.
        var logo = KitStyle.TopLeft("Logo", bar, new Vector2(edge, 0f), new Vector2(LogoWidth, Height));
        logo.localRotation = Quaternion.Euler(0f, 0f, 4f);
        whiteCopy = NameLabel(logo, KitStyle.With(KitStyle.Ice, 0f), "White");
        pinkCopy = NameLabel(logo, KitStyle.With(KitStyle.Accent, 0f), "Pink");
        title = NameLabel(logo, KitStyle.Text, "Name");

        float x = edge + LogoWidth + 20f;
        for (int i = 0; i < tabList.Count; i++)
        {
            var (label, pressed) = tabList[i];
            var rect = KitStyle.TopLeft("Tab " + label, bar, new Vector2(x, 0f), new Vector2(10f, Height));
            var hit = KitStyle.Fill(rect, Color.clear);
            hit.raycastTarget = true;
            var text = KitStyle.Flat(rect, label, TabSize, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Normal;
            float width = text.GetPreferredValues(label).x + TabPad * 2f;
            rect.sizeDelta = new Vector2(width, Height);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = text;
            if (pressed != null) button.onClick.AddListener(() => pressed());
            tabs.Add(new Tab { Rect = rect, Button = button });
            x += width;
        }
        underline = KitStyle.TopLeft("Underline", bar, new Vector2(0f, Height - 12f), new Vector2(10f, 4f));
        KitSkew.On(KitStyle.Fill(underline, KitStyle.Accent), 6f);
        SetActive(0);

        // At the right: quit, the settings gear before it, and the player's chip before them.
        IconButton("Quit", "Quit", -edge, quit);
        IconButton("Settings", "Settings", -edge - IconBox - IconGap, settings);

        var chip = FeelUI.Rect("Player", bar);
        chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(1f, .5f);
        chip.anchoredPosition = new Vector2(-edge - (IconBox + IconGap) * 2f - 8f, 0f);
        chip.sizeDelta = new Vector2(260f, 48f);
        var chipHit = KitStyle.Fill(chip, Color.clear);
        chipHit.raycastTarget = true;
        var frame = KitStyle.TopLeft("Portrait", chip, new Vector2(0f, 2f), new Vector2(44f, 44f));
        KitStyle.Fill(frame, KitStyle.With(KitStyle.Night, .9f));
        var picture = KitStyle.Stretch("Picture", frame);
        Portrait = picture.gameObject.AddComponent<RawImage>();
        Portrait.raycastTarget = false;
        Initial = KitStyle.Flat(frame, "", 24f, TextAlignmentOptions.Center, KitStyle.Ink);
        KitStyle.Bar("Accent", frame, new Vector2(0f, 41f), new Vector2(44f, 3f), KitStyle.Accent);
        PlayerName = KitStyle.Flat(KitStyle.TopLeft("Name", chip, new Vector2(56f, 2f), new Vector2(204f, 24f)), "", 17f,
            TextAlignmentOptions.MidlineLeft);
        Status = KitStyle.Flat(KitStyle.TopLeft("Status", chip, new Vector2(56f, 25f), new Vector2(204f, 20f)), "", 13f,
            TextAlignmentOptions.MidlineLeft, KitStyle.With(KitStyle.Ice, .75f));
        Status.fontStyle = FontStyles.Normal;
        var chipButton = chip.gameObject.AddComponent<Button>();
        chipButton.transition = Selectable.Transition.None;
        if (profile != null) chipButton.onClick.AddListener(() => profile());
        chip.gameObject.AddComponent<KitHover>();
    }

    // A small cut-corner square with a white icon, <paramref name="x"/> px from the bar's right edge.
    private void IconButton(string name, string icon, float x, Action pressed)
    {
        var box = FeelUI.Rect(name, bar);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, .5f);
        box.anchoredPosition = new Vector2(x, 0f);
        box.sizeDelta = new Vector2(IconBox, IconBox);
        var back = KitStyle.Cut(box, KitStyle.With(KitStyle.Ice, .08f));
        back.raycastTarget = true;
        KitButtons.IconImage(box, icon, 20f, KitStyle.Text);
        var button = box.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        if (pressed != null) button.onClick.AddListener(() => pressed());
        box.gameObject.AddComponent<KitHover>();
    }

    private static TMP_Text NameLabel(RectTransform holder, Color color, string name)
    {
        var box = KitStyle.TopLeft(name, holder, Vector2.zero, new Vector2(LogoWidth, Height));
        var label = KitStyle.Flat(box, Name, 34f, TextAlignmentOptions.MidlineLeft, color);
        label.characterSpacing = 6f;
        return label;
    }

    /// <summary>Marks tab <paramref name="index"/> as the one showing: full white, with the underline under it.</summary>
    public void SetActive(int index)
    {
        active = Mathf.Clamp(index, 0, Mathf.Max(0, tabs.Count - 1));
        for (int i = 0; i < tabs.Count; i++)
        {
            var colours = tabs[i].Button.colors;
            colours.normalColor = colours.selectedColor = i == active ? Color.white : Dim;
            colours.highlightedColor = Color.white;
            colours.pressedColor = new Color(1f, 1f, 1f, .8f);
            colours.fadeDuration = .08f;
            tabs[i].Button.colors = colours;
        }
    }

    /// <summary>Drops the bar in from above after <paramref name="delay"/> (s) and decodes the name.</summary>
    public void Open(float delay)
    {
        openAt = Time.unscaledTime + delay;
        bar.anchoredPosition = new Vector2(0f, Height);
        KitScramble.On(title).Decode(Name, delay + .1f, .6f);
        nextGlitch = Time.unscaledTime + delay + 3f;
    }

    private void Update()
    {
        // A script reload during Play forgets what Build made; nothing to move then.
        if (bar == null || title == null || underline == null) return;
        float time = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        if (!float.IsPositiveInfinity(openAt))
            bar.anchoredPosition = new Vector2(0f, Height * (1f - KitReveal.EaseOut(Mathf.Clamp01((time - openAt) / DropSeconds))));

        if (tabs.Count > 0)
        {
            var tab = tabs[active].Rect;
            float goalX = tab.anchoredPosition.x + TabPad * .6f, goalWidth = tab.sizeDelta.x - TabPad * 1.2f;
            float k = linePlaced ? 1f - Mathf.Exp(-16f * dt) : 1f;
            lineX = Mathf.Lerp(lineX, goalX, k);
            lineWidth = Mathf.Lerp(lineWidth, goalWidth, k);
            linePlaced = true;
            underline.anchoredPosition = new Vector2(lineX, underline.anchoredPosition.y);
            underline.sizeDelta = new Vector2(lineWidth, underline.sizeDelta.y);
        }

        if (time >= nextGlitch)
        {
            glitchUntil = time + GlitchSeconds;
            nextGlitch = time + 4f + (float)random.NextDouble() * 4f;
        }
        bool glitching = time < glitchUntil;
        ((RectTransform)title.rectTransform.parent).anchoredPosition = new Vector2(glitching ? ((float)random.NextDouble() - .5f) * 4f : 0f, 0f);
        Copy(whiteCopy, glitching ? -2f - (float)random.NextDouble() * 3f : 0f, glitching ? .75f : 0f);
        Copy(pinkCopy, glitching ? 2f + (float)random.NextDouble() * 3f : 0f, glitching ? .75f : 0f);
    }

    private static void Copy(TMP_Text copy, float shift, float alpha)
    {
        ((RectTransform)copy.rectTransform.parent).anchoredPosition = new Vector2(shift, alpha > 0f ? shift * .3f : 0f);
        if (!Mathf.Approximately(copy.color.a, alpha)) copy.color = KitStyle.With(copy.color, alpha);
    }
}
