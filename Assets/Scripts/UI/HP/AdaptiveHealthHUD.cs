using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach to HPBarRoot. Preserves your existing fonts and HPHealthBar placement.
[DisallowMultipleComponent]
public sealed class AdaptiveHealthHUD : MonoBehaviour
{
    [Header("Existing hierarchy - found automatically by object name")]
    [SerializeField] private RectTransform hpHealthBar;
    [SerializeField] private Graphic hpText;
    [SerializeField] private Graphic maxHPText;
    [SerializeField] private Graphic shieldText;

    [Header("Health - editable during Play")]
    [SerializeField, Min(0)] private float currentHealth = 100f;
    [SerializeField, Min(0)] private float maxHealth = 100f;
    [SerializeField, Min(0)] private float extraShield = 0f;

    [Header("Size - Canvas units")]
    [SerializeField, Min(1)] private float healthPerBlock = 20f;
    [SerializeField, Min(8)] private float blockWidth = 19f;
    [SerializeField, Min(32)] private float maxBarWidth = 194f;
    [SerializeField, Min(4)] private float barHeight = 20f;
    [SerializeField, Range(2, 20)] private int maxVisibleSections = 10;
    [SerializeField, Range(0, 10)] private float blockGap = 3f;
    [SerializeField, Range(0, 12)] private float slant = 4f;

    [Header("Shield - thin band above the main bar")]
    [SerializeField, Min(1)] private float shieldHeight = 3f;
    [SerializeField, Min(0)] private float shieldOffset = 3f;

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
    private bool dirty = true;

    private void Awake()
    {
        FindReferences();
        if (hpHealthBar == null || !IsLabel(hpText) || !IsLabel(maxHPText) || !IsLabel(shieldText))
        {
            Debug.LogError("AdaptiveHealthHUD: assign HPHealthBar, HPText, MaxHPText and ShieldText (TMP or legacy Text).", this);
            enabled = false;
            return;
        }
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
        SetLayoutEnabled(false);
        foreach (Transform child in hpHealthBar)
        {
            if (child.name == "HealthBlock" || child.name.StartsWith("HealthBlock ("))
            {
                originalBlocks.Add(child.gameObject);
                originalActive.Add(child.gameObject.activeSelf);
            }
        }
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
        if (visualRoot == null) return;
        visualRoot.gameObject.SetActive(false);
        SetOriginalVisibility(true);
        SetLayoutEnabled(true);
    }

    private void OnValidate() { dirty = true; }
    private void LateUpdate() { if (dirty) Refresh(); }

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
        float limit = Mathf.Max(32f, NonNegative(maxBarWidth));
        float height = Mathf.Max(4f, NonNegative(barHeight));
        float shear = Mathf.Clamp(NonNegative(slant), 0f, Mathf.Min(12f, pitch * 0.5f));
        int sections = Mathf.Clamp(maxVisibleSections, 2, 20);
        double naturalWidth = maximum > 0f ? (double)maximum / unit * pitch + shear : 0d;
        float width = (float)System.Math.Min(naturalWidth, limit);
        bool compact = naturalWidth > limit || (double)maximum / unit > sections;
        double step = compact ? NiceStep(System.Math.Max(unit, (double)maximum / sections)) : unit;

        // Keep the host rect unchanged: its position and pivot belong to the scene.
        visualRoot.sizeDelta = new Vector2(width, height);
        Place(healthGraphic.rectTransform, 0f, width, height);
        float shieldH = Mathf.Max(1f, NonNegative(shieldHeight));
        Place(shieldGraphic.rectTransform, height + NonNegative(shieldOffset), width, shieldH);
        healthGraphic.Configure(current, maximum, step, !compact, shear,
            NonNegative(blockGap), healthColor, emptyColor, tickColor);
        shieldGraphic.Configure(Mathf.Min(shield, maximum), maximum,
            System.Math.Max(1d, maximum), false, shear * shieldH / height,
            0f, shieldColor, Color.clear, Color.clear);
        shieldGraphic.gameObject.SetActive(shield > 0f && maximum > 0f);

        SetLabel(hpText, current.ToString("N0"));
        SetLabel(maxHPText, maximum.ToString("N0"));
        SetLabel(shieldText, shield > maximum && maximum > 0f
            ? $"+{shield:N0} ({(double)shield / maximum * 100d:N0}%)" : $"+{shield:N0}");
        shieldText.gameObject.SetActive(shield > 0f);
        if (hpText.transform.parent is RectTransform textRoot)
            LayoutRebuilder.MarkLayoutForRebuild(textRoot);
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
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return rect;
    }

    private static void Place(RectTransform rect, float y, float width, float height)
    {
        rect.anchoredPosition = new Vector2(0f, y);
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
        SetOriginalVisibility(true);
        SetLayoutEnabled(true);
        if (visualRoot != null) Destroy(visualRoot.gameObject);
    }
}
