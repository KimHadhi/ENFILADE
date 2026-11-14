using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// A prefab's VRChat PhysBones read from the prefab's own text, for a project that hasn't the VRChat SDK: there the
/// components arrive without their scripts and Unity shows none of their values, but the file still holds every one.
/// (In a project with the SDK the exporter reads the live components instead: ModAvatarExporter.) Each bone a PhysBone
/// names is given as its path from the prefab's root, as the avatar's file wants it (ModAvatarFile.PhysBone). An object
/// of a model nested in the prefab (an FBX instance, as most avatars are) is written in the file under its source's id
/// XOR its instance's id, which is how it is found here. A PhysBone its maker switched off is skipped, as VRChat runs none.
/// Part of the exporter's package: it uses nothing of the game's.
/// </summary>
internal static class ModVrcYaml
{
    // VRChat SDK 3's dynamics, by the DLL's GUID and each class's file id.
    private const string DynamicsGuid = "2a2c05204084d904aa4945ccff20d8e5";
    private const long PhysBoneScript = 1661641543, PhysBoneColliderScript = -1631200402;

    private static readonly Regex Header = new Regex(@"^--- !u!(-?\d+) &(-?\d+)[^\r\n]*", RegexOptions.Multiline);
    private static readonly Regex Script = new Regex(@"m_Script: \{fileID: (-?\d+), guid: ([0-9a-f]{32})");

