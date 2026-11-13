using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Magazine and finite reserve readout. Presentation never changes ammunition.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.AmmoHud)]
public sealed class AmmoHUD : MonoBehaviour
{
    [SerializeField] private PlayerWeaponSlots weaponSlots;

    [Header("Layout (1920 x 1080 canvas units)")]
    [SerializeField] private Vector2 screenMargin = new Vector2(40f, 40f);
    [SerializeField, Range(.65f, 1.5f)] private float hudScale = 1f;
    [Header("Colors")]
    [SerializeField] private Color accent = new Color(.58f, .95f, 1f);
    [SerializeField] private Color lowAmmo = new Color(1f, .73f, .35f);
    [SerializeField] private Color emptyAmmo = new Color(1f, .35f, .34f);
    [SerializeField, Range(.05f, .5f)] private float lowAmmoFraction = .25f;
    [SerializeField] private Color panelColor = new Color(.025f, .045f, .065f, .85f);

    private static readonly Color Muted = new Color(.59f, .67f, .71f);
    private RectTransform panel, fill, canvasRect;
    private CanvasGroup group;
    private TMP_Text magazine, capacity, reserve, status, mode;
    private Image progress, edge, background, picture;
    private WeaponSlotSilhouette silhouette;
    private WeaponRuntime shown;
    private int lastAmmo;
    private float shotPulse;
    private PlayerDamageReceiver health;

    private static AmmoHUD current;
    // The held gun's controller, for its look-over (Y), the only time its kill count shows (GunKills).
    private WeaponController weapon;
    // A rank the gun reached since its last look-over shows on the status line for this long (s) as the next one begins.
    private const float RankUpHold = 2.5f;
    private static readonly Color RankColor = new Color(1f, .6f, .18f);
    private float rankUpLeft;
    private string rankUpText;
    private WeaponData rankUpGun;

    /// <summary>The display blinks once: what it shows just changed by something taken (FieldPickups: ammo, a gun).</summary>
    public static void Blink(Color color)
    {
        if (current != null && current.panel != null) HudBlink.On(current.panel, color);
    }

    private void OnEnable()
    {
        current = this;
        GunKills.RankUpAnnounced += OnRankUpAnnounced;
    }
    private void LateUpdate() => RefreshNow();

    // A rank the gun reached in the fight, announced as it is looked over: the status line says it for a moment and the
    // panel blinks the counter's orange. Nothing shows when the rank comes (user, 2026-11-12: "킬스트릭 스탯은 무기 관찰할
    // 때만 뜨게 할 것").
    private void OnRankUpAnnounced(WeaponData gun)
    {
        if (gun == null) return;
        rankUpText = Texts.Format("gunkills.rankup", GunKills.Title(gun));
        rankUpGun = gun;
        rankUpLeft = RankUpHold;
        Blink(RankColor);
    }

    private void OnDisable()
    {
        GunKills.RankUpAnnounced -= OnRankUpAnnounced;
        if (current == this) current = null;
        HudRoom.Free(this);
        if (group != null) group.alpha = 0f;
        shown = null; shotPulse = 0f;
    }

