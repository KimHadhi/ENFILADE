using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// </summary>
public sealed class GreyboxBuilder
{
    public const string Folder = "Assets/_Project/Scenes/Campaign";
    private const string HudPrefab = "Assets/_Project/Prefabs/UI/HUD/GameHUD.prefab";
    private const string EnemyPrefab = "Assets/_Project/Resources/Enemies/Enemy_Test.prefab";
    private const string Materials = "Assets/_Project/Art/Materials/";
    // Spawn points sit this far over what they stand on, so nothing starts inside the floor (m).
    private const float Lift = .05f;

    private readonly string sceneName, title;
    private readonly List<Transform> enemySpawns = new List<Transform>(), spots = new List<Transform>(), cans = new List<Transform>(),
        shops = new List<Transform>();
    private readonly List<CampaignDoor> doors = new List<CampaignDoor>();
    private Scene scene;
    private Transform terrain, parkour, enemies, progress, systems;

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

    /// <summary>Asks before rebuilding a scene that exists, saves what is open, and starts the new scene. Null if called off.</summary>
    public static GreyboxBuilder Begin(string sceneName, string title)
    {
        var builder = new GreyboxBuilder(sceneName, title);
        if (EditorApplication.isPlayingOrWillChangePlaymode) { builder.Tell("Play를 멈춘 뒤에 실행해 주세요."); return null; }
        if (File.Exists(builder.ScenePath) && !EditorUtility.DisplayDialog(title,
                builder.ScenePath + " 가 이미 있어요. 다시 지으면 씬에서 손으로 고친 것이 사라져요. 다시 지을까요?", "다시 짓기", "취소"))
            return null;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
        builder.Start();
        return builder;
    }

    private void Start()
    {
        scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        // The player brings its own camera; the scene's light becomes the sun.
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<Camera>() != null) { Object.DestroyImmediate(root); continue; }
            if (!root.TryGetComponent(out Light sun)) continue;
            sun.name = "해";
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
        }
        Concrete = Load("MovementPlayground.mat");
        Road = Load("SurfacePads/SurfacePad_ConcreteGrit.mat");
        Metal = Load("SurfacePads/SurfacePad_Metal.mat");
        Wood = Load("SurfacePads/SurfacePad_Wood.mat");
        Marker = Load("TestRedMat.mat");
        terrain = Root("◤━━ 지형 (길찾기 굽는 곳)");
        parkour = Root("◤━━ 사다리·집라인·철봉");
        enemies = Root("◤━━ 적 나오는 곳");
        progress = Root("◤━━ 진행 자리 (CampaignSteps.csv의 spot 이름)");
        systems = Root("◤━━ 시스템");
        var input = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        input.transform.SetParent(systems, false);
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
        if (hud != null) PrefabUtility.InstantiatePrefab(hud, scene);
        else Debug.LogWarning("[Greybox] " + HudPrefab + " not found; the scene has no HUD.");
    }

    /// <summary>A solid block (or, with <paramref name="solid"/> off, a look only: no collider, no walk mesh).</summary>
    public GameObject Box(string name, float x0, float x1, float y0, float y1, float z0, float z1,
        Material material = null, string surface = null, bool solid = true)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(terrain, false);
        box.transform.localPosition = new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, (z0 + z1) * .5f);
        box.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        Dress(box, material, surface, solid);
        return box;
    }

    /// <summary>A solid panel that a campaign step opens (CampaignGate), moving by <paramref name="openOffset"/>. It stays
    /// out of the walk-mesh bake and carves the mesh where it stands at run time, so enemies pass once it opens.</summary>
    public GameObject Gate(string name, float x0, float x1, float y0, float y1, float z0, float z1, string step, Vector3 openOffset,
        Material material = null, string surface = null, bool alarm = true)
    {
        var gate = Box(name, x0, x1, y0, y1, z0, z1, material, surface);
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

    /// <summary>A rest stop's sliding door in a doorway (CampaignDoor), shutting the Checkpoint step at
    /// <paramref name="spot"/>; it slides by <paramref name="openOffset"/> into the wall beside it. Like a gate, it stays
    /// out of the walk-mesh bake and carves the mesh at run time.</summary>
    public void Door(string name, float x0, float x1, float y0, float y1, float z0, float z1, string spot, Vector3 openOffset)
    {
        var door = Box(name, x0, x1, y0, y1, z0, z1, Wood, "WoodPanel");
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

    /// <summary>A ladder from <paramref name="foot"/> up <paramref name="height"/> (past the roof it reaches), the climber
    /// standing on the <paramref name="outwardYaw"/> side and stepping off the other way at the top.</summary>
    public void Ladder(string name, Vector3 foot, float outwardYaw, float height)
    {
        var ladder = Fixture<Ladder>(name, foot, outwardYaw);
        Set(ladder, so => so.FindProperty("height").floatValue = height);
    }

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
    /// this name; "Start" is where the dead come back before the first rest stop). Gimmicks (5C) hang on them too.</summary>
    public void Spot(string name, Vector3 position)
    {
        var spot = new GameObject(name);
        spot.transform.SetParent(progress, false);
        spot.transform.position = position;
        spots.Add(spot.transform);
        var icon = EditorGUIUtility.IconContent("sv_label_1").image as Texture2D;
        if (icon != null) EditorGUIUtility.SetIconForObject(spot, icon);
    }

    /// <summary>Adds the waves, bakes the walk mesh, saves the scene and puts it in the build.</summary>
    public void Finish(string summary)
    {
        AddWaves();
        AddCampaign();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
        string dataFolder = Folder + "/" + sceneName;
        if (!AssetDatabase.IsValidFolder(dataFolder)) AssetDatabase.CreateFolder(Folder, sceneName);
        var surface = terrain.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        string meshPath = dataFolder + "/NavMesh.asset";
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        if (surface.navMeshData != null) AssetDatabase.CreateAsset(surface.navMeshData, meshPath);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        var build = EditorBuildSettings.scenes.ToList();
        if (!build.Any(entry => entry.path == ScenePath))
        {
            build.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = build.ToArray();
        }
        AssetDatabase.SaveAssets();
        Tell(summary + "\n\n진행 자리 " + spots.Count + "곳, 적 나오는 곳 " + enemySpawns.Count +
             "곳. Play하면 미션이 바로 시작돼요(목표는 Resources/Tables/CampaignSteps.csv).");
    }

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
        });
    }

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

    private void Tell(string message) => EditorUtility.DisplayDialog(title, message, "확인");
}
