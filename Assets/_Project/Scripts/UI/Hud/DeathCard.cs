using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The death screen's pieces on the screen (DeathScreen says what goes in them and when):
/// - The film over the world, under the HUD (drawn through HudCamera beneath the GameHUD canvas), as the MW3 Death
///   Screen addon draws its vignette and blood: DeathFilmGraphic's ink edge with specks, and its blood.
/// - The card, over the HUD, framing the middle of the screen so the fallen eyes still see what is in front of them:
///   above it the word (사망, or 전멸 for a wipe) on a near-black plate with a pink slash crossing behind it, and under
///   that what killed them on a thin dark band; below it three numbers on their own plates (how long they lasted,
///   their kills this life, the money they hold beside its banknote), and under those the one line that says what
///   comes next, a pink hairline under it running down with its countdown.
/// Each piece leans its own way (the HUD's Persona rule) with clear space between them, checked at every lean they have.
/// On a short screen (21:9 and wider) the whole card is drawn smaller about the middle, so it keeps clear of the top
/// centre's alerts. Its entrance: the slash draws out and the word slams in from large, its letters closing up; the
/// cause slides in; the numbers pop in one after another and count up; the line comes last. It leaves by fading as the
/// film does.
/// Real time throughout. The canvases are made on first use and destroyed with the player.
/// </summary>
public sealed class DeathCard
{
    /// <summary>What the card says.</summary>
    public struct Facts
    {
        public string Title, Cause, Next;
        public int Kills;
        public long Money;
        public float LastedSeconds;
    }

    // Over the HUD and its prompts, under PlayerLifeHUD (80: the spectate keys stay readable), the score board and menus.
    private const int CardOrder = 78;
    // Under the GameHUD canvas (0) on the HUD camera, over the world.
    private const int FilmOrder = -5;
    // Where each piece sits (px at 1080p from the screen's middle), how big it is, and its own lean (° anticlockwise).
    // Measured with the leans on (polygon distances at full size): the word's piece, slash and shards included, clears the
    // cause band by 16 px and reaches 249 px over the middle; the band's lowest corner stays 41 px over the middle and the
    // tiles' highest 62 px under it; the tiles are 19 px apart and the line 25 px under them.
    private static readonly Vector2 TitleAt = new Vector2(-24f, 178f), TitleSize = new Vector2(560f, 108f);
    private static readonly Vector2 CauseAt = new Vector2(22f, 68f), CauseSize = new Vector2(600f, 42f);
    private static readonly Vector2 NextAt = new Vector2(0f, -196f), NextSize = new Vector2(660f, 36f);
    private static readonly Vector2 TileSize = new Vector2(178f, 76f);
    private static readonly float[] TileX = { -198f, 0f, 198f }, TileLean = { 2f, -1f, 1.5f };
    private const float TileY = -108f, TitleLean = 3f, CauseLean = -1.2f, NextLean = -1f;
    private const float TitleSlant = 26f, CauseSlant = 14f, WordType = 92f, CauseType = 30f, LabelType = 17f, ValueType = 36f, NextType = 26f;
    // The word's letters close up from this far apart to their own spacing (TMP character spacing, hundredths of an em).
    private const float SpacingFrom = 60f, SpacingTo = 16f;
    // The top centre's new-objective alert reaches 243 px down from the screen's top (MissionHUD), and 20 px is kept
    // under it; the word's piece reaches TitleReach over the middle. A screen too short for both (21:9 and wider) draws
    // the whole card smaller about the middle, never below LeastScale.
    private const float TopRoom = 263f, TitleReach = 249f, LeastScale = .45f;
    // The entrance (s after it starts): slash, word, cause, the first number and the gap to the next, the line.
    private const float SlashFrom = .08f, SlashFor = .14f, WordFrom = .1f, WordFor = .16f, CauseFrom = .24f, CauseFor = .16f;
    private const float TileFrom = .36f, TileGap = .07f, TileFor = .14f, CountFor = .45f, NextFrom = .62f, NextFor = .18f;
    private const float HairlineWidth = 420f;

