using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ENFILADE tower is through (the user: "ENFILADED!!라는 문구가 뜨면서 마치 젠레스 존 제로 WIPE OUT 연출처럼 멋있게
/// 끝내고"). Zenless Zone Zero stops a fight's last blow on a frozen frame tinted in one colour; here every PC freezes
/// what its own player saw at the tower's last kill, without the HUD, and grades it into the HUD's ink, pink and paper
/// with a halftone and rays (EnfiladedFreeze.shader), while a white flash goes off, black bars close in, a pink band
/// tears across at a lean and ENFILADED!! slams down onto it with a shake, its ink shadow and pink ghost settling behind
/// it; a dark plate under it says what was done. Held, then it fades for the closing card (TowerRun). Real time: the
/// world goes on under it. Built on first use.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnfiladedFinale : MonoBehaviour
{
    private const string ShaderPath = "Combat/Shaders/EnfiladedFreeze";
    // Real seconds: the flash, the grade coming in, the band's tear, the word's slam and shake, the plate, the fade.
    private const float FlashSeconds = .22f, GradeFrom = .03f, GradeSeconds = .2f, BandFrom = .05f, BandSeconds = .16f,
        WordFrom = .1f, SlamSeconds = .16f, ShakeSeconds = .38f, PlateFrom = .38f, PlateSeconds = .22f, OutSeconds = .45f;
    // Px at 1080p: the lean (degrees), the band's height, the word's size, the bars, the shake.
    private const float Lean = -8f, BandHeight = 250f, WordSize = 210f, BarHeight = 104f, Shake = 16f;
    private static EnfiladedFinale instance;

    private Canvas canvas;
    private CanvasGroup group;
    private RawImage frame;
    private Material grade;
    private Image flash, top, bottom, band, plate;
    private RectTransform slant, wordRoot;
    private TextMeshProUGUI word, shadow, ghost, title, line;
    private Texture2D shot;
    private float age = -1f, length = 3f;

    /// <summary>The tower is through: the frozen moment and ENFILADED!!, with <paramref name="heading"/> and
    /// <paramref name="under"/> on its plate, for <paramref name="seconds"/> (real s).</summary>
    public static void Play(string heading, string under, float seconds)
    {
        if (!Application.isPlaying) return;
        if (instance == null)
        {
            var host = new GameObject("ENFILADED", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<EnfiladedFinale>();
        }
        instance.Begin(heading, under, seconds);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        // Over everything, the closing card and the banners included.
        FeelUI.SetupOverlay(gameObject, 95);
        canvas = GetComponent<Canvas>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;

        frame = FeelUI.Rect("Frame", transform).gameObject.AddComponent<RawImage>();
        frame.raycastTarget = false;
        KitStyle.Stretch(frame.rectTransform);
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader != null)
        {
            grade = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            grade.SetFloat("_Low", .025f);
            grade.SetFloat("_High", .5f);
            frame.material = grade;
        }

        top = Bar("Top bar", true);
        bottom = Bar("Bottom bar", false);

        slant = FeelUI.Rect("Slant", transform);
        slant.localRotation = Quaternion.Euler(0f, 0f, Lean);
        band = FeelUI.Panel("Band", slant, KitStyle.Accent);
        band.rectTransform.sizeDelta = new Vector2(0f, BandHeight);
        var seam = FeelUI.Panel("Seam", band.rectTransform, KitStyle.Ink);
        seam.rectTransform.anchorMin = new Vector2(0f, 0f);
        seam.rectTransform.anchorMax = new Vector2(1f, 0f);
        seam.rectTransform.pivot = new Vector2(.5f, 0f);
        seam.rectTransform.sizeDelta = new Vector2(0f, 10f);
        seam.rectTransform.anchoredPosition = new Vector2(0f, 18f);

        wordRoot = FeelUI.Rect("Word", slant);
        wordRoot.sizeDelta = new Vector2(1800f, BandHeight);
        shadow = Text("Shadow", wordRoot, WordSize, KitStyle.Ink);
        ghost = Text("Ghost", wordRoot, WordSize, KitStyle.With(KitStyle.Cyan, .85f));
        word = Text("Letters", wordRoot, WordSize, KitStyle.Ice);
        shadow.text = ghost.text = word.text = Texts.Get("tower.finale.word");

        plate = FeelUI.Panel("Plate", slant, new Color(.024f, .024f, .031f, .94f));
        plate.rectTransform.pivot = new Vector2(0f, .5f);
        plate.rectTransform.sizeDelta = new Vector2(980f, 104f);
        plate.rectTransform.anchoredPosition = new Vector2(-470f, -BandHeight * .5f - 70f);
        KitSkew.On(plate, 24f);
        title = Text("Heading", plate.transform, 38f, KitStyle.Accent);
        title.fontStyle = FontStyles.Bold;
        title.rectTransform.sizeDelta = new Vector2(920f, 46f);
        title.rectTransform.anchoredPosition = new Vector2(490f, 20f);
        line = Text("Under", plate.transform, 25f, KitStyle.With(KitStyle.Ice, .8f));
        line.fontStyle = FontStyles.Bold;
        line.rectTransform.sizeDelta = new Vector2(920f, 34f);
        line.rectTransform.anchoredPosition = new Vector2(490f, -24f);

        flash = FeelUI.Panel("Flash", transform, Color.white);
        KitStyle.Stretch(flash.rectTransform);
        canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (shot != null) Destroy(shot);
        if (grade != null) Destroy(grade);
    }

    private void Begin(string heading, string under, float seconds)
    {
        title.text = heading ?? "";
        line.text = under ?? "";
        length = Mathf.Max(1.2f, seconds);
        age = -1f;
        // Its own canvas stays off while the frame is taken, so the shot has none of it.
        canvas.enabled = false;
        StopAllCoroutines();
        StartCoroutine(Grab());
    }

    // For one frame the HUD's camera and every screen canvas are off, so what is grabbed at the frame's end is the world
    // alone; then they come back and the frozen frame plays.
    private IEnumerator Grab()
    {
        var hidden = new List<Behaviour>();
        var hud = FindFirstObjectByType<HudCamera>();
        if (hud != null && hud.TryGetComponent(out Camera view) && view.enabled) { view.enabled = false; hidden.Add(view); }
        foreach (var other in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (other != null && other != canvas && other.enabled && other.isRootCanvas && other.renderMode != RenderMode.WorldSpace)
            {
                other.enabled = false;
                hidden.Add(other);
            }
        yield return new WaitForEndOfFrame();
        Texture2D taken = null;
        try { taken = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (System.Exception error) { Debug.LogWarning("[Tower] the finale's frame couldn't be taken: " + error.Message); }
        foreach (var part in hidden) if (part != null) part.enabled = true;
        Shown(taken);
    }

    // The frame is in (or none could be taken): it plays from here.
    private void Shown(Texture2D taken)
    {
        if (shot != null && shot != taken) Destroy(shot);
        shot = taken;
        frame.texture = shot;
        frame.color = shot != null ? Color.white : new Color(1f, 1f, 1f, .55f);
        age = 0f;
        canvas.enabled = true;
        Pose(0f);
        KitSound.Play(KitSound.Cue.Enfiladed);
    }

    private void LateUpdate()
    {
        if (age < 0f) return;
        age += Time.unscaledDeltaTime;
        if (age >= length)
        {
            age = -1f;
            canvas.enabled = false;
            if (shot != null) { Destroy(shot); shot = null; frame.texture = null; }
            return;
        }
        Pose(age);
    }

    private void Pose(float t)
    {
        float leave = Mathf.Clamp01((t - (length - OutSeconds)) / OutSeconds);
        group.alpha = 1f - leave;
        flash.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01(t / FlashSeconds));
        if (grade != null)
        {
            grade.SetFloat("_Mix", Ease.Out((t - GradeFrom) / GradeSeconds));
            grade.SetFloat("_Zoom", 1f + .07f * Mathf.Clamp01(t / length));
            grade.SetFloat("_Phase", t * .6f);
        }
        float bars = BarHeight * Ease.Out(t / .3f);
        top.rectTransform.sizeDelta = bottom.rectTransform.sizeDelta = new Vector2(0f, bars);

        // The band tears across from the left; the word slams down onto it as it does.
        float open = Ease.Out((t - BandFrom) / BandSeconds);
        band.rectTransform.sizeDelta = new Vector2(3200f * open, BandHeight);
        band.rectTransform.anchoredPosition = new Vector2(-1600f * (1f - open), 0f);

        float slam = Mathf.Clamp01((t - WordFrom) / SlamSeconds);
        bool shown = t >= WordFrom;
        word.enabled = shadow.enabled = ghost.enabled = shown;
        float scale = shown ? Mathf.Lerp(2.6f, 1f, Ease.Out(slam)) : 1f;
        float shaking = Mathf.Clamp01(1f - (t - WordFrom - SlamSeconds) / ShakeSeconds);
        Vector2 jolt = t >= WordFrom + SlamSeconds && shaking > 0f
            ? new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * Shake * shaking * shaking : Vector2.zero;
        wordRoot.localScale = new Vector3(scale, scale, 1f);
        wordRoot.anchoredPosition = jolt;
        word.alpha = Mathf.Clamp01((t - WordFrom) / .05f);
        // Behind the letters: the ink shadow set down and to the right, the ghost split off the other way and drawn back in.
        shadow.rectTransform.anchoredPosition = new Vector2(14f, -12f);
        float split = 1f - Ease.Out((t - WordFrom - SlamSeconds) / .4f);
        ghost.rectTransform.anchoredPosition = new Vector2(-10f - 34f * split, 8f + 16f * split);
        ghost.alpha = .85f * Mathf.Clamp01((t - WordFrom) / .05f);

        float under = Ease.Out((t - PlateFrom) / PlateSeconds);
        plate.rectTransform.localScale = new Vector3(under, 1f, 1f);
        title.alpha = line.alpha = Mathf.Clamp01((t - PlateFrom - .1f) / .2f);
    }

    private Image Bar(string name, bool up)
    {
        var bar = FeelUI.Panel(name, transform, KitStyle.Ink);
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, up ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, up ? 1f : 0f);
        rect.pivot = new Vector2(.5f, up ? 1f : 0f);
        rect.anchoredPosition = Vector2.zero;
        return bar;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, float size, Color color)
    {
        var text = FeelUI.Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        FontRoles.Apply(text, FontRoles.Label);
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.characterSpacing = 4f;
        text.color = color;
        return text;
    }
}
