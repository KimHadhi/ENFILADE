using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The VRChat parts of a BOOTH avatar's or outfit's prefab, read from the prefab's own text. VRChat's components arrive
/// without their scripts (the VRChat SDK runs only in VRChat's own Unity 2022), so Unity can't show their values, but
/// the file still holds every one. PhysBones (what sways and how) and their colliders are read here for VrcSway to turn
/// into this game's sway; the references in them are resolved against the prefab's loaded objects. An object of a model
/// nested in the prefab (an FBX instance, as most BOOTH outfits are) is written in the file under its source's id XOR
/// its instance's id, which is how it is found here too. A PhysBone its maker switched off is skipped, as VRChat runs
/// none: Mayo's right breast carries a second, older one beside the one in use, and whichever the file listed first
/// used to set the sway.
/// </summary>
internal sealed class VrcPrefab
{
    // VRChat SDK 3's dynamics, by the DLL's GUID and each class's file id.
    private const string DynamicsGuid = "2a2c05204084d904aa4945ccff20d8e5";
    private const long PhysBoneScript = 1661641543, PhysBoneColliderScript = -1631200402;

    /// <summary>One VRCPhysBone's settings, as VRChat's documentation names them.</summary>
    internal sealed class PhysBone
    {
        public Transform Root;
        public readonly List<Transform> Ignore = new List<Transform>();
        /// <summary>The bones its colliders hang from.</summary>
        public readonly List<Transform> Colliders = new List<Transform>();
        public Vector3 Endpoint;
        public int Integration, LimitType;
        public float Pull, Spring, Stiffness, Gravity, GravityFalloff, Immobile, Radius, MaxAngleX, MaxAngleZ;
        /// <summary>
        /// How each value runs down the chain, root (0) to far end (1), as a share of it: VRChat's curve beside each
        /// value. Null where the maker left it the same all the way.
        /// </summary>
        public AnimationCurve PullCurve, SpringCurve, StiffnessCurve, GravityCurve, ImmobileCurve, RadiusCurve, MaxAngleXCurve, MaxAngleZCurve;
        public bool Collides;

        /// <summary>A value at a depth of the chain (0 root, 1 far end), its curve applied.</summary>
        public static float At(float value, AnimationCurve curve, float depth) => curve != null ? value * curve.Evaluate(depth) : value;
    }

    private static readonly Regex Header = new Regex(@"^--- !u!(-?\d+) &(-?\d+)[^\r\n]*", RegexOptions.Multiline);
    private static readonly Regex Script = new Regex(@"m_Script: \{fileID: (-?\d+), guid: ([0-9a-f]{32})");

    public readonly List<PhysBone> PhysBones = new List<PhysBone>();
    private readonly Dictionary<long, Object> objects = new Dictionary<long, Object>();
    private readonly Dictionary<long, string> documents = new Dictionary<long, string>();

