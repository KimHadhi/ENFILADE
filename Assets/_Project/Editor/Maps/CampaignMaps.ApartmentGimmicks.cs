using System.Collections.Generic;
using UnityEngine;
using Side = GreyboxBuilder.Side;

// CampaignMaps: chapter 1's block of flats, its gimmicks: the lift, the roof's machine room, what changes from run to run,
// the caretaker's log and the master key, the vents, the TVs, the cellar's switchboard, who sleeps where and who walks the
// corridors, and the rooms' furniture. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    // The flats' rooms, for their vents, crowds and furniture: the flat's number on a floor (1 NW, 2 NE, 3 SE, 4 SW), its
    // front room and back room (x0, x1, z0, z1).
    private static readonly (int n, (float, float, float, float) front, (float, float, float, float) back)[] Flats =
    {
        (1, (AptIn0, PartyX, 1.5f, FrontNorth), (AptIn0, PartyX, FrontNorth + Wall, AptN1)),
        (2, (PartyX + Wall, CoreX - Wall, 1.5f, FrontNorth), (PartyX + Wall, AptIn1, CoreZ1 + Wall, AptN1)),
        (3, (PartyX + Wall, ShaftX - Wall, FrontSouth, -1.5f), (PartyX + Wall, AptIn1, AptS0, FrontSouth - Wall)),
        (4, (AptIn0, PartyX, FrontSouth, -1.5f), (AptIn0, PartyX, AptS0, FrontSouth - Wall)),
    };

    // A flat's door on the corridor, its middle along x and the corridor side's z (for its vent, over the door).
    private static (float x, float z) FlatDoor(int n) => n switch
    {
        1 => (92.6f, 1.15f),
        2 => (97.6f, 1.15f),
        3 => (98f, -1.15f),
        _ => (91.6f, -1.15f)
    };

    private static Vector3 Middle((float x0, float x1, float z0, float z1) room, float y)
        => new Vector3((room.x0 + room.x1) * .5f, y, (room.z0 + room.z1) * .5f);

    // ---- The lift ----

    // Its car on every floor behind the corridor's doors (GimmickElevator), powered by the generator on the open roof
    // (made here, its lamps the cars'); it stands at the top floor as the map is built.
    // The generator (the user, 2026-11-18: "옥상 엘리베이터 작동용 발전기 배치"): the lift's power used to be a breaker on
    // the far wall of the roof's machine room, and a 4 m light-tower generator stood in that 3 m room as dressing, across
    // its whole depth and 0.2 m behind its door, so nobody could reach the breaker and the lift never ran. Now the
    // generator is the power: it stands on the open roof beside the machine room, and [F] at its side starts it.
    private static void AptLift(GreyboxBuilder b, System.Func<int, Rect3> floorsBox)
    {
        // At the world's origin: its doors are boxes under it, laid in its own space (GimmickElevator works out where [F] is).
        var lift = b.Switch<GimmickElevator>("B4 승강기", Vector3.zero);
        // Lengthways along the machine room's west wall, a walkway's width off it; the build measures the model and
        // says where it stood (LastStoodAt), and the handle is on its side facing the machine room's door.
        const float GeneratorYaw = 90f;
        Vector3 asked = new Vector3(97.6f, AptRoof, -3f);
        Vector3 stands = b.PropSolid("generator", asked, GeneratorYaw) != null ? b.LastStoodAt : asked;
        var power = b.Switch<GimmickBreaker>("B4 옥상 발전기", stands + new Vector3(b.ArtSize("generator").z * .5f, 1.1f, 0f), GeneratorYaw);
        var doors = new List<Transform>();
        var calls = new List<Transform>();
        var lamps = new List<Light>();
        var wakes = new List<Bounds>();
        var floors = new List<float>();
        for (int f = 0; f < AptStoreys; f++)
        {
            float y = f * Storey;
            floors.Add(y);
            doors.Add(b.Blocker($"B4 {f + 1}층 승강기 문", 101.55f, 102.75f, y, y + 2.2f, -1.45f, -1.25f, lift.transform, b.Metal, "Metal").transform);
            calls.Add(b.Mark($"B4 {f + 1}층 승강기 버튼", new Vector3(101.2f, y + 1.2f, -1.05f), 0f, lift.transform));
            b.Box($"B4 {f + 1}층 승강기 버튼판", 101.1f, 101.3f, y + 1.05f, y + 1.35f, -1.2f, -1.17f, b.Marker, solid: false);
            lamps.Add(b.CircuitLamp(power, new Vector3(102.15f, y + 2.45f, -2.9f), 4f, 1.4f));
            wakes.Add(floorsBox(f).Bounds);
        }
        var car = new Rect3(ShaftX + .1f, AptIn1 - .1f, .05f, 2.6f, ShaftZ + .1f, -1.6f).Bounds;
        GreyboxBuilder.Configure(lift, so =>
        {
            so.FindProperty("circuit").stringValue = "Ch1.Lift";
            so.FindProperty("car").boundsValue = car;
            so.FindProperty("doorOpen").vector3Value = new Vector3(1.25f, 0f, 0f);
            so.FindProperty("startFloor").intValue = AptStoreys - 1;
            so.FindProperty("reach").floatValue = 1.7f;
            Fill(so.FindProperty("floors"), floors);
            FillObjects(so.FindProperty("doors"), doors);
            FillObjects(so.FindProperty("calls"), calls);
            Fill(so.FindProperty("wakeBoxes"), wakes);
        });
        GreyboxBuilder.Configure(power, so =>
        {
            so.FindProperty("circuit").stringValue = "Ch1.Lift";
            so.FindProperty("offerUp").stringValue = "gimmick.generator.start";
            so.FindProperty("offerDown").stringValue = "gimmick.generator.stop";
            so.FindProperty("engine").boolValue = true;
            FillObjects(so.FindProperty("lamps"), lamps);
        });
    }

    private static void Fill(UnityEditor.SerializedProperty array, List<float> values)
    {
        array.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).floatValue = values[i];
    }

    private static void Fill(UnityEditor.SerializedProperty array, List<Bounds> values)
    {
        array.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).boundsValue = values[i];
    }

    private static void Fill(UnityEditor.SerializedProperty array, List<string> values)
    {
        array.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).stringValue = values[i];
    }

    private static void FillObjects<T>(UnityEditor.SerializedProperty array, List<T> values) where T : Object
    {
        array.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    // The roof: the machine room over the lift (its fuse box on the east wall is only seen; the lift's power is the
    // generator outside its door, AptLift), water tanks.
    private static void AptRoofTop(GreyboxBuilder b)
    {
        const float X0 = ShaftX - Wall, Z0 = ShaftZ - Wall, Z1 = -1.2f, Top = AptRoof + 2.7f;
        b.Box("B4 기계실 서벽 1", X0, ShaftX, AptRoof, Top, Z0, -3.6f);
        b.Box("B4 기계실 서벽 2", X0, ShaftX, AptRoof, Top, -2.4f, Z1);
        b.Box("B4 기계실 문 위", X0, ShaftX, AptRoof + 2.2f, Top, -3.6f, -2.4f);
        b.Box("B4 기계실 북벽", X0, AptX1, AptRoof, Top, -1.5f, Z1);
        b.Box("B4 기계실 남벽", X0, AptX1, AptRoof, Top, Z0, ShaftZ);
        b.Box("B4 기계실 동벽", AptIn1, AptX1, AptRoof, Top, ShaftZ, -1.5f);
        b.Box("B4 기계실 지붕", X0, AptX1, Top, Top + Wall, Z0, Z1);
        b.Mount("fusebox", new Vector3(AptIn1, AptRoof + 1.3f, -2.9f), 270f);
        b.Spot("B4Roof", new Vector3(95f, AptRoof, -6f));
        b.PropSolid("watertank", new Vector3(91f, AptRoof, -11.5f), 30f);
        b.PropSolid("watertank", new Vector3(91f, AptRoof, 12f), 300f);
        foreach (var x in new[] { 96.5f, 98.7f }) b.PropSolid("acunit", new Vector3(x, AptRoof, 14.6f), 180f);
    }

    // ---- What changes from run to run (GimmickVariant) ----

    private static void AptVariants(GreyboxBuilder b)
    {
        // One flight of the stairs has fallen in each run (between the 5th and 4th, 4th and 3rd, or 3rd and 2nd floor):
        // there the way down is through a flat's floor, a fire ladder or the lift.
        var flights = b.Variant("B4 무너진 계단 (판마다 한 곳)", 1, 101);
        for (int f = 4; f >= 2; f--)
        {
            float top = f * Storey, under = top - Storey;
            var choice = b.Choice(flights, $"{f + 1}층→{f}층 계단 무너짐");
            b.Blocker($"B4 계단 잔해 {f + 1}층→{f}층", CoreX + .05f, AptIn1 - .05f, under + .05f, top - .35f, 1.5f + Landing + .05f, CoreZ1 - .05f,
                choice, b.Concrete, "Concrete");
        }
        // Of the two holes through each of the 5th, 4th and 3rd floors one is open a run, the other filled.
        for (int f = 4; f >= 2; f--)
        {
            var holes = FloorHoles(f);
            var group = b.Variant($"B4 {f + 1}층 바닥 구멍 (둘 중 하나 막힘)", 1, 110 + f);
            for (int i = 0; i < holes.Length; i++)
            {
                var (x0, x1, z0, z1) = holes[i];
                b.Blocker($"B4 {f + 1}층 바닥 메움 {i + 1}", x0, x1, f * Storey - Wall, f * Storey, z0, z1, b.Choice(group, "메움 " + (i + 1)), b.Concrete, "Tile",
                    carves: false);
            }
        }
        // Of each pair of holes through the party walls one is open a run (front room or back room), north and south.
        for (int f = 0; f < AptStoreys; f++)
        {
            float y = f * Storey;
            foreach (bool north in new[] { true, false })
            {
                var group = b.Variant($"B4 {f + 1}층 {(north ? "북" : "남")}쪽 벽 구멍 (둘 중 하나 막힘)", 1, 130 + f * 2 + (north ? 0 : 1));
                var spans = north ? new[] { (3f, 4.2f), (12f, 13.2f) } : new[] { (-4.2f, -3f), (-13.2f, -12f) };
                for (int i = 0; i < spans.Length; i++)
                    b.Blocker($"B4 {f + 1}층 벽 구멍 메움 {i + 1}", PartyX, PartyX + Wall, y, y + 2.2f, spans[i].Item1, spans[i].Item2,
                        b.Choice(group, "메움 " + (i + 1)));
            }
        }
    }

    // ---- The gimmicks ----

    private static void AptGimmicks(GreyboxBuilder b, System.Func<int, Rect3> floorsBox)
    {
        // The caretaker's flat, 503: the log on the desk, and the can of disinfectant gas by the back room's window.
        b.PropSolid("hubdesk", new Vector3(101.6f, 12f, -13.9f), 180f);
        b.Prop("desk", new Vector3(101.2f, 12.78f, -13.9f), 180f, 0);
        var log = b.Switch<GimmickClue>("B4 관리 일지", new Vector3(101.6f, 12.9f, -13.6f));
        GreyboxBuilder.Configure(log, so =>
        {
            so.FindProperty("item").stringValue = "masterkey";
            so.FindProperty("line").stringValue = "gimmick.clue.ch1.log";
            so.FindProperty("prompt").stringValue = "gimmick.clue.ch1.prompt";
            so.FindProperty("reach").floatValue = 1.6f;
        });
        b.Spot("B4Office", new Vector3(100f, 12f, -12f));
        var gas = b.Switch<GimmickItem>("B4 소독 가스통", new Vector3(103f, 12f, -9.6f));
        var can = b.Prop("propane", new Vector3(103f, 12f, -9.6f), 20f);
        if (can != null) can.transform.SetParent(gas.transform, true);
        GreyboxBuilder.Configure(gas, so =>
        {
            so.FindProperty("id").stringValue = "gas";
            so.FindProperty("title").stringValue = "gimmick.item.gas";
        });

        // The master key: in one of six flats on the 2nd to 4th floor a run, said plainly by the log (GimmickItem).
        var spots = new (string place, Vector3 at)[]
        {
            ("gimmick.place.401", new Vector3(89.6f, 9.78f, 14.2f)),
            ("gimmick.place.402", new Vector3(97.6f, 9.78f, 6.4f)),
            ("gimmick.place.303", new Vector3(99.6f, 6.78f, -13.6f)),
            ("gimmick.place.304", new Vector3(88.6f, 6.78f, -5.2f)),
            ("gimmick.place.201", new Vector3(93.6f, 3.78f, 5.2f)),
            ("gimmick.place.203", new Vector3(101.6f, 3.78f, -14.4f)),
        };
        var key = b.Switch<GimmickItem>("B4 마스터키", spots[0].at);
        var marks = new List<Transform>();
        var places = new List<string>();
        foreach (var (place, at) in spots)
        {
            // Each on a table (blocking), the key on it.
            b.PropSolid("table", at - Vector3.up * .78f, 0f);
            marks.Add(b.Mark("마스터키 자리 " + place.Substring(place.Length - 3), at, 0f, key.transform.parent));
            places.Add(place);
        }
        b.Box("열쇠 꾸러미", -.09f, .09f, 0f, .04f, -.05f, .05f, b.Metal, solid: false, group: key.transform);
        var shine = new GameObject("열쇠 빛", typeof(Light)).GetComponent<Light>();
        shine.transform.SetParent(key.transform, false);
        shine.transform.localPosition = Vector3.up * .3f;
        shine.type = LightType.Point;
        shine.range = 1.6f;
        shine.intensity = .7f;
        shine.color = new Color(1f, .82f, .5f);
        shine.shadows = LightShadows.None;
        shine.lightmapBakeType = LightmapBakeType.Realtime;
        GreyboxBuilder.Configure(key, so =>
        {
            so.FindProperty("id").stringValue = "masterkey";
            so.FindProperty("title").stringValue = "gimmick.item.masterkey";
            so.FindProperty("salt").intValue = 7;
            FillObjects(so.FindProperty("spots"), marks);
            Fill(so.FindProperty("places"), places);
        });

        // The ground floor's stair door, chained: the master key opens it quietly; smashed in, the ground floor wakes.
        var chained = b.Blocker("B4 1층 계단 문 (쇠사슬, 마스터키)", 101.9f, 103.5f, 0f, DoorHigh, 1.25f, 1.45f, null, b.Metal, "Metal");
        var door = b.AddSwitch<GimmickLock>(chained);
        GreyboxBuilder.Configure(door, so =>
        {
            so.FindProperty("item").stringValue = "masterkey";
            so.FindProperty("itemTitle").stringValue = "gimmick.item.masterkey";
            so.FindProperty("wakeBox").boundsValue = floorsBox(0).Bounds;
            so.FindProperty("openOffset").vector3Value = new Vector3(0f, 0f, 1.1f);
            so.FindProperty("reach").floatValue = 1.8f;
        });

        // Each flat of the 2nd to 5th floor has its vent over its door: the gas fills that flat.
        for (int f = 1; f <= 4; f++)
        {
            float y = f * Storey;
            foreach (var flat in Flats)
            {
                var (x, z) = FlatDoor(flat.n);
                var vent = b.Switch<GimmickVent>($"B4 {f + 1}0{flat.n}호 환기구", new Vector3(x, y + 2.35f, z));
                b.Box($"B4 {f + 1}0{flat.n}호 환기구 덮개", x - .3f, x + .3f, y + 2.2f, y + 2.5f, z > 0f ? 1.15f : -1.2f, z > 0f ? 1.2f : -1.15f, b.Metal, solid: false);
                var room = new Rect3(Mathf.Min(flat.front.Item1, flat.back.Item1), Mathf.Max(flat.front.Item2, flat.back.Item2), y - .2f, y + AptCeiling,
                    Mathf.Min(flat.front.Item3, flat.back.Item3), Mathf.Max(flat.front.Item4, flat.back.Item4));
                GreyboxBuilder.Configure(vent, so =>
                {
                    so.FindProperty("item").stringValue = "gas";
                    so.FindProperty("itemTitle").stringValue = "gimmick.item.gas";
                    so.FindProperty("room").boundsValue = room.Bounds;
                    so.FindProperty("reach").floatValue = 1.6f;
                });
            }
        }

        // A TV in a flat on each floor from the 2nd to the 5th, and in the ground floor's lounge: switched on it blares and
        // the corridor's walkers and those standing near go to it (GimmickLure).
        var tvs = new (int f, int n)[] { (1, 2), (2, 1), (3, 4), (4, 2), (0, 1) };
        foreach (var (f, n) in tvs)
        {
            var flat = System.Array.Find(Flats, each => each.n == n);
            Vector3 at = Middle(flat.front, f * Storey) + new Vector3(0f, 0f, flat.n <= 2 ? 2.2f : -2.2f);
            float yaw = flat.n <= 2 ? 180f : 0f;
            var tv = b.Switch<GimmickLure>($"B4 {f + 1}0{n}호 TV", at, yaw);
            var model = b.Prop("oldtv", at, yaw);
            if (model != null) model.transform.SetParent(tv.transform, true);
            var glow = new GameObject("화면 빛", typeof(Light)).GetComponent<Light>();
            glow.transform.SetParent(tv.transform, false);
            glow.transform.localPosition = new Vector3(0f, .5f, .5f);
            glow.type = LightType.Point;
            glow.range = 4f;
            glow.intensity = 1.1f;
            glow.color = new Color(.62f, .78f, 1f);
            glow.shadows = LightShadows.None;
            glow.lightmapBakeType = LightmapBakeType.Realtime;
            glow.enabled = false;
            GreyboxBuilder.Configure(tv, so =>
            {
                so.FindProperty("glow").objectReferenceValue = glow;
                so.FindProperty("prompt").stringValue = "gimmick.lure.tv";
                so.FindProperty("reach").floatValue = 1.8f;
            });
        }

        // The cellar's switchboard: the alley's shutter is on it (its step's button, Ch1.05), and the block's lights come
        // on with it, so whatever still stands inside sees far.
        b.Mount("fusebox", new Vector3(AptIn0, -1.6f, -13.5f), 90f);
        b.Mount("switchlever", new Vector3(AptIn0, -1.7f, -12.6f), 90f);
        b.Spot("Basement", new Vector3(91.3f, -3f, -9f));
        b.Spot("Breaker", new Vector3(88.2f, -3f, -13f));
        var mains = b.Switch<GimmickBreaker>("B4 지하 배전반", new Vector3(AptIn0 + .2f, -1.6f, -13f), 90f);
        var lit = new List<Light>();
        for (int f = 0; f < AptStoreys; f++)
        {
            float y = f * Storey + 2.4f;
            lit.Add(b.CircuitLamp(mains, new Vector3(90f, y, 0f), 9f, 1.5f));
            lit.Add(b.CircuitLamp(mains, new Vector3(98f, y, 0f), 9f, 1.5f));
            foreach (var flat in Flats) lit.Add(b.CircuitLamp(mains, Middle(flat.front, y), 6f, 1.1f));
        }
        lit.Add(b.CircuitLamp(mains, new Vector3(91f, -.7f, -8.5f), 9f, 1.6f));
        GreyboxBuilder.Configure(mains, so =>
        {
            so.FindProperty("circuit").stringValue = "Ch1.Mains";
            so.FindProperty("step").stringValue = "Ch1.05";
            FillObjects(so.FindProperty("lamps"), lit);
        });
    }

    // ---- Who is where ----

    // Sleeping crowds, a run's pick of flats on each floor, and a walker on every corridor (two on the ground floor).
    private static void AptCrowds(GreyboxBuilder b)
    {
        const string Step = "Ch1.13";
        for (int f = 1; f < AptStoreys; f++)
        {
            float y = f * Storey;
            // The landing flat (501) and the caretaker's (503) are kept clear: the first steps in are quiet.
            var group = b.Variant($"B4 {f + 1}층 잠든 무리 (판마다 두 집)", f == 5 ? 1 : 2, 150 + f);
            foreach (var flat in Flats)
            {
                if (f == 4 && (flat.n == 1 || flat.n == 3)) continue;
                if (f == 5 && flat.n == 1) continue;
                var choice = b.Choice(group, $"{f + 1}0{flat.n}호");
                b.Sleepers(Step, Middle(flat.back, y), flat.n == 2 ? 2 : 3, 1.8f, choice);
            }
            b.Walkers(Step, 1, null, new Vector3(88.6f, y, 0f), new Vector3(98.6f, y, 0f));
        }
        var ground = b.Variant("B4 1층 잠든 무리 (셋 중 둘)", 2, 160);
        foreach (var n in new[] { 1, 2, 3 })
        {
            var flat = System.Array.Find(Flats, each => each.n == n);
            b.Sleepers(Step, Middle(flat.back, 0f), 3, 1.8f, b.Choice(ground, $"10{n}호"));
        }
        b.Walkers(Step, 2, null, new Vector3(88.6f, 0f, 0f), new Vector3(98.6f, 0f, 0f), new Vector3(91.6f, 0f, -4.5f));
        b.Sleepers("Ch1.14", new Vector3(92.5f, -3f, -5f), 2, 1.2f);
    }

    // ---- Furniture ----

    // Each flat: a sofa facing the TV wall and a table in the front room, a shelf, a fridge or lockers and boxes in the back
    // room; a few lamps the survivors left lit on the corridors; litter.
    // The furniture is named with its room and wall, and the build stands it (GreyboxBuilder.Furnish): it measures the
    // model, slides it along that wall clear of the doorways, the holes and whatever stands there already (a master
    // key's table), and refuses what the room cannot hold. It was stood at fixed points 0.5 m off a corner, whatever
    // the model's size: the "fridge" was the airport's four-door shop chiller, 4 m wide, and it and the 2 m wire shelves
    // stood through the outer and the party walls (35 of the block's props in the build of 11-16; the user, 2026-11-18:
    // "망가진 맵 오브젝트 배치 복구").
    private static void AptDressing(GreyboxBuilder b)
    {
        for (int f = 0; f < AptStoreys; f++)
        {
            float y = f * Storey;
            foreach (var flat in Flats)
            {
                if (f == 0 && flat.n == 4) continue;
                bool north = flat.n <= 2;
                float face = north ? 0f : 180f;
                var front = flat.front;
                var back = flat.back;
                var outer = north ? Side.North : Side.South;
                // The sofa on the front room's west wall, facing in (the fallen corner has none: they land there).
                if (!(f >= 4 && flat.n == 1)) b.Furnish("couch", RoomOf(front, y), Side.West, .5f, flat.n + f);
                b.Prop("chair", new Vector3(front.Item2 - 1.2f, y, (front.Item3 + front.Item4) * .5f), face + 30f * flat.n, f);
                // On the back room's outer wall: a shelf at its east end, a fridge or a locker at its west end.
                b.Furnish("wireshelf", RoomOf(back, y), outer, 1f);
                b.Furnish(f % 2 == 0 ? "homefridge" : "locker", RoomOf(back, y), outer, 0f);
                foreach (var (dx, dz) in new[] { (1.6f, 1.3f), (2.4f, 1.9f) })
                    b.Prop("cardboard", new Vector3(back.Item1 + dx, y, north ? back.Item4 - dz : back.Item3 + dz), dx * 37f + f * 11f);
                b.Prop(flat.n % 2 == 0 ? "trashbag" : "litter", new Vector3(front.Item2 - .6f, y, north ? front.Item3 + .5f : front.Item4 - .5f), f * 23f);
            }
            // Litter along the corridor and a lamp the survivors left at each end.
            foreach (var x in new[] { 89.5f, 94.5f, 100.2f }) b.Prop("litter", new Vector3(x, y, (x * 7f % 2f) - 1f), x * 13f);
            if (f != 4) b.Lamp(new Vector3(88.2f, y + 2.3f, 0f), 6f, .7f);
        }
        // The fallen corner on the 5th floor: rubble where they land.
        foreach (var (x, z) in new[] { (88.3f, 3.6f), (89.4f, 8.6f), (88.6f, 6.4f) }) b.Prop("brickdebris", new Vector3(x, 12f, z), x * 19f);
        b.Prop("rubble", new Vector3(89.6f, 12f, 4.8f), 40f);
        // The front entrance, boarded up from inside: a sofa pushed across the corridor's west end.
        b.Prop("planks", new Vector3(87.7f, 0f, -.4f), 90f);
        b.Furnish("couch", new Rect3(AptIn0, CoreX - Wall, 0f, AptCeiling, -1.2f, 1.2f).Bounds, Side.West);
        // The cellar: a store shelf, a water tank and drums along its walls. (A generator stood here as dressing: the
        // light tower, 4.2 m tall in a 2.7 m cellar, came up 1.2 m through flat 104's floor. The block's one generator
        // is the lift's, on the roof.)
        var cellar = new Rect3(AptIn0, PartyX, -3f, -Wall, AptS0, -1.5f).Bounds;
        b.Furnish("wireshelf", cellar, Side.South, 1f);
        b.Furnish("watertankportable", cellar, Side.East, .25f);
        foreach (var (x, z) in new[] { (94.4f, -4.2f), (94.5f, -5.1f) }) b.PropSolid("plasticdrum", new Vector3(x, -3f, z));
        b.Lamp(new Vector3(91f, -.8f, -3.5f), 5f, .5f);
        // The building's own sound, cellar to roof.
        b.Ambience("Ch1.Flats", AptIn0, AptIn1, -3f, AptRoof, AptS0, AptN1);
        // Supplies: a gun in 603 and 102, supplies on the way down.
        b.Pickup(new Vector3(100f, 15f, -12f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(99.4f, 0f, 13f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(102f, 12f, -11f));
        b.Pickup(new Vector3(90f, 9f, 12.5f));
        b.Pickup(new Vector3(98f, 6f, -12.5f));
        b.Pickup(new Vector3(90f, 3f, -12.5f));
        b.Pickup(new Vector3(93f, -3f, -10f));
    }
}
