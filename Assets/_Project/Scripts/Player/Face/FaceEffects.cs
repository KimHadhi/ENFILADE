using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The see-through parts of a face, drawn so they hide nothing they shouldn't. A creator keeps a face's extras (blush
/// lines, tears, sweat, a pale veil over the brow) as thin plates inside the head, in a material of their own that
/// blends and writes depth, and an expression's keys bring them out onto the skin. That material and the hair's share
/// one place in the draw order (lilToon's transparent family: AlphaTest+10, drawn among the opaque), and here the
/// face's is drawn first: every texel of a plate, drawn or not, then hides the hair behind it. Mayo's blush lines are
/// two flat quads whose outer corners stand out past her cheeks, in a material that discards nothing (_Cutoff -0.001),
/// so with her flustered face on, the side hair behind those corners was cut away in two dark triangles (the user,
/// 2026-11-14: "뒤쪽에 저 김가루 생기는데"). Measured on her meshes: 1.9 cm² of hair behind texels that draw nothing,
/// and with the pale veil of her panic face 30 cm² of hair roots behind texels that do.
/// So the blended materials of a face's meshes are worn as twins made here, the creator's own left as they are:
/// - drawn last of all that is drawn among the opaque (FaceLife.csv Effects.queue), so they blend over the hair behind
///   them, and hair in front of them still covers them by its depth;
/// - a texel that draws nothing writes no depth (Effects.cut), so it cuts nothing drawn later either (smoke, glass).
///   lilToon lights a blended texel by its alpha, emission too, so such a texel showed nothing to lose; a material
///   told to shine on clear texels all the same (a glass's reflection) keeps them.
/// A material its creator placed in the order themselves keeps its place: one with a queue of its own, one drawn by a
/// stencil test, and one that writes a stencil something else the character wears reads among the opaque (brows shown
/// through hair must be drawn before the hair). Ichigo's writes one, but its reader, the hair's shadow, is drawn
/// after all of them, so hers goes last too. Read from the materials alone, so a modder's avatar is treated the same;
/// the names are lilToon's, which their materials go by here too (ModMaterials).
/// </summary>
internal static class FaceEffects
{
    private static readonly int Cutoff = Shader.PropertyToID("_Cutoff"), ZWrite = Shader.PropertyToID("_ZWrite"),
        DstBlend = Shader.PropertyToID("_DstBlend"), StencilComp = Shader.PropertyToID("_StencilComp");
    private static readonly int[] StencilOps =
    {
        Shader.PropertyToID("_StencilPass"), Shader.PropertyToID("_StencilFail"), Shader.PropertyToID("_StencilZFail")
    };
    // lilToon's lights that can shine on a clear texel: each when it is on and not told to go by the texel's alpha.
    private static readonly (int on, int byAlpha)[] ClearLights =
    {
        (Shader.PropertyToID("_UseReflection"), Shader.PropertyToID("_ReflectionApplyTransparency")),
        (Shader.PropertyToID("_UseMatCap"), Shader.PropertyToID("_MatCapApplyTransparency")),
        (Shader.PropertyToID("_UseMatCap2nd"), Shader.PropertyToID("_MatCap2ndApplyTransparency")),
        (Shader.PropertyToID("_UseRim"), Shader.PropertyToID("_RimApplyTransparency")),
        (Shader.PropertyToID("_UseGlitter"), Shader.PropertyToID("_GlitterApplyTransparency"))
    };

    // What a twin stands for, and whether the character it was made on reads a stencil among the opaque.
    private readonly struct Origin
    {
        public readonly Material Source;
        public readonly bool Ordered;

        public Origin(Material source, bool ordered)
        {
            Source = source;
            Ordered = ordered;
        }
    }

    // Each material met and its twin (null: it needs none). Twins are shared by every model wearing the material and
    // kept for the run: a handful, one or two per see-through material of a face.
    private static readonly Dictionary<(Material source, bool ordered), Material> twinOf = new Dictionary<(Material, bool), Material>();
    private static readonly Dictionary<Material, Origin> originOf = new Dictionary<Material, Origin>();
    private static readonly List<Material> worn = new List<Material>();
    private static readonly List<(Material source, bool ordered)> gone = new List<(Material, bool)>();
    private static bool listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        foreach (var twin in originOf.Keys)
            if (twin != null) Object.Destroy(twin);
        twinOf.Clear();
        originOf.Clear();
        listening = false;
    }

    /// <summary>
    /// Puts the twins on a face's meshes (<paramref name="meshes"/>: those its keys are on), where their materials need
    /// one. <paramref name="all"/>: everything the character is drawn with, its clothes too.
    /// </summary>
    internal static void Wear(Renderer[] meshes, Renderer[] all)
    {
        if (meshes == null || meshes.Length == 0) return;
        bool ordered = Reads(all);
        foreach (var mesh in meshes)
        {
            if (mesh == null) continue;
            mesh.GetSharedMaterials(worn);
            bool changed = false;
            for (int i = 0; i < worn.Count; i++)
            {
                var twin = TwinOf(SourceOf(worn[i]), ordered);
                if (twin == worn[i]) continue;
                worn[i] = twin;
                changed = true;
            }
            if (changed) mesh.SetSharedMaterials(worn);
        }
        worn.Clear();
    }

    /// <summary>The material a twin stands for; itself for any other.</summary>
    internal static Material SourceOf(Material material) =>
        material != null && originOf.TryGetValue(material, out var origin) && origin.Source != null ? origin.Source : material;

    /// <summary>
    /// What to draw <paramref name="next"/> with where <paramref name="shown"/> was: its twin of the same kind when a
    /// twin was there (a colour set swapping a face's see-through material), itself otherwise.
    /// </summary>
    internal static Material Like(Material shown, Material next) =>
        shown != null && originOf.TryGetValue(shown, out var origin) ? TwinOf(next, origin.Ordered) : next;

    // The twin to draw a material with on a face; itself when it needs none.
    private static Material TwinOf(Material material, bool ordered)
    {
        if (material == null || originOf.ContainsKey(material)) return material;
        if (twinOf.TryGetValue((material, ordered), out var twin)) return twin != null ? twin : material;
        if (!listening)
        {
            // The numbers may change with their table: the twins take them without anything being dressed again.
            GameTables.Reloaded += Refresh;
            listening = true;
        }
        Forget();
        twin = null;
        if (Late(material, ordered) || Unseen(material))
        {
            twin = new Material(material) { name = material.name, hideFlags = HideFlags.HideAndDontSave };
            Fit(twin, material, ordered);
            originOf[twin] = new Origin(material, ordered);
        }
        twinOf[(material, ordered)] = twin;
        return twin != null ? twin : material;
    }

    // The twin as its material is now, but for the two things it is made for.
    private static void Fit(Material twin, Material source, bool ordered)
    {
        twin.CopyPropertiesFromMaterial(source);
        bool on = FaceTables.Number("Effects.on", 1f) > 0f;
        twin.renderQueue = on && Late(source, ordered) ? Last : source.renderQueue;
        if (on && Unseen(source)) twin.SetFloat(Cutoff, Mathf.Max(1e-5f, FaceTables.Number("Effects.cut", .001f)));
    }

    private static void Refresh()
    {
        foreach (var pair in originOf)
            if (pair.Key != null && pair.Value.Source != null) Fit(pair.Key, pair.Value.Source, pair.Value.Ordered);
        // Those judged to need no twin are judged again by the new numbers, the next time a face is fitted.
        foreach (var pair in twinOf)
            if (ReferenceEquals(pair.Value, null)) gone.Add(pair.Key);
        foreach (var key in gone) twinOf.Remove(key);
        gone.Clear();
    }

    // Materials that went with their model (a mod read again makes its materials anew): their twins go too.
    private static void Forget()
    {
        if (twinOf.Count < 64) return;
        foreach (var key in twinOf.Keys)
            if (key.source == null) gone.Add(key);
        foreach (var key in gone)
        {
            var twin = twinOf[key];
            twinOf.Remove(key);
            if (ReferenceEquals(twin, null)) continue;
            originOf.Remove(twin);
            if (twin != null) Object.Destroy(twin);
        }
        gone.Clear();
    }

    // The last place among the opaque: what is drawn there is drawn after every hair, and still before the sky and
    // everything truly transparent, as its creator had it.
    private static int Last =>
        Mathf.Clamp(Mathf.RoundToInt(FaceTables.Number("Effects.queue", (int)RenderQueue.GeometryLast)), (int)RenderQueue.AlphaTest + 1,
            (int)RenderQueue.GeometryLast);

    // Whether anything the character is drawn with among the opaque draws by a stencil test: then what writes the
    // stencil before it has to stay before it.
    private static bool Reads(Renderer[] all)
    {
        bool reads = false;
        if (all == null) return false;
        foreach (var renderer in all)
        {
            if (renderer == null || reads) continue;
            renderer.GetSharedMaterials(worn);
            foreach (var material in worn)
                if (material != null && material.renderQueue <= (int)RenderQueue.GeometryLast && Tests(SourceOf(material))) reads = true;
        }
        worn.Clear();
        return reads;
    }

    // Blended, yet drawn among the opaque (past the alpha-tested, not past the last of them): what shares its place in
    // the order with the hair.
    private static bool Blended(Material material)
    {
        if (material.shader == null) return false;
        int queue = material.renderQueue;
        if (queue <= (int)RenderQueue.AlphaTest || queue > (int)RenderQueue.GeometryLast) return false;
        return !material.HasProperty(DstBlend) || material.GetFloat(DstBlend) != (float)BlendMode.Zero;
    }

    // Blended and left where its shader puts it, by a creator who set no order: it goes last.
    private static bool Late(Material material, bool ordered)
    {
        if (!Blended(material) || material.renderQueue >= Last || material.renderQueue != material.shader.renderQueue) return false;
        return !Tests(material) && (!ordered || !Writes(material));
    }

    private static bool Tests(Material material)
    {
        if (!material.HasProperty(StencilComp)) return false;
        float compare = material.GetFloat(StencilComp);
        return compare != (float)CompareFunction.Always && compare != (float)CompareFunction.Disabled;
    }

    private static bool Writes(Material material)
    {
        foreach (int op in StencilOps)
            if (material.HasProperty(op) && material.GetFloat(op) != (float)StencilOp.Keep) return true;
        return false;
    }

    // Blended, writing depth and discarding nothing: its texels that draw nothing still hide what comes after.
    private static bool Unseen(Material material)
    {
        if (!Blended(material) || !material.HasProperty(Cutoff) || material.GetFloat(Cutoff) > 0f) return false;
        if (material.HasProperty(ZWrite) && material.GetFloat(ZWrite) == 0f) return false;
        foreach (var (on, byAlpha) in ClearLights)
            if (material.HasProperty(on) && material.GetFloat(on) != 0f && material.HasProperty(byAlpha) && material.GetFloat(byAlpha) == 0f) return false;
        return true;
    }
}
