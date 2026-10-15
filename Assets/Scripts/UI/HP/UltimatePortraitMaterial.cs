using UnityEngine;
using UnityEngine.UI;

// Rendering-only clone: the Image's serialized material always remains its source asset.
[ExecuteAlways]
[AddComponentMenu("")]
public sealed class UltimatePortraitMaterial : MonoBehaviour, IMaterialModifier
{
    private Material instance;
    private float spread, intensity;
    private Color tint;

    public void SetGlow(float newSpread, float newIntensity, Color newTint)
    {
        spread = newSpread;
        intensity = newIntensity;
        tint = newTint;
        ApplyGlow();
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (!isActiveAndEnabled || baseMaterial == null || !baseMaterial.HasProperty("_GlowIntensity"))
            return baseMaterial;
        if (instance == null)
            instance = new Material(baseMaterial) { name = baseMaterial.name + " (Ultimate Render)", hideFlags = HideFlags.HideAndDontSave };
        // Includes stencil properties from any preceding UI Mask modifier.
        instance.shader = baseMaterial.shader;
        instance.CopyPropertiesFromMaterial(baseMaterial);
        ApplyGlow();
        return instance;
    }

    private void ApplyGlow()
    {
        if (instance == null) return;
        instance.SetFloat("_GlowBloomSpread", spread);
        instance.SetFloat("_GlowIntensity", intensity);
        instance.SetColor("_GlowColor", tint);
    }

    private void OnDisable()
    {
        var graphic = GetComponent<Graphic>();
        if (graphic != null) graphic.SetMaterialDirty();
        if (instance != null)
        {
            if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
            instance = null;
        }
    }
}
