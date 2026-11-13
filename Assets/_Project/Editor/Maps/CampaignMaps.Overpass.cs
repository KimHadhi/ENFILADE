using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// CampaignMaps: chapter 2, the overpass, with its dressing. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    [MenuItem("ENFILADE/맵/캠페인/2장 막힌 고가도로", priority = 12)]
    private static void Overpass()
    {
        var b = GreyboxBuilder.Begin("Ch2_Overpass", "2장 막힌 고가도로");
        if (b == null) return;
        const float Deck = 8f;

        b.Box("땅", -20f, 240f, -.5f, 0f, -40f, 40f, b.Road, "Asphalt");
        b.Box("맵 끝 서", -20f, -19f, 0f, 6f, -40f, 40f);
        b.Box("맵 끝 동", 239f, 240f, 0f, 6f, -40f, 40f);
        b.Box("맵 끝 남", -20f, 240f, 0f, 6f, -40f, -39f);
        b.Box("맵 끝 북", -20f, 240f, 0f, 6f, 39f, 40f);

        // The overpass, 8 m up, broken off at x 120. Under its first stretch is a fenced pit: a fall from the deck lands
        // there, and a ladder up the deck's side brings the faller back.
        // A road on top (this map's Road: concrete sides, a road's top), as the road below it.
        b.Box("고가 상판", -19f, 120f, Deck - 1f, Deck, -7f, 7f, b.Road, "Asphalt");
        for (float x = 0f; x <= 100f; x += 20f) b.Box("고가 기둥 " + x, x - 1f, x + 1f, 0f, Deck - 1f, -1f, 1f);
        b.Box("고가 난간 북", -19f, 120f, Deck, Deck + 1f, 6.7f, 7f);
        b.Box("고가 난간 남 1", -19f, 79f, Deck, Deck + 1f, -7f, -6.7f);
        b.Box("고가 난간 남 2", 81f, 120f, Deck, Deck + 1f, -7f, -6.7f);
        b.Box("구덩이 벽", 100f, 101f, 0f, 6f, -39f, 39f);
        b.Ladder("구덩이에서 고가로 올라가는 사다리", new Vector3(80f, 0f, -7.15f), 180f);
        b.EnemySpawn("구덩이 1", new Vector3(40f, 0f, 12f));
        b.EnemySpawn("구덩이 2", new Vector3(90f, 0f, -12f));

        // The wreck they crawl out of.
        b.Box("헬기 동체", 2f, 9f, Deck, Deck + 2.2f, 1f, 4f, b.Metal, "Metal");
        // The tail broke off and lies on the road (memo 48): hanging 1.6 m up it was a perch no zombie reached.
        b.Box("헬기 꼬리 (부러짐)", 9f, 16f, Deck, Deck + .8f, 2.2f, 2.8f, b.Metal, "Metal");
        b.Box("헬기 날개", 0f, 12f, Deck + 2.3f, Deck + 2.4f, 2.3f, 2.7f, b.Metal, solid: false);
        b.PlayerStart(new Vector3(4f, Deck, -3f), 90f);
        b.Spot("Start", new Vector3(4f, Deck, -3f));

        // The jam: rows of stopped cars and two buses to run over and between, an overturned lorry across every lane.
        const float LaneA = -5f, LaneB = -1.8f, LaneC = 1.8f, LaneD = 5f;
        Car(b, 18f, Deck, LaneA); Car(b, 18f, Deck, LaneC);
        Car(b, 24f, Deck, LaneB); Car(b, 24f, Deck, LaneD);
        Car(b, 30f, Deck, LaneA); Car(b, 30f, Deck, LaneB);
        Bus(b, "고가 버스 1", 34f, 46f, Deck, 1f, 3.6f);
        Car(b, 49f, Deck, LaneA); Car(b, 50f, Deck, LaneB);
        b.Box("뒤집힌 트럭", 56f, 63f, Deck, Deck + 2.6f, -6.6f, 6.6f, b.Metal, "MetalBox");
        b.Spot("Overturn", new Vector3(66f, Deck, 0f));
        Car(b, 68f, Deck, LaneC); Car(b, 70f, Deck, LaneA);
        Car(b, 75f, Deck, LaneD); Car(b, 76f, Deck, LaneB);
        Bus(b, "고가 버스 2", 84f, 96f, Deck, -6.3f, -3.7f);
        Car(b, 100f, Deck, LaneC); Car(b, 106f, Deck, LaneD); Car(b, 107f, Deck, LaneA);
        b.EnemySpawn("고가 1", new Vector3(21f, Deck, 0f));
        b.EnemySpawn("고가 2", new Vector3(45f, Deck, -4.5f));
        b.EnemySpawn("고가 3", new Vector3(73f, Deck, 3.4f));
        b.EnemySpawn("고가 4", new Vector3(104f, Deck, -2f));

        // The break: jagged ends, and a bus parked under it, 4.8 m down, which doesn't hurt. From the road there is no way
        // back up: no wall under the broken end to climb, and the deck is beyond a jump from the bus.
        b.Box("끊어진 끝 조각 1", 120f, 121.5f, Deck - 1f, Deck - .3f, -6f, -2.5f, solid: false);
        b.Box("끊어진 끝 조각 2", 120f, 122f, Deck - 1.4f, Deck - .9f, 1.5f, 4.5f, solid: false);
        b.Box("철근", 120f, 123f, Deck - .6f, Deck - .55f, -1f, 1f, b.Metal, solid: false);
        b.Spot("BrokenEdge", new Vector3(116f, Deck, 0f));
        Bus(b, "아래 버스", 119f, 131f, 0f, -3f, -.4f);
        b.Spot("UnderBus", new Vector3(126f, 0f, 4f));

        // The road below: sound barriers either side, a filling station (rest stop), abandoned cars.
        b.Box("방음벽 남", 101f, 239f, 0f, 5f, -25.3f, -25f);
        b.Box("방음벽 북", 101f, 239f, 0f, 5f, 25f, 25.3f);
        Car(b, 136f, 0f, -8f); Car(b, 152f, 0f, -14f); Car(b, 176f, 0f, 6f); Car(b, 181f, 0f, -10f);
        var station = new List<GameObject> { b.Box("주유소 지붕", 140f, 156f, 5f, 5.4f, 8f, 20f, b.Metal, "Metal") };
        foreach (var (x, z) in new[] { (140f, 8f), (155.5f, 8f), (140f, 19.5f), (155.5f, 19.5f) })
            station.Add(b.Box("주유소 기둥", x, x + .5f, 0f, 5f, z, z + .5f, b.Metal, "MetalPipe"));
        // Within a zombie's arm from the ground (memo 48).
        station.Add(b.Box("주유기 1", 145f, 146f, 0f, 1.5f, 13f, 14f, b.Metal, "Metal"));
        station.Add(b.Box("주유기 2", 150f, 151f, 0f, 1.5f, 13f, 14f, b.Metal, "Metal"));
        // The set piece (CampaignBlast, Tables/PackFx setPieceBlast): the pumps go up as the toll hold starts, 58 m behind
        // it (the station, the rest stop, is behind everyone by then), and leave the canopy, its posts and the pumps charred.
        b.Blast("주유소 폭발", new Vector3(148f, .5f, 14f), "Ch2.07", 1.2f, "setPieceBlast", station.ToArray());
        Room(b, "주유소 가게", 158f, 168f, 12f, 22f, 0f, 4f, Side.South, 161f, 163.5f);
        b.Door("주유소 가게 문", 161f, 163.5f, 0f, 2.4f, 12.05f, 12.25f, "Station", new Vector3(2.5f, 0f, 0f));
        b.Dress(b.Box("가게 계산대", 160f, 164f, 0f, 1f, 18.5f, 19.5f, b.Wood, "Wood"), "shopcounter");
        b.Prop("cashregister", new Vector3(161f, 1f, 19f), 180f);
        b.Shop(new Vector3(162f, 0f, 17.8f), new Vector3(162f, 2.2f, 19.5f));
        // The filling station's shop: a shelf, a fridge, a vending machine outside, bins by the pumps.
        b.Dress(b.Box("가게 진열대", 165.6f, 166.4f, 0f, 1.4f, 13.5f, 18f, b.Metal, "Metal"), "shopshelf");
        b.PropSolid("fridge", new Vector3(163.5f, 0f, 21.3f), 180f);
        b.PropSolid("vending", new Vector3(157.2f, 0f, 15f), 270f);
        b.PropSolid("bin", new Vector3(147.6f, 0f, 9.2f));
        foreach (var (x, z) in new[] { (141f, 19f), (142.1f, 19.4f), (156f, 9f) }) b.Prop("garbage", new Vector3(x, 0f, z), x * 11f % 360f);
        b.Spot("Station", new Vector3(163f, 0f, 17f));
        b.EnemySpawn("아래 도로 1", new Vector3(110f, 0f, 12f));
        b.EnemySpawn("아래 도로 2", new Vector3(140f, 0f, -15f));
        b.EnemySpawn("아래 도로 3", new Vector3(175f, 0f, 16f));
        b.EnemySpawn("아래 도로 4", new Vector3(185f, 0f, -16f));

        // The toll gate: booths under a canopy, then the fallen army checkpoint: sandbags, containers and a guard post
        // with the radio (finale). The subway entrance past it is where the chapter ends.
        b.Box("톨게이트 지붕", 188f, 198f, 6f, 6.5f, -24f, 24f, b.Concrete);
        for (float z = -18f; z <= 18f; z += 9f) b.Box("요금소 " + z, 191.5f, 194f, 0f, 2.8f, z - 1.25f, z + 1.25f);
        b.Spot("TollGate", new Vector3(198f, 0f, 0f));
        b.Dress(b.Box("모래주머니 남", 204f, 205f, 0f, 1.2f, -20f, -3f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("모래주머니 북", 204f, 205f, 0f, 1.2f, 3f, 20f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("군 컨테이너 1", 210f, 216f, 0f, 2.6f, -14f, -11.5f, b.Metal, "MetalBox"), "container");
        b.Dress(b.Box("군 컨테이너 2", 214f, 220f, 0f, 2.6f, 14f, 16.5f, b.Metal, "MetalBox"), "container");
        Room(b, "검문소 초소", 207f, 213f, 4f, 10f, 0f, 3f, Side.West, 6f, 8f);
        b.Dress(b.Box("초소 무전기", 211.5f, 212.6f, 0f, 1.1f, 8.6f, 9.6f, b.Marker), "controlboxFit");
        // The army left in a hurry: crates by the sandbags.
        foreach (var (x, z) in new[] { (206f, -5.5f), (207.2f, -5.9f), (206.3f, 5.6f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Spot("CheckpointRadio", new Vector3(211f, 0f, 8f));
        b.Spot("TollHold", new Vector3(206f, 0f, 0f));
        Room(b, "지하철 입구", 225f, 233f, -4f, 4f, 0f, 3.5f, Side.West, -2f, 2f, 3f);
        b.Box("입구 셔터 (3장으로)", 232f, 232.7f, 0f, 3.2f, -3.7f, 3.7f, b.Metal, "Metal");
        b.Spot("SubwayEntrance", new Vector3(229f, 0f, 0f));
        b.EnemySpawn("톨게이트 남", new Vector3(200f, 0f, -20f));
        b.EnemySpawn("톨게이트 북", new Vector3(200f, 0f, 20f));
        b.EnemySpawn("검문소 남", new Vector3(222f, 0f, -14f));
        b.EnemySpawn("검문소 북", new Vector3(222f, 0f, 12f));
        b.EnemySpawn("주유소 뒤", new Vector3(165f, 0f, -18f));

        // Background sound by area (the deck keeps the map's own): the road and the pit below, the shade under the deck,
        // the filling station's canopy and shop, the guard post, the subway entrance.
        b.Ambience("Ch2.Below", -20f, 240f, -1f, 6.5f, -40f, 40f, 0);
        b.Ambience("Ch2.UnderDeck", -19f, 120f, -1f, 7f, -7f, 7f);
        b.Ambience("Ch2.Canopy", 140f, 156f, 0f, 5f, 8f, 20f);
        b.Ambience("Ch2.Shop", 158f, 168f, 0f, 4f, 12f, 22f);
        b.Ambience("Ch2.GuardPost", 207f, 213f, 0f, 3f, 4f, 10f);
        b.Ambience("Ch2.SubwayEntrance", 225f, 233f, 0f, 3.5f, -4f, 4f);

        // Field pickups (6D).
        b.Pickup(new Vector3(6f, Deck, -1f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(66f, Deck, 2f));
        b.Pickup(new Vector3(114f, Deck, 2f));
        b.Pickup(new Vector3(128f, 0f, 2f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(163f, 0f, 15.5f));
        b.Pickup(new Vector3(161f, 0f, 16f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(198f, 0f, 2f));
        b.Pickup(new Vector3(206f, 0f, 2f));
        b.Pickup(new Vector3(227f, 0f, 2f));
        DressOverpass(b);
        b.Finish("2장 막힌 고가도로를 지었어요 (Scenes/Campaign/Ch2_Overpass).\n" +
                 "길: 헬기 잔해 → 막힌 차들 → 뒤집힌 트럭 → 끊어진 끝에서 버스로 뛰어내리기(돌아갈 수 없음) → 주유소 → 톨게이트 → 초소 무전 → 버티기 → 지하철 입구.");
    }

    // Chapter 2's jam on the deck, the road below, the filling station and the army checkpoint in the packs' props (map
    // dressing stage 3): luggage left in the jam, cones by the wreck and the broken end, lamps along the road, a burned-
    // out army lorry and police cars before the toll, an army lorry and guard towers at the checkpoint, barbed wire on its
    // sandbags and HESCO walls to its sides.
    private static void DressOverpass(GreyboxBuilder b)
    {
        const float Deck = 8f;
        foreach (var (x, z) in new[] { (12.5f, -5.4f), (13.8f, -4.6f), (114.5f, 2f) }) b.Prop("cone", new Vector3(x, Deck, z), x * 31f % 360f);
        b.Prop("warninglight", new Vector3(11.2f, Deck, -6.1f), 0f);
        foreach (var (x, z, yaw) in new[] { (26f, 3.4f, 40f), (51.5f, -3.2f, 120f), (77.5f, 3.2f, 200f) }) b.Prop("suitcase", new Vector3(x, Deck, z), yaw);
        b.Prop("bag", new Vector3(65.5f, Deck, 2.2f), 70f);
        foreach (var (x, z) in new[] { (22f, .3f), (47f, 1.2f), (72f, -.4f), (103f, .9f) }) b.Prop("litter", new Vector3(x, Deck, z), x * 13f % 360f);
        b.Prop("trashbag", new Vector3(33.5f, Deck, 6.1f), 30f);
        foreach (var (x, z) in new[] { (28f, 6.2f), (52f, -6.2f), (90f, 5.8f) }) b.Prop("rubble", new Vector3(x, Deck, z), x * 11f % 360f);
        // The broken end: barriers and a warning light before the drop.
        b.Prop("jersey", new Vector3(115.5f, Deck, -4.5f), 90f);
        b.Prop("jersey", new Vector3(115.5f, Deck, 4.2f), 90f);
        b.Prop("sawhorse", new Vector3(114f, Deck, 0f), 90f);
        b.Prop("warninglight", new Vector3(113.5f, Deck, -2.2f), 90f);
        // The road below.
        foreach (var (x, z, yaw) in new[] { (115f, -23.5f, 0f), (140f, 23.5f, 180f), (170f, -23.5f, 0f), (185f, 23.5f, 180f) })
            b.Prop("streetlamp", new Vector3(x, 0f, z), yaw);
        b.Prop("roadplate", new Vector3(130f, 0f, 6f), 10f);
        b.Prop("roadplate", new Vector3(171f, 0f, -2f), 80f);
        b.PropSolid("wrecktruck", new Vector3(184f, 0f, 15f), 30f);
        b.PropSolid("policecar", new Vector3(199.5f, 0f, -11f), 95f);
        b.PropSolid("policecar", new Vector3(200f, 0f, 11.5f), 85f);
        foreach (var (x, z) in new[] { (196.5f, -4.5f), (196.5f, 4.5f), (197f, -13.5f), (197f, 13.5f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 17f % 360f);
        foreach (var z in new[] { -2.6f, 2.6f }) b.Prop("trafficdrum", new Vector3(195.5f, 0f, z));
        // The filling station.
        foreach (var (x, z) in new[] { (156.5f, 23f), (157.3f, 23.6f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("gascylinder", new Vector3(155.5f, 0f, 23.2f));
        b.Prop("tirestack", new Vector3(169.5f, 0f, 9.5f), 20f);
        b.Prop("jersey", new Vector3(144f, 0f, 6.6f), 0f);
        b.Prop("jersey", new Vector3(150f, 0f, 6.6f), 0f);
        foreach (var (x, z) in new[] { (148f, 10.5f), (152f, 16f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 19f % 360f);
        b.Prop("trashbag", new Vector3(157.8f, 0f, 12.5f), 120f);
        // The checkpoint: barbed wire along the sandbags' top, an army lorry, two guard towers, HESCO walls.
        for (float z = -19f; z <= -4f; z += 1.7f) b.Prop("barbedwire", new Vector3(204.5f, 1f, z), 90f);
        for (float z = 4f; z <= 19f; z += 1.7f) b.Prop("barbedwire", new Vector3(204.5f, 1f, z), 90f);
        b.PropSolid("militarytruck", new Vector3(217f, 0f, -8f), 0f);
        b.PropSolid("guardtower", new Vector3(226.5f, 0f, 19f), 180f);
        b.PropSolid("guardtower", new Vector3(226.5f, 0f, -19f), 0f);
        b.Dress(b.Box("HESCO 남", 220.5f, 222.4f, 0f, 2f, -24f, -17f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("HESCO 북", 220.5f, 222.4f, 0f, 2f, 17f, 24f, b.Wood, "Tarp"), "hesco");
        DenseOverpass(b);
    }

    // Chapter 2's density (map dressing stage 5, the user 2026-11-13: "2~4장 진행"): litter along the deck's rails, clear
    // of the wreck's cones, the ladder's gap and the broken end's barriers; rubbish along the sound barriers on the road
    // below, before the toll gate; debris along the fenced pit's walls.
    private static void DenseOverpass(GreyboxBuilder b)
    {
        const float Deck = 8f;
        string[] deck = { "litter", "trashbag", "cardboard", "brickdebris", "tire", "plasticcrate" };
        string[] road = { "trashbag", "cardboard", "litter", "tire", "rubblebag", "oilbarrel", "box" };
        string[] pit = { "rubble", "brickdebris", "tire", "litter", "cinderblock" };
        b.Clutter(new Vector3(18f, Deck, 6.2f), new Vector3(112f, Deck, 6.2f), 4.5f, deck);
        b.Clutter(new Vector3(18f, Deck, -6.2f), new Vector3(77f, Deck, -6.2f), 4.5f, deck);
        b.Clutter(new Vector3(83f, Deck, -6.2f), new Vector3(112f, Deck, -6.2f), 4.5f, deck);
        foreach (float z in new[] { 24.4f, -24.4f }) b.Clutter(new Vector3(103f, 0f, z), new Vector3(186f, 0f, z), 4f, road);
        b.Clutter(new Vector3(99.3f, 0f, -38f), new Vector3(99.3f, 0f, 38f), 4f, pit);
        foreach (float z in new[] { 38.3f, -38.3f }) b.Clutter(new Vector3(-17f, 0f, z), new Vector3(98f, 0f, z), 4.5f, pit);
    }
}
