using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wardrobe's looks on the menu's character (MainMenuPreview), all from the hit flash's overlay shader
/// (Hidden/ENFILADE/HitFlashOverlay), so nothing new is drawn for it:
/// <list type="bullet">
/// <item>a piece being tried on (the pointer resting on a choice) is worn as it really looks, its own colours, with a
/// quick pink rim over it so it reads as a try. It was worn see-through at first (2026-11-11, "투명하게 옷 미리 보기"),
/// and the user found that a piece that looked plain as a ghost was pretty worn, so people would pass it by
/// (2026-11-12);</item>
/// <item>pieces a try would take off (벗기) are drawn see-through, a faint white body with a pink rim: there, but going,
/// never simply gone, which read as already taken off;</item>
/// <item>the pieces in a slot the pointer is on are marked where they are on the body with a pink rim that breathes, so a
/// slot's tile and what it holds read together.</item>
/// </list>
/// </summary>
public static class OutfitGhost
{
    private static readonly int TintColor = Shader.PropertyToID("_TintColor"), Tint = Shader.PropertyToID("_Tint"),
        RimColor = Shader.PropertyToID("_RimColor"), Rim = Shader.PropertyToID("_Rim");
    private static Material ghost, mark, trial;
    private static readonly Dictionary<Renderer, Material[]> marked = new Dictionary<Renderer, Material[]>();
    private static readonly Dictionary<Renderer, Material[]> trialled = new Dictionary<Renderer, Material[]>();
    private static readonly List<Material> scratch = new List<Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        ghost = mark = trial = null;
        marked.Clear();
        trialled.Clear();
    }

    private static Material TrialRim
    {
        get
        {
            if (trial != null) return trial;
            var shader = FeelAssets.OverlayShader;
            if (shader == null) return null;
            trial = new Material(shader) { name = "Outfit Trial (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            // Barely any tint, so the piece shows its own colours; the rim does the telling.
            trial.SetColor(TintColor, new Color(1f, .31f, .47f, .05f));
            trial.SetColor(RimColor, KitStyle.Accent);
            trial.SetFloat(Rim, 1f);
            return trial;
        }
    }

    /// <summary>Lights these renderers, a piece tried on in its own colours, with a quick pink rim over their own
    /// materials, or takes it off every piece when <paramref name="renderers"/> is null.</summary>
    public static void Trial(IEnumerable<Renderer> renderers) => Overlay(trialled, renderers != null ? TrialRim : null, renderers);

    private static Material Ghost
    {
        get
        {
            if (ghost != null) return ghost;
            var shader = FeelAssets.OverlayShader;
            if (shader == null) return null;
            ghost = new Material(shader) { name = "Outfit Try (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            ghost.SetColor(TintColor, new Color(1f, .96f, .97f, .3f));
            ghost.SetFloat(Tint, 1f);
            ghost.SetColor(RimColor, KitStyle.Accent);
            ghost.SetFloat(Rim, 1f);
            return ghost;
        }
    }

    private static Material Mark
    {
        get
        {
            if (mark != null) return mark;
            var shader = FeelAssets.OverlayShader;
            if (shader == null) return null;
            mark = new Material(shader) { name = "Outfit Slot Mark (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            mark.SetColor(TintColor, new Color(1f, .31f, .47f, .18f));
            mark.SetColor(RimColor, KitStyle.Accent);
            return mark;
        }
    }

    /// <summary>Wears these renderers see-through, every sub-mesh, casting no shadow (the menu's character is rebuilt
    /// for every try, so nothing needs putting back).</summary>
    public static void Wear(IEnumerable<Renderer> renderers)
    {
        var material = Ghost;
        if (material == null || renderers == null) return;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            int count = Mathf.Max(1, renderer.sharedMaterials.Length);
            var materials = new Material[count];
            for (int i = 0; i < count; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>Marks these renderers with the breathing pink rim (an extra material over their own), or takes every mark
    /// off when <paramref name="renderers"/> is null.</summary>
    public static void Show(IEnumerable<Renderer> renderers) => Overlay(marked, renderers != null ? Mark : null, renderers);

    // One extra material over each renderer's own, kept so it can be taken off again; the old ones come off first.
    private static void Overlay(Dictionary<Renderer, Material[]> kept, Material material, IEnumerable<Renderer> renderers)
    {
        foreach (var pair in kept)
            if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
        kept.Clear();
        if (renderers == null || material == null) return;
        foreach (var renderer in renderers)
        {
            if (renderer == null || kept.ContainsKey(renderer)) continue;
            var own = renderer.sharedMaterials;
            kept[renderer] = own;
            scratch.Clear();
            scratch.AddRange(own);
            scratch.Add(material);
            renderer.sharedMaterials = scratch.ToArray();
        }
    }

    /// <summary>The marks breathe, the try's rim a little quicker, and a piece a try would take off fades in and out, as
    /// if going (called every frame while any of them shows).</summary>
    public static void Breathe(float time)
    {
        if (mark != null && marked.Count > 0)
        {
            float beat = .55f + .45f * Mathf.Sin(time * 5f);
            mark.SetFloat(Rim, beat);
            mark.SetFloat(Tint, .4f * beat);
        }
        if (trial != null && trialled.Count > 0)
        {
            float quick = .6f + .4f * Mathf.Sin(time * 7.5f);
            trial.SetFloat(Rim, quick);
            trial.SetFloat(Tint, .3f * quick);
        }
        // Only the removal try wears the ghost now; nothing else is drawn with it, so it can always beat.
        if (ghost != null)
        {
            float fade = .5f + .5f * Mathf.Sin(time * 4f);
            ghost.SetFloat(Tint, .35f + .65f * fade);
            ghost.SetFloat(Rim, .5f + .5f * fade);
        }
    }
}
