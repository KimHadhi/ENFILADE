using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One line of the kill feed. Your own kills glow gold from the killer's end and a sheen crosses them as they arrive, so
/// they read as yours at a glance, and the far end stays free to say body shot or headshot. Everyone's kills carry the
/// killer's streak at the left. The newest row is the feed's main course: a quarter bigger, a light card with dark ink
/// and its icon in the kill's colour, a gap under it; it turns back into an ordinary row when a newer one arrives.
/// Made once and reused.
/// </summary>
public sealed class KillFeedRow
{
    public const float Height = 32f;
    private const float Pad = 12f, Gap = 8f, AccentWidth = 4f, IconSize = 18f, SheenSeconds = .5f;
    // FeedFlame's BorderMargin: how far the border's image stands out from the row on each side (px).
    private static readonly Vector2 BorderMargin = new Vector2(14f, 20f);
    private static readonly Color Dark = new Color(.04f, .04f, .06f, .5f), Light = new Color(1f, .96f, .82f, .55f);

    private readonly RectTransform root;
    private readonly CanvasGroup group;
    private readonly KillRowPlate plate;
    private readonly Image accent;
    private readonly TextMeshProUGUI left, right;
    private readonly RawImage icon, border;
    private readonly StreakBadge streak;
    private readonly float seed = Random.value * 40f;
    // Each new row tilts the other way from the last, so two quick kills read as two slips.
    private static float nextRoll = 1f;
    private float y, lead, roll, weight = 1f, shade = 1f, painted = -1f;
    private bool sheening, mine;
    private Color warm;
    private KillFeedLook look;
    // The row's own colours and the newest row's, between which Paint blends.
    private Color plateLeft, heroLeft, leftInk, iconInk, heroIcon;

    public float Age { get; private set; }
    /// <summary>This PC's own kill: the feed lists these first (backlog 332).</summary>
    public bool Mine => mine;

    /// <summary>The height the row takes in the list now: the newest row's growth and the gap under it included.</summary>
    public float Extent => look == null ? Height : Height * (1f + look.leadScale * lead) + look.heroGap * lead;

