using UnityEngine;

// TowerMap: Halls. One part of the class; the rest is in TowerMap.cs and the files beside it.
public static partial class TowerMap
{
    // A hall across the four lanes: light, the lanes' scanners and signs, a shop counter, supplies, somewhere to sit.
    private static void Hall(GreyboxBuilder b, int level)
    {
        int segment = level / 4;
        float y = TowerLayout.FloorY(level), ceiling = TowerLayout.CeilingY(level), inner = TowerLayout.InnerHalf, length = TowerLayout.RoomLength;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            float z = TowerLayout.LaneMid(k);
            b.Box("홀 천장 등", 2f, length - 2f, ceiling - .06f, ceiling, z - .2f, z + .2f, panel, solid: false);
            for (int i = 0; i < RoomLights; i++)
                Downlight("홀 등 " + Floor(level), new Vector3(length * (i + .5f) / RoomLights, ceiling - .5f, z), 12f, 2.2f);
        }
        // The way on: a pink band along the east wall over the doors, and a scanner, a sign and a spot to stand by each.
        b.Box("홀 네온", TowerLayout.RoomLength - .04f, TowerLayout.RoomLength, y + 4.9f, y + 5f, -inner, inner, neon, solid: false);
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            Scanner(segment, k);
            Sign(segment, k);
            Vector3 at = TowerLayout.ScannerAt(segment, k);
            Vector3 stand = at + Vector3.left * .75f;
            b.Box("스캐너 자리", stand.x - .55f, stand.x + .55f, y, y + .01f, stand.z - .55f, stand.z - .5f, neon, solid: false);
            b.Box("스캐너 자리", stand.x - .55f, stand.x + .55f, y, y + .01f, stand.z + .5f, stand.z + .55f, neon, solid: false);
            b.Box("스캐너 자리", stand.x - .55f, stand.x - .5f, y, y + .01f, stand.z - .5f, stand.z + .5f, neon, solid: false);
        }
        // The shop: a counter with a stand in front of it (TowerLayout.ShopAt).
        Vector3 shop = TowerLayout.ShopAt(segment);
        b.Dress(b.Box("상점 카운터", shop.x + .7f, shop.x + 1.7f, y, y + 1.05f, shop.z - 1.6f, shop.z + 1.6f, trim), "shopcounter");
        b.Box("상점 네온", shop.x + 1.1f, shop.x + 1.3f, y + 2.7f, y + 3f, shop.z - 1.8f, shop.z + 1.8f, neon, solid: false);
        // Solid, with an unseen ridge over it (Tables/MapArt solid "cap"): walked through as a Prop before.
        b.PropSolid("vending", new Vector3(shop.x + 1.4f, y, shop.z + 3f), 270f);
        // Supplies by the walls where people arrive, and somewhere to sit out of everyone's way: a corner by the arrivals
        // and another past the shop.
        b.Pickup(new Vector3(2.4f, y, 13.5f));
        if (segment > 0) b.Pickup(new Vector3(2.4f, y, -13.5f));
        b.Dress(b.Box("소파", 5f, 7.4f, y, y + .85f, inner - 1f, inner, hallWall), "couch");
        b.Dress(b.Box("소파", 9f, 11.4f, y, y + .85f, -inner, -inner + 1f, hallWall), "couch");
        b.Dress(b.Box("소파", length * .62f, length * .62f + 2.4f, y, y + .85f, inner - 1f, inner, hallWall), "couch");
        b.Dress(b.Box("소파", length * .68f, length * .68f + 2.4f, y, y + .85f, -inner, -inner + 1f, hallWall), "couch");
        // Pots and lockers block too (a pot only to its rim, lockers capped, Tables/MapArt solid).
        b.PropSolid("plant", new Vector3(1f, y, inner - 1f));
        b.PropSolid("plant", new Vector3(length - 1f, y, -inner + 1f));
        b.PropSolid("plant", new Vector3(length * .5f, y, inner - .8f));
        b.PropSolid("plant", new Vector3(length * .5f, y, -inner + .8f));
        b.PropSolid("locker", new Vector3(length - 3.5f, y, inner - .4f), 180f);
        b.PropSolid("locker", new Vector3(length - 2.8f, y, inner - .4f), 180f);
        b.Ambience("Tower.Hall", 0f, TowerLayout.RoomLength, y, ceiling, -inner, inner, 2);
        HallFittings(b, level, segment);
        if (segment == 0) Lobby(b);
        // The hall's name over its arrivals, read from its middle (18 m off: larger than at 16 m).
        string label = segment == 0 ? "LOBBY" : "REST · " + TowerLayout.FloorOf(level - 1) + "F";
        Words3D("홀 이름 " + label, new Vector3(.2f, y + 4.2f, 0f), Vector3.left, "<b>" + label + "</b>", 9f, Color.white, new Vector2(16f, 1.8f));
    }

    // A hall's fittings from the Military Training kit, on its walls and clear of what stands in it (the scanners and
    // their marks, the shop, the sofas, plants, lockers and supplies): a departure line across the hall half a metre
    // before the scanners' marks (short of the lockers); by each lane's door an extinguisher on its low side and a
    // live-fire sign past its scanner; a camera high in each corner turned to the hall (the lobby's west ones on the
    // entrance hall's wall, the lobby having no west wall); speakers and wall lamps along the long walls; a gun locker
    // and two ammo crates on the south wall west of the middle; the facility's signs over two sofas.
    private static void HallFittings(GreyboxBuilder b, int level, int segment)
    {
        float y = TowerLayout.FloorY(level), inner = TowerLayout.InnerHalf, east = TowerLayout.RoomLength;
        float west = segment == 0 ? TowerLayout.WestEnd : 0f, line = TowerLayout.ScannerAt(segment, 0).x - 1.8f;
        b.Line("hubstripe", new Vector3(line, y + StripeLift, -inner + .4f), new Vector3(line, y + StripeLift, inner - 1.6f), 90f);
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            float door = TowerLayout.StartHalf(k);
            b.Mount("hubextinguisher", new Vector3(east, y + 1.1f, door - TowerLayout.DoorWidth * .5f - .55f), -90f);
            b.Mount("hubsign_livefire", new Vector3(east, y + 1.9f, door + 3.1f), -90f, 1.4f);
        }
        b.Mount("hubcamera", new Vector3(west, y + 5f, -inner + .45f), 55f);
        b.Mount("hubcamera", new Vector3(west, y + 5f, inner - .45f), 125f);
        b.Mount("hubcamera", new Vector3(east, y + 5f, -inner + .45f), -55f);
        b.Mount("hubcamera", new Vector3(east, y + 5f, inner - .45f), -125f);
        b.Mount("hubspeaker", new Vector3(13.5f, y + 4.6f, -inner), 0f);
        b.Mount("hubspeaker", new Vector3(22.5f, y + 4.6f, inner), 180f);
        foreach (float x in new[] { 4.5f, 13.5f, 22.5f, 31.5f })
        {
            b.Mount("hubwalllight", new Vector3(x, y + 3.6f, -inner), 0f);
            b.Mount("hubwalllight", new Vector3(x, y + 3.6f, inner), 180f);
        }
        b.PropSolid("hubgunlocker", new Vector3(14.6f, y, -inner + .33f), 0f, segment % 2);
        b.PropSolid("hubammocrate", new Vector3(16.75f, y, -inner + .42f), 0f, 0);
        b.PropSolid("hubammocrate", new Vector3(16.78f, y + b.ArtSize("hubammocrate").y, -inner + .44f), 5f, 1);
        b.Mount("hubsign_training", new Vector3(6.2f, y + 2.6f, inner), 180f, 1.5f);
        b.Mount("hubsign_attention", new Vector3(10.2f, y + 2.3f, -inner), 0f, 1.2f);
    }

    // The lobby's entrance hall: a desk, benches and the tower's name over the way in; inside the way in a checkpoint
    // line, an extinguisher, the facility's signs either side and two cameras over it.
    private static void Lobby(GreyboxBuilder b)
    {
        b.Dress(b.Box("안내 데스크", -7f, -4.6f, 0f, 1.1f, 8.5f, 11f, trim), "infodesk");
        b.Dress(b.Box("의자 줄", -9.6f, -5f, 0f, .9f, -17.8f, -17f, hallWall), "seats");
        b.Dress(b.Box("의자 줄", -9.6f, -5f, 0f, .9f, 17f, 17.8f, hallWall), "seats");
        b.Box("로비 천장 등", -9f, -1.5f, H - S - .06f, H - S, -.3f, .3f, panel, solid: false);
        Downlight("로비 등", new Vector3(-5.5f, H - S - .5f, 0f), 12f, 2.2f);
        Words3D("로비 이름", new Vector3(-.3f, 4.1f, 0f), Vector3.right, "<b>ENFILADE</b>  TOWER", 7f, Pink, new Vector2(14f, 1.4f));
        float gateWall = TowerLayout.WestEnd;
        // The entrance hall has no sound zone of its own (the lobby's covers it): its reflections by name (Tower.Gate).
        b.Reflections("Tower.Gate", gateWall, TowerLayout.WestFace, 0f, H - S, -TowerLayout.InnerHalf, TowerLayout.InnerHalf);
        b.Line("hubstripe", new Vector3(gateWall + 2.2f, StripeLift, -2.2f), new Vector3(gateWall + 2.2f, StripeLift, 2.2f), 90f);
        b.Mount("hubextinguisher", new Vector3(gateWall, 1.1f, -3.2f), 90f);
        b.Mount("hubsign_attention", new Vector3(gateWall, 1.7f, 3.6f), 90f, 1.2f);
        b.Mount("hubsign_photo", new Vector3(gateWall, 1.6f, -4.3f), 90f, 1.4f);
        b.Mount("hubcamera", new Vector3(gateWall, 4.9f, 3f), 125f);
        b.Mount("hubcamera", new Vector3(gateWall, 4.9f, -3f), 55f);
    }

    // A palm scanner by a lane's door, sunk in the floor until the hall chooses (TowerScanner).
    private static void Scanner(int segment, int lane)
    {
        var root = new GameObject("스캐너 " + segment + " " + TowerHUD.Letter(lane), typeof(TowerScanner));
        root.transform.SetParent(fixtures, false);
        root.transform.SetPositionAndRotation(TowerLayout.ScannerAt(segment, lane), Quaternion.LookRotation(Vector3.left, Vector3.up));
        // It rises out of the floor: its surfaces mapped in its own space, so they rise with it.
        Child(root.transform, "기둥", new Vector3(0f, .5f, 0f), new Vector3(.34f, 1f, .34f), GreyboxBuilder.MovingSurface(steel), true);
        var head = new GameObject("머리").transform;
        head.SetParent(root.transform, false);
        head.localPosition = new Vector3(0f, 1.04f, .03f);
        head.localRotation = Quaternion.Euler(35f, 0f, 0f);
        Child(head, "받침", new Vector3(0f, -.03f, 0f), new Vector3(.44f, .04f, .36f), GreyboxBuilder.MovingSurface(trim), false);
        var plate = Child(head, "손판", Vector3.zero, new Vector3(.4f, .035f, .32f), lamp, false);
        var line = Child(head, "인식선", new Vector3(0f, .022f, 0f), new Vector3(.36f, .012f, .025f), neon, false);
        var strip = Child(root.transform, "띠", new Vector3(0f, .5f, .175f), new Vector3(.07f, .78f, .02f), lamp, false);
        GreyboxBuilder.Configure(root.GetComponent<TowerScanner>(), so =>
        {
            so.FindProperty("segment").intValue = segment;
            so.FindProperty("lane").intValue = lane;
            so.FindProperty("plate").objectReferenceValue = plate.GetComponent<Renderer>();
            so.FindProperty("strip").objectReferenceValue = strip.GetComponent<Renderer>();
            so.FindProperty("scanLine").objectReferenceValue = line.transform;
        });
    }

    // The sign over a lane's door; the run writes the day's rooms on it (TowerSign).
    private static void Sign(int segment, int lane)
    {
        float y = TowerLayout.FloorY(TowerLayout.HallLevel(segment));
        var sign = new GameObject("표지 " + segment + " " + TowerHUD.Letter(lane), typeof(RectTransform));
        sign.transform.SetParent(fixtures, false);
        sign.transform.SetPositionAndRotation(new Vector3(TowerLayout.RoomLength - .25f, y + TowerLayout.DoorHeight + 1.25f, TowerLayout.StartHalf(lane) + .6f),
            Quaternion.LookRotation(Vector3.right, Vector3.up));
        GreyboxBuilder.Configure(sign.AddComponent<TowerSign>(), so =>
        {
            so.FindProperty("segment").intValue = segment;
            so.FindProperty("lane").intValue = lane;
        });
    }
}
