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
/// lobby) from code, as the chapters are built: the layout's boxes here, laid and dressed by the map dresser
/// (GreyboxBuilder.Attach: Tables/MapSurfaces looks, the bought kit's models from Tables/MapArt; HubBuilder.Dressing has
/// the dressing). It keeps its own saving because a chapter's builder saves under Scenes/Campaign and puts a chapter's
/// run, walk mesh and culling in it. ENFILADE > 맵 > 허브 로비 짓기 makes Scenes/Hub/Hub.unity and puts it in the build;
/// running it again rebuilds it from scratch (hand edits are lost: change the layout here).
/// The layout, +Z north, metres:
/// - The hideout, a warehouse 24 x 24 with a skylight: the start in its south half; the map table in the middle with the
///   next chapter's sign hanging over it; the class wall (lockers) on the west wall under a catwalk, reached by two crates
///   and a ladder, with a zipline from it across the hall; the stage on the east side (the results' lineup, HubStage)
///   with lights on it and a full-length mirror on its back wall under the name (HubMirror, the dressing spot in front of
///   it: HubDressing); the way out in the north wall (a shutter that rises with the count, the board over it, the
///   ready button beside it); the gun rack on the south wall by the range's opening; a low wall to vault.
/// - The firing range through the south wall, open air, 14 wide and 108 long: a firing line counter, an ammo crate,
///   four lanes of steel targets with a zombie's health (a close crowd of three; one at 15 m and one moving across at
///   30 m; one at 25 m and a thick plate at 50 m; one at 50 m and a thick plate at 100 m), distance marks on the floor
///   and the wall, and the range board over the lanes. Only where each target stands is laid here: its steel body is
///   built at play (RangeTarget, SteelTarget, Tables/RangeTargets). The stations are filled from the tables at play
///   (HubLobby), so nothing here needs a rebuild when a chapter, class or gun is added.
/// </summary>
public static partial class HubBuilder
{
    private const string Folder = "Assets/_Project/Scenes/Hub";
    private const string ScenePath = Folder + "/Hub.unity";
    private const string HudPrefab = "Assets/_Project/Prefabs/UI/HUD/GameHUD.prefab";
    private const string Title = "허브 로비";
    private const float Lift = .05f;

    private static Scene scene;
    private static Transform terrain, fixtures, stations, range, systems, lights;
    private static Material concrete, road, metal, wood, marker;

    [MenuItem("ENFILADE/맵/허브 로비 짓기")]
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
        terrain = Root("◤━━ 은신처 (지형)");
        // The map dresser on this scene: the real surfaces (Tables/MapSurfaces, rows Hub.* and the defaults) and the kit.
        dresser = GreyboxBuilder.Attach(scene, "Hub", Title, terrain);
        concrete = dresser.Concrete;
        road = dresser.Road;
        metal = dresser.Metal;
        wood = dresser.Wood;
        marker = dresser.Marker;
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
        // The catwalk wears its look by name (Tables/MapSurfaces Hub.Catwalk: a steel grating on top).
        Box("캣워크", -12f, -9.6f, 3f, 3.2f, -8f, 12f, null, "Gantry");
        // The rail leaves a gap where the ladder comes up and the zipline sets off. Its boxes keep the collision; the
        // kit's railing is what is seen.
        foreach (var (n, z0, z1) in new[] { (1, 4f, 8.8f), (2, 11.2f, 12f) })
        {
            var rail = Box("캣워크 난간 " + n, -9.7f, -9.6f, 3.2f, 4.2f, z0, z1, metal, "MetalPipe");
            if (dresser.ArtSize("hubrailing") == Vector3.zero) continue;
            rail.GetComponent<Renderer>().enabled = false;
            Span("hubrailing", new Vector3(-9.65f, 3.2f, z0), new Vector3(-9.65f, 3.2f, z1), 90f);
        }
        Box("캣워크 기둥 1", -9.85f, -9.65f, 0f, 3f, -7.9f, -7.7f, metal, "MetalPipe");
        Box("캣워크 기둥 2", -9.85f, -9.65f, 0f, 3f, 11.7f, 11.9f, metal, "MetalPipe");
        Ladder("캣워크 사다리", new Vector3(-9.45f, 0f, 9.5f), 90f);
        Box("집라인 기둥 1", -10.7f, -10.5f, 3.2f, 5.9f, 9.9f, 10.1f, metal, solid: false);
        Zipline("홀 건너는 집라인", new Vector3(-10.6f, 5.4f, 10f), new Vector3(6.4f, 2.1f, 10f));
        Box("집라인 기둥 2", 6.5f, 6.7f, 0f, 2.6f, 9.9f, 10.1f, metal, solid: false);
        Box("넘는 벽", 2f, 4f, 0f, 1f, -8.2f, -7.8f, concrete, "Concrete");

