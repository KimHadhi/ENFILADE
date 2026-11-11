using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the hub (memo 46, the user's answer: a walkable 3D hideout where guns can be fired, working like a room
/// lobby) as grey blocks from code, as the chapters are built (GreyboxBuilder; it keeps its own small helpers because a
/// chapter's builder saves under Scenes/Campaign and puts a chapter's run in it). Tools > World > 허브 로비 짓기 makes
/// Scenes/Hub/Hub.unity and puts it in the build; running it again rebuilds it from scratch (hand edits are lost: change
/// the layout here).
/// The layout, +Z north, metres:
/// - The hideout, a warehouse 24 x 24 with a skylight: the start in its south half; the map table in the middle with the
///   next chapter's sign hanging over it; the class wall (lockers) on the west wall under a catwalk, reached by two crates
///   and a ladder, with a zipline from it across the hall; the stage on the east side (the results' lineup, HubStage)
///   with lights on it; the way out in the north wall (a shutter that rises with the count, the board over it, the
///   ready button beside it); the gun rack on the south wall by the range's opening; a low wall to vault.
/// - The firing range through the south wall, open air, 14 wide and 108 long: a firing line counter, an ammo crate,
///   four lanes (a close crowd of three zombie dummies; one at 15 m and one moving across at 30 m; one at 25 m and a
///   steel plate at 50 m; one at 50 m and a plate at 100 m), distance marks on the floor and the wall, and the range
///   board over the lanes. The stations are filled from the tables at play (HubLobby), so nothing here needs a rebuild
///   when a chapter, class or gun is added.
/// </summary>
public static class HubBuilder
{
    private const string Folder = "Assets/_Project/Scenes/Hub";
    private const string ScenePath = Folder + "/Hub.unity";
    private const string HudPrefab = "Assets/_Project/Prefabs/UI/HUD/GameHUD.prefab";
    private const string Materials = "Assets/_Project/Art/Materials/";
    private const string Title = "허브 로비";
    private const float Lift = .05f;

    private static Scene scene;
    private static Transform terrain, fixtures, stations, range, systems, lights;
    private static Material concrete, road, metal, wood, marker;

    [MenuItem("Tools/World/허브 로비 짓기")]
    private static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Tell("Play를 멈춘 뒤에 실행해 주세요."); return; }
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog(Title,
                ScenePath + " 가 이미 있어요. 다시 지으면 씬에서 손으로 고친 것이 사라져요. 다시 지을까요?", "다시 짓기", "취소"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Begin();
        Hideout();
        Range();
        Finish();
    }

    private static void Begin()
    {
        scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            // The player brings its own camera; the scene's light becomes the sun.
            if (root.GetComponent<Camera>() != null) { Object.DestroyImmediate(root); continue; }
            if (!root.TryGetComponent(out Light sun)) continue;
            sun.name = "해";
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
        }
        concrete = Load("MovementPlayground.mat");
        road = Load("SurfacePads/SurfacePad_ConcreteGrit.mat");
        metal = Load("SurfacePads/SurfacePad_Metal.mat");
        wood = Load("SurfacePads/SurfacePad_Wood.mat");
        marker = Load("TestRedMat.mat");
        terrain = Root("◤━━ 은신처 (지형)");
        fixtures = Root("◤━━ 사다리·집라인");
        stations = Root("◤━━ 허브 자리 (HubStation: 지도 탁자·클래스·총·준비)");
        range = Root("◤━━ 사격장 과녁 (RangeTarget)");
        lights = Root("◤━━ 조명");
        systems = Root("◤━━ 시스템");
        var input = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        input.transform.SetParent(systems, false);
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
        if (hud != null) PrefabUtility.InstantiatePrefab(hud, scene);
        else Debug.LogWarning("[Hub] " + HudPrefab + " not found; the hub has no HUD.");
        var lobby = new GameObject("허브 진행", typeof(HubLobby));
        lobby.transform.SetParent(systems, false);
        var start = new GameObject("플레이어 시작", typeof(PlayerSpawnPoint));
        start.transform.SetParent(systems, false);
        // In a room the others start 1.5 m apart to the right of this one (GameSession), ten of them inside the hall.
        start.transform.SetPositionAndRotation(new Vector3(-6f, Lift, -6f), Quaternion.identity);
    }

    // ---- The hideout ----

    private static void Hideout()
    {
        Box("땅", -40f, 40f, -.5f, 0f, -132f, 40f, road, "Asphalt");
        // Walls, 7 m, with the way out in the north wall and the range's opening in the south wall.
        Box("서쪽 벽", -12.3f, -12f, 0f, 7f, -10.3f, 14.3f);
        Box("동쪽 벽", 12f, 12.3f, 0f, 7f, -10.3f, 14.3f);
        Box("북쪽 벽 서", -12.3f, -2.5f, 0f, 7f, 14f, 14.3f);
        Box("북쪽 벽 동", 2.5f, 12.3f, 0f, 7f, 14f, 14.3f);
        Box("북쪽 문 위", -2.5f, 2.5f, 4.5f, 7f, 14f, 14.3f);
        Box("남쪽 벽 서", -12.3f, -7.3f, 0f, 7f, -10.3f, -10f);
        Box("남쪽 벽 동", 7.3f, 12.3f, 0f, 7f, -10.3f, -10f);
        Box("사격장 입구 위", -7.3f, 7.3f, 5f, 7f, -10.3f, -10f);
        // The roof, with a skylight over the map table.
        Box("지붕 서", -12.3f, -5f, 7f, 7.3f, -10.3f, 14.3f);
        Box("지붕 동", 5f, 12.3f, 7f, 7.3f, -10.3f, 14.3f);
        Box("지붕 남", -5f, 5f, 7f, 7.3f, -10.3f, -2f);
        Box("지붕 북", -5f, 5f, 7f, 7.3f, 6f, 14.3f);
        Lamp(new Vector3(-6f, 6.2f, -4f));
        Lamp(new Vector3(6f, 6.2f, -4f));
        Lamp(new Vector3(-6f, 6.2f, 9f));
        Lamp(new Vector3(6f, 6.2f, 9f));

        // The map table, the next chapter's sign hanging over it from the roof.
        Box("지도 탁자", -2f, 2f, 0f, .92f, .8f, 3.2f, wood, "Wood");
        Station("지도 탁자 위", HubStation.Kind.MapTable, new Vector3(0f, .46f, 2f), 0f, new Vector3(4f, .92f, 2.4f));
        Station("다음 장 표지판", HubStation.Kind.ChapterSign, new Vector3(0f, 3.7f, 3.5f), 180f, new Vector3(3.2f, 1.9f, .1f));
        Box("표지판 줄 1", -1.4f, -1.37f, 4.65f, 7f, 3.48f, 3.52f, metal, solid: false);
        Box("표지판 줄 2", 1.37f, 1.4f, 4.65f, 7f, 3.48f, 3.52f, metal, solid: false);

        // The class wall: a cabinet along the west wall, a locker door per class on it (HubLobby).
        Box("클래스 사물함", -12f, -10.8f, 0f, 2.4f, -2.2f, 6.2f, metal, "Metal");
        Station("클래스 벽", HubStation.Kind.ClassWall, new Vector3(-11.4f, 1.2f, 2f), 90f, new Vector3(8.4f, 2.4f, 1.2f));
        Label("hub.sign.classes", new Vector3(-10.75f, 2.72f, 2f), Quaternion.Euler(0f, 90f, 0f), .3f, 4f, false);

        // A little parkour: two crates up to the catwalk over the class wall, a ladder down at its far end, and a
        // zipline from it across the hall to the stage's side.
        Box("나무 상자 1", -9.4f, -8.2f, 0f, .9f, -8f, -6.8f, wood, "WoodPlank");
        Box("나무 상자 2", -9.5f, -8.1f, 0f, 1.8f, -6.6f, -5.2f, wood, "WoodPlank");
        Box("캣워크", -12f, -9.6f, 3f, 3.2f, -8f, 12f, metal, "Metal");
        // The rail leaves a gap where the ladder comes up and the zipline sets off.
        Box("캣워크 난간 1", -9.7f, -9.6f, 3.2f, 4.2f, 4f, 8.8f, metal, "MetalPipe");
        Box("캣워크 난간 2", -9.7f, -9.6f, 3.2f, 4.2f, 11.2f, 12f, metal, "MetalPipe");
        Box("캣워크 기둥 1", -9.85f, -9.65f, 0f, 3f, -7.9f, -7.7f, metal, "MetalPipe");
        Box("캣워크 기둥 2", -9.85f, -9.65f, 0f, 3f, 11.7f, 11.9f, metal, "MetalPipe");
        Ladder("캣워크 사다리", new Vector3(-9.45f, 0f, 9.5f), 90f, 4.4f);
        Box("집라인 기둥 1", -10.7f, -10.5f, 3.2f, 5.9f, 9.9f, 10.1f, metal, solid: false);
        Zipline("홀 건너는 집라인", new Vector3(-10.6f, 5.4f, 10f), new Vector3(6.4f, 2.1f, 10f));
        Box("집라인 기둥 2", 6.5f, 6.7f, 0f, 2.6f, 9.9f, 10.1f, metal, solid: false);
        Box("넘는 벽", 2f, 4f, 0f, 1f, -8.2f, -7.8f, concrete, "Concrete");

        // The way out: the shutter rises with the count (HubLobby), the board over it, the ready button beside it, and
        // a lorry waiting outside.
        Station("출발 셔터", HubStation.Kind.DepartDoor, new Vector3(0f, 2.25f, 14.12f), 0f, new Vector3(5f, 4.5f, .25f), solidBox: true);
        Station("출발 준비판", HubStation.Kind.ReadyBoard, new Vector3(0f, 5.75f, 13.85f), 180f, new Vector3(4.6f, 1.7f, .1f));
        Box("준비 버튼 받침", 3.6f, 4.4f, 0f, 1f, 12.6f, 13.4f, metal, "Metal");
        Box("준비 버튼", 3.8f, 4.2f, 1f, 1.12f, 12.8f, 13.2f, marker, solid: false);
        Station("준비 버튼 자리", HubStation.Kind.Ready, new Vector3(4f, 1.06f, 13f), 180f, new Vector3(.8f, .12f, .8f));
        Box("밖에서 기다리는 트럭", -2f, 2f, 0f, 2.6f, 17f, 23f, metal, "MetalBox");

        // The gun rack on the south wall, by the range's opening.
        Box("총 진열대 판", -11.8f, -7.4f, .6f, 2.6f, -10f, -9.88f, wood, "WoodPanel");
        Station("총 진열대", HubStation.Kind.GunRack, new Vector3(-9.6f, 1.6f, -9.8f), 0f, new Vector3(4.4f, 1.8f, .3f));
        Label("hub.sign.guns", new Vector3(-9.6f, 2.95f, -9.85f), Quaternion.identity, .3f, 4f, false);

        // The stage: the results' lineup stands on it facing the hall (HubStage), lit, the name behind it.
        Box("무대", 7.5f, 11.8f, 0f, .5f, -6f, 6f, wood, "Wood");
        Box("무대 계단", 6.9f, 7.5f, 0f, .25f, -2f, 2f, wood, "Wood");
        Box("무대 뒷벽", 11.75f, 11.95f, .5f, 5.2f, -6f, 6f, road, solid: false);
        Box("무대 띠", 11.7f, 11.75f, 4.2f, 4.35f, -6f, 6f, marker, solid: false);
        Label("ENFILADE", new Vector3(11.68f, 3.4f, 0f), Quaternion.Euler(0f, -90f, 0f), .9f, 9f, true);
        var stage = new GameObject("결과 무대", typeof(HubStage));
        stage.transform.SetParent(stations, false);
        stage.transform.SetPositionAndRotation(new Vector3(9.4f, .5f, 0f), Quaternion.Euler(0f, -90f, 0f));
        Set(stage.GetComponent<HubStage>(), so => so.FindProperty("width").floatValue = 11f);
        for (int i = -1; i <= 1; i++)
            Spot(new Vector3(5.5f, 5.6f, i * 4f), new Vector3(9.4f, 1f, i * 4f));
    }

    // ---- The firing range ----

    private static void Range()
    {
        Box("사격장 서쪽 벽", -7.3f, -7f, 0f, 5f, -122f, -10f);
        Box("사격장 동쪽 벽", 7f, 7.3f, 0f, 5f, -122f, -10f);
        Box("사격장 뒤 둔덕", -7.3f, 7.3f, 0f, 8f, -122.3f, -122f, road, "Asphalt");
        Label("hub.sign.range", new Vector3(0f, 6f, -9.95f), Quaternion.identity, .5f, 6f, false);

        // The firing line: a waist-high counter with a way round at its east end, and the ammo crate.
        Box("사대", -7f, 5.8f, 0f, 1.05f, -14.4f, -13.8f, wood, "Wood");
        Box("탄약 상자", -6.8f, -5.8f, 0f, .7f, -13f, -12.2f, wood, "WoodPlank");
        Station("탄약 상자 위", HubStation.Kind.Ammo, new Vector3(-6.3f, .35f, -12.6f), 0f, new Vector3(1f, .7f, .8f));
        Station("사격장 게시판", HubStation.Kind.RangeBoard, new Vector3(0f, 4.6f, -18f), 0f, new Vector3(5f, 1.8f, .1f));

        // The lanes' lines, and how far each mark is from the firing line.
        foreach (float x in new[] { -3.5f, 0f, 3.5f })
            Box("레인 선", x - .03f, x + .03f, 0f, .015f, -121f, -14.4f, marker, solid: false);
        foreach (int metres in new[] { 5, 10, 15, 25, 50, 75, 100 })
        {
            float z = -14f - metres;
            Box(metres + " m 선", -7f, 7f, 0f, .012f, z - .05f, z + .05f, concrete, solid: false);
            // Lying on the floor, the letters' tops away from the firing line.
            Label(metres + " m", new Vector3(-5.2f, .02f, z - .5f), Quaternion.Euler(-90f, 0f, 0f), .55f, 3f, true);
            Label(metres + " m", new Vector3(-6.95f, 2.6f, z), Quaternion.Euler(0f, 90f, 0f), .6f, 3f, false);
        }

        // Lane 1: a close crowd for the shotguns. Lane 2: one at 15 m, one moving across at 30 m. Lane 3: one at 25 m,
        // a steel plate at 50 m off to the side. Lane 4: one at 50 m, a plate at 100 m.
        Dummy("근거리 무리 1", new Vector3(-6.1f, 0f, -20f));
        Dummy("근거리 무리 2", new Vector3(-5.2f, 0f, -20.7f));
        Dummy("근거리 무리 3", new Vector3(-4.3f, 0f, -19.7f));
        Dummy("15 m", new Vector3(-1.75f, 0f, -29f));
        var mover = Dummy("움직이는 과녁 30 m", new Vector3(-3.3f, 0f, -44f));
        var carriage = new GameObject("움직이는 과녁 레일", typeof(RangeMover)).transform;
        carriage.SetParent(range, false);
        carriage.position = mover.position;
        mover.SetParent(carriage, true);
        Set(carriage.GetComponent<RangeMover>(), so => so.FindProperty("travel").vector3Value = new Vector3(3.2f, 0f, 0f));
        Box("레일", -3.6f, .2f, 0f, .05f, -44.1f, -43.9f, metal, solid: false);
        Dummy("25 m", new Vector3(1f, 0f, -39f));
        Plate("강철판 50 m", new Vector3(2.6f, 0f, -64f));
        Dummy("50 m", new Vector3(4.6f, 0f, -64f));
        Plate("강철판 100 m", new Vector3(5.9f, 0f, -114f));
    }

    // A zombie dummy: wooden, its head red, arms out the way a zombie reaches; facing the firing line. Its root is at its
    // feet, so it tips over about them.
    private static Transform Dummy(string name, Vector3 feet)
    {
        var root = TargetRoot(name, feet, true);
        Part(root, "다리", PrimitiveType.Cube, new Vector3(0f, .43f, 0f), new Vector3(.4f, .86f, .22f), wood);
        Part(root, "몸", PrimitiveType.Cube, new Vector3(0f, 1.18f, 0f), new Vector3(.5f, .64f, .28f), wood);
        Part(root, "팔 1", PrimitiveType.Cube, new Vector3(-.2f, 1.38f, .28f), new Vector3(.12f, .12f, .55f), wood);
        Part(root, "팔 2", PrimitiveType.Cube, new Vector3(.2f, 1.38f, .28f), new Vector3(.12f, .12f, .55f), wood);
        var head = Part(root, "머리", PrimitiveType.Sphere, new Vector3(0f, 1.66f, 0f), Vector3.one * .3f, marker);
        head.AddComponent<WeaponHitRegion>().Kind = WeaponHitRegion.Region.Head;
        return root;
    }

    // A steel plate on a post, a round head plate over it.
    private static void Plate(string name, Vector3 feet)
    {
        var root = TargetRoot(name, feet, false);
        Part(root, "기둥", PrimitiveType.Cube, new Vector3(0f, .5f, 0f), new Vector3(.08f, 1f, .08f), metal);
        Part(root, "판", PrimitiveType.Cube, new Vector3(0f, 1.45f, 0f), new Vector3(.6f, .9f, .05f), metal);
        var head = Part(root, "머리 판", PrimitiveType.Cylinder, new Vector3(0f, 2.07f, 0f), new Vector3(.3f, .025f, .3f), marker);
        head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        head.AddComponent<WeaponHitRegion>().Kind = WeaponHitRegion.Region.Head;
    }

    private static Transform TargetRoot(string name, Vector3 feet, bool zombie)
    {
        var root = new GameObject(name, typeof(RangeTarget), typeof(WeaponHitRegion));
        root.transform.SetParent(range, false);
        // Facing the firing line (north).
        root.transform.SetPositionAndRotation(feet, Quaternion.identity);
        Set(root.GetComponent<RangeTarget>(), so =>
        {
            so.FindProperty("nameId").stringValue = zombie ? "hub.range.dummy" : "hub.range.plate";
            so.FindProperty("zombie").boolValue = zombie;
        });
        return root.transform;
    }

    private static GameObject Part(Transform root, string name, PrimitiveType shape, Vector3 at, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = name;
        part.transform.SetParent(root, false);
        part.transform.localPosition = at;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    // ---- Saving ----

    private static void Finish()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Scenes", "Hub");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        var build = EditorBuildSettings.scenes.ToList();
        if (!build.Any(entry => entry.path == ScenePath))
        {
            build.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = build.ToArray();
        }
        AssetDatabase.SaveAssets();
        Tell("허브를 지었어요: " + ScenePath + "\n\n메인 메뉴의 혼자 하기·방 만들기가 이제 허브로 들어가요. 지도 탁자에서 장을 고르고, " +
             "클래스 벽에서 클래스를, 총 진열대에서 총을 [F]로 고른 뒤 준비 버튼을 누르면 출발해요. 사격장은 남쪽 문 너머예요.");
    }

    // ---- Small helpers, in GreyboxBuilder's manner ----

    private static GameObject Box(string name, float x0, float x1, float y0, float y1, float z0, float z1,
        Material material = null, string surface = null, bool solid = true)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(terrain, false);
        box.transform.localPosition = new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, (z0 + z1) * .5f);
        box.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        box.GetComponent<Renderer>().sharedMaterial = material != null ? material : concrete;
        if (!solid) Object.DestroyImmediate(box.GetComponent<Collider>());
        if (!string.IsNullOrEmpty(surface)) Set(box.AddComponent<SurfaceType>(), so => so.FindProperty("surface").stringValue = surface);
        return box;
    }

    // A hub station (HubStation) facing <paramref name="yaw"/>; a solid one (the way out's shutter) is a box of its size.
    private static void Station(string name, HubStation.Kind kind, Vector3 at, float yaw, Vector3 size, bool solidBox = false)
    {
        GameObject station;
        if (solidBox)
        {
            station = GameObject.CreatePrimitive(PrimitiveType.Cube);
            station.name = name;
            station.transform.localScale = size;
            station.GetComponent<Renderer>().sharedMaterial = metal;
            Set(station.AddComponent<SurfaceType>(), so => so.FindProperty("surface").stringValue = "Metal");
        }
        else station = new GameObject(name);
        station.transform.SetParent(stations, false);
        station.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        Set(station.AddComponent<HubStation>(), so =>
        {
            so.FindProperty("kind").enumValueIndex = (int)kind;
            // A scaled box carries its size in its scale already: its station's area is one unit of it.
            so.FindProperty("size").vector3Value = solidBox ? Vector3.one : size;
        });
    }

    private static void Label(string words, Vector3 at, Quaternion turn, float height, float width, bool accent)
    {
        var label = new GameObject("글자 " + words, typeof(HubLabel));
        label.transform.SetParent(stations, false);
        label.transform.SetPositionAndRotation(at, turn);
        Set(label.GetComponent<HubLabel>(), so =>
        {
            so.FindProperty("words").stringValue = words;
            so.FindProperty("height").floatValue = height;
            so.FindProperty("width").floatValue = width;
            so.FindProperty("accent").boolValue = accent;
        });
    }

    private static void Ladder(string name, Vector3 foot, float outwardYaw, float height)
    {
        var ladder = new GameObject(name, typeof(Ladder));
        ladder.transform.SetParent(fixtures, false);
        ladder.transform.SetPositionAndRotation(foot, Quaternion.Euler(0f, outwardYaw, 0f));
        Set(ladder.GetComponent<Ladder>(), so => so.FindProperty("height").floatValue = height);
    }

    private static void Zipline(string name, Vector3 from, Vector3 to)
    {
        var line = new GameObject(name, typeof(Zipline));
        line.transform.SetParent(fixtures, false);
        line.transform.position = from;
        Set(line.GetComponent<Zipline>(), so =>
        {
            so.FindProperty("end").vector3Value = to - from;
            so.FindProperty("twoWay").boolValue = false;
        });
    }

    private static void Lamp(Vector3 at)
    {
        var lamp = new GameObject("등", typeof(Light));
        lamp.transform.SetParent(lights, false);
        lamp.transform.position = at;
        var light = lamp.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = 15f;
        light.intensity = 2.4f;
        light.color = new Color(1f, .86f, .64f);
        light.shadows = LightShadows.None;
    }

    // A stage light from <paramref name="at"/> onto <paramref name="onto"/>.
    private static void Spot(Vector3 at, Vector3 onto)
    {
        var spot = new GameObject("무대 조명", typeof(Light));
        spot.transform.SetParent(lights, false);
        spot.transform.SetPositionAndRotation(at, Quaternion.LookRotation(onto - at, Vector3.up));
        var light = spot.GetComponent<Light>();
        light.type = LightType.Spot;
        light.range = 14f;
        light.spotAngle = 55f;
        light.intensity = 40f;
        light.color = new Color(1f, .93f, .95f);
        light.shadows = LightShadows.None;
    }

    private static void Set(Object target, System.Action<SerializedObject> change)
    {
        var so = new SerializedObject(target);
        change(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform Root(string name)
    {
        var root = new GameObject(name).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        return root;
    }

    private static Material Load(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + path);
        if (material == null) Debug.LogWarning("[Hub] material not found: " + Materials + path);
        return material;
    }

    private static void Tell(string message) => EditorUtility.DisplayDialog(Title, message, "확인");
}
