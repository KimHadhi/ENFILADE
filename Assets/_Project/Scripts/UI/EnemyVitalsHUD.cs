using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Head-height name + &lt; HP &gt; bar with a thin posture bar, and a larger body-centred stagger cue.
/// Lost health stays as a trail (yellow for small hits, orange at ~70%, red near total), then drains;
/// heavy hits shake the bar. Colours and timing: Resources/Combat/Feedback.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyVitalsHUD : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maximumDistance = 30f;
    [SerializeField, Min(0f)] private float headroom = 0.22f;
    [SerializeField, Min(40f)] private float width = 120f;
    [SerializeField] private LayerMask occlusionLayers = ~0;
    private const float BarHeight = 8f, CapWidth = 7f, CapHeight = 14f, CapGap = 2f, CapStroke = 2.6f;
    private const float StripePeriod = 6f, EdgeWidth = 1.5f;
    private const float PostureHeight = 3f, PostureY = -9f, NameY = 17f, NameHeight = 16f;
    private ICombatVitals vitals;
    private MeleeCombatTarget posture;
    private Collider[] bodyColliders;
    private RectTransform panel, bars, healthTrack, healthFill, trailFill, postureFill, staggerPanel;
    private Image healthImage, healthLostImage, trailImage, trailEdge, postureImage;
    private RawImage trailStripes;
    private ChevronGraphic leftCap, rightCap;
    private TextMeshProUGUI nameLabel;
    private RawImage staggerBadge;
    private Transform uiRoot;
    private readonly HealthLossTrail trail = new HealthLossTrail();
    public bool IsStaggerVisible => staggerPanel != null && staggerPanel.gameObject.activeSelf;
    private readonly RaycastHit[] occlusionHits = new RaycastHit[16];
    public float HealthRatio { get; private set; }
    public float PostureRatio { get; private set; }
    public bool IsVisible => panel != null && panel.gameObject.activeSelf;
    /// <summary>Right edge of the draining loss trail, 0..1 (equals HealthRatio when idle).</summary>
    public float TrailRatio { get; private set; }
    public string DisplayName => nameLabel != null ? nameLabel.text : CombatDamageEvents.DisplayNameOf(this);

    private void OnEnable()
    {
        vitals = GetComponent<ICombatVitals>(); posture = GetComponent<MeleeCombatTarget>();
        bodyColliders = GetComponentsInChildren<Collider>();
        if (nameLabel != null) nameLabel.text = CombatDamageEvents.DisplayNameOf(this);
    }
    private void LateUpdate() => Refresh(Camera.main);

    public void Refresh(Camera view)
    {
        if (vitals == null || posture == null) OnEnable();
        float maximum = vitals == null ? 1f : Mathf.Max(1f, vitals.MaximumHealth);
        float health = vitals == null ? 0f : Mathf.Max(0f, vitals.Health);
        HealthRatio = Mathf.Clamp01(health / maximum);
        PostureRatio = posture == null ? 0f : Mathf.Clamp01(posture.Posture / posture.MaximumPosture);
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        // Keeps running while hidden so a bar that reappears shows the correct state.
        trail.Tick(health, maximum, Time.unscaledDeltaTime, settings.enemyShakeMax, impact: true);
        TrailRatio = Mathf.Clamp01(trail.Trail / maximum);
        if (!isActiveAndEnabled || view == null || vitals == null || posture == null || !posture.IsAvailable || HealthRatio <= 0f)
        { Hide(); return; }
        EnsurePanel();
        // Keep the UI under its owner without inheriting the enemy's authored size.
        Vector3 scale = transform.lossyScale;
        uiRoot.localScale = new Vector3(InverseScale(scale.x), InverseScale(scale.y), InverseScale(scale.z));
        Bounds body = BodyBounds();
        PositionPanel(panel, view, new Vector3(body.center.x, body.max.y + headroom, body.center.z));
        healthImage.color = leftCap.color = rightCap.color = settings.enemyHealthColor;
        healthLostImage.color = settings.enemyHealthLostColor;
        SetFill(healthFill, healthImage, HealthRatio);
        // Trail sits under the red fill, so only the part between current and trailing health shows.
        // Light stripes + an edge line keep even an orange trail from reading as remaining (red) health.
        bool showTrail = TrailRatio > HealthRatio;
        SetFill(trailFill, trailImage, TrailRatio);
        trailImage.enabled = showTrail;
        trailImage.color = trail.TrailColor;
        trailStripes.enabled = showTrail && settings.trailStripes;
        if (trailStripes.enabled)
        {
            trailStripes.color = new Color(1f, 1f, 1f, settings.trailStripeLighten);
            // Pattern anchored to the bar's left edge so it does not slide as the trail shortens; only the scroll moves it.
            trailStripes.uvRect = new Rect(trail.StripeScroll / StripePeriod, 0f, width * TrailRatio / StripePeriod, BarHeight / StripePeriod);
        }
        trailEdge.enabled = showTrail && settings.trailEdgeLine;
        if (trailEdge.enabled)
        {
            var edge = trailEdge.rectTransform;
            edge.anchorMin = edge.anchorMax = new Vector2(HealthRatio, .5f);
        }
        bars.anchoredPosition = trail.ShakeOffset;
        bars.localScale = Vector3.one * trail.ScalePunch;
        SetFill(postureFill, postureImage, PostureRatio);
        postureImage.color = posture.IsBroken ? new Color(1f, .28f, .20f)
            : posture.IsStunned ? new Color(1f, .91f, .52f) : new Color(.95f, .68f, .22f);
        if (posture.IsStaggered && staggerBadge.texture != null)
        {
            // Project onto the body centre, but draw in front of its geometry instead of inside the mesh.
            PositionPanel(staggerPanel, view, body.center, body.extents.magnitude + .05f, distanceScaled: true);
            float imageWidth = CombatFeedbackSettings.Instance != null ? Mathf.Max(40f, CombatFeedbackSettings.Instance.staggerImageWidth) : 260f;
            staggerBadge.rectTransform.sizeDelta = new Vector2(imageWidth, imageWidth * staggerBadge.texture.height / staggerBadge.texture.width);
        }
        else staggerPanel.gameObject.SetActive(false);
        float pop = 1f - Mathf.Pow(1f - Mathf.Clamp01(posture.StaggerAge / .24f), 3f);
        staggerBadge.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, pop);
    }

    private void PositionPanel(RectTransform target, Camera view, Vector3 anchor, float forwardOffset = 0f, bool distanceScaled = false)
    {
        Vector3 viewport = view.WorldToViewportPoint(anchor);
        bool visible = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f
            && (anchor - view.transform.position).sqrMagnitude <= maximumDistance * maximumDistance && !IsOccluded(view, anchor);
        target.gameObject.SetActive(visible);
        if (!visible) return;
        Vector3 toCamera = view.orthographic ? -view.transform.forward : (view.transform.position - anchor).normalized;
        float depthPerUnit = Mathf.Max(.01f, -Vector3.Dot(toCamera, view.transform.forward));
        float offset = Mathf.Min(forwardOffset, Mathf.Max(0f, (viewport.z - view.nearClipPlane - .05f) / depthPerUnit));
        target.position = anchor + toCamera * offset;
        target.rotation = view.transform.rotation;
        float depth = view.WorldToViewportPoint(target.position).z;
        float worldHeight = view.orthographic ? 2f * view.orthographicSize : 2f * depth * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
        float uiScale = Mathf.Clamp(view.pixelHeight / 1080f, .75f, 1.5f);
        // Stagger width is authored at 3 metres. Use the real body distance, not the
        // forward-shifted canvas distance, so drawing in front of the mesh cannot distort scaling.
        if (distanceScaled) uiScale *= Mathf.Min(2f, 3f / Mathf.Max(.01f, Vector3.Distance(view.transform.position, anchor)));
        target.localScale = Vector3.one * (worldHeight / Mathf.Max(1, view.pixelHeight) * uiScale);
    }
    private Bounds BodyBounds()
    {
        bool found = false; Bounds bounds = default;
        foreach (var collider in bodyColliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        return found ? bounds : new Bounds(transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
    }
    private bool IsOccluded(Camera view, Vector3 anchor)
    {
        Vector3 direction = anchor - view.transform.position;
        float distance = direction.magnitude;
        int count = Physics.RaycastNonAlloc(view.transform.position, direction.normalized, occlusionHits, distance,
            occlusionLayers, QueryTriggerInteraction.Ignore);
        var hits = count == occlusionHits.Length
            ? Physics.RaycastAll(view.transform.position, direction.normalized, distance, occlusionLayers, QueryTriggerInteraction.Ignore)
            : occlusionHits;
        if (hits != occlusionHits) count = hits.Length;
        var player = view.GetComponentInParent<PlayerMovement>();
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i].collider.transform;
            if (hit.IsChildOf(transform) || (player != null && hit.IsChildOf(player.transform))) continue;
            return true;
        }
        return false;
    }
    private void EnsurePanel()
    {
        if (panel != null) return;
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        uiRoot = new GameObject("Combat UI").transform;
        uiRoot.gameObject.hideFlags = HideFlags.DontSave;
        uiRoot.SetParent(transform, false);
        var root = new GameObject("Enemy HP + Posture", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(uiRoot, false);
        panel = root.GetComponent<RectTransform>();
        panel.sizeDelta = new Vector2(width + 2f * (CapWidth + CapGap), NameY + NameHeight * .5f - (PostureY - PostureHeight * .5f));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 20;
        var group = root.AddComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;

        // Everything that shakes on a heavy hit. The panel itself stays on the billboard anchor.
        bars = Rect("Bars", panel, 0f, 0f, 0f, 0f);

        // Name above the bar.
        nameLabel = Rect("Name", bars, 0f, NameY, width + 60f, NameHeight).gameObject.AddComponent<TextMeshProUGUI>();
        var font = CombatUIAssets.ResolveFont(null);
        if (font != null) nameLabel.font = font;
        var outline = CombatUIAssets.Outlined(nameLabel.font, new Color(0f, 0f, 0f, .85f), .2f);
        if (outline != null) nameLabel.fontSharedMaterial = outline;
        nameLabel.fontSize = 13f; nameLabel.fontStyle = FontStyles.Bold; nameLabel.color = new Color(1f, .97f, .95f);
        nameLabel.alignment = TextAlignmentOptions.Bottom; nameLabel.raycastTarget = false;
        nameLabel.textWrappingMode = TextWrappingModes.NoWrap; nameLabel.overflowMode = TextOverflowModes.Ellipsis;
        nameLabel.text = CombatDamageEvents.DisplayNameOf(this);

        // < [ red | almost invisible grey ] >
        healthTrack = Rect("HP", bars, 0f, 0f, width, BarHeight);
        healthLostImage = healthTrack.gameObject.AddComponent<Image>(); healthLostImage.raycastTarget = false;
        healthLostImage.color = settings.enemyHealthLostColor;
        trailFill = Stretch("Loss trail", healthTrack);
        trailImage = trailFill.gameObject.AddComponent<Image>(); trailImage.raycastTarget = false; trailImage.enabled = false;
        trailStripes = Stretch("Stripes", trailFill).gameObject.AddComponent<RawImage>();
        trailStripes.texture = CombatUIAssets.TrailStripes; trailStripes.raycastTarget = false; trailStripes.enabled = false;
        healthFill = Stretch("Fill", healthTrack);
        healthImage = healthFill.gameObject.AddComponent<Image>(); healthImage.raycastTarget = false;
        healthImage.color = settings.enemyHealthColor;
        trailEdge = Rect("Trail edge", healthTrack, 0f, 0f, EdgeWidth, BarHeight + 2f).gameObject.AddComponent<Image>();
        trailEdge.color = new Color(1f, 1f, 1f, .95f); trailEdge.raycastTarget = false; trailEdge.enabled = false;
        float capX = width * .5f + CapGap + CapWidth * .5f;
        leftCap = Rect("HP Cap <", bars, -capX, 0f, CapWidth, CapHeight).gameObject.AddComponent<ChevronGraphic>();
        leftCap.Configure(false, CapStroke); leftCap.color = settings.enemyHealthColor;
        rightCap = Rect("HP Cap >", bars, capX, 0f, CapWidth, CapHeight).gameObject.AddComponent<ChevronGraphic>();
        rightCap.Configure(true, CapStroke); rightCap.color = settings.enemyHealthColor;

        postureFill = Bar("Posture", PostureY, PostureHeight, new Color(.95f, .68f, .22f), out postureImage);
        var staggerRoot = new GameObject("Body Stagger", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        staggerRoot.hideFlags = HideFlags.DontSave;
        staggerRoot.transform.SetParent(uiRoot, false);
        staggerPanel = staggerRoot.GetComponent<RectTransform>();
        var staggerCanvas = staggerRoot.GetComponent<Canvas>(); staggerCanvas.renderMode = RenderMode.WorldSpace; staggerCanvas.sortingOrder = 21;
        var staggerGroup = staggerRoot.GetComponent<CanvasGroup>(); staggerGroup.interactable = staggerGroup.blocksRaycasts = false;
        staggerBadge = new GameObject("Stagger", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        staggerBadge.transform.SetParent(staggerPanel, false); staggerBadge.raycastTarget = false;
        var feedback = CombatFeedbackSettings.Instance;
        staggerBadge.texture = feedback != null ? feedback.staggerImage : null;
    }
    private RectTransform Bar(string label, float y, float height, Color color, out Image fillImage)
    {
        var track = Rect(label, bars, 0f, y, width, height);
        var background = track.gameObject.AddComponent<Image>(); background.color = new Color(.025f, .035f, .05f, .6f); background.raycastTarget = false;
        var fill = Stretch("Fill", track);
        fillImage = fill.gameObject.AddComponent<Image>(); fillImage.color = color; fillImage.raycastTarget = false;
        return fill;
    }
    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.sizeDelta = new Vector2(w, h); rect.anchoredPosition = new Vector2(x, y);
        return rect;
    }
    private static RectTransform Stretch(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    private static void SetFill(RectTransform rect, Image graphic, float ratio)
    { rect.anchorMax = new Vector2(ratio, 1f); graphic.enabled = ratio > 0f; }
    private static float InverseScale(float value) => Mathf.Abs(value) > .0001f ? 1f / value : 1f;
    private void Hide()
    { if (panel != null) panel.gameObject.SetActive(false); if (staggerPanel != null) staggerPanel.gameObject.SetActive(false); }
    private void OnDisable() { Hide(); trail.Reset(float.NaN); if (bars != null) { bars.anchoredPosition = Vector2.zero; bars.localScale = Vector3.one; } }
    private void OnDestroy()
    {
        if (uiRoot != null) { if (Application.isPlaying) Destroy(uiRoot.gameObject); else DestroyImmediate(uiRoot.gameObject); }
    }
}
