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
/// D-3, the third mockup round (the user, 2026-11-11: the honeycomb read as a mobile game; "B, C의 이펙트는 좋은데 나는
/// 쉴드가 있을 때 피격 효과를 말하는 거"): a hit the shield takes now cuts the screen on its side instead, as round 3's
/// C tore the HUD, two dark bands with a cyan blade along each sliding in from that edge and gone in about a tenth of a
/// second, while slanted cyan chips break off that edge and fall, as B's band shed what it lost, with how much beside
/// them (-N). Running out drops the screen out for two frames as C did, a wide cyan slash crosses it, chips burst off
/// every edge and the colour leaves it for a moment. The honeycomb and the pane stay, off by the table (0).
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
    [Header("Energy cut (D-3)")]
    [SerializeField, Range(0f, 1f), Tooltip("Brightness of the cut a hit makes on its side of the screen. 0 turns it off.")]
    private float cutStrength = 1f;
    [SerializeField, Min(.03f), Tooltip("How long the cut stays (real seconds).")]
    private float cutSeconds = .12f;
    [SerializeField, Range(.1f, 1f), Tooltip("How far in from its edge the cut runs (share of the screen).")]
    private float cutReach = .45f;
    [SerializeField, Min(0), Tooltip("Chips that break off the hit's edge.")]
    private int chipsPerHit = 4;
    [SerializeField, Min(0), Tooltip("Chips that burst off every edge when the shield runs out.")]
    private int chipsOnBreak = 16;
    [SerializeField, Min(.1f), Tooltip("How long a chip falls before it is gone (real seconds).")]
    private float chipSeconds = .6f;
    [SerializeField, Tooltip("How much the shield took, beside the chips (-N).")]
    private bool showLoss = true;
    [SerializeField, Range(0f, 1f), Tooltip("How dark the screen drops out for two frames when the shield runs out. 0 turns it off.")]
    private float dropout = .8f;

    private readonly Hit[] hits = new Hit[MaxHits];
    private int hitCount;
    private PlayerDamageReceiver receiver;
    private GameObject overlay;
    private ShieldHexGraphic honeycomb;
    private RawImage pane;
    private Texture2D paneTexture;
    private SoundBank bank;
    private EnergyCut cut;
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
        cut?.Clear();
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
        cut = new EnergyCut((RectTransform)overlay.transform, Mathf.Max(chipsPerHit * 3, chipsOnBreak));
    }

    /// <summary>
    /// The shield took (part of) a hit: direction is its screen bearing (PlayerDamageFeedback.Bearing: front up, right
    /// right), strength 0-1. A hit that also runs the shield out leaves its sound to the break.
    /// </summary>
    public void ShowHit(Vector2 direction, bool directional, float strength)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        if (receiver == null || receiver.Shield > 0f) PlaySound(HitKey, FeelSound.ShieldHit, hitVolume);
        if (cut != null && cutStrength > 0f)
        {
            float lost = receiver != null ? receiver.LastShieldTaken : 0f;
            cut.Hit(directional ? direction : Random.insideUnitCircle.normalized, Mathf.Clamp01(strength) * cutStrength, color,
                cutSeconds, cutReach, chipsPerHit, chipSeconds, showLoss && lost >= .5f ? Mathf.RoundToInt(lost) : 0);
        }
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
        if (cut != null) cut.Break(color, chipsOnBreak, chipSeconds, dropout);
        // The colour leaves the screen for a moment.
        KillConfirmEffect.Parry(new ParryScreenLook
        {
            Flash = .15f, Chromatic = .7f, Lens = -.12f, Saturation = -85f, Contrast = 14f, Bloom = .8f, Vignette = .3f,
            Hold = .06f, Fade = .35f, Filter = new Color(.9f, .97f, 1f), VignetteColor = new Color(.02f, .1f, .14f)
        });
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
        cut?.Tick(dt);
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

    /// <summary>
    /// The cut, the chips, the -N and the break's slash and dropout, all Images built once in the overlay and reused.
    /// </summary>
    private sealed class EnergyCut
    {
        private struct Chip { public RectTransform Rect; public Image Image; public Vector2 Velocity; public float Spin, Age, Life; }
        private struct Band { public RectTransform Rect; public Image Core, Blade; public float Age, Life, Shift; public Vector2 From, Along; public float Length, Strength; }

        private const float Gravity = 900f;
        private readonly RectTransform root;
        private readonly Chip[] chips;
        private readonly Band[] bands = new Band[4];
        private readonly TMPro.TextMeshProUGUI loss;
        private readonly RectTransform slash;
        private readonly Image slashImage, dark;
        private int nextChip, nextBand;
        private float lossAge = 99f, slashAge = 99f, darkAge = 99f, darkStrength;
        private Vector2 lossAt;

        public EnergyCut(RectTransform root, int most)
        {
            this.root = root;
            chips = new Chip[Mathf.Max(1, most)];
            for (int i = 0; i < chips.Length; i++)
            {
                var rect = FeelUI.Rect("Shield Chip", root);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                KitSkew.On(image, 4f);
                rect.gameObject.SetActive(false);
                chips[i] = new Chip { Rect = rect, Image = image, Life = 1f, Age = 1f };
            }
            for (int i = 0; i < bands.Length; i++)
            {
                var rect = FeelUI.Rect("Shield Cut", root);
                rect.pivot = new Vector2(0f, .5f);
                var core = rect.gameObject.AddComponent<Image>();
                core.raycastTarget = false;
                var bladeRect = FeelUI.Rect("Blade", rect);
                bladeRect.anchorMin = new Vector2(0f, 1f);
                bladeRect.anchorMax = new Vector2(1f, 1f);
                bladeRect.pivot = new Vector2(.5f, .5f);
                bladeRect.sizeDelta = new Vector2(0f, 2f);
                var blade = bladeRect.gameObject.AddComponent<Image>();
                blade.raycastTarget = false;
                rect.gameObject.SetActive(false);
                bands[i] = new Band { Rect = rect, Core = core, Blade = blade, Age = 1f, Life = 1f };
            }
            var lossRect = FeelUI.Rect("Shield Loss", root);
            lossRect.sizeDelta = new Vector2(120f, 30f);
            loss = KitStyle.Flat(lossRect, "", 22f, TMPro.TextAlignmentOptions.Center, KitStyle.Cyan);
            lossRect.gameObject.SetActive(false);
            slash = FeelUI.Rect("Shield Slash", root);
            slash.sizeDelta = new Vector2(2600f, 30f);
            slash.localRotation = Quaternion.Euler(0f, 0f, -14f);
            slashImage = slash.gameObject.AddComponent<Image>();
            slashImage.raycastTarget = false;
            KitSkew.On(slashImage, 18f);
            slash.gameObject.SetActive(false);
            var veil = FeelUI.Rect("Shield Dropout", root);
            veil.anchorMin = Vector2.zero;
            veil.anchorMax = Vector2.one;
            veil.sizeDelta = Vector2.zero;
            dark = veil.gameObject.AddComponent<Image>();
            dark.raycastTarget = false;
            dark.color = new Color(.01f, .02f, .03f, 0f);
            dark.enabled = false;
        }

        public void Clear()
        {
            // Leaving play, the overlay can go before this is switched off: nothing is left to clear (2026-11-12, the
            // user's console: "The object of type 'RectTransform' has been destroyed" from OnDisable).
            if (root == null) return;
            for (int i = 0; i < chips.Length; i++) { chips[i].Age = chips[i].Life; chips[i].Rect.gameObject.SetActive(false); }
            for (int i = 0; i < bands.Length; i++) { bands[i].Age = bands[i].Life; bands[i].Rect.gameObject.SetActive(false); }
            lossAge = slashAge = darkAge = 99f;
            loss.transform.parent.gameObject.SetActive(false);
            slash.gameObject.SetActive(false);
            dark.enabled = false;
        }

        // Where a screen bearing meets the screen's edge, in the overlay's centred space.
        private Vector2 Edge(Vector2 direction)
        {
            Vector2 half = root.rect.size * .5f;
            float scale = Mathf.Min(Mathf.Abs(direction.x) > .001f ? half.x / Mathf.Abs(direction.x) : float.MaxValue,
                Mathf.Abs(direction.y) > .001f ? half.y / Mathf.Abs(direction.y) : float.MaxValue);
            return direction * scale;
        }

        public void Hit(Vector2 direction, float strength, Color tint, float seconds, float reach, int count, float chipLife, int lost)
        {
            if (direction.sqrMagnitude < .0001f) direction = Vector2.right;
            direction.Normalize();
            Vector2 edge = Edge(direction), inward = -direction, across = new Vector2(-direction.y, direction.x);
            float length = root.rect.size.magnitude * .5f * reach;
            // Two bands, a thick and a thin, a little apart, each sliding in from the edge.
            for (int i = 0; i < 2; i++)
            {
                var band = bands[nextBand];
                nextBand = (nextBand + 1) % bands.Length;
                band.From = edge + across * ((i == 0 ? -1f : 1f) * (18f + Random.Range(0f, 26f)));
                band.Along = Rotate(inward, Random.Range(-7f, 7f));
                band.Length = length * (i == 0 ? 1f : .7f);
                band.Shift = (i == 0 ? 1f : -1f) * 14f;
                band.Age = 0f;
                band.Life = seconds;
                band.Strength = strength;
                band.Rect.sizeDelta = new Vector2(band.Length, i == 0 ? 9f : 6f);
                band.Core.color = new Color(.02f, .03f, .05f, .55f * strength);
                band.Blade.color = Color.Lerp(Color.white, tint, .35f);
                band.Rect.gameObject.SetActive(true);
                bands[(nextBand + bands.Length - 1) % bands.Length] = band;
            }
            for (int i = 0; i < count; i++)
                Throw(edge + across * Random.Range(-90f, 90f), inward * Random.Range(180f, 420f) + across * Random.Range(-160f, 160f), tint, chipLife, Random.Range(10f, 24f));
            if (lost <= 0) return;
            loss.text = "-" + lost;
            lossAt = edge + inward * 90f;
            lossAge = 0f;
            loss.transform.parent.gameObject.SetActive(true);
        }

        public void Break(Color tint, int count, float chipLife, float darkness)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                Vector2 edge = Edge(direction);
                Throw(edge, -direction * Random.Range(260f, 620f) + Vector2.up * Random.Range(0f, 240f), tint, chipLife * 1.3f, Random.Range(14f, 34f));
            }
            slashAge = 0f;
            slashImage.color = Color.white;
            slash.gameObject.SetActive(true);
            darkStrength = darkness;
            darkAge = darkness > 0f ? 0f : 99f;
        }

        private void Throw(Vector2 at, Vector2 velocity, Color tint, float life, float size)
        {
            var chip = chips[nextChip];
            nextChip = (nextChip + 1) % chips.Length;
            chip.Rect.anchoredPosition = at;
            chip.Rect.sizeDelta = new Vector2(size * 1.6f, size * .5f);
            chip.Rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            chip.Velocity = velocity;
            chip.Spin = Random.Range(-540f, 540f);
            chip.Age = 0f;
            chip.Life = life;
            chip.Image.color = Random.value < .3f ? Color.white : tint;
            chip.Rect.gameObject.SetActive(true);
            chips[(nextChip + chips.Length - 1) % chips.Length] = chip;
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < bands.Length; i++)
            {
                var band = bands[i];
                if (band.Age >= band.Life) continue;
                band.Age += dt;
                float t = Mathf.Clamp01(band.Age / band.Life);
                if (t >= 1f) { band.Rect.gameObject.SetActive(false); bands[i] = band; continue; }
                // In fast from the edge, offset to the side, then gone.
                float grow = 1f - (1f - Mathf.Clamp01(t * 3f)) * (1f - Mathf.Clamp01(t * 3f));
                Vector2 side = new Vector2(-band.Along.y, band.Along.x) * band.Shift * (1f - t);
                band.Rect.anchoredPosition = band.From + side;
                band.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(band.Along.y, band.Along.x) * Mathf.Rad2Deg);
                band.Rect.localScale = new Vector3(grow, 1f, 1f);
                var core = band.Core.color; core.a = .55f * band.Strength * (1f - t);
                band.Core.color = core;
                var blade = band.Blade.color; blade.a = band.Strength * (1f - t * t);
                band.Blade.color = blade;
                bands[i] = band;
            }
            for (int i = 0; i < chips.Length; i++)
            {
                var chip = chips[i];
                if (chip.Age >= chip.Life) continue;
                chip.Age += dt;
                if (chip.Age >= chip.Life) { chip.Rect.gameObject.SetActive(false); chips[i] = chip; continue; }
                chip.Velocity += Vector2.down * Gravity * dt;
                chip.Rect.anchoredPosition += chip.Velocity * dt;
                chip.Rect.localRotation *= Quaternion.Euler(0f, 0f, chip.Spin * dt);
                var color = chip.Image.color; color.a = 1f - Mathf.Clamp01((chip.Age / chip.Life - .5f) * 2f);
                chip.Image.color = color;
                chips[i] = chip;
            }
            if (lossAge < .7f)
            {
                lossAge += dt;
                var box = (RectTransform)loss.transform.parent;
                box.anchoredPosition = lossAt + Vector2.up * (lossAge * 40f);
                var color = loss.color; color.a = 1f - Mathf.Clamp01((lossAge - .35f) / .35f);
                loss.color = color;
                if (lossAge >= .7f) box.gameObject.SetActive(false);
            }
            if (slashAge < .32f)
            {
                slashAge += dt;
                float t = Mathf.Clamp01(slashAge / .32f);
                slash.localScale = new Vector3(Mathf.Clamp01(t * 6f), 1f - t * .7f, 1f);
                slashImage.color = new Color(Mathf.Lerp(1f, .6f, t), Mathf.Lerp(1f, .95f, t), 1f, 1f - t);
                if (slashAge >= .32f) slash.gameObject.SetActive(false);
            }
            // Two frames out, a beat apart, as round 3's C dropped the HUD out.
            if (darkAge < .2f)
            {
                darkAge += dt;
                bool out1 = darkAge < .035f, out2 = darkAge > .1f && darkAge < .135f;
                dark.enabled = out1 || out2;
                var color = dark.color; color.a = darkStrength;
                dark.color = color;
                if (darkAge >= .2f) dark.enabled = false;
            }
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad, c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(value.x * c - value.y * s, value.x * s + value.y * c);
        }
    }

    private static float Hash(int index)
    {
        float value = Mathf.Sin(index * 12.9898f + 3.17f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }
}
