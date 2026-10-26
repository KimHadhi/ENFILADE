using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation of accepted incoming damage. Each hit retains a world POSITION, never a source Transform or
/// cached screen angle. LateUpdate recomputes bearings after the camera has moved, looked and received its punch.
/// Blood is screen-locked: each splash keeps the bearing it arrived from, creeps in, holds, then recedes slowly.
/// Low health adds a persistent blood baseline that surges with every heartbeat instead of blinking on and off.
/// Pools are bounded; only a saturated pool replaces its oldest entry. No per-hit objects, materials or textures.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerDamageReceiver)), DefaultExecutionOrder(10020)]
public sealed class PlayerDamageFeedback : MonoBehaviour
{
    internal struct Hit
    {
        internal Vector3 Origin;
        internal Vector2 Direction;
        internal bool Directional;
        internal float Age, Lifetime, Strength, Alpha;
        internal ulong Serial;
    }

    internal struct Splash
    {
        internal Vector2 Direction;
        internal bool Directional;
        internal float Strength, Rear, Age, Hold, Fade, Amount;
        internal ulong Serial;
    }

    /// <summary>A rivulet that leaves the blood front and runs down the screen. Start is in -1..1 screen space.</summary>
    internal struct Drip
    {
        internal Vector2 Start;
        internal float Width, Length, Delay, Age, Lifetime, Phase;
        internal ulong Serial;
    }

    [SerializeField, Tooltip("Optional. Defaults to the player's child camera.")]
    private Camera view;
    private PlayerDamageReceiver receiver;
    private CameraViewPunch punch;
    [SerializeField, HideInInspector] private GameObject overlay;
    private PlayerDamageGraphic graphic;
    private float heartbeatTime, steadyBlood, criticalWeight;
    private ulong serial;
    internal readonly Hit[] Hits = new Hit[24];
    internal readonly Splash[] Splashes = new Splash[32];
    internal readonly Drip[] Drips = new Drip[24];
    public int ActiveHitCount { get; private set; }
    public int ActiveSplashCount { get; private set; }
    public int ActiveDripCount { get; private set; }
    public bool IsCritical { get; private set; }
    /// <summary>Heartbeat envelope, 0 between beats and 1 at the peak of the systole.</summary>
    public float PulseWeight { get; private set; }
    /// <summary>Blood amount present on every edge from low health, including the current heartbeat surge.</summary>
    public float Baseline { get; private set; }
    public float Freshness { get; private set; }
    public float RearWash { get; private set; }
    /// <summary>Low-health red vignette strength, already shaped by the heartbeat.</summary>
    public float Vignette { get; private set; }
    /// <summary>Unscaled seconds that stop during pause; drives the shader's slow crawl.</summary>
    public float Clock { get; private set; }
    /// <summary>Offsets the shader noise and flecks; re-rolled when blood lands on a clean screen.</summary>
    public Vector4 PatternSeed { get; private set; }

    private void OnEnable()
    {
        receiver = GetComponent<PlayerDamageReceiver>();
        receiver.HitResolved += OnHit;
        receiver.Died += Clear;
        if (overlay != null)
        {
            graphic = overlay.GetComponentInChildren<PlayerDamageGraphic>(true);
            if (graphic != null) graphic.Owner = this;
        }
    }

    private void OnDisable()
    {
        if (receiver != null) { receiver.HitResolved -= OnHit; receiver.Died -= Clear; }
        Clear();
        if (overlay != null) { overlay.SetActive(false); Destroy(overlay); }
        overlay = null; graphic = null;
    }

    public void Clear()
    {
        ActiveHitCount = ActiveSplashCount = ActiveDripCount = 0;
        serial = 0;
        IsCritical = false;
        PulseWeight = heartbeatTime = steadyBlood = criticalWeight = 0f;
        Baseline = Freshness = RearWash = Vignette = 0f;
        if (punch != null) punch.ClearDamage();
        if (graphic != null) graphic.SetVerticesDirty();
    }

    private Camera ResolveCamera()
    {
        if (view == null) view = GetComponentInChildren<Camera>();
        return view != null && view.isActiveAndEnabled ? view : null;
    }

