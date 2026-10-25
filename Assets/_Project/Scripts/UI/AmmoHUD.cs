using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Magazine and finite reserve readout. Presentation never changes ammunition.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(400)]
public sealed class AmmoHUD : MonoBehaviour
{
    [SerializeField] private PlayerWeaponSlots weaponSlots;
    // Keep authored references so existing scenes migrate without missing fields.
    [SerializeField, HideInInspector] private WeaponSlotSilhouette weaponIcon;
    [SerializeField, HideInInspector] private TMP_Text ammoText;
    [SerializeField, HideInInspector] private Image[] ammoMarks;
    [SerializeField, HideInInspector] private Image reloadGauge, reloadGaugeBackground;

    [Header("Layout (1920 x 1080 canvas units)")]
    [SerializeField] private Vector2 screenMargin = new Vector2(40f, 40f);
    [SerializeField, Range(.65f, 1.5f)] private float hudScale = 1f;
    [SerializeField] private TMP_FontAsset font;
    [Header("Colors")]
    [SerializeField] private Color accent = new Color(.58f, .95f, 1f);
    [SerializeField] private Color lowAmmo = new Color(1f, .73f, .35f);
    [SerializeField] private Color emptyAmmo = new Color(1f, .35f, .34f);
    [SerializeField, Range(.05f, .5f)] private float lowAmmoFraction = .25f;
    [SerializeField] private Color panelColor = new Color(.025f, .045f, .065f, .85f);

    private static readonly Color Muted = new Color(.59f, .67f, .71f);
    private RectTransform panel, fill;
    private CanvasGroup group;
    private TMP_Text magazine, capacity, reserve, status, mode;
    private Image progress, edge, background;
    private WeaponSlotSilhouette silhouette;
    private WeaponRuntime shown;
    private int lastAmmo;
    private float shotPulse;
    private PlayerDamageReceiver health;

    private void LateUpdate() => RefreshNow();
    private void OnDisable()
    {
        if (group != null) group.alpha = 0f;
        shown = null; shotPulse = 0f;
    }

    public void RefreshNow()
    {
        if (!EnsureView()) return;
        var root = (RectTransform)transform;
        root.anchoredPosition = new Vector2(-screenMargin.x, screenMargin.y);
        root.localScale = Vector3.one * hudScale;
        var runtime = weaponSlots != null && weaponSlots.isActiveAndEnabled
            ? weaponSlots.GetRuntime(weaponSlots.SelectedSlot) : null;
        if (health == null && weaponSlots != null) health = weaponSlots.GetComponent<PlayerDamageReceiver>();
        bool visible = runtime != null && runtime.Data.Category != WeaponCategory.Melee
            && (health == null || health.IsAlive);
        group.alpha = visible ? 1f : 0f;
        if (!visible) { shown = null; shotPulse = 0f; return; }

        if (runtime != shown) { shown = runtime; lastAmmo = runtime.Ammo; shotPulse = 0f; }
        else if (runtime.Ammo < lastAmmo) shotPulse = 1f;
        lastAmmo = runtime.Ammo;
        shotPulse = Mathf.MoveTowards(shotPulse, 0f, Time.unscaledDeltaTime * 6f);

        int rounds = runtime.Ammo;
        bool empty = rounds == 0, low = rounds <= Mathf.CeilToInt(runtime.Data.MagazineSize * lowAmmoFraction);
        Color tint = empty ? emptyAmmo : low ? lowAmmo : accent;
        magazine.SetText("{0:00}", rounds);
        capacity.SetText("/ {0}", runtime.Data.MagazineSize);
        reserve.SetText("{0:00}", runtime.ReserveAmmo);
        magazine.color = Color.Lerp(empty || low ? tint : Color.white, accent, shotPulse * .35f);
        magazine.rectTransform.localScale = Vector3.one * (1f + shotPulse * .035f);
        reserve.color = runtime.ReserveAmmo == 0 ? lowAmmo : Color.white;
        mode.text = runtime.Data.FireMode == WeaponFireMode.Automatic ? "AUTO"
            : runtime.Data.FireMode == WeaponFireMode.Burst ? "BURST" : "SEMI";
        silhouette.Kind = runtime.Data.FireMode == WeaponFireMode.Automatic ? 1 : 2;

        if (runtime.IsReloading)
        {
            tint = accent;
            // Empty reloads have their own duration. Use the active runtime timer for both.
            float amount = runtime.ReloadDuration > 0f
                ? Mathf.Clamp01(1f - runtime.ReloadRemaining / runtime.ReloadDuration) : 1f;
            fill.sizeDelta = new Vector2(260f * amount, 3f);
            status.SetText("RELOADING  /  {0:1}s", runtime.ReloadRemaining);
        }
        else
        {
            fill.sizeDelta = new Vector2(260f * rounds / runtime.Data.MagazineSize, 3f);
            status.text = empty ? (runtime.ReserveAmmo > 0 ? "[R]  RELOAD" : "OUT OF AMMO")
                : runtime.ReserveAmmo == 0 ? "NO RESERVE"
                : low ? "[R]  RELOAD" : "";
        }
        progress.color = edge.color = tint;
        status.color = runtime.ReserveAmmo == 0 && !empty && !runtime.IsReloading ? lowAmmo : tint;
        background.color = panelColor;
    }

