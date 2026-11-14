using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Whether a scene's baked indirect light (Adaptive Probe Volume) is whole: every cell its baking set lists for the
/// scene is in each data file's table of cells, and each file is long enough for them. A bake that saved the set and
/// then could not write its cell files (the rebuild of 2026-11-14, "Win32 IO returned 1224") left chapter 2 with a set
/// naming cells its files did not have. The render pipeline then threw every frame ("KeyNotFoundException: The given
/// key '2' was not present in the dictionary", ProbeVolumeBakingSet.LoadStreambleAssetData), and the chapter could not
/// be played. The map builder checks its bake with this and puts the data from before back when it is not whole
/// (GreyboxBuilder.BakeWrites); in play, ProbeLightGuard switches data that still throws off.
/// The set's tables are internal to SRP Core, so they are read by name. When they can't be read (another SRP version),
/// the check finds nothing wrong rather than switch the light off.
/// </summary>
public static class ProbeDataCheck
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>"" when the scene's probe data is whole (or can't be judged); else what is wrong, in a few words.</summary>
    public static string Problem(ProbeVolumeBakingSet set, string sceneGuid)
    {
        if (set == null) return "";
        try
        {
            var cells = CellsOf(set, sceneGuid);
            if (cells == null || cells.Count == 0) return "";
            var files = new List<(string name, object asset)>
            {
                ("shared", Read(set, "cellSharedDataAsset")),
                ("bricks", Read(set, "cellBricksDataAsset")),
                ("support", Read(set, "cellSupportDataAsset"))
            };
            if (Read(set, "scenarios") is IDictionary scenarios)
                foreach (DictionaryEntry scenario in scenarios)
                {
                    files.Add((scenario.Key + " data", Read(scenario.Value, "cellDataAsset")));
                    files.Add((scenario.Key + " L2", Read(scenario.Value, "cellOptionalDataAsset")));
                }
            foreach (var (name, asset) in files)
            {
                string why = Whole(asset, cells);
                if (why.Length > 0) return name + ": " + why;
            }
            return "";
        }
        catch (System.Exception)
        {
            return "";
        }
    }

    /// <summary>The scene's GUID as its probe volume scene data keeps it ("" when it can't be read).</summary>
    public static string SceneGuidOf(ProbeVolumePerSceneData data)
        => data != null && Read(data, "sceneGUID") is string guid ? guid : "";

    // The cells the set lists for this scene, or all its cells when it lists none for it.
    private static ICollection CellsOf(ProbeVolumeBakingSet set, string sceneGuid)
    {
        if (!string.IsNullOrEmpty(sceneGuid) && Read(set, "perSceneCellLists") is IDictionary perScene && perScene.Contains(sceneGuid)
            && perScene[sceneGuid] is ICollection listed && listed.Count > 0)
            return listed;
        return Read(set, "cellDescs") is IDictionary all ? all.Keys : null;
    }

    // "" when one data file has every cell and is long enough for them; an asset not baked (no GUID) or with no table is
    // not judged.
    private static string Whole(object asset, ICollection cells)
    {
        if (asset == null || !(Read(asset, "assetGUID") is string guid) || guid.Length == 0) return "";
        if (!(Read(asset, "streamableCellDescs") is IDictionary table) || table.Count == 0) return "";
        int elementSize = Read(asset, "elementSize") is int size ? size : 0;
        long need = 0;
        foreach (object cell in cells)
        {
            if (!table.Contains(cell)) return "cell " + cell + " is not in its table";
            object desc = table[cell];
            if (Read(desc, "offset") is int offset && Read(desc, "elementCount") is int count)
                need = System.Math.Max(need, offset + (long)count * elementSize);
        }
        long have = LengthOf(asset);
        return have >= 0 && have < need ? "the file holds " + have + " bytes, its cells need " + need : "";
    }

    // The length of the data the pipeline reads: the file (the editor reads the project's file, a build its streaming
    // copy), else the text asset a build carries instead; -1 when neither can be had.
    private static long LengthOf(object asset)
    {
        try
        {
            var size = asset.GetType().GetMethod("GetFileSize", Members);
            if (size != null && size.Invoke(asset, null) is long length) return length;
        }
        catch (System.Exception) { }
        return Read(asset, "asset") is TextAsset text && text != null ? text.dataSize : -1;
    }

    // A field or property of an object by name (null when it has none).
    private static object Read(object owner, string name)
    {
        if (owner == null) return null;
        var type = owner.GetType();
        var field = type.GetField(name, Members);
        if (field != null) return field.GetValue(owner);
        var property = type.GetProperty(name, Members);
        return property != null && property.GetIndexParameters().Length == 0 ? property.GetValue(owner) : null;
    }
}
