using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Damage numbers for enemies and practice targets. Created on the first hit during Play; no scene setup.
/// Each number follows the transform it hit (knockback, shove, flight). If that object is destroyed,
/// for example by a death blow, the number finishes at the last position instead of vanishing with it.
/// Tuning lives in Resources/Combat/Feedback (Damage popups).
/// </summary>
// Runs after CameraViewPunch (10000) so numbers stay on their targets in the kicked view.
[DisallowMultipleComponent, DefaultExecutionOrder(10010)]
public sealed class DamagePopupManager : MonoBehaviour
{
    private const int MaxPopups = 48;
    private static DamagePopupManager instance;
    public static DamagePopupManager Instance => instance;
    public int ActiveCount => active.Count;

    private RectTransform canvasRect;
    private readonly List<Popup> active = new List<Popup>();
    private readonly Stack<Popup> pool = new Stack<Popup>();
    private readonly List<Collider> colliders = new List<Collider>();

    private sealed class Popup
    {
        public RectTransform root, content;
        public CanvasGroup group;
        public Image glow;
        public RawImage critical, kill, slash, counterSlash;
        public TextMeshProUGUI label, shade;
        public Transform follow;
        public Vector3 offset, anchor;
        public Vector2 jitter;
        public float age, lifetime, startScale, settledScale;
        public Color tint;
        public bool deathBlow, punch;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
    }

