using UnityEngine;
using UnityEngine.UI;

/// <summary>Compact head-height HP/posture bars for enemies and practice targets.</summary>
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
    private RectTransform panel, healthFill, postureFill;
    private Image healthImage, postureImage;
    private RawImage staggerBadge;
    public bool IsStaggerVisible => IsVisible && staggerBadge != null && staggerBadge.gameObject.activeSelf;
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
        Vector3 anchor = HeadAnchor();
        Vector3 viewport = view.WorldToViewportPoint(anchor);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f
            || (anchor - view.transform.position).sqrMagnitude > maximumDistance * maximumDistance || IsOccluded(view, anchor))
        { Hide(); return; }
        EnsurePanel();
        panel.gameObject.SetActive(true);
        panel.position = anchor;
        panel.rotation = view.transform.rotation;
        // Keep the same small screen footprint as enemies approach or move away.
        float worldHeight = view.orthographic ? 2f * view.orthographicSize
            : 2f * viewport.z * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
        float uiScale = Mathf.Clamp(view.pixelHeight / 1080f, .75f, 1.5f);
        panel.localScale = Vector3.one * (worldHeight / Mathf.Max(1, view.pixelHeight) * uiScale);
        SetFill(healthFill, healthImage, HealthRatio);
        SetFill(postureFill, postureImage, PostureRatio);
        postureImage.color = posture.IsBroken ? new Color(1f, .28f, .20f)
            : posture.IsStunned ? new Color(1f, .91f, .52f) : new Color(.95f, .68f, .22f);
        staggerBadge.gameObject.SetActive(posture.IsStaggered && staggerBadge.texture != null);
        float pop = 1f - Mathf.Pow(1f - Mathf.Clamp01(posture.StaggerAge / .24f), 3f);
        staggerBadge.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, pop);
    }
    private Vector3 HeadAnchor()
    {
        bool found = false; Bounds bounds = default;
        foreach (var collider in bodyColliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        return found ? new Vector3(bounds.center.x, bounds.max.y + headroom, bounds.center.z)
            : transform.position + Vector3.up * (2f + headroom);
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
        // Keep this root outside scaled enemy transforms; position follows collider bounds instead.
        var root = new GameObject("Enemy HP + Posture", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.DontSave;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        panel = root.GetComponent<RectTransform>(); panel.sizeDelta = new Vector2(width, 19f);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 20;
        var group = root.AddComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;
        healthFill = Bar("HP", 5f, 9f, new Color(.95f, .31f, .25f), out healthImage);
        postureFill = Bar("Posture", -4f, 5f, new Color(.95f, .68f, .22f), out postureImage);
        staggerBadge = new GameObject("Stagger", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        staggerBadge.transform.SetParent(panel, false); staggerBadge.raycastTarget = false;
        var settings = CombatFeedbackSettings.Instance;
        staggerBadge.texture = settings != null ? settings.staggerImage : null;
        float aspect = staggerBadge.texture != null ? (float)staggerBadge.texture.width / staggerBadge.texture.height : 1.54f;
        staggerBadge.rectTransform.sizeDelta = new Vector2(150f, 150f / aspect);
        staggerBadge.rectTransform.anchoredPosition = new Vector2(0f, 22f + 75f / aspect);
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
    private void Hide() { if (panel != null) panel.gameObject.SetActive(false); }
    private void OnDisable() => Hide();
    private void OnDestroy()
    {
        if (panel == null) return;
        if (Application.isPlaying) Destroy(panel.gameObject); else DestroyImmediate(panel.gameObject);
    }
}
