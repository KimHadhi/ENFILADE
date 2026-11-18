using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Map dressing, a bought scene as a map's building (the user, 2026-11-14, through the general manager: "공항 실내는 그
/// 그대로 쓰면 되지 않음 내가 산 에셋?? 공항 맵 같던데 에셋 사니까 풀 패키지로 샀으니까", then "팩 씬으로 새로 짓기"):
/// chapter 4's terminal is the Airport Terminal Scene pack's own scene (Assets/ThirdParty/Airport_Terminal_Scene/Scene),
/// opened beside the map and moved into it whole, turned and placed (PackScene), its 2,600 props, lamps and reflection
/// probes where the pack's maker laid them. The pack made it a set to look round from inside, so the builder finishes it
/// as a building:
/// - Its camera, sound sources, sun and old light probes go (the map has its own player camera, sound zones, sun and
///   probe volume). Its lamps stay Baked: their light and its bounce go into the map's probe volume
///   (GreyboxBuilder.Probes), and none of them is drawn live.
/// - Its collision is its own: the building's collision meshes (walls, floors, escalators) stay; the props' colliders go,
///   and each prop that stands on a floor and is big enough to bump into (a counter, a shelf, a row of seats, a vending
///   machine; not a bottle or a chair) gets an unseen box of its own size turned with it, which goes into the walk-mesh
///   bake as the layout's boxes do. A top higher than a zombie's arm that no zombie could stand on (narrow, or under a
///   ceiling) gets the unseen ridge nobody stands on (Cap), so no prop is a perch out of every zombie's reach (memo 48).
/// - Its doors are painted on (no door leads out): PackCut takes a doorway out of its walls and their collision
///   (MeshCut), for the map's way in and out.
/// - Its walls have their inner face only: PackOutside gives walls and ceilings an outer face (turned copies in the
///   map's own wall and roof looks, Tables/MapSurfaces), the glass a face both ways and the collision both sides (a body
///   coming from outside went through a one-sided wall), and PackPodium fills the space under its upper floor where it
///   has no ground floor (seen from outside, that floor hung 4 m up in the air).
/// - Its props get the map's levels of detail (FitLods), and it is static like the rest of the map (MarkStatic): its
///   opaque shell hides what is behind it, and everything takes part in the probe bake and the reflection probes.
/// What it changes is kept beside the scene (Scenes/Campaign/&lt;scene&gt;/Pack: the cut and turned meshes); the pack's own
/// files are never changed, so each build makes the map from them the same way.
/// </summary>
public sealed partial class GreyboxBuilder
{
    /// <summary>Which objects of a bought scene are its building, by the start of their names.</summary>
    public sealed class PackParts
    {
        /// <summary>Collision meshes that stay as they are (walls, floors, ramps, a counter's own collider).</summary>
        public string[] Collision = { };
        /// <summary>Opaque walls: they hide what is behind them and get an outer face in the wall look.</summary>
        public string[] Walls = { };
        /// <summary>Ceilings: they hide what is behind them and get an outer face in the roof look.</summary>
        public string[] Ceilings = { };
        /// <summary>Floors and roofs: they hide what is behind them; their outer face is there already.</summary>
        public string[] Shell = { };
        /// <summary>See-through panes (windows, skylights): a face both ways, never hiding anything.</summary>
        public string[] Glass = { };
        /// <summary>Other parts of the building (escalators, planters): no box and no LOD of their own.</summary>
        public string[] Fixed = { };
        /// <summary>Parts a doorway never cuts (the floors' collision under it: a doorway is cut down past the floor
        /// so the wall goes to its foot).</summary>
        public string[] Uncut = { };
    }

    // A prop that stands on a floor is at most this far over it (m), and is boxed from this size on: this high and this
    // long on its longer side (m).
    private const float PackStandsWithin = .35f, PackSolidHeight = .3f, PackSolidLength = .6f;
    // The podium's grid (m), how far a floor's height may be from the given one (m), and how far under the upper floor
    // the podium stops (m).
    private const float PodiumCell = 1f, PodiumFloorNear = .3f, PodiumUnder = .05f;

