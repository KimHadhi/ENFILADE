using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's big launch button at the bottom right, as FPS lobbies have it: a slanted pink plate, tilted,
/// with "출격" large, its key (Enter) in a small box at the right, and a line under it saying what it will start
/// ("혼자 · 1장 옥상 탈출"). It flashes white when pressed. The one solid pink thing on the screen.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuLaunch : MonoBehaviour
{
    public const float Width = 520f, Height = 116f;
    private const float Slant = 24f, Tilt = 2f;

    private TMP_Text what;
    private Action onPressed;

    public RectTransform Root { get; private set; }
    /// <summary>The plate's flash, for the screen's opening (KitReveal.Card) and for presses.</summary>
    public Image Lid { get; private set; }

    public static MainMenuLaunch Make(RectTransform parent, float edge, float bottom, Action pressed)
    {
        var root = FeelUI.Rect("Launch", parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-edge, bottom);
        root.sizeDelta = new Vector2(Width, Height);
        root.localRotation = Quaternion.Euler(0f, 0f, Tilt);
        var launch = root.gameObject.AddComponent<MainMenuLaunch>();
        launch.Root = root;
        launch.Build(pressed);
        return launch;
    }

    private void Build(Action pressed)
    {
        KitStyle.Fill(Root, Color.clear).raycastTarget = true;
        KitSkew.On(KitStyle.Fill(KitStyle.Stretch("Plate", Root), KitStyle.Accent), Slant);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", Root, new Vector2(36f, 4f), new Vector2(300f, 76f)), Texts.Get("menu.launch.title"), 58f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ink);
        title.characterSpacing = 4f;
        var key = KitStyle.TopLeft("Key", Root, new Vector2(Width - 120f, 26f), new Vector2(76f, 30f));
        KitStyle.Fill(key, KitStyle.With(KitStyle.Ink, .16f));
        KitStyle.Flat(key, "Enter", 15f, TextAlignmentOptions.Center, KitStyle.Ink).fontStyle = FontStyles.Normal;
        what = KitStyle.Flat(KitStyle.TopLeft("What", Root, new Vector2(40f, 78f), new Vector2(Width - 80f, 26f)), "", 17f,
            TextAlignmentOptions.MidlineLeft, KitStyle.With(KitStyle.Ink, .7f));
        Lid = KitStyle.Fill(KitStyle.Stretch("Flash", Root), KitStyle.Flash);
        KitSkew.On(Lid, Slant);

        onPressed = pressed;
        var button = Root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Press);
        Root.gameObject.AddComponent<KitHover>();
    }

    /// <summary>Presses it (a click, or Enter on the lobby): a white flash, then whatever it starts.</summary>
    public void Press()
    {
        KitPulse.Play(Lid, new Color(1f, 1f, 1f, .6f), .3f);
        onPressed?.Invoke();
    }

    /// <summary>The line under "출격": what pressing it will start.</summary>
    public void Say(string text)
    {
        if (what != null) what.text = text;
    }
}
