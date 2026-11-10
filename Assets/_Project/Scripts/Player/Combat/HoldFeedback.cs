using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What being held looks and sounds like on the held player's own screen (user, 2026-11-10: a shambler's grab went by
/// unnoticed, and a runner's pin showed nothing but the fall). As a hold begins the grab cue sounds, lower for a pin
/// (CombatCueAudio's Grab). While it lasts the screen's edges close in dark red, further for each zombie holding and
/// further still as a pin runs out, with a throb at every grab and bite; and a line under the crosshair says what has
/// them and what gets them out: 잡혔다 with the shove key, 뜯어먹히는 중 lying under one that eats them, 깔렸다 with a bar
/// that fills as the runner's bite closes on the kill. Made by PlayerHold on first need, so this PC's player only.
/// Numbers: Tables/Player.csv, row HoldFeedback.
/// </summary>
[DisallowMultipleComponent]
public sealed class HoldFeedback : MonoBehaviour
{
    [SerializeField, Range(0f, 1f), Tooltip("How far the edges close in with one shambler holding (0-1).")]
    private float grabEdge = .45f;
    [SerializeField, Range(0f, 1f), Tooltip("Added for each further zombie holding.")]
    private float perHolder = .12f;
    [SerializeField, Range(0f, 1f), Tooltip("Pinned by a runner, where the edges start; they close the rest of the way as the pin runs out.")]
    private float pinEdge = .6f;
    [SerializeField, Range(0f, 1f), Tooltip("The throb added at a grab and at each bite.")]
    private float throb = .3f;
    [SerializeField, Min(.05f), Tooltip("How long a throb takes to fade (real s).")]
    private float throbSeconds = .4f;
    [SerializeField, Tooltip("The colour the edges close in with.")]
    private Color edgeColor = new Color(.5f, .02f, .05f);
    [SerializeField, Range(.3f, 1.5f), Tooltip("Pitch of the grab cue as a shambler takes hold.")]
    private float grabPitch = .8f;
    [SerializeField, Range(.3f, 1.5f), Tooltip("Pitch of the grab cue as a runner pins them.")]
    private float pinPitch = .6f;

    // In and out (real s), the prompt's place above the screen's bottom (px at 1080p) and the pin bar's size.
    private const float FadeIn = .12f, FadeOut = .3f, PromptUp = 300f, BarWidth = 320f, BarHeight = 6f;
    private static readonly Color Danger = new Color(1f, .3f, .28f);
    private static Texture2D edgeTexture;

    private PlayerHold hold;
    private PlayerDamageReceiver receiver;
    private GameObject host;
    private CanvasGroup group;
    private RawImage edge;
    private TextMeshProUGUI title, hint;
    private RectTransform prompt;
    private Image bar, barBack;
    private float shown, beat, progress;
    private int holders = 1;
    private bool pinned, eaten, fights, bursts, written;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => edgeTexture = null;

    private void Awake()
    {
        hold = GetComponent<PlayerHold>();
        receiver = GetComponent<PlayerDamageReceiver>();
    }

