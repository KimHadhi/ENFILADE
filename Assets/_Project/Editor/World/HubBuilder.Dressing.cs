using UnityEngine;

/// <summary>
/// The hub dressed as a military training facility (the user, 2026-11-12: "로비도 좀 꾸며 줄 수 있나. 나 그 military
/// training 에셋도 임포트 했으니까 그냥 그레이박스 말고 진짜 실무 실품처럼 만들어 줄 수 있나."), with the bought Dekogon
/// Military Training Facility kit (brought in from its Unreal export by Tools/Props/plan_unreal.py and build_props.py
/// to Assets/ThirdParty/Dekogon/MilitaryTrainingFacility, its kinds hub* in Tables/MapArt), laid through the map
/// dresser (GreyboxBuilder.Attach). As on the chapters, the boxes keep the collision and the models are only seen; a
/// model standing out far enough to be walked into gets an unseen box of its own size (Solid). The boxes wear
/// Tables/MapSurfaces looks by name (rows Hub.*: corrugated steel over the hall's panels and under its roof, a steel
/// grating on the catwalk, concrete range walls, gravel downrange, a dirt berm).
/// - The hall: the kit's training floor; concrete wall panels the first 4.5 m of every wall; full-height pillars at
///   the corners, either side of both ways out and along the north wall; concrete beams under the roof every 4 m with
///   fluorescent fixtures between them; a duct and pipe runs; floor stripes round the map table, before the stage,
///   at the way out and inside the range's opening; the gun rack as a slatwall with lamps over it; a gun locker, the
///   quartermaster's desk, crates and boxes; extinguishers, cameras, speakers, red alarm lights by the way out, and
///   the facility's signs.
/// - The range: a covered firing point (a steel roof on the side walls, two beams, panel lights and two lamps); the
///   counters, a long and a short cabinet per lane with the lane's number on it; booth panels between the lanes;
///   rubber mats; the ammo crate; overhead concrete baffles 20, 45 and 70 m out; caged lamps along both walls; the
///   range's rules and signs, horn speakers, a camera, spent targets pinned up; rock before the dirt berm.
/// Models face +Z unturned (the kit's front, Unreal's +Y, comes in as Unity's +Z), so a yaw here is where a front looks.
/// Places are the layout's (metres, +Z north, as HubBuilder's), so they live here with it.
/// </summary>
public static partial class HubBuilder
{
    // The hall's and the firing point's floor: the box under it tops out here and the kit's floor lies on it, so feet,
    // shots and marks meet what is seen (the ground outside stays 1 cm lower).
    private const float Floor = .01f;
    // The hall roof's underside, and the firing point roof's.
    private const float RoofUnder = 7f, ShelterUnder = 4f;
    // The counters' back edge (downrange side) and how big the backstop's rocks are.
    private const float CounterBack = -14.41f, RockScale = 1.6f;
    // How far a wall piece stands proud of its wall's face, so the box behind never shows through it.
    private const float Proud = .02f;

    private static GreyboxBuilder dresser;

    // ---- The hall ----

    private static void DressHall()
    {
        HallFloor();
        HallWalls();
        HallCeiling();
        HallStripes();
        HallFittings();
    }

    private static void HallFloor()
    {
        var floor = Box("은신처 바닥", -12f, 12f, -.5f, Floor, -10f, 14f, null, "Concrete");
        floor.GetComponent<Renderer>().enabled = false;
        // 4 m tiles, each turned a quarter by its place so the floor's wear doesn't run in rows.
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                dresser.Prop("hubfloor", new Vector3(-10f + i * 4f, Floor, -8f + j * 4f), (i * 3 + j * 5) % 4 * 90f);
    }

