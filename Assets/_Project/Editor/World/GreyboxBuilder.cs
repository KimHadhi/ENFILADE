using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a campaign map's grey-block scene from code (multiplayer plan stage 5A). A chapter's layout (CampaignMaps)
/// places boxes, ramps, ladders, cables, swing bars, the player's start, enemy spawn points and named progress spots;
/// this makes the scene around them the way the test maps are made (the light, the UI input, GameHUD, the waves' manager
/// with Enemy_Test, a walk mesh baked from the boxes' colliders), saves it under Scenes/Campaign with its walk mesh
/// beside it, and puts it in the build so the main menu offers it (Maps.csv names it). Running it again rebuilds the
/// scene from scratch, so hand edits to a built scene are lost: change the layout in code instead.
/// Boxes are given by their extents in metres, x0..x1, y0..y1, z0..z1. A chapter runs along +X.
/// Small props (the user's memo 48): no prop a player can stand on may be out of every zombie's reach. The bake keeps
/// small tops, lays a walk slab over narrow ones a zombie could climb to, and the build names in the console any prop
/// still out of reach (see "Small props" below). Every chapter at once: RebuildAll. Each chapter's sky, sun and fog
/// come from Tables/MapLook (GreyboxBuilder.Look.cs).
/// </summary>
public sealed partial class GreyboxBuilder
{
    public const string Folder = "Assets/_Project/Scenes/Campaign";
    private const string HudPrefab = "Assets/_Project/Prefabs/UI/HUD/GameHUD.prefab";
    private const string EnemyPrefab = "Assets/_Project/Resources/Enemies/Enemy_Test.prefab";
    private const string Materials = "Assets/_Project/Art/Materials/";
    // Spawn points sit this far over what they stand on, so nothing starts inside the floor (m).
    private const float Lift = .05f;

    // While every chapter is rebuilt in one go (RebuildAll): what each build said, told once at the end.
    private static StringBuilder batch;

    // A solid box of the layout, for the small props' walk slabs and their check: where it is, and whether it moves (a
    // gate, a door), which keeps it out of both.
    private sealed class Block
    {
        public GameObject Shape;
        public Bounds Box;
        public bool Moves;
    }

    private readonly string sceneName, title;
    private readonly List<Transform> enemySpawns = new List<Transform>(), spots = new List<Transform>(), cans = new List<Transform>(),
        shops = new List<Transform>();
    private readonly List<CampaignDoor> doors = new List<CampaignDoor>();
    private readonly List<Block> blocks = new List<Block>();
    // Glass and the like: solid, but they hide nothing behind them from the camera (MarkStatic).
    private readonly HashSet<GameObject> seeThrough = new HashSet<GameObject>();

    /// <summary>The scene gets a campaign chapter's run of objectives (CampaignRun). Off for a map that brings its own
    /// run, as the ENFILADE tower does (TowerMap, TowerRun).</summary>
    public bool AddsCampaign { get; set; } = true;
    private Scene scene;
    private Transform terrain, parkour, enemies, progress, systems, pickups, ambience;
    private int pickupCount;

    public Material Concrete { get; private set; }
    public Material Road { get; private set; }
    public Material Metal { get; private set; }
    public Material Wood { get; private set; }
    public Material Marker { get; private set; }

    private GreyboxBuilder(string sceneName, string title)
    {
        this.sceneName = sceneName;
        this.title = title;
    }

    public string ScenePath => Folder + "/" + sceneName + ".unity";

