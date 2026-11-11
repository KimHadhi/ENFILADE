using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wardrobe's two looks on the menu's character (MainMenuPreview), both from the hit flash's overlay shader
/// (Hidden/ENFILADE/HitFlashOverlay), so nothing new is drawn for it:
/// <list type="bullet">
/// <item>a piece being tried on (the pointer resting on a choice) is worn see-through: a faint white body with a pink rim,
/// so it reads at once as "this is how it would sit" and never as worn (the user asked for a transparent preview,
/// 2026-11-11: "옷 선택 옵션에서도 투명하게 옷 미리 보기가 있게끔");</item>
/// <item>the pieces in a slot the pointer is on are marked where they are on the body with a pink rim that breathes, so a
/// slot's tile and what it holds read together.</item>
/// </list>
/// </summary>
public static class OutfitGhost
{
    private static readonly int TintColor = Shader.PropertyToID("_TintColor"), Tint = Shader.PropertyToID("_Tint"),
        RimColor = Shader.PropertyToID("_RimColor"), Rim = Shader.PropertyToID("_Rim");
    private static Material ghost, mark;
    private static readonly Dictionary<Renderer, Material[]> marked = new Dictionary<Renderer, Material[]>();
    private static readonly List<Material> scratch = new List<Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        ghost = mark = null;
        marked.Clear();
    }

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
    public static void Show(IEnumerable<Renderer> renderers)
    {
        foreach (var pair in marked)
            if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
        marked.Clear();
        var material = Mark;
        if (renderers == null || material == null) return;
        foreach (var renderer in renderers)
        {
            if (renderer == null || marked.ContainsKey(renderer)) continue;
            var own = renderer.sharedMaterials;
            marked[renderer] = own;
            scratch.Clear();
            scratch.AddRange(own);
            scratch.Add(material);
            renderer.sharedMaterials = scratch.ToArray();
        }
    }

    /// <summary>The marks breathe (called every frame while a slot is pointed at).</summary>
    public static void Breathe(float time)
    {
        if (mark == null || marked.Count == 0) return;
        float beat = .55f + .45f * Mathf.Sin(time * 5f);
        mark.SetFloat(Rim, beat);
        mark.SetFloat(Tint, .4f * beat);
    }
}
