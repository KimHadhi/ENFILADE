using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// CampaignMaps: chapter 4's gimmicks outside the terminal (the airside, x measured with its Airside shift as in
// CampaignMaps.Airport): the jet bridge that reaches the airliner at its gate, the three stands whose one fueled airliner
// the clues name, the fuel truck's spreading fire in the tug's hold, the transport's cargo door with its lever and wedge,
// and the supplies to load for greed. One part of the class; the rest is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    // The pick of the fueled stand: one roll of the run's seed, shared by the valves and the clues.
    private const int FuelSalt = 270;

    private static void AirsideGimmicks(GreyboxBuilder b, float x, CampaignBlast truckBlast, GameObject truck)
    {
        var valves = new List<GimmickFuelValve>();
        JetBridge(b, x, valves);
        Stands(b, x, valves);
        FuelClues(b, x, valves);
        FuelTruck(b, x, truckBlast, truck);
        CargoDoor(b, x, out var door);
        CargoLoad(b, x, door);
    }

    // The gate's jet bridge reaches the airliner on stand 1 (the card "탑승교 운전"): its cab is where the bridge already
    // ends over the apron (x 196-199 + Airside), and an 8 m telescoping end reaches from the cab's east opening to the
    // airliner's front door while anyone stands in the cab (GimmickJetBridge). Docked, the door opens on the cabin: along
    // its aisle to the rear door on its far side and the slide down to the apron, by the stands and the tower. The
    // service stairs stay; a crowd sleeps at their foot (the quiet way is through the plane).
    // The end is telescoping lengths that slide out of one another, as a real bridge's tunnels do, on a track made here
    // (SetPieceTrack; the user, 2026-11-18: "실시간 연산 대신 베이크된(Baked) 애니메이션으로 교체"). It was one holder
    // whose scale along x was written every frame, resizing its four colliders. Each length is rigid and sits on a
    // kinematic body; the first stays at the cab and each one after it travels one more length less the overlap, so
    // together they reach the door. Drawn in, they stand as one length off the cab.
    private static void JetBridge(GreyboxBuilder b, float x, List<GimmickFuelValve> valves)
    {
        const float Floor = 4f, Cab = 199f, Reach = 8f;
        // How many lengths, how far two that are drawn out still overlap (m), and how much smaller each is than the one
        // it slides out of: narrower on each side, its floor a little higher and its roof a little lower (m).
        const int Lengths = 4;
        const float Overlap = .3f, NestSide = .06f, NestRise = .03f, Skin = .12f;
        float length = (Reach + (Lengths - 1) * Overlap) / Lengths;
        var bridge = b.Switch<GimmickJetBridge>("탑승교 (조종석에 서 있으면 늘어남)", Vector3.zero);
        var parts = new List<Transform>();
        var travel = new List<Vector3>();
        for (int k = 0; k < Lengths; k++)
        {
            var holder = new GameObject("탑승교 마디 " + (k + 1));
            holder.transform.SetParent(bridge.transform, false);
            holder.transform.position = new Vector3(Cab + x, 0f, 0f);
            float half = 1.6f - k * NestSide, floor = Floor + k * NestRise, roof = Floor + 2.6f - k * NestRise;
            b.Box("마디 바닥", 0f, length, floor - .4f, floor, -half, half, b.Metal, "Gantry", group: holder.transform);
            b.Box("마디 벽 남", 0f, length, floor, roof, -half - Skin, -half, b.Metal, "Metal", group: holder.transform);
            b.Box("마디 벽 북", 0f, length, floor, roof, half, half + Skin, b.Metal, "Metal", group: holder.transform);
            b.Box("마디 지붕", 0f, length, roof, roof + Skin, -half - Skin, half + Skin, b.Metal, group: holder.transform);
            // The first length is the cab's own end and stays; the rest are the moving parts.
            if (k == 0) continue;
            parts.Add(b.MovingPart(holder));
            travel.Add(new Vector3(k * (length - Overlap), 0f, 0f));
        }

        // Stand 1's airliner, nose north, its cabin floor level with the bridge's, the front door facing the bridge.
        float w0 = Cab + Reach + x, w1 = w0 + 4f, middle = (w0 + w1) * .5f;
        var door = b.Blocker("1번 계류장 여객기 앞문", w0 - .05f, w0 + .2f, Floor, Floor + 2.2f, -1.2f, 1.2f, bridge.transform, b.Metal, "Metal");
        b.Box("1번 여객기 배", w0, w1, 2f, Floor, -14f, 10f, b.Metal, "Metal");
        MetalWallZ(b, "1번 여객기 서벽", w0, w0 + .2f, -14f, 10f, Floor, Floor + 2.4f, -1.2f, 1.2f, Floor + 2.2f);
        MetalWallZ(b, "1번 여객기 동벽", w1 - .2f, w1, -14f, 10f, Floor, Floor + 2.4f, -12f, -10.6f, Floor + 2.2f);
        b.Box("1번 여객기 지붕", w0, w1, Floor + 2.4f, Floor + 2.7f, -14f, 10f, b.Metal, "Metal");
        b.Box("1번 여객기 앞벽", w0, w1, Floor, Floor + 2.4f, 9.8f, 10f, b.Metal, "Metal");
        b.Box("1번 여객기 뒷벽", w0, w1, Floor, Floor + 2.4f, -14f, -13.8f, b.Metal, "Metal");
        b.Box("1번 여객기 기수", w0 + .5f, w1 - .5f, 2.4f, 6.2f, 10f, 12.5f, b.Metal, "Metal");
        b.Box("1번 여객기 꼬리", w0 + 1f, w1 - 1f, 3f, 6.4f, -18f, -14f, b.Metal, "Metal");
        b.Box("1번 여객기 수직 꼬리", middle - .15f, middle + .15f, 6.4f, 11f, -18f, -14.5f, b.Metal, solid: false);
        Airliner(b, "1번 여객기", middle, 0f, 8.5f);
        // Its cabin: rows of seats on either side of a 1 m aisle, clear by the two doors.
        for (float z = -9.6f; z <= 8.4f; z += 1.2f)
        {
            if (z > -2f && z < 2f) continue;
            b.Box("1번 여객기 좌석", w0 + .2f, middle - .5f, Floor, Floor + .9f, z - .35f, z + .35f, b.Wood, "Carpet");
            b.Box("1번 여객기 좌석", middle + .5f, w1 - .2f, Floor, Floor + .9f, z - .35f, z + .35f, b.Wood, "Carpet");
        }
        b.Lamp(new Vector3(middle, Floor + 2.1f, -2f), 7f, .5f);
        b.Ramp("1번 여객기 비상 미끄럼틀", new Vector3(w1 + 8f, 0f, -11.3f), new Vector3(w1, Floor, -11.3f), 1.6f, b.Metal);
        GreyboxBuilder.Configure(bridge, so =>
        {
            so.FindProperty("cab").boundsValue = new Bounds(new Vector3(197.5f + x, Floor + .9f, 0f), new Vector3(2.8f, 1.8f, 4.4f));
            var moving = so.FindProperty("track.parts");
            var moves = so.FindProperty("track.travel");
            moving.arraySize = moves.arraySize = parts.Count;
            for (int i = 0; i < parts.Count; i++)
            {
                moving.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
                moves.GetArrayElementAtIndex(i).vector3Value = travel[i];
            }
            so.FindProperty("seconds").floatValue = 10f;
            so.FindProperty("door").objectReferenceValue = door;
            so.FindProperty("reach").floatValue = 2.4f;
        });
        valves.Add(Valve(b, "1번 계류장 급유 밸브", new Vector3(w1 + 2.6f, 0f, 4f), 0));
        // A crowd asleep at the service stairs' foot, under the bridge.
        b.Sleepers("Ch4.06", new Vector3(197.5f + x, 0f, -16.5f), 3, 1.4f);
    }

    // Stands 2 and 3 on the apron north of the bridge: an airliner each, nose north (the card "기름 찬 비행기 찾기").
    private static void Stands(GreyboxBuilder b, float x, List<GimmickFuelValve> valves)
    {
        foreach (var (name, middle, stand) in new[] { ("2번", 221.4f + x, 1), ("3번", 245.4f + x, 2) })
        {
            b.Box(name + " 여객기 동체", middle - 2f, middle + 2f, 2f, 6.7f, 22f, 46f, b.Metal, "Metal");
            b.Box(name + " 여객기 기수", middle - 1.5f, middle + 1.5f, 2.4f, 6.2f, 46f, 48.5f, b.Metal, "Metal");
            b.Box(name + " 여객기 꼬리", middle - 1f, middle + 1f, 3f, 6.4f, 18f, 22f, b.Metal, "Metal");
            b.Box(name + " 여객기 수직 꼬리", middle - .15f, middle + .15f, 6.4f, 11f, 18f, 21.5f, b.Metal, solid: false);
            Airliner(b, name + " 여객기", middle, 33f, 44f);
            valves.Add(Valve(b, name + " 계류장 급유 밸브", new Vector3(middle + 4.5f, 0f, 30f), stand));
        }
    }

    // An airliner's wings, engines and gear about its middle line (nose north): wings and engines at wingZ, the nose
    // gear at noseZ. The wings are overhead and only seen; the engines and the gear stand in the way.
    private static void Airliner(GreyboxBuilder b, string name, float middle, float wingZ, float noseZ)
    {
        b.Box(name + " 날개", middle - 14f, middle + 14f, 2.4f, 2.8f, wingZ - 3f, wingZ + 3f, b.Metal, solid: false);
        foreach (float side in new[] { -1f, 1f })
        {
            float engine = middle + side * 7.5f;
            b.Box(name + " 엔진", engine - 1.5f, engine + 1.5f, 1f, 2.4f, wingZ - 2f, wingZ + 2f, b.Metal, "Metal");
            float wheel = middle + side * 1.3f;
            b.Carve(b.Box(name + " 뒷바퀴", wheel - .3f, wheel + .3f, 0f, 2f, wingZ - .3f, wingZ + .3f, b.Metal, "MetalPipe"));
        }
        b.Carve(b.Box(name + " 앞바퀴", middle - .3f, middle + .3f, 0f, 2f, noseZ - .3f, noseZ + .3f, b.Metal, "MetalPipe"));
    }

    // A metal wall along z (x0..x1 thick) with one opening (along z holeZ0..holeZ1, from its foot up to holeTop).
    private static void MetalWallZ(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float y0, float y1,
        float holeZ0, float holeZ1, float holeTop)
    {
        b.Box(name, x0, x1, y0, y1, z0, holeZ0, b.Metal, "Metal");
        b.Box(name, x0, x1, y0, y1, holeZ1, z1, b.Metal, "Metal");
        b.Box(name, x0, x1, holeTop, y1, holeZ0, holeZ1, b.Metal, "Metal");
    }

    // A stand's fuel valve: a hydrant box in the apron with its handwheel (GimmickFuelValve, finishing Ch4.14).
    private static GimmickFuelValve Valve(GreyboxBuilder b, string name, Vector3 at, int stand)
    {
        b.Box(name + " 함", at.x - .35f, at.x + .35f, 0f, .5f, at.z - .3f, at.z + .3f, b.Metal, "MetalBox");
        b.Box(name + " 손잡이", at.x - .18f, at.x + .18f, .5f, .56f, at.z - .18f, at.z + .18f, b.Marker, solid: false);
        var valve = b.Switch<GimmickFuelValve>(name, at + Vector3.up * .55f);
        GreyboxBuilder.Configure(valve, so =>
        {
            so.FindProperty("stand").intValue = stand;
            so.FindProperty("stands").intValue = 3;
            so.FindProperty("salt").intValue = FuelSalt;
            so.FindProperty("step").stringValue = "Ch4.14";
            so.FindProperty("place").stringValue = "gimmick.place.ch4.stand" + (stand + 1);
            so.FindProperty("reach").floatValue = 1.8f;
        });
        return valve;
    }

    // The three clues to the fueled stand: the departure board in the south-east gallery (the pack's flap boards), the
    // pilot's notebook in the gate room, the tower's terminal by its radio. Each says it plainly and points the step's
    // guide at the valve (spot FuelValve, at the apron's middle until a clue is read).
    private static void FuelClues(GreyboxBuilder b, float x, List<GimmickFuelValve> valves)
    {
        b.Spot("FuelValve", new Vector3(222f + x, 0f, 14f));
        b.Box("조종사 가방", 257.35f, 257.85f, 4f, 4.35f, -1.2f, -.8f, b.Wood, "Tarp", solid: false);
        b.Box("조종사 수첩", 257.45f, 257.75f, 4.35f, 4.38f, -1.1f, -.9f, b.Marker, solid: false);
        foreach (var (name, at, prompt, line, reach) in new[]
        {
            ("출발 안내판", new Vector3(218.55f, 5.3f, 16.71f), "gimmick.fuelclue.board.prompt", "gimmick.fuelclue.board", 3.5f),
            ("조종사 수첩", new Vector3(257.6f, 4.4f, -1f), "gimmick.fuelclue.note.prompt", "gimmick.fuelclue.note", 1.6f),
            ("관제 단말", new Vector3(217.5f + x, 1.1f, -50.6f), "gimmick.fuelclue.tower.prompt", "gimmick.fuelclue.tower", 1.8f),
        })
        {
            var clue = b.Switch<GimmickFuelClue>(name, at);
            GreyboxBuilder.Configure(clue, so =>
            {
                so.FindProperty("prompt").stringValue = prompt;
                so.FindProperty("line").stringValue = line;
                so.FindProperty("spot").stringValue = "FuelValve";
                FillObjects(so.FindProperty("valves"), valves);
                so.FindProperty("reach").floatValue = reach;
            });
        }
    }

    // The fuel truck (the card "번지는 연료 불"): a shot or a blast sets it off; else it goes up 35 s into the tug's hold.
    // Its fuel spreads into a lane of fire south of it across the apron (x 304-340, z 2-9), 40 s, carving the walk mesh
    // so the horde from the north goes round it. Its old set piece is fired from here now (its step left empty).
    private static void FuelTruck(GreyboxBuilder b, float x, CampaignBlast blast, GameObject truck)
    {
        var bounds = truck.GetComponent<Renderer>().bounds;
        var gimmick = b.Switch<GimmickFuelTruck>("급유차 (쏘면 터짐)", bounds.center);
        var target = b.Box("급유차 맞는 곳", bounds.min.x - .2f, bounds.max.x + .2f, 0f, bounds.max.y + .2f, bounds.min.z - .2f, bounds.max.z + .2f,
            null, solid: false, group: null);
        target.transform.SetParent(gimmick.transform, true);
        target.GetComponent<Renderer>().enabled = false;
        target.AddComponent<GimmickTarget>();
        var lane = new Bounds(new Vector3(235.4f + x, 1.25f, 5.5f), new Vector3(36f, 2.5f, 7f));
        var block = new GameObject("불길 막힘", typeof(NavMeshObstacle)).GetComponent<NavMeshObstacle>();
        block.transform.SetParent(gimmick.transform, false);
        block.transform.position = lane.center;
        block.shape = NavMeshObstacleShape.Box;
        block.center = Vector3.zero;
        block.size = lane.size;
        block.carving = true;
        block.carveOnlyStationary = false;
        // Off until the lane burns (GimmickFuelTruck).
        block.enabled = false;
        GreyboxBuilder.Configure(gimmick, so =>
        {
            so.FindProperty("step").stringValue = "Ch4.09";
            so.FindProperty("goOff").floatValue = 35f;
            so.FindProperty("blast").objectReferenceValue = blast;
            so.FindProperty("lane").boundsValue = lane;
            so.FindProperty("seconds").floatValue = 40f;
            so.FindProperty("block").objectReferenceValue = block;
        });
    }

    // The transport's cargo door (the card "자원자 피날레"): a door across the hold's rear opening, shut as the ride's last
    // stretch begins (Ch4.13) unless someone stands at the lever on its post by the ramp, or a wedge was knocked in.
    private static void CargoDoor(GreyboxBuilder b, float x, out GimmickCargoDoor door)
    {
        door = b.Switch<GimmickCargoDoor>("수송기 화물 문", Vector3.zero);
        var panel = b.Blocker("수송기 화물 문 (닫히면 막힘)", 345.7f + x, 346f + x, 1.2f, 5.5f, -2.7f, 2.7f, door.transform, b.Metal, "Metal");
        panel.SetActive(false);
        float leverX = 353.9f + x, leverZ = -4.6f;
        b.Box("화물 문 레버 기둥", leverX - .1f, leverX + .1f, 0f, 1.2f, leverZ - .1f, leverZ + .1f, b.Metal, "MetalPipe");
        b.Box("화물 문 레버", leverX - .03f, leverX + .03f, 1.2f, 1.6f, leverZ - .03f, leverZ + .03f, b.Marker, solid: false);
        b.Box("화물 문 쐐기", leverX + .12f, leverX + .3f, .9f, 1.05f, leverZ - .08f, leverZ + .08f, b.Wood, "WoodPlank", solid: false);
        var holder = door;
        GreyboxBuilder.Configure(holder, so =>
        {
            so.FindProperty("step").stringValue = "Ch4.13";
            so.FindProperty("door").objectReferenceValue = panel;
            so.FindProperty("lever").boundsValue = new Bounds(new Vector3(leverX, .9f, leverZ), new Vector3(1.6f, 1.8f, 1.6f));
            so.FindProperty("wedgeSeconds").floatValue = 12f;
            so.FindProperty("reach").floatValue = 1.8f;
        });
    }

    // Supplies for greed (the card "수송기에 더 싣기"): eight crates stacked by the ramp; each loaded shows in the hold.
    private static void CargoLoad(GreyboxBuilder b, float x, GimmickCargoDoor door)
    {
        Vector3 stack = new Vector3(355f + x, 0f, 6.5f);
        var load = b.Switch<GimmickCargoLoad>("보급 상자 싣기", stack + Vector3.up);
        // Its crates under a holder at the map's origin: boxes under a group are placed in its own space.
        var crates = new GameObject("보급 상자들").transform;
        crates.SetParent(load.transform, false);
        crates.position = Vector3.zero;
        var pile = new List<GameObject>();
        var loaded = new List<GameObject>();
        for (int i = 0; i < 8; i++)
        {
            float px = stack.x + (i % 2) * .95f - .475f, pz = stack.z + (i / 2 % 2) * .95f - .475f, py = i / 4 * .9f;
            pile.Add(b.DressPart(b.Box("보급 상자", px - .44f, px + .44f, py, py + .88f, pz - .44f, pz + .44f, b.Wood, "WoodPlank", group: crates), "crateFit"));
            float hx = 338f + x + (i % 4) * 1.4f, hz = i < 4 ? -1.4f : 1.4f;
            var inHold = b.Box("실은 보급 상자", hx - .5f, hx + .5f, 1.2f, 2.1f, hz - .5f, hz + .5f, b.Wood, "WoodPlank", group: crates);
            inHold.SetActive(false);
            loaded.Add(inHold);
        }
        GreyboxBuilder.Configure(load, so =>
        {
            Fill(so.FindProperty("steps"), new List<string> { "Ch4.12", "Ch4.13" });
            FillObjects(so.FindProperty("pile"), pile);
            FillObjects(so.FindProperty("loaded"), loaded);
            so.FindProperty("loadSeconds").floatValue = 2.5f;
            so.FindProperty("door").objectReferenceValue = door;
            so.FindProperty("hordeMore").intValue = 2;
            so.FindProperty("rateMore").floatValue = .12f;
            so.FindProperty("reach").floatValue = 2.2f;
        });
    }
}
