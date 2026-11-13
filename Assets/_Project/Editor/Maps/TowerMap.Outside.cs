using UnityEditor;
using UnityEngine;

// TowerMap: Outside. One part of the class; the rest is in TowerMap.cs and the files beside it.
public static partial class TowerMap
{
    // The square in front of the west face: the army's checkpoint at the tower's door, lamps, the edge of the map, and
    // the tower's own neon up its corners and round each hall.
    private static void Square(GreyboxBuilder b)
    {
        float x0 = TowerLayout.WestEnd - W, x1 = TowerLayout.EastEnd + W, outer = TowerLayout.OuterHalf, top = TowerLayout.FloorY(TowerLayout.Levels);
        // The map's east edge keeps a strip of ground behind the tower however long it is.
        float east = x1 + 14f;
        b.Box("땅 서", -80f, x0, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("땅 동", x1, east, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("땅 남", x0, x1, -.5f, 0f, -60f, -outer, b.Road, "Asphalt");
        b.Box("땅 북", x0, x1, -.5f, 0f, outer, 60f, b.Road, "Asphalt");
        b.Box("맵 끝 서", -81f, -80f, 0f, 5f, -60f, 60f);
        b.Box("맵 끝 동", east, east + 1f, 0f, 5f, -60f, 60f);
        b.Box("맵 끝 남", -80f, east, 0f, 5f, -61f, -60f);
        b.Box("맵 끝 북", -80f, east, 0f, 5f, 60f, 61f);
        // Neon up the four corners and round the halls' levels.
        foreach (float x in new[] { x0 - .06f, x1 + .06f })
            foreach (float z in new[] { -outer - .06f, outer + .06f })
                b.Box("탑 네온", x - .1f, x + .1f, 0f, top, z - .1f, z + .1f, neon, solid: false);
        for (int segment = 1; segment < TowerLayout.Segments; segment++)
        {
            float y = TowerLayout.FloorY(TowerLayout.HallLevel(segment)) - .2f;
            b.Box("탑 띠", x0 - .08f, x0, y, y + .25f, -outer, outer, neon, solid: false);
            b.Box("탑 띠", x1, x1 + .08f, y, y + .25f, -outer, outer, neon, solid: false);
            b.Box("탑 띠", x0, x1, y, y + .25f, -outer - .08f, -outer, neon, solid: false);
            b.Box("탑 띠", x0, x1, y, y + .25f, outer, outer + .08f, neon, solid: false);
        }
        Words3D("탑 이름", new Vector3(x0 - .15f, 8.2f, 0f), Vector3.right, "<b>ENFILADE</b>", 40f, Color.white, new Vector2(34f, 5f));
        Words3D("탑 이름 아래", new Vector3(x0 - .15f, 5.6f, 0f), Vector3.right, "TOWER  ·  TRAINING BLOCK 7", 10f, Pink, new Vector2(30f, 1.6f));
        // The army's checkpoint: a funnel of barriers to the door, sandbags, a guard tower, a lorry, lamps.
        float door = x0 - 1f;
        b.Dress(b.Box("검문 방벽", door - 16f, door - 2f, 0f, .9f, -6.5f, -5.9f), "jersey");
        b.Dress(b.Box("검문 방벽", door - 16f, door - 2f, 0f, .9f, 5.9f, 6.5f), "jersey");
        b.Dress(b.Box("모래주머니", door - 9f, door - 7.8f, 0f, 1.2f, -14f, -8f), "sandbags");
        b.Dress(b.Box("모래주머니", door - 9f, door - 7.8f, 0f, 1.2f, 8f, 14f), "sandbags");
        b.PropSolid("guardtower", new Vector3(door - 6f, 0f, -18f), 90f);
        b.PropSolid("militarytruck", new Vector3(door - 24f, 0f, 16f), 0f);
        b.Prop("warninglight", new Vector3(door - 2.5f, 0f, -5.3f), 90f);
        b.Prop("warninglight", new Vector3(door - 2.5f, 0f, 5.3f), 90f);
        foreach (float z in new[] { -22f, 22f }) b.Prop("streetlamp", new Vector3(door - 12f, 0f, z), z < 0f ? 0f : 180f);
        foreach (var (x, z) in new[] { (door - 20f, -9f), (door - 21f, -8.2f), (door - 19.5f, 9f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 17f % 360f);
        // On the face either side of the way in (kit, as the hub's): the gate's sign, the range's rules, a camera over it.
        b.Mount("hubsign_gate", new Vector3(x0, 3f, 4.4f), -90f, 1.6f);
        b.Mount("hubsign_livefire", new Vector3(x0, 1.7f, -3.6f), -90f, 1.5f);
        b.Mount("hubcamera", new Vector3(x0, 4.4f, -3.2f), -55f);
        Light("입구 등", new Vector3(x0 - 2f, 4.6f, 0f), 12f, 1.8f);
    }

    // The run of the tower (TowerRun) and the inside's sound.
    private static void Run(GreyboxBuilder b)
    {
        var run = b.AddSystem("타워 진행").AddComponent<TowerRun>();
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        GreyboxBuilder.Configure(run, so => so.FindProperty("enemyPrefab").objectReferenceValue = enemy != null ? enemy.GetComponent<EnemyHealth>() : null);
        b.Ambience("Tower.Inside", TowerLayout.WestEnd, TowerLayout.EastEnd, 0f, TowerLayout.FloorY(TowerLayout.Levels), -TowerLayout.InnerHalf,
            TowerLayout.InnerHalf, 1);
    }
}
