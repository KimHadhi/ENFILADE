using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The class's rank under the HUD's portrait while playing (RankProgress): the insignia this game's growth adds up to, a
/// thin bar to the next one (white what was held when the game began, pink what this game has added; a paler pink the
/// share of the money held, which counts when the game ends and so goes down when money is spent) and the next insignia
/// faint at its end. A kill fills the bar a little and floats a tiny "+100 XP" over it, so what a kill is worth reads at a
/// glance (the user, 2026-11-11: "경험치를 얼마나 받는지 잘 모르겠음"); an insignia this game climbs to shows at once
/// with a small pink arrow up and how many, settling for good when the game ends (ClassGrowth, RankClimb). No other numbers: the user
/// finds big sums tacky, and money stays the one number with a banknote. Made by HudPortrait under its tile, so it leans
/// with it; it sits below the picture because teammates' cards stack above it. Sizes: Hud.csv Rank rows.
/// </summary>
[DisallowMultipleComponent]
public sealed class RankStrip : MonoBehaviour
{
    public const string Row = "Rank";

    [Tooltip("The insignia's side (px at 1080p).")]
    [SerializeField] private float insignia = 30f;
    [Tooltip("The next insignia's side, faint at the bar's end (px).")]
    [SerializeField] private float next = 16f;
    [Tooltip("The bar's thickness (px).")]
    [SerializeField] private float thickness = 5f;
    [Tooltip("How far below the picture the strip's middle sits (px).")]
    [SerializeField] private float below = 22f;
    [Tooltip("How long a kill's +XP floats over the bar (s).")]
    [SerializeField] private float floatSeconds = .9f;
    [Tooltip("How far it rises meanwhile (px).")]
    [SerializeField] private float floatRise = 10f;

    private static readonly Color Empty = new Color(1f, 1f, 1f, .14f), Held = new Color(1f, 1f, 1f, .72f);
    private static readonly Color Kept = new Color(1f, .62f, .74f, .85f);
    private RectTransform root, bar, heldFill, gameFill, keptFill;
    private RawImage badge, upcoming;
    private TMP_Text climb, gain;
    private UITriangle arrow;
    private float half, barLeft, barWidth, heldShown, gameShown, keptShown, gainAge = float.MaxValue, gainAmount, popAge = float.MaxValue;
    private long lastKills = -1;
    private int lastRank, lastPrestige, lastClimb;