    public void RefreshNow()
    {
        if (!EnsureView()) return;
        var root = (RectTransform)transform;
        // Its group is centred on the canvas, so the bottom-right corner is half the canvas away.
        Vector2 half = canvasRect.rect.size * .5f;
        root.anchoredPosition = new Vector2(half.x - screenMargin.x, -half.y + screenMargin.y);
        root.localScale = Vector3.one * hudScale;
        if (weaponSlots == null && PlayerRoster.Local != null) weaponSlots = PlayerRoster.Local.GetComponent<PlayerWeaponSlots>();
        var runtime = weaponSlots != null && weaponSlots.isActiveAndEnabled
            ? weaponSlots.GetRuntime(weaponSlots.SelectedSlot) : null;
        if (health == null && weaponSlots != null) health = weaponSlots.GetComponent<PlayerDamageReceiver>();
        bool visible = runtime != null && runtime.Data.Category != WeaponCategory.Melee
            && (health == null || health.IsAlive);
        group.alpha = visible ? 1f : 0f;
        // While it shows, the display stays under the right edge's column of callout windows, so they keep above it: on
        // a 21:9 screen (935 px of canvas) a second window came down onto it.
        if (visible && HudRoom.Measure(panel, out float near, out float far, out float top, out float bottom))
            HudRoom.Take(this, near, far, top, bottom, HudRoom.Way.Over);
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
        mode.text = runtime.FireMode == WeaponFireMode.Automatic ? "AUTO"
            : runtime.FireMode == WeaponFireMode.Burst ? "BURST" : runtime.FireMode == WeaponFireMode.Fanning ? "FAN" : "SEMI";
        silhouette.Kind = runtime.FireMode == WeaponFireMode.Automatic ? 1 : 2;
        // The gun's own picture (WeaponIcons) where it has one; the vector outline otherwise.
        var icon = WeaponIcons.For(runtime.Data);
        if (picture.sprite != icon) picture.sprite = icon;
        picture.enabled = icon != null;
        silhouette.enabled = icon == null;

        if (runtime.IsReloading)
        {
            tint = accent;
            // Empty reloads have their own duration. Use the active runtime timer for both.
            float amount = runtime.ReloadDuration > 0f
                ? Mathf.Clamp01(1f - runtime.ReloadRemaining / runtime.ReloadDuration) : 1f;
            fill.sizeDelta = new Vector2(260f * amount, 3f);
            // TextMeshPro's own format ({0:1} is one decimal), so the template goes to SetText as written in Texts.csv.
            status.SetText(Texts.Get("hud.ammo.reloading"), runtime.ReloadRemaining);
        }
        else
        {
            fill.sizeDelta = new Vector2(260f * rounds / runtime.Data.MagazineSize, 3f);
            // A cut-short reload with only its bolt left counts as empty: nothing is chambered (WeaponRuntime.ChamberDue).
            status.text = runtime.ChamberDue ? "[R]  RELOAD"
                : empty ? (runtime.ReserveAmmo > 0 ? "[R]  RELOAD" : "OUT OF AMMO")
                : runtime.ReserveAmmo == 0 ? "NO RESERVE"
                : low ? "[R]  RELOAD" : "";
        }
        progress.color = edge.color = tint;
        status.color = runtime.ReserveAmmo == 0 && !empty && !runtime.IsReloading ? lowAmmo : tint;
        // Only while the gun is looked over (Y): first a rank it reached since its last look-over (GunKills keeps it
        // waiting), then its rank and kill count. A look-over cut short takes the rank-up notice with it.
        if (weapon == null && weaponSlots != null) weapon = weaponSlots.GetComponent<WeaponController>();
        bool lookedOver = weapon != null && weapon.IsInspecting && !runtime.IsReloading;
        if (lookedOver) GunKills.AnnounceRankUp(runtime.Data);
        else rankUpLeft = 0f;
        rankUpLeft = Mathf.Max(0f, rankUpLeft - Time.unscaledDeltaTime);
        if (lookedOver)
        {
            status.text = rankUpLeft > 0f && rankUpGun == runtime.Data && !string.IsNullOrEmpty(rankUpText) ? rankUpText
                : Texts.Format("gunkills.inspect", GunKills.Title(runtime.Data), GunKills.Kills(runtime.Data), GunKills.Headshots(runtime.Data));
            status.color = RankColor;
        }
        background.color = panelColor;
    }

    private bool EnsureView()
    {
        if (panel != null) return true;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null || !(transform is RectTransform root)) return false;
        // The RIGHT container has a fixed 100px size, so the display finds the screen's corner from the canvas's
        // own size (RefreshNow) and stays in RIGHT, standing on the HUD's right side (HudPlane, HudBoard) with the rest.
        canvasRect = (RectTransform)canvas.rootCanvas.transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
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
        // Over the outline's box: the icon canvas's shape inside it, the gun at its own size.
        picture = Image("Weapon picture", panel, 218, 115, 62, 27, Muted);
        picture.preserveAspect = true;
        picture.enabled = false;
        magazine = Label("Magazine", 16, 49, 110, 70, 64, Color.white);
        magazine.fontStyle = FontStyles.Bold;
        magazine.overflowMode = FontRoles.ClippedOverflow(magazine);
        magazine.enableAutoSizing = true; magazine.fontSizeMin = 32f; magazine.fontSizeMax = 64f;
        capacity = Label("Capacity", 126, 58, 65, 28, 19, Muted);
        Image("Divider", panel, 198, 58, 1, 48, new Color(.65f, .8f, .85f, .22f));
        Label("Reserve label", 218, 95, 70, 17, 13, Muted).text = Texts.Get("hud.ammo.reserve");
        reserve = Label("Reserve count", 216, 57, 70, 38, 30, Color.white);
        reserve.enableAutoSizing = true; reserve.fontSizeMin = 10f; reserve.fontSizeMax = 30f;
        Image("Track", panel, 20, 40, 260, 3, new Color(1f, 1f, 1f, .12f));
        progress = Image("Progress", panel, 20, 40, 260, 3, accent); fill = progress.rectTransform;
        status = Label("Status", 20, 11, 260, 20, 14, accent);
        // The look-over's rank and kill line is longer than the reload words: it shrinks to fit rather than being cut.
        status.enableAutoSizing = true; status.fontSizeMin = 9f; status.fontSizeMax = 14f;
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
        // The Ammo text role (Fonts.csv). Until 2026-11-12 this borrowed the hidden prefab label's font, TMP's sample
        // LiberationSans, so the status line's Korean came from the fallback in another face.
        FontRoles.Apply(text, FontRoles.Ammo);
        text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = FontRoles.ClippedOverflow(text);
        return text;
    }
}
