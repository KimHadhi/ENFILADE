using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How the title screen opens: the screen is night-dark, a pink line draws itself across the middle, and the dark
/// splits along it like a shutter, the top half rising and the bottom half falling away, to show the menu building
/// itself. Under 0.7 s. Calls back when the menu should start its own reveal, then removes itself.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuIntro : MonoBehaviour
{
    private const float LineSeconds = .18f, OpenAt = .14f, OpenSeconds = .46f, LineFade = .3f;

    private RectTransform top, bottom, line;
    private Image lineImage;
    private float startAt, signalAt;
    private Action opened;

    /// <summary>Starts the intro over <paramref name="canvas"/>; <paramref name="ready"/> runs <paramref name="readyAt"/> seconds in.</summary>
    public static MainMenuIntro Play(RectTransform canvas, Action ready, float readyAt = .12f)
    {
        var holder = KitStyle.Stretch("Intro", canvas);
        var intro = holder.gameObject.AddComponent<MainMenuIntro>();
        intro.top = Half("Top", holder, true);
        intro.bottom = Half("Bottom", holder, false);
        intro.line = FeelUI.Rect("Line", holder);
        intro.line.sizeDelta = new Vector2(2400f, 2f);
        intro.lineImage = KitStyle.Fill(intro.line, KitStyle.Accent);
        intro.line.localScale = new Vector3(0f, 1f, 1f);
        intro.opened = ready;
        intro.signalAt = readyAt;
        intro.startAt = Time.unscaledTime;
        return intro;
    }

    private static RectTransform Half(string name, RectTransform parent, bool upper)
    {
        var half = FeelUI.Rect(name, parent);
        half.anchorMin = new Vector2(0f, upper ? .5f : 0f);
        half.anchorMax = new Vector2(1f, upper ? 1f : .5f);
        half.pivot = new Vector2(.5f, upper ? 0f : 1f);
        half.sizeDelta = Vector2.zero;
        KitStyle.Fill(half, KitStyle.Night).raycastTarget = false;
        return half;
    }

    private void Update()
    {
        // A script reload during Play forgets the shutter's parts: just take it away.
        if (top == null || bottom == null || line == null || lineImage == null) { Destroy(gameObject); return; }
        if (LoadingScreen.Visible) { startAt = Time.unscaledTime; return; } // held shut under the loading screen (SceneWarmup), then played in full
        float t = Time.unscaledTime - startAt;
        line.localScale = new Vector3(KitReveal.EaseOut(Mathf.Clamp01(t / LineSeconds)), 1f, 1f);
        float fade = Mathf.Clamp01((t - LineSeconds) / LineFade);
        lineImage.color = KitStyle.With(KitStyle.Accent, 1f - fade);

        float k = Mathf.Clamp01((t - OpenAt) / OpenSeconds);
        // Slow to start, quick through the middle, soft at the end.
        float eased = k * k * k * (k * (k * 6f - 15f) + 10f);
        top.anchoredPosition = new Vector2(0f, top.rect.height * eased);
        bottom.anchoredPosition = new Vector2(0f, -bottom.rect.height * eased);

        if (opened != null && t >= signalAt)
        {
            var ready = opened;
            opened = null;
            ready();
        }
        if (k >= 1f && fade >= 1f && opened == null) Destroy(gameObject);
    }
}
