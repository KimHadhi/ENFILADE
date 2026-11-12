using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stage 6E, the user's pick A (chevrons) with their LAYERLAB Space Rank insignia: at a chapter's ending, beside its
/// results (CampaignScreen), the class's insignia shows, tilted, its name above it. If the game took it up a rank, the
/// old one shrinks away and the new one slams down oversized and settles with a shake, the screen flashes pink, and a
/// slanted pink 등급 상승 plate sweeps in under it; a prestige says 프레스티지 instead and adds a pink diamond. No numbers
/// (the user's rule: growth reads as the insignia only). Short and loud, then it fades with the card. Real time.
/// With the hub built it plays on the results' closing sheet instead (ResultsSheet), which tells it where to stand.
/// </summary>
[DisallowMultipleComponent]
public sealed class RankClimb : MonoBehaviour
{
    private static readonly Color Pink = new Color(1f, .31f, .6f), Ink = new Color(.05f, .05f, .06f), Pale = new Color(.95f, .95f, .95f);
    // Where it sits on the 1920x1080 card: right of the results.
    private static readonly Vector2 At = new Vector2(690f, -210f);
    private Vector2 at = At;
    private const float Size = 250f, FadeIn = .35f, FadeOut = .5f, ClimbAt = .9f, Slam = .3f, Shake = .22f, FlashSeconds = .2f;

    private ClassGrowth.Climb climb;
    private float age, length;
    private CanvasGroup group;
    private RectTransform before, after, plate, root;
    private Image flash;
    private RectTransform[] pips;
    private bool climbed;

    /// <summary>The ending's insignia for this game's climb, shown this long (real s), at <paramref name="where"/> (px from
    /// the screen's middle on a 1080-high canvas; the closing card's right by default). Nothing for a null climb.</summary>
    public static RankClimb Play(ClassGrowth.Climb climb, float seconds, Vector2? where = null)
    {
        if (climb == null || !Application.isPlaying) return null;
        var shown = new GameObject("Rank climb").AddComponent<RankClimb>();
        RuntimeSceneHierarchy.Group(shown.gameObject, RuntimeSceneHierarchy.Ui, "Screens");
        shown.Begin(climb, seconds, where ?? At);
        return shown;
    }

    private void Begin(ClassGrowth.Climb shown, float seconds, Vector2 where)
    {
        climb = shown;
        at = where;
        length = Mathf.Max(FadeIn + FadeOut + 1f, seconds);
        Build();
        Pose(0f);
    }

    private void Update()
    {
        if (climb == null) return;
        // A hitch pauses the climb rather than skipping it (ResultsShow.Step).
        age += ResultsShow.Step();
        if (age >= length) { Destroy(gameObject); return; }
        Pose(age);
    }