        // The way out: the shutter rises with the count (HubLobby), the board over it, the ready button beside it, and
        // a lorry waiting outside.
        var shutter = Station("출발 셔터", HubStation.Kind.DepartDoor, new Vector3(0f, 2.25f, 14.12f), 0f, new Vector3(5f, 4.5f, .25f), solidBox: true);
        // The kit's roller door filling the shutter, its coil box to the hall, riding up with it (HubLobby moves the
        // station), so it leaves the static dressing.
        if (DressBox(shutter, "hubshutter")) dresser.LastPlaced.transform.SetParent(shutter.transform, true);
        Station("출발 준비판", HubStation.Kind.ReadyBoard, new Vector3(0f, 5.75f, 13.85f), 180f, new Vector3(4.6f, 1.7f, .1f));
        Box("준비 버튼 받침", 3.6f, 4.4f, 0f, 1f, 12.6f, 13.4f, metal, "Metal");
        Box("준비 버튼", 3.8f, 4.2f, 1f, 1.12f, 12.8f, 13.2f, marker, solid: false);
        Station("준비 버튼 자리", HubStation.Kind.Ready, new Vector3(4f, 1.06f, 13f), 180f, new Vector3(.8f, .12f, .8f));
        // The lorry is the M923 the chapters use (Tables/MapArt militarytruck), its box its size.
        DressBox(Box("밖에서 기다리는 트럭", -1.5f, 1.5f, 0f, 3.05f, 16.5f, 24.7f, metal, "MetalBox"), "militarytruck");

        // The gun rack on the south wall, by the range's opening: a slatwall the guns hang before.
        DressBox(Box("총 진열대 판", -11.8f, -7.4f, .6f, 2.6f, -10f, -9.88f, wood, "WoodPanel"), "hubslatwall");
        Station("총 진열대", HubStation.Kind.GunRack, new Vector3(-9.6f, 1.6f, -9.8f), 0f, new Vector3(4.4f, 1.8f, .3f));
        Label("hub.sign.guns", new Vector3(-9.6f, 2.95f, -9.85f), Quaternion.identity, .3f, 4f, false);

