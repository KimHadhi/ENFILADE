using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "I got hit" look for any damaged target: the whole body flashes red (Minecraft-style), the silhouette gets a red rim,
/// and the exact hit spot glows hot and cools down (up to four spots at once). A kill flash turns the body white.
/// Works by appending one overlay material (Hidden/Morphang/HitFlashOverlay) to the target's mesh renderers while the
/// flash runs and removing it afterwards, so idle enemies cost nothing and their own materials are never modified.
/// Added on demand; timing and colours in GunFeel → Enemy hit flash.
/// </summary>
[DisallowMultipleComponent]
public sealed class HitFlashRenderer : MonoBehaviour
{
    private struct Spot { public Vector3 local; public float age, strength; }
    private struct Slot { public Renderer renderer; public int index; }

    private static Material overlay;
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor"), TintId = Shader.PropertyToID("_Tint"),
        RimColorId = Shader.PropertyToID("_RimColor"), RimId = Shader.PropertyToID("_Rim"), GlowColorId = Shader.PropertyToID("_GlowColor"),
        RadiusId = Shader.PropertyToID("_HitRadius"), WhiteId = Shader.PropertyToID("_White");
    private static readonly int[] HitIds = { Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"), Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3") };

    private readonly Spot[] spots = new Spot[4];
    private readonly List<Slot> slots = new List<Slot>();
    private readonly List<Material> scratch = new List<Material>();
    private MaterialPropertyBlock block;
    private int nextSpot;
    private float tintAge = 99f, tintStrength, whiteAge = 99f, whiteSeconds = .12f;

    public static Material OverlayMaterial
    {
        get
        {
            if (overlay != null) return overlay;
            var shader = FeelAssets.OverlayShader;
            if (shader == null) return null;
            overlay = new Material(shader) { name = "Hit Flash Overlay (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            return overlay;
        }
    }

    public bool IsFlashing => slots.Count > 0;

    public static HitFlashRenderer For(Component target)
    {
        if (target == null) return null;
        var flash = target.GetComponent<HitFlashRenderer>();
        return flash != null ? flash : target.gameObject.AddComponent<HitFlashRenderer>();
    }

    /// <summary>Red flash plus a glowing spot at point (ignored when zero, e.g. executions without a contact point).</summary>
    public void Flash(Vector3 point, bool critical)
    {
        if (!GunFeelSettings.Instance.hitFlash) return;
        tintAge = 0f;
        tintStrength = critical ? 1f : .8f;
        if (point != Vector3.zero && Near(point))
        {
            spots[nextSpot] = new Spot { local = transform.InverseTransformPoint(point), age = 0f, strength = critical ? 1.5f : 1f };
            nextSpot = (nextSpot + 1) % spots.Length;
        }
        Attach();
    }

    /// <summary>White-hot body flash that cools into the red tint; used by corpses on the killing blow.</summary>
    public void KillFlash(Vector3 point, float seconds)
    {
        whiteAge = 0f; whiteSeconds = Mathf.Max(.02f, seconds);
        Flash(point, true);
    }

    private bool Near(Vector3 point)
    {
        // Contact points far from the body (some melee sources report the attacker) would light nothing anyway.
        var bounds = new Bounds(transform.position, Vector3.one * .1f);
        foreach (var renderer in Renderers()) bounds.Encapsulate(renderer.bounds);
        bounds.Expand(.6f);
        return bounds.Contains(point);
    }

    private IEnumerable<Renderer> Renderers()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && renderer.enabled && renderer.GetComponentInParent<Canvas>() == null)
                yield return renderer;
    }

    private void Attach()
    {
        enabled = true;
        if (slots.Count > 0) return;
        var material = OverlayMaterial;
        if (material == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        foreach (var renderer in Renderers())
        {
            renderer.GetSharedMaterials(scratch);
            if (scratch.Contains(material)) continue;
            scratch.Add(material);
            renderer.SetSharedMaterials(scratch);
            slots.Add(new Slot { renderer = renderer, index = scratch.Count - 1 });
        }
    }

    private void Detach()
    {
        foreach (var slot in slots)
        {
            if (slot.renderer == null) continue;
            block.Clear();
            slot.renderer.SetPropertyBlock(block, slot.index);
            slot.renderer.GetSharedMaterials(scratch);
            scratch.Remove(OverlayMaterial);
            slot.renderer.SetSharedMaterials(scratch);
        }
        slots.Clear();
    }

    private void LateUpdate()
    {
        if (slots.Count == 0) { enabled = false; return; }
        var settings = GunFeelSettings.Instance;
        // Game time: a hit-stop freezes the flash at its brightest, which sells the impact.
        float dt = Time.deltaTime;
        tintAge += dt; whiteAge += dt;
        float tint = tintStrength * Fall(tintAge / settings.hitTintSeconds);
        float white = whiteAge < whiteSeconds ? 1f - whiteAge / whiteSeconds : 0f;
        bool glowing = false;
        for (int i = 0; i < spots.Length; i++)
        {
            spots[i].age += dt;
            if (spots[i].strength > 0f && spots[i].age < settings.hitGlowSeconds) glowing = true;
        }
        if (tint <= .001f && white <= 0f && !glowing) { Detach(); enabled = false; return; }

        block.Clear();
        block.SetColor(TintColorId, settings.hitTintColor);
        block.SetFloat(TintId, tint);
        block.SetColor(RimColorId, settings.hitRimColor);
        block.SetFloat(RimId, Mathf.Clamp01(tint * 1.2f));
        block.SetColor(GlowColorId, settings.hitGlowColor);
        block.SetFloat(RadiusId, settings.hitGlowRadius);
        block.SetFloat(WhiteId, white);
        for (int i = 0; i < spots.Length; i++)
        {
            float strength = spots[i].strength > 0f ? spots[i].strength * Mathf.Pow(Fall(spots[i].age / settings.hitGlowSeconds), .7f) : 0f;
            Vector3 world = transform.TransformPoint(spots[i].local);
            block.SetVector(HitIds[i], new Vector4(world.x, world.y, world.z, strength));
        }
        foreach (var slot in slots)
            if (slot.renderer != null) slot.renderer.SetPropertyBlock(block, slot.index);
    }

    /// <summary>1 → 0 with a fast start and a soft tail.</summary>
    private static float Fall(float t) { t = Mathf.Clamp01(t); return (1f - t) * (1f - t); }

    private void OnDisable() { if (slots.Count > 0) Detach(); }
}
