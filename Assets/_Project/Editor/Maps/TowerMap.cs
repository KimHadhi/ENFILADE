using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the ENFILADE tower's scene (ENFILADE > 맵 > 엔필레이드 타워 짓기) from TowerLayout, with GreyboxBuilder: an old
/// army training block on a fenced square. Inside, four lanes of killhouse rooms climb side by side, level after level,
/// their shared walls glazed with armoured glass so each lane watches the next one's fight; between every three floors a
/// hall across all four lanes. Each room is a long one (TowerLayout.RoomLength) with holding pens at its far end, where its
/// zombies come out (TowerRun), rows of cover down its length and its floor number on the far wall, lit by downlights that
/// shine nothing up through the slab onto the level above. Each hall has its lanes' doors with a palm scanner beside each and a sign
/// over it, a shop counter, supplies and somewhere to sit. Doors are steel panels that slide into the wall (TowerDoor),
/// with a lamp over each. The look is the HUD's: dark slate, white light panels, pink neon (Art/Materials/Tower).
/// Map dressing stage 5 (the user, 2026-11-13: "타워 진행"): each block of three floors between two halls has its own
/// walls and floor, darker as it climbs (floors 1-3 bare concrete, 4-6 olive paint, 7-9 the slate, 10 dark steel on
/// tread plate), the halls mid-grey paint (the white hall names must read on it) on light terrazzo, the stairwells the
/// slate all the way; outside, a skin with painted slit windows from the third level up and the city round the square
/// (Tables/MapSurfaces EnfiladeTower.*,
/// MapWindows Slit, MapSkyline EnfiladeTower). The Military Training kit (Tables/MapArt hub*, as the hub's) fits the
/// rooms out (a start line, a camera, an extinguisher, a range sign, crates in the entry corner, spent targets on the
/// far wall) and the halls (a departure line, signs and extinguishers by the lanes' doors, cameras, speakers and wall
/// lamps, a gun locker and crates), all on walls and in corners, clear of the cover, the pens and the champion's spot.
/// Rebuilding wipes hand edits, so the layout lives here and in TowerLayout.
/// </summary>
public static partial class TowerMap
{
    private const string Title = "엔필레이드 타워", EnemyPrefab = ProjectFolders.PrefabsByName + "Enemies/Enemy_Test.prefab";
    private const float W = TowerLayout.Wall, H = TowerLayout.Height, S = TowerLayout.Slab;
    // The outer walls' outer skin (m): the look with the painted windows, so their inner faces keep the rooms' walls.
    private const float Skin = .1f;
    // How far the kit's floor stripes lie over a floor's top, so the floor never shows through them.
    private const float StripeLift = .004f;
    private static readonly Color Cool = new Color(.9f, .94f, 1f), Pink = new Color(1f, .31f, .47f);
    private static Material wall, floor, trim, steel, neon, panel, lamp, glass;
    // Each block of floors' walls and floors (segment by segment), and the halls': rows EnfiladeTower.RoomWall1-4,
    // RoomFloor1-4, HallWall, HallFloor; the base Wall and Floor where a row or its textures are missing.
    private static readonly Material[] roomWalls = new Material[TowerLayout.Segments], roomFloors = new Material[TowerLayout.Segments];
    private static Material hallWall, hallFloor;
    private static Transform fixtures, lights;

    [MenuItem("ENFILADE/맵/엔필레이드 타워 짓기", priority = 30)]
    public static void Build()
    {
        var b = GreyboxBuilder.Begin(TowerRun.SceneName, Title);
        if (b == null) return;
        b.AddsCampaign = false;
        // Real concrete, terrazzo and steel from Tables/MapSurfaces (map dressing stage 4), the flat HUD colours where it
        // has no row; the neon, light panels, lamps and glass stay as they are.
        wall = b.Surface("Wall", GreyboxBuilder.MaterialAt("Tower/TowerWall.mat"));
        floor = b.Surface("Floor", GreyboxBuilder.MaterialAt("Tower/TowerFloor.mat"));
        trim = b.Surface("Trim", GreyboxBuilder.MaterialAt("Tower/TowerTrim.mat"));
        steel = b.Surface("Steel", GreyboxBuilder.MaterialAt("Tower/TowerSteel.mat"));
        for (int segment = 0; segment < TowerLayout.Segments; segment++)
        {
            roomWalls[segment] = b.Surface("RoomWall" + (segment + 1), wall);
            roomFloors[segment] = b.Surface("RoomFloor" + (segment + 1), floor);
        }
        hallWall = b.Surface("HallWall", wall);
        hallFloor = b.Surface("HallFloor", floor);
        neon = GreyboxBuilder.MaterialAt("Tower/TowerNeon.mat");
        panel = GreyboxBuilder.MaterialAt("Tower/TowerPanel.mat");
        lamp = GreyboxBuilder.MaterialAt("Tower/TowerLamp.mat");
        glass = GreyboxBuilder.MaterialAt("Tower/TowerGlass.mat");
        fixtures = b.Group("◤━━ 타워 장치 (스캐너·문 등·표지)");
        lights = b.Group("◤━━ 타워 조명");

        int doors = 0;
        Shell(b);
        Slabs(b);
        StairWalls(b);
        for (int level = 0; level < TowerLayout.Levels; level++)
        {
            doors += EndWalls(b, level);
            if (TowerLayout.IsHall(level)) Hall(b, level);
            else Room(b, level);
            if (level < TowerLayout.Levels - 1) Stairs(b, level);
        }
        Square(b);
        Run(b);
        b.PlayerStart(TowerLayout.StartSpot, 90f);
        // From the square's south-west, low, looking up at the tower's west face (its name, the way in) and along its
        // south face. The second point is where the camera looks, not its angles.
        b.Shot(new Vector3(-60f, 8f, -52f), new Vector3(12f, 32f, 0f));
        b.Finish("엔필레이드 타워를 지었어요(Scenes/Campaign/" + TowerRun.SceneName + ".unity).\n" +
                 "레인 4개 × 10층, 휴게층 3곳과 1층 로비, 문 " + doors + "개, 스캐너 " + TowerLayout.Segments * TowerLayout.Lanes + "개.\n" +
                 "허브의 지도 탁자나 메인 메뉴에서 '엔필레이드 타워'를 고르면 돼요. 오늘의 방 구성은 Tables/TowerFloors, 진행 수치는 Tables/Tower.");
    }

    // The rooms' signs by the entry door, by turns: the kind and its scale (the kit's signs are small plates).
    private static readonly (string kind, float scale)[] RoomSigns =
    {
        ("hubsign_livefire", 1.4f), ("hubsign_eyeear", 1.8f), ("hubsign_attention", 1.1f), ("hubsign_warning", 1.5f)
    };
}