        // The stage: the results' lineup stands on it facing the hall (HubStage), lit, the name behind it.
        Box("무대", 7.5f, 11.8f, 0f, .5f, -6f, 6f, wood, "Wood");
        Box("무대 계단", 6.9f, 7.5f, 0f, .25f, -2f, 2f, wood, "Wood");
        // Solid (it had no collider): a shot at it lands on its face, not on the hall's wall hidden behind it, and nobody
        // walks into it, so the mirror needs no unseen stop in front of it.
        Box("무대 뒷벽", 11.75f, 11.95f, .5f, 5.2f, -6f, 6f, road, "Concrete");
        Box("무대 띠", 11.7f, 11.75f, 4.2f, 4.35f, -6f, 6f, marker, solid: false);
        Label("ENFILADE", new Vector3(11.68f, 3.4f, 0f), Quaternion.Euler(0f, -90f, 0f), .9f, 9f, true);
        // The mirror under the name, standing on the stage (user, 2026-11-12: "로비 무대 엔필레이드 쪽에 거울 넣어서 자기 볼 수
        // 있게 만들고 여기서 자기 캐릭터 커스터마이징"): 3 m wide, 2.2 m tall, its top well under the name's letters.
        Mirror(new Vector3(11.7f, .55f + 1.1f, 0f), 3f, 2.2f);
        var stage = new GameObject("결과 무대", typeof(HubStage));
        stage.transform.SetParent(stations, false);
        stage.transform.SetPositionAndRotation(new Vector3(9.4f, .5f, 0f), Quaternion.Euler(0f, -90f, 0f));
        GreyboxBuilder.Set(stage.GetComponent<HubStage>(), so => so.FindProperty("width").floatValue = 11f);
        for (int i = -1; i <= 1; i++)
            Spot(new Vector3(5.5f, 5.6f, i * 4f), new Vector3(9.4f, 1f, i * 4f));
        DressHall();
    }

    // ---- The firing range ----

    private static void Range()
    {
        Box("사격장 서쪽 벽", -7.3f, -7f, 0f, 5f, -122f, -10f);
        Box("사격장 동쪽 벽", 7f, 7.3f, 0f, 5f, -122f, -10f);
        // The berm wears dirt by name (Tables/MapSurfaces Hub.Berm); rock stands before it (DressRange).
        Box("사격장 뒤 둔덕", -7.3f, 7.3f, 0f, 8f, -122.3f, -122f, null, "Dirt");
        Label("hub.sign.range", new Vector3(0f, 6f, -9.95f), Quaternion.identity, .5f, 6f, false);

        // The firing line: a waist-high counter with a way round at its east end (its box the kit's cabinets' size,
        // which are what is seen: DressRange), and the ammo crate (the kit's, its box its size).
        // Without the kit the box's wood stays the counter, so it is never an unseen wall.
        var counter = Box("사대", -7f, 5.46f, 0f, 1.01f, CounterBack, -13.8f, wood, "WoodPanel");
        if (dresser.ArtSize("hubcounter") != Vector3.zero) counter.GetComponent<Renderer>().enabled = false;
        DressBox(Box("탄약 상자", -6.87f, -5.73f, 0f, .55f, -12.95f, -12.2f, wood, "Plastic"), "hubammocrate", 0);
        Station("탄약 상자 위", HubStation.Kind.Ammo, new Vector3(-6.3f, .275f, -12.575f), 0f, new Vector3(1.14f, .55f, .75f));
        Station("사격장 게시판", HubStation.Kind.RangeBoard, new Vector3(0f, 4.6f, -18f), 0f, new Vector3(5f, 1.8f, .1f));

        // The lanes' lines, and how far each mark is from the firing line, on the range's floor (its top at Floor).
        foreach (float x in new[] { -3.5f, 0f, 3.5f })
            Box("레인 선", x - .03f, x + .03f, Floor, Floor + .008f, -121f, CounterBack, marker, solid: false);
        foreach (int metres in new[] { 5, 10, 15, 25, 50, 75, 100 })
        {
            float z = -14f - metres;
            Box(metres + " m 선", -7f, 7f, Floor, Floor + .006f, z - .05f, z + .05f, concrete, solid: false);
            // Lying on the floor, the letters' tops away from the firing line.
            Label(metres + " m", new Vector3(-5.2f, Floor + .02f, z - .5f), Quaternion.Euler(-90f, 0f, 0f), .55f, 3f, true);
            Label(metres + " m", new Vector3(-6.95f, 2.6f, z), Quaternion.Euler(0f, 90f, 0f), .6f, 3f, false);
        }

        // Lane 1: a close crowd for the shotguns. Lane 2: one at 15 m, one moving across at 30 m. Lane 3: one at 25 m,
        // a steel plate at 50 m off to the side. Lane 4: one at 50 m, a plate at 100 m.
        Dummy("근거리 무리 1", new Vector3(-6.1f, Floor, -20f));
        Dummy("근거리 무리 2", new Vector3(-5.2f, Floor, -20.7f));
        Dummy("근거리 무리 3", new Vector3(-4.3f, Floor, -19.7f));
        Dummy("15 m", new Vector3(-1.75f, Floor, -29f));
        var mover = Dummy("움직이는 과녁 30 m", new Vector3(-3.3f, Floor, -44f));
        var carriage = new GameObject("움직이는 과녁 레일", typeof(RangeMover)).transform;
        carriage.SetParent(range, false);
        carriage.position = mover.position;
        mover.SetParent(carriage, true);
        GreyboxBuilder.Set(carriage.GetComponent<RangeMover>(), so => so.FindProperty("travel").vector3Value = new Vector3(3.2f, 0f, 0f));
        Box("레일", -3.6f, .2f, Floor, Floor + .05f, -44.1f, -43.9f, metal, solid: false);
        Dummy("25 m", new Vector3(1f, Floor, -39f));
        Plate("강철판 50 m", new Vector3(2.6f, Floor, -64f));
        Dummy("50 m", new Vector3(4.6f, Floor, -64f));
        Plate("강철판 100 m", new Vector3(5.9f, Floor, -114f));
        DressRange();
    }

    // A steel target with a zombie's health (Hub.csv dummyHealth), facing the firing line. Only the spot is laid here:
    // RangeTarget builds its steel body at play (SteelTarget: a hinged base, a post, a silhouette torso plate and a round
    // head plate, Tables/RangeTargets row Zombie), so its shape changes with the table and needs no rebuild.
    private static Transform Dummy(string name, Vector3 feet) => TargetRoot(name, feet, true);

    // A thick steel plate (plateHealth), built the same way at play (row Plate).
    private static void Plate(string name, Vector3 feet) => TargetRoot(name, feet, false);

    private static Transform TargetRoot(string name, Vector3 feet, bool zombie)
    {
        var root = new GameObject(name, typeof(RangeTarget), typeof(WeaponHitRegion));
        root.transform.SetParent(range, false);
        // Facing the firing line (north).
        root.transform.SetPositionAndRotation(feet, Quaternion.identity);
        GreyboxBuilder.Set(root.GetComponent<RangeTarget>(), so =>
        {
            so.FindProperty("nameId").stringValue = zombie ? "hub.range.dummy" : "hub.range.plate";
            so.FindProperty("zombie").boolValue = zombie;
        });
        return root.transform;
    }

    // ---- Saving ----

    private static void Finish()
    {
        // Everything is laid now, so the catwalk's ladder can be fitted to the catwalk.
        string ladders = LadderFit.FitAll(fixtures);
        // The dresser's holder goes and what never moves turns static; its lines (surfaces, models) join the report.
        string dressed = dresser.EndDressing();
        dresser = null;
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
             "클래스 벽에서 클래스를, 총 진열대에서 총을 [F]로 고른 뒤 준비 버튼을 누르면 출발해요. 사격장은 남쪽 문 너머예요." +
             (ladders.Length > 0 ? "\n\n" + ladders : "") + dressed);
    }

    // ---- Small helpers, in GreyboxBuilder's manner ----

    // A box under the hall's terrain, through the dresser: with no material it wears the look its name claims
    // (Tables/MapSurfaces Hub.* boxes), else the scene's concrete.
    private static GameObject Box(string name, float x0, float x1, float y0, float y1, float z0, float z1,
        Material material = null, string surface = null, bool solid = true)
        => dresser.Box(name, x0, x1, y0, y1, z0, z1, material, surface, solid);

    // The stage's mirror, its glass's middle at <paramref name="glass"/> on the back wall (x 11.75), facing the hall (-x):
    // a metal frame standing a little proud of the glass with the stage band's pink along its top. The glass is
    // HubMirror's, made at play, with the station for [F]; its collider is laid here, on the glass's own face.
    // Shooting the mirror (the user, 2026-11-12, "원래 거울에 총 쏘면 이렇게 이펙트가 크고 과한가?") used to hit an unseen
    // stop 6 cm in front of the glass, 4.7 m tall and plain (Concrete's dust and smoke), which played in the air before
    // the glass, even a metre over the frame, and the mirror showed it a second time. The stop kept players off the glass
    // while the back wall had no collider; the wall is solid now (Box above), so what stops a body and a bullet is what
    // is there: the frame (Metal sparks), the glass (Mirror: a small glass burst and a small crack gone in seconds,
    // ImpactEffects.csv) and the wall around (Concrete).
    private static void Mirror(Vector3 glass, float width, float height)
    {
        const float wall = 11.75f, rim = .06f, proud = .025f, floor = .5f;
        float front = glass.x - proud, y0 = glass.y - height * .5f, y1 = glass.y + height * .5f;
        float z0 = glass.z - width * .5f, z1 = glass.z + width * .5f;
        Box("거울 틀 왼쪽", front, wall, floor, y1 + rim, z1, z1 + rim, metal, "Metal");
        Box("거울 틀 오른쪽", front, wall, floor, y1 + rim, z0 - rim, z0, metal, "Metal");
        Box("거울 틀 위", front, wall, y1, y1 + rim, z0, z1, metal, "Metal");
        Box("거울 틀 아래", front, wall, floor, y0, z0, z1, metal, "Metal");
        Box("거울 띠", front - .006f, front, y1 + rim - .022f, y1 + rim, z0 - rim, z1 + rim, marker, solid: false);
        // The glass: unseen, from its face back to the wall.
        var pane = new GameObject("거울 유리", typeof(BoxCollider));
        pane.transform.SetParent(terrain, false);
        pane.transform.localPosition = new Vector3((glass.x + wall) * .5f, glass.y, glass.z);
        pane.GetComponent<BoxCollider>().size = new Vector3(wall - glass.x, height, width);
        GreyboxBuilder.Set(pane.AddComponent<SurfaceType>(), so => so.FindProperty("surface").stringValue = "Mirror");
        var station = Station("거울", HubStation.Kind.Mirror, glass, -90f, new Vector3(width, height, .1f));
        station.AddComponent<HubMirror>();
    }

    // A hub station (HubStation) facing <paramref name="yaw"/>; a solid one (the way out's shutter) is a box of its size.
    private static GameObject Station(string name, HubStation.Kind kind, Vector3 at, float yaw, Vector3 size, bool solidBox = false)
    {
        GameObject station;
        if (solidBox)
        {
            station = GameObject.CreatePrimitive(PrimitiveType.Cube);
            station.name = name;
            station.transform.localScale = size;
            station.GetComponent<Renderer>().sharedMaterial = metal;
            GreyboxBuilder.Set(station.AddComponent<SurfaceType>(), so => so.FindProperty("surface").stringValue = "Metal");
        }
        else station = new GameObject(name);
        station.transform.SetParent(stations, false);
        station.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        GreyboxBuilder.Set(station.AddComponent<HubStation>(), so =>
        {
            so.FindProperty("kind").enumValueIndex = (int)kind;
            // A scaled box carries its size in its scale already: its station's area is one unit of it.
            so.FindProperty("size").vector3Value = solidBox ? Vector3.one : size;
        });
        return station;
    }

    private static void Label(string words, Vector3 at, Quaternion turn, float height, float width, bool accent)
    {
        var label = new GameObject("글자 " + words, typeof(HubLabel));
        label.transform.SetParent(stations, false);
        label.transform.SetPositionAndRotation(at, turn);
        GreyboxBuilder.Set(label.GetComponent<HubLabel>(), so =>
        {
            so.FindProperty("words").stringValue = words;
            so.FindProperty("height").floatValue = height;
            so.FindProperty("width").floatValue = width;
            so.FindProperty("accent").boolValue = accent;
        });
    }

    // Its height is measured once the hall stands (Finish, LadderFit): the rails end flush with the floor it leads to.
    private static void Ladder(string name, Vector3 foot, float outwardYaw)
    {
        var ladder = new GameObject(name, typeof(Ladder));
        ladder.transform.SetParent(fixtures, false);
        ladder.transform.SetPositionAndRotation(foot, Quaternion.Euler(0f, outwardYaw, 0f));
    }

    private static void Zipline(string name, Vector3 from, Vector3 to)
    {
        var line = new GameObject(name, typeof(Zipline));
        line.transform.SetParent(fixtures, false);
        line.transform.position = from;
        GreyboxBuilder.Set(line.GetComponent<Zipline>(), so =>
        {
            so.FindProperty("end").vector3Value = to - from;
            so.FindProperty("twoWay").boolValue = false;
        });
    }

    private static void Lamp(Vector3 at, float range = 15f, float intensity = 2.4f)
    {
        var lamp = new GameObject("등", typeof(Light));
        lamp.transform.SetParent(lights, false);
        lamp.transform.position = at;
        var light = lamp.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
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

    private static Transform Root(string name)
    {
        var root = new GameObject(name).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        return root;
    }

    private static void Tell(string message) => EditorUtility.DisplayDialog(Title, message, "확인");
}
