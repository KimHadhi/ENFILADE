using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional visual guide to the balance beam's actual footsteps: A blue on the left, D pink on the right. Each ring
/// previews its next foot placement well before descent and meets its circle when that foot lands. Press the matching key on that contact; the
/// note due next stands out. PERFECT!, GOOD or MISS pops up above, and a dot below turns red for each miss left to answer
/// for. Players can read the feet directly by looking down, or use these circles as a guide.
/// Numbers: Player.csv row BalanceGauge.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(270)]
public sealed class BalanceGaugeHUD : MonoBehaviour
{
    [SerializeField, Min(10f), Tooltip("Diameter of a circle (pixels at 1080p).")]
    private float circleSize = 76f;
    [SerializeField, Min(0f), Tooltip("Gap between the two circles (pixels at 1080p).")]
    private float gap = 70f;
    [SerializeField, Min(1f), Tooltip("How many times a circle's size its ring starts at.")]
    private float approachScale = 2.4f;
    [SerializeField, Tooltip("Height of the circles from the middle of the screen (pixels at 1080p).")]
    private float height = -150f;
    [SerializeField, Range(0f, 1f), Tooltip("Opacity of the notes.")]
    private float opacity = .95f;
    [SerializeField, Range(0f, 1f), Tooltip("Opacity of the circle that isn't due next, as a share of the other's.")]
    private float restOpacity = .45f;
    [SerializeField, Min(.01f), Tooltip("How long the notes take to fade in or out (s).")]
    private float fadeTime = .12f;
    [SerializeField, Range(0f, 1f), Tooltip("Strength of key and judgment color flashes. The timing target never changes size.")]
    private float flashStrength = .65f;
    [SerializeField, Min(.01f), Tooltip("How long key and judgment color flashes last (s).")]
    private float flashTime = .12f;
    [SerializeField, Min(.05f), Tooltip("How long PERFECT!, GOOD or MISS stays up (s).")]
    private float judgementTime = .5f;

    private const float DotSize = 9f, DotGap = 7f, JudgementFontSize = 34f;
    private static readonly Color LeftKey = new Color(.3f, .65f, 1f), RightKey = new Color(1f, .42f, .6f);
    private static readonly Color Perfect = new Color(1f, .85f, .3f), Good = new Color(.5f, 1f, .6f), Miss = new Color(1f, .25f, .18f);
    private static readonly Color DotRest = new Color(1f, 1f, 1f, .35f);

    private sealed class Note
    {
        public int Side;
        public Color Key;
        public Image Disc, Rim, Ring;
        public float KeyFlash, JudgementFlash;
        public Color JudgementColor;
    }

