using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Side = GreyboxBuilder.Side;

// CampaignMaps: chapter 1, the rooftops and the apartment it starts in, with its dressing. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    [MenuItem("ENFILADE/맵/캠페인/1장 옥상 탈출", priority = 11)]
    private static void Rooftops()
    {
        var b = GreyboxBuilder.Begin("Ch1_Rooftops", "1장 옥상 탈출");
        if (b == null) return;

        // The ground and the edge of the map, open over the block of flats' cellar (B4, CampaignMaps.Apartment).
        b.Box("땅 서", -30f, AptIn0, -.5f, 0f, -50f, 50f, b.Road, "Asphalt");
        b.Box("땅 동", PartyX, 240f, -.5f, 0f, -50f, 50f, b.Road, "Asphalt");
        b.Box("땅 남", AptIn0, PartyX, -.5f, 0f, -50f, AptS0, b.Road, "Asphalt");
        b.Box("땅 북", AptIn0, PartyX, -.5f, 0f, -1.5f, 50f, b.Road, "Asphalt");
        b.Box("맵 끝 서", -30f, -29f, 0f, 8f, -50f, 50f);
        b.Box("맵 끝 동", 239f, 240f, 0f, 8f, -50f, 50f);
        b.Box("맵 끝 남", -30f, 240f, 0f, 8f, -50f, -49f);
        b.Box("맵 끝 북", -30f, 240f, 0f, 8f, 49f, 50f);

        // 1. The flat's roof: the start room (safe room), a low wall and two air conditioners to vault.
        b.Box("B1 아파트", 0f, 24f, 0f, 18f, -10f, 10f);
        Parapets(b, "B1", 0f, 24f, -10f, 10f, 18f);
        b.Box("B1 난간 서", 0f, Wall, 18f, 18f + ParapetHeight, -10f, 10f);
        Room(b, "시작 안전가옥", 2f, 9f, -3f, 3f, 18f, 3f, Side.East, -1f, 1f);
        b.PlayerStart(new Vector3(5f, 18f, 0f), 90f);
        b.Spot("Start", new Vector3(5.5f, 18f, 0f));
        b.Box("B1 낮은 벽", 16.6f, 17f, 18f, 19f, -3f, 3f);
        b.Dress(b.Box("B1 실외기 1", 13f, 15f, 18f, 19.1f, 4f, 6f, b.Metal, "Metal"), "acunit");
        b.Dress(b.Box("B1 실외기 2", 13f, 15f, 18f, 19.1f, -6f, -4f, b.Metal, "Metal"), "acunit");

        // 2. Over a 3 m gap, 2 m down. The lift room is climbed (wall climb, or its ladder), and the cable from its top
        // crosses the street.
        b.Box("B2", 27f, 47f, 0f, 16f, -10f, 10f);
        Parapets(b, "B2", 27f, 47f, -10f, 10f, 16f);
        b.Box("B2 엘리베이터실", 37f, 43f, 16f, 19.4f, -3f, 3f);
        b.Ladder("B2 엘리베이터실 사다리", new Vector3(40f, 16f, -3.15f), 180f);
        b.Box("집라인 기둥 1", 42.6f, 42.8f, 19.4f, 22.2f, -.1f, .1f, b.Metal, solid: false);
        b.Zipline("길 건너는 집라인", new Vector3(42.5f, 21.7f, 0f), new Vector3(70f, 15.1f, 0f));
        b.Box("집라인 기둥 2", 70.2f, 70.4f, 13f, 15.8f, -.1f, .1f, b.Metal, solid: false);
        b.Spot("LiftRoom", new Vector3(40f, 19.4f, 0f));
        b.Spot("ZiplineEnd", new Vector3(72f, 13f, 0f));
        // The roofs are crossed as only the players cross them; each roof has its own spawn points.
        b.NotWalked("Ch1.01", "3 m 틈을 뛰어 건너고 승강기실을 타고 오르는 곳(지붕마다 스폰 지점이 따로 있음)");
        b.NotWalked("Ch1.02", "짚라인으로 길을 건너는 곳");
        b.NotWalked("Ch1.03", "스윙 바로 아파트 5층에 뛰어드는 곳");
        b.EnemySpawn("B2 옥상 1", new Vector3(32f, 16f, 6f));
        b.EnemySpawn("B2 옥상 2", new Vector3(45f, 16f, -7f));

        b.Dress(b.Box("길1 차 1", 50f, 54.5f, 0f, 1.5f, -6f, -4.2f, b.Metal, "Metal"), "car");
        b.Dress(b.Box("길1 차 2", 56f, 60.5f, 0f, 1.5f, 3f, 4.8f, b.Metal, "Metal"), "car");
        b.EnemySpawn("길1", new Vector3(55f, 0f, -1f));
        b.EnemySpawn("길1 남쪽", new Vector3(55f, 0f, -25f));
        // The street under the cable, 13 m and more under the roofs and walled off from the flats and the alley: its
        // points send only to someone who fell.
        b.NotWalked("길1", "지붕 아래 13 m가 넘는 길, 떨어진 사람에게만 보냄");
        b.NotWalked("길1 남쪽", "지붕 아래 13 m가 넘는 길, 떨어진 사람에게만 보냄");

        // 3. Too far to jump to the next roof: the swing bar over the gap, in line with the hole the war blew in the block of
        // flats' 5th and 6th floors (z 2..10), which it swings them into. A ladder brings a faller back up.
        b.Box("B3", 63f, 80f, 0f, 13f, -10f, 10f);
        Parapets(b, "B3", 63f, 80f, -10f, 10f, 13f);
        b.Box("B3 위성안테나", 66f, 68f, 13f, 14.2f, 6f, 8f, b.Metal, "Metal");
        b.Bar("틈 위 철봉", new Vector3(83.5f, 15.9f, 6f), 90f, 3f);
        b.Box("철봉 기둥 1", 83.4f, 83.6f, 0f, 16.1f, 4.1f, 4.3f, b.Metal, solid: false);
        b.Box("철봉 기둥 2", 83.4f, 83.6f, 0f, 16.1f, 7.7f, 7.9f, b.Metal, solid: false);
        b.Ladder("틈에 떨어지면 올라오는 사다리", new Vector3(80.15f, 0f, 6f), 90f);
        b.EnemySpawn("B3 옥상 1", new Vector3(70f, 13f, -6f));
        b.EnemySpawn("B3 옥상 2", new Vector3(75f, 13f, 7f));

        // 4. Down through the block of flats (user, 2026-11-12: inside the flats, a fight indoors; 2026-11-14, the map
        // gimmicks: "1장 아파트 실내가 좋았던 게 암살 플레이하기 좋아서", "아파트 좀 넓게 해도 돼"): six storeys of flats to
        // sneak through, in at the 5th floor's fallen corner, down to the ground floor's chained stair door and the cellar's
        // switchboard, out of the back door into the alley (ApartmentBlock).
        ApartmentBlock(b);
        b.Spot("B4Entry", new Vector3(89.2f, 12f, 6f));
        b.Spot("B4Lobby", new Vector3(101.5f, 0f, -.2f));
        b.Box("B4 별관", 104f, 112f, 0f, 13.5f, 4f, 50f);
        b.Dress(b.Box("골목 트럭", 108f, 114f, 0f, 3f, 0f, 3f, b.Metal, "MetalBox"), "van");
        foreach (int f in new[] { 0, 2, 4 })
        {
            b.EnemySpawn($"B4 {f + 1}층 복도 서", new Vector3(88.4f, f * Storey, 0f));
            b.EnemySpawn($"B4 {f + 1}층 복도 동", new Vector3(99.2f, f * Storey, 0f));
        }
        b.EnemySpawn("B4 3층 2호", new Vector3(97.5f, 6f, 13f));
        b.EnemySpawn("B4 2층 4호", new Vector3(90f, 3f, -12f));
        b.EnemySpawn("B4 옥상", new Vector3(93f, AptRoof, -4f));

        // 5. The alley, closed in on every side but the shutter. The cellar's switchboard powers it and starts the hold
        // (Ch1.05, the map gimmick card "셔터는 지하 차단기로"): the alarm wails and the shutter rises with the hold, so the
        // way on (and the street's horde) opens when it ends.
        b.Box("B5 골목 남쪽 건물", 104f, 137f, 0f, 10f, -50f, -4f);
        b.Box("B6 골목 북쪽 건물", 112f, 137f, 0f, 9f, 4f, 50f);
        b.Box("B7 창고 남", 137f, 141f, 0f, 17f, -50f, -2.5f);
        b.Box("B7 창고 북", 137f, 141f, 0f, 17f, 2.5f, 50f);
        b.Box("B7 셔터 위 벽", 137f, 141f, 4.5f, 17f, -2.5f, 2.5f);
        b.Gate("셔터 (버티는 동안 올라감, 경보)", 136.7f, 137f, 0f, 4.6f, -2.6f, 2.6f, "Ch1.06", new Vector3(0f, 4.4f, 0f), b.Metal, "Metal");
        // The shutter's control box: dead until the block of flats' cellar switchboard is thrown (Ch1.05).
        b.Dress(b.Box("셔터 제어함", 134f, 134.4f, 1f, 1.6f, -4f, -3.85f, b.Marker, solid: false), "controlboxFit");
        b.Spot("AlleyHold", new Vector3(124f, 0f, 0f));
        b.Dress(b.Box("골목 쓰레기통 1", 118f, 120f, 0f, 1.4f, -3.9f, -2.6f, b.Metal, "MetalBox"), "dumpster");
        b.Dress(b.Box("골목 쓰레기통 2", 126f, 128f, 0f, 1.4f, 2.6f, 3.9f, b.Metal, "MetalBox"), "dumpster");
        b.Dress(b.Box("골목 팔레트", 122f, 123.2f, 0f, .9f, -1f, .2f, b.Wood, "WoodPlank"), "palletload");
        // Rubbish round the bins and boxes by the shutter (the Airport pack's bags, boxes and crates; seen only).
        foreach (var (x, z) in new[] { (117.4f, -3.4f), (120.6f, -3.5f), (121.3f, -2.9f), (125.4f, 3.4f), (128.6f, 3.2f), (129.4f, 3.5f) })
            b.Prop("garbage", new Vector3(x, 0f, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (131f, -3.5f), (132.1f, -3.4f), (131.6f, -2.6f) }) b.Prop("box", new Vector3(x, 0f, z), x * 5f % 360f);
        foreach (var (x, z) in new[] { (107f, 3.3f), (108.2f, 3.3f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        b.EnemySpawn("골목 서쪽 끝", new Vector3(106f, 0f, -2f));
        b.EnemySpawn("골목 서쪽 구석", new Vector3(105.5f, 0f, 2.5f));
        b.EnemySpawn("골목 트럭 옆", new Vector3(113f, 0f, -3.2f));
        b.EnemySpawn("골목 가운데", new Vector3(120f, 0f, 2f));
        b.EnemySpawn("셔터 통로", new Vector3(139f, 0f, 0f));

        // 6. The corner shop over the street: the rest stop (doors 5D, shop stage 6).
        Room(b, "편의점", 145f, 157f, 10f, 20f, 0f, 4.5f, Side.South, 149f, 151.5f);
        b.Door("편의점 문", 149f, 151.5f, 0f, 2.4f, 10.05f, 10.25f, "RestStop", new Vector3(2.5f, 0f, 0f));
        b.Dress(b.Box("편의점 계산대", 147f, 152f, 0f, 1f, 15f, 16f, b.Wood, "Wood"), "shopcounter");
        b.Prop("cashregister", new Vector3(148f, 1f, 15.5f), 180f);
        b.Shop(new Vector3(149.5f, 0f, 14.3f), new Vector3(149.5f, 2.2f, 16f));
        // Chest high (memo 48): a zombie's arm reaches a player standing on it, as it couldn't at 1.8 m.
        b.Dress(b.Box("편의점 진열대", 153.5f, 154.3f, 0f, 1.4f, 12f, 18f, b.Metal, "Metal"), "shopshelf");
        b.PropSolid("fridge", new Vector3(156.1f, 0f, 15f), 270f);
        b.PropSolid("vending", new Vector3(153.5f, 0f, 9.4f), 180f);
        b.PropSolid("bin", new Vector3(147.5f, 0f, 9.3f));
        b.Spot("RestStop", new Vector3(151f, 0f, 15f));
        b.Dress(b.Box("길2 차 1", 144f, 148.5f, 0f, 1.5f, -8f, -6.2f, b.Metal, "Metal"), "car");
        b.Dress(b.Box("길2 차 2", 158f, 162.5f, 0f, 1.5f, -3f, -1.2f, b.Metal, "Metal"), "car");
        b.EnemySpawn("길2", new Vector3(150f, 0f, -12f));
        b.EnemySpawn("길2 북쪽", new Vector3(162f, 0f, 8f));

        // 7. The building site, through the gate in its fence. Players climb two containers, a wall up to the scaffold
        // and its ladder to the roof (helipad, finale); enemies walk two-lane switchback ramps up the south side.
        b.Dress(b.Box("공사장 펜스 남", 170f, 170.2f, 0f, 2.2f, -50f, -3f, b.Metal, "MetalPipe"), "constructionfence");
        b.Dress(b.Box("공사장 펜스 북", 170f, 170.2f, 0f, 2.2f, 3f, 50f, b.Metal, "MetalPipe"), "constructionfence");
        b.Spot("SiteGate", new Vector3(172f, 0f, 0f));
        b.Dress(b.Box("컨테이너 1", 176f, 182f, 0f, 2.6f, 10f, 12.5f, b.Metal, "MetalBox"), "container");
        b.Box("컨테이너 2", 179f, 182f, 2.6f, 5.2f, 10f, 12.5f, b.Metal, "MetalBox");
        b.Box("공사 중 건물", 182f, 200f, 0f, 14f, -9f, 9f);
        b.Box("비계 벽", 182f, 182.3f, 5.2f, 8.7f, 9f, 12f, b.Wood, "WoodPanel");
        b.Box("비계 발판", 182.3f, 188f, 8.5f, 8.7f, 9f, 12f, b.Wood, "WoodPlank");
        b.Box("비계 기둥", 187.8f, 188f, 0f, 8.5f, 11.8f, 12f, b.Metal, solid: false);
        b.Ladder("비계 사다리", new Vector3(185f, 8.7f, 9.15f), 0f);
        b.Ladder("바닥에서 지붕 사다리", new Vector3(195f, 0f, 9.15f), 0f);
        const float LaneA = -12f, LaneB = -10f, Lane = 2f;
        b.Ramp("적 경사로 1", new Vector3(186f, 0f, LaneA), new Vector3(195f, 3.5f, LaneA), Lane);
        b.Box("경사로 층계참 1", 195f, 198f, 3.2f, 3.5f, -13f, -9f);
        b.Ramp("적 경사로 2", new Vector3(195f, 3.5f, LaneB), new Vector3(186f, 7f, LaneB), Lane);
        b.Box("경사로 층계참 2", 183f, 186f, 6.7f, 7f, -13f, -9f);
        b.Ramp("적 경사로 3", new Vector3(186f, 7f, LaneA), new Vector3(195f, 10.5f, LaneA), Lane);
        b.Box("경사로 층계참 3", 195f, 198f, 10.2f, 10.5f, -13f, -9f);
        b.Ramp("적 경사로 4", new Vector3(195f, 10.5f, LaneB), new Vector3(186f, 14f, LaneB), Lane);
        b.Box("경사로 층계참 4 (지붕)", 183f, 186f, 13.7f, 14f, -13f, -9f);
        b.Box("헬기 착륙장 표시", 186f, 196f, 14f, 14.03f, -4f, 4f, b.Marker, solid: false);
        b.Spot("Helipad", new Vector3(191f, 14f, 0f));
        b.Spot("SiteRoof", new Vector3(190f, 14f, 2f));
        SiteGimmicks(b);
        b.Dress(b.Box("무전기", 187.6f, 188.4f, 14f, 15.1f, 4.6f, 5.2f, b.Marker), "controlboxFit");
        foreach (var (x, z) in new[] { (175f, 9f), (176.2f, 8.6f), (184f, -10.5f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Spot("Radio", new Vector3(188f, 14f, 4f));
        b.Box("크레인 기둥", 203f, 204.6f, 0f, 30f, -.8f, .8f, b.Metal, "MetalCrane");
        b.Box("크레인 팔", 176f, 212f, 30f, 31f, -.5f, .5f, b.Metal, solid: false);
        b.Box("크레인 무게추", 208f, 212f, 28.5f, 30f, -1f, 1f, solid: false);
        b.EnemySpawn("공사장 남서", new Vector3(175f, 0f, -15f));
        b.EnemySpawn("공사장 남동", new Vector3(205f, 0f, -15f));
        b.EnemySpawn("공사장 북동", new Vector3(205f, 0f, 14f));
        b.EnemySpawn("공사장 북서", new Vector3(176f, 0f, 20f));
        b.EnemySpawn("경사로 아래", new Vector3(186f, 0f, -16f));

        // Background sound by area (user, 2026-11-10; the rows are in Tables/Ambience, the roofs keep the map's own): the
        // street below the roofs, the start room, the alley with its bins and the shutter's passage, the corner shop.
        b.Ambience("Ch1.Street", -30f, 240f, -1f, 7f, -50f, 50f, 0);
        b.Ambience("Ch1.SafeRoom", 2f, 9f, 18f, 21f, -3f, 3f);
        b.Ambience("Ch1.Alley", 104f, 141f, -1f, 7f, -4f, 4f);
        b.Ambience("Ch1.Shop", 145f, 157f, 0f, 4.5f, 10f, 20f);

        // Field pickups (6D): a gun at the start and on the last roof, supplies along the way, more for the alley's hold
        // and the finale, a gun and supplies in the rest stop.
        b.Pickup(new Vector3(6.5f, 18f, 2f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(8f, 18f, -2f));
        b.Pickup(new Vector3(40f, 19.4f, 1.5f));
        b.Pickup(new Vector3(72f, 13f, 2f));
        b.Pickup(new Vector3(95f, 12f, 2f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(110f, 0f, 1.5f));
        b.Pickup(new Vector3(124f, 0f, 2f));
        b.Pickup(new Vector3(152.5f, 0f, 13.5f));
        b.Pickup(new Vector3(148f, 0f, 13f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(172f, 0f, 2f));
        b.Pickup(new Vector3(191f, 14f, -2f));
        DressRooftops(b);
        b.Finish("1장 옥상 탈출을 지었어요 (Scenes/Campaign/Ch1_Rooftops). 메인 메뉴의 맵 목록에도 나와요.\n" +
                 "길: 안전가옥 → 옥상 틈 점프 → 엘리베이터실 벽 오르기 → 집라인 → 철봉으로 아파트 5층의 무너진 모서리 → 관리인 숙소(503, 관리 일지·소독 가스) → " +
                 "계단·바닥 구멍·비상 사다리·엘리베이터로 1층 → 쇠사슬 문(마스터키 또는 부수기) → 지하 배전반(셔터 버티기 시작) → 뒷문으로 골목 → 편의점 → " +
                 "공사장 옥상 무전 → 착륙 자리에 연막 → 버티기 → 헬기.");
    }

    // Chapter 1's streets, roofs, alley and building site in the packs' props (map dressing stage 3, 2026-11-12): a
    // police car and barricaded street ends, poles and lamps, water tanks and air conditioners on the roofs, rubbish in
    // the alley, the building site's materials and machines. Big things block (PropSolid) and stand clear of the
    // routes and the spawns; small things are only seen.
    private static void DressRooftops(GreyboxBuilder b)
    {
        // Roofs.
        b.PropSolid("watertank", new Vector3(30.5f, 16f, -6.5f), 0f);
        foreach (var x in new[] { 72f, 74.2f }) b.PropSolid("acunit", new Vector3(x, 13f, -8.4f), 0f);
        // B4's roof has its own (AptRoofTop).
        // Street 1 under the zipline: barricaded ends, a police car, cones round the first car, rubbish.
        foreach (var z in new[] { -21f, 22f })
            foreach (var x in new[] { 51.5f, 55f, 58.5f }) b.Prop("jersey", new Vector3(x, 0f, z), 0f);
        b.PropSolid("policecar", new Vector3(54f, 0f, 12f), 75f);
        foreach (var (x, z) in new[] { (49.8f, -7f), (51f, -7.4f), (50.2f, -3.2f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 37f % 360f);
        b.Prop("trafficdrum", new Vector3(60.8f, 0f, 7f));
        b.Prop("powerpole", new Vector3(48.4f, 0f, -16f), 90f);
        b.Prop("powerpole", new Vector3(48.4f, 0f, 18f), 90f);
        b.Prop("streetlamp", new Vector3(61.6f, 0f, -10f), 270f);
        b.Prop("streetlamp", new Vector3(61.6f, 0f, 12f), 270f);
        foreach (var (x, z) in new[] { (48f, 3f), (62f, -3f), (49f, 9.5f) }) b.Prop("trashbag", new Vector3(x, 0f, z), x * 13f % 360f);
        foreach (var (x, z) in new[] { (53f, 1.5f), (57.5f, -8f), (56f, 16f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("cardboard", new Vector3(62.2f, 0f, -14f), 15f);
        // The alley.
        b.Prop("trashcan", new Vector3(116f, 0f, 3.4f), 0f);
        b.Prop("cardboard", new Vector3(133f, 0f, 3.2f), 40f);
        b.Prop("planks", new Vector3(130.5f, 0f, -3.6f), 90f);
        b.Prop("electricbox", new Vector3(135.5f, 0f, 3.65f), 180f);
        foreach (var (x, z) in new[] { (112f, -1f), (126f, -.5f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 11f % 360f);
        // Street 2 and the building site's gate.
        b.PropSolid("policecar", new Vector3(165f, 0f, -12f), 30f);
        b.Prop("jersey", new Vector3(168.4f, 0f, -6f), 90f);
        b.Prop("jersey", new Vector3(168.4f, 0f, 6f), 90f);
        foreach (var (x, z) in new[] { (169f, -2.5f), (169f, 2.5f), (167.5f, .8f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 23f % 360f);
        b.Prop("warninglight", new Vector3(169.3f, 0f, -4.2f), 270f);
        b.Prop("streetlamp", new Vector3(143f, 0f, -20f), 0f);
        b.Prop("streetlamp", new Vector3(165f, 0f, 22f), 180f);
        b.Prop("trashbag", new Vector3(144.5f, 0f, 9f), 60f);
        b.Prop("litter", new Vector3(157.8f, 0f, 9.3f), 10f);
        // The building site: bricks and bags by the gate, pipes, a mixer and a generator by the building, toilets and a
        // water tank in the north corner, tarps, boards and drums.
        b.PropSolid("bricks", new Vector3(173.5f, 0f, -6f), 0f);
        b.PropSolid("bricks", new Vector3(176.5f, 0f, -14f), 90f);
        foreach (var (x, z) in new[] { (175.5f, -6.8f), (176.4f, -7.3f) }) b.Prop("cementbags", new Vector3(x, 0f, z), x * 17f % 360f);
        b.PropSolid("concretepipe", new Vector3(205f, 0f, 4f), 0f);
        b.PropSolid("concretepipe", new Vector3(205f, 0f, 7f), 0f);
        b.PropSolid("concretemixer", new Vector3(207f, 0f, -4f), 90f);
        b.PropSolid("generator", new Vector3(208f, 0f, 11f), 180f);
        b.PropSolid("portapotty", new Vector3(172.5f, 0f, 24f), 90f);
        b.PropSolid("portapotty", new Vector3(174.5f, 0f, 24f), 90f);
        b.PropSolid("watertankportable", new Vector3(179f, 0f, 24f), 0f);
        b.Prop("tarp", new Vector3(178f, 0f, -22f), 20f);
        b.Prop("plywood", new Vector3(202f, 0f, -18f), 70f);
        b.Prop("woodpallet", new Vector3(203.5f, 0f, -20.5f), 10f);
        foreach (var (x, z) in new[] { (209f, -12f), (209.7f, -12.6f) }) b.Prop("oilbarrel", new Vector3(x, 0f, z), x * 29f % 360f);
        b.Prop("wheelbarrow", new Vector3(180f, 0f, -3.5f), 20f);
        b.Prop("propane", new Vector3(209f, 0f, 13.5f), 0f);
        foreach (var (x, z) in new[] { (201f, -10.5f), (201.5f, 10f) }) b.Prop("brickdebris", new Vector3(x, 0f, z), x * 19f % 360f);
        DenseRooftops(b);
    }

    // Map dressing stage 5 (the user, 2026-11-12: "맵에 밀도가 없어"): the chapter's floors filled, kept off its ways. On
    // the roofs, clutter along the inside of the long parapets (the run across each roof keeps to its middle, |z| < 4);
    // in the streets, rubbish and bins along both kerbs past the barricades and abandoned cars askew in the lanes; at the
    // buildings' feet and in the gaps the jumps cross, rubbish seen from above. The city beyond the edge and the
    // buildings' own walls and windows are tables' work (MapSkyline, MapSurfaces, MapWindows).
    private static void DenseRooftops(GreyboxBuilder b)
    {
        const float In = 10f - Wall - .6f, Foot = 10.6f;
        string[] roof = { "!acunit", "oilbarrel", "cardboard", "trashbag", "brickdebris", "plasticcrate", "tire", "pallet", "cinderblock", "gascylinder" };
        string[] street = { "trashbag", "cardboard", "litter", "box", "tire", "plasticcrate", "trashcan", "!dumpster", "rubblebag", "oilbarrel" };
        string[] rubbish = { "trashbag", "cardboard", "box", "litter", "brickdebris" };
        // Roofs: B1 (18 m), B2 (16 m, past its water tank), B3 (13 m, clear of its AC units and dish), B4 (12 m, south side).
        b.Clutter(new Vector3(1.2f, 18f, In), new Vector3(23f, 18f, In), 3f, roof);
        b.Clutter(new Vector3(1.2f, 18f, -In), new Vector3(23f, 18f, -In), 3f, roof);
        b.Clutter(new Vector3(28.2f, 16f, In), new Vector3(46f, 16f, In), 3f, roof);
        b.Clutter(new Vector3(33f, 16f, -In), new Vector3(46f, 16f, -In), 3f, roof);
        b.Clutter(new Vector3(64.2f, 13f, In), new Vector3(79f, 13f, In), 3f, roof);
        b.Clutter(new Vector3(64.2f, 13f, -In), new Vector3(70.5f, 13f, -In), 3f, roof);
        b.Clutter(new Vector3(88.5f, AptRoof, AptS0 + .6f), new Vector3(98.5f, AptRoof, AptS0 + .6f), 3f, roof);
        // Street 1 (between B2 and B3): both kerbs past the barricades, cars left askew beyond them.
        foreach (float x in new[] { 47.6f, 62.4f })
        {
            b.Clutter(new Vector3(x, 0f, -47f), new Vector3(x, 0f, -14f), 3f, street);
            b.Clutter(new Vector3(x, 0f, 14f), new Vector3(x, 0f, 47f), 3f, street);
        }
        foreach (var (x, z, yaw) in new[] { (52.5f, -32f, 18f), (58f, -41f, 160f), (53.5f, 34f, 200f), (59f, 42f, 10f) })
            b.PropSolid("car", new Vector3(x, 0f, z), yaw);
        // Street 2 (between the warehouse and the building site's fence): both kerbs, cars.
        b.Clutter(new Vector3(141.6f, 0f, -47f), new Vector3(141.6f, 0f, -5f), 3f, street);
        b.Clutter(new Vector3(141.6f, 0f, 5f), new Vector3(141.6f, 0f, 47f), 3f, street);
        b.Clutter(new Vector3(169.3f, 0f, -47f), new Vector3(169.3f, 0f, -8f), 3f, street);
        b.Clutter(new Vector3(169.3f, 0f, 8f), new Vector3(169.3f, 0f, 47f), 3f, street);
        foreach (var (x, z, yaw) in new[] { (150f, -30f, 75f), (157f, -42f, 120f), (152f, 33f, 260f), (160f, 41f, 15f) })
            b.PropSolid("car", new Vector3(x, 0f, z), yaw);
        // The buildings' feet (seen from the roofs) and the square west of B1 with two wrecks.
        b.Clutter(new Vector3(-.6f, 0f, -9f), new Vector3(-.6f, 0f, 9f), 3f, street);
        foreach (var (x0, x1) in new[] { (1f, 23f), (28f, 46f), (64f, 79f) })
        {
            b.Clutter(new Vector3(x0, 0f, Foot), new Vector3(x1, 0f, Foot), 3.5f, street);
            b.Clutter(new Vector3(x0, 0f, -Foot), new Vector3(x1, 0f, -Foot), 3.5f, street);
        }
        b.PropSolid("car", new Vector3(-14f, 0f, 22f), 35f);
        b.PropSolid("car", new Vector3(-19f, 0f, -27f), 110f);
        // The gaps the jumps cross: B1 to B2, and under the swing bar (clear of its posts and the ladder back up).
        b.Clutter(new Vector3(25.5f, 0f, -9f), new Vector3(25.5f, 0f, 9f), 2.5f, rubbish);
        b.Clutter(new Vector3(83.5f, 0f, -9f), new Vector3(83.5f, 0f, -2.6f), 2.5f, rubbish);
        // The alley, along its walls clear of the bins, the pallet and the shutter's boxes (small things only: the hold's
        // horde comes through here).
        b.Clutter(new Vector3(104.6f, 0f, -3.6f), new Vector3(116.5f, 0f, -3.6f), 2.5f, "litter", "cardboard", "trashbag");
        b.Clutter(new Vector3(109.5f, 0f, 3.6f), new Vector3(115f, 0f, 3.6f), 2.5f, "litter", "cardboard", "trashbag");
    }

    // The building site's gimmicks: a container hanging from the crane over where the horde crosses the site to the ramps
    // (the map gimmick card "매달린 컨테이너 떨구기": shoot its cable and it falls on them), and the three places the ride may
    // come down (the card "착륙 자리 고르기"): the roof's pad, far up the containers and the scaffold but reached by zombies
    // only up the ramps; the yard behind the building, nearer and open; the street outside the gate, nearest and open all
    // round. The smoke lit at one (Ch1.15) takes the hold and the escape there.
    private static void SiteGimmicks(GreyboxBuilder b)
    {
        const float Hang = 20f;
        var drop = b.Switch<GimmickDrop>("공사장 매달린 컨테이너", new Vector3(180f, 0f, 0f));
        var load = new GameObject("떨어지는 짐").transform;
        load.SetParent(drop.transform, false);
        load.localPosition = new Vector3(0f, Hang, 0f);
        // Local to the load: a 6 m container under its slings.
        var box = b.Blocker("매달린 컨테이너", -3f, 3f, 0f, 2.6f, -1.25f, 1.25f, load, b.Metal, "MetalBox");
        var before = b.LastPlaced;
        b.Dress(box, "container");
        if (b.LastPlaced != before && b.LastPlaced != null) b.LastPlaced.transform.SetParent(load, true);
        b.Box("컨테이너 슬링 서", -2.6f, .05f, 2.6f, 2.68f, -.04f, .04f, b.Metal, solid: false, group: load);
        b.Box("컨테이너 슬링 동", -.05f, 2.6f, 2.6f, 2.68f, -.04f, .04f, b.Metal, solid: false, group: load);
        // The cable up to the arm (local to the gimmick), with a fat box round it for the shot to find.
        var cable = b.Box("크레인 케이블", -.05f, .05f, Hang + 2.6f, 30f, -.05f, .05f, b.Metal, solid: false, group: drop.transform);
        var aim = new GameObject("케이블 표적").transform;
        aim.SetParent(cable.transform, false);
        aim.gameObject.AddComponent<BoxCollider>().size = new Vector3(6f, 1f, 6f);
        aim.gameObject.AddComponent<GimmickTarget>();
        GreyboxBuilder.Configure(drop, so =>
        {
            so.FindProperty("load").objectReferenceValue = load;
            so.FindProperty("drop").floatValue = Hang;
            so.FindProperty("crush").boundsValue = new Bounds(new Vector3(180f, 1.5f, 0f), new Vector3(6.4f, 3f, 2.9f));
            so.FindProperty("cable").objectReferenceValue = cable;
        });

        // The landing zones: the roof's pad (where the step's Helipad stands), the yard, the street; each marked on the ground.
        var landing = b.Switch<GimmickLanding>("헬기 착륙 자리 고르기", new Vector3(180f, 0f, 8f));
        var zones = new List<Transform>
        {
            b.Mark("착륙 자리 옥상", new Vector3(191f, 14f, 0f), 0f, landing.transform),
            b.Mark("착륙 자리 마당", new Vector3(192f, 0f, 17f), 0f, landing.transform),
            b.Mark("착륙 자리 길", new Vector3(157f, 0f, -8f), 0f, landing.transform),
        };
        b.Box("착륙 자리 표시 마당", 188f, 196f, 0f, .03f, 13f, 21f, b.Marker, solid: false);
        b.Box("착륙 자리 표시 길", 153f, 161f, 0f, .03f, -12f, -4f, b.Marker, solid: false);
        b.Spot("Smoke", new Vector3(192f, 0f, 17f));
        GreyboxBuilder.Configure(landing, so =>
        {
            so.FindProperty("spot").stringValue = "Helipad";
            so.FindProperty("step").stringValue = "Ch1.15";
            so.FindProperty("reach").floatValue = 3.5f;
            var list = so.FindProperty("zones");
            list.arraySize = zones.Count;
            for (int i = 0; i < zones.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];
        });
    }
}
