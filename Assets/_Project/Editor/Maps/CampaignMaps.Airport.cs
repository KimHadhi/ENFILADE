using UnityEditor;
using UnityEngine;

// CampaignMaps: chapter 4, the airport, with its dressing. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    [MenuItem("ENFILADE/맵/캠페인/4장 공항", priority = 14)]
    private static void Airport()
    {
        var b = GreyboxBuilder.Begin("Ch4_Airport", "4장 공항");
        if (b == null) return;
        // The terminal stands on a podium 4 m up, as departures do; the apron and the runway are at the ground.
        const float Floor = 4f, Roof = 16f, Upper = Floor + 5.4f;

        // The ground and the edge of the map: the river to the west, the airfield's wall on the other sides.
        b.Box("땅", -22f, 360f, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("맵 끝 서 (강)", -22f, -21f, 0f, 6f, -60f, 60f);
        b.Box("맵 끝 동", 359f, 360f, 0f, 8f, -60f, 60f);
        b.Box("맵 끝 남", -22f, 360f, 0f, 8f, -60f, -59f);
        b.Box("맵 끝 북", -22f, 360f, 0f, 8f, 59f, 60f);

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

        // 2. The landside road and the ramp up to the departures curb, where the terminal's two doors are. A wall from the
        // terminal's corners to the map's edges keeps everyone going through the terminal, not around it.
        Car(b, 27f, 0f, -14f); Car(b, 30f, 0f, 17f); Car(b, 41f, 0f, -24f); Car(b, 44f, 0f, 22f);
        b.Ramp("출발층 진입로", new Vector3(32f, 0f, 0f), new Vector3(48f, Floor, 0f), 12f, b.Road);
        b.Box("출발층 도로", 48f, 60f, 0f, Floor, -40.3f, 40.3f, b.Road, "Asphalt");
        Car(b, 53f, Floor, -22f); Car(b, 55f, Floor, 24f);
        b.Box("공항 셔틀버스", 50.5f, 53.1f, Floor, Floor + 3.2f, -37f, -25f, b.Metal, "Metal");
        b.Box("공항 담장 북", 60f, 60.3f, 0f, 6f, 40.3f, 59f);
        b.Box("공항 담장 남", 60f, 60.3f, 0f, 6f, -59f, -40.3f);
        b.Spot("Departures", new Vector3(57f, Floor, 0f));
        b.EnemySpawn("도로 북", new Vector3(38f, 0f, 45f));
        b.EnemySpawn("도로 남", new Vector3(38f, 0f, -45f));
        b.EnemySpawn("출발층 북", new Vector3(54f, Floor, 36f));

        // 3. The terminal: the departure hall with check-in islands, the information desk and a food court upstairs
        // reached by a stopped escalator. Its roof keeps the sun out, so lamps light it.
        b.Box("터미널 바닥", 60f, 180.3f, 0f, Floor, -40.3f, 40.3f, b.Concrete, "Tile");
        b.Box("터미널 지붕", 60f, 180.3f, Roof, Roof + .5f, -40.3f, 40.3f);
        b.Box("터미널 벽 북", 60f, 180.3f, Floor, Roof, 40f, 40.3f);
        b.Box("터미널 벽 남", 60f, 180.3f, Floor, Roof, -40.3f, -40f);
        b.Box("정면 벽 1", 60f, 60.3f, Floor, Roof, -40f, -10f);
        b.Box("정면 벽 2", 60f, 60.3f, Floor, Roof, -6f, 6f);
        b.Box("정면 벽 3", 60f, 60.3f, Floor, Roof, 10f, 40f);
        b.Box("정면 문 위 1", 60f, 60.3f, Floor + 3f, Roof, -10f, -6f);
        b.Box("정면 문 위 2", 60f, 60.3f, Floor + 3f, Roof, 6f, 10f);
        // Dressed with the Airport Terminal Scene pack (GreyboxBuilder.Dress, Tables/MapArt): the boxes stay the collision.
        b.Dress(b.Box("안내 데스크", 66f, 70f, Floor, Floor + 1.1f, -2f, 2f, b.Wood, "Wood"), "infodesk");
        foreach (float z in new[] { -26f, -12f, 2f })
        {
            b.Dress(b.Box("체크인 카운터", 76f, 100f, Floor, Floor + 1.1f, z, z + 2.4f, b.Wood, "Wood"), "checkin");
            for (float x = 80f; x <= 96f; x += 8f) b.Hang("signCheckin", new Vector3(x, Floor + 4.6f, z + 1.2f));
            // The queue's posts on the hall side of each island.
            for (float x = 78f; x <= 98f; x += 2.5f) b.Prop("linepost", new Vector3(x, Floor, z - 1.6f));
        }
        b.Box("포장기", 104f, 105.2f, Floor, Floor + 1.4f, -4f, -2.8f, b.Metal, "Metal");
        foreach (float z in new[] { -36f, -32f }) b.Dress(b.Box("홀 의자 줄", 104f, 116f, Floor, Floor + .5f, z, z + .6f, b.Metal, "Metal"), "seats");
        // Left behind when everyone ran: luggage, trolleys, bins and rubbish (seen only, walked through, but the trolleys,
        // bins and plants block), vending machines and an ATM by the walls, and the hall's ceiling lamps.
        foreach (var (x, z) in new[] { (73f, -20f), (74.5f, -19.2f), (83f, -15f), (92f, -15.5f), (78f, -1f), (95f, -2f), (101f, 6f), (86f, 8f),
                     (110f, -10f), (65f, 12f), (71f, -30f), (104f, -20f) })
            b.Prop("suitcase", new Vector3(x, Floor, z), x * 13f % 360f);
        foreach (var (x, z) in new[] { (72f, -8f), (102f, -14f), (105f, 9f), (90f, 20f) }) b.PropSolid("trolley", new Vector3(x, Floor, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (89.3f, -18f), (109.3f, -18f), (89.3f, 14f), (109.3f, 14f) }) b.PropSolid("bin", new Vector3(x, Floor, z));
        foreach (var (x, z) in new[] { (61f, -38f), (62.2f, -37.2f), (118f, -38.5f), (61f, 38f), (118f, 26f), (116.8f, 26.8f) })
            b.Prop("garbage", new Vector3(x, Floor, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (64f, -38.5f), (115f, -37.5f), (116.2f, -36.2f) }) b.Prop("box", new Vector3(x, Floor, z), x * 5f % 360f);
        b.PropSolid("vending", new Vector3(96f, Floor, -39.4f));
        b.PropSolid("vending", new Vector3(98f, Floor, -39.4f));
        b.PropSolid("atm", new Vector3(61f, Floor, -14f), 90f);
        foreach (var (x, z) in new[] { (61.2f, -11.5f), (61.2f, 11.5f), (61.2f, -4.5f), (61.2f, 4.5f) }) b.PropSolid("plant", new Vector3(x, Floor, z));
        foreach (float x in new[] { 88f, 108f })
            foreach (float z in new[] { -18f, 14f })
                b.Box("홀 기둥", x - .6f, x + .6f, Floor, Roof, z - .6f, z + .6f);
        b.Box("2층 식당가", 64f, 116f, Upper - .4f, Upper, 28f, 40f, b.Concrete, "Tile");
        b.Ramp("멈춘 에스컬레이터", new Vector3(70f, Floor, 14f), new Vector3(70f, Upper, 28f), 3f, b.Metal);
        b.Box("2층 난간 1", 64f, 68.5f, Upper, Upper + 1f, 28f, 28.2f, b.Metal, "MetalPipe");
        b.Box("2층 난간 2", 71.5f, 116f, Upper, Upper + 1f, 28f, 28.2f, b.Metal, "MetalPipe");
        foreach (float x in new[] { 80f, 96f, 112f }) b.Box("2층 기둥", x - .4f, x + .4f, Floor, Upper - .4f, 28.2f, 29f);
        b.Dress(b.Box("식당 계산대", 84f, 92f, Upper, Upper + 1.1f, 37f, 38.2f, b.Wood, "Wood"), "glasscounter");
        foreach (float x in new[] { 76f, 82f, 98f, 104f })
        {
            b.Dress(b.Box("식탁", x, x + 1f, Upper, Upper + .75f, 32f, 33f, b.Wood, "Wood"), "table");
            b.Prop("chair", new Vector3(x + .5f, Upper, 31.4f), 0f);
            b.Prop("chair", new Vector3(x + .5f, Upper, 33.6f), 180f);
        }
        b.EnemySpawn("홀 서남", new Vector3(66f, Floor, -34f));
        b.EnemySpawn("홀 동남", new Vector3(112f, Floor, -26f));
        b.EnemySpawn("홀 동북", new Vector3(114f, Floor, 22f));
        b.EnemySpawn("홀 2층", new Vector3(100f, Upper, 35f));

        // 4. The security line across the terminal: four lanes between the scanners, a detector frame in each.
        foreach (var (z0, z1) in new[] { (-40f, -14f), (-10f, -6f), (-2f, 2f), (6f, 10f), (14f, 40f) })
            b.Box("검색대 칸막이", 120f, 120.3f, Floor, Floor + 4f, z0, z1);
        foreach (float lane in new[] { -12f, -4f, 4f, 12f })
        {
            b.Dress(b.Box("엑스레이 검색기", 120.3f, 125.5f, Floor, Floor + 1.2f, lane - 2f, lane - 1f, b.Metal, "Metal"), "xray");
            // The walk-through detector, seen only: everyone walks through it.
            b.Prop("bodyscanner", new Vector3(126.65f, Floor, lane), 90f);
            b.Prop("linepost", new Vector3(117.5f, Floor, lane - 1.2f));
            b.Prop("linepost", new Vector3(117.5f, Floor, lane + 1.2f));
        }
        b.Spot("Security", new Vector3(129f, Floor, 0f));

        // 5. Airside: the duty-free shop is the rest stop (its door, its till), a café, the gates' seats, a stopped moving
        // walkway, and the gate desk by the jet bridge's door.
        Room(b, "면세점", 140f, 154f, 26f, 40f, Floor, 4f, Side.South, 145f, 147.5f);
        b.Door("면세점 문", 145f, 147.5f, Floor, Floor + 2.4f, 26.05f, 26.25f, "DutyFree", new Vector3(2.5f, 0f, 0f));
        b.Dress(b.Box("면세점 계산대", 148f, 152f, Floor, Floor + 1f, 35f, 36f, b.Wood, "Wood"), "shopcounter");
        b.Prop("cashregister", new Vector3(149f, Floor + 1f, 35.5f), 180f);
        b.Prop("perfume", new Vector3(151f, Floor + 1f, 35.4f));
        b.Prop("whiskey", new Vector3(151.6f, Floor + 1f, 35.6f));
        b.Shop(new Vector3(150f, Floor, 34.3f), new Vector3(150f, Floor + 2.2f, 36f));
        b.Dress(b.Box("면세점 진열대", 141f, 141.8f, Floor, Floor + 1.4f, 29f, 37f, b.Metal, "Metal"), "shopshelf");
        b.PropSolid("glassshelf", new Vector3(152.6f, Floor, 29.5f), 270f);
        b.PropSolid("glassshelf", new Vector3(152.6f, Floor, 31.5f), 270f);
        b.Hang("banner", new Vector3(146.25f, Floor + 3.6f, 25.8f));
        b.Spot("DutyFree", new Vector3(147f, Floor, 32f));
        b.Lamp(new Vector3(147f, Floor + 3.5f, 33f), 9f, 1.6f);
        b.Dress(b.Box("카페 계산대", 128f, 134f, Floor, Floor + 1.1f, -38f, -36.8f, b.Wood, "Wood"), "glasscounter");
        foreach (float x in new[] { 130f, 134f })
        {
            b.PropSolid("table", new Vector3(x, Floor, -33.5f));
            b.Prop("chair", new Vector3(x, Floor, -34.4f), 0f);
            b.Prop("chair", new Vector3(x, Floor, -32.6f), 180f);
        }
        b.Box("멈춘 무빙워크", 128f, 170f, Floor, Floor + .15f, -31f, -29.6f, b.Metal, "Metal");
        foreach (float z in new[] { -24f, -20f, -16f, 12f, 16f })
            b.Dress(b.Box("탑승구 의자 줄", 158f, 176f, Floor, Floor + .5f, z, z + .6f, b.Metal, "Metal"), "seats");
        b.Dress(b.Box("탑승구 데스크", 172f, 175f, Floor, Floor + 1.1f, 5f, 6.2f, b.Wood, "Wood"), "boardingdesk");
        b.Spot("Gate", new Vector3(177f, Floor, 0f));
        b.Hang("signGates", new Vector3(150f, Floor + 4.2f, 0f), 90f);
        b.Hang("signGates", new Vector3(175f, Floor + 3.4f, -3f), 90f);
        b.Hang("signExit", new Vector3(63f, Floor + 3.4f, 0f), 270f);
        foreach (var (x, z) in new[] { (160f, -22f), (163f, -21.6f), (167f, -18f), (171f, -14f), (161f, 14f), (169f, 18f), (174f, 10f), (139f, -6f) })
            b.Prop("suitcase", new Vector3(x, Floor, z), x * 17f % 360f);
        foreach (var (x, z) in new[] { (165f, -26f), (157f, 10f), (170f, 14.5f), (138f, 8f) }) b.Prop("bag", new Vector3(x, Floor, z), x * 9f % 360f);
        foreach (var (x, z) in new[] { (156f, -12f), (156f, 20f), (137f, -20f) }) b.PropSolid("bin", new Vector3(x, Floor, z));
        foreach (var (x, z) in new[] { (179f, -38f), (178f, -37f), (127f, 38f), (179f, 38f) }) b.Prop("garbage", new Vector3(x, Floor, z), x * 11f % 360f);
        b.PropSolid("vending", new Vector3(160f, Floor, 39.4f), 180f);
        b.PropSolid("vending", new Vector3(162f, Floor, 39.4f), 180f);
        foreach (var (x, z) in new[] { (154.5f, -10f), (154.5f, 8f) }) b.PropSolid("plant", new Vector3(x, Floor, z));
        foreach (var (x, z) in new[] { (72f, -18f), (72f, 20f), (96f, -30f), (96f, -4f), (110f, 20f), (132f, -14f), (150f, 8f), (166f, -24f), (166f, 22f) })
        {
            b.Lamp(new Vector3(x, Roof - 1f, z), 20f, 2.4f);
            b.Hang("lamp", new Vector3(x, Roof, z));
        }
        b.EnemySpawn("대합실 남", new Vector3(136f, Floor, -24f));
        b.EnemySpawn("대합실 동남", new Vector3(172f, Floor, -35f));
        b.EnemySpawn("대합실 북", new Vector3(166f, Floor, 34f));

        // 6. The gate's jet bridge: its plane is gone, so it ends in the air over the apron, and its service stairs go down.
        b.Box("동쪽 벽 1", 180f, 180.3f, Floor, Roof, -40f, -2f);
        b.Box("동쪽 벽 2", 180f, 180.3f, Floor, Roof, 2f, 40f);
        b.Box("탑승구 문 위", 180f, 180.3f, Floor + 2.6f, Roof, -2f, 2f);
        b.Box("탑승교 바닥", 180.3f, 196f, Floor - .4f, Floor, -1.6f, 1.6f, b.Metal, "Gantry");
        b.Box("탑승교 벽 남", 180.3f, 196f, Floor, Floor + 2.6f, -1.9f, -1.6f, b.Metal, "Metal");
        b.Box("탑승교 벽 북", 180.3f, 196f, Floor, Floor + 2.6f, 1.6f, 1.9f, b.Metal, "Metal");
        b.Box("탑승교 지붕", 180.3f, 196f, Floor + 2.6f, Floor + 2.9f, -1.9f, 1.9f, b.Metal);
        b.Box("탑승교 다리", 191.7f, 192.3f, 0f, Floor - .4f, -.3f, .3f, b.Metal, "MetalPipe");
        b.Box("탑승교 끝 바닥", 196f, 199f, Floor - .4f, Floor, -2.4f, 2.4f, b.Metal, "Gantry");
        b.Box("탑승교 끝 벽 북", 196f, 199f, Floor, Floor + 2.6f, 2.1f, 2.4f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 남 1", 196f, 196.6f, Floor, Floor + 2.6f, -2.4f, -2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 남 2", 198.4f, 199f, Floor, Floor + 2.6f, -2.4f, -2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 동 1", 198.7f, 199f, Floor, Floor + 2.6f, -2.1f, -1.2f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 동 2", 198.7f, 199f, Floor, Floor + 2.6f, 1.2f, 2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 지붕", 196f, 199f, Floor + 2.6f, Floor + 2.9f, -2.4f, 2.4f, b.Metal);
        b.Box("정비 계단 위", 196.4f, 198.6f, Floor - .4f, Floor, -3.6f, -2.4f, b.Metal, "Gantry");
        b.Ramp("정비 계단", new Vector3(197.5f, 0f, -11f), new Vector3(197.5f, Floor, -3.6f), 1.6f, b.Metal);
        b.Lamp(new Vector3(188f, Floor + 2.3f, 0f), 8f, 1.2f);
        b.Spot("Apron", new Vector3(197.5f, 0f, -12.5f));

        // 7. The apron: baggage carts, freight cans, a fuel truck, a catering truck, a stairs truck, and the control tower,
        // whose ground-floor radio reaches the transport's crew.
        foreach (float x in new[] { 196f, 199f, 202f, 205f }) b.Box("수하물 카트", x, x + 2.4f, 0f, 1f, 14f, 15.4f, b.Metal, "MetalBox");
        b.Box("견인 트랙터", 207.5f, 210.5f, 0f, 1.4f, 13.7f, 15.7f, b.Metal, "Metal");
        foreach (float x in new[] { 200f, 202.2f, 204.4f, 206.6f })
            b.Box("항공 화물 컨테이너", x, x + 1.6f, 0f, 1.5f, 30f, 31.6f, b.Metal, "MetalBox");
        // The set piece (CampaignBlast, Tables/PackFx setPieceBlast): the fuel truck goes up as the tug's hold starts, 60 m
        // from the tug (50 m when the airliner is pushed all the way), and is left charred.
        var fuelTruck = b.Box("급유차", 226f, 235f, 0f, 3f, 10f, 12.6f, b.Metal, "Metal");
        b.Blast("급유차 폭발", new Vector3(230.5f, 1f, 11.3f), "Ch4.09", 2.5f, "setPieceBlast", fuelTruck);
        b.Box("기내식 트럭", 205f, 212f, 0f, 3.8f, -32f, -29.5f, b.Metal, "MetalBox");
        b.Ramp("계단차 계단", new Vector3(238f, 0f, -21f), new Vector3(238f, 3.5f, -14.5f), 1.6f, b.Metal);
        b.Box("계단차 발판", 237.2f, 238.8f, 3.2f, 3.5f, -14.5f, -13f, b.Metal, "Gantry");
        Room(b, "관제탑 1층", 214f, 224f, -52f, -42f, 0f, 4f, Side.North, 217.5f, 220f);
        b.Box("관제탑 기둥", 217f, 221f, 4.3f, 26f, -49f, -45f);
        b.Box("관제탑 관제실", 214f, 224f, 26f, 30f, -52f, -42f, b.Metal, "Metal");
        b.Dress(b.Box("관제탑 무전기", 221.5f, 223f, 0f, 1.1f, -51.2f, -50.2f, b.Marker), "controlboxFit");
        b.PropSolid("table", new Vector3(217.5f, 0f, -50.6f));
        b.Prop("screen", new Vector3(217.5f, .78f, -50.8f), 180f);
        b.Prop("desk", new Vector3(218.3f, .78f, -50.6f), 180f);
        b.Prop("officechair", new Vector3(217.5f, 0f, -49.3f), 180f);
        foreach (var (x, z) in new[] { (215f, -43f), (223f, -43.2f) }) b.Prop("fusebox", new Vector3(x, 1.2f, z), 180f);
        b.Spot("TowerRadio", new Vector3(222.2f, 0f, -49.5f));
        // The apron's leftovers: crates and boxes by the catering truck and the carts.
        foreach (var (x, z) in new[] { (204f, -27f), (205.2f, -26.4f), (213f, -27.5f), (200f, 17f), (201.3f, 17.6f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (214f, -28f), (198f, 12f), (210f, 31f) }) b.Prop("box", new Vector3(x, 0f, z), x * 5f % 360f);
        foreach (var (x, z) in new[] { (196f, 20f), (233f, 9f) }) b.Prop("suitcase", new Vector3(x, 0f, z), x * 13f % 360f);
        b.Lamp(new Vector3(219f, 3.4f, -47f), 8f, 1.4f);
        b.EnemySpawn("계류장 북", new Vector3(215f, 0f, 45f));
        b.EnemySpawn("계류장 북동", new Vector3(240f, 0f, 30f));
        b.EnemySpawn("계류장 남", new Vector3(232f, 0f, -55f));
        b.EnemySpawn("터미널 밑", new Vector3(186f, 0f, 25f));

        // 8. The runway, and across it the airliner that was being pushed back when everything fell: the transport can't
        // take off past it. Its tug pushes it 40 m north, off the runway, while someone stands at the tug (Ch4.09, a
        // manned hold; CampaignPushback). Its body, wings and engines hang high enough to walk under, and its gear and
        // the tug cut the walk mesh as they go.
        b.Box("활주로", 238f, 359f, 0f, .02f, -12f, 12f, b.Concrete, solid: false);
        for (float x = 245f; x < 355f; x += 12f) b.Box("활주로 중앙선", x, x + 6f, .02f, .03f, -.2f, .2f, b.Metal, solid: false);
        var plane = b.Pushback("버려진 여객기 (견인차로 밀어냄)", "Ch4.09", new Vector3(0f, 0f, 40f));
        b.Box("여객기 동체", 278f, 282f, 2.4f, 6.6f, -20f, 16f, b.Metal, "Metal", group: plane);
        b.Box("여객기 기수", 278.6f, 281.4f, 3f, 6f, -22f, -20f, b.Metal, "Metal", group: plane);
        b.Box("여객기 날개", 263f, 297f, 5.4f, 5.8f, -6f, 1f, b.Metal, "Metal", group: plane);
        b.Box("여객기 엔진 왼쪽", 268f, 271f, 2.6f, 5.4f, -9f, -3f, b.Metal, "Metal", group: plane);
        b.Box("여객기 엔진 오른쪽", 289f, 292f, 2.6f, 5.4f, -9f, -3f, b.Metal, "Metal", group: plane);
        b.Box("여객기 수평 꼬리", 273f, 287f, 6.6f, 6.9f, 12f, 15.5f, b.Metal, solid: false, group: plane);
        b.Box("여객기 수직 꼬리", 279.85f, 280.15f, 6.6f, 12f, 10f, 16f, b.Metal, solid: false, group: plane);
        b.Carve(b.Box("여객기 앞바퀴", 279.7f, 280.3f, 0f, 2.4f, -17.3f, -16.7f, b.Metal, "MetalPipe", group: plane));
        b.Carve(b.Box("여객기 뒷바퀴 왼쪽", 276.6f, 277.4f, 0f, 5.4f, -3.4f, -2.6f, b.Metal, "MetalPipe", group: plane));
        b.Carve(b.Box("여객기 뒷바퀴 오른쪽", 282.6f, 283.4f, 0f, 5.4f, -3.4f, -2.6f, b.Metal, "MetalPipe", group: plane));
        var tug = b.Box("견인차", 278.8f, 281.2f, 0f, 1.2f, -26f, -22.5f, b.Metal, "Metal", group: plane);
        b.Carve(tug);
        b.PushbackTug(plane, tug);
        b.Box("견인 막대", 279.9f, 280.1f, .4f, .6f, -22.5f, -17.3f, b.Metal, solid: false, group: plane);
        b.Spot("Tug", new Vector3(280f, 0f, -24.25f), plane);
        b.EnemySpawn("활주로 서남", new Vector3(250f, 0f, -45f));
        b.EnemySpawn("활주로 서북", new Vector3(250f, 0f, 45f));
        b.EnemySpawn("활주로 남", new Vector3(300f, 0f, -48f));
        b.EnemySpawn("활주로 북동", new Vector3(318f, 0f, 48f));

        // 9. The last transport at the runway's east end, its rear ramp down: the finale's hold is around it, the escape
        // is into its hold, and as the song ends it goes with whoever is aboard.
        b.Box("수송기 화물칸 바닥", 322f, 346f, 0f, 1.2f, -3f, 3f, b.Metal, "Gantry");
        b.Box("수송기 벽 남", 322f, 346f, 1.2f, 5.5f, -3f, -2.7f, b.Metal, "Metal");
        b.Box("수송기 벽 북", 322f, 346f, 1.2f, 5.5f, 2.7f, 3f, b.Metal, "Metal");
        b.Box("수송기 앞벽", 322f, 322.3f, 1.2f, 5.5f, -2.7f, 2.7f, b.Metal, "Metal");
        b.Box("수송기 지붕", 322f, 346f, 5.5f, 5.8f, -3f, 3f, b.Metal, "Metal");
        b.Box("수송기 기수", 318f, 322f, 1f, 4.5f, -2.2f, 2.2f, b.Metal, "Metal");
        b.Ramp("수송기 램프", new Vector3(352f, 0f, 0f), new Vector3(346f, 1.2f, 0f), 5.4f, b.Metal);
        b.Box("수송기 날개", 330f, 336f, 5.8f, 6.2f, -22f, 22f, b.Metal, "Metal");
        foreach (float z in new[] { -15.4f, -8.2f, 5.8f, 13f }) b.Box("수송기 엔진", 328f, 332.5f, 3.6f, 5.8f, z, z + 2.4f, b.Metal, "Metal");
        b.Box("수송기 바퀴 덮개 남", 328f, 340f, 0f, 1.5f, -4.2f, -3f, b.Metal, "Metal");
        b.Box("수송기 바퀴 덮개 북", 328f, 340f, 0f, 1.5f, 3f, 4.2f, b.Metal, "Metal");
        b.Box("수송기 수직 꼬리", 340f, 346f, 5.8f, 12f, -.15f, .15f, b.Metal, solid: false);
        b.Box("수송기 수평 꼬리", 341f, 346f, 10.5f, 10.8f, -9f, 9f, b.Metal, solid: false);
        b.Dress(b.Box("화물칸 짐 1", 326f, 328f, 1.2f, 2.4f, -2.2f, -.6f, b.Wood, "WoodPlank"), "crateFit");
        b.Dress(b.Box("화물칸 짐 2", 330f, 332.4f, 1.2f, 2.2f, .8f, 2.2f, b.Wood, "WoodPlank"), "crateFit");
        b.Prop("moneybag", new Vector3(329f, 1.2f, -1.9f), 30f);
        b.Dress(b.Box("출발 신호등", 350.6f, 351.4f, 0f, 1.3f, -5f, -4.2f, b.Marker), "controlboxFit");
        b.Spot("Transport", new Vector3(353f, 0f, 0f));
        b.Spot("Signal", new Vector3(351f, 0f, -3.6f));
        b.Spot("Cargo", new Vector3(336f, 1.2f, 0f));
        b.EnemySpawn("수송기 남", new Vector3(340f, 0f, -32f));
        b.EnemySpawn("수송기 북", new Vector3(340f, 0f, 32f));
        b.EnemySpawn("활주로 동남", new Vector3(356f, 0f, -20f));
        b.EnemySpawn("활주로 동북", new Vector3(356f, 0f, 20f));

        // Background sound by area (rows in Tables/Ambience; the runway and the road keep the map's own): the pier, the
        // hall, the concourse, the duty-free shop, the jet bridge, the apron's machinery, the tower's radio room.
        b.Ambience("Ch4.Pier", -22f, 20f, -1f, 10f, -60f, 60f, 0);
        b.Ambience("Ch4.Hall", 60f, 120f, Floor - 1f, Roof, -40f, 40f);
        b.Ambience("Ch4.Concourse", 120f, 180f, Floor - 1f, Roof, -40f, 40f);
        b.Ambience("Ch4.DutyFree", 140f, 154f, Floor, Floor + 4f, 26f, 40f);
        b.Ambience("Ch4.Bridge", 180.3f, 199f, Floor - .5f, Floor + 3f, -2.4f, 2.4f);
        b.Ambience("Ch4.Apron", 180f, 250f, -1f, 12f, -60f, 60f, 0);
        b.Ambience("Ch4.Tower", 214f, 224f, 0f, 4f, -52f, -42f);

        // Field pickups (6D): a gun at the pier, upstairs in the hall and in the rest stop, supplies along the way and
        // in the transport's hold for the finale.
        b.Pickup(new Vector3(-8f, .3f, 2.5f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(6f, 0f, 9f));
        b.Pickup(new Vector3(52f, Floor, -4f));
        b.Pickup(new Vector3(68f, Floor, 3.5f));
        b.Pickup(new Vector3(110f, Upper, 34f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(131f, Floor, 3f));
        b.Pickup(new Vector3(151f, Floor, 30f));
        b.Pickup(new Vector3(143f, Floor, 33f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(197.5f, 0f, -14.5f));
        b.Pickup(new Vector3(219f, 0f, -46f));
        b.Pickup(new Vector3(262f, 0f, -20f));
        b.Pickup(new Vector3(336f, 1.2f, 1.6f));
        b.Pickup(new Vector3(350f, 0f, 8f));
        b.Shot(new Vector3(-10f, 50f, -75f), new Vector3(170f, 0f, 0f));
        DressAirport(b);
        b.Finish("4장 공항을 지었어요 (Scenes/Campaign/Ch4_Airport).\n" +
                 "길: 화물 부두 → 화물장 → 출발층 진입로 → 터미널 출발 홀(2층 식당가) → 보안 검색대 → 면세점(휴게소) → 탑승교와 정비 계단 → " +
                 "계류장 → 관제탑 무전 → 활주로를 막은 여객기를 견인차로 밀어내기(누가 견인차 옆에 있어야 움직임) → 수송기 → 출발 신호 → 버티기 → 탑승.");
    }

    // Chapter 4's cargo yard, landside road, terminal, apron and the transport's evacuation point in the packs' props
    // (map dressing stage 3). The runway's middle (x 245-315), where the airliner is pushed back, is left clear.
    private static void DressAirport(GreyboxBuilder b)
    {
        const float Floor = 4f;
        // Cargo yard.
        foreach (var (x, z) in new[] { (14f, 9f), (14.7f, 9.6f), (13.6f, 10.2f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 7f % 360f);
        b.PropSolid("palletload", new Vector3(16f, 0f, -6f), 0f);
        foreach (var (x, z) in new[] { (10f, 7.5f), (11.2f, 7.9f) }) b.Prop("woodcrate", new Vector3(x, 0f, z), x * 13f % 360f);
        b.Prop("plasticcrate", new Vector3(2.5f, 0f, 8.6f), 30f);
        b.Prop("tirestack", new Vector3(-4f, 0f, 18f), 0f);
        foreach (var (x, z) in new[] { (15f, -10.5f), (15.6f, -11f) }) b.Prop("gascylinder", new Vector3(x, 0f, z), x * 11f % 360f);
        b.Prop("streetlamp", new Vector3(18.5f, 0f, -20f), 270f);
        b.Prop("streetlamp", new Vector3(18.5f, 0f, 20f), 270f);
        // Landside road and the departures curb.
        b.PropSolid("policecar", new Vector3(30f, 0f, 22f), 10f);
        foreach (var z in new[] { -30f, -27f, -24f }) b.Prop("jersey", new Vector3(34f, 0f, z), 90f);
        foreach (var (x, z) in new[] { (31f, -6f), (32f, 6f), (40f, -2f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 17f % 360f);
        b.Prop("trafficdrum", new Vector3(36f, 0f, 9f));
        foreach (var z in new[] { -30f, -10f, 10f, 30f }) b.Prop("streetlamp", new Vector3(46f, 0f, z), 90f);
        foreach (var (x, z) in new[] { (57f, 6f), (58.5f, -9f) }) b.Prop("suitcase", new Vector3(x, Floor, z), x * 31f % 360f);
        b.Prop("trashbag", new Vector3(59.3f, Floor, 15f), 60f);
        // Terminal: what people dropped.
        foreach (var (x, z) in new[] { (70f, -5f), (84f, 12f), (101f, -18f), (118f, 5f), (150f, -8f), (165f, 14f) })
            b.Prop("litter", new Vector3(x, Floor, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (61f, 38.5f), (120f, -38.5f), (178f, 38.5f) }) b.Prop("trashbag", new Vector3(x, Floor, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (77f, 25f), (133f, -14f) }) b.Prop("cardboard", new Vector3(x, Floor, z), x * 13f % 360f);
        b.Prop("plasticbarrier", new Vector3(125f, Floor, -16f), 90f);
        b.Prop("plasticbarrier", new Vector3(125f, Floor, 16f), 90f);
        // Apron.
        foreach (var (x, z) in new[] { (205f, -20f), (206.2f, -19.4f), (226f, 12f), (227f, 12.8f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 19f % 360f);
        foreach (var (x, z) in new[] { (196f, 30f), (196.8f, 30.6f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 23f % 360f);
        b.PropSolid("palletload", new Vector3(234f, 0f, -32f), 0f);
        b.Prop("tirestack", new Vector3(238f, 0f, -48f), 0f);
        b.PropSolid("militarytruck", new Vector3(228f, 0f, -30f), 90f);
        b.Prop("streetlamp", new Vector3(208f, 0f, 38f), 180f);
        b.Prop("streetlamp", new Vector3(236f, 0f, -38f), 0f);
        // The transport's evacuation point: HESCO walls and sandbags round the hold, barbed wire, guard towers, lorries.
        b.Dress(b.Box("HESCO 수송기 남", 318f, 334f, 0f, 2f, -30f, -28.1f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("HESCO 수송기 북", 318f, 334f, 0f, 2f, 28.1f, 30f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("모래주머니 수송기 남", 312f, 313f, 0f, 1.2f, -20f, -12f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("모래주머니 수송기 북", 312f, 313f, 0f, 1.2f, 12f, 20f, b.Wood, "Tarp"), "sandbags");
        for (float x = 319f; x <= 333f; x += 1.7f) b.Prop("barbedwire", new Vector3(x, 0f, -27f), 0f);
        for (float x = 319f; x <= 333f; x += 1.7f) b.Prop("barbedwire", new Vector3(x, 0f, 27f), 0f);
        b.PropSolid("guardtower", new Vector3(316f, 0f, -34f), 0f);
        b.PropSolid("guardtower", new Vector3(316f, 0f, 34f), 180f);
        b.PropSolid("militarytruck", new Vector3(350f, 0f, -18f), 0f);
        b.PropSolid("wrecktruck", new Vector3(352f, 0f, 20f), 200f);
        b.Prop("streetlamp", new Vector3(312f, 0f, -26f), 90f);
        b.Prop("streetlamp", new Vector3(312f, 0f, 26f), 270f);
        DenseAirport(b);
    }

    // Chapter 4's density (map dressing stage 5): what was dropped along the terminal's long walls (clear of the vending
    // machines, the café and the duty-free shop), outside the landside walls, along the cargo yard's river edge (clear
    // of the pier) and the airfield's walls by the apron.
    private static void DenseAirport(GreyboxBuilder b)
    {
        const float Floor = 4f;
        string[] hall = { "litter", "cardboard", "trashbag", "box" };
        string[] yard = { "tire", "pallet", "oilbarrel", "plasticcrate", "cardboard" };
        string[] apron = { "tire", "oilbarrel", "pallet", "cardboard", "trashbag" };
        b.Clutter(new Vector3(62f, Floor, 39.4f), new Vector3(139f, Floor, 39.4f), 5f, hall);
        b.Clutter(new Vector3(164f, Floor, 39.4f), new Vector3(179f, Floor, 39.4f), 5f, hall);
        b.Clutter(new Vector3(62f, Floor, -39.4f), new Vector3(94f, Floor, -39.4f), 5f, hall);
        b.Clutter(new Vector3(100f, Floor, -39.4f), new Vector3(126f, Floor, -39.4f), 5f, hall);
        b.Clutter(new Vector3(136f, Floor, -39.4f), new Vector3(179f, Floor, -39.4f), 5f, hall);
        foreach (float z in new[] { 50f, -50f })
            b.Clutter(new Vector3(59.4f, 0f, z - 8f), new Vector3(59.4f, 0f, z + 8f), 4f, "trashbag", "litter", "cardboard", "tire");
        b.Clutter(new Vector3(-20.3f, 0f, -56f), new Vector3(-20.3f, 0f, -9f), 5f, yard);
        b.Clutter(new Vector3(-20.3f, 0f, 9f), new Vector3(-20.3f, 0f, 56f), 5f, yard);
        foreach (float z in new[] { 58.3f, -58.3f }) b.Clutter(new Vector3(184f, 0f, z), new Vector3(356f, 0f, z), 6f, apron);
    }
}