    private GameObject cardHost, filmHost;
    private RectTransform card;
    private DeathFilmGraphic ink, blood;
    private CanvasGroup titleGroup, causeGroup, nextGroup;
    private readonly CanvasGroup[] tileGroups = new CanvasGroup[3];
    private RectTransform titleRect, causeRect, nextRect;
    private readonly RectTransform[] tileRects = new RectTransform[3];
    private DeathCardGraphic titlePlate;
    private TextMeshProUGUI titleText, causeText, nextText;
    private readonly TextMeshProUGUI[] values = new TextMeshProUGUI[3];
    private Image hairline;
    private Facts facts;
    private float startedAt = -1f, wordAt = -1f;
    private bool still;

    public bool Built => cardHost != null;

    /// <summary>Makes the canvases ahead of the first death (DeathScreen.Prepare), hidden.</summary>
    public void Build()
    {
        if (cardHost != null) return;
        filmHost = new GameObject("Death Film", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupHud(filmHost, FilmOrder);
        ink = Film("Death Ink", DeathFilmGraphic.Layer.Ink, 7331);
        blood = Film("Death Blood", DeathFilmGraphic.Layer.Blood, 4409);

        cardHost = new GameObject("Death Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupOverlay(cardHost, CardOrder);
        card = FeelUI.Rect("Death Card", cardHost.transform);
        BuildTitle();
        BuildCause();
        for (int i = 0; i < 3; i++) BuildTile(i);
        BuildNext();
        Show(false);
    }

    /// <summary>Destroys both canvases (the player is going).</summary>
    public void Destroy()
    {
        if (cardHost != null) Object.Destroy(cardHost);
        if (filmHost != null) Object.Destroy(filmHost);
        cardHost = filmHost = null;
    }

    /// <summary>Starts the entrance with these words, now.</summary>
    public void Begin(in Facts said, float now)
    {
        Build();
        facts = said;
        startedAt = wordAt = now;
        still = false;
        titleText.text = said.Title ?? "";
        causeText.text = said.Cause ?? "";
        nextText.text = said.Next ?? "";
        Show(true);
    }

    /// <summary>The word changes (사망 becomes 전멸 when the team falls), slammed in again where the card already is.</summary>
    public void Restamp(string title, string cause, float now)
    {
        if (cardHost == null || startedAt < 0f) return;
        facts.Title = title;
        titleText.text = title ?? "";
        if (cause != null) { facts.Cause = cause; causeText.text = cause; }
        wordAt = now;
    }

    /// <summary>Every piece in its settled place with these words, for Tools → HUD → 겹침 검사 (IHudPreview).</summary>
    public void Hold(in Facts said)
    {
        Build();
        facts = said;
        still = true;
        titleText.text = said.Title ?? "";
        causeText.text = said.Cause ?? "";
        nextText.text = said.Next ?? "";
        Show(true);
        Tick(float.MaxValue, 1f, "", -1f, 0f, 0f);
    }

    /// <summary>Hides everything at once.</summary>
    public void Hide()
    {
        startedAt = wordAt = -1f;
        still = false;
        if (cardHost != null) Show(false);
    }

    /// <summary>
    /// This frame's look: the entrance by the time since it began, everything faded by <paramref name="shown"/> (1 while
    /// it holds, falling to 0 as it leaves), the line's words and its hairline (<paramref name="fill"/> 0-1, below 0 for
    /// none), and the film's two layers.
    /// </summary>
    public void Tick(float now, float shown, string next, float fill, float inkLevel, float bloodLevel)
    {
        if (cardHost == null) return;
        ink.canvasRenderer.SetAlpha(Mathf.Clamp01(inkLevel));
        blood.canvasRenderer.SetAlpha(Mathf.Clamp01(bloodLevel));
        if (!still && startedAt < 0f) return;
        float since = still ? 99f : now - startedAt, word = still ? 99f : now - wordAt;
        shown = Mathf.Clamp01(shown);
        // A shorter screen draws the card smaller about the middle (canvas units; the scaler matches width and height).
        var canvasRect = (RectTransform)cardHost.transform;
        float scale = Mathf.Clamp((canvasRect.rect.height * .5f - TopRoom) / TitleReach, LeastScale, 1f);
        card.localScale = new Vector3(scale, scale, 1f);

        // The slash draws out and the word slams in from large, its letters closing up.
        titlePlate.Reveal = KitReveal.EaseOut(Mathf.Clamp01((word - SlashFrom) / SlashFor));
        float slam = Mathf.Clamp01((word - WordFrom) / WordFor);
        titleGroup.alpha = shown * Mathf.Clamp01(slam * 3f);
        titleRect.localScale = Vector3.one * Mathf.LerpUnclamped(1.22f, 1f, Back(slam));
        titleText.characterSpacing = Mathf.Lerp(SpacingFrom, SpacingTo, KitReveal.EaseOut(slam));
        // Leaving, the word rises a little and the numbers sink: the card opens up as it goes.
        float away = 1f - shown;
        titleRect.anchoredPosition = TitleAt + new Vector2(0f, 10f * away);

        float cause = KitReveal.EaseOut(Mathf.Clamp01((since - CauseFrom) / CauseFor));
        causeGroup.alpha = shown * cause;
        causeRect.anchoredPosition = CauseAt + new Vector2(40f * (1f - cause), 6f * away);

        for (int i = 0; i < 3; i++)
        {
            float pop = Mathf.Clamp01((since - TileFrom - TileGap * i) / TileFor);
            tileGroups[i].alpha = shown * KitReveal.EaseOut(pop);
            tileRects[i].localScale = Vector3.one * Mathf.Lerp(.88f, 1f, KitReveal.EaseOut(pop));
            tileRects[i].anchoredPosition = new Vector2(TileX[i], TileY - 10f * away);
            float count = KitReveal.EaseOut(Mathf.Clamp01((since - TileFrom - TileGap * i) / CountFor));
            values[i].text = i == 0 ? Clock(facts.LastedSeconds * count)
                : i == 1 ? Mathf.RoundToInt(facts.Kills * count).ToString()
                : Wallet.Format((long)System.Math.Round(facts.Money * (double)count));
        }

        float line = KitReveal.EaseOut(Mathf.Clamp01((since - NextFrom) / NextFor));
        nextGroup.alpha = shown * line;
        nextRect.anchoredPosition = NextAt + new Vector2(0f, -10f * away);
        if (!string.IsNullOrEmpty(next) && nextText.text != next) nextText.text = next;
        bool hasFill = fill >= 0f;
        hairline.enabled = hasFill;
        if (hasFill) hairline.rectTransform.sizeDelta = new Vector2(HairlineWidth * Mathf.Clamp01(fill), 2f);
    }

    /// <summary>m:ss, the way a run's time reads.</summary>
    public static string Clock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return (whole / 60) + ":" + (whole % 60).ToString("00");
    }

    private void Show(bool on)
    {
        if (cardHost != null && cardHost.activeSelf != on) cardHost.SetActive(on);
        if (filmHost != null && filmHost.activeSelf != on) filmHost.SetActive(on);
    }

    // An overshoot that settles: the slam.
    private static float Back(float t)
    {
        const float s = 1.6f;
        t -= 1f;
        return t * t * ((s + 1f) * t + s) + 1f;
    }

    private DeathFilmGraphic Film(string name, DeathFilmGraphic.Layer layer, int seed)
    {
        var rect = KitStyle.Stretch(name, filmHost.transform);
        var film = rect.gameObject.AddComponent<DeathFilmGraphic>();
        film.Configure(layer, seed);
        film.canvasRenderer.SetAlpha(0f);
        return film;
    }

    private RectTransform Piece(string name, Vector2 at, Vector2 size, float lean, out CanvasGroup group)
    {
        var rect = FeelUI.Rect(name, card);
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, lean);
        group = rect.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        group.alpha = 0f;
        return rect;
    }