    private static void HallWalls()
    {
        // The north wall either side of the way out, the south wall either side of the range's opening, the west wall
        // all along (behind the class lockers and the catwalk), the east wall either side of the stage's back wall.
        Row("hubwall", new Vector3(-12f, Floor, 14f), new Vector3(-2.5f, Floor, 14f), 180f);
        Row("hubwall", new Vector3(2.5f, Floor, 14f), new Vector3(12f, Floor, 14f), 180f);
        Row("hubwall", new Vector3(-12f, Floor, -10f), new Vector3(-7.3f, Floor, -10f), 0f);
        Row("hubwall", new Vector3(7.3f, Floor, -10f), new Vector3(12f, Floor, -10f), 0f);
        Row("hubwall", new Vector3(-12f, Floor, -10f), new Vector3(-12f, Floor, 14f), 90f);
        Row("hubwall", new Vector3(12f, Floor, -10f), new Vector3(12f, Floor, -6f), -90f);
        Row("hubwall", new Vector3(12f, Floor, 6f), new Vector3(12f, Floor, 14f), -90f);
        // Pillars up to the roof (the kit's 4 m one raised): the corners, both sides of the way out, the north wall's
        // middles, the stage's back corners, and inside the range's opening on the range walls' lines.
        foreach (var at in new[]
                 {
                     new Vector2(-12f, -10f), new Vector2(12f, -10f), new Vector2(-12f, 14f), new Vector2(12f, 14f),
                     new Vector2(-2.78f, 14f), new Vector2(2.78f, 14f), new Vector2(-7.25f, 14f), new Vector2(7.25f, 14f),
                     new Vector2(12f, -6f), new Vector2(12f, 6f), new Vector2(-7.02f, -10f), new Vector2(7.02f, -10f)
                 })
        {
            float height = dresser.ArtSize("hubpillar").y;
            if (height > .01f) Solid("hubpillar", new Vector3(at.x, Floor, at.y), 0f, new Vector3(1f, (RoofUnder - Floor) / height, 1f), "Concrete");
        }
    }

    private static void HallCeiling()
    {
        // Concrete beams across the hall under the roof every 4 m (the skylight's edges sit on two of them).
        float beamDown = dresser.ArtSize("hubbeam").y;
        for (float z = -6f; z <= 10f; z += 4f)
            Span("hubbeam", new Vector3(-12f, RoofUnder - beamDown, z), new Vector3(12f, RoofUnder - beamDown, z), 0f);
        // Fluorescent fixtures between the beams, in two rows clear of the skylight and the stage's spot lights.
        for (float z = -8f; z <= 12f; z += 4f)
            foreach (float x in new[] { -6.5f, 6.5f })
                dresser.Hang("hubhanglight", new Vector3(x, RoofUnder, z), 90f);
        // A duct down the west side under the beams (clear of the catwalk and the zipline), pipes along the north wall
        // over its panels and along the west wall over the catwalk, clear of the pillars' faces.
        float ductFloor = RoofUnder - beamDown - dresser.ArtSize("hubduct").y;
        Span("hubduct", new Vector3(-7.6f, ductFloor, -10f), new Vector3(-7.6f, ductFloor, 14f), 0f);
        float pipe = dresser.ArtSize("hubpipe").y;
        foreach (float y in new[] { 5.05f, 5.4f })
        {
            Span("hubpipe", new Vector3(-12f, y - pipe * .5f, 13.55f), new Vector3(-3.1f, y - pipe * .5f, 13.55f), 0f);
            Span("hubpipe", new Vector3(3.1f, y - pipe * .5f, 13.55f), new Vector3(12f, y - pipe * .5f, 13.55f), 0f);
        }
        Span("hubpipe", new Vector3(-11.55f, 5.9f - pipe * .5f, -9.7f), new Vector3(-11.55f, 5.9f - pipe * .5f, 13.7f), 90f);
    }

    private static void HallStripes()
    {
        float y = Floor + .004f;
        // Round the map table, before the stage, across the way out and inside the range's opening.
        Stripe(new Vector3(-2.8f, y, 0f), new Vector3(2.8f, y, 0f));
        Stripe(new Vector3(-2.8f, y, 4f), new Vector3(2.8f, y, 4f));
        Stripe(new Vector3(-2.8f, y, 0f), new Vector3(-2.8f, y, 4f));
        Stripe(new Vector3(2.8f, y, 0f), new Vector3(2.8f, y, 4f));
        Stripe(new Vector3(6.6f, y, -6f), new Vector3(6.6f, y, 6f));
        Stripe(new Vector3(-2.5f, y, 12.4f), new Vector3(2.5f, y, 12.4f));
        Stripe(new Vector3(-6.7f, y, -9.55f), new Vector3(6.7f, y, -9.55f));
    }

