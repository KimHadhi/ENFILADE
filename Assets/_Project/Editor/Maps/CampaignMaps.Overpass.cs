using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// CampaignMaps: chapter 2, the overpass, with its dressing. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    // Chapter 2 as the map gimmicks remade it (the user's picks of 2026-11-14, all seven of the chapter's cards): the jam
    // on the broken overpass with its alarm cars, the drop to the road, the service area (indoors, the rest stop in its
    // office) with its pumps to spill, the car pile across the road that only fire clears, the ramp up to the toll road,
    // the toll office upstairs (its cameras and the barrier's switch), the army checkpoint (its armory and the officer's
    // card), and the last span that falls in behind them on the run to the subway entrance.
    private const float Deck = 8f, RoadEnd = 362f;

    [MenuItem("ENFILADE/맵/캠페인/2장 막힌 고가도로", priority = 12)]
    private static void Overpass()
    {
        var b = GreyboxBuilder.Begin("Ch2_Overpass", "2장 막힌 고가도로");
        if (b == null) return;

        b.Box("땅", -20f, RoadEnd, -.5f, 0f, -40f, 40f, b.Road, "Asphalt");
        b.Box("맵 끝 서", -20f, -19f, 0f, 6f, -40f, 40f);
        b.Box("맵 끝 동", RoadEnd - 1f, RoadEnd, 0f, 6f, -40f, 40f);
        b.Box("맵 끝 남", -20f, RoadEnd, 0f, 6f, -40f, -39f);
        b.Box("맵 끝 북", -20f, RoadEnd, 0f, 6f, 39f, 40f);

        // The overpass, 8 m up, broken off at x 120. Under its first stretch is a fenced pit: a fall from the deck lands
        // there, and a ladder up the deck's side brings the faller back.
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

        // The jam: rows of stopped cars and two buses to run over and between, an overturned lorry across every lane; some
        // cars' alarms still set (OverpassJam).
        OverpassJam(b);
        Bus(b, "고가 버스 1", 34f, 46f, Deck, 1f, 3.6f);
        b.Box("뒤집힌 트럭", 56f, 63f, Deck, Deck + 2.6f, -6.6f, 6.6f, b.Metal, "MetalBox");
        b.Spot("Overturn", new Vector3(66f, Deck, 0f));
        Bus(b, "고가 버스 2", 84f, 96f, Deck, -6.3f, -3.7f);
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

        // The road below: sound barriers either side all the way to the subway entrance.
        b.Box("방음벽 남", 101f, RoadEnd, 0f, 5f, -25.3f, -25f);
        b.Box("방음벽 북", 101f, RoadEnd, 0f, 5f, 25f, 25.3f);
        Car(b, 133f, 0f, -8f); Car(b, 136f, 0f, 12f);
        b.EnemySpawn("아래 도로 1", new Vector3(110f, 0f, 12f));
        b.EnemySpawn("아래 도로 2", new Vector3(140f, 0f, -22f));
        b.EnemySpawn("아래 도로 3", new Vector3(181f, 0f, 20f));
        b.EnemySpawn("아래 도로 4", new Vector3(181f, 0f, -22f));
        b.EnemySpawn("휴게소 뒤", new Vector3(165f, 0f, 24f));

        // The service area (the card "고속도로 휴게소 건물"): its forecourt's pumps (the card "주유소 기름 함정") and the
        // building, the rest stop in its office (ServiceArea).
        ServiceArea(b);

        // The car pile across the road (the card "불로만 치우는 바리케이드"): fuel from the service area's store poured on
        // it and lit, it burns while they hold, sinking as it goes (BurnPile).
        BurnPile(b);

        // Up to the toll road: the ramp, the road below it walled off (the only way on is up), the second deck with the
        // toll plaza and its office, the army checkpoint, and the last span (TollRoad).
        TollRoad(b);

        // Background sound by area (the deck keeps the map's own): the road and the pit below, the shade under the deck,
        // the service area's rooms, the toll office, the guard post, the subway entrance.
        b.Ambience("Ch2.Below", -20f, RoadEnd, -1f, 6.5f, -40f, 40f, 0);
        b.Ambience("Ch2.UnderDeck", -19f, 120f, -1f, 7f, -7f, 7f);
        b.Ambience("Ch2.Canopy", 138f, 154f, 0f, 5f, -20f, -8f);
        b.Ambience("Ch2.Shop", 156f, 178f, 0f, 4.5f, 4f, 22f);
        b.Ambience("Ch2.GuardPost", 222f, 234f, Deck, Deck + 6f, 9f, 17f);
        b.Ambience("Ch2.GuardPost", 286f, 294f, Deck, Deck + 3f, -9f, -4f);
        b.Ambience("Ch2.SubwayEntrance", 350f, 358f, 0f, 3.5f, -4f, 4f);

        // Field pickups (6D).
        b.Pickup(new Vector3(6f, Deck, -1f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(66f, Deck, 2f));
        b.Pickup(new Vector3(114f, Deck, 2f));
        b.Pickup(new Vector3(128f, 0f, 2f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(175f, 0f, 19f));
        b.Pickup(new Vector3(171f, 0f, 20.5f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(222f, Deck, 2f));
        b.Pickup(new Vector3(280f, Deck, 2f));
        b.Pickup(new Vector3(352f, 0f, 2f));
        DressOverpass(b);
        b.Finish("2장 막힌 고가도로를 지었어요 (Scenes/Campaign/Ch2_Overpass).\n" +
                 "길: 헬기 잔해 → 경보 차가 섞인 정체 → 뒤집힌 트럭 → 끊어진 끝에서 버스로 뛰어내리기(돌아갈 수 없음) → 휴게소(사무실이 휴게소) → " +
                 "창고 기름통을 차 무더기에 붓고 불붙여 버티기(주유기 기름 함정) → 램프로 톨게이트 고가 → 관제실 2층에서 차단벽 올리기(CCTV) → " +
                 "군 검문소(무기고·장교 카드) → 초소 무전 → 버티기 → 무너지는 마지막 경간을 달려 지하철 입구.");
    }

    // Chapter 2's jam on the deck, the road below, the service area and the toll road in the packs' props (map dressing
    // stage 3): luggage left in the jam, cones by the wreck and the broken end, lamps along the road, police cars before the
    // pile, the toll road's army lorry and guard towers, barbed wire on its sandbags and HESCO walls to its sides.
    private static void DressOverpass(GreyboxBuilder b)
    {
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
        foreach (var (x, z, yaw) in new[] { (115f, -23.5f, 0f), (140f, 23.5f, 180f), (178f, -23.5f, 0f) })
            b.Prop("streetlamp", new Vector3(x, 0f, z), yaw);
        b.Prop("roadplate", new Vector3(130f, 0f, 6f), 10f);
        b.PropSolid("policecar", new Vector3(182f, 0f, -16f), 95f);
        b.PropSolid("policecar", new Vector3(182.5f, 0f, 16.5f), 85f);
        foreach (var (x, z) in new[] { (180.5f, -9.5f), (180.5f, 9.5f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 17f % 360f);
        DenseOverpass(b);
    }

    // Chapter 2's density (map dressing stage 5): litter along the deck's rails, clear of the wreck's cones, the ladder's
    // gap and the broken end's barriers; rubbish along the sound barriers before the service area; debris along the fenced
    // pit's walls.
    private static void DenseOverpass(GreyboxBuilder b)
    {
        string[] deck = { "litter", "trashbag", "cardboard", "brickdebris", "tire", "plasticcrate" };
        string[] road = { "trashbag", "cardboard", "litter", "tire", "rubblebag", "oilbarrel", "box" };
        string[] pit = { "rubble", "brickdebris", "tire", "litter", "cinderblock" };
        b.Clutter(new Vector3(18f, Deck, 6.2f), new Vector3(112f, Deck, 6.2f), 4.5f, deck);
        b.Clutter(new Vector3(18f, Deck, -6.2f), new Vector3(77f, Deck, -6.2f), 4.5f, deck);
        b.Clutter(new Vector3(83f, Deck, -6.2f), new Vector3(112f, Deck, -6.2f), 4.5f, deck);
        foreach (float z in new[] { 24.4f, -24.4f }) b.Clutter(new Vector3(103f, 0f, z), new Vector3(136f, 0f, z), 4f, road);
        b.Clutter(new Vector3(99.3f, 0f, -38f), new Vector3(99.3f, 0f, 38f), 4f, pit);
        foreach (float z in new[] { 38.3f, -38.3f }) b.Clutter(new Vector3(-17f, 0f, z), new Vector3(98f, 0f, z), 4.5f, pit);
    }
}