    private Transform pack;
    private PackParts packParts;
    // Where PackScene puts the bought scene's origin, for PackPoint even when the scene was not found.
    private Matrix4x4 packFrame = Matrix4x4.identity;
    // The building's opaque parts and their outer faces: they hide what is behind them (MarkStatic).
    private readonly HashSet<GameObject> packOccluders = new HashSet<GameObject>();
    // The meshes this build cut or turned, by the object that shows or collides with them.
    private readonly Dictionary<Mesh, Mesh> packCutMeshes = new Dictionary<Mesh, Mesh>();
    private int packLamps, packSolids, packCapped, packCuts, packOutside, packPodium, packRemoved, packLods;
    private string packMissing;

    /// <summary>A point given in the bought scene's own coordinates, where it is in the map.</summary>
    public Vector3 PackPoint(float x, float y, float z) => packFrame.MultiplyPoint3x4(new Vector3(x, y, z));

    /// <summary>
    /// Opens the scene at <paramref name="scenePath"/> beside the map and moves everything in it into the map, under one
    /// root at <paramref name="position"/> turned <paramref name="yaw"/>° about Y, and readies it as a building (see the
    /// class): what of it is the building is <paramref name="parts"/>. Null (and named in the build's report) when the
    /// scene isn't there.
    /// </summary>
    public Transform PackScene(string name, string scenePath, Vector3 position, float yaw, PackParts parts)
    {
        packFrame = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
        packParts = parts;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            packMissing = scenePath;
            Debug.LogWarning("[Greybox] " + sceneName + ": the bought scene " + scenePath + " is not in the project; the map has no building there.");
            return null;
        }
        var opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        var root = new GameObject(name).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        root.SetParent(terrain, false);
        root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        foreach (var top in opened.GetRootGameObjects())
        {
            SceneManager.MoveGameObjectToScene(top, scene);
            // Its place in the bought scene becomes its place under the root.
            top.transform.SetParent(root, false);
        }
        EditorSceneManager.CloseScene(opened, true);
        pack = root;
        CleanPack();
        Physics.SyncTransforms();
        return root;
    }

    // What the map brings itself goes, the lamps stay Baked (the props' colliders go in PackProps).
    private void CleanPack()
    {
        // A camera on its own object goes with it; one inside a prefab copy only loses the component (a prefab copy's
        // part can't be taken away).
        foreach (var eye in pack.GetComponentsInChildren<Camera>(true))
            if (PrefabUtility.IsPartOfPrefabInstance(eye.gameObject)) Object.DestroyImmediate(eye);
            else Object.DestroyImmediate(eye.gameObject);
        foreach (var ear in pack.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(ear);
        foreach (var sound in pack.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(sound);
        foreach (var probes in pack.GetComponentsInChildren<LightProbeGroup>(true)) Object.DestroyImmediate(probes);
        foreach (var body in pack.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        foreach (var lamp in pack.GetComponentsInChildren<Light>(true))
        {
            if (lamp.type == LightType.Directional)
            {
                // URP's data for a light needs the light, so it goes first. The pack's sun stayed on beside the map's
                // until the rebuild of 11-14 ("Can't remove Light because UniversalAdditionalLightData (Script) depends
                // on it"): chapter 4 had two suns.
                if (lamp.TryGetComponent(out UniversalAdditionalLightData data)) Object.DestroyImmediate(data);
                Object.DestroyImmediate(lamp);
                continue;
            }
            // Baked whole into the probe volume: lit rooms with no lamp drawn live (two hundred of them would be).
            lamp.lightmapBakeType = LightmapBakeType.Baked;
            packLamps++;
        }
        // Its reflection probes are baked with the map's (a live one renders the room again every frame).
        foreach (var probe in pack.GetComponentsInChildren<ReflectionProbe>(true)) probe.mode = ReflectionProbeMode.Baked;
        // Scripts left out when it was brought in (import_unitypackage.py --strip-scripts takes them off already).
        foreach (var part in pack.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(part.gameObject) == 0) continue;
            try { GameObjectUtility.RemoveMonoBehavioursWithMissingScript(part.gameObject); }
            catch (System.Exception error) { Debug.LogWarning("[Greybox] " + part.name + ": a missing script stays (" + error.Message + ")", part); }
        }
    }

    /// <summary>
    /// The props' own colliders go, and each one that had any, stands on a floor and is big enough gets a box of its
    /// size (see the class); every prop gets the map's levels of detail. Called once the building is finished (its
    /// doorways cut and props taken away), so no box is left standing where a prop was taken away.
    /// </summary>
    public void PackProps()
    {
        if (pack == null) return;
        var units = new HashSet<GameObject>();
        foreach (var solid in pack.GetComponentsInChildren<Collider>(true))
        {
            var unit = Unit(solid.gameObject);
            if (Named(solid.gameObject, packParts.Collision)) continue;
            Object.DestroyImmediate(solid);
            if (!Named(unit, packParts.Fixed)) units.Add(unit);
        }
        Physics.SyncTransforms();
        foreach (var unit in units) PackSolid(unit);
        // Every prop is culled when small on screen, as the map's own models are (Tables/MapLods): a prefab copy as a
        // whole, a part of the scene's own with nothing drawn under it on its own (a group with drawn parts under it
        // would put those parts in two LOD groups).
        // A pack's group may hold parts that stand apart from it (the airport's phone blocks hold their phones and
        // signs): those parts are held already, so no group of their own goes on them (a renderer in two groups is
        // refused: "Renderer 'phoneblock_big_glass' is registered with more than one LODGroup", the rebuild of 11-14).
        var held = new HashSet<Renderer>();
        foreach (var group in pack.GetComponentsInChildren<LODGroup>(true)) Hold(group, held);
        var props = new HashSet<GameObject>();
        foreach (var shape in pack.GetComponentsInChildren<Renderer>(true))
        {
            var unit = Unit(shape.gameObject);
            // The building, its outer faces and the podium are never small on screen.
            if (IsBuilding(unit) || packOccluders.Contains(shape.gameObject)) continue;
            if (unit == shape.gameObject && shape.GetComponentsInChildren<Renderer>(true).Length > 1) continue;
            props.Add(unit);
        }
        foreach (var prop in props)
        {
            FitLods(prop, held);
            // Small ones cast no shadow, as the map's own models (QuietShadows).
            QuietShadows(prop);
        }
        packLods = props.Count;
    }

    private void PackSolid(GameObject unit)
    {
        // Its turn about Y: its forward flattened (or its up, for a model stood on its exporter's -90° X).
        var turn = unit.transform.rotation;
        Vector3 forward = turn * Vector3.forward;
        if (Mathf.Abs(forward.y) > .7f) forward = turn * Vector3.up;
        forward.y = 0f;
        float yaw = forward.sqrMagnitude < 1e-6f ? 0f : Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
        var frame = Quaternion.Euler(0f, yaw, 0f);
        var back = Quaternion.Inverse(frame);
        Vector3 origin = unit.transform.position, min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (var shape in unit.GetComponentsInChildren<Renderer>())
        {
            if (!(shape is MeshRenderer) && !(shape is SkinnedMeshRenderer)) continue;
            var local = shape.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                var at = new Vector3(corner % 2 == 0 ? local.min.x : local.max.x, corner / 2 % 2 == 0 ? local.min.y : local.max.y,
                    corner / 4 == 0 ? local.min.z : local.max.z);
                Vector3 p = back * (shape.transform.TransformPoint(at) - origin);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        if (min.x > max.x) return;
        Vector3 size = max - min;
        if (size.y < PackSolidHeight || Mathf.Max(size.x, size.z) < PackSolidLength) return;
        Vector3 middle = origin + frame * ((min + max) * .5f);
        float bottom = origin.y + min.y;
        // It must stand on the building's floor (a wall-hung sign or a hanging lamp gets no box).
        if (!PackFloorUnder(new Vector3(middle.x, bottom, middle.z), out float floor) || bottom - floor > PackStandsWithin) return;
        float height = bottom - floor + size.y, top = floor + height;
        float room = PackRoomOver(new Vector3(middle.x, top, middle.z));
        bool narrow = Mathf.Min(size.x, size.z) < 2f * AgentRadius + NarrowStrip;
        bool cap = height > ArmHeight && (narrow || room < AgentHeight);
        if (!cap && height > SolidBandLow && height < SolidBandTop)
            height = height < SolidBandLow + SolidBandSink ? SolidBandLow : SolidBandTop;
        var half = new Vector3(size.x * .5f, 0f, size.z * .5f);
        var box = Box(unit.name + " (충돌)", middle.x - half.x, middle.x + half.x, floor, floor + height, middle.z - half.z, middle.z + half.z);
        box.GetComponent<Renderer>().enabled = false;
        TurnBlock(box, yaw);
        var block = BlockOf(box);
        if (block != null)
        {
            block.Floor = floor;
            block.Prop = true;
        }
        packSolids++;
        if (!cap) return;
        Cap(box, unit.name, new Vector3(middle.x, floor + height, middle.z), half, yaw);
        packCapped++;
    }

    // The building's floor just under a point (its collision meshes only), within a step.
    private bool PackFloorUnder(Vector3 point, out float floor)
    {
        floor = float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * .25f, Vector3.down, .25f + PackStandsWithin + .05f))
            if (hit.collider.transform.IsChildOf(pack)) floor = Mathf.Max(floor, hit.point.y);
        return !float.IsNegativeInfinity(floor);
    }

    // How much room there is over a top before the building's next floor (m; much when nothing is over it). Its floors
    // are seen from below here, so backfaces count.
    private float PackRoomOver(Vector3 top)
    {
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        float room = 100f;
        foreach (var hit in Physics.RaycastAll(top + Vector3.up * .02f, Vector3.up, 100f))
            if (hit.collider.transform.IsChildOf(pack)) room = Mathf.Min(room, hit.distance + .02f);
        Physics.queriesHitBackfaces = backfaces;
        return room;
    }

    /// <summary>
    /// Takes the box from <paramref name="from"/> to <paramref name="to"/> (in the bought scene's own coordinates) out
    /// of the building's walls, glass and collision (a doorway), the parts named by <paramref name="names"/> or, if none,
    /// its walls, glass and collision; never the parts its PackParts leave uncut (the floor under the doorway).
    /// </summary>
    public void PackCut(string name, Vector3 from, Vector3 to, params string[] names)
    {
        if (pack == null) return;
        if (names.Length == 0) names = Join(packParts.Walls, packParts.Glass, packParts.Collision);
        var box = new Bounds((from + to) * .5f, Abs(to - from));
        var toPack = pack.worldToLocalMatrix;
        // A mesh both shown and collided with (a collision mesh's filter and collider) is cut once for both.
        var cutHere = new Dictionary<Mesh, Mesh>();
        foreach (var filter in pack.GetComponentsInChildren<MeshFilter>(true))
            if (Named(filter.gameObject, names) && !Named(filter.gameObject, packParts.Uncut) && CutInto(filter.sharedMesh, toPack * filter.transform.localToWorldMatrix, box, cutHere, out var cut))
                filter.sharedMesh = cut;
        foreach (var solid in pack.GetComponentsInChildren<MeshCollider>(true))
            if (Named(solid.gameObject, names) && !Named(solid.gameObject, packParts.Uncut) && CutInto(solid.sharedMesh, toPack * solid.transform.localToWorldMatrix, box, cutHere, out var cut))
                solid.sharedMesh = cut;
        if (cutHere.Count > 0) packCuts++;
        else Debug.LogWarning("[Greybox] " + sceneName + ": the doorway '" + name + "' met none of the building's walls.");
    }

    // One mesh's cut, kept as an asset beside the scene under its first name (a mesh cut again in this build replaces
    // its earlier cut there, every user of it having moved on to the new one).
    private bool CutInto(Mesh mesh, Matrix4x4 toBox, Bounds box, Dictionary<Mesh, Mesh> cutHere, out Mesh cut)
    {
        cut = null;
        if (mesh == null) return false;
        if (cutHere.TryGetValue(mesh, out cut)) return true;
        if (!ReadableModel(mesh)) return false;
        var made = MeshCut.Cut(mesh, toBox, box);
        if (made == null) return false;
        var first = packCutMeshes.TryGetValue(mesh, out var original) ? original : mesh;
        SavePackMesh(made, first.name);
        packCutMeshes[made] = first;
        cutHere[mesh] = made;
        cut = made;
        return true;
    }

    /// <summary>
    /// The outer faces (see the class): walls in the look <paramref name="wallLook"/>, ceilings in
    /// <paramref name="roofLook"/> (Tables/MapSurfaces), glass in its own materials, and the collision turned round too.
    /// </summary>
    public void PackOutside(string wallLook, string roofLook)
    {
        if (pack == null) return;
        var wall = Look(wallLook) ?? Concrete;
        var roof = Look(roofLook) ?? Concrete;
        foreach (var filter in pack.GetComponentsInChildren<MeshFilter>(true))
        {
            var part = filter.gameObject;
            bool isWall = Named(part, packParts.Walls), isCeiling = Named(part, packParts.Ceilings), isGlass = Named(part, packParts.Glass);
            if (!isWall && !isCeiling && !isGlass) continue;
            if (!part.TryGetComponent(out MeshRenderer shown) || !ReadableModel(filter.sharedMesh)) continue;
            var turned = MeshCut.Flipped(filter.sharedMesh, !isGlass);
            if (turned == null) continue;
            SavePackMesh(turned, filter.sharedMesh.name + " 바깥");
            var outside = new GameObject(part.name + " 바깥", typeof(MeshFilter), typeof(MeshRenderer));
            outside.transform.SetParent(part.transform, false);
            outside.GetComponent<MeshFilter>().sharedMesh = turned;
            var look = outside.GetComponent<MeshRenderer>();
            look.sharedMaterials = isGlass ? shown.sharedMaterials : new[] { isWall ? wall : roof };
            // The inner faces already cast their shadows both ways.
            look.shadowCastingMode = ShadowCastingMode.Off;
            if (!isGlass) packOccluders.Add(outside);
            packOutside++;
        }
        foreach (var solid in pack.GetComponentsInChildren<MeshCollider>(true))
        {
            if (!Named(solid.gameObject, packParts.Collision) || !ReadableModel(solid.sharedMesh)) continue;
            var turned = MeshCut.Flipped(solid.sharedMesh, true);
            if (turned == null) continue;
            SavePackMesh(turned, solid.sharedMesh.name + " 바깥 충돌");
            var outside = new GameObject(solid.gameObject.name + " 바깥 충돌", typeof(MeshCollider));
            outside.transform.SetParent(solid.transform, false);
            outside.GetComponent<MeshCollider>().sharedMesh = turned;
        }
        // The opaque shell hides what is behind it; glass never does, even named like a shell part ("dachfesnter", the
        // roof's windows, starts like "dach", the roof).
        foreach (var shown in pack.GetComponentsInChildren<MeshRenderer>(true))
            if (Named(shown.gameObject, Join(packParts.Walls, packParts.Ceilings, packParts.Shell)) && !Named(shown.gameObject, packParts.Glass))
                packOccluders.Add(shown.gameObject);
    }

    /// <summary>
    /// Fills the space under the building's upper floor (at <paramref name="upper"/> m in its own coordinates) wherever it
    /// has no ground floor (at <paramref name="ground"/>) under it, with boxes in the look <paramref name="look"/> from the
    /// ground to just under that floor: seen from outside, a floor with nothing under it hung in the air.
    /// </summary>
    public void PackPodium(string look, float ground, float upper)
    {
        if (pack == null) return;
        Physics.SyncTransforms();
        var floors = new List<Collider>();
        var extent = new Bounds();
        bool any = false;
        foreach (var solid in pack.GetComponentsInChildren<MeshCollider>(true))
        {
            if (!Named(solid.gameObject, packParts.Collision) || solid.transform.parent != null && solid.name.EndsWith(" 바깥 충돌")) continue;
            floors.Add(solid);
            var local = solid.sharedMesh != null ? solid.sharedMesh.bounds : new Bounds();
            var inPack = TransformBounds(pack.worldToLocalMatrix * solid.transform.localToWorldMatrix, local);
            if (any) extent.Encapsulate(inPack);
            else { extent = inPack; any = true; }
        }
        if (!any) return;
        int across = Mathf.CeilToInt(extent.size.x / PodiumCell), along = Mathf.CeilToInt(extent.size.z / PodiumCell);
        var under = new bool[across, along];
        for (int i = 0; i < across; i++)
            for (int j = 0; j < along; j++)
            {
                var at = new Vector3(extent.min.x + (i + .5f) * PodiumCell, upper + 2f, extent.min.z + (j + .5f) * PodiumCell);
                bool top = false, bottom = false;
                foreach (var hit in Physics.RaycastAll(pack.TransformPoint(at), Vector3.down, upper - ground + 3f))
                {
                    if (!floors.Contains(hit.collider)) continue;
                    float y = pack.InverseTransformPoint(hit.point).y;
                    if (Mathf.Abs(y - upper) < PodiumFloorNear) top = true;
                    if (Mathf.Abs(y - ground) < PodiumFloorNear) bottom = true;
                }
                under[i, j] = top && !bottom;
            }
        var podium = new GameObject("받침 (윗층 밑)").transform;
        podium.SetParent(pack, false);
        var material = Look(look) ?? Concrete;
        // Runs along x, grown along z while the next row has the same run.
        for (int j = 0; j < along; j++)
            for (int i = 0; i < across; i++)
            {
                if (!under[i, j]) continue;
                int end = i;
                while (end + 1 < across && under[end + 1, j]) end++;
                int rows = 1;
                while (j + rows < along && Run(under, i, end, j + rows, across)) rows++;
                for (int r = 0; r < rows; r++)
                    for (int k = i; k <= end; k++) under[k, j + r] = false;
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "받침";
                Object.DestroyImmediate(block.GetComponent<Collider>());
                block.transform.SetParent(podium, false);
                float x0 = extent.min.x + i * PodiumCell, x1 = extent.min.x + (end + 1) * PodiumCell;
                float z0 = extent.min.z + j * PodiumCell, z1 = extent.min.z + (j + rows) * PodiumCell, y1 = upper - PodiumUnder;
                block.transform.localPosition = new Vector3((x0 + x1) * .5f, (ground + y1) * .5f, (z0 + z1) * .5f);
                block.transform.localScale = new Vector3(x1 - x0, y1 - ground, z1 - z0);
                block.GetComponent<Renderer>().sharedMaterial = material;
                packOccluders.Add(block);
                packPodium++;
                i = end;
            }
    }

    // Whether the cells i..end of row j are all set, and the cells just beside them in that row aren't (the same run).
    private static bool Run(bool[,] cells, int i, int end, int j, int across)
    {
        for (int k = i; k <= end; k++) if (!cells[k, j]) return false;
        return (i == 0 || !cells[i - 1, j]) && (end + 1 >= across || !cells[end + 1, j]);
    }

    /// <summary>Takes away the bought scene's objects named <paramref name="startsWith"/> within
    /// <paramref name="within"/> m of a point in its own coordinates (a door painted shut where the map opens one).</summary>
    public void PackRemove(string startsWith, Vector3 near, float within = .8f)
    {
        if (pack == null) return;
        var gone = new HashSet<GameObject>();
        foreach (var part in pack.GetComponentsInChildren<Transform>(true))
        {
            if (!part.name.StartsWith(startsWith, System.StringComparison.Ordinal)) continue;
            if (Vector3.Distance(pack.InverseTransformPoint(part.position), near) > within) continue;
            gone.Add(Unit(part.gameObject));
        }
        foreach (var unit in gone)
            if (unit != null)
            {
                Object.DestroyImmediate(unit);
                packRemoved++;
            }
        if (gone.Count == 0) Debug.LogWarning("[Greybox] " + sceneName + ": nothing named '" + startsWith + "' near " + Format(near) + " in the bought scene.");
    }

    // Static flags for the bought scene's renderers (MarkStatic): batched, culled when hidden, in the reflection probes'
    // pictures and the probe bake; its opaque shell hides what is behind it.
    private bool PackStatic(Renderer shape)
    {
        if (pack == null || !shape.transform.IsChildOf(pack)) return false;
        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        if (packOccluders.Contains(shape.gameObject)) flags |= StaticEditorFlags.OccluderStatic;
        TakesPartInBake(shape, ref flags);
        GameObjectUtility.SetStaticEditorFlags(shape.gameObject, flags);
        return true;
    }

    // The outermost prefab copy a part belongs to (a prop), or the part itself.
    private static GameObject Unit(GameObject part)
    {
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(part);
        return root != null ? root : part;
    }

    private bool IsBuilding(GameObject part)
        => Named(part, Join(packParts.Collision, packParts.Walls, packParts.Ceilings, packParts.Shell, packParts.Glass, packParts.Fixed));

    private static bool Named(GameObject part, string[] names)
    {
        foreach (string start in names)
            if (part.name.StartsWith(start, System.StringComparison.Ordinal)) return true;
        return false;
    }

    private static string[] Join(params string[][] lists)
    {
        var all = new List<string>();
        foreach (var list in lists) all.AddRange(list);
        return all.ToArray();
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
    {
        var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
            result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(corner % 2 == 0 ? local.min.x : local.max.x,
                corner / 2 % 2 == 0 ? local.min.y : local.max.y, corner / 4 == 0 ? local.min.z : local.max.z)));
        return result;
    }

    // An imported model's mesh is read only with Read/Write on: switched on once for the bought building's model.
    private static bool ReadableModel(Mesh mesh)
    {
        if (mesh == null) return false;
        if (MeshCut.Readable(mesh)) return true;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (AssetImporter.GetAtPath(path) is ModelImporter model && !model.isReadable)
        {
            model.isReadable = true;
            model.SaveAndReimport();
        }
        return MeshCut.Readable(mesh);
    }

    // A mesh this build made, as an asset beside the scene (Scenes/Campaign/<scene>/Pack/<name>.asset).
    private void SavePackMesh(Mesh mesh, string name)
    {
        string data = Folder + "/" + sceneName, folder = data + "/Pack";
        if (!AssetDatabase.IsValidFolder(data)) AssetDatabase.CreateFolder(Folder, sceneName);
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(data, "Pack");
        foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        AssetDatabase.CreateAsset(mesh, folder + "/" + name + ".asset");
    }

    // For the build's report.
    private string PackReport()
    {
        if (packMissing != null) return "\n팩 장면: " + packMissing + "이 없어 건물이 비었어요(Tools/Props의 import_unitypackage.py로 들여 주세요).";
        if (pack == null) return "";
        return "\n팩 장면(" + pack.name + "): 등 " + packLamps + "개는 Baked로 프로브에 구웠어요. 소품 충돌 상자 " + packSolids + "개(그중 " + packCapped +
               "개는 못 올라서게 지붕을 씌움), 문 " + packCuts + "곳을 벽에서 잘라 냄, 바깥 면 " + packOutside + "개, 윗층 밑 받침 " + packPodium +
               "개, 지운 소품 " + packRemoved + "개, LOD를 맞춘 소품 " + packLods + "개.";
    }
}