    private void Pose(float t)
    {
        group.alpha = Mathf.Clamp01(t / FadeIn) * Mathf.Clamp01((length - t) / FadeOut);
        float settle = Mathf.Clamp01(t / FadeIn);
        (climb.Rose ? before : after).localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, 1f - (1f - settle) * (1f - settle));
        if (!climb.Rose) return;
        float since = t - ClimbAt;
        if (since < 0f) return;
        if (!climbed) Climb();
        // The old one goes; the new one slams down from nearly twice its size, a hair under, then home.
        before.localScale = Vector3.one * Mathf.Max(0f, 1f - since / .12f);
        float slam = Mathf.Clamp01(since / Slam);
        float size = slam < .55f ? Mathf.Lerp(1.9f, .92f, slam / .55f) : Mathf.Lerp(.92f, 1f, (slam - .55f) / .45f);
        after.localScale = Vector3.one * size;
        float shake = since < Slam + Shake && since > Slam * .5f ? (1f - (since - Slam * .5f) / (Slam * .5f + Shake)) * 12f : 0f;
        Vector2 jitter = shake > 0f ? new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * shake : Vector2.zero;
        root.anchoredPosition = at + jitter;
        var color = flash.color;
        color.a = since < FlashSeconds + Slam * .5f && since > Slam * .5f ? .28f * (1f - (since - Slam * .5f) / FlashSeconds) : 0f;
        flash.color = color;
        float sweep = Mathf.Clamp01((since - Slam * .5f) / .16f);
        sweep = 1f - (1f - sweep) * (1f - sweep);
        plate.anchoredPosition = new Vector2(Mathf.Lerp(-900f, 0f, sweep), -Size * .62f);
        foreach (var pip in pips) if (pip != null) pip.localScale = Vector3.one * Mathf.Lerp(0f, 1f, slam);
    }

    private void Climb()
    {
        climbed = true;
        after.gameObject.SetActive(true);
        plate.gameObject.SetActive(true);
        CombatCueAudio.Play(gameObject, CombatCue.ParryBreak, 1.15f);
    }

    private void Build()
    {
        var layer = new GameObject("Rank climb UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        layer.transform.SetParent(transform, false);
        var canvas = layer.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 87;
        var scaler = layer.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        group = layer.GetComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;

        flash = Panel("Flash", layer.transform, new Color(Pink.r, Pink.g, Pink.b, 0f));
        var full = flash.rectTransform;
        full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one;
        full.offsetMin = full.offsetMax = Vector2.zero;

        root = new GameObject("Insignia", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(layer.transform, false);
        root.anchoredPosition = at;
        root.localRotation = Quaternion.Euler(0f, 0f, -6f);

        // The class's name above, on a dark slab of its own tilt.
        var slab = Panel("Name slab", root, Ink).rectTransform;
        slab.sizeDelta = new Vector2(Size * .9f, 34f);
        slab.anchoredPosition = new Vector2(0f, Size * .6f);
        slab.localRotation = Quaternion.Euler(0f, 0f, 9f);
        var name = Text("Class", slab, climb.ClassTitle, 22f, Pale);
        name.rectTransform.sizeDelta = slab.sizeDelta;

        before = Insignia("Before", root, climb.RankBefore);
        after = Insignia("After", root, climb.RankAfter);
        after.gameObject.SetActive(!climb.Rose);
        if (!climb.Rose) before.gameObject.SetActive(false);

        plate = Panel("Climbed", root, Pink).rectTransform;
        plate.sizeDelta = new Vector2(Size * 1.15f, 40f);
        plate.localRotation = Quaternion.Euler(0f, 0f, 3f);
        var words = Text("Climbed text", plate, climb.PrestigeAfter > climb.PrestigeBefore ? Texts.Get("rank.prestige") : Texts.Get("rank.up"), 24f, Ink);
        words.rectTransform.sizeDelta = plate.sizeDelta;
        plate.gameObject.SetActive(false);

        int count = Mathf.Min(climb.PrestigeAfter, ClassGrowth.MostPips);
        pips = new RectTransform[count];
        for (int i = 0; i < count; i++)
        {
            var pip = Panel("Prestige", root, Pink).rectTransform;
            pip.sizeDelta = new Vector2(14f, 14f);
            pip.localRotation = Quaternion.Euler(0f, 0f, 45f);
            pip.anchoredPosition = new Vector2((i - (count - 1) * .5f) * 24f, -Size * .9f);
            // Only a new prestige's diamonds pop in; the ones it had are already there.
            if (climb.PrestigeAfter == climb.PrestigeBefore || i < climb.PrestigeBefore) pips[i] = null;
            else pips[i] = pip;
            if (pips[i] != null) pip.localScale = Vector3.zero;
        }
    }

    private static RectTransform Insignia(string label, Transform parent, int rank)
    {
        var image = new GameObject(label, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent, false);
        image.raycastTarget = false;
        image.texture = ClassGrowth.Insignia(rank);
        image.rectTransform.sizeDelta = new Vector2(Size, Size);
        if (image.texture == null) image.color = Color.clear;
        return image.rectTransform;
    }

    private static Image Panel(string label, Transform parent, Color color)
    {
        var image = new GameObject(label, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.raycastTarget = false;
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI Text(string label, Transform parent, string words, float size, Color color)
    {
        var text = new GameObject(label, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.raycastTarget = false;
        var font = CombatFeedbackSettings.Instance != null ? CombatFeedbackSettings.Instance.promptFont : null;
        if (font != null) text.font = font;
        text.text = words;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }
}
