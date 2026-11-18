using UnityEditor;
using UnityEngine;
using Side = GreyboxBuilder.Side;

// CampaignMaps: chapter 4, the airport, with its dressing. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    // The terminal is the Airport Terminal Scene pack's own scene (the user, 2026-11-14: "공항 실내는 그 그대로 쓰면 되지
    // 않음 내가 산 에셋??", then "팩 씬으로 새로 짓기"), brought in whole by GreyboxBuilder.PackScene. Its own coordinates
    // become the map's turned -90° about Y and moved by TerminalAt: map (x, y, z) = (154 - z, y, x - 33.35) from the
    // pack's (x, y, z). So its north-east entrance (the pack's z 84) faces the landside to the west, and the south-east
    // gate room's end wall (the pack's z -112.9) stands at the map's x 266.9, on the map's centre line, where the jet
    // bridge goes on.
    private const string TerminalScene = "Assets/ThirdParty/Airport_Terminal_Scene/Scene/terminal_scene.unity";
    private static readonly Vector3 TerminalAt = new Vector3(154f, 0f, -33.35f);
    private const float TerminalYaw = -90f;

    // Which of the pack scene's objects are its building (the rest are props): its collision meshes and the check-in
    // counters' wall (its own mesh collider), walls, ceilings, floors and roof, glass, and the escalators, planters and
    // the like. A doorway is cut through the walls' collision, never the floors' under it.
    private static readonly GreyboxBuilder.PackParts TerminalParts = new GreyboxBuilder.PackParts
    {
        Collision = new[] { "colmesh", "luggage_chechin_counter_wall" },
        Walls = new[] { "waende" },
        Ceilings = new[] { "decken" },
        Shell = new[] { "boden", "dach" },
        Glass = new[] { "fenster", "dachfesnter" },
        // The body scanner too: its arch is walked through (its panels: CampaignMaps.SecurityLane), not a solid box.
        Fixed = new[] { "rolltreppe", "blumen", "luggage_chechin_counter_wall", "bodyscanner" },
        Uncut = new[] { "colmesh_floor" }
    };

    [MenuItem("ENFILADE/맵/캠페인/4장 공항", priority = 14)]
    private static void Airport()
    {
        var b = GreyboxBuilder.Begin("Ch4_Airport", "4장 공항");
        if (b == null) return;
        // The terminal's upper floor (the pack's), where the jet bridge and the duty-free shop are. Everything east of the
        // terminal (the jet bridge, the apron, the tower, the runway, the airliner, the transport) stands where it stood
        // before the pack's terminal came, moved east by Airside.
        const float Floor = 4f, Airside = 86.6f;

        // The ground and the edge of the map: the river to the west, the airfield's wall on the other sides. Under the
        // terminal the ground sits a little lower, so the terminal's own ground floor (at 0) never fights it for the
        // same plane.
        b.Box("땅 서", -22f, 70f, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("땅 터미널 밑", 70f, 250.3f, -.5f, -.05f, -85f, 60f, b.Road, "Asphalt");
        b.Box("땅 동", 250.3f, 446.6f, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("맵 끝 서 (강)", -22f, -21f, 0f, 6f, -60f, 60f);
        b.Box("맵 끝 동", 445.6f, 446.6f, 0f, 8f, -60f, 60f);
        b.Box("맵 끝 남 서", -22f, 70.3f, 0f, 8f, -60f, -59f);
        b.Box("맵 끝 남 동", 254f, 446.6f, 0f, 8f, -60f, -59f);
        b.Box("맵 끝 북", -22f, 446.6f, 0f, 8f, 59f, 60f);

        // 1. The cargo pier where chapter 3's boat left them, and the cargo yard: containers to climb, its gate in the fence.
        b.Box("화물 부두", -20f, -6f, 0f, .3f, -6f, 6f, b.Wood, "WoodPlank");
        b.Box("군 보트", -19.5f, -15.5f, 0f, 1.6f, 7f, 10f, b.Metal, solid: false);
        b.PlayerStart(new Vector3(-10f, .3f, 0f), 90f);
        b.Spot("Start", new Vector3(-10f, .3f, 0f));
        b.Dress(b.Box("화물 컨테이너 1", 0f, 12f, 0f, 2.6f, 12f, 14.5f, b.Metal, "MetalBox"), "containerRow");
        b.Dress(b.Box("화물 컨테이너 2", 3f, 9f, 2.6f, 5.2f, 12f, 14.5f, b.Metal, "MetalBox"), "container");
        b.Dress(b.Box("화물 컨테이너 3", 4f, 16f, 0f, 2.6f, -16f, -13.5f, b.Metal, "MetalBox"), "containerRow");
        b.Box("지게차", 10f, 12f, 0f, 2.2f, 4f, 7f, b.Metal, "Metal");
        b.Dress(b.Box("화물 팔레트 1", 2f, 3.2f, 0f, .9f, -4f, -2.8f, b.Wood, "WoodPlank"), "palletload");
        b.Dress(b.Box("화물 팔레트 2", 6f, 7.2f, 0f, .9f, 4.5f, 5.7f, b.Wood, "WoodPlank"), "palletload");
        b.Dress(b.Box("화물장 펜스 남", 20f, 20.2f, 0f, 2.4f, -59f, -4f, b.Metal, "MetalPipe"), "constructionfence");
        b.Dress(b.Box("화물장 펜스 북", 20f, 20.2f, 0f, 2.4f, 4f, 59f, b.Metal, "MetalPipe"), "constructionfence");
        b.Spot("CargoGate", new Vector3(23f, 0f, 0f));
        b.EnemySpawn("화물장 북", new Vector3(5f, 0f, 30f));
        b.EnemySpawn("화물장 남", new Vector3(8f, 0f, -35f));

        // 2. The landside road and the drop-off before the terminal's entrance, a door at the end of its north-east
        // wing (the pack's door was painted shut; PackCut opens it). The airport's wall from the terminal's corners to
        // the map's edges keeps everyone going through the terminal, not round it.
        Car(b, 27f, 0f, -14f); Car(b, 30f, 0f, 17f); Car(b, 41f, 0f, -24f); Car(b, 44f, 0f, 22f);
        Car(b, 50f, 0f, 38f); Car(b, 47f, 0f, 52f);
        Bus(b, "공항 셔틀버스", 56f, 58.6f, 0f, 22f, 34f);
        b.Box("출발 입구 차양", 62f, 70f, 3.2f, 3.45f, 40f, 46.5f, b.Metal, solid: false);
        // The awning is seen only, so its pillars' tops would be perches under it: nobody stands on them (NoStand).
        foreach (float z in new[] { 40.3f, 46.2f }) b.NoStand(b.Box("출발 입구 차양 기둥", 62.2f, 62.5f, 0f, 3.2f, z - .15f, z + .15f, b.Metal, "MetalPipe"));
        b.Box("공항 담장 북", 70f, 70.3f, 0f, 6f, 46.65f, 59f);
        b.Box("공항 담장 남", 70f, 70.3f, 0f, 6f, -59f, 6.45f);
        b.Spot("Departures", new Vector3(64f, 0f, 44.65f));
        b.EnemySpawn("도로 북", new Vector3(38f, 0f, 45f));
        b.EnemySpawn("도로 남", new Vector3(38f, 0f, -45f));
        b.EnemySpawn("입구 앞", new Vector3(58f, 0f, 10f));

        // 3. The terminal, as the pack's maker built and furnished it: the entrance corridor with the security lanes
        // (x-ray belts, a body scanner), the entrance hall, a corridor to the round lounge, escalators up to the
        // check-in hall and the long upper concourse of shops, the duty-free shop (the rest stop), the food court, and
        // the south-east gate hall, whose upper gallery ends in a waiting room with the jet bridge's door.
        b.PackScene("◤━━ 공항 터미널 (Airport Terminal Scene 팩의 장면)", TerminalScene, TerminalAt, TerminalYaw, TerminalParts);
        // Doors painted shut where the map opens a way, and a phone booth in front of the jet bridge's door.
        b.PackRemove("tuere_03d", new Vector3(78.53f, 0f, 83.88f));
        b.PackRemove("phoneblock_big_clean", new Vector3(33.0f, Floor, -112.3f));
        // The doorways, in the pack's coordinates: the entrance at the north-east wing's end, the jet bridge's door at
        // the gate room's end, and the duty-free shop's door where its glass front has a gap for one.
        b.PackCut("입구", new Vector3(76.4f, -.5f, 83.4f), new Vector3(79.6f, 2.8f, 84.6f));
        b.PackCut("탑승교 문", new Vector3(31.75f, Floor - .5f, -113.4f), new Vector3(34.95f, Floor + 2.6f, -112.4f));
        b.PackCut("면세점 문", new Vector3(-37.8f, Floor - .5f, -55.03f), new Vector3(-36.6f, Floor + 2.5f, -52.46f));
        b.PackOutside("PackWall", "TerminalRoof");
        b.PackPodium("PackWall", 0f, Floor);
        b.PackProps();
        // The terminal's gimmicks: the security lane, the announcement desks, the fire alarm (CampaignMaps.AirportGimmicks).
        TerminalGimmicks(b);
        // Frames round the cut doorways (the pack's walls have no thickness to show at a cut edge).
        b.Box("입구 문틀 남", 69.5f, 70.5f, 0f, 2.8f, 42.75f, 43.05f, b.Metal, "Metal");
        b.Box("입구 문틀 북", 69.5f, 70.5f, 0f, 2.8f, 46.25f, 46.55f, b.Metal, "Metal");
        b.Box("입구 문틀 위", 69.5f, 70.5f, 2.8f, 3.05f, 42.75f, 46.55f, b.Metal, "Metal");
        b.Box("면세점 문틀 서", 206.16f, 206.46f, Floor, Floor + 2.5f, -70.85f, -70.5f, b.Metal, "Metal");
        b.Box("면세점 문틀 동", 209.03f, 209.33f, Floor, Floor + 2.5f, -70.85f, -70.5f, b.Metal, "Metal");
        b.Box("면세점 문틀 위", 206.16f, 209.33f, Floor + 2.5f, Floor + 2.7f, -70.85f, -70.5f, b.Metal, "Metal");
        b.Spot("Security", new Vector3(84f, 0f, 13.65f));
        b.EnemySpawn("입구 홀", new Vector3(92f, 0f, 10.65f));
        b.EnemySpawn("라운지", new Vector3(124f, 0f, -31.35f));
        b.EnemySpawn("2층 체크인", new Vector3(154f, Floor, -63.35f));
        b.EnemySpawn("2층 상가", new Vector3(190f, Floor, -60.35f));
        b.EnemySpawn("식당가", new Vector3(236f, Floor, -28.35f));
        b.EnemySpawn("남동 회랑", new Vector3(234f, Floor, -3.35f));
        b.EnemySpawn("남동 탑승 홀", new Vector3(224f, 0f, 31.65f));

        // 4. The duty-free shop on the upper concourse is the rest stop: its door slides along the glass front and shuts
        // it, and its till (the pack's) is the shop.
        b.Door("면세점 문", 206.46f, 209.03f, Floor, Floor + 2.5f, -70.45f, -70.25f, "DutyFree", new Vector3(-2.6f, 0f, 0f));
        b.Shop(new Vector3(214.6f, Floor, -75.95f), new Vector3(214.7f, Floor + 2.4f, -74.35f));
        b.Spot("DutyFree", new Vector3(214f, Floor, -77.35f));

        // 5. The gate: the waiting room at the end of the south-east gallery, its door onto the jet bridge.
        b.Spot("Gate", new Vector3(262f, Floor, 0f));

        // 6. The gate's jet bridge: its plane is gone, so it ends in the air over the apron, and its service stairs go down.
        float x = Airside;
        b.Box("탑승교 바닥", 180.3f + x, 196f + x, Floor - .4f, Floor, -1.6f, 1.6f, b.Metal, "Gantry");
        b.Box("탑승교 벽 남", 180.3f + x, 196f + x, Floor, Floor + 2.6f, -1.9f, -1.6f, b.Metal, "Metal");
        b.Box("탑승교 벽 북", 180.3f + x, 196f + x, Floor, Floor + 2.6f, 1.6f, 1.9f, b.Metal, "Metal");
        b.Box("탑승교 지붕", 180.3f + x, 196f + x, Floor + 2.6f, Floor + 2.9f, -1.9f, 1.9f, b.Metal);
        b.Box("탑승교 다리", 191.7f + x, 192.3f + x, 0f, Floor - .4f, -.3f, .3f, b.Metal, "MetalPipe");
        b.Box("탑승교 끝 바닥", 196f + x, 199f + x, Floor - .4f, Floor, -2.4f, 2.4f, b.Metal, "Gantry");
        b.Box("탑승교 끝 벽 북", 196f + x, 199f + x, Floor, Floor + 2.6f, 2.1f, 2.4f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 남 1", 196f + x, 196.6f + x, Floor, Floor + 2.6f, -2.4f, -2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 남 2", 198.4f + x, 199f + x, Floor, Floor + 2.6f, -2.4f, -2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 동 1", 198.7f + x, 199f + x, Floor, Floor + 2.6f, -2.1f, -1.2f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 동 2", 198.7f + x, 199f + x, Floor, Floor + 2.6f, 1.2f, 2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 지붕", 196f + x, 199f + x, Floor + 2.6f, Floor + 2.9f, -2.4f, 2.4f, b.Metal);
        b.Box("정비 계단 위", 196.4f + x, 198.6f + x, Floor - .4f, Floor, -3.6f, -2.4f, b.Metal, "Gantry");
        b.Ramp("정비 계단", new Vector3(197.5f + x, 0f, -11f), new Vector3(197.5f + x, Floor, -3.6f), 1.6f, b.Metal);
        b.Lamp(new Vector3(188f + x, Floor + 2.3f, 0f), 8f, 1.2f);
        // Between the service stairs' foot and the slide off stand 1's airliner (CampaignMaps.AirsideGimmicks): either way
        // down reaches it (CampaignSteps radius 12).
        b.Spot("Apron", new Vector3(207.4f + x, 0f, -13f));
        b.Box("계류장 담장 북", 250f, 250.3f, 0f, 6f, 46.65f, 59f);

        // 7. The apron: baggage carts, freight cans, a fuel truck, a catering truck, a stairs truck, and the control tower,
        // whose ground-floor radio reaches the transport's crew.
        foreach (float cart in new[] { 196f, 199f, 202f, 205f }) b.Box("수하물 카트", cart + x, cart + x + 2.4f, 0f, 1f, 14f, 15.4f, b.Metal, "MetalBox");
        b.Box("견인 트랙터", 207.5f + x, 210.5f + x, 0f, 1.4f, 13.7f, 15.7f, b.Metal, "Metal");
        foreach (float can in new[] { 200f, 202.2f, 204.4f, 206.6f })
            b.Box("항공 화물 컨테이너", can + x, can + x + 1.6f, 0f, 1.5f, 30f, 31.6f, b.Metal, "MetalBox");
        // The set piece (CampaignBlast, Tables/PackFx setPieceBlast): the fuel truck goes up as the tug's hold starts, 60 m
        // from the tug (50 m when the airliner is pushed all the way), and is left charred.
        // Now a gimmick (GimmickFuelTruck, the card "번지는 연료 불"): shot at any time it goes, else 35 s into the hold, and
        // its fuel spreads into a lane of fire; the blast is fired from there (no step of its own).
        var fuelTruck = b.Box("급유차", 226f + x, 235f + x, 0f, 3f, 10f, 12.6f, b.Metal, "Metal");
        var truckBlast = b.Blast("급유차 폭발", new Vector3(230.5f + x, 1f, 11.3f), "", 2.5f, "setPieceBlast", fuelTruck);
        b.Box("기내식 트럭", 205f + x, 212f + x, 0f, 3.8f, -32f, -29.5f, b.Metal, "MetalBox");
        b.Ramp("계단차 계단", new Vector3(238f + x, 0f, -21f), new Vector3(238f + x, 3.5f, -14.5f), 1.6f, b.Metal);
        b.Box("계단차 발판", 237.2f + x, 238.8f + x, 3.2f, 3.5f, -14.5f, -13f, b.Metal, "Gantry");
        Room(b, "관제탑 1층", 214f + x, 224f + x, -52f, -42f, 0f, 4f, Side.North, 217.5f + x, 220f + x);
        b.Box("관제탑 기둥", 217f + x, 221f + x, 4.3f, 26f, -49f, -45f);
        b.Box("관제탑 관제실", 214f + x, 224f + x, 26f, 30f, -52f, -42f, b.Metal, "Metal");
        b.Dress(b.Box("관제탑 무전기", 221.5f + x, 223f + x, 0f, 1.1f, -51.2f, -50.2f, b.Marker), "controlboxFit");
        b.PropSolid("table", new Vector3(217.5f + x, 0f, -50.6f));
        b.Prop("screen", new Vector3(217.5f + x, .78f, -50.8f), 180f);
        b.Prop("desk", new Vector3(218.3f + x, .78f, -50.6f), 180f);
        b.Prop("officechair", new Vector3(217.5f + x, 0f, -49.3f), 180f);
        foreach (var (fx, fz) in new[] { (215f, -43f), (223f, -43.2f) }) b.Prop("fusebox", new Vector3(fx + x, 1.2f, fz), 180f);
        b.Spot("TowerRadio", new Vector3(222.2f + x, 0f, -49.5f));
        // The apron's leftovers: crates and boxes by the catering truck and the carts.
        foreach (var (cx, cz) in new[] { (204f, -27f), (205.2f, -26.4f), (213f, -27.5f), (200f, 17f), (201.3f, 17.6f) }) b.Prop("crate", new Vector3(cx + x, 0f, cz), cx * 7f % 360f);
        foreach (var (cx, cz) in new[] { (214f, -28f), (198f, 12f), (210f, 31f) }) b.Prop("box", new Vector3(cx + x, 0f, cz), cx * 5f % 360f);
        foreach (var (cx, cz) in new[] { (196f, 20f), (233f, 9f) }) b.Prop("suitcase", new Vector3(cx + x, 0f, cz), cx * 13f % 360f);
        b.Lamp(new Vector3(219f + x, 3.4f, -47f), 8f, 1.4f);
        b.EnemySpawn("계류장 북", new Vector3(215f + x, 0f, 45f));
        b.EnemySpawn("계류장 북동", new Vector3(240f + x, 0f, 30f));
        b.EnemySpawn("계류장 남", new Vector3(232f + x, 0f, -55f));
        b.EnemySpawn("터미널 밑", new Vector3(186f + x, 0f, 25f));

        // 8. The runway, and across it the airliner that was being pushed back when everything fell: the transport can't
        // take off past it. Its tug pushes it 40 m north, off the runway, while someone stands at the tug (Ch4.09, a
        // manned hold; CampaignPushback). Its body, wings and engines hang high enough to walk under, and its gear and
        // the tug cut the walk mesh as they go.
        b.Box("활주로", 238f + x, 359f + x, 0f, .02f, -12f, 12f, b.Concrete, solid: false);
        for (float stripe = 245f; stripe < 355f; stripe += 12f) b.Box("활주로 중앙선", stripe + x, stripe + x + 6f, .02f, .03f, -.2f, .2f, b.Metal, solid: false);
        var plane = b.Pushback("버려진 여객기 (견인차로 밀어냄)", "Ch4.09", new Vector3(0f, 0f, 40f));
        b.Box("여객기 동체", 278f + x, 282f + x, 2.4f, 6.6f, -20f, 16f, b.Metal, "Metal", group: plane);
        b.Box("여객기 기수", 278.6f + x, 281.4f + x, 3f, 6f, -22f, -20f, b.Metal, "Metal", group: plane);
        b.Box("여객기 날개", 263f + x, 297f + x, 5.4f, 5.8f, -6f, 1f, b.Metal, "Metal", group: plane);
        b.Box("여객기 엔진 왼쪽", 268f + x, 271f + x, 2.6f, 5.4f, -9f, -3f, b.Metal, "Metal", group: plane);
        b.Box("여객기 엔진 오른쪽", 289f + x, 292f + x, 2.6f, 5.4f, -9f, -3f, b.Metal, "Metal", group: plane);
        b.Box("여객기 수평 꼬리", 273f + x, 287f + x, 6.6f, 6.9f, 12f, 15.5f, b.Metal, solid: false, group: plane);
        b.Box("여객기 수직 꼬리", 279.85f + x, 280.15f + x, 6.6f, 12f, 10f, 16f, b.Metal, solid: false, group: plane);
        b.Carve(b.Box("여객기 앞바퀴", 279.7f + x, 280.3f + x, 0f, 2.4f, -17.3f, -16.7f, b.Metal, "MetalPipe", group: plane));
        b.Carve(b.Box("여객기 뒷바퀴 왼쪽", 276.6f + x, 277.4f + x, 0f, 5.4f, -3.4f, -2.6f, b.Metal, "MetalPipe", group: plane));
        b.Carve(b.Box("여객기 뒷바퀴 오른쪽", 282.6f + x, 283.4f + x, 0f, 5.4f, -3.4f, -2.6f, b.Metal, "MetalPipe", group: plane));
        var tug = b.Box("견인차", 278.8f + x, 281.2f + x, 0f, 1.2f, -26f, -22.5f, b.Metal, "Metal", group: plane);
        b.Carve(tug);
        b.PushbackTug(plane, tug);
        b.Box("견인 막대", 279.9f + x, 280.1f + x, .4f, .6f, -22.5f, -17.3f, b.Metal, solid: false, group: plane);
        b.Spot("Tug", new Vector3(280f + x, 0f, -24.25f), plane);
        b.EnemySpawn("활주로 서남", new Vector3(250f + x, 0f, -45f));
        b.EnemySpawn("활주로 서북", new Vector3(250f + x, 0f, 45f));
        b.EnemySpawn("활주로 남", new Vector3(300f + x, 0f, -48f));
        b.EnemySpawn("활주로 북동", new Vector3(318f + x, 0f, 48f));

        // 9. The last transport at the runway's east end, its rear ramp down: the finale's hold is around it, the escape
        // is into its hold, and as the song ends it goes with whoever is aboard.
        b.Box("수송기 화물칸 바닥", 322f + x, 346f + x, 0f, 1.2f, -3f, 3f, b.Metal, "Gantry");
        b.Box("수송기 벽 남", 322f + x, 346f + x, 1.2f, 5.5f, -3f, -2.7f, b.Metal, "Metal");
        b.Box("수송기 벽 북", 322f + x, 346f + x, 1.2f, 5.5f, 2.7f, 3f, b.Metal, "Metal");
        b.Box("수송기 앞벽", 322f + x, 322.3f + x, 1.2f, 5.5f, -2.7f, 2.7f, b.Metal, "Metal");
        b.Box("수송기 지붕", 322f + x, 346f + x, 5.5f, 5.8f, -3f, 3f, b.Metal, "Metal");
        b.Box("수송기 기수", 318f + x, 322f + x, 1f, 4.5f, -2.2f, 2.2f, b.Metal, "Metal");
        b.Ramp("수송기 램프", new Vector3(352f + x, 0f, 0f), new Vector3(346f + x, 1.2f, 0f), 5.4f, b.Metal);
        b.Box("수송기 날개", 330f + x, 336f + x, 5.8f, 6.2f, -22f, 22f, b.Metal, "Metal");
        foreach (float z in new[] { -15.4f, -8.2f, 5.8f, 13f }) b.Box("수송기 엔진", 328f + x, 332.5f + x, 3.6f, 5.8f, z, z + 2.4f, b.Metal, "Metal");
        b.Box("수송기 바퀴 덮개 남", 328f + x, 340f + x, 0f, 1.5f, -4.2f, -3f, b.Metal, "Metal");
        b.Box("수송기 바퀴 덮개 북", 328f + x, 340f + x, 0f, 1.5f, 3f, 4.2f, b.Metal, "Metal");
        b.Box("수송기 수직 꼬리", 340f + x, 346f + x, 5.8f, 12f, -.15f, .15f, b.Metal, solid: false);
        b.Box("수송기 수평 꼬리", 341f + x, 346f + x, 10.5f, 10.8f, -9f, 9f, b.Metal, solid: false);
        b.Dress(b.Box("화물칸 짐 1", 326f + x, 328f + x, 1.2f, 2.4f, -2.2f, -.6f, b.Wood, "WoodPlank"), "crateFit");
        b.Dress(b.Box("화물칸 짐 2", 330f + x, 332.4f + x, 1.2f, 2.2f, .8f, 2.2f, b.Wood, "WoodPlank"), "crateFit");
        b.Prop("moneybag", new Vector3(329f + x, 1.2f, -1.9f), 30f);
        b.Dress(b.Box("출발 신호등", 350.6f + x, 351.4f + x, 0f, 1.3f, -5f, -4.2f, b.Marker), "controlboxFit");
        b.Spot("Transport", new Vector3(353f + x, 0f, 0f));
        b.Spot("Signal", new Vector3(351f + x, 0f, -3.6f));
        b.Spot("Cargo", new Vector3(336f + x, 1.2f, 0f));
        b.EnemySpawn("수송기 남", new Vector3(340f + x, 0f, -32f));
        b.EnemySpawn("수송기 북", new Vector3(340f + x, 0f, 32f));
        b.EnemySpawn("활주로 동남", new Vector3(356f + x, 0f, -20f));
        b.EnemySpawn("활주로 동북", new Vector3(356f + x, 0f, 20f));
        // The airside's gimmicks: the jet bridge, the stands and the fueled airliner, the fuel truck's fire, the cargo
        // door and the supplies for greed (CampaignMaps.AirsideGimmicks).
        AirsideGimmicks(b, x, truckBlast, fuelTruck);

        // Background sound by area (rows in Tables/Ambience; the runway and the road keep the map's own): the pier, the
        // terminal's ground-floor halls, its upper concourse and the gate hall, the duty-free shop, the jet bridge, the
        // apron's machinery, the tower's radio room. The terminal's reflections are the pack's own probes.
        b.Ambience("Ch4.Pier", -22f, 20f, -1f, 10f, -60f, 60f, 0);
        b.Ambience("Ch4.Hall", 70f, 110f, -1f, 8f, 6.45f, 46.65f);
        b.Ambience("Ch4.Hall", 106f, 134f, -1f, 3.5f, -41.35f, 10.45f);
        b.Ambience("Ch4.Concourse", 106f, 254f, 3.5f, 14f, -81.35f, -6.35f);
        b.Ambience("Ch4.Concourse", 214f, 267f, -1f, 14f, -13f, 46.65f);
        b.Ambience("Ch4.DutyFree", 201.7f, 222.75f, Floor, Floor + 4f, -81.35f, -70.55f);
        b.Ambience("Ch4.Bridge", 180.3f + x, 199f + x, Floor - .5f, Floor + 3f, -2.4f, 2.4f);
        b.Ambience("Ch4.Apron", 180f + x, 250f + x, -1f, 12f, -60f, 60f, 0);
        b.Ambience("Ch4.Tower", 214f + x, 224f + x, 0f, 4f, -52f, -42f);

        // Field pickups (6D): a gun at the pier, upstairs by the lounge's escalators, in the rest stop and in the gate
        // hall, supplies along the way and in the transport's hold for the finale.
        b.Pickup(new Vector3(-8f, .3f, 2.5f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(6f, 0f, 9f));
        b.Pickup(new Vector3(60f, 0f, 38f));
        b.Pickup(new Vector3(76f, 0f, 44.65f));
        b.Pickup(new Vector3(104f, 0f, 10.65f));
        b.Pickup(new Vector3(124f, Floor, -49.35f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(213f, Floor, -79.55f));
        b.Pickup(new Vector3(216f, Floor, -79.55f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(239f, Floor, -28.35f));
        b.Pickup(new Vector3(239f, 0f, 36.65f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(197.5f + x, 0f, -14.5f));
        b.Pickup(new Vector3(219f + x, 0f, -46f));
        b.Pickup(new Vector3(262f + x, 0f, -20f));
        b.Pickup(new Vector3(336f + x, 1.2f, 1.6f));
        b.Pickup(new Vector3(350f + x, 0f, 8f));
        b.Shot(new Vector3(-30f, 80f, -130f), new Vector3(190f, 0f, -10f));
        DressAirport(b, x);
        b.Finish("4장 공항을 지었어요 (Scenes/Campaign/Ch4_Airport).\n" +
                 "길: 화물 부두 → 화물장 → 터미널 출발 입구 → 입구 복도의 보안 검색대 → 입구 홀과 라운지 → 에스컬레이터로 2층 → 체크인 홀과 상가 → " +
                 "면세점(휴게소) → 식당가 → 남동쪽 회랑 끝 탑승 대기실 → 탑승교와 정비 계단 → 계류장 → 관제탑 무전 → " +
                 "활주로를 막은 여객기를 견인차로 밀어내기(누가 견인차 옆에 있어야 움직임) → 수송기 → 출발 신호 → 버티기 → 탑승.\n" +
                 "터미널은 Airport Terminal Scene 팩의 장면 그대로예요(Assets/ThirdParty/Airport_Terminal_Scene/Scene).");
    }

    // Chapter 4's cargo yard, landside road, apron and the transport's evacuation point in the packs' props (map dressing
    // stage 3); the terminal is the pack's own, furnished as it came. The runway's middle, where the airliner is pushed
    // back, is left clear. <paramref name="x"/>: how far east the airside stands (Airport).
    private static void DressAirport(GreyboxBuilder b, float x)
    {
        // Cargo yard.
        foreach (var (px, pz) in new[] { (14f, 9f), (14.7f, 9.6f), (13.6f, 10.2f) }) b.Prop("drum", new Vector3(px, 0f, pz), px * 7f % 360f);
        b.PropSolid("palletload", new Vector3(16f, 0f, -6f), 0f);
        foreach (var (px, pz) in new[] { (10f, 7.5f), (11.2f, 7.9f) }) b.Prop("woodcrate", new Vector3(px, 0f, pz), px * 13f % 360f);
        b.Prop("plasticcrate", new Vector3(2.5f, 0f, 8.6f), 30f);
        b.Prop("tirestack", new Vector3(-4f, 0f, 18f), 0f);
        foreach (var (px, pz) in new[] { (15f, -10.5f), (15.6f, -11f) }) b.Prop("gascylinder", new Vector3(px, 0f, pz), px * 11f % 360f);
        b.Prop("streetlamp", new Vector3(18.5f, 0f, -20f), 270f);
        b.Prop("streetlamp", new Vector3(18.5f, 0f, 20f), 270f);
        // Landside road and the drop-off before the entrance.
        b.PropSolid("policecar", new Vector3(30f, 0f, 22f), 10f);
        foreach (var z in new[] { -30f, -27f, -24f }) b.Prop("jersey", new Vector3(34f, 0f, z), 90f);
        foreach (var (px, pz) in new[] { (31f, -6f), (32f, 6f), (40f, -2f) }) b.Prop("cone", new Vector3(px, 0f, pz), px * 17f % 360f);
        b.Prop("trafficdrum", new Vector3(36f, 0f, 9f));
        foreach (var z in new[] { -30f, -10f, 10f, 30f }) b.Prop("streetlamp", new Vector3(46f, 0f, z), 90f);
        foreach (var (px, pz) in new[] { (66f, 40.5f), (64.5f, 49f) }) b.Prop("suitcase", new Vector3(px, 0f, pz), px * 31f % 360f);
        b.Prop("trashbag", new Vector3(68f, 0f, 38f), 60f);
        // Apron.
        foreach (var (px, pz) in new[] { (205f, -20f), (206.2f, -19.4f), (226f, 12f), (227f, 12.8f) }) b.Prop("cone", new Vector3(px + x, 0f, pz), px * 19f % 360f);
        foreach (var (px, pz) in new[] { (196f, 30f), (196.8f, 30.6f) }) b.Prop("drum", new Vector3(px + x, 0f, pz), px * 23f % 360f);
        b.PropSolid("palletload", new Vector3(234f + x, 0f, -32f), 0f);
        b.Prop("tirestack", new Vector3(238f + x, 0f, -48f), 0f);
        b.PropSolid("militarytruck", new Vector3(228f + x, 0f, -30f), 90f);
        b.Prop("streetlamp", new Vector3(208f + x, 0f, 38f), 180f);
        b.Prop("streetlamp", new Vector3(236f + x, 0f, -38f), 0f);
        // The transport's evacuation point: HESCO walls and sandbags round the hold, barbed wire, guard towers, lorries.
        b.Dress(b.Box("HESCO 수송기 남", 318f + x, 334f + x, 0f, 2f, -30f, -28.1f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("HESCO 수송기 북", 318f + x, 334f + x, 0f, 2f, 28.1f, 30f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("모래주머니 수송기 남", 312f + x, 313f + x, 0f, 1.2f, -20f, -12f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("모래주머니 수송기 북", 312f + x, 313f + x, 0f, 1.2f, 12f, 20f, b.Wood, "Tarp"), "sandbags");
        for (float wire = 319f; wire <= 333f; wire += 1.7f) b.Prop("barbedwire", new Vector3(wire + x, 0f, -27f), 0f);
        for (float wire = 319f; wire <= 333f; wire += 1.7f) b.Prop("barbedwire", new Vector3(wire + x, 0f, 27f), 0f);
        b.PropSolid("guardtower", new Vector3(316f + x, 0f, -34f), 0f);
        b.PropSolid("guardtower", new Vector3(316f + x, 0f, 34f), 180f);
        b.PropSolid("militarytruck", new Vector3(350f + x, 0f, -18f), 0f);
        b.PropSolid("wrecktruck", new Vector3(352f + x, 0f, 20f), 200f);
        b.Prop("streetlamp", new Vector3(312f + x, 0f, -26f), 90f);
        b.Prop("streetlamp", new Vector3(312f + x, 0f, 26f), 270f);
        DenseAirport(b, x);
    }

    // Chapter 4's density (map dressing stage 5): what was dropped along the airport's walls by the landside, along the
    // cargo yard's river edge (clear of the pier) and the airfield's walls by the apron. The terminal's own clutter is
    // the pack's.
    private static void DenseAirport(GreyboxBuilder b, float x)
    {
        string[] yard = { "tire", "pallet", "oilbarrel", "plasticcrate", "cardboard" };
        string[] apron = { "tire", "oilbarrel", "pallet", "cardboard", "trashbag" };
        string[] landside = { "trashbag", "litter", "cardboard", "tire" };
        b.Clutter(new Vector3(69.6f, 0f, 48f), new Vector3(69.6f, 0f, 58f), 4f, landside);
        b.Clutter(new Vector3(69.6f, 0f, -58f), new Vector3(69.6f, 0f, -42f), 4f, landside);
        b.Clutter(new Vector3(69.6f, 0f, -20f), new Vector3(69.6f, 0f, 2f), 4f, landside);
        b.Clutter(new Vector3(-20.3f, 0f, -56f), new Vector3(-20.3f, 0f, -9f), 5f, yard);
        b.Clutter(new Vector3(-20.3f, 0f, 9f), new Vector3(-20.3f, 0f, 56f), 5f, yard);
        foreach (float z in new[] { 58.3f, -58.3f }) b.Clutter(new Vector3(184f + x, 0f, z), new Vector3(356f + x, 0f, z), 6f, apron);
    }
}
