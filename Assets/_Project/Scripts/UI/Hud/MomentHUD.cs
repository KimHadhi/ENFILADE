using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The special moments' pieces on the screen. Both stay clear of the crosshair and of every other HUD piece.
/// <list type="bullet">
/// <item>The ultimate's notice, on every PC, whoever fired it. The user's main note on the pick page: "컷인 띠의 저
/// 깔끔함과 시인성이 좋다. 특히 왼쪽 아래에 궁극기 썼다고 나오는 게 팀원들한테도 뜨면 누가 궁극기 썼는지 다른 데 보고 있어도
/// 바로 알 것 같아서".
/// It is a near-black band leaning its class's own way, on the HUD's left plane, beside the teammates' cards. Its parts:
/// the class colour along its top (white for a frame); the class's sigil drawing itself in its emblem and stamping (the C
/// drafts' sigils); the ultimate's name; who fired it, in their seat colour; the HUD's pink shard past its tip.
/// Each class ends its band its own way, so two never read as one template: a blade's point, rank bars, a rangefinder's
/// ticks, twin cuts, a heartbeat, a nested diamond, hazard stripes.
/// It slides in, holds long enough to read and slides out; a new one takes the old one's place.</item>
/// <item>The small tag over the class chip, on this player's own screen: CHARGE, DUEL, REVERSAL, GUN KATA. It is a
/// hairline drawn out from the middle, the word, and a line under it in the class colour (the A drafts). The kata's tag
/// stamps its own sigil beside the word.</item>
/// </list>
/// Real time. Numbers: Tables/MomentFx.csv, rows Notice and Tag.
/// </summary>
[DisallowMultipleComponent]
public sealed class MomentHUD : MonoBehaviour, IHudPreview
{
    private const string NoticeRow = "Notice", TagRow = "Tag";

    private static MomentHUD instance;

    // The notice's pieces.
    private RectTransform plane, band;
    private BandGraphic plate;
    private SigilGraphic emblem;
    private TextMeshProUGUI title, who;
    private Image seat;
    private float noticeAge = -1f, noticeHold = 1.2f;
    // The tag's pieces.
    private RectTransform tagRoot;
    private Image line, lineGlow;
    private SigilGraphic tagSigil;
    private TextMeshProUGUI tagTitle, tagSub;
    private float tagAge = -1f;
    private bool previewing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private static MomentHUD Instance
    {
        get
        {
            if (instance != null) return instance;
            var host = new GameObject("Moment HUD");
            instance = host.AddComponent<MomentHUD>();
            instance.Build();
            return instance;
        }
    }

    // ---- What the moments call ----

    /// <summary>
    /// <paramref name="who"/> fired their ultimate (this PC's player, or a friend's as their PC said it): the notice, with
    /// their class's colour, sigil and band, their name and the ultimate's.
    /// </summary>
    public static void Notice(PlayerMovement who, string classId)
    {
        if (!Application.isPlaying || !MomentLook.On(NoticeRow)) return;
        string name = who != null && who.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName
            : who != null ? who.name : "";
        Color seatColour = who != null && who.TryGetComponent(out PlayerTally seatTally) ? seatTally.Color : Color.white;
        Instance.ShowNotice(name, seatColour, classId);
    }

    /// <summary>The small tag over the class chip on this screen: a word, a line under it, the moment's colour.</summary>
    public static void Tag(string word, string under, Color colour, MomentLook.Sigil? sigil = null)
    {
        if (!Application.isPlaying || !MomentLook.On(TagRow)) return;
        Instance.ShowTag(word, under, colour, sigil);
    }

    /// <summary>This player's CHARGE: its tag, with how long and how far (pick A).</summary>
    public static void Charge(float seconds, float reach)
        => Tag(MomentLook.Text(TagRow, "charge.word", "CHARGE"),
            string.Format(MomentLook.Text(TagRow, "charge.under", "돌격 · {0:0.0}s · 둘레 {1:0}m"), seconds, reach), MomentLook.ColorOf(PlayerClass.Assault));

