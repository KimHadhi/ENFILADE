using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Map dressing, levels of detail (the user, 2026-11-13: "LOD 진행"): a model the builder places stops being drawn,
/// and stops casting its shadow, once it shows less than a share of the screen's height (Tables/MapLods, the scene's
/// row or Default). Most bought props carry LODs of their own (the airport and heist packs' LODGroups, the Dekogon and
/// Megascans models' _LOD1.. meshes, which Unity groups on import): their levels stay as their makers set them and
/// only the last one, where the model goes, is set to the table's share, so no pack culls a model early (an imported
/// group's defaults can) or never (a last level of 0). A model without LODs (the MOUT pieces, the cars, the trash and
/// rubble packs; none heavier than 17k triangles, so a coarser mesh would save little) gets a group of one level that
/// only culls. A LODGroup measures the model's own size, so a can goes at tens of metres and a truck at hundreds, and
/// the quality's LOD Bias (PC 2) doubles those distances. Static batching and the occlusion bake keep working, since a
/// LODGroup only decides per camera which of its renderers draw. Culling is only what one camera draws: the models'
/// collision boxes stay, so play and every PC's world are the same.
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string LodTable = "MapLods";
    // The share when the table has neither this scene's row nor Default, and the range a row may set.
    private const float LodCullFallback = .01f, LodCullLeast = .0005f, LodCullMost = .2f;
    // A pack's last level may already sit under the share; the culled level then goes this far under the level before it.
    private const float LodUnderLast = .5f;

    private float lodCull = -1f;
    private int lodsTuned, lodsAdded;

    // The screen share under which a placed model is not drawn: this scene's row of Tables/MapLods, else Default.
    private float LodCull()
    {
        if (lodCull >= 0f) return lodCull;
        if (!GameTables.TryGetRow(LodTable, sceneName, out var row)) GameTables.TryGetRow(LodTable, "Default", out row);
        lodCull = Mathf.Clamp(row != null ? Number(row, "cull", LodCullFallback) : LodCullFallback, LodCullLeast, LodCullMost);
        return lodCull;
    }

    // A placed copy's levels of detail (see the class): its own groups keep their levels and get the table's last one;
    // renderers no group holds go into a new single-level group on the copy, unless the copy already has a group of its
    // own (a GameObject holds one LODGroup), where they stay as they were.
    private void FitLods(GameObject copy)
    {
        float cull = LodCull();
        var held = new HashSet<Renderer>();
        foreach (var group in copy.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = group.GetLODs();
            if (lods.Length == 0) continue;
            foreach (var lod in lods)
                if (lod.renderers != null)
                    foreach (var renderer in lod.renderers)
                        if (renderer != null) held.Add(renderer);
            int last = lods.Length - 1;
            float before = last > 0 ? lods[last - 1].screenRelativeTransitionHeight : 1f;
            float want = Mathf.Min(cull, before * LodUnderLast);
            if (Mathf.Approximately(lods[last].screenRelativeTransitionHeight, want)) continue;
            lods[last].screenRelativeTransitionHeight = want;
            group.SetLODs(lods);
            lodsTuned++;
        }
        var loose = new List<Renderer>();
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            if (!held.Contains(renderer)) loose.Add(renderer);
        if (loose.Count == 0 || copy.TryGetComponent(out LODGroup _)) return;
        var added = copy.AddComponent<LODGroup>();
        added.fadeMode = LODFadeMode.None;
        added.SetLODs(new[] { new LOD(cull, loose.ToArray()) });
        added.RecalculateBounds();
        lodsAdded++;
    }

    // For the build's report.
    private string LodReport()
        => lodsTuned + lodsAdded == 0 ? ""
            : "\nLOD(Tables/MapLods): 화면 높이의 " + (LodCull() * 100f).ToString("0.##") + "%보다 작으면 안 그림. 팩의 LOD " + lodsTuned
              + "개는 마지막 단계만 맞추고, LOD가 없던 모델 " + lodsAdded + "개에 한 단계 LOD를 달았어요.";
}
