using System.Collections.Generic;
using UnityEngine;

// CampaignMaps: chapter 4's gimmicks in the terminal (the bought pack's scene, so every place here is measured off its own
// floor plan and props: the security lane's x-ray belt and body scanner, the info desks, the speakers, the fire alarm
// panel in the control room off the entrance hall): the security lane, the announcement desks, the fire alarm and the
// control room's two-lever shutter, and who lies or walks where in the terminal. One part of the class; the rest is in
// CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    // The terminal's own speakers (the pack's speaker_0001 props, high on the walls), where the announcements and the fire
    // bells sound: the lounge's two, the check-in hall's, the food court's, the gate room's, the gate hall's two.
    private static readonly Vector3[] TerminalSpeakers =
    {
        new Vector3(113.28f, 6.95f, -31.42f), new Vector3(126.72f, 6.95f, -31.49f), new Vector3(178.63f, 7.41f, -41.97f),
        new Vector3(233.07f, 7.34f, -29.39f), new Vector3(266.53f, 7.47f, 5.01f), new Vector3(226.71f, 6.71f, 34.7f),
        new Vector3(237.26f, 6.71f, 34.7f)
    };

    private static void TerminalGimmicks(GreyboxBuilder b)
    {
        var speakers = new List<Transform>();
        foreach (var at in TerminalSpeakers) speakers.Add(b.Mark("터미널 스피커", at));
        SecurityLane(b);
        InfoDesks(b, speakers);
        FireAlarm(b, speakers);
        TerminalCrowds(b);
    }

    // The security lane in the entrance corridor (the card "보안 검색대", after Hitman's detectors and PAYDAY 2 Golden Grin):
    // the corridor (x 78-82.1) is split by glass along the x-ray belt; east of it the body scanner's arch is the way
    // through (GimmickDetector: with guns it beeps and wakes the hall); the belt carries a player's guns in a tray to its
    // end (GimmickTray), so they walk the arch bare-handed; west of the belt the operators' strip ends in the staff gate
    // (GimmickLock: the staff card, from landside, or broken in loud). The pack's scanner gets no solid box of its own
    // (TerminalParts.Fixed): its two side panels and top stand here, the arch open between them.
    private static void SecurityLane(GreyboxBuilder b)
    {
        var glass = GreyboxBuilder.MaterialAt("Tower/TowerGlass.mat");
        // The scanner's arch (the pack's model, 1.4 m wide: two 15 cm panels, 1.1 m open between them, walked along z).
        foreach (var (a, c) in new[] { (80.49f, 80.64f), (81.74f, 81.89f) }) Unseen(b.Box("검색대 문 옆판", a, c, 0f, 2.71f, 23.66f, 24.66f));
        Unseen(b.Box("검색대 문 위", 80.49f, 81.89f, 2.25f, 2.71f, 23.66f, 24.66f));
        // Glass between the belt and the arch, from the x-ray machine's exit to the belt's head, up past a jump.
        b.SeeThrough(b.Box("검색대 유리 칸막이", 80.34f, 80.44f, 0f, 3f, 22.86f, 28.6f, glass, "ArmorGlass"));
        // Across the operators' strip at the machine's exit: the staff gate, glass over it and over the belt's exit.
        b.SeeThrough(b.Box("직원 문 위 유리", 78f, 79.45f, 2.3f, 3f, 22.8f, 22.92f, glass, "ArmorGlass"));
        b.SeeThrough(b.Box("벨트 출구 위 유리", 79.45f, 80.34f, 1.66f, 3f, 22.8f, 22.92f, glass, "ArmorGlass"));
        var gate = b.Blocker("직원 문 (직원 카드)", 78f, 79.45f, 0f, 2.3f, 22.8f, 22.92f, null, b.Metal, "Metal");
        var staffLock = b.AddSwitch<GimmickLock>(gate);
        var hall = new Bounds(new Vector3(96f, 2f, 3.5f), new Vector3(54f, 6f, 53f));
        GreyboxBuilder.Configure(staffLock, so =>
        {
            so.FindProperty("item").stringValue = "staffcard";
            so.FindProperty("itemTitle").stringValue = "gimmick.item.staffcard";
            so.FindProperty("toughness").floatValue = 320f;
            so.FindProperty("wakeBox").boundsValue = hall;
            so.FindProperty("openOffset").vector3Value = new Vector3(-1.5f, 0f, 0f);
        });

        // The detector: its arch, what its beep wakes (the corridor, the entrance hall and the way to the lounge), its lamp.
        var detector = b.Switch<GimmickDetector>("금속 탐지기", Vector3.zero);
        var lamp = new GameObject("탐지기 등", typeof(Light)).GetComponent<Light>();
        lamp.transform.SetParent(detector.transform, false);
        lamp.transform.position = new Vector3(81.19f, 2.85f, 24.16f);
        lamp.type = LightType.Point;
        lamp.range = 4f;
        lamp.intensity = 3f;
        lamp.color = new Color(1f, .15f, .1f);
        lamp.shadows = LightShadows.None;
        lamp.lightmapBakeType = LightmapBakeType.Realtime;
        GreyboxBuilder.Configure(detector, so =>
        {
            so.FindProperty("arch").boundsValue = new Bounds(new Vector3(81.19f, 1.1f, 24.16f), new Vector3(1.1f, 2.2f, 1f));
            so.FindProperty("wakeBox").boundsValue = hall;
            so.FindProperty("lamp").objectReferenceValue = lamp;
        });

        // The belt: its head north of the machine, its end south of it; the tray (a basket with a bag in it) rides it.
        var tray = b.Switch<GimmickTray>("엑스레이 벨트", Vector3.zero);
        var head = b.Mark("벨트 머리", new Vector3(79.9f, .8f, 28.3f), 0f, tray.transform);
        var end = b.Mark("벨트 끝", new Vector3(79.9f, .8f, 21.6f), 0f, tray.transform);
        var basket = new GameObject("바구니").transform;
        basket.SetParent(tray.transform, false);
        basket.position = head.position;
        b.Box("바구니 바닥", -.28f, .28f, 0f, .1f, -.22f, .22f, b.Metal, solid: false, group: basket);
        b.Box("바구니 속 총 가방", -.2f, .2f, .1f, .24f, -.13f, .13f, b.Wood, "Tarp", solid: false, group: basket);
        GreyboxBuilder.Configure(tray, so =>
        {
            so.FindProperty("head").objectReferenceValue = head;
            so.FindProperty("end").objectReferenceValue = end;
            so.FindProperty("tray").objectReferenceValue = basket.gameObject;
            so.FindProperty("rideSeconds").floatValue = 6f;
            so.FindProperty("reach").floatValue = 1.7f;
        });

        // The staff card: on landside, by a sleeping guard, where the run puts it (three places); the security
        // board in the corridor says where it was lost.
        var card = b.Switch<GimmickItem>("직원 카드", new Vector3(31.8f, 0f, 20.4f));
        var spots = new List<Transform>
        {
            b.Mark("직원 카드 자리 경찰차", new Vector3(31.8f, 0f, 20.4f), 0f, card.transform.parent),
            b.Mark("직원 카드 자리 셔틀버스", new Vector3(55.4f, 0f, 31f), 0f, card.transform.parent),
            b.Mark("직원 카드 자리 지게차", new Vector3(12.6f, 0f, 6.8f), 0f, card.transform.parent),
        };
        b.Box("카드", -.06f, .06f, 0f, .02f, -.04f, .04f, b.Marker, solid: false, group: card.transform);
        var shine = new GameObject("카드 빛", typeof(Light)).GetComponent<Light>();
        shine.transform.SetParent(card.transform, false);
        shine.transform.localPosition = Vector3.up * .3f;
        shine.type = LightType.Point;
        shine.range = 1.4f;
        shine.intensity = .7f;
        shine.color = new Color(.5f, .85f, 1f);
        shine.shadows = LightShadows.None;
        shine.lightmapBakeType = LightmapBakeType.Realtime;
        b.Sleepers("Ch4.02", new Vector3(32.6f, 0f, 20.4f), 1, .1f, card.transform);
        GreyboxBuilder.Configure(card, so =>
        {
            so.FindProperty("id").stringValue = "staffcard";
            so.FindProperty("title").stringValue = "gimmick.item.staffcard";
            so.FindProperty("salt").intValue = 41;
            FillObjects(so.FindProperty("spots"), spots);
            Fill(so.FindProperty("places"), new List<string> { "gimmick.place.ch4.policecar", "gimmick.place.ch4.bus", "gimmick.place.ch4.forklift" });
        });
        var board = b.Switch<GimmickClue>("보안 게시판", new Vector3(81.75f, 1.3f, 31.69f));
        GreyboxBuilder.Configure(board, so =>
        {
            so.FindProperty("item").stringValue = "staffcard";
            so.FindProperty("line").stringValue = "gimmick.clue.ch4.board";
            so.FindProperty("prompt").stringValue = "gimmick.clue.ch4.prompt";
            so.FindProperty("reach").floatValue = 1.8f;
        });
    }

    // A box that stands for a model's solid part: felt, never seen (the model shows).
    private static GameObject Unseen(GameObject box)
    {
        box.GetComponent<Renderer>().enabled = false;
        return box;
    }

    // The announcement desks (the card "공항 방송 미끼", after PAYDAY 3's Rock the Cradle): one of three works each run, the
    // lounge's info desk, the gate hall's or the check-in hall's boarding counter, and its call plays on every speaker
    // in the terminal and pages the far wing, where every calm zombie that hears it walks off to (GimmickLure's gatherAt).
    private static void InfoDesks(GreyboxBuilder b, List<Transform> speakers)
    {
        var east = b.Mark("방송이 부르는 곳 동쪽 (탑승 홀)", new Vector3(230f, 0f, 33f));
        var west = b.Mark("방송이 부르는 곳 서쪽 (입구 홀)", new Vector3(95f, 0f, 12f));
        var desks = b.Variant("안내 방송 데스크 (셋 중 하나)", 1, 260);
        foreach (var (name, at, pages) in new[]
        {
            ("라운지 안내 데스크", new Vector3(119.68f, 1f, -31.68f), east),
            ("탑승 홀 안내 데스크", new Vector3(231.83f, 1f, 28.42f), west),
            ("체크인 홀 탑승 카운터", new Vector3(170.4f, 5f, -44.44f), east),
        })
        {
            var desk = b.Switch<GimmickLure>(name, at);
            desk.transform.SetParent(b.Choice(desks, name), true);
            GreyboxBuilder.Configure(desk, so =>
            {
                so.FindProperty("prompt").stringValue = pages == east ? "gimmick.lure.pa.east" : "gimmick.lure.pa.west";
                so.FindProperty("cue").enumValueIndex = (int)GimmickSounds.Cue.Chime;
                so.FindProperty("radius").floatValue = 45f;
                so.FindProperty("seconds").floatValue = 28f;
                so.FindProperty("reach").floatValue = 3.4f;
                FillObjects(so.FindProperty("emitters"), speakers);
                so.FindProperty("gatherAt").objectReferenceValue = pages;
            });
        }
    }

    // The fire alarm (the card "꺼야 멈추는 화재 경보"): smoke detectors on the ceilings of the check-in hall, the shops'
    // concourse, the food court, the south-east gallery and the gate hall, the bells at the terminal's speakers, red beacons
    // turning under the ceilings, and its panel in the control room off the corridor from the entrance hall (the pack's own
    // fire panel, BMA_Schaltkasten), behind a steel shutter two levers roll up (the card "둘이 같이 하는 일").
    // Ceiling heights are the pack's: 4 m over the ground floor's corridor and hall, 8 m over the upper floor and the
    // two-storey gate hall (skylights left out).
    private static void FireAlarm(GreyboxBuilder b, List<Transform> speakers)
    {
        var alarm = b.Switch<GimmickFireAlarm>("화재 경보", Vector3.zero);
        var panel = b.Mark("화재 경보반", new Vector3(128.55f, 1.3f, 5.57f), 0f, alarm.transform);
        var detectors = new List<Transform>();
        foreach (var at in new[] { new Vector3(170f, 7.95f, -50f), new Vector3(185f, 7.95f, -68f), new Vector3(238f, 7.95f, -30f),
                     new Vector3(236f, 7.95f, -1f), new Vector3(226f, 7.95f, 30f) })
        {
            var detector = b.Box("화재 감지기", at.x - .18f, at.x + .18f, at.y - .07f, at.y, at.z - .18f, at.z + .18f, b.Concrete, "Plaster", solid: false, group: alarm.transform);
            detector.AddComponent<GimmickTarget>();
            b.Box("화재 감지기 불", at.x - .03f, at.x + .03f, at.y - .09f, at.y - .07f, at.z - .03f, at.z + .03f, b.Marker, solid: false, group: alarm.transform);
            detectors.Add(detector.transform);
        }
        var beacons = new List<GimmickBeacon>();
        foreach (var at in new[] { new Vector3(104f, 3.85f, 16f), new Vector3(116f, 3.85f, 0f), new Vector3(156f, 7.85f, -61f),
                     new Vector3(200f, 7.85f, -66f), new Vector3(238f, 7.85f, -31.5f), new Vector3(222f, 7.85f, 33f) })
        {
            b.Box("경광등 몸통", at.x - .12f, at.x + .12f, at.y - .05f, at.y + .15f, at.z - .12f, at.z + .12f, b.Marker, solid: false, group: alarm.transform);
            var turn = new GameObject("경광등", typeof(Light), typeof(GimmickBeacon));
            turn.transform.SetParent(alarm.transform, false);
            turn.transform.SetPositionAndRotation(at, Quaternion.Euler(35f, 0f, 0f));
            var beam = turn.GetComponent<Light>();
            beam.type = LightType.Spot;
            beam.spotAngle = 45f;
            beam.innerSpotAngle = 25f;
            beam.range = 22f;
            beam.intensity = 30f;
            beam.color = new Color(1f, .1f, .05f);
            beam.shadows = LightShadows.None;
            beam.lightmapBakeType = LightmapBakeType.Realtime;
            GreyboxBuilder.Configure(turn.GetComponent<GimmickBeacon>(), so =>
            {
                so.FindProperty("beam").objectReferenceValue = beam;
                so.FindProperty("turnSeconds").floatValue = 1.2f;
            });
            beacons.Add(turn.GetComponent<GimmickBeacon>());
        }
        GreyboxBuilder.Configure(alarm, so =>
        {
            so.FindProperty("panel").objectReferenceValue = panel;
            FillObjects(so.FindProperty("bells"), speakers);
            FillObjects(so.FindProperty("beacons"), beacons);
            FillObjects(so.FindProperty("detectors"), detectors);
            so.FindProperty("reach").floatValue = 1.8f;
        });

        // The control room (x 122-129, z 2.75-7.25, its door in its west wall at z 4.25-5.5): a steel shutter across the
        // door, one lever beside it, the other across the corridor on its west wall. Supplies inside.
        var hands = b.Switch<GimmickTwoHands>("관제실 셔터", Vector3.zero);
        var shutter = b.Blocker("관제실 셔터 (레버 둘)", 121.85f, 122.15f, 0f, 2.3f, 4.2f, 5.55f, hands.transform, b.Metal, "Metal");
        var levers = new List<Transform>();
        foreach (var (at, face) in new[] { (new Vector3(121.82f, 1.05f, 3.55f), 270f), (new Vector3(110.32f, 1.05f, -2f), 90f) })
        {
            // The plate on the wall, and the lever's arm on a pivot turned about the wall's run.
            Vector3 back = Quaternion.Euler(0f, face, 0f) * Vector3.forward * -.06f;
            var plate = new GameObject("레버 판").transform;
            plate.SetParent(hands.transform, false);
            plate.SetPositionAndRotation(at + back, Quaternion.Euler(0f, face, 0f));
            b.Box("레버 판", -.18f, .18f, -.3f, .3f, -.03f, .03f, b.Metal, "Metal", solid: false, group: plate);
            var pivot = new GameObject("레버").transform;
            pivot.SetParent(hands.transform, false);
            pivot.SetPositionAndRotation(at, Quaternion.Euler(0f, face, 0f));
            b.Box("레버 팔", -.03f, .03f, 0f, .42f, -.03f, .03f, b.Marker, solid: false, group: pivot);
            b.Box("레버 손잡이", -.08f, .08f, .4f, .46f, -.04f, .04f, b.Metal, "Metal", solid: false, group: pivot);
            levers.Add(pivot);
        }
        GreyboxBuilder.Configure(hands, so =>
        {
            so.FindProperty("first").objectReferenceValue = levers[0];
            so.FindProperty("second").objectReferenceValue = levers[1];
            so.FindProperty("shutter").objectReferenceValue = shutter.transform;
            so.FindProperty("rise").floatValue = 2.4f;
            so.FindProperty("latchSeconds").floatValue = 8f;
            so.FindProperty("reach").floatValue = 1.6f;
        });
        b.Pickup(new Vector3(125.5f, 0f, 6.3f));
        b.Pickup(new Vector3(127.6f, 0f, 3.4f), PickupSpot.Roll.Gun);
    }

    // Who is in the terminal before anyone comes in (placed as the way to the security lane begins, out of sight): asleep
    // in the entrance hall past the scanner and along the corridor to the lounge, and one walking the hall, so a beep at
    // the scanner (or the staff gate broken in) is felt at once.
    private static void TerminalCrowds(GreyboxBuilder b)
    {
        b.Sleepers("Ch4.03", new Vector3(90f, 0f, 16f), 3, 1.4f);
        b.Sleepers("Ch4.03", new Vector3(104f, 0f, 9.5f), 2, 1.2f);
        b.Sleepers("Ch4.03", new Vector3(116f, 0f, -10f), 2, 1.2f);
        b.Walkers("Ch4.03", 1, null, new Vector3(76f, 0f, 12f), new Vector3(108f, 0f, 12f));
    }
}