    /// <summary>The prefab at <paramref name="path"/>; it has no PhysBones once BoothImport has taken VRChat's parts off.</summary>
    internal static VrcPrefab Read(string path)
    {
        var prefab = new VrcPrefab();
        string text = File.ReadAllText(path);
        var headers = Header.Matches(text);
        for (int i = 0; i < headers.Count; i++)
        {
            int start = headers[i].Index + headers[i].Length;
            int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            if (long.TryParse(headers[i].Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                prefab.documents[id] = text.Substring(start, end - start);
        }
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset == null) continue;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long local)) prefab.objects.TryAdd(local, asset);
            // A nested model's objects: source id XOR instance id.
            var source = PrefabUtility.GetCorrespondingObjectFromSource(asset);
            var instance = PrefabUtility.GetPrefabInstanceHandle(asset);
            if (source != null && instance != null
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long sourceId)
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(instance, out string _, out long instanceId))
                prefab.objects.TryAdd(sourceId ^ instanceId, asset);
        }
        foreach (var document in prefab.documents.Values)
            if (IsScript(document, PhysBoneScript)) prefab.ReadPhysBone(document);
        return prefab;
    }

    private void ReadPhysBone(string document)
    {
        if (Regex.IsMatch(document, @"^  m_Enabled: 0\b", RegexOptions.Multiline)) return;
        var owner = Find<GameObject>(Reference(document, "m_GameObject"));
        var bone = new PhysBone
        {
            Root = Find<Transform>(Reference(document, "rootTransform")).OrNull() ?? (owner != null ? owner.transform : null),
            Endpoint = Vector(document, "endpointPosition"),
            Integration = (int)Number(document, "integrationType"),
            LimitType = (int)Number(document, "limitType"),
            Pull = Number(document, "pull"),
            Spring = Number(document, "spring"),
            Stiffness = Number(document, "stiffness"),
            Gravity = Number(document, "gravity"),
            GravityFalloff = Number(document, "gravityFalloff"),
            Immobile = Number(document, "immobile"),
            Radius = Number(document, "radius"),
            MaxAngleX = Number(document, "maxAngleX"),
            MaxAngleZ = Number(document, "maxAngleZ"),
            PullCurve = Curve(document, "pullCurve"),
            SpringCurve = Curve(document, "springCurve"),
            StiffnessCurve = Curve(document, "stiffnessCurve"),
            GravityCurve = Curve(document, "gravityCurve"),
            ImmobileCurve = Curve(document, "immobileCurve"),
            RadiusCurve = Curve(document, "radiusCurve"),
            MaxAngleXCurve = Curve(document, "maxAngleXCurve"),
            MaxAngleZCurve = Curve(document, "maxAngleZCurve"),
            Collides = Number(document, "allowCollision") != 0f,
        };
        if (bone.Root == null) return;
        foreach (long id in References(document, "ignoreTransforms"))
            if (Find<Transform>(id) is Transform ignored) bone.Ignore.Add(ignored);
        foreach (long id in References(document, "colliders"))
        {
            if (!documents.TryGetValue(id, out var collider) || !IsScript(collider, PhysBoneColliderScript)) continue;
            var on = Find<Transform>(Reference(collider, "rootTransform")).OrNull() ?? Find<Transform>(Reference(collider, "m_GameObject"));
            if (on != null) bone.Colliders.Add(on);
        }
        PhysBones.Add(bone);
    }

    private T Find<T>(long id) where T : Object
    {
        if (id == 0 || !objects.TryGetValue(id, out var found) || found == null) return null;
        if (found is T exact) return exact;
        if (typeof(T) == typeof(Transform))
            return (found is GameObject go ? go.transform : found is Component part ? part.transform : null) as T;
        if (typeof(T) == typeof(GameObject)) return (found is Component component ? component.gameObject : null) as T;
        return null;
    }

    private static bool IsScript(string document, long script)
    {
        var match = Script.Match(document);
        return match.Success && match.Groups[2].Value == DynamicsGuid
            && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id == script;
    }

    private static float Number(string document, string field)
    {
        var match = Regex.Match(document, @"^  " + field + @": ([^\r\n{]+)", RegexOptions.Multiline);
        return match.Success && TableCells.TryNumber(match.Groups[1].Value.Trim(), out float value)
            ? value : 0f;
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

    // An AnimationCurve field as Unity writes it: its keys, tangents and weights, so it evaluates exactly as VRChat's
    // own. Null when it has no keys (the maker drew none).
    private static AnimationCurve Curve(string document, string field)
    {
        var block = Regex.Match(document, @"^  " + field + @":[ \t]*\r?\n    serializedVersion: \d+\r?\n    m_Curve:(.*?)\r?\n    m_PreInfinity",
            RegexOptions.Multiline | RegexOptions.Singleline);
        if (!block.Success) return null;
        var keys = new List<Keyframe>();
        foreach (Match key in Regex.Matches(block.Groups[1].Value,
                     @"time: (\S+)\s+value: (\S+)\s+inSlope: (\S+)\s+outSlope: (\S+)\s+tangentMode: \S+\s+weightedMode: (\S+)\s+inWeight: (\S+)\s+outWeight: (\S+)"))
        {
            float Part(int group) => TableCells.TryNumber(key.Groups[group].Value, out float v) ? v : 0f;
            keys.Add(new Keyframe(Part(1), Part(2), Part(3), Part(4), Part(6), Part(7)) { weightedMode = (WeightedMode)(int)Part(5) });
        }
        return keys.Count > 0 ? new AnimationCurve(keys.ToArray()) : null;
    }

    private static Vector3 Vector(string document, string field)
    {
        var match = Regex.Match(document, @"^  " + field + @": \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}", RegexOptions.Multiline);
        if (!match.Success) return Vector3.zero;
        float Axis(int group) => TableCells.TryNumber(match.Groups[group].Value.Trim(), out float v) ? v : 0f;
        return new Vector3(Axis(1), Axis(2), Axis(3));
    }
}