    private static void HallFittings()
    {
        // The way out: red alarm lights either side of the board (over the pipes), the gate's number, an extinguisher.
        Mount("hubalarmlight", new Vector3(-3.5f, 6.1f, 14f), 180f);
        Mount("hubalarmlight", new Vector3(3.5f, 6.1f, 14f), 180f);
        Mount("hubsign_bay", new Vector3(-4.2f, 3.2f, 14f), 180f, 2f);
        Mount("hubextinguisher", new Vector3(-3.6f, 1.1f, 14f), 180f);
        Mount("hubsign_attention", new Vector3(-9.4f, 2.2f, 14f), 180f, 1.2f);
        // The quartermaster's desk by the north-east corner, a stool at it, an ammo can and a case on it, boxes beside.
        Vector3 desk = dresser.ArtSize("hubdesk");
        float deskZ = 14f - Proud - desk.z * .5f;
        Solid("hubdesk", new Vector3(9.4f, Floor, deskZ), 180f, Vector3.one, "Metal");
        dresser.Prop("hubstool", new Vector3(9.4f, Floor, deskZ - desk.z * .5f - .45f), 20f);
        dresser.Prop("hubammocan", new Vector3(8.95f, Floor + desk.y, deskZ + .05f), 90f);
        dresser.Prop("hubcase", new Vector3(9.85f, Floor + desk.y, deskZ), 160f, 0);
        float box = dresser.ArtSize("hubcardboard").y;
        Solid("hubcardboard", new Vector3(11.1f, Floor, 13.3f), 12f, Vector3.one, "WoodPanel");
        Solid("hubcardboard", new Vector3(11.15f, Floor + box, 13.28f), 38f, Vector3.one, "WoodPanel", 1);
        // South wall east of the range's opening: an extinguisher, the range's rules, a gun locker under the range's sign.
        Mount("hubextinguisher", new Vector3(7.55f, 1.1f, -10f), 0f);
        Mount("hubsign_livefire", new Vector3(8.45f, 1.75f, -10f), 0f, 1.5f);
        Solid("hubgunlocker", new Vector3(10.25f, Floor, -10f + Proud + dresser.ArtSize("hubgunlocker").z * .5f), 0f, Vector3.one, "MetalBox");
        Mount("hubsign_training", new Vector3(10.25f, 3.25f, -10f), 0f);
        // Lamps over the gun rack (west of the opening), clear of its sign.
        Mount("hubwalllight", new Vector3(-10.7f, 3.55f, -10f), 0f);
        Mount("hubwalllight", new Vector3(-8.5f, 3.55f, -10f), 0f);
        // The west wall: a warning under the catwalk's south end; over its pipe, between two beams, a speaker; a camera
        // over the north corner.
        Mount("hubsign_warning", new Vector3(-12f, 1.6f, -9f), 90f, 1.4f);
        Mount("hubspeaker", new Vector3(-12f, 6.45f, 4f), 90f);
        Mount("hubcamera", new Vector3(-12f, 6.4f, 12.6f), 125f);
        // The east wall: north of the stage the floor cleaner parked, an extinguisher, a sign and a speaker; south of it
        // two crates and a camera over the corner.
        Solid("hubcleaner", new Vector3(11.3f, Floor, 9.3f), -90f, Vector3.one, "Metal");
        Mount("hubextinguisher", new Vector3(12f, 1.1f, 7.1f), -90f);
        Mount("hubsign_photo", new Vector3(12f, 2.6f, 11.6f), -90f);
        Mount("hubspeaker", new Vector3(12f, 6f, 12f), -90f);
        float crate = dresser.ArtSize("hubammocrate").y;
        Solid("hubammocrate", new Vector3(11.15f, Floor, -7.7f), 90f, Vector3.one, "Plastic");
        Solid("hubammocrate", new Vector3(11.12f, Floor + crate, -7.66f), 96f, Vector3.one, "Plastic", 1);
        Mount("hubcamera", new Vector3(12f, 6.2f, -9.2f), -125f);
    }

    // ---- The range ----