    /// <summary>The PhysBones in the prefab file at <paramref name="path"/>; none when it has none or can't be read.</summary>
    internal static List<ModAvatarFile.PhysBone> Read(string path)
    {
        var found = new List<ModAvatarFile.PhysBone>();
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (root == null || !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return found;
        string text = File.ReadAllText(path);
        var documents = new Dictionary<long, string>();
        var headers = Header.Matches(text);
        for (int i = 0; i < headers.Count; i++)
        {
            int start = headers[i].Index + headers[i].Length;
            int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            if (long.TryParse(headers[i].Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                documents[id] = text.Substring(start, end - start);
        }
        var objects = new Dictionary<long, Object>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset == null) continue;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long local) && !objects.ContainsKey(local)) objects[local] = asset;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(asset);
            var instance = PrefabUtility.GetPrefabInstanceHandle(asset);
            if (source != null && instance != null
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long sourceId)
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(instance, out string _, out long instanceId)
                && !objects.ContainsKey(sourceId ^ instanceId))
                objects[sourceId ^ instanceId] = asset;
        }
        Transform Find(long id)
        {
            if (id == 0 || !objects.TryGetValue(id, out var at) || at == null) return null;
            return at is Transform transform ? transform : at is GameObject go ? go.transform : at is Component part ? part.transform : null;
        }
        foreach (var document in documents.Values)
        {
            if (!IsScript(document, PhysBoneScript) || Regex.IsMatch(document, @"^  m_Enabled: 0\b", RegexOptions.Multiline)) continue;
            var on = Find(Reference(document, "rootTransform"));
            if (on == null) on = Find(Reference(document, "m_GameObject"));
            if (on == null || !Under(on, root.transform)) continue;
            var bone = new ModAvatarFile.PhysBone
            {
                root = PathOf(on, root.transform),
                endpoint = Vector(document, "endpointPosition"),
                integration = (int)Number(document, "integrationType"),
                limitType = (int)Number(document, "limitType"),
                pull = Number(document, "pull"),
                spring = Number(document, "spring"),
                stiffness = Number(document, "stiffness"),
                gravity = Number(document, "gravity"),
                gravityFalloff = Number(document, "gravityFalloff"),
                immobile = Number(document, "immobile"),
                radius = Number(document, "radius"),
                maxAngleX = Number(document, "maxAngleX"),
                maxAngleZ = Number(document, "maxAngleZ"),
                pullCurve = Curve(document, "pullCurve"),
                springCurve = Curve(document, "springCurve"),
                stiffnessCurve = Curve(document, "stiffnessCurve"),
                gravityCurve = Curve(document, "gravityCurve"),
                immobileCurve = Curve(document, "immobileCurve"),
                radiusCurve = Curve(document, "radiusCurve"),
                maxAngleXCurve = Curve(document, "maxAngleXCurve"),
                maxAngleZCurve = Curve(document, "maxAngleZCurve"),
                collides = Number(document, "allowCollision") != 0f,
            };
            var ignore = new List<string>();
            foreach (long id in References(document, "ignoreTransforms"))
                if (Find(id) is Transform ignored && Under(ignored, root.transform)) ignore.Add(PathOf(ignored, root.transform));
            var colliders = new List<string>();
            foreach (long id in References(document, "colliders"))
            {
                if (!documents.TryGetValue(id, out var collider) || !IsScript(collider, PhysBoneColliderScript)) continue;
                var stands = Find(Reference(collider, "rootTransform"));
                if (stands == null) stands = Find(Reference(collider, "m_GameObject"));
                if (stands != null && Under(stands, root.transform)) colliders.Add(PathOf(stands, root.transform));
            }
            bone.ignore = ignore.ToArray();
            bone.colliders = colliders.ToArray();
            found.Add(bone);
        }
        return found;
    }

    /// <summary>A bone's path from the root ("" for the root itself), as Transform.Find takes it.</summary>
    internal static string PathOf(Transform bone, Transform root)
    {
        var names = new List<string>();
        for (var at = bone; at != null && at != root; at = at.parent) names.Insert(0, at.name);
        return string.Join("/", names);
    }

    internal static bool Under(Transform bone, Transform root)
    {
        for (var at = bone; at != null; at = at.parent)
            if (at == root) return true;
        return false;
    }

    private static bool IsScript(string document, long script)
    {
        var match = Script.Match(document);
        return match.Success && match.Groups[2].Value == DynamicsGuid
            && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id == script;
    }

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

    private static float Number(string document, string field)
    {
        var match = Regex.Match(document, @"^  " + field + @": ([^\r\n{]+)", RegexOptions.Multiline);
        return match.Success && TryNumber(match.Groups[1].Value, out float value) ? value : 0f;
    }

    private static long Reference(string document, string field)
    {
        var match = Regex.Match(document, @"^  " + field + @": \{fileID: (-?\d+)", RegexOptions.Multiline);
        return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0;
    }

    private static List<long> References(string document, string field)
    {
        var found = new List<long>();
        var list = Regex.Match(document, @"^  " + field + @":[ \t]*\r?\n((?:  - [^\r\n]*\r?\n)*)", RegexOptions.Multiline);
        if (!list.Success) return found;
        foreach (Match item in Regex.Matches(list.Groups[1].Value, @"fileID: (-?\d+)"))
            if (long.TryParse(item.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id != 0) found.Add(id);
        return found;
    }

    // An AnimationCurve field as Unity writes it: its keys with their tangents and weights. No keys where the maker drew none.
    private static ModAvatarFile.Curve Curve(string document, string field)
    {
        var curve = new ModAvatarFile.Curve();
        var block = Regex.Match(document, @"^  " + field + @":[ \t]*\r?\n    serializedVersion: \d+\r?\n    m_Curve:(.*?)\r?\n    m_PreInfinity",
            RegexOptions.Multiline | RegexOptions.Singleline);
        if (!block.Success) return curve;
        var keys = new List<ModAvatarFile.Key>();
        foreach (Match key in Regex.Matches(block.Groups[1].Value,
                     @"time: (\S+)\s+value: (\S+)\s+inSlope: (\S+)\s+outSlope: (\S+)\s+tangentMode: \S+\s+weightedMode: (\S+)\s+inWeight: (\S+)\s+outWeight: (\S+)"))
        {
            float Part(int group) => TryNumber(key.Groups[group].Value, out float v) ? v : 0f;
            keys.Add(new ModAvatarFile.Key
            {
                time = Part(1), value = Part(2), inSlope = Part(3), outSlope = Part(4), weighted = (int)Part(5), inWeight = Part(6), outWeight = Part(7)
            });
        }
        curve.keys = keys.ToArray();
        return curve;
    }

    private static Vector3 Vector(string document, string field)
    {
        var match = Regex.Match(document, @"^  " + field + @": \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}", RegexOptions.Multiline);
        if (!match.Success) return Vector3.zero;
        float Axis(int group) => TryNumber(match.Groups[group].Value, out float v) ? v : 0f;
        return new Vector3(Axis(1), Axis(2), Axis(3));
    }
}
