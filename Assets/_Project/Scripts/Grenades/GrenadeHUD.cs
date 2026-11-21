using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The class throwable on the HUD: a small plate of its own just left of the ammo display (AmmoHUD), at the screen's
/// corner where the ammo display isn't up (a Knight's fists): the throwable's silhouette (Throwables.Icon), how many are
/// left, its key, and while one cooks the fuse running out under it. It leans at its own angle (Persona
/// style: GrenadeRules hudYaw/hudRoll), pops when the count changes and blinks when some come back. It sits apart from the
/// weapon slots and touches none of them. While a cooking one is held, the cook ring of [MW] Quick Grenade closes in round
/// the crosshair (GrenadeCookRing, GrenadeIndicators.csv Ring.*), where the eyes are. PlayerGrenades (this PC's player)
/// says what to show every frame.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.AmmoHud + 1)]
public sealed class GrenadeHUD : MonoBehaviour
{
    private const float Width = 132f, Height = 58f, PopSeconds = .22f, ShakeSeconds = .3f;
    private static readonly Color PanelColor = new Color(.025f, .045f, .065f, .85f), Muted = new Color(.59f, .67f, .71f);
    private static GrenadeHUD current;
    private static float lookForAmmoAt;

    /// <summary>
    /// The cook ring: [MW] Quick Grenade and Grenade Indicator's Indicator:DrawTimer as it is (the user, 2026-11-22:
    /// "원본대로 하라고"), GrenadeIndicators.csv rows Ring.* (1080p canvas px, seconds). Times are from the pin.
    /// </summary>
    [Serializable]
    public sealed class RingLook
    {
        public bool on = true;
        /// <summary>The inside radius at the pin (px), closing to 0 as the fuse burns; the width outward; the sides.</summary>
        public float radius = 40f, thickness = 2f;
        public int segments = 30;
        /// <summary>Shown this long after the pin (s); turns late this long before the fuse ends (s).</summary>
        public float showAfter = .5f, redLead = 1.5f;
        /// <summary>The time constants it comes up, fades and turns colour by (s), and the strength under which it is gone.</summary>
        public float fadeInTau = .3f, fadeOutTau = .1f, colorTau = .3f, gone = .014f;
        public Color color = Color.white, lateColor = Color.red;
    }

    private readonly RingLook ringLook = new RingLook();
    private GrenadeCookRing ring;
    private Transform crosshair;
    // ringHeld: how long the one in hand has burnt (s), below 0 for none; burnt and fuse: the last told, kept while it
    // fades (the original's m_FragSafeTime stops at the throw); the ring's strength and colour as they follow.
    private float ringHeld = -1f, burnt, fuseSeconds, ringAlpha, lookForCrosshairAt;
    private Color ringColor = Color.white;