    private static void OnDamaged(CombatDamageReport report)
    {
        if (!Application.isPlaying || report.Target == null) return;
        if (instance == null)
        {
            var root = new GameObject("Damage Popups", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = root.AddComponent<DamagePopupManager>();
        }
        instance.Show(report);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelAssets.GroupRuntimeRoot(gameObject, "Screen");
        var canvas = GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
        var scaler = GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        canvasRect = (RectTransform)transform;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    public void Show(CombatDamageReport report)
    {
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        var hit = report.Hit;
        var popup = Rent();
        popup.follow = report.Target.transform;
        popup.anchor = AnchorPoint(report);
        // World-space offset, not local: the number does not orbit when the enemy turns, only travels with it.
        popup.offset = popup.anchor - popup.follow.position;
        popup.deathBlow = report.IsKillingBlow;
        popup.lifetime = Mathf.Max(.1f, popup.deathBlow ? settings.deathBlowLifetime : settings.popupLifetime);
        popup.age = 0f;
        // Finishing blows stay closer to the hit so they read as the centrepiece.
        Vector2 jitter = settings.popupJitter * (popup.deathBlow ? .5f : 1f);
        // Square-root bias pushes numbers toward the edges instead of piling up in the middle.
        float side = Random.Range(-1f, 1f);
        side = Mathf.Sign(side) * Mathf.Sqrt(Mathf.Abs(side));
        popup.jitter = new Vector2(side * jitter.x, Random.Range(-jitter.y * .5f, jitter.y));

        int amount = Mathf.Max(1, Mathf.RoundToInt(hit.Amount));
        Vector2 range = settings.popDamageRange;
        float weight = Mathf.InverseLerp(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y), amount);
        weight = 1f - (1f - weight) * (1f - weight); // early damage steps read clearly, huge hits saturate
        popup.startScale = Mathf.Max(1f, Mathf.Lerp(settings.popStartScale.x, settings.popStartScale.y, weight));

        // Anything the player did not deal (enemy friendly fire, falls, hazards) reads as grey environment damage.
        bool environmental = !IsFromPlayer(hit);
        bool critical = report.IsCritical && !environmental;
        // Priority: environment (grey) > finishing blow (orange) > counter (sky blue) > critical (gold) > normal (white).
        Color tint = environmental ? settings.environmentDamageColor
            : popup.deathBlow ? settings.deathBlowColor
            : hit.IsCounter ? settings.counterColor
            : critical ? settings.criticalColor : settings.normalDamageColor;
        float size = Mathf.Max(8f, settings.damageFontSize);
        popup.root.gameObject.SetActive(true);
        popup.label.fontSize = size;
        popup.label.color = tint;
        popup.tint = tint;
        // Critical, counter and the player's finishing blow get the punch (hit-stop, white flash, icon pop).
        popup.punch = !environmental && (critical || hit.IsCounter || popup.deathBlow);
        popup.settledScale = !popup.punch ? 1f : popup.deathBlow ? settings.deathBlowSettledScale : settings.punchSettledScale;
        string value = amount.ToString();
        popup.label.text = value;
        // Finishing blow: orange digits with a crimson drop shadow (a copy offset down-right, behind the number).
        bool finisher = popup.deathBlow && !environmental;
        popup.shade.gameObject.SetActive(finisher);
        if (finisher)
        {
            popup.shade.fontSize = size; popup.shade.text = value;
            popup.shade.color = settings.deathBlowShadowColor;
        }
        Vector2 text = popup.label.GetPreferredValues(value);

        float icon = size * .95f, gap = size * .12f;
        // Left icon slot: counter (return arrow, sky blue) takes it, otherwise critical (burst, gold).
        bool counter = hit.IsCounter && !environmental;
        bool leftIcon = counter || critical;
        float width = text.x + (leftIcon ? icon + gap : 0f) + (popup.deathBlow ? icon + gap : 0f);
        float x = -width * .5f;
        popup.critical.gameObject.SetActive(leftIcon);
        if (leftIcon)
        {
            popup.critical.texture = counter
                ? (settings.counterIcon != null ? settings.counterIcon : CombatUIAssets.CounterArrow)
                : (settings.criticalIcon != null ? settings.criticalIcon : CombatUIAssets.CriticalBurst);
            // Icon follows the number: sky blue / gold normally, orange when the hit is also a finishing blow.
            popup.critical.color = tint;
            Place(popup.critical.rectTransform, x + icon * .5f, icon, icon);
            x += icon + gap;
        }
        Place(popup.label.rectTransform, x + text.x * .5f, text.x + 6f, size * 1.3f);
        Place(popup.shade.rectTransform, x + text.x * .5f, text.x + 6f, size * 1.3f);
        popup.shade.rectTransform.anchoredPosition += new Vector2(size * .06f, -size * .06f);
        float labelCentre = x + text.x * .5f;
        x += text.x;
        // Counter: a white sword cut behind the number, drawn from top-right to bottom-left.
        popup.counterSlash.gameObject.SetActive(counter);
        if (counter)
        {
            float length = text.x + size * 1.6f, angle = settings.counterSlashAngle * Mathf.Deg2Rad;
            var cut = popup.counterSlash.rectTransform;
            popup.counterSlash.texture = CombatUIAssets.BladeSlash;
            // Light, but clearly carrying the number's colour. Pale colours are enriched first,
            // otherwise mixing them into white still reads as plain white.
            Color.RGBToHSV(tint, out float hue, out float saturation, out float brightness);
            Color rich = Color.HSVToRGB(hue, Mathf.Max(saturation, .6f), Mathf.Max(brightness, .95f));
            Color slashTint = Color.Lerp(settings.counterSlashColor, rich, settings.counterSlashTint);
            slashTint.a = settings.counterSlashColor.a;
            popup.counterSlash.color = slashTint;
            cut.sizeDelta = new Vector2(length, size * settings.counterSlashThickness);
            // Pivot sits on the top-right end, so growing the x scale draws the cut toward the bottom-left.
            cut.anchoredPosition = new Vector2(labelCentre, 0f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (length * .5f);
            cut.localRotation = Quaternion.Euler(0f, 0f, settings.counterSlashAngle);
            cut.localScale = new Vector3(0f, 1f, 1f);
        }
        popup.kill.gameObject.SetActive(popup.deathBlow);
        if (popup.deathBlow)
        {
            popup.kill.texture = settings.killIcon != null ? settings.killIcon : CombatUIAssets.Skull;
            popup.kill.color = settings.killIconColor;
            Place(popup.kill.rectTransform, x + gap + icon * .5f, icon, icon);
        }
        popup.glow.gameObject.SetActive(popup.deathBlow);
        popup.slash.gameObject.SetActive(popup.deathBlow);
        if (popup.deathBlow)
        {
            // Small glow that flares once, plus a slash line sweeping out behind the number.
            popup.glow.color = settings.deathBlowGlowColor;
            Place(popup.glow.rectTransform, 0f, width + size * 1.3f, size * 1.6f);
            popup.slash.texture = CombatUIAssets.SlashLine;
            popup.slash.color = settings.deathBlowSlashColor;
            Place(popup.slash.rectTransform, 0f, width + size * 1.8f, size * .12f);
            popup.slash.rectTransform.anchoredPosition = new Vector2(0f, -size * .08f);
        }
        popup.root.SetAsLastSibling();
        active.Add(popup);
        Animate(popup, Camera.main, settings);
    }

    private void LateUpdate()
    {
        var view = Camera.main;
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        float dt = Time.unscaledDeltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var popup = active[i];
            popup.age += dt;
            if (popup.age >= popup.lifetime) { Return(popup); active.RemoveAt(i); continue; }
            Animate(popup, view, settings);
        }
    }

    private void Animate(Popup popup, Camera view, CombatFeedbackSettings settings)
    {
        // Unity's null check is true after Destroy, so a killed enemy leaves the last anchor in place.
        if (popup.follow != null) popup.anchor = popup.follow.position + popup.offset;
        Vector3 screen = view != null ? view.WorldToScreenPoint(popup.anchor) : Vector3.zero;
        if (view == null || screen.z <= view.nearClipPlane) { popup.group.alpha = 0f; return; }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        float life = Mathf.Clamp01(popup.age / popup.lifetime);
        float rise = settings.popupRise * (1f - (1f - life) * (1f - life));
        popup.root.anchoredPosition = local + popup.jitter + Vector2.up * rise;
        if (popup.deathBlow && popup.age < settings.deathBlowShakeSeconds)
        {
            // Finishing blow rattles on impact and settles.
            float decay = 1f - popup.age / settings.deathBlowShakeSeconds;
            popup.root.anchoredPosition += new Vector2(Mathf.Sin(popup.age * 97f), Mathf.Cos(popup.age * 71f))
                * (settings.deathBlowShake * decay * decay);
        }

        // Big on the first frame, then a fast ease-out back to normal size.
        float pop;
        if (popup.punch)
        {
            // Punch: freeze at the peak for a moment, snap back, dip slightly, settle a little larger than normal.
            // Finishing blows open bigger and hold the freeze longer than criticals and counters.
            float peak = popup.startScale * (popup.deathBlow ? settings.deathBlowStartBoost : settings.punchStartBoost);
            float hold = popup.deathBlow ? settings.deathBlowHoldSeconds : settings.punchHoldSeconds;
            float snap = Mathf.Max(.01f, settings.punchShrinkSeconds);
            if (popup.age < hold) pop = peak;
            else
            {
                float t = Mathf.Clamp01((popup.age - hold) / snap);
                pop = Mathf.Lerp(peak, 1f, 1f - Mathf.Pow(1f - t, 3f));
                if (t >= 1f) pop -= settings.punchSettleDip * Mathf.Sin(Mathf.Clamp01((popup.age - hold - snap) / .12f) * Mathf.PI);
            }
            pop *= popup.settledScale;
            float flash = settings.punchFlashSeconds > 0f ? Mathf.Clamp01(1f - popup.age / settings.punchFlashSeconds) : 0f;
            popup.label.color = Color.Lerp(popup.tint, Color.white, flash);
            float iconT = 1f - Mathf.Pow(1f - Mathf.Clamp01(popup.age / Mathf.Max(.01f, settings.punchIconPopSeconds)), 3f);
            var iconScale = Vector3.one * (1.6f - .6f * iconT);
            var iconTwist = Quaternion.Euler(0f, 0f, (1f - iconT) * 25f);
            popup.critical.rectTransform.localScale = popup.kill.rectTransform.localScale = iconScale;
            popup.critical.rectTransform.localRotation = popup.kill.rectTransform.localRotation = iconTwist;
        }
        else
        {
            float shrink = Mathf.Clamp01(popup.age / Mathf.Max(.01f, settings.popShrinkSeconds));
            pop = Mathf.Lerp(popup.startScale, 1f, 1f - Mathf.Pow(1f - shrink, 3f));
        }
        float distance = Vector3.Distance(view.transform.position, popup.anchor);
        float far = Mathf.Clamp(settings.popupFullSizeDistance / Mathf.Max(.01f, distance), settings.popupMinimumDistanceScale, 1f);
        popup.content.localScale = Vector3.one * (pop * far);

        popup.group.alpha = Mathf.Clamp01((popup.lifetime - popup.age) / Mathf.Max(.01f, settings.popupFadeSeconds));
        if (popup.counterSlash.gameObject.activeSelf)
        {
            float cut = 1f - Mathf.Pow(1f - Mathf.Clamp01(popup.age / .1f), 3f);
            popup.counterSlash.rectTransform.localScale = new Vector3(cut, 1f, 1f);
        }
        if (popup.deathBlow)
        {
            // The light flares once on impact and then holds steady (no breathing).
            float flare = 1f - Mathf.Clamp01(popup.age / .2f);
            Color glow = settings.deathBlowGlowColor;
            glow.a *= .5f + .4f * flare;
            popup.glow.color = glow;
            popup.glow.rectTransform.localScale = Vector3.one * (1f + flare * .25f);
            float sweep = 1f - Mathf.Pow(1f - Mathf.Clamp01(popup.age / .12f), 3f);
            popup.slash.rectTransform.localScale = new Vector3(sweep, 1f, 1f);
        }
    }

    private static bool IsFromPlayer(DamageHit hit)
        => hit.Kind != DamageKind.Fall && hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() != null;

    private Vector3 AnchorPoint(CombatDamageReport report)
    {
        var hit = report.Hit;
        Bounds body = BodyBounds(report.Target);
        Vector3 upper = new Vector3(body.center.x, body.center.y + body.extents.y * .45f, body.center.z);
        // Executions report the feet; tests and some sources report no contact point. Use the upper body then.
        if (hit.Kind == DamageKind.Execution || hit.Point == Vector3.zero) return upper;
        Bounds grown = body; grown.Expand(1f);
        return grown.Contains(hit.Point) ? hit.Point + Vector3.up * .12f : upper;
    }

    private Bounds BodyBounds(Component target)
    {
        target.GetComponentsInChildren(colliders);
        bool found = false; Bounds bounds = default;
        foreach (var collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        colliders.Clear();
        return found ? bounds : new Bounds(target.transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
    }

    private Popup Rent()
    {
        if (active.Count >= MaxPopups) { var oldest = active[0]; active.RemoveAt(0); Return(oldest); }
        return pool.Count > 0 ? pool.Pop() : Build();
    }

    private void Return(Popup popup)
    {
        popup.follow = null;
        popup.root.gameObject.SetActive(false);
        pool.Push(popup);
    }

    private Popup Build()
    {
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        var popup = new Popup();
        popup.root = Rect("Damage Popup", transform);
        popup.group = popup.root.gameObject.AddComponent<CanvasGroup>();
        popup.group.interactable = popup.group.blocksRaycasts = false;
        popup.content = Rect("Content", popup.root);

        popup.glow =Rect("Death Blow Glow", popup.content).gameObject.AddComponent<Image>();
        popup.glow.sprite = CombatUIAssets.SoftGlow; popup.glow.type = Image.Type.Sliced;
        popup.glow.raycastTarget = false;

        popup.slash = Rect("Death Blow Slash", popup.content).gameObject.AddComponent<RawImage>();
        popup.slash.raycastTarget = false;

        popup.counterSlash = Rect("Counter Slash", popup.content).gameObject.AddComponent<RawImage>();
        popup.counterSlash.raycastTarget = false;
        popup.counterSlash.rectTransform.pivot = new Vector2(1f, .5f);
        popup.counterSlash.gameObject.SetActive(false);

        popup.critical = Rect("Critical Icon", popup.content).gameObject.AddComponent<RawImage>();
        popup.critical.raycastTarget = false;

        var font = CombatUIAssets.ResolveFont(settings.damageFont);
        popup.shade = Label("Damage Shadow", popup.content, font);   // behind the number, finishing blows only
        popup.label = Label("Damage", popup.content, font);
        var outline = CombatUIAssets.Outlined(popup.label.font, DarkOutline, .24f);
        if (outline != null) { popup.label.fontSharedMaterial = outline; popup.shade.fontSharedMaterial = outline; }
        popup.shade.gameObject.SetActive(false);

        popup.kill = Rect("Kill Icon", popup.content).gameObject.AddComponent<RawImage>();
        popup.kill.raycastTarget = false;
        popup.root.gameObject.SetActive(false);
        return popup;
    }

    private static readonly Color DarkOutline = new Color(.16f, .04f, 0f, .95f);

    private static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    private static void Place(RectTransform rect, float x, float width, float height)
    {
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity; // pooled icons may keep a previous punch twist
    }
}
