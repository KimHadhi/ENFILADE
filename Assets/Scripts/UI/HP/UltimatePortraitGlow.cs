using UnityEngine;
using UnityEngine.UI;

/// <summary>Animates a private UI material, including stencil-modified UI rendering materials.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class UltimatePortraitGlow : MonoBehaviour
{
    [SerializeField] private UltimateCharge source;
    [SerializeField] private Graphic portrait;
    [Header("Charging: empty to ready")]
    [SerializeField] private Vector2 spreadRange = new Vector2(0.005f, 0.04f);
    [SerializeField] private Vector2 intensityRange = new Vector2(0.65f, 2.2f);
    [SerializeField] private Color emptyGlowColor = new Color(1f, 0.35f, 0.42f, 1f);
    [SerializeField] private Color readyGlowColor = new Color(1f, 0.015f, 0.03f, 1f);
    [SerializeField, Min(0)] private float transitionSeconds = 0.15f;
    [Header("Ready heartbeat")]
    [SerializeField, Min(0)] private float heartbeatIntensity = 1.4f;
    [SerializeField, Min(0.1f)] private float heartbeatsPerSecond = 1.1f;

    private static readonly int Spread = Shader.PropertyToID("_GlowBloomSpread");
    private static readonly int Intensity = Shader.PropertyToID("_GlowIntensity");
    private static readonly int GlowColor = Shader.PropertyToID("_GlowColor");
    private Material originalMaterial, instance;
    private Graphic materialOwner;
    private float displayedProgress, heartbeatTime;
    private bool wasReady;

    private void LateUpdate() { if (Application.IsPlaying(gameObject)) RefreshNow(Time.unscaledDeltaTime); }

    public void RefreshNow(float deltaTime = 0f)
    {
        if (!EnsureMaterial()) return;
        UltimateCharge charge = UltimateCharge.ResolveHUDSource(this, source);
        float dt = Mathf.Max(0f, deltaTime);
        displayedProgress = transitionSeconds <= 0f ? charge.ReadinessRatio
            : Mathf.MoveTowards(displayedProgress, charge.ReadinessRatio, dt / transitionSeconds);
        bool ready = charge.IsReady;
        if (ready && !wasReady) heartbeatTime = 0f;
        if (ready) heartbeatTime += dt; else heartbeatTime = 0f;
        wasReady = ready;
        float pulse = ready ? Heartbeat(heartbeatTime, heartbeatsPerSecond) : 0f;
        float spread = Mathf.Max(0.0001f, Mathf.Lerp(spreadRange.x, spreadRange.y, displayedProgress));
        float intensity = Mathf.Max(0f, Mathf.Lerp(intensityRange.x, intensityRange.y, displayedProgress) + pulse * heartbeatIntensity);
        Color tint = Color.Lerp(emptyGlowColor, readyGlowColor, displayedProgress);
        Apply(instance, spread, intensity, tint);
        // A UI Mask can render a stencil copy rather than Graphic.material itself.
        Material rendered = portrait.materialForRendering;
        if (rendered != null && rendered != instance && rendered != originalMaterial)
            Apply(rendered, spread, intensity, tint);
    }

    internal static float Heartbeat(float time, float frequency)
    {
        float phase = Mathf.Repeat(time * Mathf.Max(0.1f, frequency), 1f);
        float first = (phase - 0.12f) / 0.055f;
        float second = (phase - 0.32f) / 0.075f;
        return Mathf.Clamp01(Mathf.Exp(-first * first) + 0.6f * Mathf.Exp(-second * second));
    }

    private bool EnsureMaterial()
    {
        if (instance != null && (portrait != materialOwner || (portrait != null && portrait.material != instance))) ReleaseMaterial();
        if (instance != null) return source != null && portrait != null;
        if (source == null) source = GetComponent<UltimateCharge>();
        if (source == null || portrait == null || portrait.material == null) return false;
        originalMaterial = portrait.material;
        if (!originalMaterial.HasProperty(Spread) || !originalMaterial.HasProperty(Intensity) || !originalMaterial.HasProperty(GlowColor))
        {
            Debug.LogError("UltimatePortraitGlow: portrait must use the HexGlowFrame material.", this);
            enabled = false;
            return false;
        }
        instance = new Material(originalMaterial) { name = originalMaterial.name + " (Ultimate Runtime)", hideFlags = HideFlags.HideAndDontSave };
        materialOwner = portrait;
        portrait.material = instance;
        displayedProgress = UltimateCharge.ResolveHUDSource(this, source).ReadinessRatio;
        heartbeatTime = 0f;
        wasReady = false;
        return true;
    }

    private static void Apply(Material material, float spread, float intensity, Color tint)
    {
        if (!material.HasProperty(Spread)) return;
        material.SetFloat(Spread, spread);
        material.SetFloat(Intensity, intensity);
        material.SetColor(GlowColor, tint);
    }

    private void OnDisable() { ReleaseMaterial(); }
    private void OnDestroy() { ReleaseMaterial(); }

    public void StopPreview() { ReleaseMaterial(); }

    private void ReleaseMaterial()
    {
        if (instance == null) return;
        if (materialOwner != null && materialOwner.material == instance) materialOwner.material = originalMaterial;
        if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
        instance = null;
        originalMaterial = null;
        materialOwner = null;
    }
}
