using System;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The main menu's character screen (<see cref="CharacterSkinView"/>). It takes the menu column's place while open, and
/// the 3D preview beside it shows each pick, since <see cref="MainMenuPreview"/> follows the settings. Picks are saved at
/// once, as the menu's other choices are.
/// </summary>
public sealed class CharacterScreenView
{
    private const float Width = 600f;
    private readonly RectTransform panel, menu;
    private readonly Action closed;

    /// <param name="menu">The menu column the screen stands in for.</param>
    /// <param name="closed">Called on the way back to the menu.</param>
    public CharacterScreenView(Transform canvas, RectTransform menu, Action closed)
    {
        this.menu = menu;
        this.closed = closed;
        panel = W.Rect("Character Screen", canvas);
        panel.anchorMin = panel.anchorMax = panel.pivot = menu.pivot;
        panel.anchoredPosition = menu.anchoredPosition;
        panel.sizeDelta = new Vector2(Width, 0f);
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel.gameObject.SetActive(false);
    }

    /// <summary>Showing rebuilds the screen, so it opens on the saved pick.</summary>
    public void Show()
    {
        W.Clear(panel);
        W.Size(W.Label(panel, Texts.Get("menu.character.title"), 48f, W.Text), height: 72f);
        var cards = W.Rect("Cards", panel);
        var layout = cards.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        new CharacterSkinView(cards, Width, false, GameSettings.Save);
        W.Size(W.Rect("Gap", panel), height: 12f);
        W.Size(W.ActionButton(panel, Texts.Get("common.back"), false, Hide), height: 54f);
        menu.gameObject.SetActive(false);
        panel.gameObject.SetActive(true);
    }

    public bool IsOpen => panel.gameObject.activeSelf;

    /// <summary>Goes back to the menu if the screen is open (the lobby's 플레이 tab).</summary>
    public void Close()
    {
        if (panel.gameObject.activeSelf) Hide();
    }

    private void Hide()
    {
        panel.gameObject.SetActive(false);
        menu.gameObject.SetActive(true);
        closed?.Invoke();
    }
}