    /// <summary>Asks before rebuilding a scene that exists, saves what is open, and starts the new scene. Null if called off.
    /// Within RebuildAll it asks nothing: that was asked once for every chapter.</summary>
    public static GreyboxBuilder Begin(string sceneName, string title)
    {
        var builder = new GreyboxBuilder(sceneName, title);
        if (EditorApplication.isPlayingOrWillChangePlaymode) { builder.Tell("Play를 멈춘 뒤에 실행해 주세요."); return null; }
        if (batch == null && File.Exists(builder.ScenePath) && !EditorUtility.DisplayDialog(title,
                builder.ScenePath + " 가 이미 있어요. 다시 지으면 씬에서 손으로 고친 것이 사라져요. 다시 지을까요?", "다시 짓기", "취소"))
            return null;
        if (batch == null && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
        builder.Start();
        return builder;
    }

    /// <summary>
    /// Rebuilds every chapter one after another (Tools > World > 캠페인 회색 맵 > 모든 장 다시 짓기): one question first,
    /// then each chapter's build as if picked from its own menu item, and one window at the end with what each made and
    /// what its small-prop check found. A chapter that fails is reported and the rest still build.
    /// </summary>
    public static void RebuildAll(string title, params (string name, System.Action build)[] chapters)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog(title, "Play를 멈춘 뒤에 실행해 주세요.", "확인"); return; }
        if (!EditorUtility.DisplayDialog(title, "캠페인 " + chapters.Length + "개 장을 1장부터 차례로 다시 지어요(좀비 길찾기, 가림·환경광 굽기까지). " +
                "씬에서 손으로 고친 것은 모두 사라져요. 계속할까요?", "모두 다시 짓기", "취소"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        batch = new StringBuilder();
        try
        {
            for (int i = 0; i < chapters.Length; i++)
            {
                EditorUtility.DisplayProgressBar(title, chapters[i].name, (float)i / chapters.Length);
                try { chapters[i].build(); }
                catch (System.Exception error)
                {
                    Debug.LogException(error);
                    batch.Append("■ ").Append(chapters[i].name).Append(": 짓다가 멈췄어요 (").Append(error.Message).Append(")\n\n");
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        string report = batch.ToString();
        batch = null;
        EditorUtility.DisplayDialog(title, report.Length > 0 ? report : "지은 장이 없어요.", "확인");
    }

    private void Start()
    {
        scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        // The player brings its own camera; the scene's light becomes the sun.
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<Camera>() != null) { Object.DestroyImmediate(root); continue; }
            if (!root.TryGetComponent(out Light light)) continue;
            light.name = "해";
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            light.shadows = LightShadows.Soft;
            sun = light;
        }
        // Map dressing stage 4: the scene's real surfaces where Tables/MapSurfaces has them, else the grey ones.
        BeginSurfaces();
        Concrete = Surface("Concrete", Load("MovementPlayground.mat"));
        Road = Surface("Road", Load("SurfacePads/SurfacePad_ConcreteGrit.mat"));
        Metal = Surface("Metal", Load("SurfacePads/SurfacePad_Metal.mat"));
        Wood = Surface("Wood", Load("SurfacePads/SurfacePad_Wood.mat"));
        Marker = Load("TestRedMat.mat");
        terrain = Root("◤━━ 지형 (길찾기 굽는 곳)");
        parkour = Root("◤━━ 사다리·집라인·철봉");
        enemies = Root("◤━━ 적 나오는 곳");
        progress = Root("◤━━ 진행 자리 (CampaignSteps.csv의 spot 이름)");
        systems = Root("◤━━ 시스템");
        pickups = Root("◤━━ 줍기 자리 (무엇이 놓일지는 Tables/Pickups)");
        var input = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        input.transform.SetParent(systems, false);
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
        if (hud != null) PrefabUtility.InstantiatePrefab(hud, scene);
        else Debug.LogWarning("[Greybox] " + HudPrefab + " not found; the scene has no HUD.");
    }

    /// <summary>A solid block (or, with <paramref name="solid"/> off, a look only: no collider, no walk mesh). In a
    /// <paramref name="group"/> made by Pushback it moves with the group.</summary>
    public GameObject Box(string name, float x0, float x1, float y0, float y1, float z0, float z1,
        Material material = null, string surface = null, bool solid = true, Transform group = null)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(group != null ? group : terrain, false);
        box.transform.localPosition = new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, (z0 + z1) * .5f);
        box.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        Dress(box, material, surface, solid);
        if (solid)
            blocks.Add(new Block { Shape = box, Box = new Bounds(box.transform.localPosition, box.transform.localScale), Moves = group != null });
        return box;
    }

    /// <summary>
    /// A group of boxes that a manned hold moves as one by <paramref name="offset"/> (CampaignPushback; chapter 4's
    /// airliner): put its parts in with Box(..., group: it), give the tug to PushbackTug and carve what stands on the
    /// ground (Carve). The group sits at the origin, so its parts take world extents, and it stays out of the walk-mesh
    /// bake, the small-prop check and the static flags.
    /// </summary>
    public Transform Pushback(string name, string step, Vector3 offset)
    {
        var group = new GameObject(name);
        group.transform.SetParent(terrain, false);
        group.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Set(group.AddComponent<CampaignPushback>(), so =>
        {
            so.FindProperty("step").stringValue = step;
            so.FindProperty("offset").vector3Value = offset;
        });
        return group.transform;
    }

    /// <summary>Where a pushback's engine sounds from.</summary>
    public void PushbackTug(Transform group, GameObject tug)
        => Set(group.GetComponent<CampaignPushback>(), so => so.FindProperty("tug").objectReferenceValue = tug.transform);

    /// <summary>A part of a moving group that cuts the walk mesh where it stands as it goes (the bake left it out). It
    /// carves again each time it has moved a third of a metre, not every frame.</summary>
    public void Carve(GameObject part)
    {
        var obstacle = part.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = Vector3.one;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        obstacle.carvingMoveThreshold = .3f;
    }

    /// <summary>Where Tools > World > 맵 사진 찍기 (MapPictures) takes the menu's map picture from, looking at
    /// <paramref name="lookAt"/>.</summary>
    public void Shot(Vector3 position, Vector3 lookAt)
    {
        var shot = new GameObject("MapShot").transform;
        shot.SetParent(systems, false);
        shot.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
    }

    // A gate or a door: it moves, so no walk slab goes on it and the small-prop check leaves it alone.
    private void Moving(GameObject shape)
    {
        foreach (var block in blocks) if (block.Shape == shape) block.Moves = true;
    }

