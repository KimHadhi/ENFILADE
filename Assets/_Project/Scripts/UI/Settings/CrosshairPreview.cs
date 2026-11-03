using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// A live crosshair in the settings, drawn by the same <see cref="DynamicCrosshairView"/> as the HUD and at the same
/// canvas scale. Half dark, half light, so colour and outline can be judged on both. It spins and cycles on real time
/// while the game is paused; with <see cref="Kicks"/> a click shows the shot kick, unless the shape is held.
/// </summary>
public sealed class CrosshairPreview
{
    private readonly DynamicCrosshairView view;
    private readonly TextMeshProUGUI hint;
    private bool kicks;

    /// <summary>The preview's box, for controls laid over it.</summary>
    public RectTransform Box { get; }

    public bool Kicks
    {
        get => kicks;
        set { kicks = value; ShowHint(); }
    }

    /// <summary>The steady crosshair setting: a held shape ignores the click kick, like the HUD.</summary>
    public bool HoldShape
    {
        get => view.HoldShape;
        set { view.HoldShape = value; ShowHint(); }
    }

    private void ShowHint() => hint.gameObject.SetActive(kicks && !view.HoldShape);

    public CrosshairPreview(Transform parent, float height)
    {
        Box = W.Rect("Crosshair Preview", parent);
        W.Size(Box, height: height);
        var dark = W.Fill(Half(Box, 0f), new Color(.1f, .11f, .13f));
        W.Fill(Half(Box, .5f), new Color(.72f, .74f, .77f));
        view = Draw(Box);

        hint = W.Label(Box, "눌러서 사격 반동 보기", 15f, W.Muted, TextAlignmentOptions.BottomLeft);
        W.Stretch(hint.rectTransform);
        hint.rectTransform.offsetMin = new Vector2(12f, 8f);
        var button = Box.gameObject.AddComponent<Button>();
        button.targetGraphic = dark;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { if (kicks) view.Kick(8f); });
        Kicks = false;
    }

    public void Show(CrosshairProfile profile)
    {
        if (profile != null) view.ApplyProfile(profile);
    }

    /// <summary>A live crosshair centred in <paramref name="parent"/>, waiting for a profile. It never takes clicks.</summary>
    public static DynamicCrosshairView Draw(Transform parent)
    {
        var crosshair = Centred(W.Rect("Crosshair", parent));
        var parts = Centred(W.Rect("Parts", crosshair));
        var dot = Centred(W.Rect("Dot", crosshair)).gameObject.AddComponent<Image>();
        dot.raycastTarget = false;
        var template = Centred(W.Rect("Part Template", crosshair));
        template.gameObject.AddComponent<Image>().raycastTarget = false;
        template.gameObject.SetActive(false);
        var view = crosshair.gameObject.AddComponent<DynamicCrosshairView>();
        view.ConfigurePreview(dot, parts, template.gameObject);
        return view;
    }

    private static RectTransform Half(RectTransform box, float left)
    {
        var half = W.Rect("Half", box);
        half.anchorMin = new Vector2(left, 0f); half.anchorMax = new Vector2(left + .5f, 1f);
        half.offsetMin = half.offsetMax = Vector2.zero;
        return half;
    }

    private static RectTransform Centred(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }
}
