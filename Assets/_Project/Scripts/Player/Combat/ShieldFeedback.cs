using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The player's own shield on this PC's screen, after Apex Legends' shields: energy, never blood.
/// A hit the shield takes lights a honeycomb of the shield's cyan along the screen's edge on the side it came from,
/// white-hot for a moment and gone in about a third of a second, with an electric crack instead of the flesh sound
/// (PlayerDamageFeedback leaves out the blood and colours its arc to match; the HUD's shield band flashes white).
/// Running the shield out (backlog 321) gets a moment of its own: a cracked pane of the same cyan bursts outward off the
/// screen's edge, the view takes a small extra knock, a glassy break plays and it is said at the top of the screen
/// (AlertHUD); the HUD's band shatters (ShieldShatter).
/// Sounds: clips in Art/Audio/HitConfirm/ShieldHit and ShieldBreak when there are any, else FeelAudio's synthesized ones.
/// This PC's player only, as the shield is decided there; others see sparks on the body (PlayerNetworkCombat). Both
/// overlays are built at spawn, so the first hit costs nothing in a fight. Numbers: Tables/Player.csv, row Shield.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerDamageReceiver))]
public sealed class ShieldFeedback : MonoBehaviour
{
    /// <summary>A shield's colour as others see it on a body, where no table row is read.</summary>
    public static readonly Color WorldColor = new Color(.6f, .93f, 1f);
    private const string BankId = "HitConfirm", HitKey = "ShieldHit", BreakKey = "ShieldBreak";
    private const int MaxHits = 8;
    // The pane's shards round the edge, and how far it grows as it bursts off the screen.
    private const int Facets = 28;
    private const float Burst = .14f, WhiteSeconds = .05f;

    private struct Hit
    {
        public Vector2 Direction;
        public bool Directional;
        public float Strength, Age;
    }

    [SerializeField, Tooltip("The shield's colour: the honeycomb, the arc, the pane. The HUD's shield band is the same cyan.")]
    private Color color = new Color(.65f, .95f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("Brightness of the honeycomb a full-strength hit lights. 0 turns it off.")]
    private float hitStrength = .85f;
    [SerializeField, Min(.05f), Tooltip("How long a hit's honeycomb takes to fade (real seconds).")]
    private float hitSeconds = .32f;
    [SerializeField, Min(.5f), Tooltip("How tightly the light keeps to the side the hit came from. Higher is narrower.")]
    private float hitFocus = 3f;
    [SerializeField, Range(.3f, .95f), Tooltip("How far in from the edge a full-strength hit's light reaches (1 is the edge; lower reaches further in).")]
    private float hitReach = .55f;
    [SerializeField, Min(0f), Tooltip("Volume of the synthesized shield hit. Clips use Sounds.csv's HitConfirm/ShieldHit row.")]
    private float hitVolume = .7f;
    [SerializeField, Range(0f, 1f), Tooltip("Brightness of the cracked pane when the shield runs out. 0 turns it off.")]
    private float paneStrength = .55f;
    [SerializeField, Min(.05f), Tooltip("How long the pane takes to burst outward and fade (real seconds).")]
    private float paneSeconds = .5f;
    [SerializeField, Min(0f), Tooltip("Extra knock to the view when the shield runs out (degrees). 0 turns it off.")]
    private float viewKick = 1.6f;
    [SerializeField, Range(0f, 1f), Tooltip("Extra shake when the shield runs out.")]
    private float trauma = .2f;
    [SerializeField, Min(0f), Tooltip("Volume of the synthesized break. Clips use Sounds.csv's HitConfirm/ShieldBreak row.")]
    private float breakVolume = .8f;

    private readonly Hit[] hits = new Hit[MaxHits];
    private int hitCount;
    private PlayerDamageReceiver receiver;
    private GameObject overlay;
    private ShieldHexGraphic honeycomb;
    private RawImage pane;
    private Texture2D paneTexture;
    private SoundBank bank;
    // 0 to 1 through the pane's life; 1 is idle.
    private float paneLife = 1f;

    public Color Tint => color;
    public int HitCount => hitCount;

