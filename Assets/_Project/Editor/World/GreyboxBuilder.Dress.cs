using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Map dressing, models (the user's World War Z look, 2026-11-12): the bought packs' models (Airport Terminal Scene,
/// Heist Essentials) on the grey layout. The layout's boxes keep the collision and the zombies' walk mesh; a model is
/// only seen: its colliders, lights, sounds, rigid bodies and scripts are taken off, and it stays out of the walk-mesh
/// bake and the small-prop check, so dressing can never open a hole in a wall or a perch a zombie can't reach.
/// Tables/MapArt names each kind's prefabs (under Assets/, ';' between variants) and how one is put on a box:
/// "tile" repeats the model at about its own size along the box's long side (a row of seats, a check-in counter, a
/// shelf), "stack" does the same and piles rows up to the box's top (a sandbag wall), "fit" scales one copy evenly
/// into the box (a scanner, a desk, a car), "stretch" fills the box on every axis, "native" stands one copy at its own
/// size on the box's floor. A dressed box hides its grey look. Props (Prop, Hang)
/// stand where the layout says with no box: walked through when small (a bag, a suitcase); PropSolid gives a bigger
/// one (a vending machine, a trolley) an unseen box of its own size that blocks and goes into the bake. The model's
/// long side is turned along the box's long side; the table's yaw turns a model whose front faces the wrong way. A
/// variant is picked by the place (the same on every build), or by number. A kind without a row or a prefab that
/// isn't there leaves the grey box as it was and is named once in the build's report.
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string ArtTable = "MapArt";
    // A collision box whose top would fall in the band a zombie's arm can't reach and no walk slab covers (small props,
    // memo 48) is raised past it.
    private const float SolidBandLow = 1.5f, SolidBandTop = 2.05f;

    private enum ArtFit { Native, Fit, Stretch, Tile, Stack }
    // Stacked rows sink into the row below by this share of their height, as filled bags do.
    private const float StackSettle = .08f;

    private Transform dressing;
    private readonly Dictionary<string, GameObject> artPrefabs = new Dictionary<string, GameObject>();
    private readonly Dictionary<GameObject, Bounds> artBounds = new Dictionary<GameObject, Bounds>();
    private readonly HashSet<string> artMissing = new HashSet<string>();
    private int artPlaced;

    /// <summary>Dresses a box of the layout with a model of <paramref name="kind"/> (Tables/MapArt) and returns the box.</summary>
    public GameObject Dress(GameObject box, string kind, int pick = -1)
    {
        if (box == null) return null;
        var center = box.transform.position;
        if (!ArtRow(kind, pick, center, out var prefab, out var fit, out float yaw, out float scale)) return box;
        var target = new Bounds(center, box.transform.lossyScale);
        var size = BoundsOf(prefab).size;
        bool boxAlongX = target.size.x >= target.size.z, modelAlongX = size.x >= size.z;
        float turn = (boxAlongX == modelAlongX ? 0f : 90f) + yaw;
        Vector3 turned = Turned(size, turn);
        float floor = target.min.y;
        switch (fit)
        {
            case ArtFit.Tile:
            case ArtFit.Stack:
            {
                float length = boxAlongX ? target.size.x : target.size.z, own = Mathf.Max(.01f, boxAlongX ? turned.x : turned.z);
                int count = Mathf.Max(1, Mathf.RoundToInt(length / (own * scale)));
                float s = length / count / own;
                // A stack climbs in rows of the piece's settled height until the box's top; every other row is turned
                // half round so the same piece doesn't line up into stripes.
                float rise = Mathf.Max(.01f, turned.y * s * (1f - StackSettle));
                int rows = fit == ArtFit.Stack ? Mathf.Max(1, Mathf.RoundToInt(target.size.y / rise)) : 1;
                Vector3 along = boxAlongX ? Vector3.right : Vector3.forward;
                for (int row = 0; row < rows; row++)
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 at = center + along * ((i + .5f) * length / count - length * .5f);
                        Place(prefab, new Vector3(at.x, floor + row * rise, at.z), turn + row % 2 * 180f, Vector3.one * s, false);
                    }
                break;
            }
            case ArtFit.Fit:
            {
                float s = Mathf.Min(target.size.x / Mathf.Max(.01f, turned.x), target.size.y / Mathf.Max(.01f, turned.y),
                    target.size.z / Mathf.Max(.01f, turned.z)) * scale;
                Place(prefab, new Vector3(center.x, floor, center.z), turn, Vector3.one * s, false);
                break;
            }
            case ArtFit.Stretch:
            {
                // Scale in the model's own axes: a quarter turn swaps x and z.
                bool swapped = Mathf.Abs(Mathf.DeltaAngle(turn, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(turn, 270f)) < 1f;
                Vector3 want = new Vector3(target.size.x / Mathf.Max(.01f, turned.x), target.size.y / Mathf.Max(.01f, turned.y),
                    target.size.z / Mathf.Max(.01f, turned.z));
                Place(prefab, new Vector3(center.x, floor, center.z), turn, swapped ? new Vector3(want.z, want.y, want.x) : want, false);
                break;
            }
            default:
                Place(prefab, new Vector3(center.x, floor, center.z), turn, Vector3.one * scale, false);
                break;
        }
        if (box.TryGetComponent(out Renderer look)) look.enabled = false;
        return box;
    }

    /// <summary>A model of <paramref name="kind"/> standing on <paramref name="floor"/>, turned to <paramref name="yaw"/>
    /// (degrees, plus the table's), seen only: small things (a bag, a suitcase) are walked through.</summary>
    public GameObject Prop(string kind, Vector3 floor, float yaw = 0f, int pick = -1, float scale = 1f)
    {
        if (!ArtRow(kind, pick, floor, out var prefab, out _, out float turn, out float own)) return null;
        return Place(prefab, floor, yaw + turn, Vector3.one * (own * scale), false);
    }

    /// <summary>A model of <paramref name="kind"/> hanging with its top at <paramref name="top"/> (a ceiling lamp, a sign).</summary>
    public GameObject Hang(string kind, Vector3 top, float yaw = 0f, int pick = -1, float scale = 1f)
    {
        if (!ArtRow(kind, pick, top, out var prefab, out _, out float turn, out float own)) return null;
        return Place(prefab, top, yaw + turn, Vector3.one * (own * scale), true);
    }

    /// <summary>
    /// A bigger prop (a vending machine, a trolley) that blocks: the model, and under the terrain an unseen box of the
    /// model's own footprint and height (raised past the band no zombie reaches, see SolidBandTop), which goes into the
    /// walk-mesh bake like any box of the layout.
    /// </summary>
    public GameObject PropSolid(string kind, Vector3 floor, float yaw = 0f, int pick = -1, float scale = 1f)
    {
        var model = Prop(kind, floor, yaw, pick, scale);
        if (model == null) return null;
        var bounds = WorldBounds(model);
        float top = bounds.max.y - floor.y;
        if (top > SolidBandLow && top < SolidBandTop) top = SolidBandTop;
        var box = Box(kind + " (충돌)", bounds.min.x, bounds.max.x, floor.y, floor.y + top, bounds.min.z, bounds.max.z);
        box.GetComponent<Renderer>().enabled = false;
        return model;
    }

    // The table's row for a kind and the variant for this place. False (and named once in the report) when missing.
    private bool ArtRow(string kind, int pick, Vector3 place, out GameObject prefab, out ArtFit fit, out float yaw, out float scale)
    {
        prefab = null; fit = ArtFit.Native; yaw = 0f; scale = 1f;
        if (!GameTables.TryGetRow(ArtTable, kind, out var row)) { artMissing.Add(kind); return false; }
        var paths = Text(row, "prefabs").Split(';');
        int index = pick >= 0 ? pick : Mathf.Abs(Mathf.RoundToInt(place.x * 7.3f) * 31 + Mathf.RoundToInt(place.z * 5.1f) * 17);
        string path = "Assets/" + paths[index % paths.Length].Trim();
        if (!artPrefabs.TryGetValue(path, out prefab))
            artPrefabs[path] = prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { artMissing.Add(kind + " (" + path + ")"); return false; }
        switch (Text(row, "fit").ToLowerInvariant())
        {
            case "tile": fit = ArtFit.Tile; break;
            case "stack": fit = ArtFit.Stack; break;
            case "fit": fit = ArtFit.Fit; break;
            case "stretch": fit = ArtFit.Stretch; break;
            default: fit = ArtFit.Native; break;
        }
        yaw = Number(row, "yaw", 0f);
        scale = Number(row, "scale", 1f);
        return true;
    }

    // One copy: its bounds' bottom (or top) centre on the point, turned, scaled over the prefab's own scale, stripped
    // to a look, and static like the rest of the map.
    private GameObject Place(GameObject prefab, Vector3 point, float yaw, Vector3 scale, bool hang)
    {
        dressing ??= Root("◤━━ 꾸밈 (Tables/MapArt, 보이기만 함)");
        var bounds = BoundsOf(prefab);
        var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        copy.transform.SetParent(dressing, true);
        var turn = Quaternion.Euler(0f, yaw, 0f);
        // The prefab's own rotation stays under the turn (models often stand on a -90° X from their exporter).
        Quaternion own = copy.transform.rotation;
        Vector3 scaled = Vector3.Scale(copy.transform.localScale, scale);
        // Bounds were measured at the prefab's own rotation and scale with its pivot at the origin.
        Vector3 c = Vector3.Scale(bounds.center, scale), e = Vector3.Scale(bounds.extents, scale);
        Vector3 offset = turn * c;
        float y = hang ? point.y - c.y - e.y : point.y - c.y + e.y;
        copy.transform.SetPositionAndRotation(new Vector3(point.x - offset.x, y, point.z - offset.z), turn * own);
        copy.transform.localScale = scaled;
        Strip(copy);
        artPlaced++;
        return copy;
    }

    // What a model needs only to be seen: no colliders (the layout's boxes collide), lights (the map has its own),
    // physics, sound or scripts.
    private static void Strip(GameObject copy)
    {
        foreach (var part in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(part);
        for (int pass = 0; pass < 2; pass++)
            foreach (var part in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (part != null) Object.DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(part);
    }

    // The prefab's renderers' bounds at its own pivot, rotation and scale (measured once on a throwaway copy).
    private Bounds BoundsOf(GameObject prefab)
    {
        if (artBounds.TryGetValue(prefab, out var known)) return known;
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        probe.transform.position = Vector3.zero;
        var bounds = WorldBounds(probe);
        Object.DestroyImmediate(probe);
        return artBounds[prefab] = bounds;
    }

    private static Bounds WorldBounds(GameObject root)
    {
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (any) bounds.Encapsulate(renderer.bounds);
            else { bounds = renderer.bounds; any = true; }
        }
        return bounds;
    }

    // A size seen after a turn about Y (axis-aligned extents of the turned box).
    private static Vector3 Turned(Vector3 size, float yaw)
    {
        float r = yaw * Mathf.Deg2Rad, c = Mathf.Abs(Mathf.Cos(r)), s = Mathf.Abs(Mathf.Sin(r));
        return new Vector3(size.x * c + size.z * s, size.y, size.x * s + size.z * c);
    }

    // For the build's report.
    private string DressReport()
    {
        if (artPlaced == 0 && artMissing.Count == 0) return "";
        string text = "\n꾸밈: 모델 " + artPlaced + "개";
        if (artMissing.Count > 0) text += " (못 찾은 종류: " + string.Join(", ", artMissing) + ")";
        return text;
    }
}
