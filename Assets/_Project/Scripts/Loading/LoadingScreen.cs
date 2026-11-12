using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen over every scene change and the warm-up after it (the user, 2026-11-12: "게임 시작할 때 로딩 미리 해 놔야
/// 할 듯. 처음 총 쏘고 적 잡을 때 렉이 엄청 걸림"). Until 0.8.195 a scene change was a plain SceneManager.LoadScene: the old
/// scene's last frame froze for the 8 to 11 s the editor took to read and wake the hub or a chapter (Logs/hitches.txt), and
/// whatever a fight draws first (shader variants, pools, corpses, glyphs) was made in the fight. Now SceneLoad puts this
/// up first, the scene loads behind it, and SceneWarmup builds and draws what combat needs while it is still up, telling
/// each step on the bar.
/// An overlay canvas over every other one (Order), kept across scenes and drawn after the cameras: whatever the warm-up
/// draws in front of the player's camera is drawn for real, so its shaders and pipeline states are made, and is never
/// seen. While it is up the cursor is let go, which every gameplay input reads as "not playing" (PlayerMovement
/// .InputAvailable, PlayerLook, the TakesInput checks), and the backdrop takes every click; as it lifts the cursor is
/// taken again if the scene had taken it. Numbers: Tables/Loading.csv, Screen row. Words: loading.* in Texts.csv.
/// </summary>
[DisallowMultipleComponent]
public sealed class LoadingScreen : MonoBehaviour
{
    public const string TableName = "Loading", TableRow = "Screen";
    // Over every canvas the game makes: the HUD, the menus and the console all sort below 30000.
    private const int Order = 32000;
    // The bar's length and the block's place from the bottom left (reference pixels, 1920x1080).
    private const float BarWidth = 760f, Margin = 96f;

    [Tooltip("Seconds the screen takes to come up over a running scene.")]
    public float fadeIn = .15f;
    [Tooltip("Seconds it takes to lift once the warm-up is done.")]
    public float fadeOut = .35f;
    [Tooltip("Least seconds it stays up once shown, so a quick load never flashes.")]
    public float leastShown = .5f;
    [Tooltip("How much of the map's picture shows through the dark (0-1).")]
    public float pictureAlpha = .32f;
    [Tooltip("How much bigger the map's picture slowly grows while the screen is up (share).")]
    public float pictureDrift = .05f;
    [Tooltip("Seconds the bar takes to catch up with the work done.")]
    public float barEase = .15f;
    [Tooltip("Degrees a second the small pink square turns, so a long frame shows as a stop rather than as nothing.")]
    public float spin = 220f;

    private static LoadingScreen instance;

    private Canvas canvas;
    private CanvasGroup group;
    private RawImage picture;
    private AspectRatioFitter pictureFit;
    private TextMeshProUGUI title, stage, percent;
    private RectTransform fill, spinner;
    private string stageId, sceneShown;
    private float target, shown, shownAt, liftAskedAt = -1f;
    private int coveredFrame = -1, percentShown = -1;
    private bool wanted, holdCursor, retake, visible;

    /// <summary>Raised once the screen has faded all the way out.</summary>
    public static event Action Lifted;

    /// <summary>The screen is up or coming up (not yet asked to lift).</summary>
    public static bool Showing => instance != null && instance.wanted;

    /// <summary>Anything of the screen is on the display, fading included.</summary>
    public static bool Visible => instance != null && instance.visible;

    /// <summary>
    /// The screen has covered the whole display for at least one drawn frame: what is behind it can freeze, load or be
    /// drawn for warming without anyone seeing it.
    /// </summary>
    public static bool Covered => instance != null && instance.coveredFrame >= 0 && Time.frameCount > instance.coveredFrame;

    /// <summary>How far the bar has been told the work is (0-1).</summary>
    public static float Progress => instance != null ? instance.target : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        Lifted = null;
    }

    /// <summary>
    /// Puts the screen up (or keeps it up) for <paramref name="scene"/>: its name and picture from Maps.csv, the game's
    /// name for the title screen. <paramref name="instant"/>: no fade in, for a scene that starts under it.
    /// </summary>
    public static void Show(string scene = null, bool instant = false)
    {
        if (!Application.isPlaying) return;
        var screen = Ensure();
        if (!screen.wanted)
        {
            screen.wanted = true;
            screen.shownAt = Time.unscaledTime;
            screen.target = screen.shown = 0f;
            screen.percentShown = -1;
            screen.holdCursor = true;
        }
        screen.liftAskedAt = -1f;
        if (instant && screen.group.alpha < 1f)
        {
            screen.group.alpha = 1f;
            screen.coveredFrame = Time.frameCount;
        }
        screen.Apply();
        if (scene != null) screen.Name(scene);
    }

    /// <summary>The work done so far (0-1, never goes back while the screen is up) and what is being done (a Texts id).</summary>
    public static void Report(float progress, string textId = null)
    {
        if (instance == null || !instance.wanted) return;
        instance.target = Mathf.Max(instance.target, Mathf.Clamp01(progress));
        if (textId != null && textId != instance.stageId)
        {
            instance.stageId = textId;
            instance.stage.text = Texts.Get(textId);
        }
    }

    /// <summary>Done: the bar fills, and the screen lifts once it has been up its least time.</summary>
    public static void Hide()
    {
        if (instance == null || !instance.wanted) return;
        instance.target = 1f;
        if (instance.liftAskedAt < 0f) instance.liftAskedAt = Time.unscaledTime;
    }

    /// <summary>Runs <paramref name="routine"/> on the screen's own object, which lives across scene changes.</summary>
    public static Coroutine Run(IEnumerator routine) => Application.isPlaying ? Ensure().StartCoroutine(routine) : null;

    private static LoadingScreen Ensure()
    {
        if (instance != null) return instance;
        var host = new GameObject("Loading Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        DontDestroyOnLoad(host);
        return host.AddComponent<LoadingScreen>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        GameTables.Bind(TableName, TableRow, this);
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Order;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        Build((RectTransform)transform);
        canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        LetGo();
    }

    // Black over everything, the map's picture dimmed under it, and at the bottom left a pink tag, the map's name, the bar
    // with its percent and the step being done. Each piece keeps its own lean (the HUD's rule: never one shared slant).
    private void Build(RectTransform root)
    {
        var backdrop = KitStyle.Fill(KitStyle.Stretch("Backdrop", root), KitStyle.Night);
        // The one piece that takes clicks: nothing under the screen can be pressed while it is up.
        backdrop.raycastTarget = true;

        var frame = KitStyle.Stretch("Picture Frame", root);
        frame.gameObject.AddComponent<RectMask2D>();
        var pictureRect = FeelUI.Rect("Picture", frame);
        picture = pictureRect.gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        pictureFit = pictureRect.gameObject.AddComponent<AspectRatioFitter>();
        pictureFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        pictureFit.aspectRatio = 16f / 9f;
        picture.enabled = false;
        KitStyle.Fill(KitStyle.Stretch("Shade", root), KitStyle.With(KitStyle.Night, .55f));

        var block = Corner("Block", root, new Vector2(Margin, Margin), new Vector2(BarWidth + 200f, 240f));

        var tag = Corner("Tag", block, new Vector2(0f, 176f), new Vector2(200f, 42f));
        KitStyle.Cut(tag, KitStyle.Accent);
        KitStyle.Flat(tag, Texts.Get("loading.tag"), 22f, TextAlignmentOptions.Center, KitStyle.Ink);
        tag.localRotation = Quaternion.Euler(0f, 0f, 3f);

        spinner = Corner("Spinner", block, new Vector2(222f, 186f), new Vector2(20f, 20f));
        spinner.pivot = new Vector2(.5f, .5f);
        spinner.anchoredPosition += new Vector2(10f, 10f);
        KitStyle.Fill(spinner, KitStyle.Accent);

        title = KitStyle.Label(Corner("Title", block, new Vector2(0f, 80f), new Vector2(BarWidth + 400f, 90f)), "", 64f,
            TextAlignmentOptions.BottomLeft, KitStyle.Text);

        var bar = Corner("Bar", block, new Vector2(0f, 58f), new Vector2(BarWidth, 6f));
        KitStyle.Fill(bar, KitStyle.Faint);
        bar.localRotation = Quaternion.Euler(0f, 0f, -.6f);
        fill = Corner("Fill", bar, Vector2.zero, new Vector2(0f, 6f));
        KitStyle.Fill(fill, KitStyle.Accent);

        percent = KitStyle.Flat(Corner("Percent", block, new Vector2(BarWidth + 18f, 44f), new Vector2(120f, 34f)), "0%", 24f,
            TextAlignmentOptions.Left, KitStyle.Soft);
        stage = KitStyle.Label(Corner("Stage", block, new Vector2(0f, 6f), new Vector2(BarWidth, 40f)), "", 24f,
            TextAlignmentOptions.Left, KitStyle.Soft);
    }

    private static RectTransform Corner(string name, Transform parent, Vector2 at, Vector2 size)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
        return rect;
    }

    private void Name(string scene)
    {
        if (scene == sceneShown) return;
        sceneShown = scene;
        bool menu = scene == MainMenuScreen.SceneName;
        title.text = menu ? Texts.Get("loading.title.menu") : MapTable.DisplayName(scene);
        var texture = menu ? null : MapTable.Picture(scene);
        picture.texture = texture;
        picture.enabled = texture != null;
        if (texture != null) pictureFit.aspectRatio = texture.width / (float)Mathf.Max(1, texture.height);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (wanted && liftAskedAt >= 0f && Time.unscaledTime - shownAt >= leastShown && shown >= .995f)
        {
            wanted = false;
            LetGo();
        }
        shown = Mathf.Abs(target - shown) < .002f ? target : Mathf.Lerp(shown, target, 1f - Mathf.Exp(-dt / Mathf.Max(.01f, barEase)));
        float alpha = group.alpha;
        if (wanted) alpha = fadeIn <= 0f ? 1f : Mathf.MoveTowards(alpha, 1f, dt / fadeIn);
        else alpha = fadeOut <= 0f ? 0f : Mathf.MoveTowards(alpha, 0f, dt / fadeOut);
        group.alpha = alpha;
        if (alpha < 1f) coveredFrame = -1;
        else if (coveredFrame < 0) coveredFrame = Time.frameCount;
        Apply();
        if (!visible) return;

        fill.sizeDelta = new Vector2(BarWidth * shown, fill.sizeDelta.y);
        int whole = Mathf.RoundToInt(shown * 100f);
        if (whole != percentShown)
        {
            percentShown = whole;
            percent.text = whole + "%";
        }
        spinner.localRotation = Quaternion.Euler(0f, 0f, spinner.localEulerAngles.z - spin * dt);
        if (picture.enabled)
            picture.rectTransform.localScale = Vector3.one * (1f + pictureDrift * Mathf.Clamp01((Time.unscaledTime - shownAt) / 8f));
    }

    // The canvas draws only while anything of it shows; Lifted goes out the frame it is gone.
    private void Apply()
    {
        bool now = group.alpha > 0f || wanted;
        group.blocksRaycasts = wanted;
        if (now == visible) return;
        visible = now;
        canvas.enabled = now;
        if (!now) Lifted?.Invoke();
    }

    // Late, so a scene that takes the cursor as it starts (PlayerLook) has done so by now: it is let go again until the
    // screen lifts, and taken back then. A menu that frees and shows the cursor meanwhile (a class pick) keeps it free.
    private void LateUpdate()
    {
        if (!holdCursor) return;
        if (Cursor.lockState != CursorLockMode.None)
        {
            retake = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else if (Cursor.visible) retake = false;
        Cursor.visible = false;
    }

    private void LetGo()
    {
        if (!holdCursor) return;
        holdCursor = false;
        if (retake)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else Cursor.visible = true;
        retake = false;
    }
}