    private void Awake()
    {
        receiver = GetComponent<PlayerDamageReceiver>();
        bank = SoundBank.Load(BankId);
        Build();
    }

    private void OnEnable() { if (receiver != null) receiver.ShieldBroken += OnBroken; }

    private void OnDisable()
    {
        if (receiver != null) receiver.ShieldBroken -= OnBroken;
        Clear();
    }

    private void OnDestroy()
    {
        if (overlay != null) Destroy(overlay);
        if (paneTexture != null) Destroy(paneTexture);
    }

    public void Clear()
    {
        hitCount = 0;
        paneLife = 1f;
        if (pane != null) pane.enabled = false;
        if (honeycomb != null) honeycomb.enabled = false;
    }

    private void Build()
    {
        overlay = new GameObject("Shield", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        RuntimeSceneHierarchy.Group(overlay, "EFFECTS", "Screen", gameObject);
        FeelUI.SetupOverlay(overlay, 25);
        var field = FeelUI.Rect("Honeycomb", overlay.transform);
        field.anchorMin = Vector2.zero; field.anchorMax = Vector2.one;
        field.offsetMin = field.offsetMax = Vector2.zero;
        ShieldHexGraphic.Prepare();
        honeycomb = field.gameObject.AddComponent<ShieldHexGraphic>();
        honeycomb.raycastTarget = false;
        honeycomb.Owner = this;
        honeycomb.enabled = false;

        paneTexture = PaneTexture();
        var rect = FeelUI.Rect("Cracked Pane", overlay.transform);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        pane = rect.gameObject.AddComponent<RawImage>();
        pane.texture = paneTexture; pane.raycastTarget = false; pane.enabled = false;
    }

    /// <summary>
    /// The shield took (part of) a hit: direction is its screen bearing (PlayerDamageFeedback.Bearing: front up, right
    /// right), strength 0-1. A hit that also runs the shield out leaves its sound to the break.
    /// </summary>
    public void ShowHit(Vector2 direction, bool directional, float strength)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        if (receiver == null || receiver.Shield > 0f) PlaySound(HitKey, FeelSound.ShieldHit, hitVolume);
        if (hitStrength <= 0f || honeycomb == null) return;
        int slot = hitCount < MaxHits ? hitCount++ : Oldest();
        hits[slot] = new Hit { Direction = direction, Directional = directional, Strength = Mathf.Clamp01(strength), Age = 0f };
        honeycomb.enabled = true;
        Paint();
    }

    private int Oldest()
    {
        int oldest = 0;
        for (int i = 1; i < hitCount; i++) if (hits[i].Age > hits[oldest].Age) oldest = i;
        return oldest;
    }

    /// <summary>How much honeycomb shows toward a screen point in -1..1 space (ShieldHexGraphic's vertex alpha).</summary>
    internal float LightAt(Vector2 point)
    {
        float radius = PlayerDamageFeedback.Rounded(point);
        float magnitude = point.magnitude;
        Vector2 toward = magnitude > .0001f ? point / magnitude : Vector2.zero;
        float light = 0f;
        for (int i = 0; i < hitCount; i++)
        {
            var hit = hits[i];
            float life = Mathf.Clamp01(hit.Age / hitSeconds);
            // Snaps on, then falls off fast and lingers a hair.
            float fade = Mathf.Clamp01(hit.Age / .02f) * (1f - life) * (1f - life);
            float side = hit.Directional ? Mathf.Pow(Mathf.Clamp01(Vector2.Dot(toward, hit.Direction) * .5f + .5f), hitFocus) : .55f;
            // Harder hits reach further in, and each wave rolls a little further in as it fades.
            float reach = Mathf.Lerp(.86f, hitReach, hit.Strength) - .05f * life;
            light = Mathf.Max(light, hit.Strength * fade * side * Mathf.SmoothStep(reach, 1f, radius));
        }
        return Mathf.Clamp01(light * hitStrength);
    }

