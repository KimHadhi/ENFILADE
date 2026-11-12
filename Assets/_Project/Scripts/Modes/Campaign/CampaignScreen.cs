using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What a campaign chapter puts on the screen (CampaignRun): its story card when it begins and ends, faded over the game
/// without stopping it (at the end with the chapter's results under the story: kills and money, the team's too), and the
/// prompt at something to press ("[F] 셔터 올리기").
/// </summary>
[DisallowMultipleComponent]
public sealed class CampaignScreen : MonoBehaviour
{
    private const float FadeIn = .8f, FadeOut = 1.2f, TitleSize = 64f, StorySize = 30f, PromptSize = 27f;
    // The card's pieces stack from the top with these gaps (px at 1080p), the stack centred on the screen, inside Room:
    // a long story gets smaller type (down to SmallestStory) rather than running off the card.
    private const float TitleGap = 44f, ResultsGap = 56f, Room = 960f, SmallestStory = 18f;
    private static readonly Color Shade = new Color(0f, 0f, 0f, .82f), Pale = new Color(1f, .94f, .9f);
    private const float ResultsSize = 26f;
    private GameObject canvas;
    private CanvasGroup card;
    private TextMeshProUGUI title, story, prompt, results;
    private float shownAt = float.NegativeInfinity, shownFor;

    /// <summary>Shows the card for <paramref name="seconds"/>, fading in and out; with <paramref name="summary"/> (a
    /// chapter's end, stage 6A) the story moves up and the money and experience sit under it.</summary>
    public void ShowCard(string heading, string[] lines, float seconds, string summary = null)
    {
        Build();
        bool withResults = !string.IsNullOrEmpty(summary);
        title.text = heading ?? "";
        story.text = lines != null ? string.Join("\n\n", lines) : "";
        results.text = summary ?? "";
        results.gameObject.SetActive(withResults);
        story.fontSize = withResults ? ResultsSize : StorySize;
        Stack(withResults);
        shownAt = Time.unscaledTime;
        shownFor = Mathf.Max(FadeIn + FadeOut, seconds);
    }

    // Each piece as tall as its words need, one under the other, the stack centred on the screen. The boxes used to be
    // fixed: the story's box began 40 px inside the title's, so its first line ran through the chapter's name (the
    // user, 2026-11-12: "글자가 1장이랑 아래 설명 텍스트랑 겹쳐서"), and a longer story or title would only overlap more.
    private void Stack(bool withResults)
    {
        float titleHeight = Height(title), resultsHeight = withResults ? Height(results) : 0f;
        float storyHeight = Height(story);
        float others = titleHeight + TitleGap + (withResults ? ResultsGap + resultsHeight : 0f);
        // Too much story for the screen: smaller type until it fits (the height goes about with the square of the size).
        for (int i = 0; i < 3 && others + storyHeight > Room && story.fontSize > SmallestStory; i++)
        {
            float room = Mathf.Max(1f, Room - others);
            story.fontSize = Mathf.Max(SmallestStory, story.fontSize * Mathf.Sqrt(room / storyHeight));
            storyHeight = Height(story);
        }
        float top = (others + storyHeight) / 2f;
        top = Place(title, top, titleHeight) - TitleGap;
        top = Place(story, top, storyHeight) - ResultsGap;
        if (withResults) Place(results, top, resultsHeight);
    }

    // Sets the label's box to this height with its top edge at y = top (px from the screen's middle); returns its bottom.
    private static float Place(TMP_Text label, float top, float height)
    {
        var box = label.rectTransform;
        box.sizeDelta = new Vector2(box.sizeDelta.x, height);
        box.anchoredPosition = new Vector2(box.anchoredPosition.x, top - height / 2f);
        return top - height;
    }

    // The height the label's words take at its width (px); at least one line, so an empty piece keeps its place.
    private static float Height(TMP_Text label)
        => Mathf.Ceil(Mathf.Max(label.GetPreferredValues(label.text, label.rectTransform.sizeDelta.x, 0f).y, label.fontSize));

    /// <summary>The prompt this frame, or null for none.</summary>
    public void Prompt(string text)
    {
        if (string.IsNullOrEmpty(text) && canvas == null) return;
        Build();
        bool shown = !string.IsNullOrEmpty(text);
        if (prompt.gameObject.activeSelf != shown) prompt.gameObject.SetActive(shown);
        if (shown) prompt.text = text;
    }

    private void Update()
    {
        if (card == null) return;
        float age = Time.unscaledTime - shownAt;
        float alpha = age < 0f || age > shownFor ? 0f : Mathf.Min(Mathf.Clamp01(age / FadeIn), Mathf.Clamp01((shownFor - age) / FadeOut));
        card.alpha = alpha;
        if (card.gameObject.activeSelf != alpha > 0f) card.gameObject.SetActive(alpha > 0f);
    }

    private void Build()
    {
        if (canvas != null) return;
        canvas = new GameObject("Campaign Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas.hideFlags = HideFlags.DontSave;
        canvas.transform.SetParent(transform, false);
        var layer = canvas.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = 85;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        var shade = FeelUI.Rect("Card", canvas.transform);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.sizeDelta = Vector2.zero;
        var fill = shade.gameObject.AddComponent<Image>();
        fill.color = Shade;
        fill.raycastTarget = false;
        card = shade.gameObject.AddComponent<CanvasGroup>();
        card.blocksRaycasts = card.interactable = false;
        title = Label("Title", shade, TitleSize, new Vector2(1400f, 120f), new Vector2(0f, 170f), ObjectiveLogPanel.Accent);
        story = Label("Story", shade, StorySize, new Vector2(1300f, 520f), new Vector2(0f, -110f), Pale);
        story.fontStyle = FontStyles.Normal;
        story.alignment = TextAlignmentOptions.Top;
        results = Label("Results", shade, ResultsSize, new Vector2(1000f, 470f), new Vector2(0f, -255f), Pale);
        // Numbers read in the score font (Pretendard), as they do on the HUD.
        var numbers = GunFeelSettings.Instance.scoreFont;
        if (numbers != null) results.font = numbers;
        results.fontStyle = FontStyles.Normal;
        results.alignment = TextAlignmentOptions.TopLeft;
        results.gameObject.SetActive(false);
        card.gameObject.SetActive(false);

        prompt = Label("Prompt", canvas.transform, PromptSize, new Vector2(1200f, 60f), new Vector2(0f, -175f), Pale);
        prompt.gameObject.SetActive(false);
    }

    private static TextMeshProUGUI Label(string name, Transform parent, float size, Vector2 box, Vector2 at, Color color)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false);
        label.rectTransform.sizeDelta = box;
        label.rectTransform.anchoredPosition = at;
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null && settings.promptFont != null) label.font = settings.promptFont;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        return label;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas);
    }
}
