using UnityEditor;
using UnityEngine;

/// <summary>
/// The campaign's chapters as grey blocks (multiplayer plan stage 5A), one item each under
/// ENFILADE > 맵 > 캠페인. The story runs on from one chapter to the next:
/// 1장 옥상 탈출: survivors in a downtown flat cross the rooftops to a building site whose roof the rescue helicopter
///   lands on. Alarm shutter in the alley (hold out), rest stop in a corner shop, finale on the helipad. The helicopter
///   takes them, but goes down over the ring road.
/// 2장 막힌 고가도로: from the wreck along the jammed overpass toward the army checkpoint at the toll gate. The road is
///   broken, so they jump down to the road below (no way back). Rest stop at a filling station, finale at the gate.
/// 3장 지하철: the checkpoint has fallen; the way out is the subway under the river. Fuel cans for the depot's generator,
///   rest stop in the depot, finale at the far bank's exit. The army's boat takes them down the river to the airport.
/// 4장 공항 (the user's memo 15): from the cargo pier through the terminal (check-in hall, security, the duty-free shop as
///   the rest stop, a jet bridge with no plane) to the apron and the control tower's radio. An airliner left across the
///   runway keeps the last transport on the ground: its tug pushes it off while someone mans the tug (a manned hold,
///   CampaignPushback). Finale at the transport's ramp; it takes off as the song ends.
/// Each chapter runs along +X; the gimmicks (5C) and objectives (5B) hang on the named spots under 진행 자리.
/// </summary>
public static partial class CampaignMaps
{
    private const float Wall = .3f, ParapetHeight = .9f;
    private enum Side { West, East, South, North }

    /// <summary>Every chapter in a row after one question (memo 48: the small-prop fix needs each map rebuilt).</summary>
    [MenuItem("ENFILADE/맵/캠페인/모든 장 다시 짓기", priority = 0)]
    private static void RebuildAll() => GreyboxBuilder.RebuildAll("모든 장 다시 짓기",
        ("1장 옥상 탈출", Rooftops), ("2장 막힌 고가도로", Overpass), ("3장 지하철", Subway), ("4장 공항", Airport));

    // Two flights per floor side by side, from a landing by the corridor (near) to a half landing at the far end and
    // back, from top down to bottom (3 m a floor), with a wall between them.
    private static void Flights(GreyboxBuilder b, string name, float x0, float x1, float near, float far, float end,
        float top, float bottom)
    {
        float half = (x1 - x0) * .5f, a = x0 + half * .5f, c = x0 + half * 1.5f;
        int floor = 0;
        for (float y = top; y > bottom + .01f; y -= 3f, floor++)
        {
            float mid = y - 1.5f;
            b.Ramp($"{name} {floor * 2 + 1}", new Vector3(a, mid, far), new Vector3(a, y, near), half - .1f, b.Concrete);
            b.Box($"{name} 참 {floor + 1}", x0, x1, mid - Wall, mid, far, end, b.Concrete, "Concrete");
            b.Ramp($"{name} {floor * 2 + 2}", new Vector3(c, y - 3f, near), new Vector3(c, mid, far), half - .1f, b.Concrete);
        }
        b.Box(name + " 가운데 벽", x0 + half - .05f, x0 + half + .05f, bottom, top + 1f, near, far);
    }

    // A floor slab with its top at y over x0..x1, z0..z1, open over the rectangle hx0..hx1, hz0..hz1.
    private static void Slab(GreyboxBuilder b, string name, float y, float x0, float x1, float z0, float z1,
        float hx0, float hx1, float hz0, float hz1, Material material, string surface)
    {
        float y0 = y - Wall;
        if (hz0 > z0) b.Box(name + " 남", x0, x1, y0, y, z0, hz0, material, surface);
        if (hz1 < z1) b.Box(name + " 북", x0, x1, y0, y, hz1, z1, material, surface);
        if (hx0 > x0) b.Box(name + " 서", x0, hx0, y0, y, hz0, hz1, material, surface);
        if (hx1 < x1) b.Box(name + " 동", hx1, x1, y0, y, hz0, hz1, material, surface);
    }

    // A wall along x (z0..z1 thick) from y0 to y1 with door openings 2.3 m high at the given spans (west to east).
    private static void WallAlongX(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float y0, float y1,
        params (float from, float to)[] doors)
    {
        float x = x0;
        int part = 0;
        foreach (var (from, to) in doors)
        {
            if (from > x) b.Box($"{name} {++part}", x, from, y0, y1, z0, z1);
            b.Box($"{name} 문 위 {part}", from, to, y0 + 2.3f, y1, z0, z1);
            x = to;
        }
        if (x1 > x) b.Box($"{name} {++part}", x, x1, y0, y1, z0, z1);
    }

    // A stopped car, 4.5 m long across x, at <paramref name="y"/> (the road it stands on), dressed as one of
    // the abandoned cars (Tables/MapArt car, picked by place).
    private static void Car(GreyboxBuilder b, float x, float y, float z)
        => b.Dress(b.Box("차", x - 2.25f, x + 2.25f, y, y + 1.5f, z - .95f, z + .95f, b.Metal, "Metal"), "car");

    private static void Bus(GreyboxBuilder b, string name, float x0, float x1, float y, float z0, float z1)
        => b.Box(name, x0, x1, y, y + 3.2f, z0, z1, b.Metal, "Metal");

    // Knee-high walls along a roof's two long sides.
    private static void Parapets(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float roof)
    {
        b.Box(name + " 난간 남", x0, x1, roof, roof + ParapetHeight, z0, z0 + Wall);
        b.Box(name + " 난간 북", x0, x1, roof, roof + ParapetHeight, z1 - Wall, z1);
    }

    // A hollow room with one door, walls inside its extents, and a roof.
    private static void Room(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float floor, float height,
        Side door, float doorFrom, float doorTo, float doorHeight = 2.4f)
    {
        float top = floor + height;
        RoomWall(b, name + " 서벽", x0, x0 + Wall, z0, z1, floor, top, door == Side.West, false, doorFrom, doorTo, floor + doorHeight);
        RoomWall(b, name + " 동벽", x1 - Wall, x1, z0, z1, floor, top, door == Side.East, false, doorFrom, doorTo, floor + doorHeight);
        RoomWall(b, name + " 남벽", x0, x1, z0, z0 + Wall, floor, top, door == Side.South, true, doorFrom, doorTo, floor + doorHeight);
        RoomWall(b, name + " 북벽", x0, x1, z1 - Wall, z1, floor, top, door == Side.North, true, doorFrom, doorTo, floor + doorHeight);
        b.Box(name + " 지붕", x0, x1, top, top + Wall, z0, z1);
    }

    private static void RoomWall(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float floor, float top,
        bool hasDoor, bool alongX, float from, float to, float doorTop)
    {
        if (!hasDoor) { b.Box(name, x0, x1, floor, top, z0, z1); return; }
        if (alongX)
        {
            b.Box(name + " 1", x0, from, floor, top, z0, z1);
            b.Box(name + " 2", to, x1, floor, top, z0, z1);
            b.Box(name + " 문 위", from, to, doorTop, top, z0, z1);
            return;
        }
        b.Box(name + " 1", x0, x1, floor, top, z0, from);
        b.Box(name + " 2", x0, x1, floor, top, to, z1);
        b.Box(name + " 문 위", x0, x1, doorTop, top, from, to);
    }
}