    /// <summary>This player's duel called: its tag (pick A).</summary>
    public static void Duel(float seconds)
        => Tag(MomentLook.Text(TagRow, "duel.word", "DUEL"),
            string.Format(MomentLook.Text(TagRow, "duel.under", "결투 · {0:0.0}s · 1 : 1"), seconds), PingMarks.BountyColor);

    /// <summary>This player's reversal: its tag (pick A).</summary>
    public static void Reversal()
        => Tag(MomentLook.Text(TagRow, "reversal.word", "REVERSAL"), MomentLook.Text(TagRow, "reversal.under", "되치기 · 자세 무너짐"), KitStyle.Accent);

    /// <summary>This player's Gun Kata: its tag with the kata's own sigil stamped beside it (pick C).</summary>
    public static void Kata(float seconds)
        => Tag(MomentLook.Text(TagRow, "kata.word", "GUN KATA"),
            string.Format(MomentLook.Text(TagRow, "kata.under", "건 카타 · {0:0.0}s · 꺼내면 가득"), seconds), MomentLook.ColorOf(PlayerClass.Point), MomentLook.Sigil.Kata);

    // ---- Building ----

    private void Build()
    {
        // The notice rides the HUD's left plane, as the teammates' cards do (TeamHealthPanel), so it leans with the HUD.
        var noticeHost = new GameObject("Moment Notice HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        noticeHost.transform.SetParent(transform, false);
        FeelUI.SetupHud(noticeHost, 26);
        plane = HudPlane.Create("Left Plane", noticeHost.transform, HudPlane.Side.Left);
        band = FeelUI.Rect("Moment Notice", plane);
        band.anchorMin = band.anchorMax = Vector2.zero;
        band.pivot = Vector2.zero;
        plate = band.gameObject.AddComponent<BandGraphic>();
        plate.raycastTarget = false;
        var emblemRect = FeelUI.Rect("Emblem", band);
        emblemRect.anchorMin = emblemRect.anchorMax = emblemRect.pivot = Vector2.zero;
        emblem = emblemRect.gameObject.AddComponent<SigilGraphic>();
        emblem.raycastTarget = false;
        title = FeelUI.ScoreLabel("Title", band, 27f, TextAlignmentOptions.BottomLeft);
        title.fontStyle = FontStyles.Bold | FontStyles.Italic;
        title.characterSpacing = 4f;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Overflow;
        title.raycastTarget = false;
        who = FeelUI.ScoreLabel("Who", band, 13f, TextAlignmentOptions.BottomLeft);
        who.characterSpacing = 2f;
        who.textWrappingMode = TextWrappingModes.NoWrap;
        who.raycastTarget = false;
        var seatRect = FeelUI.Rect("Seat", band);
        seatRect.anchorMin = seatRect.anchorMax = seatRect.pivot = Vector2.zero;
        seat = seatRect.gameObject.AddComponent<Image>();
        seat.raycastTarget = false;
        band.gameObject.SetActive(false);

        // The tag is flat over the class chip, as the chip is (ClassHUD).
        var tagHost = new GameObject("Moment Tag HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        tagHost.transform.SetParent(transform, false);
        FeelUI.SetupOverlay(tagHost, 77);
        var scaler = tagHost.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        tagRoot = FeelUI.Rect("Moment Tag", tagHost.transform);
        tagRoot.anchorMin = tagRoot.anchorMax = tagRoot.pivot = new Vector2(.5f, 0f);
        tagRoot.sizeDelta = new Vector2(420f, 52f);
        lineGlow = Bar("Line glow", tagRoot, 6f);
        line = Bar("Line", tagRoot, 1.6f);
        tagTitle = FeelUI.ScoreLabel("Word", tagRoot, 24f, TextAlignmentOptions.Bottom);
        tagTitle.characterSpacing = 9f;
        tagTitle.fontStyle = FontStyles.Bold;
        tagTitle.textWrappingMode = TextWrappingModes.NoWrap;
        tagTitle.raycastTarget = false;
        Place(tagTitle.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 16f), new Vector2(420f, 30f));
        tagSub = FeelUI.ScoreLabel("Under", tagRoot, 13f, TextAlignmentOptions.Top);
        tagSub.characterSpacing = 3f;
        tagSub.textWrappingMode = TextWrappingModes.NoWrap;
        tagSub.raycastTarget = false;
        Place(tagSub.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, -4f), new Vector2(420f, 18f));
        var sigilRect = FeelUI.Rect("Sigil", tagRoot);
        sigilRect.anchorMin = sigilRect.anchorMax = sigilRect.pivot = new Vector2(.5f, 0f);
        sigilRect.sizeDelta = new Vector2(40f, 40f);
        tagSigil = sigilRect.gameObject.AddComponent<SigilGraphic>();
        tagSigil.raycastTarget = false;
        tagSigil.Widths(1.8f, 6f);
        tagRoot.gameObject.SetActive(false);
    }

