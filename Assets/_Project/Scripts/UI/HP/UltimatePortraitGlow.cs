using UnityEngine;
using UnityEngine.UI;

/// <summary>Animates a private UI material, including stencil-modified UI rendering materials.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class UltimatePortraitGlow : MonoBehaviour
{
    [SerializeField] private UltimateCharge source;
    [SerializeField] private Graphic portrait;
    [Tooltip("Persistent source material used to recover a missing portrait material.")]
    [SerializeField] private Material portraitMaterial;
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
    private UltimatePortraitMaterial materialEffect;
    private Graphic materialOwner;
    private bool warnedInvalidMaterial;
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
        materialEffect.SetGlow(spread, intensity, tint);
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
        if (portrait != materialOwner) ReleaseMaterial();
        if (source == null) source = GetComponent<UltimateCharge>();
        if (source == null || portrait == null) return false;
        // Graphic.material must always retain a persistent source, never a DontSave clone.
        if (portrait.material == portrait.defaultMaterial && IsGlowMaterial(portraitMaterial))
            portrait.material = portraitMaterial;
        if (!IsGlowMaterial(portrait.material))
        {
            ReleaseMaterial();
            if (!warnedInvalidMaterial)
                Debug.LogWarning("UltimatePortraitGlow: assign a HexGlowFrame material to the portrait; glow will resume automatically when assigned.", this);
            warnedInvalidMaterial = true;
            return false;
        }
        warnedInvalidMaterial = false;
        if (materialEffect != null) return true;
        materialOwner = portrait;
        materialEffect = portrait.gameObject.AddComponent<UltimatePortraitMaterial>();
        materialEffect.hideFlags = HideFlags.HideAndDontSave;
        portrait.SetMaterialDirty();
        displayedProgress = UltimateCharge.ResolveHUDSource(this, source).ReadinessRatio;
        heartbeatTime = 0f;
        wasReady = false;
        return true;
    }

    private static bool IsGlowMaterial(Material material)
    {
        return material != null && material.HasProperty(Spread)
            && material.HasProperty(Intensity) && material.HasProperty(GlowColor);
    }

    private void OnDisable() { ReleaseMaterial(); }
    private void OnDestroy() { ReleaseMaterial(); }

    public void StopPreview() { ReleaseMaterial(); }

    private void ReleaseMaterial()
    {
        if (materialEffect != null)
        {
            materialEffect.enabled = false;
            if (Application.isPlaying) Destroy(materialEffect); else DestroyImmediate(materialEffect);
        }
        if (materialOwner != null) materialOwner.SetMaterialDirty();
        materialEffect = null;
        materialOwner = null;
    }
}