    private bool EnsureView()
    {
        if (panel != null) return true;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null || !(transform is RectTransform root)) return false;
        if (font == null) font = ammoText != null ? ammoText.font : TMP_Settings.defaultFontAsset;
        // Replace the old bullet marks / reload ring inside the existing HUD, without another canvas.
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        var oldBackground = GetComponent<Image>();
        if (oldBackground != null) { oldBackground.enabled = false; oldBackground.raycastTarget = false; }
        // The legacy RIGHT container has a fixed 100px size; anchor directly to its canvas for all aspect ratios.
        root.SetParent(canvas.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(1f, 0f);
        root.pivot = new Vector2(1f, 0f); root.sizeDelta = new Vector2(300f, 150f);
        panel = Rect("Ammo display", root, 0, 0, 300, 150);
        group = panel.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        background = Image("Backdrop", panel, 0, 0, 300, 150, panelColor);
        edge = Image("Top accent", panel, 0, 148, 300, 2, accent);
        mode = Label("Fire mode", 20, 119, 90, 19, 13, Muted);
        var icon = Rect("Weapon silhouette", panel, 218, 115, 62, 27);
        silhouette = icon.gameObject.AddComponent<WeaponSlotSilhouette>();
        silhouette.raycastTarget = false; silhouette.color = Muted;
        magazine = Label("Magazine", 16, 49, 110, 70, 64, Color.white);
        magazine.fontStyle = FontStyles.Bold;
        magazine.enableAutoSizing = true; magazine.fontSizeMin = 32f; magazine.fontSizeMax = 64f;
        capacity = Label("Capacity", 126, 58, 65, 28, 19, Muted);
        Image("Divider", panel, 198, 58, 1, 48, new Color(.65f, .8f, .85f, .22f));
        Label("Reserve label", 218, 95, 70, 17, 13, Muted).text = "RESERVE";
        reserve = Label("Reserve count", 216, 57, 70, 38, 30, Color.white);
        reserve.enableAutoSizing = true; reserve.fontSizeMin = 10f; reserve.fontSizeMax = 30f;
        Image("Track", panel, 20, 40, 260, 3, new Color(1f, 1f, 1f, .12f));
        progress = Image("Progress", panel, 20, 40, 260, 3, accent); fill = progress.rectTransform;
        status = Label("Status", 20, 11, 260, 20, 14, accent);
        return true;
    }

    private RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = gameObject.layer; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h);
        return rect;
    }
    private Image Image(string name, Transform parent, float x, float y, float w, float h, Color color)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    private TMP_Text Label(string name, float x, float y, float w, float h, float size, Color color)
    {
        var text = Rect(name, panel, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
}