    private void BuildTitle()
    {
        titleRect = Piece("Death Title", TitleAt, TitleSize, TitleLean, out titleGroup);
        titlePlate = titleRect.gameObject.AddComponent<DeathCardGraphic>();
        titlePlate.Configure(DeathCardGraphic.Shape.Title, TitleSlant, .94f);
        var box = KitStyle.Stretch("Word", titleRect);
        // Over the plate's slant: the word sits in its middle, a touch right to follow the lean.
        box.offsetMin = new Vector2(TitleSlant * .5f, 4f);
        box.offsetMax = new Vector2(TitleSlant * .5f, 0f);
        titleText = KitStyle.Flat(box, "", WordType, TextAlignmentOptions.Center, KitStyle.Ice);
        titleText.characterSpacing = SpacingTo;
    }

    private void BuildCause()
    {
        causeRect = Piece("Death Cause", CauseAt, CauseSize, CauseLean, out causeGroup);
        causeRect.gameObject.AddComponent<DeathCardGraphic>().Configure(DeathCardGraphic.Shape.Band, CauseSlant, .82f);
        var box = KitStyle.Stretch("Words", causeRect);
        box.offsetMin = new Vector2(34f, 0f);
        box.offsetMax = new Vector2(-24f, 0f);
        causeText = FeelUI.ScoreLabel("Text", box, CauseType, TextAlignmentOptions.MidlineLeft);
        var fit = causeText.rectTransform;
        fit.anchorMin = Vector2.zero;
        fit.anchorMax = Vector2.one;
        fit.offsetMin = fit.offsetMax = Vector2.zero;
        causeText.color = KitStyle.Ice;
        causeText.fontStyle = FontStyles.Bold;
        causeText.richText = true;
        // A long name shrinks to the band rather than running past it.
        causeText.enableAutoSizing = true;
        causeText.fontSizeMin = CauseType * .6f;
        causeText.fontSizeMax = CauseType;
    }

