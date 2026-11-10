using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using W = SettingsWidgets;

/// <summary>
/// "누르고 있으면 실제 화면": while held, the whole settings menu fades out so the real HUD crosshair shows over the game,
/// as the kind being edited even when the other one is in hand (<see cref="PlayerCrosshairs.Showcase"/>). Letting go, or
/// the menu closing, brings everything back. Runs on real time, since the menu pauses the game.
/// </summary>
[DisallowMultipleComponent]
public sealed class CrosshairPeek : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    private const float FadeSeconds = .12f;
    private CanvasGroup menu;
    private CrosshairKind kind;
    private bool held;

    /// <summary>A small button in the top right of the preview <paramref name="box"/>.</summary>
    public static void Add(RectTransform box, CrosshairKind kind)
    {
        var chip = W.Rect("Real Screen", box);
        chip.anchorMin = chip.anchorMax = chip.pivot = Vector2.one;
        chip.anchoredPosition = new Vector2(-8f, -8f);
        chip.sizeDelta = new Vector2(214f, 30f);
        W.Fill(chip, new Color(0f, 0f, 0f, .6f));
        var label = W.Label(chip, Texts.Get("crosshair.peek"), 15f, W.Text, TextAlignmentOptions.Center);
        W.Stretch(label.rectTransform);
        var peek = chip.gameObject.AddComponent<CrosshairPeek>();
        peek.kind = kind;
        var canvas = box.GetComponentInParent<Canvas>(true).rootCanvas; // the menu builds while still hidden
        if (!canvas.TryGetComponent(out peek.menu)) peek.menu = canvas.gameObject.AddComponent<CanvasGroup>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        held = true;
        Cursor.visible = false;
        PlayerCrosshairs.SetShowcase(kind);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Release();
    }

    public void OnPointerClick(PointerEventData eventData) { } // keeps the click from reaching the preview's shot kick

    private void Update()
    {
        menu.alpha = Mathf.MoveTowards(menu.alpha, held ? 0f : 1f, Time.unscaledDeltaTime / FadeSeconds);
    }

    private void OnDisable()
    {
        Release();
        if (menu != null) menu.alpha = 1f;
    }

    private void Release()
    {
        if (!held) return;
        held = false;
        Cursor.visible = true;
        PlayerCrosshairs.SetShowcase(null);
    }
}