    /// <summary>A solid panel that a campaign step opens (CampaignGate), moving by <paramref name="openOffset"/>. It stays
    /// out of the walk-mesh bake and carves the mesh where it stands at run time, so enemies pass once it opens.</summary>
    public GameObject Gate(string name, float x0, float x1, float y0, float y1, float z0, float z1, string step, Vector3 openOffset,
        Material material = null, string surface = null, bool alarm = true)
    {
        var gate = Box(name, x0, x1, y0, y1, z0, z1, material, surface);
        Moving(gate);
        gate.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var obstacle = gate.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = Vector3.one;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        Set(gate.AddComponent<CampaignGate>(), so =>
        {
            so.FindProperty("step").stringValue = step;
            so.FindProperty("openOffset").vector3Value = openOffset;
            so.FindProperty("alarm").boolValue = alarm;
        });
        return gate;
    }

    /// <summary>
    /// A panel that slides (the ENFILADE tower's doors, TowerMap): solid, out of the walk-mesh bake and carving the mesh
    /// where it stands at run time, as a gate does, so it keeps zombies in while shut. What moves it is up to the caller.
    /// </summary>
    public GameObject SlidingPanel(string name, float x0, float x1, float y0, float y1, float z0, float z1, Material material = null,
        string surface = null)
    {
        var panel = Box(name, x0, x1, y0, y1, z0, z1, material, surface);
        Moving(panel);
        panel.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var obstacle = panel.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = Vector3.one;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        return panel;
    }

    /// <summary>A solid shape that hides nothing behind it: never an occluder in the culling bake (glass).</summary>
    public GameObject SeeThrough(GameObject shape)
    {
        if (shape != null) seeThrough.Add(shape);
        return shape;
    }

    /// <summary>A top-level group of the scene's hierarchy, for a map's own fixtures.</summary>
    public Transform Group(string name) => Root(name);

    /// <summary>An object under the scene's systems (a map's own run).</summary>
    public GameObject AddSystem(string name)
    {
        var system = new GameObject(name);
        system.transform.SetParent(systems, false);
        return system;
    }

    /// <summary>A material by its path under Art/Materials.</summary>
    public static Material MaterialAt(string path) => Load(path);

    /// <summary>Sets serialized fields of a component made in code.</summary>
    public static void Configure(Object target, System.Action<SerializedObject> change) => Set(target, change);

    /// <summary>A rest stop's sliding door in a doorway (CampaignDoor), shutting the Checkpoint step at
    /// <paramref name="spot"/>; it slides by <paramref name="openOffset"/> into the wall beside it. Like a gate, it stays
    /// out of the walk-mesh bake and carves the mesh at run time.</summary>
    public void Door(string name, float x0, float x1, float y0, float y1, float z0, float z1, string spot, Vector3 openOffset)
    {
        var door = Box(name, x0, x1, y0, y1, z0, z1, Wood, "WoodPanel");
        Moving(door);
        door.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var obstacle = door.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = Vector3.one;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        var slider = door.AddComponent<CampaignDoor>();
        Set(slider, so =>
        {
            so.FindProperty("spot").stringValue = spot;
            so.FindProperty("openOffset").vector3Value = openOffset;
        });
        doors.Add(slider);
    }

    /// <summary>A fuel can for a Collect step, standing on <paramref name="floor"/> (CampaignCans). No collider: it is
    /// picked up by reach, and carried it must not shove its carrier.</summary>
    public void Can(Vector3 floor)
    {
        var can = GameObject.CreatePrimitive(PrimitiveType.Cube);
        can.name = "연료통 " + (cans.Count + 1);
        Object.DestroyImmediate(can.GetComponent<Collider>());
        can.transform.SetParent(parkour, false);
        can.transform.position = floor + Vector3.up * .21f;
        can.transform.localScale = new Vector3(.3f, .42f, .2f);
        can.GetComponent<Renderer>().sharedMaterial = Marker;
        cans.Add(can.transform);
    }

    /// <summary>A rest stop's shop (ShopMenu, stage 6B): where to stand to open it, in front of a counter, with a sign over
    /// the counter at <paramref name="sign"/>.</summary>
    public void Shop(Vector3 stand, Vector3 sign)
    {
        var place = new GameObject("상점 자리").transform;
        place.SetParent(progress, false);
        place.position = stand;
        shops.Add(place);
        Box("상점 표지", sign.x - .6f, sign.x + .6f, sign.y, sign.y + .3f, sign.z - .05f, sign.z + .05f, Marker, solid: false);
    }