    private readonly Note[] notes = new Note[2];
    private PlayerMovement player;
    private CanvasGroup group;
    private Image[] dots;
    private TextMeshProUGUI judgementText;
    private float judgementLeft;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnEnable()
    {
        if (player != null)
        {
            player.BalanceBeat += OnBalanceBeat;
            player.BalanceKeyPressed += OnBalanceKeyPressed;
        }
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.BalanceBeat -= OnBalanceBeat;
            player.BalanceKeyPressed -= OnBalanceKeyPressed;
        }
        if (group != null) group.alpha = 0f;
    }

    private void OnDestroy() { if (group != null) Destroy(group.gameObject); }

    private void OnBalanceKeyPressed(int side)
    {
        if (side == 0) return;
        EnsureGauge();
        notes[side < 0 ? 0 : 1].KeyFlash = 1f;
    }

    private void OnBalanceBeat(BalanceJudgement judgement)
    {
        EnsureGauge();
        var note = notes[player.Abilities.Fixtures.BalanceNoteSide < 0 ? 0 : 1];
        note.JudgementFlash = 1f;
        note.JudgementColor = judgement == BalanceJudgement.Perfect ? Perfect : judgement == BalanceJudgement.Good ? Good : Miss;
        judgementLeft = judgementTime;
        judgementText.text = judgement == BalanceJudgement.Perfect ? "PERFECT!" : judgement == BalanceJudgement.Good ? "GOOD" : "MISS";
        judgementText.color = note.JudgementColor;
    }

    private void LateUpdate()
    {
        if (player == null || !player.IsInitialized) return;
        var fixtures = player.Abilities.Fixtures;
        bool balancing = fixtures.BalanceActive;
        if (!balancing && group == null) return;
        EnsureGauge();
        float dt = Time.unscaledDeltaTime;
        group.alpha = Mathf.MoveTowards(group.alpha, balancing ? opacity : 0f, dt / fadeTime);
        // Off the beam it all fades with the group and is gone for good: the next beam starts clean.
        if (!balancing)
        {
            judgementLeft = 0f;
            foreach (var note in notes) note.KeyFlash = note.JudgementFlash = 0f;
            return;
        }
        foreach (var note in notes)
        {
            note.KeyFlash = Mathf.MoveTowards(note.KeyFlash, 0f, dt / flashTime);
            note.JudgementFlash = Mathf.MoveTowards(note.JudgementFlash, 0f, dt / flashTime);
        }
        judgementLeft = Mathf.Max(0f, judgementLeft - dt);

        Show(notes[0], fixtures.BalanceLeftRing, fixtures.BalanceNoteSide);
        Show(notes[1], fixtures.BalanceRightRing, fixtures.BalanceNoteSide);

        float left = Mathf.Clamp01(judgementLeft / judgementTime);
        var label = judgementText.rectTransform;
        label.anchoredPosition = new Vector2(0f, height + circleSize * .5f + 26f);
        label.localScale = Vector3.one;
        judgementText.alpha = Mathf.Clamp01(left * 2.5f);

        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].enabled = i < fixtures.BalanceSlipLimit;
            float x = (i - (fixtures.BalanceSlipLimit - 1) * .5f) * (DotSize + DotGap);
            dots[i].rectTransform.sizeDelta = Vector2.one * DotSize;
            dots[i].rectTransform.anchoredPosition = new Vector2(x, height - circleSize * .5f - 20f);
            dots[i].color = i < fixtures.BalanceSlips ? Miss : DotRest;
        }
    }

    // 0 is the exact judgement target. A late ring keeps shrinking inside instead of sticking on the rim.
    private void Show(Note note, float ring, int dueSide)
    {
        float x = note.Side * (circleSize + gap) * .5f;
        // Judging a note must never move the target that the next approach ring is closing onto.
        Place(note.Disc, circleSize, x);
        Place(note.Rim, circleSize, x);
        Color disc = note.Key;
        disc.a = note.Side == dueSide ? 1f : restOpacity;
        // Raw key edges light their own channel immediately, including buffered early presses and overlapping keys.
        disc.a = Mathf.Max(disc.a, note.KeyFlash);
        Color pressed = Color.Lerp(disc, Color.white, note.KeyFlash * flashStrength * .25f);
        pressed.a = disc.a;
        note.Disc.color = Color.Lerp(pressed, note.JudgementColor, note.JudgementFlash * flashStrength);
        Color rim = Color.Lerp(Color.white, note.Key, note.KeyFlash * flashStrength);
        rim.a = disc.a;
        note.Rim.color = rim;
        Place(note.Ring, circleSize * Mathf.Max(.1f, Mathf.LerpUnclamped(1f, approachScale, ring)), x);
        Color line = note.Key;
        line.a = ring < 1f ? 1f : 0f;
        note.Ring.color = line;
    }

    private void Place(Image image, float size, float x)
    {
        image.rectTransform.sizeDelta = Vector2.one * size;
        image.rectTransform.anchoredPosition = new Vector2(x, height);
    }

    private void EnsureGauge()
    {
        if (group != null) return;
        var root = new GameObject("Balance notes", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = group.blocksRaycasts = false;
        notes[0] = NewNote(root.transform, -1, "A", LeftKey);
        notes[1] = NewNote(root.transform, 1, "D", RightKey);
        judgementText = Text("Judgement", root.transform);
        judgementText.fontStyle = FontStyles.Bold | FontStyles.Italic;
        judgementText.fontSize = JudgementFontSize;
        judgementText.alpha = 0f;
        judgementText.rectTransform.sizeDelta = new Vector2(300f, 50f);
        dots = new Image[Mathf.Max(player.AbilitySettings.balanceSlipLimit, player.AbilitySettings.balanceRunSlipLimit)];
        for (int i = 0; i < dots.Length; i++) dots[i] = Part("Miss", root.transform, CombatUIAssets.Disc);
    }

    private Note NewNote(Transform parent, int side, string letter, Color key)
    {
        var note = new Note
        {
            Side = side, Key = key,
            Disc = Part(letter + " circle", parent, CombatUIAssets.Disc),
            Rim = Part(letter + " rim", parent, CombatUIAssets.Ring),
            Ring = Part(letter + " ring", parent, CombatUIAssets.Ring)
        };
        var text = Text("Key", note.Disc.transform);
        text.text = letter;
        text.fontStyle = FontStyles.Bold;
        text.fontSize = circleSize * .55f;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        return note;
    }

    private static Image Part(string partName, Transform parent, Sprite sprite)
    {
        var part = new GameObject(partName, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        part.transform.SetParent(parent, false);
        part.raycastTarget = false;
        part.sprite = sprite;
        return part;
    }

    private static TextMeshProUGUI Text(string partName, Transform parent)
    {
        var text = new GameObject(partName, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.raycastTarget = false;
        text.font = CombatUIAssets.ResolveFont(null);
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        return text;
    }
}