    private static Image Bar(string name, Transform parent, float height)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 12f);
        rect.sizeDelta = new Vector2(0f, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static void Place(RectTransform rect, Vector2 pivot, Vector2 at, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = pivot;
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
    }

    // ---- The notice ----

    private void ShowNotice(string name, Color seatColour, string classId)
    {
        Color colour = MomentLook.ColorOf(classId);
        float width = MomentLook.Number(NoticeRow, "width", 470f), height = MomentLook.Number(NoticeRow, "height", 52f);
        band.sizeDelta = new Vector2(width, height);
        plate.Shape(width, height, MomentLook.Number(NoticeRow, "slant", 18f), BandGraphic.CapOf(classId), colour);
        float block = height - 12f;
        var emblemRect = emblem.rectTransform;
        emblemRect.anchoredPosition = new Vector2(9f, 5f);
        emblemRect.sizeDelta = new Vector2(block, block);
        emblem.Sigil = MomentLook.SigilOf(classId);
        emblem.Tint = colour;
        emblem.Widths(2f, 6.5f);
        emblem.Progress = 0f;
        emblem.Ring = -1f;
        title.text = MomentLook.UltTitle(classId);
        title.color = KitStyle.Ice;
        Place(title.rectTransform, Vector2.zero, new Vector2(block + 22f, height * .38f), new Vector2(width - block - 60f, height * .6f));
        who.text = (string.IsNullOrEmpty(name) ? "" : name + "  ·  ") + MomentLook.UltWhat(classId);
        who.color = colour;
        Place(who.rectTransform, Vector2.zero, new Vector2(block + 34f, 5f), new Vector2(width - block - 70f, height * .36f));
        Place((RectTransform)seat.transform, Vector2.zero, new Vector2(block + 23f, 8f), new Vector2(4f, height * .26f));
        seat.color = seatColour;
        noticeHold = MomentLook.Number(NoticeRow, "hold", 1.2f);
        noticeAge = 0f;
        band.gameObject.SetActive(true);
        PoseNotice();
    }

    private void PoseNotice()
    {
        float inSeconds = MomentLook.Number(NoticeRow, "in", .09f), outSeconds = MomentLook.Number(NoticeRow, "out", .14f);
        float left = MomentLook.Number(NoticeRow, "left", 252f), bottom = MomentLook.Number(NoticeRow, "bottom", 292f);
        float width = band.sizeDelta.x;
        float slideIn = 1f - Mathf.Pow(1f - Mathf.Clamp01(noticeAge / inSeconds), 3f);
        float slideOut = Mathf.Pow(Mathf.Clamp01((noticeAge - inSeconds - noticeHold) / outSeconds), 3f);
        float hidden = -width - 90f;
        band.anchoredPosition = new Vector2(Mathf.Lerp(hidden, left, slideIn) + (hidden - left) * slideOut, bottom);
        float tilt = GameSettings.Current.hudTilt;
        band.localRotation = Quaternion.Euler(0f, -MomentLook.Number(NoticeRow, "yaw", 8f) * tilt, plate.Lean * tilt);
        plate.Flash = 1f - Mathf.Clamp01(noticeAge / .08f);
        // The sigil draws itself in the emblem, then is stamped.
        float drawFrom = .03f, drawFor = Mathf.Max(.05f, MomentLook.Number(NoticeRow, "draw", .22f));
        emblem.Progress = Mathf.Clamp01((noticeAge - drawFrom) / drawFor);
        float stamp = noticeAge - drawFrom - drawFor;
        emblem.Ring = stamp >= 0f && stamp < .16f ? stamp / .16f : -1f;
        float scale = stamp >= 0f && stamp < .1f ? Mathf.Lerp(1.15f, 1f, stamp / .1f) : 1f;
        emblem.rectTransform.localScale = Vector3.one * scale;
        if (noticeAge > inSeconds + noticeHold + outSeconds && !previewing) { band.gameObject.SetActive(false); noticeAge = -1f; }
    }

    // ---- The tag ----

    private void ShowTag(string word, string under, Color colour, MomentLook.Sigil? sigil)
    {
        tagTitle.text = word;
        tagTitle.color = KitStyle.Ice;
        tagSub.text = under;
        tagSub.color = colour;
        line.color = Color.Lerp(colour, Color.white, .6f);
        lineGlow.color = KitStyle.With(colour, .3f);
        tagSigil.gameObject.SetActive(sigil.HasValue);
        if (sigil.HasValue)
        {
            tagSigil.Sigil = sigil.Value;
            tagSigil.Tint = colour;
            tagSigil.Progress = 0f;
            // Left of the word, as wide as the word is set.
            float wordWidth = Mathf.Min(380f, tagTitle.GetPreferredValues(word).x);
            tagSigil.rectTransform.anchoredPosition = new Vector2(-wordWidth * .5f - 34f, 14f);
        }
        tagAge = 0f;
        tagRoot.gameObject.SetActive(true);
        PoseTag();
    }

    private void PoseTag()
    {
        float seconds = MomentLook.Number(TagRow, "seconds", .9f);
        tagRoot.anchoredPosition = new Vector2(0f, MomentLook.Number(TagRow, "bottom", 196f));
        float tilt = GameSettings.Current.hudTilt;
        tagRoot.localRotation = Quaternion.Euler(0f, 0f, MomentLook.Number(TagRow, "lean", 2f) * tilt);
        float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(tagAge / .12f), 3f);
        float back = Mathf.Pow(Mathf.Clamp01((tagAge - (seconds - .16f)) / .14f), 3f);
        float half = MomentLook.Number(TagRow, "line", 176f) * grow * (1f - back);
        line.rectTransform.sizeDelta = new Vector2(half * 2f, 1.6f);
        lineGlow.rectTransform.sizeDelta = new Vector2(half * 2f + 6f, 6f);
        float words = Mathf.Clamp01((tagAge - .03f) / .05f) * (1f - Mathf.Clamp01((tagAge - (seconds - .18f)) / .12f));
        float rise = (1f - Mathf.Pow(1f - Mathf.Clamp01((tagAge - .03f) / .12f), 3f));
        tagTitle.alpha = words;
        tagSub.alpha = words;
        tagTitle.rectTransform.anchoredPosition = new Vector2(0f, 16f + (1f - rise) * 10f);
        if (tagSigil.gameObject.activeSelf)
        {
            tagSigil.Progress = Mathf.Clamp01((tagAge - .02f) / .2f);
            float stamp = tagAge - .22f;
            tagSigil.Ring = stamp >= 0f && stamp < .16f ? stamp / .16f : -1f;
            tagSigil.color = new Color(1f, 1f, 1f, words);
        }
        if (tagAge > seconds && !previewing) { tagRoot.gameObject.SetActive(false); tagAge = -1f; }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (noticeAge >= 0f && !previewing) { noticeAge += dt; PoseNotice(); }
        if (tagAge >= 0f && !previewing) { tagAge += dt; PoseTag(); }
    }

    /// <summary>Tools → HUD → 겹침 검사: the notice and the tag held where they rest, with sample words.</summary>
    public void Preview(bool show)
    {
        previewing = show;
        if (show)
        {
            ShowNotice("PLAYER", Color.white, PlayerClass.Knight);
            noticeAge = MomentLook.Number(NoticeRow, "in", .09f) + .3f;
            PoseNotice();
            ShowTag("GUN KATA", "건 카타 · 8.0s · 꺼내면 가득", MomentLook.ColorOf(PlayerClass.Point), MomentLook.Sigil.Kata);
            tagAge = .45f;
            PoseTag();
        }
        else
        {
            band.gameObject.SetActive(false); noticeAge = -1f;
            tagRoot.gameObject.SetActive(false); tagAge = -1f;
        }
    }
}