    private void BuildTile(int index)
    {
        var rect = Piece("Death Stat " + (index + 1), new Vector2(TileX[index], TileY), TileSize, TileLean[index], out tileGroups[index]);
        tileRects[index] = rect;
        rect.gameObject.AddComponent<DeathCardGraphic>().Configure(DeathCardGraphic.Shape.Tile, 0f, .86f);
        string label = Texts.Get(index == 0 ? "death.stat.lasted" : index == 1 ? "death.stat.kills" : "death.stat.money");
        var top = KitStyle.TopLeft("Label", rect, new Vector2(14f, 8f), new Vector2(TileSize.x - 28f, 22f));
        var caption = KitStyle.Label(top, label, LabelType, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        caption.fontStyle = FontStyles.Normal;
        var bottom = KitStyle.TopLeft("Value", rect, new Vector2(14f, 30f), new Vector2(TileSize.x - 28f, 40f));
        values[index] = KitStyle.Flat(bottom, "", ValueType, index == 2 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        if (index != 2) return;
        // The money's banknote stands at the plate's left, the number at its right (the user's money rule).
        var note = MoneyIcon.Make(bottom, 26f);
        note.rectTransform.anchorMin = note.rectTransform.anchorMax = new Vector2(0f, .5f);
        note.rectTransform.anchoredPosition = new Vector2(13f, 0f);
    }

    private void BuildNext()
    {
        nextRect = Piece("Death Next", NextAt, NextSize, NextLean, out nextGroup);
        nextText = FeelUI.ScoreLabel("Text", nextRect, NextType, TextAlignmentOptions.Center);
        var fit = nextText.rectTransform;
        fit.anchorMin = Vector2.zero;
        fit.anchorMax = Vector2.one;
        fit.offsetMin = fit.offsetMax = Vector2.zero;
        nextText.color = KitStyle.Ice;
        nextText.fontStyle = FontStyles.Bold;
        var line = FeelUI.Rect("Countdown", nextRect);
        line.anchorMin = line.anchorMax = new Vector2(.5f, 0f);
        line.anchoredPosition = new Vector2(0f, -4f);
        line.sizeDelta = new Vector2(HairlineWidth, 2f);
        hairline = KitStyle.Fill(line, KitStyle.Accent);
    }
}
