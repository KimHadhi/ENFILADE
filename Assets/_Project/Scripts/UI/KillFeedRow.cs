using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One line of the kill feed. Your own kills glow gold from the killer's end and a sheen crosses them as they arrive, so
/// they read as yours at a glance, and the far end stays free to say body shot or headshot. Everyone's kills carry the
/// killer's streak at the left. Made once and reused.
/// </summary>
public sealed class KillFeedRow
{
    public const float Height = 32f;
    private const float Pad = 12f, Gap = 8f, AccentWidth = 4f, IconSize = 18f, SheenSeconds = .5f;
    private static readonly Color Dark = new Color(.04f, .04f, .06f, .5f), Light = new Color(1f, .96f, .82f, .55f);

    private readonly RectTransform root;
    private readonly CanvasGroup group;
    private readonly KillRowPlate plate;
    private readonly Image accent;
    private readonly TextMeshProUGUI left, right;
    private readonly RawImage icon;
    private readonly StreakBadge streak;
    private float y;
    private bool sheening;

    public float Age { get; private set; }

    public KillFeedRow(Transform feed)
    {
        root = FeelUI.Rect("Kill", feed);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        plate = root.gameObject.AddComponent<KillRowPlate>();
        accent = FeelUI.Rect("Accent", root).gameObject.AddComponent<Image>();
        var accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(1f, 0f); accentRect.anchorMax = Vector2.one; accentRect.pivot = new Vector2(1f, .5f);
        accentRect.sizeDelta = new Vector2(AccentWidth, 0f); accentRect.anchoredPosition = Vector2.zero;
        accent.raycastTarget = false;
        left = FeelUI.ScoreLabel("Weapon", root, 15f, TextAlignmentOptions.Center);
        icon = FeelUI.Rect("Icon", root).gameObject.AddComponent<RawImage>();
        icon.raycastTarget = false;
        right = FeelUI.ScoreLabel("Target", root, 17f, TextAlignmentOptions.Center);
        right.color = Color.white;
        foreach (var rect in new[] { left.rectTransform, icon.rectTransform, right.rectTransform })
            rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
        streak = new StreakBadge(root);
    }

    public void Show(string leftText, Texture iconTexture, Color iconColor, string rightText, Color accentColor, bool mine, int streakCount, KillFeedLook look)
    {
        left.text = leftText;
        left.color = mine ? Color.white : new Color(.86f, .88f, .92f);
        right.text = rightText;
        icon.gameObject.SetActive(iconTexture != null);
        icon.texture = iconTexture;
        icon.color = iconColor;
        accent.color = accentColor;
        Color warm = GunFeelSettings.Instance.scoreColor;
        warm.a = look.ownPlateStrength;
        plate.Paint(mine ? warm : Dark, Dark);
        plate.Sheen(-1f, Color.clear);
        sheening = mine;
        streak.Show(streakCount, look);

        // Right-aligned: [left] [icon] [right] with the accent bar at the far right.
        float iconWidth = iconTexture != null ? IconSize : 0f;
        float rightWidth = right.GetPreferredValues(rightText).x, leftWidth = left.GetPreferredValues(leftText).x;
        root.sizeDelta = new Vector2(Pad + leftWidth + Gap + iconWidth + Gap + rightWidth + Pad + AccentWidth, Height);
        float x = -AccentWidth - Pad;
        Place(right.rectTransform, x - rightWidth * .5f, rightWidth + 4f, Height); x -= rightWidth + Gap;
        Place(icon.rectTransform, x - iconWidth * .5f, iconWidth, iconWidth); x -= iconWidth + Gap;
        Place(left.rectTransform, x - leftWidth * .5f, leftWidth + 4f, Height);
        Age = 0f; y = 0f;
        root.gameObject.SetActive(true);
    }

    public void Hide() => root.gameObject.SetActive(false);

    /// <summary>Ages the row, eases it toward its place in the list and fades it out at the end of its life.</summary>
    public void Tick(float dt, float targetY, float lifetime)
    {
        Age += dt;
        y = Mathf.Lerp(y, targetY, 1f - Mathf.Exp(-18f * dt));
        float enter = 1f - Mathf.Pow(1f - Mathf.Clamp01(Age / .18f), 3f);
        root.anchoredPosition = new Vector2((1f - enter) * 80f, y);
        group.alpha = enter * Mathf.Clamp01((lifetime - Age) / .4f);
        // The accent bar flares wide on arrival.
        float flash = 1f - Mathf.Clamp01(Age / .25f);
        accent.rectTransform.localScale = new Vector3(1f + flash * 1.5f, 1f, 1f);
        streak.Tick();
        if (!sheening) return;
        float through = Age / SheenSeconds;
        if (through >= 1f) { sheening = false; plate.Sheen(-1f, Color.clear); return; }
        plate.Sheen(1.2f * through - .1f, Light);
    }

    private static void Place(RectTransform rect, float x, float width, float height)
    {
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, height);
    }
}
