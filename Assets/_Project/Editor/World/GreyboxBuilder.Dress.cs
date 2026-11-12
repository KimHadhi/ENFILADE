using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    // memo 48) is raised past it, or brought down to its bottom when it is only this little over it (a car's roof:
    // standing 0.45 m over it looked like floating, standing 0.1 m into it doesn't show).
    private const float SolidBandLow = 1.5f, SolidBandTop = 2.05f, SolidBandSink = .15f;
    // The ridge over a top nobody should stand on: steeper than the 45° anyone walks, and its slabs' thickness (m).
    private const float CapSlope = 55f, CapThickness = .2f;

    private enum ArtFit { Native, Fit, Stretch, Tile, Stack }
    // Stacked rows sink into the row below by this share of their height, as filled bags do.
    private const float StackSettle = .08f;

    private Transform dressing;

    /// <summary>The model Dress, Prop, Hang or PropSolid put down last (of a row Dress lays, its last copy), for a
    /// builder that moves it on: the hub's shutter rides its door (HubBuilder), so it leaves the static dressing.</summary>
    public GameObject LastPlaced { get; private set; }
    private readonly Dictionary<string, GameObject> artPrefabs = new Dictionary<string, GameObject>();
    private readonly Dictionary<GameObject, Bounds> artBounds = new Dictionary<GameObject, Bounds>();
    private readonly HashSet<string> artMissing = new HashSet<string>();
    private int artPlaced;

    /// <summary>Dresses a box of the layout with a model of <paramref name="kind"/> (Tables/MapArt) and returns the box.</summary>
    public GameObject Dress(GameObject box, string kind, int pick = -1)
    {
        if (box == null) return null;
        // Cover meant to be shot over (Tables/ShootOverCover: sandbags, HESCO, jersey barriers…): the gun's wall rule looks
        // past its box while the player's eyes are over its top, model or no model.
        if (ShootOverCover.IsKind(kind) && !box.TryGetComponent<ShootOverCover>(out _)) box.AddComponent<ShootOverCover>();
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

    /// <summary>A model of <paramref name="kind"/> standing on <paramref name="floor"/>, turned to <paramref name="yaw"/>,
    /// scaled on each of its own axes (times the table's scale): a wall panel stretched to its bay's length keeps its
    /// height, a pillar raised to the roof keeps its width. Seen only, like Prop.</summary>
    public GameObject PropScaled(string kind, Vector3 floor, float yaw, Vector3 scale, int pick = -1)
    {
        if (!ArtRow(kind, pick, floor, out var prefab, out _, out float turn, out float own)) return null;
        return Place(prefab, floor, yaw + turn, scale * own, false);
    }

    /// <summary>The size of a model of <paramref name="kind"/> (variant <paramref name="pick"/>) as it stands unturned,
    /// times the table's scale: its own x, height and depth, for a builder fitting copies to a wall or a lane. Zero when
    /// the kind has no row or model (named in the report like any missing kind).</summary>
    public Vector3 ArtSize(string kind, int pick = 0)
    {
        if (!ArtRow(kind, Mathf.Max(0, pick), Vector3.zero, out var prefab, out _, out float yaw, out float scale)) return Vector3.zero;
        return Turned(BoundsOf(prefab).size, yaw) * scale;
    }

    /// <summary>
    /// Whole copies of <paramref name="kind"/> from <paramref name="from"/> to <paramref name="to"/> (floor points), turned to
    /// <paramref name="yaw"/>, each stretched along the line only (its own x or z, whichever the line runs along) so the run
    /// ends where it should: a row of wall panels, a beam across a hall, a floor stripe. <paramref name="shift"/> moves them
    /// all off the line (a wall piece's depth, so its front sits on the wall's face). Seen only, like Prop.
    /// </summary>
    public void Line(string kind, Vector3 from, Vector3 to, float yaw, Vector3 shift = default)
    {
        Vector3 size = ArtSize(kind), line = to - from;
        float length = line.magnitude;
        if (size == Vector3.zero || length < .01f) return;
        Vector3 dir = line / length;
        bool alongX = Mathf.Abs(Vector3.Dot(Quaternion.Euler(0f, yaw, 0f) * Vector3.right, dir)) > .7f;
        float own = Mathf.Max(.01f, alongX ? size.x : size.z);
        int count = Mathf.Max(1, Mathf.RoundToInt(length / own));
        float stretch = length / count / own;
        var scale = alongX ? new Vector3(stretch, 1f, 1f) : new Vector3(1f, 1f, stretch);
        for (int i = 0; i < count; i++)
            PropScaled(kind, from + dir * ((i + .5f) * length / count) + shift, yaw, scale);
    }

    /// <summary>A model of <paramref name="kind"/> hung on a wall: its middle at <paramref name="middle"/>'s height, its back
    /// on the wall's face there, its front (the kit's +Z) turned to <paramref name="face"/>. Seen only, like Prop.</summary>
    public GameObject Mount(string kind, Vector3 middle, float face, float scale = 1f, int pick = -1)
    {
        Vector3 size = ArtSize(kind, Mathf.Max(0, pick)) * scale;
        if (size == Vector3.zero) return null;
        Vector3 front = Quaternion.Euler(0f, face, 0f) * Vector3.forward;
        return Prop(kind, middle + front * (size.z * .5f + .005f) - Vector3.up * (size.y * .5f), face, pick, scale);
    }

    /// <summary>A model of <paramref name="kind"/> hanging with its top at <paramref name="top"/> (a ceiling lamp, a sign).</summary>
    public GameObject Hang(string kind, Vector3 top, float yaw = 0f, int pick = -1, float scale = 1f)
    {
        if (!ArtRow(kind, pick, top, out var prefab, out _, out float turn, out float own)) return null;
        return Place(prefab, top, yaw + turn, Vector3.one * (own * scale), true);
    }

    /// <summary>
    /// A bigger prop (a vending machine, a trolley) that blocks: the model, and under the terrain an unseen box of the
    /// model's own size turned with it, which goes into the walk-mesh bake like any box of the layout. Its top stays out
    /// of the band no zombie reaches (SolidBandLow to SolidBandTop): just over the band's bottom it comes down to it,
    /// higher up it goes up past it. Tables/MapArt's solid column can say otherwise for a kind: "cap" for a prop nobody
    /// should stand on (an unseen steep ridge over its top, see Cap), or a height in metres for one that only blocks
    /// that far up (a plant's pot; its leaves are walked and shot through).
    /// </summary>
    public GameObject PropSolid(string kind, Vector3 floor, float yaw = 0f, int pick = -1, float scale = 1f)
    {
        if (!ArtRow(kind, pick, floor, out var prefab, out _, out float turn, out float own)) return null;
        float size = own * scale, angle = yaw + turn;
        var model = Place(prefab, floor, angle, Vector3.one * size, false);
        // The model's own box, measured unturned and turned with it: a car at 30° keeps a car-sized box, where its
        // axis-aligned bounds stood past its corners as unseen walls with a roof in the air beside it.
        Vector3 half = BoundsOf(prefab).extents * size;
        float height = half.y * 2f;
        string solid = ArtSolid(kind);
        bool cap = solid == "cap";
        if (!cap && float.TryParse(solid, NumberStyles.Float, CultureInfo.InvariantCulture, out float upTo) && upTo > 0f)
            height = Mathf.Min(height, upTo);
        else if (!cap && height > SolidBandLow && height < SolidBandTop)
            height = height < SolidBandLow + SolidBandSink ? SolidBandLow : SolidBandTop;
        var box = Box(kind + " (충돌)", floor.x - half.x, floor.x + half.x, floor.y, floor.y + height, floor.z - half.z, floor.z + half.z);
        box.GetComponent<Renderer>().enabled = false;
        if (ShootOverCover.IsKind(kind)) box.AddComponent<ShootOverCover>();
        TurnBlock(box, angle);
        if (cap) Cap(box, kind, new Vector3(floor.x, floor.y + height, floor.z), half, angle);
        return model;
    }

    // The table's solid column for a kind ("" when it has none).
    private static string ArtSolid(string kind)
        => GameTables.TryGetRow(ArtTable, kind, out var row) ? Text(row, "solid").ToLowerInvariant() : "";

    // The layout's record of a box just made.
    private Block BlockOf(GameObject shape)
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
            if (blocks[i].Shape == shape) return blocks[i];
        return null;
    }

    // A box turned about Y on its centre, and its record: its own size stays, its world extent grows to the turned box's.
    private void TurnBlock(GameObject box, float yaw)
    {
        float turned = Mathf.Repeat(yaw, 360f);
        if (turned < .01f || turned > 359.99f) return;
        box.transform.localRotation = Quaternion.Euler(0f, turned, 0f);
        var block = BlockOf(box);
        if (block == null) return;
        block.Yaw = turned;
        block.Box = new Bounds(block.Box.center, Turned(block.Size, turned));
    }

    // Nobody stands on it: an unseen ridge of two slopes, steeper than anyone walks, along the top's longer side and
    // rising from both long edges to its middle. The player's climbs want a walkable top (ParkourDetector, the
    // controller's 45° slope limit) and the zombies' walk mesh stops at 45° too, so neither finds one, and a body
    // dropped on it slides off. A vending machine against a taller wall can't have walk mesh on its top (the bake trims
    // the agent's radius off at the wall), so a player up there was out of every zombie's reach; now nobody gets up.
    private void Cap(GameObject box, string kind, Vector3 top, Vector3 half, float yaw)
    {
        var turn = Quaternion.Euler(0f, yaw, 0f);
        bool alongX = half.x >= half.z;
        float span = alongX ? half.z : half.x, length = (alongX ? half.x : half.z) * 2f;
        Vector3 across = turn * (alongX ? Vector3.forward : Vector3.right);
        Vector3 ridge = top + Vector3.up * (span * Mathf.Tan(CapSlope * Mathf.Deg2Rad));
        foreach (float side in new[] { -1f, 1f })
        {
            var slope = Ramp(kind + " (못 올라섬)", top + across * (span * side), ridge, length, null, CapThickness);
            slope.GetComponent<Renderer>().enabled = false;
        }
        var block = BlockOf(box);
        if (block != null) block.NoStand = true;
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
    // to a look, gone once it is small on screen (FitLods), and static like the rest of the map.
    private GameObject Place(GameObject prefab, Vector3 point, float yaw, Vector3 scale, bool hang)
    {
        dressing ??= Root("◤━━ 꾸밈 (Tables/MapArt, 보이기만 함)");
        var bounds = BoundsOf(prefab);
        var copy = Spawn(prefab);
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
        FitLods(copy);
        artPlaced++;
        LastPlaced = copy;
        return copy;
    }

    // Models come in under this inactive holder and are stripped there (Strip), before anything of theirs wakes up: so
    // Unity never cooks the convex hull of a collider that is thrown away anyway (the airport's baggage cart has one past
    // the 256-polygon limit, which warned on every build, 11-12), and no prop script or light runs in the editor.
    private Transform staging;

    private GameObject Spawn(GameObject prefab)
    {
        if (staging == null)
        {
            var holder = new GameObject("◤━━ 들이는 중 (지을 때만)");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);
            staging = holder.transform;
        }
        var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, staging);
        Strip(copy);
        return copy;
    }

    // Before the scene is finished: the holder is empty by then (every copy went on to the dressing or was measured and
    // destroyed), and it isn't part of the map.
    private void EndStaging()
    {
        if (staging != null) Object.DestroyImmediate(staging.gameObject);
        staging = null;
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
        var probe = Spawn(prefab);
        // Out of the holder, awake now with nothing left but its renderers, so they have bounds.
        probe.transform.SetParent(null, true);
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
        return text + LodReport();
    }
}