    private static void DressRange()
    {
        // The firing point's floor (concrete) and downrange (gravel), their tops at the hall's floor.
        Box("사대 바닥", -7f, 7f, -.5f, Floor, CounterBack, -10f, null, "Concrete");
        Box("사격장 바닥", -7f, 7f, -.5f, Floor, -122f, CounterBack, null, "Gravel");
        // The covered firing point: a steel roof resting on the side walls (low enough to leave the range board seen
        // from the counters), two beams under it, panel lights and two lamps.
        Box("사대 지붕", -7.3f, 7.3f, ShelterUnder, ShelterUnder + .25f, -15.6f, -10.3f, null, "Metal");
        float beamDown = dresser.ArtSize("hubbeam").y;
        foreach (float z in new[] { -11f, -15.2f })
            Span("hubbeam", new Vector3(-7f, ShelterUnder - beamDown, z), new Vector3(7f, ShelterUnder - beamDown, z), 0f);
        foreach (float x in new[] { -5.25f, -1.75f, 1.75f, 5.25f })
            dresser.Hang("hubpanellight", new Vector3(x, ShelterUnder, -13.1f), 0f);
        Lamp(new Vector3(-3.5f, 3.7f, -12.8f), 9f, 1.4f);
        Lamp(new Vector3(3.5f, 3.7f, -12.8f), 9f, 1.4f);
        RangeCounters();
        RangeWalls();
        // Overhead concrete baffles 20, 45 and 70 m out, on the walls' tops, each with an unseen stop of its size.
        Vector3 slab = dresser.ArtSize("hubbarrier");
        foreach (float z in new[] { -34f, -59f, -84f })
        {
            Span("hubbarrier", new Vector3(-7.3f, 5f - slab.y, z), new Vector3(7.3f, 5f - slab.y, z), 0f);
            var stop = Box("머리 위 방호벽", -7.3f, 7.3f, 5f - slab.y, 5f, z - slab.z * .5f, z + slab.z * .5f, null, "Concrete");
            stop.GetComponent<Renderer>().enabled = false;
        }
        // The backstop: two rocks before the dirt berm (sunk to the line they were made to stand at), and an unseen stop
        // inside them, so a miss lands about at the rock.
        Vector3 rock = dresser.ArtSize("hubrock");
        dresser.Prop("hubrock", new Vector3(-3.4f, Floor - .45f * RockScale, -122f + rock.z * RockScale * .5f), 0f, -1, RockScale);
        dresser.Prop("hubrock", new Vector3(4f, Floor - .45f * RockScale * .9f, -122f + rock.z * RockScale * .45f), 0f, -1, RockScale * .9f);
        var berm = Box("바위 막", -7.3f, 7.3f, 0f, 6.5f, -122f, -120.3f, null, "Stone");
        berm.GetComponent<Renderer>().enabled = false;
    }

    private static void RangeCounters()
    {
        // A long and a short cabinet per lane stretched to its 3.5 m (the last lane only the long one, leaving the way
        // downrange), doors to the shooters, the lane's number on the long one; booth panels on the lane lines; mats.
        Vector3 longOne = dresser.ArtSize("hubcounter"), shortOne = dresser.ArtSize("hubcountershort");
        if (longOne.x <= .01f || shortOne.x <= .01f) return;
        float z = CounterBack + longOne.z * .5f, front = CounterBack + longOne.z, top = Floor + longOne.y;
        for (int lane = 0; lane < 4; lane++)
        {
            float x0 = -7f + lane * 3.5f;
            if (lane < 3)
            {
                float stretch = 3.5f / (longOne.x + shortOne.x);
                dresser.PropScaled("hubcounter", new Vector3(x0 + longOne.x * stretch * .5f, Floor, z), 0f, new Vector3(stretch, 1f, 1f));
                dresser.PropScaled("hubcountershort", new Vector3(x0 + (longOne.x + shortOne.x * .5f) * stretch, Floor, z), 0f, new Vector3(stretch, 1f, 1f));
                Mount("hubsign_lane", new Vector3(x0 + 1.75f, .72f, front), 0f, 2.5f, lane);
            }
            else
            {
                dresser.Prop("hubcounter", new Vector3(x0 + longOne.x * .5f, Floor, z));
                Mount("hubsign_lane", new Vector3(x0 + longOne.x * .5f, .72f, front), 0f, 2.5f, lane);
            }
        }
        float panel = dresser.ArtSize("hubboothwall").z;
        foreach (float x in new[] { -3.5f, 0f, 3.5f })
            Solid("hubboothwall", new Vector3(x, top, CounterBack + panel * .5f), 0f, Vector3.one, "WoodPanel");
        foreach (float x in new[] { -4.75f, -1.75f, 1.75f, 5.25f })
            dresser.Prop("hubmat", new Vector3(x, Floor, -12.9f), 0f);
        // On the counters by the panels: ammo cans, a case, a box of rounds.
        dresser.Prop("hubammocan", new Vector3(-6.55f, top, -14.15f), 80f);
        dresser.Prop("hubcase", new Vector3(-.45f, top, -14.1f), 185f, 1);
        dresser.Prop("hubcardboard", new Vector3(3.05f, top, -14.12f), 8f, 2);
        dresser.Prop("hubammocan", new Vector3(5.1f, top, -14.15f), 95f);
    }