    /// <summary>A zombie took hold just now (PlayerHold): the cue and a throb.</summary>
    internal void Began(bool pin)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        CombatCueAudio.Play(gameObject, CombatCue.Grab, pin ? pinPitch : grabPitch);
        beat = 1f;
    }

    /// <summary>A bite landed (PlayerHold): a smaller throb.</summary>
    internal void Bitten() => beat = Mathf.Max(beat, .6f);

    private void LateUpdate()
    {
        bool held = hold != null && hold.IsHeld && receiver != null && receiver.IsAlive;
        if (!held && shown <= 0f)
        {
            if (host != null && host.activeSelf) host.SetActive(false);
            return;
        }
        if (host == null) Build();
        if (!host.activeSelf) host.SetActive(true);
        float dt = Time.unscaledDeltaTime;
        shown = Mathf.MoveTowards(shown, held ? 1f : 0f, dt / (held ? FadeIn : FadeOut));
        beat = Mathf.MoveTowards(beat, 0f, dt / throbSeconds);
        // What holds them is read while it does; fading out, the last of it stays on show.
        if (held)
        {
            bool nowPinned = hold.IsPinned, nowEaten = hold.IsEaten, nowFights = hold.FightsPinned, nowBursts = hold.CanBurstShield;
            int nowHolders = Mathf.Max(1, hold.Holders);
            // A label made in code has no text at all until it is given some (null, not ""), so what was written is
            // remembered here and not read back from the label.
            if (!written || nowPinned != pinned || nowEaten != eaten || nowFights != fights || nowHolders != holders || nowBursts != bursts)
            {
                pinned = nowPinned; eaten = nowEaten; fights = nowFights; holders = nowHolders; bursts = nowBursts;
                Write();
                written = true;
            }
            progress = hold.PinProgress;
        }
        float closed = pinned ? Mathf.Lerp(pinEdge, 1f, progress) : Mathf.Clamp01(grabEdge + perHolder * (holders - 1));
        var tint = edgeColor;
        tint.a = Mathf.Clamp01(closed + throb * beat * beat);
        edge.color = tint;
        group.alpha = shown;
        prompt.localScale = Vector3.one * (1f + .12f * beat * beat);
        barBack.gameObject.SetActive(pinned);
        if (pinned) bar.fillAmount = progress;
    }

    // What has them, and what gets them out of it (Texts.csv hold.*).
    private void Write()
    {
        string shove = Texts.Get("hold.shove");
        if (pinned)
        {
            title.text = Texts.Get("hold.pinned");
            // With shield left, V bursts it to throw the runner off (PlayerHold.TryBurstShield).
            hint.text = bursts ? Texts.Get("hold.pinned.burst") : fights ? shove : Texts.Get("hold.pinned.alone");
        }
        else
        {
            title.text = Texts.Get(eaten ? "hold.eaten" : "hold.grabbed") + (holders > 1 ? "  " + Texts.Format("hold.count", holders) : "");
            hint.text = shove;
        }
    }

    private void Build()
    {
        host = new GameObject("Hold Feedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        FeelUI.SetupOverlay(host, 24);
        group = host.GetComponent<CanvasGroup>();
        written = false;
        group.alpha = 0f;
        group.interactable = group.blocksRaycasts = false;

        var edgeRect = FeelUI.Rect("Edge", host.transform);
        edgeRect.anchorMin = Vector2.zero;
        edgeRect.anchorMax = Vector2.one;
        edge = edgeRect.gameObject.AddComponent<RawImage>();
        edge.texture = EdgeTexture();
        edge.raycastTarget = false;

        prompt = FeelUI.Rect("Prompt", host.transform);
        prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, 0f);
        prompt.anchoredPosition = new Vector2(0f, PromptUp);
        title = FeelUI.Label("Title", prompt, 42f, TextAlignmentOptions.Center);
        title.rectTransform.sizeDelta = new Vector2(900f, 52f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        title.color = Danger;
        title.raycastTarget = false;
        hint = FeelUI.Label("Hint", prompt, 21f, TextAlignmentOptions.Center);
        hint.rectTransform.sizeDelta = new Vector2(900f, 28f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, -16f);
        hint.color = Color.white;
        hint.raycastTarget = false;

        // The pin's bar: how near the runner's bite is to the kill.
        var back = FeelUI.Rect("Pin Bar", prompt);
        back.sizeDelta = new Vector2(BarWidth, BarHeight);
        back.anchoredPosition = new Vector2(0f, -44f);
        barBack = back.gameObject.AddComponent<Image>();
        barBack.color = new Color(0f, 0f, 0f, .6f);
        barBack.raycastTarget = false;
        var fill = FeelUI.Rect("Fill", back);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        bar = fill.gameObject.AddComponent<Image>();
        bar.sprite = CombatUIAssets.Disc;
        bar.type = Image.Type.Filled;
        bar.fillMethod = Image.FillMethod.Horizontal;
        bar.fillOrigin = (int)Image.OriginHorizontal.Left;
        bar.fillAmount = 0f;
        bar.color = Danger;
        bar.raycastTarget = false;
        barBack.gameObject.SetActive(false);
    }

    // Clear in the middle, closing in toward the edges and most at the corners; stretched over the screen.
    private static Texture2D EdgeTexture()
    {
        if (edgeTexture != null) return edgeTexture;
        const int Size = 128;
        edgeTexture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "Hold Edge (Runtime)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float u = (x + .5f) / Size * 2f - 1f, v = (y + .5f) / Size * 2f - 1f;
            float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, 1.25f, Mathf.Sqrt(u * u + v * v)));
            pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(near * near * 255f));
        }
        edgeTexture.SetPixels32(pixels);
        edgeTexture.Apply(false, true);
        return edgeTexture;
    }

    private void OnDisable()
    {
        shown = beat = 0f;
        if (host != null) host.SetActive(false);
    }

    private void OnDestroy()
    {
        if (host != null) Destroy(host);
    }
}
