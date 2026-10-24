using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach to HPBarRoot. Preserves your existing fonts and HPHealthBar placement.
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class AdaptiveHealthHUD : MonoBehaviour
{
    [Header("Live player health")]
    [Tooltip("During Play, read this player's PlayerDamageReceiver. Disable Auto Find Player and clear Player for standalone values. Edit mode uses the values below.")]
    [SerializeField] private PlayerMovement player;
    [Tooltip("When Player is empty, find a single active player in this scene, or a single player across loaded scenes. Multiple candidates require explicit assignment.")]
    [SerializeField] private bool autoFindPlayer = true;
    private float nextPlayerSearch;

    [Header("Existing hierarchy - found automatically by object name")]
    [SerializeField] private RectTransform hpHealthBar;
    [SerializeField] private Graphic hpText;
    [SerializeField] private Graphic maxHPText;
    [SerializeField] private Graphic shieldText;

    [Header("Keep the scene's block placement and size")]
    [SerializeField] private bool matchExistingBlocks = true;
    [SerializeField] private Vector2 barOffset = new Vector2(0f, -2f);

    [Header("Health - live player values during Play, editable outside Play")]
    [SerializeField, Min(0)] private float currentHealth = 100f;
    [SerializeField, Min(0)] private float maxHealth = 100f;
    [SerializeField, Min(0)] private float extraShield = 0f;

    [Header("Size - Canvas units")]
    [SerializeField, Min(1)] private float healthPerBlock = 20f;
    [Tooltip("Maximum health up to and including this value uses separate blocks.")]
    [SerializeField, Min(1)] private float blockModeMaxHealth = 300f;
    [Tooltip("Horizontal pitch between blocks, in Canvas units.")]
    [SerializeField, Min(8)] private float blockWidth = 32f;
    [SerializeField, Min(4)] private float barHeight = 32f;
    [SerializeField, Range(2, 20)] private int maxVisibleSections = 10;
    [SerializeField, Range(0, 10)] private float blockGap = 5.0526316f;
    [SerializeField, Range(0, 12)] private float slant = 6.4f;

    [Header("Shield - thin band above the main bar")]
    [Tooltip("Minimum HP-equivalent scale for shield width, fill and overflow percentage.")]
    [SerializeField, Min(100f)] private float minimumShieldScale = 100f;
    [SerializeField, Min(1)] private float shieldHeight = 4.8f;
    [SerializeField, Min(0)] private float shieldOffset = 4.8f;
    [Tooltip("Move only the shield bar horizontally. Positive = right, negative = left. Canvas units.")]
    [SerializeField] private float shieldXOffset = 3f;

    [Header("Colors")]
    [SerializeField] private Color healthColor = new Color(1f, 0.31f, 0.47f, 1f);
    [SerializeField] private Color emptyColor = new Color(1f, 0.31f, 0.47f, 0.18f);
    [SerializeField] private Color shieldColor = new Color(0.65f, 0.95f, 1f, 1f);
    [SerializeField] private Color tickColor = new Color(1f, 1f, 1f, 0.85f);

    private RectTransform visualRoot;
    private SlantedHealthGraphic healthGraphic, shieldGraphic;
    private readonly List<GameObject> originalBlocks = new List<GameObject>();
    private readonly List<bool> originalActive = new List<bool>();
    private readonly List<Behaviour> barLayouts = new List<Behaviour>();
    private readonly List<bool> layoutEnabled = new List<bool>();
    private bool capturedLayout;
    private Vector2 capturedOffset;
    private float capturedWidth, capturedHeight, capturedPitch, capturedFirstWidth;
    private bool dirty = true;
    private string originalHPLabel, originalMaxLabel, originalShieldLabel;
    private bool originalShieldVisible;

    // Damage feedback (Play only): lost HP trails in a loss colour, then drains; heavy hits shake the bar.
    // Colours, drain speed and shake strength: Resources/Combat/Feedback (Health loss).
    private readonly HealthLossTrail lossTrail = new HealthLossTrail();
    private float lastShield = float.NaN;
    private Vector2 restingPosition;
    private double healthStep;
    private bool healthBlocks;
    private float healthShear, healthGap;

    private void Start()
    {
        if (Application.IsPlaying(gameObject)) RefreshNow();
    }

    private void Initialize()
    {
        if (visualRoot != null) return;
        FindReferences();
        if (hpHealthBar == null || !IsLabel(hpText) || !IsLabel(maxHPText) || !IsLabel(shieldText))
        {
            Debug.LogError("AdaptiveHealthHUD: assign HPHealthBar, HPText, MaxHPText and ShieldText (TMP or legacy Text).", this);
            enabled = false;
            return;
        }
        originalHPLabel = GetLabel(hpText);
        originalMaxLabel = GetLabel(maxHPText);
        originalShieldLabel = GetLabel(shieldText);
        originalShieldVisible = shieldText.gameObject.activeSelf;
        foreach (LayoutGroup layout in hpHealthBar.GetComponents<LayoutGroup>())
        {
            barLayouts.Add(layout);
            layoutEnabled.Add(layout.enabled);
        }
        ContentSizeFitter fitter = hpHealthBar.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            barLayouts.Add(fitter);
            layoutEnabled.Add(fitter.enabled);
        }
        foreach (Transform child in hpHealthBar)
        {
            if (child.name == "HealthBlock" || child.name.StartsWith("HealthBlock ("))
            {
                originalBlocks.Add(child.gameObject);
                originalActive.Add(child.gameObject.activeSelf);
            }
        }
        // Resolve the original UI BEFORE disabling layouts or hiding its blocks.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(hpHealthBar);
        CaptureExistingLayout();
        SetLayoutEnabled(false);
        visualRoot = MakeRect("Adaptive Visuals (Runtime)", hpHealthBar);
        healthGraphic = MakeRect("Health", visualRoot).gameObject.AddComponent<SlantedHealthGraphic>();
        shieldGraphic = MakeRect("Shield", visualRoot).gameObject.AddComponent<SlantedHealthGraphic>();
        SetOriginalVisibility(false);
        Refresh();
    }

    private void OnEnable()
    {
        if (visualRoot == null) return;
        visualRoot.gameObject.SetActive(true);
        SetLayoutEnabled(false);
        SetOriginalVisibility(false);
        dirty = true;
    }

    private void OnDisable()
    {
        if (!Application.IsPlaying(gameObject)) { StopPreview(); return; }
        if (visualRoot == null) return;
        visualRoot.gameObject.SetActive(false);
        SetOriginalVisibility(true);
        SetLayoutEnabled(true);
    }

    private void OnValidate() { dirty = true; }
    private void LateUpdate() { if (Application.IsPlaying(gameObject)) RefreshNow(); }

    public PlayerMovement ResolvePlayer()
    {
        if (Application.IsPlaying(gameObject) && player == null && autoFindPlayer && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + 0.5f;
            PlayerMovement local = null, any = null;
            int localCount = 0, totalCount = 0;
            foreach (var candidate in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!candidate.isActiveAndEnabled) continue;
                any = candidate; totalCount++;
                if (candidate.gameObject.scene == gameObject.scene) { local = candidate; localCount++; }
            }
            if (localCount == 1) player = local;
            else if (localCount == 0 && totalCount == 1) player = any;
        }
        return player;
    }

    public void RefreshNow()
    {
        ResolvePlayer();
        if (Application.IsPlaying(gameObject) && player != null)
        {
            // PlayerMovement can create the receiver at runtime; do not cache it before Awake.
            PlayerDamageReceiver receiver = player.DamageReceiver;
            if (receiver == null) receiver = player.GetComponent<PlayerDamageReceiver>();
            if (receiver != null) SetValues(receiver.Health, receiver.MaximumHealth, receiver.Shield);
        }
        if (visualRoot == null) Initialize();
        if (dirty) Refresh();
        if (Application.IsPlaying(gameObject)) AnimateDamage(Time.unscaledDeltaTime);
    }

    private void AnimateDamage(float dt)
    {
        if (visualRoot == null || healthGraphic == null) return;
        float shield = NonNegative(extraShield);
        float shieldLoss = float.IsNaN(lastShield) ? 0f : Mathf.Max(0f, lastShield - shield);
        lastShield = shield;
        bool wasAnimating = lossTrail.IsAnimating;
        float maximum = NonNegative(maxHealth);
        lossTrail.Tick(Mathf.Min(NonNegative(currentHealth), maximum), maximum, dt,
            CombatFeedbackSettings.InstanceOrDefaults.playerShakeMax, shieldLoss);
        if (!wasAnimating && !lossTrail.IsAnimating) return;
        // Per-frame path: only the health mesh and bar offset, not the full layout/label refresh.
        ConfigureHealthGraphic();
        visualRoot.anchoredPosition = restingPosition + lossTrail.ShakeOffset;
    }

    private void ConfigureHealthGraphic()
    {
        float maximum = NonNegative(maxHealth);
        float current = Mathf.Min(NonNegative(currentHealth), maximum);
        bool live = Application.IsPlaying(gameObject) && !float.IsNaN(lossTrail.Trail);
        float trailing = live ? Mathf.Clamp(lossTrail.Trail, current, maximum) : current;
        var feedback = CombatFeedbackSettings.InstanceOrDefaults;
        healthGraphic.SetTrailDecoration(live && feedback.trailStripes, 10f, lossTrail.StripeScroll,
            lossTrail.StripeColor, live && feedback.trailEdgeLine ? new Color(1f, 1f, 1f, .95f) : Color.clear);
        healthGraphic.Configure(current, trailing, maximum, healthStep, healthBlocks, healthShear,
            healthGap, healthColor, emptyColor, tickColor, live ? lossTrail.TrailColor : Color.clear);
    }

    public void BindPlayer(PlayerMovement target) { player = target; nextPlayerSearch = 0f; dirty = true; }

    public void StopPreview()
    {
        if (visualRoot == null) return;
        SetOriginalVisibility(true);
        SetLayoutEnabled(true);
        SetLabel(hpText, originalHPLabel);
        SetLabel(maxHPText, originalMaxLabel);
        SetLabel(shieldText, originalShieldLabel);
        if (shieldText != null) shieldText.gameObject.SetActive(originalShieldVisible);
        if (Application.isPlaying) Destroy(visualRoot.gameObject);
        else DestroyImmediate(visualRoot.gameObject);
        visualRoot = null;
        healthGraphic = shieldGraphic = null;
        originalBlocks.Clear(); originalActive.Clear();
        barLayouts.Clear(); layoutEnabled.Clear();
        capturedLayout = false;
        dirty = true;
    }

    public void SetValues(float current, float maximum, float shield)
    {
        current = NonNegative(current);
        maximum = NonNegative(maximum);
        shield = NonNegative(shield);
        if (currentHealth == current && maxHealth == maximum && extraShield == shield) return;
        currentHealth = current; maxHealth = maximum; extraShield = shield;
        dirty = true;
    }

    private void Refresh()
    {
        if (visualRoot == null) return;
        dirty = false;
        float maximum = NonNegative(maxHealth);
        float current = Mathf.Min(NonNegative(currentHealth), maximum);
        float shield = NonNegative(extraShield);
        float unit = Mathf.Max(1f, NonNegative(healthPerBlock));
        float pitch = Mathf.Max(8f, NonNegative(blockWidth));
        float threshold = Mathf.Max(1f, NonNegative(blockModeMaxHealth));
        float height = Mathf.Max(4f, NonNegative(barHeight));
        float sourceHeight = height;
        float sourcePitch = pitch;
        bool useCaptured = matchExistingBlocks && capturedLayout;
        if (useCaptured)
        {
            pitch = capturedPitch;
            height = capturedHeight;
        }
        float verticalScale = height / sourceHeight;
        float horizontalScale = pitch / sourcePitch;
        float shear = Mathf.Min(NonNegative(slant) * verticalScale, pitch * 0.5f);
        int sections = Mathf.Clamp(maxVisibleSections, 2, 20);
        // 300 is still blocks. Above it, retain exactly the width of the 300-HP row.
        float firstWidth = useCaptured ? capturedFirstWidth : pitch + shear;
        float width = (float)CalculateDisplayWidth(maximum, threshold, unit, pitch, firstWidth);
        float shieldScale = Mathf.Max(maximum, Mathf.Max(100f, NonNegative(minimumShieldScale)));
        float shieldWidth = (float)CalculateDisplayWidth(shieldScale, threshold, unit, pitch, firstWidth);
        bool compact = maximum > threshold;
        double step = compact ? NiceStep(System.Math.Max(unit, (double)maximum / sections)) : unit;

        // Use the original blocks' lower-left corner, not the parent rect's corner.
        restingPosition = (useCaptured ? capturedOffset : Vector2.zero) + barOffset;
        visualRoot.anchoredPosition = restingPosition + (Application.IsPlaying(gameObject) ? lossTrail.ShakeOffset : Vector2.zero);
        visualRoot.sizeDelta = new Vector2(width, height);
        Place(healthGraphic.rectTransform, 0f, width, height);
        float shieldH = Mathf.Max(1f, NonNegative(shieldHeight)) * verticalScale;
        float shieldX = float.IsNaN(shieldXOffset) || float.IsInfinity(shieldXOffset) ? 0f : shieldXOffset;
        Place(shieldGraphic.rectTransform, height + NonNegative(shieldOffset) * verticalScale, shieldWidth, shieldH, shieldX);
        healthStep = step; healthBlocks = !compact; healthShear = shear;
        healthGap = NonNegative(blockGap) * horizontalScale;
        ConfigureHealthGraphic();
        shieldGraphic.Configure(Mathf.Min(shield, shieldScale), shieldScale,
            shieldScale, false, shear * shieldH / height,
            0f, shieldColor, Color.clear, Color.clear);
        shieldGraphic.gameObject.SetActive(shield > 0f);

        SetLabel(hpText, current.ToString("N0"));
        SetLabel(maxHPText, maximum.ToString("N0"));
        SetLabel(shieldText, shield > shieldScale
            ? $"+{shield:N0} ({(double)shield / shieldScale * 100d:N0}%)" : $"+{shield:N0}");
        shieldText.gameObject.SetActive(shield > 0f);
        if (hpText.transform.parent is RectTransform textRoot)
        {
            // The authored row positions children without controlling their widths.
            // Resolve each label's new width BEFORE the row positions its centre pivot;
            // otherwise a wider shield label grows leftward over the maximum HP label.
            foreach (RectTransform child in textRoot)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                var fitter = child.GetComponent<ContentSizeFitter>();
                if (fitter != null && fitter.isActiveAndEnabled) fitter.SetLayoutHorizontal();
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(textRoot);
        }
    }

    private void CaptureExistingLayout()
    {
        capturedLayout = false;
        if (!matchExistingBlocks) return;

        var bounds = new List<Rect>();
        var corners = new Vector3[4];
        // Prefer visible blocks. Hidden authoring blocks still retain their rects
        // and can supply the layout when every original block is disabled.
        bool hasActiveRects = false;
        for (int i = 0; i < originalBlocks.Count; i++)
        {
            if (!originalActive[i] || originalBlocks[i] == null) continue;
            RectTransform rect = originalBlocks[i].transform as RectTransform;
            if (rect != null && rect.rect.width > 0f && rect.rect.height > 0f)
            {
                hasActiveRects = true;
                break;
            }
        }
        for (int i = 0; i < originalBlocks.Count; i++)
        {
            if (originalBlocks[i] == null || (hasActiveRects && !originalActive[i])) continue;
            RectTransform block = originalBlocks[i].transform as RectTransform;
            if (block == null || block.rect.width <= 0f || block.rect.height <= 0f) continue;
            block.GetWorldCorners(corners);
            Vector3 a = hpHealthBar.InverseTransformPoint(corners[0]);
            float left = a.x, right = a.x, bottom = a.y, top = a.y;
            for (int j = 1; j < 4; j++)
            {
                Vector3 p = hpHealthBar.InverseTransformPoint(corners[j]);
                left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x);
                bottom = Mathf.Min(bottom, p.y); top = Mathf.Max(top, p.y);
            }
            bounds.Add(Rect.MinMaxRect(left, bottom, right, top));
        }
        if (bounds.Count == 0)
        {
            // A HUD without template blocks uses the Inspector's manual dimensions.
            return;
        }
        bounds.Sort((a, b) => a.xMin.CompareTo(b.xMin));
        float minX = bounds[0].xMin, minY = bounds[0].yMin;
        float maxX = bounds[0].xMax, maxY = bounds[0].yMax;
        foreach (Rect rect in bounds)
        {
            minX = Mathf.Min(minX, rect.xMin); minY = Mathf.Min(minY, rect.yMin);
            maxX = Mathf.Max(maxX, rect.xMax); maxY = Mathf.Max(maxY, rect.yMax);
        }
        capturedWidth = maxX - minX;
        capturedHeight = maxY - minY;
        capturedFirstWidth = bounds[0].width;
        // Average pitch reproduces the full row's measured extent at the same count.
        capturedPitch = bounds.Count > 1
            ? (bounds[bounds.Count - 1].xMin - bounds[0].xMin) / (bounds.Count - 1)
            : capturedFirstWidth + NonNegative(blockGap);
        capturedOffset = new Vector2(minX - hpHealthBar.rect.xMin, minY - hpHealthBar.rect.yMin);
        capturedLayout = capturedWidth > 0f && capturedHeight > 0f && capturedPitch > 0f;
    }

    internal static double CalculateNaturalWidth(double blockCount, double pitch, double firstWidth)
    {
        if (blockCount <= 0d) return 0d;
        if (blockCount <= 1d) return firstWidth * blockCount;
        return (blockCount - 1d) * pitch + firstWidth;
    }

    internal static double CalculateDisplayWidth(double maximum, double threshold, double unit, double pitch, double firstWidth)
    {
        return CalculateNaturalWidth(System.Math.Min(maximum, threshold) / unit, pitch, firstWidth);
    }

    private void FindReferences()
    {
        if (hpHealthBar == null) hpHealthBar = FindNamed("HPHealthBar") as RectTransform;
        if (hpText == null) hpText = FindLabel("HPText");
        if (maxHPText == null) maxHPText = FindLabel("MaxHPText");
        if (shieldText == null) shieldText = FindLabel("ShieldText");
    }

    private Transform FindNamed(string objectName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child;
        return null;
    }

    private Graphic FindLabel(string objectName)
    {
        Transform child = FindNamed(objectName);
        return child != null ? child.GetComponent<Graphic>() : null;
    }

    private static bool IsLabel(Graphic target) { return target is TMP_Text || target is Text; }

    private static string GetLabel(Graphic target)
    {
        if (target is TMP_Text tmp) return tmp.text;
        return target is Text legacy ? legacy.text : string.Empty;
    }

    private static void SetLabel(Graphic target, string value)
    {
        if (target is TMP_Text tmp) tmp.text = value;
        else if (target is Text legacy) legacy.text = value;
    }

    private void SetOriginalVisibility(bool restore)
    {
        for (int i = 0; i < originalBlocks.Count; i++)
            if (originalBlocks[i] != null) originalBlocks[i].SetActive(restore && originalActive[i]);
    }

    private void SetLayoutEnabled(bool restore)
    {
        for (int i = 0; i < barLayouts.Count; i++)
            if (barLayouts[i] != null) barLayouts[i].enabled = restore && layoutEnabled[i];
    }

    private static RectTransform MakeRect(string objectName, Transform parent)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        if (!Application.isPlaying) rect.gameObject.hideFlags = HideFlags.HideAndDontSave;
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return rect;
    }

    private static void Place(RectTransform rect, float y, float width, float height, float x = 0f)
    {
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static float NonNegative(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
    }

    private static double NiceStep(double minimum)
    {
        double power = System.Math.Pow(10d, System.Math.Floor(System.Math.Log10(minimum)));
        double n = minimum / power;
        return (n <= 1d ? 1d : n <= 2d ? 2d : n <= 5d ? 5d : 10d) * power;
    }

    private void OnDestroy()
    {
        StopPreview();
    }
}