    /// <summary>
    /// A field pickup's place on <paramref name="floor"/> (stage 6D, PickupSpot): what lies there is rolled from
    /// Tables/Pickups each run, only guns, only supplies (ammo, heals, shields, money) or anything.
    /// </summary>
    public void Pickup(Vector3 floor, PickupSpot.Roll kind = PickupSpot.Roll.Supply)
    {
        string name = kind == PickupSpot.Roll.Gun ? "총 자리 " : kind == PickupSpot.Roll.Supply ? "보급 자리 " : "줍기 자리 ";
        var spot = new GameObject(name + ++pickupCount, typeof(PickupSpot));
        spot.transform.SetParent(pickups, false);
        spot.transform.position = floor + Vector3.up * Lift;
        spot.GetComponent<PickupSpot>().Kind = kind;
    }

    /// <summary>
    /// An area with its own background sound (AmbienceArea), a box by its extents: inside it the map's ambience gives way
    /// to <paramref name="sound"/>, a row id in Tables/Ambience. Where boxes overlap the higher
    /// <paramref name="priority"/> wins, then the smaller box, so a room inside a street area takes over there.
    /// </summary>
    public void Ambience(string sound, float x0, float x1, float y0, float y1, float z0, float z1, int priority = 1)
    {
        ambience ??= Root("◤━━ 환경음 구역 (Tables/Ambience의 id)");
        var area = new GameObject("환경음 " + sound).AddComponent<AmbienceArea>();
        area.transform.SetParent(ambience, false);
        area.transform.localPosition = new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, (z0 + z1) * .5f);
        Set(area, so =>
        {
            so.FindProperty("size").vector3Value = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
            so.FindProperty("sound").stringValue = sound;
            so.FindProperty("priority").intValue = priority;
        });
    }

    /// <summary>A warm lamp with no shadows, for underground stretches the sun can't reach.</summary>
    public void Lamp(Vector3 position, float range = 14f, float intensity = 2.2f)
    {
        var lamp = new GameObject("등", typeof(Light));
        lamp.transform.SetParent(parkour, false);
        lamp.transform.position = position;
        var light = lamp.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(1f, .86f, .64f);
        light.shadows = LightShadows.None;
    }

    /// <summary>A slab whose top runs from <paramref name="bottom"/> to <paramref name="top"/> (centre line), for stairs
    /// the enemies can walk.</summary>
    public GameObject Ramp(string name, Vector3 bottom, Vector3 top, float width, Material material = null, float thickness = .3f)
    {
        Vector3 along = top - bottom;
        var turn = Quaternion.LookRotation(along.normalized, Vector3.up);
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = name;
        ramp.transform.SetParent(terrain, false);
        ramp.transform.SetPositionAndRotation((bottom + top) * .5f - turn * Vector3.up * (thickness * .5f), turn);
        ramp.transform.localScale = new Vector3(width, thickness, along.magnitude);
        Dress(ramp, material, null, true);
        return ramp;
    }

    /// <summary>A ladder from <paramref name="foot"/> (in the plane of its rungs, 0.15 m or so off the wall) up to the
    /// floor it leads to, the climber standing on the <paramref name="outwardYaw"/> side and stepping off the other way at
    /// the top. How high it goes is measured once the layout stands (Finish, LadderFit): its rails end flush with that
    /// floor.</summary>
    public void Ladder(string name, Vector3 foot, float outwardYaw) => Fixture<Ladder>(name, foot, outwardYaw);

    /// <summary>A cable from <paramref name="from"/> to <paramref name="to"/>, ridden that way only unless two-way.</summary>
    public void Zipline(string name, Vector3 from, Vector3 to, bool twoWay = false)
    {
        var line = Fixture<Zipline>(name, from, 0f);
        Set(line, so =>
        {
            so.FindProperty("end").vector3Value = to - from;
            so.FindProperty("twoWay").boolValue = twoWay;
        });
    }

    /// <summary>A swing bar across the way, swung along <paramref name="swingYaw"/>, with a pipe to look at.</summary>
    public void Bar(string name, Vector3 centre, float swingYaw, float width)
    {
        // SwingBar: local X is the bar, local Z the swing; yaw 90 swings along +X with the bar along Z.
        var bar = Fixture<SwingBar>(name, centre, swingYaw);
        Set(bar, so => so.FindProperty("gripWidth").floatValue = width);
        var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pipe.name = "관";
        Object.DestroyImmediate(pipe.GetComponent<Collider>());
        pipe.transform.SetParent(bar.transform, false);
        pipe.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        pipe.transform.localScale = new Vector3(.07f, width * .5f + .3f, .07f);
        pipe.GetComponent<Renderer>().sharedMaterial = Metal;
    }

    /// <summary>Where the player (or, online, everyone) starts, facing <paramref name="yaw"/>.</summary>
    public void PlayerStart(Vector3 feet, float yaw)
    {
        var start = new GameObject("플레이어 시작", typeof(PlayerSpawnPoint));
        start.transform.SetParent(systems, false);
        start.transform.SetPositionAndRotation(feet + Vector3.up * Lift, Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>A place enemies come from (the waves now, the director from 5D).</summary>
    public void EnemySpawn(string name, Vector3 feet)
    {
        var point = new GameObject(name).transform;
        point.SetParent(enemies, false);
        point.position = feet + Vector3.up * Lift;
        enemySpawns.Add(point);
    }

    /// <summary>A named spot along the way, where a step of Tables/CampaignSteps sends the players (its spot column is
    /// this name; "Start" is where the dead come back before the first rest stop). Gimmicks (5C) hang on them too. Put on
    /// a moving <paramref name="group"/> (Pushback), it goes along.</summary>
    public void Spot(string name, Vector3 position, Transform group = null)
    {
        var spot = new GameObject(name);
        spot.transform.SetParent(group != null ? group : progress, false);
        spot.transform.position = position;
        spots.Add(spot.transform);
        var icon = EditorGUIUtility.IconContent("sv_label_1").image as Texture2D;
        if (icon != null) EditorGUIUtility.SetIconForObject(spot, icon);
    }

    // How far down a zombie drops off a ledge and how wide a gap it hops (m): the overpass deck (8 m) over the road
    // counts, the rooftops (12 m and more) over the street don't.
    private const float DropHeight = 9f, JumpGap = 1.6f;

    // The project's walk-mesh agent (type 0, what the zombies use) gets links down ledges and across gaps.
    private static void AllowDrops()
    {
        var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
        var agents = settings.FindProperty("m_Settings");
        for (int i = 0; agents != null && i < agents.arraySize; i++)
        {
            var agent = agents.GetArrayElementAtIndex(i);
            if (agent.FindPropertyRelative("agentTypeID").intValue != 0) continue;
            agent.FindPropertyRelative("ledgeDropHeight").floatValue = DropHeight;
            agent.FindPropertyRelative("maxJumpAcrossDistance").floatValue = JumpGap;
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Adds the waves, bakes the walk mesh, marks what never moves static, saves the scene, bakes its occlusion
    /// culling and puts it in the build.</summary>
    public void Finish(string summary)
    {
        // Every box is laid now, so each ladder can be fitted to the floor it leads to.
        string ladders = LadderFit.FitAll(parkour);
        AddWaves();
        if (AddsCampaign) AddCampaign();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
        string dataFolder = Folder + "/" + sceneName;
        if (!AssetDatabase.IsValidFolder(dataFolder)) AssetDatabase.CreateFolder(Folder, sceneName);
        string look = ApplyLook();
        var surface = terrain.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        // Memo 48: small tops keep their walk mesh (Unity's default dropped every region under 2 m²), also when the
        // surface is baked again from its own button.
        surface.minRegionArea = TopRegionArea;
        // Friend feedback C5: zombies drop off ledges and hop small gaps to reach players (ZombieDrop walks the links),
        // so no ledge is a safe spot.
        AllowDrops();
        var links = new SerializedObject(surface);
        links.FindProperty("m_GenerateLinks").boolValue = true;
        links.ApplyModifiedPropertiesWithoutUndo();
        int slabs = BakeWalkMesh(surface);
        string meshPath = dataFolder + "/NavMesh.asset";
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        if (surface.navMeshData != null) AssetDatabase.CreateAsset(surface.navMeshData, meshPath);
        int stranded = CheckProps();
        MarkStatic();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        // The occlusion data is kept beside the saved scene (Scenes/Campaign/<scene>/OcclusionCullingData), so it bakes
        // after the first save, and the scene is saved again to keep hold of it.
        bool culled = BakeOcclusion();
        // The sky's ambient light and reflection are baked the same way, beside the saved scene (LightingData).
        bool lit = look != null && BakeEnvironment();
        if (culled || lit)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        var build = EditorBuildSettings.scenes.ToList();
        if (!build.Any(entry => entry.path == ScenePath))
        {
            build.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = build.ToArray();
        }
        AssetDatabase.SaveAssets();
        // A map with its own run (the ENFILADE tower) has no campaign steps or spawn spots to report.
        Tell(summary + "\n\n" + (AddsCampaign ? "진행 자리 " + spots.Count + "곳, 적 나오는 곳 " + enemySpawns.Count +
             "곳. Play하면 미션이 바로 시작돼요(목표는 Resources/Tables/CampaignSteps.csv).\n" : "") +
             "작은 소품: 좁은 꼭대기 " + slabs + "곳에 좀비 길을 깔았어요. " +
             (stranded == 0 ? "좀비가 못 가는 소품은 없어요." : "좀비가 못 가는 소품이 " + stranded + "곳 남았어요(콘솔에 이름과 자리).") +
             (ladders.Length > 0 ? "\n" + ladders : "") +
             "\n가림 굽기(오클루전 컬링): " + (culled ? "구웠어요. 벽과 건물 뒤의 좀비는 그리지 않아요." : "실패했어요(콘솔을 보세요). 맵은 그대로 쓸 수 있어요.") +
             "\n하늘·해·안개: " + (look == null ? "Tables/MapLook에 이 장의 줄이 없어 기본 하늘이에요."
                 : look + (lit ? " (환경광·반사를 구웠어요)" : " (환경광을 굽지 못했어요. Window > Rendering > Lighting의 Generate Lighting을 눌러 주세요)")) +
             SurfaceReport() + DressReport());
    }

    // ---- Culling (the user's memo 40: "최적화, 컬링") ----
    // What of the layout never moves is static: batched into a few draws per material (Batching Static), hiding what is
    // behind it from the camera (Occluder Static, solid boxes) and left undrawn while something hides it (Occludee
    // Static). Gates, doors and pushback groups move, so they stay dynamic. The occlusion data is baked from these, and
    // anything that moves (zombies, players, pickups, effects) is culled against it at run time as well, since every
    // renderer allows dynamic occlusion unless it says otherwise: a horde behind a building or a tunnel wall costs no
    // drawing. No Contribute GI: nothing is lightmapped, and it would make the editor start baking lights.
    // Smaller occluders than this are not used to hide anything, and the gaps it may see through (m); larger values bake
    // faster and coarser. The maps are big boxes, so a coarse bake is enough.
    private const float SmallestOccluder = 4f, SmallestHole = .25f, BackfaceThreshold = 100f;

    private void MarkStatic()
    {
        // Dressing (GreyboxBuilder.Dress): batched and hidden behind walls, but too small and too varied to hide much.
        if (dressing != null)
            foreach (var shape in dressing.GetComponentsInChildren<Renderer>())
                GameObjectUtility.SetStaticEditorFlags(shape.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        foreach (var shape in terrain.GetComponentsInChildren<Renderer>())
        {
            var part = shape.gameObject;
            if (part.GetComponentInParent<CampaignGate>() != null || part.GetComponentInParent<CampaignDoor>() != null
                || part.GetComponentInParent<CampaignPushback>() != null || part.GetComponentInParent<TowerDoor>() != null)
            {
                // It moves: its surface is mapped in its own space, or the texture would slide over it as it goes.
                shape.sharedMaterials = MovingMaterials(shape.sharedMaterials);
                continue;
            }
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
            // A dressed box's grey look is hidden (GreyboxBuilder.Dress): its model may not fill it, so it hides nothing.
            // Glass is seen through: as an occluder it would hide the room behind it (the tower's lanes).
            if (shape.enabled && part.TryGetComponent(out Collider _) && !seeThrough.Contains(part)) flags |= StaticEditorFlags.OccluderStatic;
            GameObjectUtility.SetStaticEditorFlags(part, flags);
        }
    }

    private static bool BakeOcclusion()
    {
        StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
        StaticOcclusionCulling.smallestHole = SmallestHole;
        StaticOcclusionCulling.backfaceThreshold = BackfaceThreshold;
        bool done = StaticOcclusionCulling.Compute();
        if (!done) Debug.LogWarning("[Greybox] occlusion culling did not bake; the scene still works, only without it.");
        return done;
    }

    // ---- Small props (the user's memo 48: "좀비가 못 올라가는 작은 소품") ----
    // The zombies' walk mesh (agent type 0: radius .5 m, step .75 m) has nothing on a top narrower than about twice the
    // radius, and Unity dropped every walk region under 2 m², so a 2×2 m box's top had none either. ZombieDrop climbs
    // only onto walk mesh, so a player standing on such a prop was beyond every zombie. Now: small regions stay
    // (TopRegionArea); a narrow top a zombie could climb to gets a thin walk slab in the bake only (no collider, so
    // players and shots never meet it); a top low enough is within a zombie's arm from the floor anyway; and CheckProps
    // names in the console whatever is still out of reach, so the layout can be changed (lower it to ArmHeight, or
    // past AgentHeight so a slab can go on it).
    private const float AgentRadius = .5f, AgentHeight = 2f, StepHeight = .75f, PlayerHeight = 1.8f;
    // A zombie's arm reaches a player standing on a narrow top up to this high over the floor it stands on
    // (EnemyCombatAI.attackRange 2 m, feet to feet, from the edge of its walk mesh); ZombieDrop climbs at most
    // ClimbMost and only toward a player within ClimbNear across (m).
    private const float ArmHeight = 1.5f, ClimbMost = 4.5f, ClimbNear = 2.6f;
    // The smallest walk region kept (m²), the width of a narrow top's walk strip (m) and of its slab (m).
    private const float TopRegionArea = .25f, NarrowStrip = .5f, SlabThickness = .05f;
    // Tops larger than this are places (roofs, decks, floors) with their own ways up, not props (m²).
    private const float PropArea = 60f;

    // The walk mesh from the terrain's colliders as NavMeshSurface.BuildNavMesh makes it (the same modifiers, so gates
    // and doors stay out of it, and drop links), plus the narrow props' slabs. Kept in the surface, so the scene loads it
    // as before. Returns how many slabs went in.
    private int BakeWalkMesh(NavMeshSurface surface)
    {
        var markups = new List<NavMeshBuildMarkup>();
        foreach (var modifier in terrain.GetComponentsInChildren<NavMeshModifier>())
        {
            if (!modifier.isActiveAndEnabled || !modifier.AffectsAgentType(surface.agentTypeID)) continue;
            markups.Add(new NavMeshBuildMarkup
            {
                root = modifier.transform, overrideArea = modifier.overrideArea, area = modifier.area,
                ignoreFromBuild = modifier.ignoreFromBuild, applyToChildren = modifier.applyToChildren,
                overrideGenerateLinks = modifier.overrideGenerateLinks, generateLinks = modifier.generateLinks
            });
        }
        var sources = new List<NavMeshBuildSource>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(terrain, surface.layerMask, surface.useGeometry, surface.defaultArea, true,
            markups, false, sources);
        int slabs = AddNarrowTops(sources);
        var bounds = new Bounds();
        bool any = false;
        foreach (var shape in terrain.GetComponentsInChildren<Renderer>())
        {
            if (any) bounds.Encapsulate(shape.bounds);
            else { bounds = shape.bounds; any = true; }
        }
        bounds.Expand(4f);
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(surface.GetBuildSettings(), sources, bounds, terrain.position, terrain.rotation);
        surface.RemoveData();
        surface.navMeshData = data;
        if (data == null) return slabs;
        data.name = terrain.name;
        // Added now, so CheckProps can ask the walk mesh where a zombie can go.
        surface.AddData();
        return slabs;
    }

    // A walk slab over each narrow top a zombie could climb to: AgentHeight to ClimbMost over its floor (a slab lower
    // than AgentHeight would also shut the floor beside the prop to the zombies, since its overhang is too low to
    // stand under), room above it to stand, too narrow or too small for walk mesh of its own. The slab is wide enough
    // that NarrowStrip is left after the bake trims the agent's radius off its edges, and drop links come off it.
    private int AddNarrowTops(List<NavMeshBuildSource> sources)
    {
        int added = 0;
        foreach (var prop in blocks)
        {
            if (prop.Moves || !IsProp(prop) || Headroom(prop) < AgentHeight) continue;
            float height = prop.Box.max.y - FloorUnder(prop);
            if (height < AgentHeight || height > ClimbMost || HasRoomToWalk(prop)) continue;
            Vector3 size = prop.Box.size, top = prop.Box.center + Vector3.up * (size.y * .5f - SlabThickness * .5f);
            float minimum = 2f * AgentRadius + NarrowStrip;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(Mathf.Max(size.x, minimum), SlabThickness, Mathf.Max(size.z, minimum)),
                transform = Matrix4x4.TRS(top, Quaternion.identity, Vector3.one),
                area = 0,
                generateLinks = true
            });
            added++;
        }
        return added;
    }

    // After the bake: every prop a player can stand on and no zombie can get to, named in the console with the prop as
    // its context (click the line to find it). Returns how many.
    private int CheckProps()
    {
        int stranded = 0;
        var wide = new List<string>();
        foreach (var prop in blocks)
        {
            if (prop.Moves || !IsProp(prop) || Headroom(prop) < PlayerHeight) continue;
            float floor = FloorUnder(prop), top = prop.Box.max.y, height = top - floor;
            if (height <= ArmHeight || height > ClimbMost) continue;
            Vector3 centre = new Vector3(prop.Box.center.x, top, prop.Box.center.z);
            bool walk = NavMesh.SamplePosition(centre + Vector3.up * .1f, out NavMeshHit hit, .8f, NavMesh.AllAreas)
                        && Mathf.Abs(hit.position.y - top) < .3f;
            if (!walk)
            {
                stranded++;
                string why = Headroom(prop) < AgentHeight ? "위가 2 m보다 낮게 막혀 좀비가 설 수 없어요"
                    : height < AgentHeight ? "높이 1.5~2 m의 좁은 꼭대기: 1.5 m 이하로 낮추거나 2 m 넘게 높이세요"
                    : "꼭대기에 좀비 길이 생기지 않았어요(옆의 더 높은 벽에 붙어 있으면 그래요)";
                Debug.LogWarning("[Greybox] " + sceneName + ": 좀비가 못 가는 소품 '" + prop.Shape.name + "' (" + Format(centre) +
                                 ", 바닥에서 " + height.ToString("0.0") + " m): " + why + ".", prop.Shape);
                continue;
            }
            // A top so wide that a player in its middle stands farther than ClimbNear from any zombie at its foot.
            if (Mathf.Min(prop.Box.size.x, prop.Box.size.z) * .5f + AgentRadius > ClimbNear) wide.Add(prop.Shape.name);
        }
        if (wide.Count > 0)
            Debug.Log("[Greybox] " + sceneName + ": 가운데가 좀비 기어오르기 거리(ZombieDrop " + ClimbNear + " m) 밖인 넓은 꼭대기 " +
                      wide.Count + "곳: " + string.Join(", ", wide) + ". 가장자리로 오면 좀비가 올라와요.");
        return stranded;
    }

    private static string Format(Vector3 at) => at.x.ToString("0.#") + ", " + at.y.ToString("0.#") + ", " + at.z.ToString("0.#");

    // Small enough to be a prop, and higher than a step over its floor.
    private bool IsProp(Block prop)
        => prop.Box.size.x * prop.Box.size.z <= PropArea && prop.Box.max.y - FloorUnder(prop) > StepHeight;

    // Its top keeps walk mesh of its own: NarrowStrip both ways and more than the smallest region once the bake has
    // trimmed the agent's radius off every edge.
    private static bool HasRoomToWalk(Block prop)
    {
        float x = prop.Box.size.x - 2f * AgentRadius, z = prop.Box.size.z - 2f * AgentRadius;
        return x >= NarrowStrip && z >= NarrowStrip && x * z >= 2f * TopRegionArea;
    }

    // The highest top of the layout under this block, at or below its bottom: what it stands on (0, the ground, if none).
    private float FloorUnder(Block prop)
    {
        float floor = 0f;
        foreach (var other in blocks)
            if (other != prop && !other.Moves && other.Box.max.y <= prop.Box.min.y + .02f && Overlaps(prop.Box, other.Box))
                floor = Mathf.Max(floor, other.Box.max.y);
        return floor;
    }

    // How much room there is over its top before something else of the layout (m; very much if nothing).
    private float Headroom(Block prop)
    {
        float top = prop.Box.max.y, room = 100f;
        foreach (var other in blocks)
            if (other != prop && other.Box.min.y >= top - .02f && Overlaps(prop.Box, other.Box))
                room = Mathf.Min(room, other.Box.min.y - top);
        return room;
    }

    // The two footprints share some floor, more than a touch along an edge.
    private static bool Overlaps(Bounds a, Bounds b)
        => a.min.x < b.max.x - .05f && a.max.x > b.min.x + .05f && a.min.z < b.max.z - .05f && a.max.z > b.min.z + .05f;

    private void AddWaves()
    {
        var waves = new GameObject("웨이브", typeof(WaveManager));
        waves.transform.SetParent(systems, false);
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        Set(waves.GetComponent<WaveManager>(), so =>
        {
            so.FindProperty("enemyPrefab").objectReferenceValue = enemy != null ? enemy.GetComponent<EnemyHealth>() : null;
            var points = so.FindProperty("spawnPoints");
            points.arraySize = enemySpawns.Count;
            for (int i = 0; i < enemySpawns.Count; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = enemySpawns[i];
            so.FindProperty("autoStart").boolValue = false;
        });
    }

    // The chapter's run of objectives (CampaignRun): its steps come from Tables/CampaignSteps by the scene's name.
    private void AddCampaign()
    {
        var run = new GameObject("캠페인 진행", typeof(CampaignRun));
        run.transform.SetParent(systems, false);
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        Set(run.GetComponent<CampaignRun>(), so =>
        {
            so.FindProperty("enemyPrefab").objectReferenceValue = enemy != null ? enemy.GetComponent<EnemyHealth>() : null;
            Fill(so.FindProperty("spots"), spots);
            Fill(so.FindProperty("spawns"), enemySpawns);
            Fill(so.FindProperty("cans"), cans);
            Fill(so.FindProperty("doors"), doors);
            Fill(so.FindProperty("shops"), shops);
            // A shot fuel can's blast sound (friend feedback C17); the blast and its fire are drawn in code (CanBlastEffect).
            so.FindProperty("canBlastSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(CanBlastSound);
        });
    }

    private const string CanBlastSound = "Assets/ThirdParty/Vefects/Combat Flipbook VFX URP/Audio/WAV/Sergi/SFX_Vefects_Explosion_01_One_Shot_01.wav";

    private static void Fill<T>(SerializedProperty array, List<T> items) where T : Object
    {
        array.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private void Dress(GameObject shape, Material material, string surface, bool solid)
    {
        shape.GetComponent<Renderer>().sharedMaterial = material != null ? material : Concrete;
        if (!solid) Object.DestroyImmediate(shape.GetComponent<Collider>());
        if (string.IsNullOrEmpty(surface)) return;
        Set(shape.AddComponent<SurfaceType>(), so => so.FindProperty("surface").stringValue = surface);
    }

    private T Fixture<T>(string name, Vector3 position, float yaw) where T : Component
    {
        var fixture = new GameObject(name, typeof(T));
        fixture.transform.SetParent(parkour, false);
        fixture.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return fixture.GetComponent<T>();
    }

    private static void Set(Object target, System.Action<SerializedObject> change)
    {
        var so = new SerializedObject(target);
        change(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private Transform Root(string name)
    {
        var root = new GameObject(name).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        return root;
    }

    private static Material Load(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + path);
        if (material == null) Debug.LogWarning("[Greybox] material not found: " + Materials + path);
        return material;
    }

    // A window now, or while RebuildAll runs, a paragraph of its closing window.
    private void Tell(string message)
    {
        if (batch != null) batch.Append("■ ").Append(title).Append('\n').Append(message).Append("\n\n");
        else EditorUtility.DisplayDialog(title, message, "확인");
    }
}