    public KillFeedRow(Transform feed)
    {
        root = FeelUI.Rect("Kill", feed);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        plate = root.gameObject.AddComponent<KillRowPlate>();
        // Fire round the whole row (backlog 325), over the plate and under everything on it.
        border = FeelUI.Rect("Border Fire", root).gameObject.AddComponent<RawImage>();
        border.material = StreakBadge.FlameMaterial(StreakBadge.BorderOfFire);
        border.raycastTarget = false;
        var borderRect = border.rectTransform;
        borderRect.anchorMin = Vector2.zero; borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = -BorderMargin; borderRect.offsetMax = BorderMargin;
        border.gameObject.SetActive(false);
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

    /// <summary>emphatic: a headshot, a burning streak or a grave notice leans out a little further while newest. fire: the
    /// border of fire round the row, a skull streak's blue or a named or elite kill's magenta (backlog 325).</summary>
    public void Show(string leftText, Texture iconTexture, Color iconColor, string rightText, Color accentColor, bool mine, int streakCount,
        KillFeedLook look, bool emphatic, FeedBorder fire = FeedBorder.None)
    {
        this.look = look;
        weight = 1f + (emphatic || fire != FeedBorder.None || streakCount >= look.flameFrom ? look.leadEmphasis : 0f);
        roll = nextRoll * look.leadRoll;
        nextRoll = -nextRoll;
        lead = 0f;
        left.text = leftText;
        leftInk = mine ? Color.white : new Color(.86f, .88f, .92f);
        right.text = rightText;
        icon.gameObject.SetActive(iconTexture != null);
        icon.texture = iconTexture;
        iconInk = iconColor;
        // On the light card the icon takes the kill's colour, deep enough to read on it.
        heroIcon = new Color(accentColor.r * .78f, accentColor.g * .78f, accentColor.b * .78f, 1f);
        accent.color = accentColor;
        this.mine = mine;
        warm = GunFeelSettings.Instance.scoreColor;
        plate.Highlight(Color.clear, 0f);
        plateLeft = mine ? new Color(warm.r, warm.g, warm.b, look.ownPlateStrength) : Dark;
        heroLeft = mine ? Color.Lerp(look.heroPlate, new Color(warm.r, warm.g, warm.b, look.heroPlate.a), look.heroOwnWash) : look.heroPlate;
        painted = -1f;
        Paint(0f);
        plate.Sheen(-1f, Color.clear);
        sheening = mine;
        streak.Show(streakCount, look);

        // Right-aligned: [left] [icon] [right] with the accent bar at the far right.
        float iconWidth = iconTexture != null ? IconSize : 0f;
        float rightWidth = right.GetPreferredValues(rightText).x, leftWidth = left.GetPreferredValues(leftText).x;
        float rowWidth = Pad + leftWidth + Gap + iconWidth + Gap + rightWidth + Pad + AccentWidth;
        root.sizeDelta = new Vector2(rowWidth, Height);
        // The row's width travels in the uv's whole part, so the shader knows where the plate's outline runs.
        border.gameObject.SetActive(fire != FeedBorder.None);
        if (fire != FeedBorder.None)
        {
            Color tint = fire == FeedBorder.Standout ? look.eliteColor : look.blueColor;
            tint.a = 1f;
            border.color = tint;
            border.uvRect = new Rect(Mathf.Floor(seed), Mathf.Round(rowWidth), 1f, 1f);
        }
        float x = -AccentWidth - Pad;
        Place(right.rectTransform, x - rightWidth * .5f, rightWidth + 4f, Height); x -= rightWidth + Gap;
        Place(icon.rectTransform, x - iconWidth * .5f, iconWidth, iconWidth); x -= iconWidth + Gap;
        Place(left.rectTransform, x - leftWidth * .5f, leftWidth + 4f, Height);
        Age = 0f; y = 0f; shade = 1f;
        root.gameObject.SetActive(true);
    }

    public void Hide() => root.gameObject.SetActive(false);

    /// <summary>
    /// Ages the row, eases it toward its place in the list and fades it out at the end of its life. rank is 0 for the
    /// newest row, 1 for the one below it, and so on. The newest grows into its light card, leans a touch toward the
    /// player, stands a little off and keeps a wider end bar; it arrives leaning further and settles, and gives all that
    /// back once a newer row arrives. Each older row is a step dimmer.
    /// </summary>
    public void Tick(float dt, float targetY, float lifetime, int rank)
    {
        Age += dt;
        y = Mathf.Lerp(y, targetY, 1f - Mathf.Exp(-18f * dt));
        float enter = 1f - Mathf.Pow(1f - Mathf.Clamp01(Age / .18f), 3f);
        lead = Mathf.Lerp(lead, rank == 0 ? 1f : 0f, 1f - Mathf.Exp(-12f * dt));
        Paint(lead);
        float settle = Mathf.Pow(1f - Mathf.Clamp01(Age / .3f), 3f);
        float yaw = look != null ? look.leadYaw * weight * lead + 4f * settle : 0f;
        float lift = look != null ? look.leadLift * weight * lead + 18f * settle : 0f;
        // The row pivots on its right end; turning its left end toward the player is a negative yaw.
        root.localRotation = Quaternion.Euler(0f, -yaw, roll * lead);
        root.anchoredPosition3D = new Vector3((1f - enter) * 80f, y, -lift);
        root.localScale = Vector3.one * (1f + (look != null ? look.leadScale : 0f) * lead);
        // A recent row keeps full opacity and lit edges, gold for your own, fainter white for anyone else's; both ease off
        // over .4 s once it stops being recent (backlog 332).
        float recent = look != null ? 1f - Mathf.Clamp01((Age - look.recentSeconds) / .4f) : 0f;
        float dim = look != null ? Mathf.Lerp(Mathf.Max(look.olderFloor, 1f - look.olderFade * rank), 1f, recent) : 1f;
        shade = Mathf.Lerp(shade, dim, 1f - Mathf.Exp(-10f * dt));
        if (look != null)
        {
            Color edge = mine ? warm : Color.white;
            edge.a = recent * (mine ? 1f : look.recentOthers);
            plate.Highlight(edge, look.recentEdge);
        }
        group.alpha = enter * shade * Mathf.Clamp01((lifetime - Age) / .4f);
        // The accent bar flares wide on arrival and stays wider while the row is the newest.
        float flash = 1f - Mathf.Clamp01(Age / .25f);
        accent.rectTransform.localScale = new Vector3(1f + flash * 1.5f + (look != null ? look.leadAccent : 0f) * lead, 1f, 1f);
        streak.Tick();
        if (!sheening) return;
        float through = Age / SheenSeconds;
        if (through >= 1f) { sheening = false; plate.Sheen(-1f, Color.clear); return; }
        plate.Sheen(1.2f * through - .1f, Light);
    }

    // From the row's own colours (0) to the newest row's light card with dark ink (1).
    private void Paint(float hero)
    {
        if (look == null || Mathf.Abs(hero - painted) < .002f) return;
        painted = hero;
        plate.Paint(Color.Lerp(plateLeft, heroLeft, hero), Color.Lerp(Dark, look.heroPlate, hero));
        left.color = Color.Lerp(leftInk, look.heroInk, hero);
        right.color = Color.Lerp(Color.white, look.heroInk, hero);
        icon.color = Color.Lerp(iconInk, heroIcon, hero);
    }

    private static void Place(RectTransform rect, float x, float width, float height)
    {
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, height);
    }
}