    private RectTransform root, plate, fuseFill;
    private CanvasGroup group;
    private Image edge, fuseTrack, fuseBar;
    private RawImage icon;
    private TMP_Text count, label, key;
    private AmmoHUD ammo;
    private PlayerWeaponSlots slots;
    private Throwable kind;
    private int shown = -1;
    private bool aiming, wanted;
    private float fuseShare = -1f, popAge = 1f, shakeAge = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        lookForAmmoAt = 0f;
    }

    /// <summary>
    /// This frame's throwable: its kind (None hides the plate), how many are left, whether G is held to aim, and while
    /// cooking the share of its fuse left (below 0: not cooking).
    /// </summary>
    internal static void Show(Throwable kind, int count, bool aiming, float fuseShare)
    {
        var hud = Ensure();
        if (hud == null) return;
        // With the Mk II HUD on (2026-11-22) its grenade tile shows the count, so the plate stays away; the cook ring
        // (DrawRing) still draws either way.
        hud.wanted = kind != Throwable.None && !MkHud.On;
        if (hud.kind != kind) { hud.kind = kind; hud.shown = -1; if (hud.icon != null) hud.icon.texture = Throwables.Icon(kind); }
        if (count != hud.shown)
        {
            if (hud.shown >= 0) hud.popAge = 0f;
            hud.shown = count;
            if (hud.count != null) hud.count.SetText("×{0}", count);
        }
        hud.aiming = aiming;
        hud.fuseShare = fuseShare;
    }

    /// <summary>Nothing to show (no player, or a class that has no throwable yet).</summary>
    internal static void Hide()
    {
        if (current == null) return;
        current.wanted = false;
        current.ringHeld = -1f;
    }

    /// <summary>
    /// This frame's cook ring round the crosshair: how long the cooking throwable in hand has burnt and how long its fuse
    /// is (s); held below 0 for none (it fades out at the size it was).
    /// </summary>
    internal static void Ring(float held, float fuse)
    {
        var hud = Ensure();
        if (hud == null) return;
        hud.ringHeld = held;
        if (held < 0f) return;
        hud.burnt = held;
        hud.fuseSeconds = fuse;
    }

    /// <summary>
    /// The cook ring is on the screen: the gun's crosshair steps aside meanwhile (WeaponController.UpdateCrosshair), since
    /// the addon's quick grenade is a weapon of its own, so the gun's crosshair isn't drawn while it is in hand.
    /// </summary>
    internal static bool RingShown => current != null && current.ringAlpha > 0f;

    /// <summary>Some came back (an ammo pile, the shop, a Support, a knife taken back): the plate blinks.</summary>
    internal static void Blink(Color color)
    {
        if (current != null && current.plate != null) HudBlink.On(current.plate, color);
    }

    /// <summary>G with none left: the plate gives a little shake (nothing is lost, it only says so).</summary>
    internal static void Refused()
    {
        if (current != null) current.shakeAge = 0f;
    }

    // Found or made beside the ammo display, under the same side of the HUD.
    private static GrenadeHUD Ensure()
    {
        if (current != null) return current;
        if (Time.unscaledTime < lookForAmmoAt) return null;
        lookForAmmoAt = Time.unscaledTime + 1f;
        var ammo = FindAnyObjectByType<AmmoHUD>();
        if (ammo == null || ammo.transform.parent == null) return null;
        var host = new GameObject("Grenade display", typeof(RectTransform));
        host.layer = ammo.gameObject.layer;
        host.transform.SetParent(ammo.transform.parent, false);
        current = host.AddComponent<GrenadeHUD>();
        current.ammo = ammo;
        current.Build();
        return current;
    }

    private void Build()
    {
        GameTables.BindNested(GrenadeIndicators.TableName, GrenadeIndicators.RingRow, this, ringLook);
        ringColor = ringLook.color;
        root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(1f, 0f);
        root.sizeDelta = new Vector2(Width, Height);
        plate = Rect("Plate", root, 0f, 0f, Width, Height);
        group = plate.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        group.alpha = 0f;
        Image("Backdrop", plate, 0f, 0f, Width, Height, PanelColor);
        edge = Image("Top accent", plate, 0f, Height - 2f, Width, 2f, Color.white);
        icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<RawImage>();
        icon.rectTransform.SetParent(plate, false);
        Place(icon.rectTransform, 8f, 12f, 36f, 36f);
        icon.raycastTarget = false;
        icon.texture = Throwables.Icon(kind);
        // The name along the top, the count under it, the key at the count's right: none over another.
        label = Label("Name", 50f, 38f, Width - 56f, 14f, 11f, Muted);
        count = Label("Count", 50f, 6f, 46f, 32f, 30f, Color.white);
        count.fontStyle = FontStyles.Bold;
        key = Label("Key", Width - 36f, 12f, 30f, 16f, 11f, Muted);
        key.alignment = TextAlignmentOptions.MidlineRight;
        fuseTrack = Image("Fuse track", plate, 8f, 3f, Width - 16f, 3f, new Color(1f, 1f, 1f, .12f));
        fuseBar = Image("Fuse", plate, 8f, 3f, Width - 16f, 3f, KitStyle.Accent);
        fuseFill = fuseBar.rectTransform;
        fuseTrack.enabled = fuseBar.enabled = false;
    }

    private void LateUpdate()
    {
        if (root == null) return;
        DrawRing();
        var info = Throwables.Of(kind);
        bool visible = wanted && info != null && ammo != null;
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (!visible) return;
        Place();
        float dt = Time.unscaledDeltaTime;
        popAge += dt;
        shakeAge += dt;
        // A change of count pops the number; a refused press shakes the plate a little.
        float pop = 1f + .14f * (1f - Mathf.Clamp01(popAge / PopSeconds));
        count.rectTransform.localScale = Vector3.one * pop;
        float shake = shakeAge < ShakeSeconds ? Mathf.Sin(shakeAge * 60f) * 4f * (1f - shakeAge / ShakeSeconds) : 0f;
        plate.anchoredPosition = new Vector2(shake, 0f);
        bool empty = shown <= 0;
        count.color = empty ? Muted : Color.white;
        icon.color = empty ? new Color(1f, 1f, 1f, .35f) : Color.white;
        label.text = info.Name;
        key.text = KeyBindings.Bracketed("Player/Grenade");
        // Aiming: the edge goes pink, the game's one accent; otherwise the throwable's own colour.
        edge.color = aiming ? KitStyle.Accent : KitStyle.With(info.Tint, .9f);
        bool cooking = fuseShare >= 0f;
        fuseTrack.enabled = fuseBar.enabled = cooking;
        if (cooking)
        {
            fuseFill.sizeDelta = new Vector2((Width - 16f) * Mathf.Clamp01(fuseShare), 3f);
            // The cook ring's colour: white, turning red over the fuse's last seconds once the ring is up. It used to
            // swing between pink and white four times a second, a flicker the user's rule forbids.
            fuseBar.color = ringColor;
        }
    }

    // The cook ring as the addon draws it: shown once the fuse has burnt showAfter, the strength coming up (fadeInTau) and
    // going (fadeOutTau) by the original's frame-time Lerp, white turning red over the fuse's last redLead seconds
    // (colorTau), the inside radius closing from radius to nothing as the fuse burns; let go, it fades at the size it was.
    // Made beside the HUD crosshair the first time it shows and kept on its middle (the screen's, where the addon drew it).
    private void DrawRing()
    {
        var look = ringLook;
        float dt = Time.unscaledDeltaTime;
        if (!look.on || ringHeld < look.showAfter)
        {
            if (ringAlpha > look.gone) ringAlpha = Mathf.Lerp(ringAlpha, 0f, Step(dt, look.fadeOutTau));
            else
            {
                ringAlpha = 0f;
                ringColor = look.color;
                if (ring != null && ring.gameObject.activeSelf) ring.gameObject.SetActive(false);
                return;
            }
        }
        else ringAlpha = Mathf.Lerp(ringAlpha, 1f, Step(dt, look.fadeInTau));
        ringColor = Color.Lerp(ringColor, burnt >= fuseSeconds - look.redLead ? look.lateColor : look.color, Step(dt, look.colorTau));
        if (ring == null)
        {
            if (Time.unscaledTime < lookForCrosshairAt) return;
            lookForCrosshairAt = Time.unscaledTime + 1f;
            var view = DynamicCrosshairView.FindHud();
            if (view == null || view.transform.parent == null) return;
            crosshair = view.transform;
            ring = GrenadeCookRing.Create(crosshair.parent);
        }
        bool shown = crosshair != null;
        if (ring.gameObject.activeSelf != shown) ring.gameObject.SetActive(shown);
        if (!shown) return;
        ring.rectTransform.position = crosshair.position;
        ring.rectTransform.rotation = crosshair.rotation;
        float share = fuseSeconds > 0f ? Mathf.Clamp01(burnt / fuseSeconds) : 1f;
        var tint = ringColor;
        tint.a = ringAlpha;
        ring.Show(look.radius - look.radius * share, look.thickness, look.segments, tint);
    }

    // Lerp(FrameTime() / tau, ...): the share of the way a value goes this frame, as the addon steps it.
    private static float Step(float dt, float tau) => Mathf.Clamp01(dt / Mathf.Max(1e-4f, tau));

    // Just left of the ammo display, bottom edges level; where the ammo display isn't up, in its corner.
    private void Place()
    {
        var ammoRect = (RectTransform)ammo.transform;
        float scale = ammoRect.localScale.x;
        if (slots == null && PlayerRoster.Local != null) slots = PlayerRoster.Local.GetComponent<PlayerWeaponSlots>();
        var runtime = slots != null && slots.isActiveAndEnabled ? slots.GetRuntime(slots.SelectedSlot) : null;
        bool ammoUp = runtime != null && runtime.Data.Category != WeaponCategory.Melee;
        float gap = Throwables.Rule("hudGap", 14f);
        root.anchoredPosition = ammoRect.anchoredPosition + (ammoUp ? new Vector2(-(300f * scale + gap), 0f) : Vector2.zero);
        root.localScale = Vector3.one * scale;
        root.localRotation = Quaternion.Euler(0f, Throwables.Rule("hudYaw", -7f), Throwables.Rule("hudRoll", 3f));
    }

    private RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = gameObject.layer;
        rect.SetParent(parent, false);
        Place(rect, x, y, w, h);
        return rect;
    }

    private static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(w, h);
    }

    private Image Image(string name, Transform parent, float x, float y, float w, float h, Color color)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text Label(string name, float x, float y, float w, float h, float size, Color color)
    {
        var text = Rect(name, plate, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        // The ammo display's text role (Fonts.csv), so the two read as one set.
        FontRoles.Apply(text, FontRoles.Ammo);
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = FontRoles.ClippedOverflow(text);
        return text;
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
        if (ring != null) Destroy(ring.gameObject);
    }
}
