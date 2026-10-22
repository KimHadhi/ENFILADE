using UnityEngine;
using UnityEngine.UI;

/// <summary>Head-height HP/posture bars and a larger body-centred stagger cue.</summary>
[DisallowMultipleComponent]
public sealed class EnemyVitalsHUD : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maximumDistance = 30f;
    [SerializeField, Min(0f)] private float headroom = 0.22f;
    [SerializeField, Min(40f)] private float width = 120f;
    [SerializeField] private LayerMask occlusionLayers = ~0;
    private ICombatVitals vitals;
    private MeleeCombatTarget posture;
    private Collider[] bodyColliders;
    private RectTransform panel, healthFill, postureFill, staggerPanel;
    private Image healthImage, postureImage;
    private RawImage staggerBadge;
    private Transform uiRoot;
    public bool IsStaggerVisible => staggerPanel != null && staggerPanel.gameObject.activeSelf;
    private readonly RaycastHit[] occlusionHits = new RaycastHit[16];
    public float HealthRatio { get; private set; }
    public float PostureRatio { get; private set; }
    public bool IsVisible => panel != null && panel.gameObject.activeSelf;

    private void OnEnable()
    {
        vitals = GetComponent<ICombatVitals>(); posture = GetComponent<MeleeCombatTarget>();
        bodyColliders = GetComponentsInChildren<Collider>();
    }
    private void LateUpdate() => Refresh(Camera.main);

    public void Refresh(Camera view)
    {
        if (vitals == null || posture == null) OnEnable();
        HealthRatio = vitals == null ? 0f : Mathf.Clamp01(vitals.Health / Mathf.Max(1f, vitals.MaximumHealth));
        PostureRatio = posture == null ? 0f : Mathf.Clamp01(posture.Posture / posture.MaximumPosture);
        if (!isActiveAndEnabled || view == null || vitals == null || posture == null || !posture.IsAvailable || HealthRatio <= 0f)
        { Hide(); return; }
        EnsurePanel();
        // Keep the UI under its owner without inheriting the enemy's authored size.
        Vector3 scale = transform.lossyScale;
        uiRoot.localScale = new Vector3(InverseScale(scale.x), InverseScale(scale.y), InverseScale(scale.z));
        Bounds body = BodyBounds();
        PositionPanel(panel, view, new Vector3(body.center.x, body.max.y + headroom, body.center.z));
        SetFill(healthFill, healthImage, HealthRatio);
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
        uiRoot = new GameObject("Combat UI").transform;
        uiRoot.gameObject.hideFlags = HideFlags.DontSave;
        uiRoot.SetParent(transform, false);
        var root = new GameObject("Enemy HP + Posture", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(uiRoot, false);
        panel = root.GetComponent<RectTransform>(); panel.sizeDelta = new Vector2(width, 19f);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 20;
        var group = root.AddComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;
        healthFill = Bar("HP", 5f, 9f, new Color(.95f, .31f, .25f), out healthImage);
        postureFill = Bar("Posture", -4f, 5f, new Color(.95f, .68f, .22f), out postureImage);
        var staggerRoot = new GameObject("Body Stagger", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        staggerRoot.hideFlags = HideFlags.DontSave;
        staggerRoot.transform.SetParent(uiRoot, false);
        staggerPanel = staggerRoot.GetComponent<RectTransform>();
        var staggerCanvas = staggerRoot.GetComponent<Canvas>(); staggerCanvas.renderMode = RenderMode.WorldSpace; staggerCanvas.sortingOrder = 21;
        var staggerGroup = staggerRoot.GetComponent<CanvasGroup>(); staggerGroup.interactable = staggerGroup.blocksRaycasts = false;
        staggerBadge = new GameObject("Stagger", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        staggerBadge.transform.SetParent(staggerPanel, false); staggerBadge.raycastTarget = false;
        var settings = CombatFeedbackSettings.Instance;
        staggerBadge.texture = settings != null ? settings.staggerImage : null;
    }
    private RectTransform Bar(string label, float y, float height, Color color, out Image fillImage)
    {
        var track = new GameObject(label, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        track.SetParent(panel, false); track.sizeDelta = new Vector2(width, height); track.anchoredPosition = new Vector2(0f, y);
        var background = track.GetComponent<Image>(); background.color = new Color(.025f, .035f, .05f, .92f); background.raycastTarget = false;
        var inset = new GameObject("Inset", typeof(RectTransform)).GetComponent<RectTransform>();
        inset.SetParent(track, false); inset.anchorMin = Vector2.zero; inset.anchorMax = Vector2.one;
        inset.offsetMin = Vector2.one; inset.offsetMax = -Vector2.one;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        fill.SetParent(inset, false); fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fillImage = fill.GetComponent<Image>(); fillImage.color = color; fillImage.raycastTarget = false;
        return fill;
    }
    private static void SetFill(RectTransform rect, Image graphic, float ratio)
    { rect.anchorMax = new Vector2(ratio, 1f); graphic.enabled = ratio > 0f; }
    private static float InverseScale(float value) => Mathf.Abs(value) > .0001f ? 1f / value : 1f;
    private void Hide()
    { if (panel != null) panel.gameObject.SetActive(false); if (staggerPanel != null) staggerPanel.gameObject.SetActive(false); }
    private void OnDisable() => Hide();
    private void OnDestroy()
    {
        if (uiRoot != null) { if (Application.isPlaying) Destroy(uiRoot.gameObject); else DestroyImmediate(uiRoot.gameObject); }
    }
}
