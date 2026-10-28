using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Head-height name + &lt; HP &gt; bar with a thin posture bar, and a larger body-centred stagger cue.
/// While executable (staggered) the cue and the HP bar beat like a heart, faster as posture recovers;
/// the moment it starts, the bar kicks, flashes white and a light sweeps across it. Every beat the
/// cue throws an expanding echo of itself and the bar radiates a glow ripple.
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
    private const float StripePeriod = 6f;
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
    private Image executablePlate, executableFlash, executableSweep;
    private static readonly Color NameColor = new Color(1f, .97f, .95f);
    private RawImage executableBloom, executableGlow, executableRipple, executableRippleHot;
    private RawImage staggerGlow, staggerEcho, trailEdgeGlow;
    private static Texture2D radialGlow, radialRing;
    private float capX, heartPhase, executableAge = float.PositiveInfinity, beatAge = float.PositiveInfinity;
    private float lastHealth = float.NaN, edgePulseAge = float.PositiveInfinity;
    private bool wasExecutable;
    private const float GlowHeight = 28f, SweepWidth = 22f;
    /// <summary>0..1 heartbeat envelope while executable (1 on the "lub"), 0 otherwise.</summary>
    public float Heartbeat { get; private set; }
    /// <summary>Unscaled seconds since the enemy last became executable.</summary>
    public float ExecutableAge => executableAge;
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
        // Every hit restarts the white edge line's pulse.
        if (health < lastHealth - .0001f) edgePulseAge = 0f; else edgePulseAge += Time.unscaledDeltaTime;
        lastHealth = health;
        // Keeps running while hidden so a bar that reappears shows the correct state.
        trail.Tick(health, maximum, Time.unscaledDeltaTime, settings.enemyShakeMax, impact: true);
        TrailRatio = Mathf.Clamp01(trail.Trail / maximum);
        TickHeartbeat(settings, Time.unscaledDeltaTime);
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
        AnimateHealthEdge(settings, showTrail);
        bars.anchoredPosition = trail.ShakeOffset;
        bars.localScale = Vector3.one * (trail.ScalePunch * ExecutableBarScale(settings));
        AnimateExecutableBar(settings);
        SetFill(postureFill, postureImage, PostureRatio);
        postureImage.color = wasExecutable ? settings.executableAccentColor : posture.IsBroken ? new Color(1f, .28f, .20f)
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
        staggerBadge.rectTransform.localScale = Vector3.one * (Mathf.Lerp(1.35f, 1f, pop) * (1f + settings.heartbeatScale * Heartbeat));
        staggerBadge.color = new Color(1f, 1f, 1f, Mathf.Lerp(.82f, 1f, Heartbeat));
        AnimateStaggerBeat(settings);
    }

    private void AnimateStaggerBeat(CombatFeedbackSettings settings)
    {
        var badge = staggerBadge.rectTransform;
        bool beating = wasExecutable && staggerPanel.gameObject.activeSelf;
        Color tint = settings.executableMarkGlowColor;
        staggerGlow.enabled = beating;
        if (beating)
        {
            staggerGlow.rectTransform.sizeDelta = badge.sizeDelta * 1.6f;
            staggerGlow.rectTransform.localScale = badge.localScale;
            staggerGlow.color = new Color(tint.r, tint.g, tint.b, tint.a * Mathf.Lerp(.25f, .9f, Heartbeat));
        }
        // An echo of the mark pops out of it on every "lub" and fades as it grows.
        float t = Mathf.Clamp01(beatAge / .55f);
        staggerEcho.enabled = beating && t < 1f;
        if (!staggerEcho.enabled) return;
        float grow = 1f - (1f - t) * (1f - t);
        staggerEcho.rectTransform.sizeDelta = badge.sizeDelta;
        staggerEcho.rectTransform.localScale = badge.localScale * Mathf.Lerp(1f, 1.75f, grow);
        staggerEcho.color = new Color(1f, 1f, 1f, .75f * (1f - t) * (1f - t));
    }
    /// <summary>Soft ring, so a stretched copy reads as an elliptical shock wave rather than a box.</summary>
    private static Texture2D RadialRing
    {
        get
        {
            if (radialRing != null) return radialRing;
            const int size = 128;
            radialRing = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "Radial Ring (Runtime)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy), d = (r - .72f) / (r < .72f ? .3f : .14f);
                // Bright rim with a faint fill inside, fading to nothing at the texture edge.
                float a = Mathf.Max(Mathf.Exp(-d * d), .18f * Mathf.Clamp01(1f - r)) * Mathf.Clamp01((1f - r) / .08f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            radialRing.SetPixels32(pixels); radialRing.Apply(false, true);
            return radialRing;
        }
    }
    private static Texture2D RadialGlow
    {
        get
        {
            if (radialGlow != null) return radialGlow;
            const int size = 64;
            radialGlow = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "Radial Glow (Runtime)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f;
                float a = Mathf.Clamp01((Mathf.Exp(-4f * (dx * dx + dy * dy)) - Mathf.Exp(-4f)) / (1f - Mathf.Exp(-4f)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            radialGlow.SetPixels32(pixels); radialGlow.Apply(false, true);
            return radialGlow;
        }
    }

    private void TickHeartbeat(CombatFeedbackSettings settings, float dt)
    {
        bool executable = posture != null && posture.IsStaggered && posture.IsAvailable;
        if (executable && !wasExecutable) { executableAge = 0f; heartPhase = 0f; }
        else if (executable) executableAge += dt;
        wasExecutable = executable;
        if (!executable) { Heartbeat = 0f; executableAge = beatAge = float.PositiveInfinity; return; }
        // Posture refills during the stagger, so the beat quickens as the execution window closes.
        float bpm = Mathf.Lerp(settings.heartbeatBpm.x, settings.heartbeatBpm.y, PostureRatio);
        float previous = heartPhase;
        heartPhase = Mathf.Repeat(heartPhase + dt * Mathf.Max(1f, bpm) / 60f, 1f);
        // A new beat starts when the phase wraps (and at the moment of becoming executable).
        if (executableAge <= 0f || heartPhase < previous) beatAge = 0f; else beatAge += dt;
        Heartbeat = HeartbeatShape(heartPhase);
    }
    /// <summary>"Lub-dub": a full beat at phase 0, a smaller one shortly after, then rest.</summary>
    private static float HeartbeatShape(float phase)
    {
        float lub = Bump(phase, 0f) + Bump(phase, 1f);
        return Mathf.Clamp01(Mathf.Max(lub, .6f * Bump(phase, .2f)));
    }
    private static float Bump(float phase, float centre) { float d = (phase - centre) / .065f; return Mathf.Exp(-d * d); }
    private float FlashProgress(CombatFeedbackSettings settings) => Mathf.Clamp01(executableAge / Mathf.Max(.05f, settings.executableFlashSeconds));
    private float ExecutableBarScale(CombatFeedbackSettings settings)
    {
        if (!wasExecutable) return 1f;
        float kick = 1f - FlashProgress(settings);
        return 1f + settings.executableBarPunch * kick * kick * kick + .07f * Heartbeat;
    }
    private void AnimateExecutableBar(CombatFeedbackSettings settings)
    {
        float t = FlashProgress(settings), fade = 1f - t;
        bool executable = wasExecutable;
        // Brackets spring outward at the break, then tap outward on every beat.
        float spread = executable ? 10f * fade * fade * fade + 2.5f * Heartbeat : 0f;
        leftCap.rectTransform.anchoredPosition = new Vector2(-capX - spread, 0f);
        rightCap.rectTransform.anchoredPosition = new Vector2(capX + spread, 0f);
        // Execution palette: the whole bar re-dresses so the state reads even without the effects.
        nameLabel.color = executable ? settings.executableNameColor : NameColor;
        executablePlate.enabled = executable;
        if (executable)
        {
            healthImage.color = settings.executableHealthColor;
            healthLostImage.color = settings.executableHealthLostColor;
            executablePlate.color = settings.executablePlateColor;
            leftCap.color = rightCap.color = Color.Lerp(settings.executableAccentColor, Color.white, Mathf.Max(fade * fade, Heartbeat));
        }
        // Wide bloom + hot core glow behind the whole bar, both brightening on each beat.
        Color tint = settings.executableGlowColor, hot = Color.Lerp(tint, Color.white, .45f);
        executableBloom.enabled = executableGlow.enabled = executable;
        if (executable)
        {
            float beat = Mathf.Max(fade, Heartbeat);
            executableBloom.color = new Color(tint.r, tint.g, tint.b, tint.a * Mathf.Lerp(.3f, .75f, beat));
            executableGlow.color = new Color(hot.r, hot.g, hot.b, tint.a * Mathf.Lerp(.5f, 1f, beat));
        }
        float ripple = Mathf.Clamp01(beatAge / .6f);
        executableRipple.enabled = executable && ripple < 1f;
        if (executableRipple.enabled)
        {
            float reach = settings.executableRippleReach * (1f - (1f - ripple) * (1f - ripple));
            Color ring = settings.executableGlowColor;
            ring.a *= .9f * (1f - ripple) * (1f - ripple);
            executableRipple.color = ring;
            executableRipple.rectTransform.sizeDelta = new Vector2(width * 1.35f + 2f * reach, GlowHeight * 1.4f + 1.6f * reach);
        }
        // A brighter, quicker inner wave leads the outer one.
        float flare = Mathf.Clamp01(beatAge / .35f);
        executableRippleHot.enabled = executable && flare < 1f;
        if (executableRippleHot.enabled)
        {
            float reach = .6f * settings.executableRippleReach * (1f - (1f - flare) * (1f - flare));
            executableRippleHot.color = new Color(1f, 1f, 1f, (1f - flare) * (1f - flare));
            executableRippleHot.rectTransform.sizeDelta = new Vector2(width * 1.2f + 2f * reach, GlowHeight + 1.6f * reach);
        }
        executableFlash.enabled = executable && t < 1f;
        if (executableFlash.enabled) executableFlash.color = new Color(1f, .95f, .95f, .9f * fade * fade);
        executableSweep.enabled = executable && t < 1f;
        if (executableSweep.enabled)
        {
            var sweep = executableSweep.rectTransform;
            float x = 1f - fade * fade;
            sweep.anchorMin = sweep.anchorMax = new Vector2(x, .5f);
            executableSweep.color = new Color(1f, 1f, 1f, Mathf.Sin(Mathf.PI * t));
        }
    }

    private const float EdgeAttackSeconds = .04f;
    /// <summary>
    /// White line between remaining and lost health. A hit snaps it tall, bright and stretched to the right
    /// with a glow behind it; it then springs back (a small squash past rest) and settles. If the trail drains
    /// before the spring ends the line stays to finish it, fading out.
    /// </summary>
    private void AnimateHealthEdge(CombatFeedbackSettings settings, bool showTrail)
    {
        float settle = Mathf.Max(.05f, settings.trailEdgeSettleSeconds);
        bool springing = edgePulseAge < EdgeAttackSeconds + settle;
        float u = Mathf.Clamp01((edgePulseAge - EdgeAttackSeconds) / settle);
        float spring, burst;
        if (edgePulseAge < EdgeAttackSeconds)
        {
            float a = edgePulseAge / EdgeAttackSeconds;
            spring = burst = 1f - (1f - a) * (1f - a) * (1f - a);
        }
        else
        {
            // Damped wobble: overshoots below rest once (a squash), barely ticks back, and lands at 0 when u = 1.
            spring = Mathf.Exp(-4.5f * u) * Mathf.Cos(u * 2.4f * Mathf.PI) * (1f - u);
            burst = Mathf.Exp(-8f * u) * (1f - u);
        }
        if (!springing) spring = burst = 0f;
        trailEdge.enabled = settings.trailEdgeLine && (showTrail || springing);
        trailEdgeGlow.enabled = trailEdge.enabled && springing && settings.trailEdgeGlowColor.a > 0f;
        if (!trailEdge.enabled) return;
        float lift = Mathf.Max(0f, spring);
        // Drained before the spring finished: let the line go with it instead of popping off.
        float presence = showTrail ? 1f : 1f - u * u;
        float baseWidth = Mathf.Max(.5f, settings.trailEdgeWidth), height = BarHeight + 2f;
        var edge = trailEdge.rectTransform;
        edge.anchorMin = edge.anchorMax = new Vector2(HealthRatio, .5f);
        edge.pivot = new Vector2(.25f, .5f);
        edge.sizeDelta = new Vector2(baseWidth + settings.trailEdgeHitStretch * burst, height);
        edge.localScale = new Vector3(1f, Mathf.Max(.2f, 1f + settings.trailEdgePulseScale * spring), 1f);
        float alpha = Mathf.Lerp(settings.trailEdgeAlpha.x, settings.trailEdgeAlpha.y, lift) * presence;
        trailEdge.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        if (!trailEdgeGlow.enabled) return;
        var glow = trailEdgeGlow.rectTransform;
        glow.anchorMin = glow.anchorMax = edge.anchorMin;
        glow.pivot = new Vector2(.35f, .5f);
        glow.sizeDelta = new Vector2(14f + 3f * settings.trailEdgeHitStretch * burst, height * (1f + settings.trailEdgePulseScale * lift) * 1.8f);
        Color tint = settings.trailEdgeGlowColor;
        trailEdgeGlow.color = new Color(tint.r, tint.g, tint.b, tint.a * Mathf.Max(burst, .35f * lift) * presence);
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
        nameLabel.fontSize = 13f; nameLabel.fontStyle = FontStyles.Bold; nameLabel.color = NameColor;
        nameLabel.alignment = TextAlignmentOptions.Bottom; nameLabel.raycastTarget = false;
        nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        nameLabel.overflowMode = CombatUIAssets.ClippedOverflow(nameLabel);
        nameLabel.text = CombatDamageEvents.DisplayNameOf(this);

        // Executable glow sits behind the bar; the flash and sweep go on top of it (below).
        executableBloom = EllipseImage("Executable bloom", RadialGlow, width * 1.7f, GlowHeight * 3.2f);
        executableGlow = EllipseImage("Executable glow", RadialGlow, width * 1.25f, GlowHeight * 1.3f);
        executableRipple = EllipseImage("Executable ripple", RadialRing, width * 1.35f, GlowHeight * 1.4f);
        executableRippleHot = EllipseImage("Executable flare", RadialRing, width * 1.2f, GlowHeight);
        executablePlate = Rect("Executable plate", bars, 0f, 0f, width + 2f * (CapWidth + CapGap) + 16f, BarHeight + 16f).gameObject.AddComponent<Image>();
        executablePlate.sprite = CombatUIAssets.SoftGlow; executablePlate.type = Image.Type.Sliced; executablePlate.pixelsPerUnitMultiplier = 2f;
        executablePlate.raycastTarget = false; executablePlate.enabled = false;

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
        trailEdgeGlow = Rect("Trail edge glow", healthTrack, 0f, 0f, 14f, BarHeight * 2f).gameObject.AddComponent<RawImage>();
        trailEdgeGlow.texture = RadialGlow; trailEdgeGlow.raycastTarget = false; trailEdgeGlow.enabled = false;
        trailEdge = Rect("Trail edge", healthTrack, 0f, 0f, settings.trailEdgeWidth, BarHeight + 2f).gameObject.AddComponent<Image>();
        trailEdge.color = new Color(1f, 1f, 1f, .95f); trailEdge.raycastTarget = false; trailEdge.enabled = false;
        executableFlash = Stretch("Executable flash", healthTrack).gameObject.AddComponent<Image>();
        executableFlash.raycastTarget = false; executableFlash.enabled = false;
        executableSweep = Rect("Executable sweep", healthTrack, 0f, 0f, SweepWidth, BarHeight + 6f).gameObject.AddComponent<Image>();
        executableSweep.sprite = CombatUIAssets.SoftGlow; executableSweep.raycastTarget = false; executableSweep.enabled = false;
        capX = width * .5f + CapGap + CapWidth * .5f;
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
        staggerGlow = BadgeLayer("Stagger glow", RadialGlow);
        staggerEcho = BadgeLayer("Stagger echo", null);
        staggerBadge = new GameObject("Stagger", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        staggerBadge.transform.SetParent(staggerPanel, false); staggerBadge.raycastTarget = false;
        var feedback = CombatFeedbackSettings.Instance;
        staggerBadge.texture = feedback != null ? feedback.staggerImage : null;
        staggerEcho.texture = staggerBadge.texture;
    }
    private RawImage EllipseImage(string label, Texture texture, float w, float h)
    {
        var glow = Rect(label, bars, 0f, 0f, w, h).gameObject.AddComponent<RawImage>();
        glow.texture = texture; glow.raycastTarget = false; glow.enabled = false;
        return glow;
    }
    private RawImage BadgeLayer(string label, Texture texture)
    {
        var layer = new GameObject(label, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        layer.transform.SetParent(staggerPanel, false); layer.raycastTarget = false; layer.texture = texture; layer.enabled = false;
        return layer;
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
    private void OnDisable()
    {
        Hide(); trail.Reset(float.NaN); wasExecutable = false; Heartbeat = 0f; executableAge = beatAge = float.PositiveInfinity;
        lastHealth = float.NaN; edgePulseAge = float.PositiveInfinity;
        if (bars != null) { bars.anchoredPosition = Vector2.zero; bars.localScale = Vector3.one; }
    }
    private void OnDestroy()
    {
        if (uiRoot != null) { if (Application.isPlaying) Destroy(uiRoot.gameObject); else DestroyImmediate(uiRoot.gameObject); }
    }
}