    private static void RangeWalls()
    {
        // Caged lamps on both walls between the distance marks.
        foreach (float z in new[] { -22f, -47f, -72f, -97f })
        {
            Mount("hubbulkhead", new Vector3(-7f, 3.4f, z), 90f);
            Mount("hubbulkhead", new Vector3(7f, 3.4f, z), -90f);
        }
        // By the firing point: the range's rules, eye and ear protection, a warning, horn speakers under the roof, a
        // camera, and spent targets pinned up by the way downrange.
        Mount("hubsign_livefire", new Vector3(-7f, 1.9f, -11.5f), 90f, 1.5f);
        Mount("hubsign_eyeear", new Vector3(7f, 2.6f, -11.6f), -90f, 2f);
        Mount("hubsign_warning", new Vector3(7f, 2.3f, -18.5f), -90f, 1.5f);
        Mount("hubspeaker", new Vector3(-7f, 3.55f, -14.6f), 90f);
        Mount("hubspeaker", new Vector3(7f, 3.55f, -14.6f), -90f);
        Mount("hubcamera", new Vector3(-7f, 3.6f, -12.2f), 125f);
        for (int i = 0; i < 3; i++)
            Mount("hubtarget", new Vector3(7f, 1.45f, -12.7f + i * .7f), -90f, .8f, i);
    }

    // ---- Helpers ----

    // Whole copies of a piece from a to b (floor points), turned to yaw, each stretched along the line only (its own x
    // or z, whichever the line runs along) so the run ends where it should; shift moves them all off the line.
    private static void Span(string kind, Vector3 a, Vector3 b, float yaw, Vector3 shift = default)
    {
        Vector3 size = dresser.ArtSize(kind), line = b - a;
        float length = line.magnitude;
        if (size == Vector3.zero || length < .01f) return;
        Vector3 dir = line / length;
        bool alongX = Mathf.Abs(Vector3.Dot(Quaternion.Euler(0f, yaw, 0f) * Vector3.right, dir)) > .7f;
        float own = Mathf.Max(.01f, alongX ? size.x : size.z);
        int count = Mathf.Max(1, Mathf.RoundToInt(length / own));
        float stretch = length / count / own;
        var scale = alongX ? new Vector3(stretch, 1f, 1f) : new Vector3(1f, 1f, stretch);
        for (int i = 0; i < count; i++)
            dresser.PropScaled(kind, a + dir * ((i + .5f) * length / count) + shift, yaw, scale);
    }

    // A run of wall pieces along a wall's face from a to b, their fronts to face (their backs into the wall).
    private static void Row(string kind, Vector3 a, Vector3 b, float face)
    {
        float depth = dresser.ArtSize(kind).z;
        Vector3 front = Quaternion.Euler(0f, face, 0f) * Vector3.forward;
        Span(kind, a, b, face, front * (Proud - depth * .5f));
    }

    // A floor stripe from a to b (the kit's 2 m line piece, stretched to fit).
    private static void Stripe(Vector3 a, Vector3 b)
        => Span("hubstripe", a, b, Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.z - a.z) ? 0f : 90f);

    // A piece hung on a wall: its middle at the point's height, its back on the wall's face there, its front to face.
    private static GameObject Mount(string kind, Vector3 middle, float face, float scale = 1f, int pick = -1)
    {
        Vector3 size = dresser.ArtSize(kind, Mathf.Max(0, pick)) * scale;
        if (size == Vector3.zero) return null;
        Vector3 front = Quaternion.Euler(0f, face, 0f) * Vector3.forward;
        return dresser.Prop(kind, middle + front * (size.z * .5f + .005f) - Vector3.up * (size.y * .5f), face, pick, scale);
    }

    // A piece that blocks: the model on the floor point, stretched on its own axes, and an unseen box of its size turned
    // with it (what shots and feet meet, as surface).
    private static GameObject Solid(string kind, Vector3 floor, float yaw, Vector3 stretch, string surface, int pick = 0)
    {
        Vector3 size = Vector3.Scale(dresser.ArtSize(kind, pick), stretch);
        if (size == Vector3.zero) return null;
        var model = dresser.PropScaled(kind, floor, yaw, stretch, pick);
        var box = Box(kind + " (충돌)", floor.x - size.x * .5f, floor.x + size.x * .5f, floor.y, floor.y + size.y,
            floor.z - size.z * .5f, floor.z + size.z * .5f, null, surface);
        box.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        box.GetComponent<Renderer>().enabled = false;
        return model;
    }

    // Dresses a box (the gun board, the truck, the shutter) with a kind; true if a model went on it.
    private static bool DressBox(GameObject box, string kind, int pick = -1)
    {
        var before = dresser.LastPlaced;
        dresser.Dress(box, kind, pick);
        return dresser.LastPlaced != before;
    }
}