    private void EnsureOverlay()
    {
        if (overlay != null) return;
        overlay = new GameObject("Player Damage Feedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        // Keep the overlay in its owner's scene, including additive scene loads.
        RuntimeSceneHierarchy.Group(overlay, "EFFECTS", "Screen", gameObject);
        FeelUI.SetupOverlay(overlay, 24);
        var rect = FeelUI.Rect("Blood and Direction Arcs", overlay.transform);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        graphic = rect.gameObject.AddComponent<PlayerDamageGraphic>();
        graphic.raycastTarget = false;
        graphic.Owner = this;
    }

    private void OnHit(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || !Application.isPlaying || receiver == null || !receiver.IsAlive) return;
        var camera = ResolveCamera();
        if (camera == null) return;
        var settings = PlayerDamageFeedbackSettings.Instance;
        EnsureOverlay();
        bool directional = TryOrigin(hit, out Vector3 origin);
        Vector2 direction = directional ? Bearing(camera.transform, origin) : Vector2.zero;
        // Shield-only hits still give feedback; HP is only read for the low-health layers.
        float strength = Mathf.Lerp(.55f, 1f, Mathf.Clamp01(hit.Amount / receiver.MaximumHealth * 4f));
        int index = ActiveHitCount < Hits.Length ? ActiveHitCount++ : OldestHit();
        Hits[index] = new Hit { Origin = origin, Direction = direction, Directional = directional,
            Lifetime = Mathf.Max(.1f, settings.hitSeconds), Strength = strength, Alpha = strength, Serial = ++serial };
        if (settings.bloodVignette)
        {
            if (ActiveSplashCount == 0) PatternSeed = new Vector4(Random.Range(0f, 500f), Random.Range(0f, 500f));
            SpawnDrips(direction, directional, strength, settings);
            int slot = ActiveSplashCount < Splashes.Length ? ActiveSplashCount++ : OldestSplash();
            Splashes[slot] = new Splash { Direction = direction, Directional = directional, Strength = strength * settings.bloodPerHit,
                Rear = directional ? Mathf.Max(0f, -direction.y) : 0f, Hold = Mathf.Max(0f, settings.bloodHoldSeconds),
                Fade = Mathf.Max(.1f, settings.bloodFadeSeconds), Serial = ++serial };
            Freshness = 1f;
        }
        if (settings.cameraShake)
        {
            punch = CameraViewPunch.For(camera);
            float side = directional ? direction.x : Random.Range(-.5f, .5f);
            punch.DamagePunch(new Vector3(.45f, side * .35f, -side) * (settings.impactKickDegrees * strength),
                settings.traumaPerHit * strength);
        }
        graphic.SetVerticesDirty();
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    private bool TryOrigin(DamageHit hit, out Vector3 origin)
    {
        origin = default;
        if (hit.Kind == DamageKind.Fall) return false;
        if (hit.Origin.HasValue && Finite(hit.Origin.Value)) { origin = hit.Origin.Value; return true; }
        if (hit.Source != null && Finite(hit.Source.transform.position))
        { origin = hit.Source.transform.position; return true; }
        if (!Finite(hit.Direction) || hit.Direction.sqrMagnitude < .000001f) return false;
        // Legacy hits without a launch position: preserve their incoming ray direction at a finite distance.
        origin = (Finite(hit.Point) ? hit.Point : transform.position) - hit.Direction.normalized * 30f;
        return true;
    }

    /// <summary>HUD compass: front=up, right=right, rear=down. Camera pitch and roll participate in the 3D transform.</summary>
    public static Vector2 Bearing(Transform camera, Vector3 worldOrigin)
    {
        Vector3 local = camera.InverseTransformDirection(worldOrigin - camera.position);
        Vector2 bearing = new Vector2(local.x, local.z);
        // An origin exactly above/below the view has no azimuth; choose the corresponding vertical pole.
        return bearing.sqrMagnitude > .000001f ? bearing.normalized : local.y < 0f ? Vector2.down : Vector2.up;
    }

    public bool TryGetIndicator(int index, out Vector3 worldOrigin, out Vector2 direction, out float opacity)
    {
        worldOrigin = default; direction = default; opacity = 0f;
        if (index < 0 || index >= ActiveHitCount || !Hits[index].Directional) return false;
        worldOrigin = Hits[index].Origin; direction = Hits[index].Direction; opacity = Hits[index].Alpha;
        return true;
    }

    /// <summary>Blood amount (0..1) toward a screen point in -1..1 space. The shader turns it into the liquid front.</summary>
    public float BloodAt(Vector2 point)
    {
        float magnitude = point.magnitude;
        Vector2 toward = magnitude > .0001f ? point / magnitude : Vector2.zero;
        float clear = 1f - Mathf.Clamp01(Baseline);
        for (int i = 0; i < ActiveSplashCount; i++)
        {
            var splash = Splashes[i];
            float weight = splash.Directional
                ? Mathf.Pow(Mathf.Clamp01(Vector2.Dot(toward, splash.Direction) * .5f + .5f), 3.5f) : 1f;
            clear *= 1f - Mathf.Clamp01(splash.Amount * weight);
        }
        return 1f - clear;
    }

    /// <summary>Rounded-rectangle radius used by the shader: 1 on the screen edge midpoints, larger in the corners.</summary>
    internal static float Rounded(Vector2 point)
        => Mathf.Pow(Mathf.Pow(Mathf.Abs(point.x), 3f) + Mathf.Pow(Mathf.Abs(point.y), 3f), 1f / 3f);

    /// <summary>Where a rivulet starts for a blood amount: just inside the edge, deeper for heavier blood.</summary>
    internal static float DripRadius(float amount) => Mathf.Lerp(1f, .9f, Mathf.Sqrt(Mathf.Clamp01(amount)));

    private void SpawnDrips(Vector2 direction, bool directional, float strength, PlayerDamageFeedbackSettings settings)
    {
        int count = Mathf.Clamp(settings.dripsPerHit, 0, 4);
        // Blood from below has nowhere to run.
        if (directional && direction.y < -.5f) count = 0;
        float splash = strength * settings.bloodPerHit;
        float heading = directional ? Mathf.Atan2(direction.y, direction.x) : Mathf.PI * .5f;
        for (int i = 0; i < count; i++)
        {
            float angle = heading + Random.Range(-.65f, .65f);
            Vector2 toward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // Start inside the reddened edge, a little deeper when blood has already gathered there.
            float amount = 1f - (1f - BloodAt(toward)) * (1f - splash);
            float radius = DripRadius(amount);
            Vector2 start = toward * (radius / Rounded(toward));
            start = new Vector2(Mathf.Clamp(start.x, -.97f, .97f), Mathf.Clamp(start.y, -.97f, .97f));
            if (start.y < -.55f) continue;
            float room = (start.y + 1f) * 540f * .85f;
            int index = ActiveDripCount < Drips.Length ? ActiveDripCount++ : OldestDrip();
            Drips[index] = new Drip { Start = start, Width = Random.Range(3f, 5f) * Mathf.Sqrt(strength),
                Length = Mathf.Min(room, settings.dripMaxLength * Random.Range(.45f, 1f) * strength),
                Delay = Random.Range(.12f, .45f), Lifetime = settings.bloodHoldSeconds + settings.bloodFadeSeconds,
                Phase = Random.Range(0f, Mathf.PI * 2f), Serial = ++serial };
        }
    }

    private int OldestDrip()
    {
        int oldest = 0;
        for (int i = 1; i < Drips.Length; i++) if (Drips[i].Serial < Drips[oldest].Serial) oldest = i;
        return oldest;
    }

    private int OldestHit()
    {
        int oldest = 0;
        for (int i = 1; i < Hits.Length; i++) if (Hits[i].Serial < Hits[oldest].Serial) oldest = i;
        return oldest;
    }

    private int OldestSplash()
    {
        int oldest = 0;
        for (int i = 1; i < Splashes.Length; i++) if (Splashes[i].Serial < Splashes[oldest].Serial) oldest = i;
        return oldest;
    }

    /// <summary>Creeps in over 160 ms with a brief surge, holds, then recedes back to the edges.</summary>
    private static float SplashAmount(in Splash splash)
    {
        float spread = 1f - Mathf.Pow(1f - Mathf.Clamp01(splash.Age / .16f), 3f);
        float surge = 1f + .3f * Mathf.Exp(-splash.Age * 9f);
        float recede = splash.Age <= splash.Hold ? 1f
            : 1f - Mathf.SmoothStep(0f, 1f, (splash.Age - splash.Hold) / splash.Fade);
        return splash.Strength * spread * surge * recede;
    }

    private void LateUpdate()
    {
        if (receiver == null || !receiver.isActiveAndEnabled || !receiver.IsAlive)
        { if (ActiveHitCount > 0 || ActiveSplashCount > 0 || ActiveDripCount > 0 || IsCritical || Baseline > 0f) Clear(); return; }
        var camera = ResolveCamera();
        if (camera == null) { Clear(); return; }
        var settings = PlayerDamageFeedbackSettings.Instance;
        // Freeze visual lifetimes during pause; run at real speed during combat hit-stop/slow motion.
        float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
        Clock += dt;
        for (int i = ActiveHitCount - 1; i >= 0; i--)
        {
            var hit = Hits[i]; hit.Age += dt;
            if (hit.Age >= hit.Lifetime) { RemoveHit(i); continue; }
            if (hit.Directional) hit.Direction = Bearing(camera.transform, hit.Origin);
            float fade = Mathf.InverseLerp(Mathf.Clamp(settings.holdFraction, 0f, .95f), 1f, hit.Age / hit.Lifetime);
            hit.Alpha = hit.Strength * (1f - Mathf.SmoothStep(0f, 1f, fade));
            Hits[i] = hit;
        }
        float rear = 0f;
        for (int i = ActiveSplashCount - 1; i >= 0; i--)
        {
            var splash = Splashes[i]; splash.Age += dt;
            if (!settings.bloodVignette || splash.Age >= splash.Hold + splash.Fade) { RemoveSplash(i); continue; }
            splash.Amount = SplashAmount(splash);
            rear = Mathf.Max(rear, splash.Rear * splash.Amount);
            Splashes[i] = splash;
        }
        for (int i = ActiveDripCount - 1; i >= 0; i--)
        {
            var drip = Drips[i]; drip.Age += dt;
            if (!settings.bloodVignette || drip.Age >= drip.Lifetime) { RemoveDrip(i); continue; }
            Drips[i] = drip;
        }
        RearWash = Mathf.Clamp01(rear * settings.rearWashOpacity);
        Freshness = ActiveSplashCount > 0 ? Freshness * Mathf.Exp(-dt / Mathf.Max(.05f, settings.freshSeconds)) : 0f;

        float health = receiver.Health / receiver.MaximumHealth;
        IsCritical = settings.criticalPulse && health <= settings.criticalHealthFraction;
        if (IsCritical)
        {
            heartbeatTime += dt;
            float phase = Mathf.Repeat(heartbeatTime * Mathf.Max(1f, settings.heartbeatBpm) / 60f, 1f);
            // A strong systole followed by a weaker diastole: "lub-dub", then a quiet interval. Each beat slams in and eases out.
            PulseWeight = Mathf.Clamp01(Beat(phase, .1f, .025f, .09f) + .62f * Beat(phase, .32f, .03f, .11f));
        }
        else { PulseWeight = heartbeatTime = 0f; }
        // The steady stain follows health smoothly (fast in, slow out); the heartbeat surges on top of it.
        float injured = settings.criticalPulse
            ? Mathf.InverseLerp(Mathf.Max(settings.injuredHealthFraction, settings.criticalHealthFraction + .01f), 0f, health) : 0f;
        float steadyTarget = Mathf.Max(injured * settings.injuredBlood, IsCritical ? settings.criticalBlood : 0f);
        steadyBlood = Mathf.MoveTowards(steadyBlood, steadyTarget, dt * (steadyTarget > steadyBlood ? 3f : .45f));
        criticalWeight = Mathf.MoveTowards(criticalWeight, IsCritical ? 1f : 0f, dt * (IsCritical ? 4f : .8f));
        Baseline = Mathf.Clamp01(steadyBlood + PulseWeight * settings.heartbeatSurge);
        Vignette = settings.criticalVignette * criticalWeight;
        if (Baseline > 0f || Vignette > 0f) EnsureOverlay();

        if (!settings.cameraShake && punch != null) punch.ClearDamage();
        if (graphic != null)
        {
            bool showing = ActiveHitCount > 0 || ActiveSplashCount > 0 || ActiveDripCount > 0 || Baseline > .001f || Vignette > .001f;
            graphic.enabled = showing;
            if (showing) { graphic.SetVerticesDirty(); graphic.ApplyShaderState(); }
        }
    }

    private static float Beat(float phase, float centre, float attack, float release)
    { float distance = (phase - centre) / (phase < centre ? attack : release); return Mathf.Exp(-distance * distance * .5f); }
    private void RemoveHit(int index)
    { for (int i = index; i < ActiveHitCount - 1; i++) Hits[i] = Hits[i + 1]; ActiveHitCount--; }
    private void RemoveSplash(int index)
    { for (int i = index; i < ActiveSplashCount - 1; i++) Splashes[i] = Splashes[i + 1]; ActiveSplashCount--; }
    private void RemoveDrip(int index)
    { for (int i = index; i < ActiveDripCount - 1; i++) Drips[i] = Drips[i + 1]; ActiveDripCount--; }
}