    private void OnBroken(DamageHit hit)
    {
        if (!Application.isPlaying || receiver == null) return;
        PlaySound(BreakKey, FeelSound.ShieldBreak, breakVolume);
        // Broken by the blow that killed: the death screen has the moment.
        if (!receiver.IsAlive) return;
        AlertHUD.Self(Texts.Get("shield.broken"));
        if (paneStrength > 0f && pane != null) { paneLife = 0f; ApplyPane(); }
        if (viewKick <= 0f && trauma <= 0f) return;
        var view = GetComponentInChildren<Camera>();
        if (view != null && view.isActiveAndEnabled)
            CameraViewPunch.For(view).DamagePunch(new Vector3(viewKick, 0f, Random.Range(-.4f, .4f) * viewKick), trauma);
    }

    private void PlaySound(string key, FeelSound fallback, float volume)
    {
        var clip = bank != null ? bank.Pick(key) : null;
        if (clip != null)
        {
            var tuning = bank.SettingsOf(key);
            FeelAudio.PlayClip(clip, tuning.Volume, tuning.RandomPitch());
            return;
        }
        FeelAudio.Play(fallback, null, volume, Random.Range(.96f, 1.04f));
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
        if (hitCount > 0)
        {
            for (int i = hitCount - 1; i >= 0; i--)
            {
                hits[i].Age += dt;
                if (hits[i].Age < hitSeconds) continue;
                hits[i] = hits[--hitCount];
            }
            Paint();
        }
        if (paneLife < 1f)
        {
            paneLife = Mathf.Min(1f, paneLife + dt / paneSeconds);
            ApplyPane();
        }
    }

    // White-hot for a moment after the newest hit, then the shield's colour.
    private void Paint()
    {
        honeycomb.enabled = hitCount > 0;
        if (hitCount == 0) return;
        float newest = float.MaxValue;
        for (int i = 0; i < hitCount; i++) newest = Mathf.Min(newest, hits[i].Age);
        Color tint = Color.Lerp(Color.white, color, Mathf.Clamp01(newest / WhiteSeconds));
        tint.a = 1f;
        honeycomb.color = tint;
        honeycomb.SetVerticesDirty();
    }

    // Shows at full strength, white-hot for a moment, then bursts outward off the screen's edge while it fades.
    private void ApplyPane()
    {
        float fade = 1f - paneLife;
        pane.enabled = paneLife < 1f;
        if (!pane.enabled) return;
        float burst = 1f - fade * fade * fade;
        pane.rectTransform.localScale = Vector3.one * (1f + Burst * burst);
        Color tint = Color.Lerp(Color.white, color, Mathf.Clamp01(paneLife * paneSeconds / WhiteSeconds));
        tint.a = paneStrength * fade * fade;
        pane.color = tint;
    }

    /// <summary>
    /// White shards in from the screen's edge on a rounded-rectangle frame (as the blood is): each of the facets around
    /// the edge reaches in its own way to a point, see-through near its tip and thicker at the edge, with a bright crack
    /// along its inner edge and thin seams between shards. Tinted by the RawImage.
    /// </summary>
    private static Texture2D PaneTexture()
    {
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "Shield Break Pane", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
            float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 3f) + Mathf.Pow(Mathf.Abs(v), 3f), 1f / 3f);
            float cell = (Mathf.Atan2(v, u) / (2f * Mathf.PI) + .5f) * Facets;
            int index = Mathf.FloorToInt(cell);
            float within = cell - index;
            index = ((index % Facets) + Facets) % Facets;
            // Each shard's own reach, pointed in its middle.
            float tip = .7f + .16f * Hash(index) + .08f * Mathf.Abs(within - .5f) * 2f;
            float body = Mathf.Clamp01((r - tip) / .04f);
            float rim = Mathf.Clamp01(1f - Mathf.Abs(r - tip) / .012f);
            float seam = body * Mathf.Clamp01(1f - Mathf.Min(within, 1f - within) * 2f * Mathf.PI * r / Facets / .006f);
            float fill = body * Mathf.Lerp(.3f, .75f, Mathf.Clamp01((r - tip) / .35f));
            float alpha = Mathf.Max(fill, Mathf.Max(rim, seam));
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static float Hash(int index)
    {
        float value = Mathf.Sin(index * 12.9898f + 3.17f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }
}