    /// <summary>Makes the strip under a tile whose picture reaches <paramref name="pictureHalf"/> from its middle (px).</summary>
    public static RankStrip Make(RectTransform tile, float pictureHalf)
    {
        var rect = new GameObject("Rank Strip", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(tile, false);
        rect.gameObject.layer = tile.gameObject.layer;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var strip = rect.gameObject.AddComponent<RankStrip>();
        strip.half = pictureHalf;
        GameTables.Bind(HudCamera.TableName, Row, strip);
        return strip;
    }

    private void Start() => Build();

    private void Build()
    {
        root = (RectTransform)transform;
        root.sizeDelta = new Vector2(half * 2f, insignia);
        root.anchoredPosition = new Vector2(0f, -half - below);
        badge = Image("Insignia", new Vector2(-half + insignia * .5f, 0f), insignia, Color.white);
        barLeft = -half + insignia + 6f;
        barWidth = Mathf.Max(20f, half * 2f - insignia - 6f - next - 8f);
        bar = Box("Bar", barLeft, barWidth, Empty);
        heldFill = Box("Held", barLeft, 0f, Held);
        keptFill = Box("Kept", barLeft, 0f, Kept);
        gameFill = Box("This Game", barLeft, 0f, KitStyle.Accent);
        upcoming = Image("Next Insignia", new Vector2(barLeft + barWidth + 4f + next * .5f, 0f), next, new Color(1f, 1f, 1f, .35f));
        // A small pink arrow up and how many insignias this game climbs (drawn: the score font has no arrow glyph).
        var tip = Child("Climb Arrow");
        tip.anchoredPosition = new Vector2(-half + insignia + 2f, insignia * .5f - 1f);
        tip.sizeDelta = new Vector2(8f, 7f);
        arrow = tip.gameObject.AddComponent<UITriangle>();
        arrow.SetCorners(new Vector2(0f, 0f), new Vector2(.5f, 1f), new Vector2(1f, 0f));
        arrow.color = KitStyle.Accent;
        arrow.raycastTarget = false;
        climb = Label("Climb", 12f, TextAlignmentOptions.Left);
        climb.rectTransform.anchoredPosition = new Vector2(-half + insignia + 8f + 45f, insignia * .5f);
        climb.color = KitStyle.Accent;
        gain = Label("Gain", 13f, TextAlignmentOptions.Center);
        gain.color = KitStyle.Accent;
        gain.enabled = false;
    }

    private RawImage Image(string name, Vector2 at, float size, Color tint)
    {
        var rect = Child(name);
        rect.anchoredPosition = at;
        rect.sizeDelta = new Vector2(size, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.color = tint;
        image.raycastTarget = false;
        return image;
    }

    // A flat bar from the left, grown by its width.
    private RectTransform Box(string name, float left, float width, Color tint)
    {
        var rect = Child(name);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(left, 0f);
        rect.sizeDelta = new Vector2(width, thickness);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = tint;
        image.raycastTarget = false;
        return rect;
    }

    private TMP_Text Label(string name, float size, TextAlignmentOptions alignment)
    {
        var text = FeelUI.ScoreLabel(name, root, size, alignment);
        text.rectTransform.sizeDelta = new Vector2(90f, size * 1.4f);
        text.gameObject.layer = gameObject.layer;
        text.raycastTarget = false;
        return text;
    }

    private RectTransform Child(string name)
    {
        var rect = FeelUI.Rect(name, root);
        rect.gameObject.layer = gameObject.layer;
        return rect;
    }

    private void LateUpdate()
    {
        if (badge == null) return;
        float dt = Time.unscaledDeltaTime;
        var view = RankProgress.Now();
        badge.texture = ClassGrowth.Insignia(view.Rank);
        badge.enabled = badge.texture != null;
        upcoming.texture = view.Top ? null : ClassGrowth.Insignia(view.Rank + 1);
        upcoming.enabled = upcoming.texture != null;

        // A kill's growth: a tiny +XP over where the bar has filled to.
        if (lastKills >= 0 && view.Kills > lastKills && view.Playing)
        {
            gainAmount = gainAge < .4f ? gainAmount + (view.Kills - lastKills) : view.Kills - lastKills;
            gainAge = 0f;
            gain.text = Texts.Format("hud.rank.gain", gainAmount.ToString("N0"));
            gain.enabled = true;
        }
        lastKills = view.Kills;
        // A new insignia within reach of this game: it pops in pink.
        if ((view.Rank != lastRank || view.Prestige != lastPrestige) && view.Climb > lastClimb) popAge = 0f;
        lastRank = view.Rank;
        lastPrestige = view.Prestige;
        lastClimb = view.Climb;

        float held = view.StoredShare, total = view.Share;
        float kills = view.Earned > 0 ? Mathf.Lerp(held, total, view.Kills / (float)view.Earned) : held;
        float ease = 1f - Mathf.Exp(-12f * dt);
        heldShown = Mathf.Lerp(heldShown, held, ease);
        gameShown = Mathf.Lerp(gameShown, kills, ease);
        keptShown = Mathf.Lerp(keptShown, total, ease);
        Fill(heldFill, 0f, heldShown);
        Fill(gameFill, heldShown, gameShown);
        Fill(keptFill, gameShown, keptShown);

        climb.enabled = arrow.enabled = view.Climb > 0;
        if (climb.enabled) climb.text = view.Climb.ToString();
        popAge += dt;
        float pop = Mathf.Clamp01(popAge / .18f);
        badge.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, 1f - (1f - pop) * (1f - pop));
        badge.color = Color.Lerp(KitStyle.Accent, Color.white, pop);

        if (gainAge < floatSeconds)
        {
            gainAge += dt;
            float k = Mathf.Clamp01(gainAge / Mathf.Max(.05f, floatSeconds));
            gain.rectTransform.anchoredPosition = new Vector2(barLeft + barWidth * gameShown, thickness + 8f + floatRise * k);
            gain.alpha = 1f - k * k;
            if (gainAge >= floatSeconds) gain.enabled = false;
        }
    }

    private void Fill(RectTransform part, float from, float to)
    {
        part.anchoredPosition = new Vector2(barLeft + barWidth * from, 0f);
        part.sizeDelta = new Vector2(Mathf.Max(0f, barWidth * (to - from)), thickness);
    }
}
