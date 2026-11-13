using UnityEditor;
using UnityEngine;

// CampaignMaps: chapter 3, the subway, with its dressing. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    [MenuItem("ENFILADE/맵/캠페인/3장 지하철", priority = 13)]
    private static void Subway()
    {
        var b = GreyboxBuilder.Begin("Ch3_Subway", "3장 지하철");
        if (b == null) return;
        const float Bank = 8f;

        b.Box("바닥", -10f, 345f, -.5f, 0f, -25f, 25f, b.Concrete, "Concrete");
        // The floors that aren't the concourse's tiles (Tables/MapSurfaces by name), 1 cm over the map's floor: ballast
        // under the tracks and through the dark tunnel, the depot's and the service tunnel's concrete.
        b.Box("선로 바닥", 30f, 90.3f, -.5f, .01f, -12f, 0f, null, "Gravel");
        b.Box("터널 바닥", 90.3f, 170f, -.5f, .01f, -6f, 2f, null, "Gravel");
        b.Box("정비창 바닥", 170.3f, 217.7f, -.5f, .01f, -20f, 20f, null, "Concrete");
        b.Box("강 밑 터널 바닥", 218f, 300f, -.5f, .01f, -3f, 3f, null, "Concrete");
        b.Box("맵 끝 서", -10f, -9f, 0f, 12f, -25f, 25f);
        b.Box("맵 끝 동 (강)", 344f, 345f, 0f, 10f, -25f, 25f);
        b.Box("맵 끝 남", -10f, 345f, 0f, 12f, -25f, -24f);
        b.Box("맵 끝 북", -10f, 345f, 0f, 12f, 24f, 25f);

        // 1. The concourse, down the stairs from chapter 2's entrance: ticket barriers to vault.
        b.Box("대합실 천장", -9f, 30f, 5f, 5.5f, -12.3f, 12.3f);
        b.Box("대합실 벽 남", -9f, 30f, 0f, 5f, -12.3f, -12f);
        b.Box("대합실 벽 북", -9f, 30f, 0f, 5f, 12f, 12.3f);
        // Up to the ceiling (memo 48): 3.2 m high against the map's end it was a ledge no zombie could get onto.
        b.Box("들어온 셔터", -9f, -8.7f, 0f, 5f, -4f, 4f, b.Metal, "Metal");
        b.Box("개찰구 남", 15f, 15.4f, 0f, 1f, -11.7f, -1f, b.Metal, "Metal");
        b.Box("개찰구 북", 15f, 15.4f, 0f, 1f, 1f, 11.7f, b.Metal, "Metal");
        b.Box("매점", 5f, 8f, 0f, 2.4f, 7f, 11f);
        // Vending machines, bins and rubbish on the concourse, its exit sign (the Airport pack's props).
        b.PropSolid("vending", new Vector3(10f, 0f, -11.4f));
        b.PropSolid("vending", new Vector3(12f, 0f, -11.4f));
        foreach (var (x, z) in new[] { (3f, -10.8f), (20f, 10.8f) }) b.PropSolid("bin", new Vector3(x, 0f, z));
        foreach (var (x, z) in new[] { (-7f, 10.6f), (-6f, 11.2f), (26f, -11f) }) b.Prop("garbage", new Vector3(x, 0f, z), x * 11f % 360f);
        b.Hang("signExit", new Vector3(0f, 5f, 0f), 270f);
        b.PlayerStart(new Vector3(-5f, 0f, 0f), 90f);
        b.Spot("Start", new Vector3(-5f, 0f, 0f));
        b.Lamp(new Vector3(2f, 4.5f, 0f));
        b.Lamp(new Vector3(22f, 4.5f, 0f));
        b.EnemySpawn("대합실", new Vector3(24f, 0f, -9f));

        // 2. The platform hall: a platform on the north side, the tracks and a stopped train on the south. Underground
        // spaces with no sound zone of their own get their reflection probes by name (Tables/MapReflections Ch3.*).
        b.Reflections("Ch3.Platform", 30f, 90f, 0f, 6f, -12f, 12f);
        b.Box("승강장 홀 천장", 30f, 90f, 6f, 6.5f, -12.3f, 12.3f);
        b.Box("홀 칸막이 위", 29.7f, 30f, 5f, 6.5f, -12.3f, 12.3f);
        b.Box("승강장 홀 벽 남", 30f, 90f, 0f, 6f, -12.3f, -12f);
        b.Box("승강장 홀 벽 북", 30f, 90f, 0f, 6f, 12f, 12.3f);
        b.Box("승강장", 30f, 90f, 0f, 1.1f, 0f, 12f, b.Concrete, "Tile");
        b.Ramp("승강장 오르는 길", new Vector3(26f, 0f, 6f), new Vector3(30f, 1.1f, 6f), 11.4f);
        b.Box("멈춘 열차 1", 40f, 60f, 0f, 3.4f, -9f, -2.6f, b.Metal, "Metal");
        b.Box("멈춘 열차 2", 61.5f, 82f, 0f, 3.4f, -9f, -2.6f, b.Metal, "Metal");
        foreach (float x in new[] { 40f, 55f, 70f, 85f }) b.Box("승강장 기둥", x - .4f, x + .4f, 1.1f, 6f, 5.6f, 6.4f);
        b.Dress(b.Box("벤치 1", 46f, 48f, 1.1f, 1.6f, 9.5f, 10.1f, b.Wood, "Wood"), "seatsShort");
        b.Dress(b.Box("벤치 2", 76f, 78f, 1.1f, 1.6f, 9.5f, 10.1f, b.Wood, "Wood"), "seatsShort");
        foreach (var (x, z) in new[] { (52f, 11f), (64f, 10.9f) }) b.PropSolid("bin", new Vector3(x, 1.1f, z));
        foreach (var (x, z) in new[] { (58f, 4f), (71f, 8f) }) b.Prop("suitcase", new Vector3(x, 1.1f, z), x * 13f % 360f);
        b.Spot("Platform", new Vector3(75f, 1.1f, 6f));
        b.Lamp(new Vector3(45f, 5.5f, 4f));
        b.Lamp(new Vector3(65f, 5.5f, -6f));
        b.Lamp(new Vector3(85f, 5.5f, 4f));
        b.EnemySpawn("선로 1", new Vector3(50f, 0f, -11f));
        b.EnemySpawn("승강장 끝", new Vector3(72f, 1.1f, 10f));
        b.EnemySpawn("선로 2", new Vector3(88f, 0f, -5f));
        b.Box("홀 동벽 남", 90f, 90.3f, 0f, 6.5f, -12.3f, -6.3f);
        b.Box("홀 동벽 북", 90f, 90.3f, 0f, 6.5f, 2.3f, 12.3f);
        b.Box("터널 입구 위", 90f, 90.3f, 5.5f, 6.5f, -6.3f, 2.3f);

        // 3. The dark tunnel: rubble, a pile to climb, a derailed car leaving a side gap. A fuel can lies near its end.
        b.Reflections("Ch3.Tunnel", 90.3f, 170f, 0f, 5.5f, -6f, 2f);
        b.Box("터널 벽 남", 90.3f, 170f, 0f, 5.5f, -6.3f, -6f);
        b.Box("터널 벽 북", 90.3f, 170f, 0f, 5.5f, 2f, 2.3f);
        b.Box("터널 천장", 90.3f, 170f, 5.5f, 6f, -6.3f, 2.3f);
        b.Dress(b.Box("잔해 1", 110f, 113f, 0f, 1.4f, -5.9f, -2f, b.Concrete, "ConcreteGrit"), "rubblepile");
        b.Dress(b.Box("잔해 더미", 128f, 130f, 0f, 2.2f, -2f, 1.9f, b.Concrete, "ConcreteGrit"), "rubblepile");
        b.Box("탈선한 객차", 140f, 152f, 0f, 3f, -5.7f, -2.2f, b.Metal, "Metal");
        b.Can(new Vector3(160f, 0f, 1f));
        b.Lamp(new Vector3(100f, 5f, -2f), 10f, 1.6f);
        b.Lamp(new Vector3(125f, 5f, -2f), 10f, 1.6f);
        b.Lamp(new Vector3(150f, 5f, -2f), 10f, 1.6f);
        b.EnemySpawn("터널 1", new Vector3(100f, 0f, 0f));
        b.EnemySpawn("터널 2", new Vector3(120f, 0f, -4f));
        b.EnemySpawn("터널 3", new Vector3(146f, 0f, 0f));
        b.EnemySpawn("터널 4", new Vector3(162f, 0f, -4f));
        b.Spot("DepotDoor", new Vector3(172f, 0f, -2f));

        // 4. The depot: the office (rest stop), the generator that wants four cans, five cans about (the floor, atop two
        // stacked containers, a catwalk up a ladder, the tunnel, behind the office), and the flood gate the generator opens.
        b.Box("정비창 서벽 남", 170f, 170.3f, 0f, 9.5f, -20.3f, -6.3f);
        b.Box("정비창 서벽 북", 170f, 170.3f, 0f, 9.5f, 2.3f, 20.3f);
        b.Box("정비창 서벽 위", 170f, 170.3f, 5.5f, 9.5f, -6.3f, 2.3f);
        b.Box("정비창 천장", 170f, 218f, 9f, 9.5f, -20.3f, 20.3f);
        b.Reflections("Ch3.Depot", 170.3f, 217.7f, 0f, 9f, -20f, 20f);
        b.Box("정비창 벽 남", 170.3f, 218f, 0f, 9f, -20.3f, -20f);
        b.Box("정비창 벽 북", 170.3f, 218f, 0f, 9f, 20f, 20.3f);
        b.Box("정비창 동벽 남", 217.7f, 218f, 0f, 9f, -20.3f, -3f);
        b.Box("정비창 동벽 북", 217.7f, 218f, 0f, 9f, 3f, 20.3f);
        b.Box("방수문 위 벽", 217.7f, 218f, 4.5f, 9f, -3f, 3f);
        b.Gate("방수문 (발전기가 돌면 열림)", 217.3f, 217.7f, 0f, 4.6f, -3.1f, 3.1f, "Ch3.04", new Vector3(0f, 4.4f, 0f), b.Metal, "Metal", alarm: false);
        Room(b, "정비창 사무실", 172f, 182f, 10f, 18f, 0f, 3f, Side.South, 175f, 177.5f);
        b.Door("사무실 문", 175f, 177.5f, 0f, 2.4f, 10.05f, 10.25f, "Office", new Vector3(2.5f, 0f, 0f));
        b.Spot("Office", new Vector3(177f, 0f, 14.5f));
        // Chest high (memo 48), within a zombie's arm.
        b.Box("사무실 보급함", 179f, 181.5f, 0f, 1.4f, 16.6f, 17.6f, b.Metal, "MetalBox");
        b.Shop(new Vector3(180.2f, 0f, 15.9f), new Vector3(180.2f, 2.2f, 17.6f));
        b.Lamp(new Vector3(177f, 2.6f, 14f), 6f, 1.4f);
        // The depot office's desk, and fuse boxes on the depot's walls.
        b.PropSolid("table", new Vector3(173.8f, 0f, 16.6f), 90f);
        b.Prop("screen", new Vector3(173.8f, .78f, 16.4f), 270f);
        b.Prop("desk", new Vector3(173.7f, .78f, 17.4f), 270f);
        b.Prop("officechair", new Vector3(174.9f, 0f, 16.6f), 270f);
        foreach (var (x, z) in new[] { (170.5f, 8f), (170.5f, -12f), (217.5f, 10f) }) b.Prop("fusebox", new Vector3(x, 1.3f, z), x < 200f ? 90f : 270f);
        foreach (var (x, z) in new[] { (183f, -13f), (184.2f, -12.6f), (199f, 13f), (215f, -18f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (192f, -18.8f), (193.1f, -19f) }) b.Prop("box", new Vector3(x, 0f, z), x * 5f % 360f);
        b.Box("발전기", 205f, 209f, 0f, 2.5f, -6f, -2f, b.Metal, "Metal");
        b.Box("발전기 배기관", 206.8f, 207.2f, 2.5f, 8f, -4.2f, -3.8f, b.Metal, solid: false);
        b.Spot("Generator", new Vector3(203.4f, 0f, -4f));
        b.Box("정비 차량", 178f, 196f, 0f, 3.2f, -8f, -4.4f, b.Metal, "Metal");
        b.Dress(b.Box("컨테이너 1", 184f, 190f, 0f, 2.6f, -18f, -15.5f, b.Metal, "MetalBox"), "container");
        b.Box("컨테이너 2", 186f, 190f, 2.6f, 5.2f, -18f, -15.5f, b.Metal, "MetalBox");
        b.Box("통로 발판", 195f, 215f, 4f, 4.2f, 16f, 19.7f, b.Metal, "Gantry");
        b.Box("통로 난간", 195f, 215f, 4.2f, 5.2f, 15.9f, 16f, b.Metal, solid: false);
        b.Ladder("통로 사다리", new Vector3(200f, 0f, 15.85f), 180f);
        b.Can(new Vector3(192f, 0f, 8f));
        b.Can(new Vector3(188f, 5.2f, -16.7f));
        b.Can(new Vector3(210f, 4.2f, 18f));
        b.Can(new Vector3(180f, 0f, 19f));
        b.Lamp(new Vector3(182f, 8.5f, 0f), 16f);
        b.Lamp(new Vector3(198f, 8.5f, 6f), 16f);
        b.Lamp(new Vector3(210f, 8.5f, -12f), 16f);
        b.Lamp(new Vector3(205f, 8.5f, 14f), 16f);
        b.EnemySpawn("정비창 남", new Vector3(195f, 0f, -18f));
        b.EnemySpawn("정비창 북동", new Vector3(212f, 0f, 12f));
        b.EnemySpawn("정비창 가운데", new Vector3(186f, 0f, 2f));
        b.EnemySpawn("정비창 남동", new Vector3(214f, 0f, -14f));

        // 5. Under the river: a long service tunnel with pipes to vault, then the emergency ramp up to the far bank.
        b.Box("강 밑 터널 벽 남", 218f, 300f, 0f, 4.5f, -3.3f, -3f);
        b.Box("강 밑 터널 벽 북", 218f, 300f, 0f, 4.5f, 3f, 3.3f);
        b.Box("강 밑 터널 천장", 218f, 300f, 4.5f, 5f, -3.3f, 3.3f);
        b.Reflections("Ch3.RiverTunnel", 218f, 300f, 0f, 4.5f, -3f, 3f);
        foreach (float x in new[] { 240f, 265f, 285f }) b.Box("낮은 배관", x, x + .4f, 0f, 1f, -3f, 3f, b.Metal, "MetalPipe");
        b.Lamp(new Vector3(230f, 4f, 0f), 9f, 1.4f);
        b.Lamp(new Vector3(255f, 4f, 0f), 9f, 1.4f);
        b.Lamp(new Vector3(280f, 4f, 0f), 9f, 1.4f);
        b.EnemySpawn("강 밑 1", new Vector3(250f, 0f, 0f));
        b.EnemySpawn("강 밑 2", new Vector3(275f, 0f, 0f));
        b.Spot("StairsBottom", new Vector3(302f, 0f, 0f));
        b.Box("계단실 벽 남", 300f, 322f, 0f, 12f, -3.3f, -3f);
        b.Box("계단실 벽 북", 300f, 322f, 0f, 12f, 3f, 3.3f);
        b.Box("계단실 천장", 300f, 322f, 12f, 12.5f, -3.3f, 3.3f);
        b.Box("계단실 입구 위", 299.7f, 300f, 4.5f, 12.5f, -3.3f, 3.3f);
        b.Ramp("비상 계단", new Vector3(300f, 0f, 0f), new Vector3(322f, Bank, 0f), 6f);
        b.Lamp(new Vector3(311f, 7f, 0f), 10f, 1.4f);
        b.EnemySpawn("계단 아래", new Vector3(306f, 0f, 0f));

        // 6. The far bank, out in the open: the flare that calls the army's boat (finale) and the pier it comes to.
        // Paving on top, by name (Tables/MapSurfaces Ch3_Subway.Plaza), not the concourse's tiles.
        b.Box("강 건너 광장", 322f, 344f, 0f, Bank, -24f, 24f, null, "Concrete");
        b.Box("광장 서쪽 난간 남", 322f, 322.3f, Bank, Bank + 1.2f, -24f, -3.3f);
        b.Box("광장 서쪽 난간 북", 322f, 322.3f, Bank, Bank + 1.2f, 3.3f, 24f);
        b.Spot("Exit", new Vector3(326f, Bank, 0f));
        b.Dress(b.Box("신호탄 상자", 331.6f, 332.4f, Bank, Bank + 1.1f, 2.6f, 3.4f, b.Marker), "crateFit");
        b.Spot("Flare", new Vector3(332f, Bank, 2f));
        b.Spot("Plaza", new Vector3(333f, Bank, 0f));
        Car(b, 330f, Bank, -10f);
        Car(b, 334f, Bank, 12f);
        b.Dress(b.Box("모래주머니", 326f, 327f, Bank, Bank + 1.2f, -16f, -8f, b.Wood, "Tarp"), "sandbags");
        b.Box("부두", 338f, 344f, Bank, Bank + .3f, -5f, 5f, b.Wood, "WoodPlank");
        b.Box("군 보트", 339.5f, 343.5f, Bank + .3f, Bank + 1.8f, 6f, 9f, b.Metal, solid: false);
        b.Spot("Boat", new Vector3(341f, Bank + .3f, 0f));
        b.EnemySpawn("광장 남서", new Vector3(330f, Bank, -21f));
        b.EnemySpawn("광장 북서", new Vector3(330f, Bank, 21f));
        b.EnemySpawn("광장 남동", new Vector3(342f, Bank, -20f));
        b.EnemySpawn("광장 북동", new Vector3(342f, Bank, 20f));

        // Background sound by area (the platform hall, the tunnels and the depot keep the map's machinery hum): the
        // concourse, the depot office, the far bank out in the open.
        b.Ambience("Ch3.Concourse", -9f, 30f, -1f, 5f, -12f, 12f);
        b.Ambience("Ch3.Office", 172f, 182f, 0f, 3f, 10f, 18f);
        b.Ambience("Ch3.FarBank", 322f, 345f, 7f, 40f, -25f, 25f);

        // Field pickups (6D).
        b.Pickup(new Vector3(-3f, 0f, 1.5f));
        b.Pickup(new Vector3(75f, 1.1f, 4f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(156f, 0f, 1f));
        b.Pickup(new Vector3(172f, 0f, 0f));
        b.Pickup(new Vector3(177f, 0f, 13f));
        b.Pickup(new Vector3(176f, 0f, 15f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(200f, 0f, -2f));
        b.Pickup(new Vector3(300f, 0f, 1f));
        b.Pickup(new Vector3(333f, Bank, 2f));
        DressSubway(b);
        b.Finish("3장 지하철을 지었어요 (Scenes/Campaign/Ch3_Subway).\n" +
                 "길: 대합실 → 승강장 → 어두운 터널 → 정비창 사무실(휴게소) → 연료통 4개를 발전기에 → 방수문 → 강 밑 터널 → 비상 계단 → 신호탄 → 버티기 → 보트.");
    }

    // Chapter 3's concourse, platform, tunnel, depot, river tunnel and far bank in the packs' props (map dressing
    // stage 3): rubbish and boxes where people sheltered, debris on the tracks, the depot's shelves, lockers, drums and
    // workbench, and on the far bank the army's evacuation point (barbed wire on the sandbags, a lorry, HESCO, barriers).
    private static void DressSubway(GreyboxBuilder b)
    {
        const float Bank = 8f;
        // Concourse.
        foreach (var (x, z) in new[] { (-6.5f, -11.2f), (28.5f, 11.2f) }) b.Prop("trashbag", new Vector3(x, 0f, z), x * 13f % 360f);
        foreach (var (x, z) in new[] { (5f, -3f), (19f, 4f), (24f, -2f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("cardboard", new Vector3(6.5f, 0f, 6.2f), 25f);
        b.Prop("plasticcrate", new Vector3(8.6f, 0f, 7.2f), 80f);
        b.Prop("planks", new Vector3(-4f, 0f, 11.4f), 0f);
        // Platform and tracks.
        foreach (var (x, z) in new[] { (35f, -11f), (86f, -10f) }) b.Prop("brickdebris", new Vector3(x, 0f, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (58f, -1f), (75f, -11f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 17f % 360f);
        b.Prop("cardboard", new Vector3(44f, 1.1f, 11.2f), 10f);
        b.Prop("trashbag", new Vector3(66f, 1.1f, 11.3f), 140f);
        b.Prop("plasticcrate", new Vector3(80f, 1.1f, 11.2f), 15f);
        foreach (var (x, z) in new[] { (50f, 3f), (68f, 2.5f) }) b.Prop("litter", new Vector3(x, 1.1f, z), x * 19f % 360f);
        b.Prop("bag", new Vector3(61f, 1.1f, 7.5f), 200f);
        // Tunnel.
        foreach (var (x, z) in new[] { (100f, -5.4f), (118f, 1.4f), (135f, -5.3f) }) b.Prop("rubble", new Vector3(x, 0f, z), x * 23f % 360f);
        b.Prop("planks", new Vector3(122f, 0f, 1.3f), 0f);
        b.Prop("corrugated", new Vector3(155f, 0f, 1.4f), 0f);
        b.Prop("drum", new Vector3(165f, 0f, -5.4f), 40f);
        // Depot.
        b.PropSolid("palletload", new Vector3(201f, 0f, -18.5f), 0f);
        foreach (var (x, z) in new[] { (212f, 17.5f), (212.8f, 18.2f), (211.4f, 18.4f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 29f % 360f);
        foreach (var (x, z) in new[] { (216.5f, -8f), (216.8f, -9.2f) }) b.Prop("gascylinder", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("plasticcrate", new Vector3(171.5f, 0f, -18.5f), 0f);
        foreach (var x in new[] { 176f, 179f }) b.PropSolid("wireshelf", new Vector3(x, 0f, -19.4f), 0f);
        foreach (var z in new[] { 4.4f, 5f }) b.PropSolid("locker", new Vector3(171f, 0f, z), 90f);
        b.PropSolid("worktable", new Vector3(198f, 0f, -12f), 0f);
        b.Prop("stool", new Vector3(198.8f, 0f, -11f), 30f);
        b.Prop("toolbox", new Vector3(199.5f, 0f, -12.9f), 10f);
        b.Prop("handtruck", new Vector3(203f, 0f, 8f), 60f);
        b.Prop("tire", new Vector3(215.5f, 0f, 6f), 0f);
        b.Prop("tirestack", new Vector3(213f, 0f, 7.5f), 0f);
        b.PropSolid("spool", new Vector3(208f, 0f, 18f), 0f);
        // River tunnel.
        b.Prop("electricbox", new Vector3(230f, 0f, 2.7f), 180f);
        b.Prop("electricbox", new Vector3(270f, 0f, -2.7f), 0f);
        foreach (var x in new[] { 246f, 292f }) b.Prop("litter", new Vector3(x, 0f, .5f), x * 11f % 360f);
        // Far bank: the army's evacuation point.
        for (float z = -15.2f; z <= -8.8f; z += 1.6f) b.Prop("barbedwire", new Vector3(326.5f, Bank + 1.1f, z), 90f);
        b.PropSolid("militarytruck", new Vector3(338.5f, Bank, 16f), 90f);
        b.Dress(b.Box("HESCO 광장", 336f, 337.9f, Bank, Bank + 2f, -24f, -14f, b.Wood, "Tarp"), "hesco");
        b.Prop("jersey", new Vector3(336.5f, Bank, -6.5f), 0f);
        b.Prop("jersey", new Vector3(336.5f, Bank, 6.5f), 0f);
        b.Prop("warninglight", new Vector3(337.5f, Bank, -5.6f), 90f);
        foreach (var (x, z) in new[] { (327.5f, -17.5f), (328.6f, -18f) }) b.Prop("woodcrate", new Vector3(x, Bank, z), x * 13f % 360f);
        b.Prop("streetlamp", new Vector3(323f, Bank, 10f), 90f);
        b.Prop("streetlamp", new Vector3(323f, Bank, -20f), 90f);
        DenseSubway(b);
    }

    // Chapter 3's density (map dressing stage 5): rubbish along the concourse's walls (clear of the kiosk and the vending
    // machines) and the platform's back wall, debris on the track bed and in the dark tunnel (clear of the fuel can near
    // its end), along the depot's north wall past the office's can, in the service tunnel and round the far bank's square.
    private static void DenseSubway(GreyboxBuilder b)
    {
        const float Bank = 8f, Top = .01f;
        string[] hall = { "litter", "cardboard", "trashbag", "box", "plasticcrate" };
        string[] track = { "brickdebris", "litter", "rubble", "trashbag" };
        string[] tunnel = { "rubble", "brickdebris", "litter", "tire" };
        string[] depot = { "oilbarrel", "plasticcrate", "cardboard", "tire", "pallet" };
        string[] square = { "trashbag", "cardboard", "litter", "tire", "box" };
        b.Clutter(new Vector3(-8f, 0f, -11.4f), new Vector3(8.5f, 0f, -11.4f), 3.5f, hall);
        b.Clutter(new Vector3(13.5f, 0f, -11.4f), new Vector3(29f, 0f, -11.4f), 3.5f, hall);
        b.Clutter(new Vector3(-8f, 0f, 11.4f), new Vector3(4f, 0f, 11.4f), 3.5f, hall);
        b.Clutter(new Vector3(9f, 0f, 11.4f), new Vector3(29f, 0f, 11.4f), 3.5f, hall);
        b.Clutter(new Vector3(31f, 1.1f, 11.4f), new Vector3(89f, 1.1f, 11.4f), 4f, hall);
        b.Clutter(new Vector3(31f, Top, -11.4f), new Vector3(89f, Top, -11.4f), 5f, track);
        b.Clutter(new Vector3(92f, Top, -5.5f), new Vector3(168f, Top, -5.5f), 5f, tunnel);
        b.Clutter(new Vector3(92f, Top, 1.5f), new Vector3(156f, Top, 1.5f), 5f, tunnel);
        b.Clutter(new Vector3(164f, Top, 1.5f), new Vector3(168f, Top, 1.5f), 5f, tunnel);
        b.Clutter(new Vector3(183f, Top, 19.3f), new Vector3(216f, Top, 19.3f), 4f, depot);
        foreach (float z in new[] { 2.5f, -2.5f })
            b.Clutter(new Vector3(220f, Top, z), new Vector3(298f, Top, z), 6f, "litter", "cardboard", "brickdebris");
        foreach (float z in new[] { 23.4f, -23.4f }) b.Clutter(new Vector3(323f, Bank, z), new Vector3(343f, Bank, z), 3.5f, square);
    }
}
