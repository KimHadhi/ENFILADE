using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Map dressing, where players can see (the user, 2026-11-14: "맵 최적화랑 꾸미기 좀. 특히 허브 쪽 데칼은 보이지도 않는
/// 바깥에 엄청 깔아 뒀더만, 제대로 좀..."). Decals went on every face of every box that wore a surface, so the hub's outer
/// walls, its roof and the ground beyond its walls took most of them, where nobody stands or looks. Now the build first
/// finds where a player's eyes can be (BuildSight): over the walk mesh (the map's own; the hub, which bakes none, gets one
/// here only for this, SightMesh), only its pieces a path reaches from the player's start, a progress spot or where
/// players climb or ride (ladders, ziplines, swing bars: no path goes up those) (a roof nobody climbs to, the ground
/// outside the walls or a sealed room has walk mesh too, but no way there), every SightSpacing m at eye height. Seen asks
/// whether one of those eyes, within a distance and not side-on, sees a spot with nothing solid between: a decal is laid
/// only where one does (GreyboxBuilder.Decals). SeenBox asks it of a whole model: the dressing nobody can see is taken
/// away (CullUnseenDressing). Glass, gates, doors and the hub's shutter never block the view (they are seen through or
/// open in play).
/// </summary>
public sealed partial class GreyboxBuilder
{
    // Eyes over the walk mesh this far apart (m) and this high over it (m). A face seen at a shallower angle than this
    // (the sine of it) counts as unseen, and at most this many eyes are tried for one spot, nearest first.
    private const float SightSpacing = 2.5f, SightEye = 1.6f, SightGrazing = .09f;
    private const int SightTries = 48;
    // The eyes' grid (m), how far off the walk mesh a start may be (m), and how finely corners are matched (per metre).
    private const float SightCell = 8f, SightStartReach = 3f, SightWeld = 50f;
    // A model's corners are tried this share of the way out from its middle; a hit this close round its box is its own
    // (m); the quality's LOD bias (PC), and the farthest a model is looked for (m).
    private const float SightCornerIn = .8f, SightOwnRoom = .3f, SightLodBias = 2f, SightModelFar = 250f;
    // A ladder's top start is this far over the top of its rails, so it lands on the floor there (m).
    private const float SightClimbOver = .3f;

    private Dictionary<Vector2Int, List<Vector3>> sightCells;
    private int sightEyes, sightPieces, sightReached, unseenModels;
    private readonly List<(float distance, Vector3 eye)> sightNear = new List<(float, Vector3)>();
    private readonly RaycastHit[] sightHits = new RaycastHit[32];

    // The eyes over the walk mesh loaded now (see the class). With no walk mesh, every face counts as seen (Seen).
    private void BuildSight()
    {
        sightCells = null;
        sightEyes = sightPieces = sightReached = 0;
        var mesh = NavMesh.CalculateTriangulation();
        int triangles = mesh.indices.Length / 3;
        if (triangles == 0) return;
        // Pieces: triangles that share a corner. Tiles repeat the corners on their borders, so corners match by place.
        var parent = new int[triangles];
        for (int t = 0; t < triangles; t++) parent[t] = t;
        var owner = new Dictionary<Vector3Int, int>();
        for (int t = 0; t < triangles; t++)
            for (int k = 0; k < 3; k++)
            {
                Vector3 v = mesh.vertices[mesh.indices[t * 3 + k]];
                var key = new Vector3Int(Mathf.RoundToInt(v.x * SightWeld), Mathf.RoundToInt(v.y * SightWeld), Mathf.RoundToInt(v.z * SightWeld));
                if (owner.TryGetValue(key, out int other)) Join(parent, t, other);
                else owner[key] = t;
            }
        // A piece is reached when a path from a start ends on it (drops and hops count: the paths take the links).
        var starts = SightStarts();
        var reached = new Dictionary<int, bool>();
        var path = new NavMeshPath();
        for (int t = 0; t < triangles; t++)
        {
            int piece = Find(parent, t);
            if (reached.ContainsKey(piece)) continue;
            Vector3 middle = Corner(mesh, t, 0) / 3f + Corner(mesh, t, 1) / 3f + Corner(mesh, t, 2) / 3f;
            bool reach = starts.Count == 0;
            starts.Sort((p, q) => (p - middle).sqrMagnitude.CompareTo((q - middle).sqrMagnitude));
            foreach (var start in starts)
                if (NavMesh.CalculatePath(start, middle, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    reach = true;
                    break;
                }
            reached[piece] = reach;
            sightPieces++;
            if (reach) sightReached++;
        }
        sightCells = new Dictionary<Vector2Int, List<Vector3>>();
        for (int t = 0; t < triangles; t++)
            if (reached[Find(parent, t)]) Eyes(Corner(mesh, t, 0), Corner(mesh, t, 1), Corner(mesh, t, 2), 0);
    }

    // Where a path may start: the player's starts and the progress spots, and where players climb or ride to what no
    // path goes up to (along a ladder, both ends of a zipline, a swing bar), put on the walk mesh.
    private List<Vector3> SightStarts()
    {
        var found = new List<Vector3>();
        foreach (var start in Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (start.gameObject.scene == scene) found.Add(start.transform.position);
        foreach (var spot in spots)
            if (spot != null) found.Add(spot.position);
        // A ladder's foot and the floor at its top (LadderFit has fitted its height to that floor by now): only there, or
        // a point past its top would land on whatever is above (the hub's roof, and from there the yard outside).
        foreach (var ladder in Object.FindObjectsByType<Ladder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ladder.gameObject.scene == scene)
            {
                found.Add(ladder.transform.position);
                found.Add(ladder.transform.position + ladder.transform.up * ladder.Height + Vector3.up * SightClimbOver);
            }
        foreach (var line in Object.FindObjectsByType<Zipline>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (line.gameObject.scene != scene) continue;
            found.Add(line.transform.position);
            var end = new UnityEditor.SerializedObject(line).FindProperty("end");
            if (end != null) found.Add(line.transform.position + end.vector3Value);
        }
        foreach (var bar in Object.FindObjectsByType<SwingBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (bar.gameObject.scene == scene) found.Add(bar.transform.position);
        var on = new List<Vector3>();
        foreach (var point in found)
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, SightStartReach, NavMesh.AllAreas)) on.Add(hit.position);
        return on;
    }

    // Eyes over a triangle of the walk mesh: split in four until each part is about SightSpacing across, an eye over each.
    private void Eyes(Vector3 a, Vector3 b, Vector3 c, int depth)
    {
        if (Vector3.Cross(b - a, c - a).magnitude * .5f > SightSpacing * SightSpacing && depth < 10)
        {
            Vector3 ab = (a + b) * .5f, bc = (b + c) * .5f, ca = (c + a) * .5f;
            Eyes(a, ab, ca, depth + 1);
            Eyes(ab, b, bc, depth + 1);
            Eyes(ca, bc, c, depth + 1);
            Eyes(ab, bc, ca, depth + 1);
            return;
        }
        Vector3 eye = (a + b + c) / 3f + Vector3.up * SightEye;
        var key = SightKey(eye);
        if (!sightCells.TryGetValue(key, out var list)) sightCells[key] = list = new List<Vector3>();
        list.Add(eye);
        sightEyes++;
    }

    /// <summary>
    /// Whether a player's eye sees <paramref name="point"/> on a face looking along <paramref name="normal"/>: one within
    /// <paramref name="far"/> m, in front of the face and not side-on, with nothing solid between. True when the scene
    /// has no eyes (no walk mesh), so a map is never left bare by a failed bake.
    /// </summary>
    private bool Seen(Vector3 point, Vector3 normal, float far)
    {
        if (sightCells == null) return true;
        sightNear.Clear();
        int reach = Mathf.CeilToInt(far / SightCell);
        var home = SightKey(point);
        for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (!sightCells.TryGetValue(new Vector2Int(home.x + dx, home.y + dz), out var eyes)) continue;
                foreach (var eye in eyes)
                {
                    Vector3 to = eye - point;
                    float distance = to.magnitude;
                    if (distance > far || Vector3.Dot(to, normal) < SightGrazing * distance) continue;
                    sightNear.Add((distance, eye));
                }
            }
        sightNear.Sort((p, q) => p.distance.CompareTo(q.distance));
        Vector3 from = point + normal * .05f;
        for (int i = 0; i < sightNear.Count && i < SightTries; i++)
            if (!Blocked(from, sightNear[i].eye, null))
                return true;
        return false;
    }

    /// <summary>
    /// Whether a player's eye sees any of <paramref name="box"/> (its middle or a corner drawn a little in), from within
    /// <paramref name="far"/> m of its middle, with nothing solid between but what lies in the box itself (a model's own
    /// collision box). True when the scene has no eyes.
    /// </summary>
    private bool SeenBox(Bounds box, float far)
    {
        if (sightCells == null) return true;
        sightNear.Clear();
        int reach = Mathf.CeilToInt((far + box.extents.magnitude) / SightCell);
        var home = SightKey(box.center);
        for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (!sightCells.TryGetValue(new Vector2Int(home.x + dx, home.y + dz), out var eyes)) continue;
                foreach (var eye in eyes)
                {
                    float distance = Vector3.Distance(eye, box.center);
                    if (distance <= far) sightNear.Add((distance, eye));
                }
            }
        sightNear.Sort((p, q) => p.distance.CompareTo(q.distance));
        var own = box;
        own.Expand(SightOwnRoom);
        for (int i = 0; i < sightNear.Count && i < SightTries; i++)
            for (int k = -1; k < 8; k++)
            {
                Vector3 spot = k < 0 ? box.center : box.center + Vector3.Scale(box.extents * SightCornerIn,
                    new Vector3(k % 2 == 0 ? -1f : 1f, k / 2 % 2 == 0 ? -1f : 1f, k / 4 == 0 ? -1f : 1f));
                if (!Blocked(sightNear[i].eye, spot, own)) return true;
            }
        return false;
    }

    // Whether something solid stands between two points. Glass is seen through (SeeThrough), and so are gates, doors and
    // the hub's shutter, which open in play; and anything inside allow (a model's own collision box).
    private bool Blocked(Vector3 from, Vector3 to, Bounds? allow)
    {
        Vector3 way = to - from;
        float length = way.magnitude;
        if (length < .001f) return false;
        int hits = Physics.RaycastNonAlloc(from, way / length, sightHits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            var solid = sightHits[i].collider;
            if (allow.HasValue && allow.Value.Contains(sightHits[i].point)) continue;
            if (seeThrough.Contains(solid.gameObject) || MovesWithGroup(solid.gameObject) || solid.GetComponentInParent<HubStation>() != null) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Map optimisation (the user, 2026-11-14: "맵 최적화"): the dressing's models that no player's eye can see from
    /// anywhere they can get to, within the distance at which the model's LOD would hide it anyway (Tables/MapLods), are
    /// taken away: props behind the map's edge walls, in rooms nobody enters and no window shows. Their collision boxes
    /// stay, so play is the same.
    /// </summary>
    private void CullUnseenDressing()
    {
        if (dressing == null || sightCells == null) return;
        // A model's screen share at distance d is its size over the screen's height there (2 d tan 30°), doubled by the
        // quality's LOD bias: it is hidden past size × reach.
        float reach = SightLodBias / (LodCull() * 2f * Mathf.Tan(30f * Mathf.Deg2Rad));
        var copies = new List<GameObject>();
        foreach (Transform copy in dressing) copies.Add(copy.gameObject);
        foreach (var copy in copies)
        {
            var box = WorldBounds(copy);
            float size = Mathf.Max(box.size.x, box.size.y, box.size.z);
            if (size <= 0f || SeenBox(box, Mathf.Min(SightModelFar, size * reach))) continue;
            Object.DestroyImmediate(copy);
            artPlaced--;
            unseenModels++;
        }
    }

    // For a scene that bakes no walk mesh of its own (the hub): one baked from all its colliders (the shut shutter keeps
    // the street out) only to find the eyes, given back to be dropped once they are found (DropSightMesh).
    private (NavMeshDataInstance instance, NavMeshData data) SightMesh()
    {
        Physics.SyncTransforms();
        var bounds = new Bounds();
        bool any = false;
        foreach (var solid in terrain.GetComponentsInChildren<Collider>())
        {
            if (!solid.enabled || solid.isTrigger) continue;
            if (any) bounds.Encapsulate(solid.bounds);
            else { bounds = solid.bounds; any = true; }
        }
        if (!any) return (default, null);
        bounds.Expand(4f);
        var sources = new List<NavMeshBuildSource>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, true,
            new List<NavMeshBuildMarkup>(), false, sources);
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
        return data == null ? (default, null) : (NavMesh.AddNavMeshData(data), data);
    }

    private static void DropSightMesh((NavMeshDataInstance instance, NavMeshData data) baked)
    {
        if (baked.instance.valid) NavMesh.RemoveNavMeshData(baked.instance);
        if (baked.data != null) Object.DestroyImmediate(baked.data);
    }

    private static Vector2Int SightKey(Vector3 at) => new Vector2Int(Mathf.FloorToInt(at.x / SightCell), Mathf.FloorToInt(at.z / SightCell));

    private static Vector3 Corner(NavMeshTriangulation mesh, int triangle, int k) => mesh.vertices[mesh.indices[triangle * 3 + k]];

    private static int Find(int[] parent, int t)
    {
        while (parent[t] != t)
        {
            parent[t] = parent[parent[t]];
            t = parent[t];
        }
        return t;
    }

    private static void Join(int[] parent, int a, int b)
    {
        a = Find(parent, a);
        b = Find(parent, b);
        if (a != b) parent[a] = b;
    }

    // For the build's report.
    private string SightReport()
        => sightCells == null ? "" : "\n보이는 곳: 걸을 수 있는 곳 중 출발점·진행 자리에서 닿는 조각 " + sightReached + "/" + sightPieces +
                                     "개 위 눈높이 " + sightEyes + "곳에서 보이는 면에만 데칼을 붙이고, 아무도 못 보는 꾸밈 모델 " + unseenModels + "개를 뺐어요.";
}
