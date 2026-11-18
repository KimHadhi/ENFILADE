using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What a build checks about its own map, so nobody has to find it by playing (the user, 2026-11-18, on a list of map
/// bugs each found by hand: props through walls, an objective that rang behind a locked door, walls one walks through,
/// car hitboxes bigger than the car). Each check adds its lines to one file, Logs/MapAudit/&lt;scene&gt;.txt, written as
/// the build ends, and a line to the closing window that names only what needs the layout or a table changed. A later
/// session reads the file ("맵 검사 결과 봐 줘"); the same things can be measured again from the saved scene without
/// Unity (Tools/Maps).
/// The checks: where the solid props stood (GreyboxBuilder.Standing), whether every Reach step's spot is a place one
/// can get to, and whether what is seen blocks: no collider without its mesh, no part of a bought building's shell
/// that is drawn at walking height with nothing solid behind it, no wall a walking body passes from its back, and
/// how much of a prop's box a shot now passes because the prop's seen shape is not there (below). And whether a
/// cage's crowd has the floor to stand on (CrowdReport), and whether the walk mesh joins each stretch of the mission's
/// route and every spawn point to it (MeasureRoute).
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string AuditFolder = "Logs/MapAudit";
    // A Reach spot whose chest-high point has something of the map within this is buried (m), as CampaignRun finds it.
    private const float ReachProbe = .05f;

    // Seen but not blocking: the shell is sampled about this far apart (m), in this band of height over the floor under
    // the sample (m, knee to head: where a walking body is), and a sample is covered when something solid is this near
    // it (m). A walking body's capsule for the cast from behind (m): its radius, its lowest and highest point over the
    // feet (the lowest clears a step), how far it is sent, and how much nearer a back face must stop it to count.
    private const float ShellStep = .5f, ShellLow = .25f, ShellHigh = 1.85f, ShellNear = .25f, ShellProud = .02f;
    private const float BodyRadius = .28f, BodyLow = .65f, BodyHigh = 1.45f, BodyReach = 10f, BodySlack = .3f;
    private const int BodyWays = 16, ShellNamed = 12;
    // Shots at a box whose prop is judged by its seen shape: rays this far apart over each of its sides and its top (m).
    private const float ShotGrid = .25f;
    // A cage's crowd: its circle is sampled this far apart (m); a sample is a place to stand when the walk mesh is this
    // near under it (m) and within this of the cage's floor (m, as CampaignHorde.PlaceCaged takes it); two of the
    // crowd stand no nearer than this (m, CampaignHorde.CagedApart).
    private const float CrowdGrid = .25f, CrowdUnder = .15f, CrowdFloor = 1.2f, CrowdApart = 1.1f;
    // The route: how far the game looks for the walk mesh round a spot and round a spawn point (m: SpawnRoute.SpotSearch,
    // CampaignHorde.PointSearch), and how far this looks for it at a hop's end.
    private const float SpotSearch = 3f, PointSearch = 2f, HopSearch = 1f;

    private readonly StringBuilder audit = new StringBuilder();
    private bool auditToRead;
    // What MeasureRoute found, kept until the file is written.
    private readonly StringBuilder routeAudit = new StringBuilder();
    private string routeLine = "";
    private bool routeToRead;

    // Every check's line for the closing window, and the file. Called once, as the build ends.
    private string AuditReport()
    {
        string line = StandReport() + PointReport() + RouteReport() + ReachReport() + ColliderReport() + ShellReport() + BackReport() + ShotReport() + CrowdReport();
        if (audit.Length == 0) return line;
        string path = AuditFolder + "/" + sceneName + ".txt";
        try
        {
            Directory.CreateDirectory(AuditFolder);
            File.WriteAllText(path, "# " + sceneName + " 맵 검사 (지을 때마다 다시 씀: GreyboxBuilder.Audit)\n" + audit, new UTF8Encoding(false));
        }
        catch (IOException error)
        {
            Debug.LogWarning("[Greybox] " + sceneName + ": " + path + " could not be written (" + error.Message + ").");
            return line;
        }
        return line + (auditToRead ? " 자세한 것은 " + path + "." : "");
    }

    // The route and the walk mesh. A way the players go that the walk mesh does not join is a way no zombie follows
    // them on and none comes at them by, and nothing in play says so: the horde just does not arrive. It was found by
    // reading the baked meshes after the fact (the spawn session, item 20: each storey of chapter 1's flats an island,
    // chapter 3's river tunnel four of them). So every build asks the walk mesh it has just baked, as the game asks
    // it (SpawnRoute): for each step, whether a walker gets from the spot before it to its own, and back; for each
    // spawn point, whether it stands on the walk mesh and whether it can get to a place of the route, or lies on
    // ground the squad drops to. What the layout says is meant (NotWalked: a gap to jump, a cable, a climb) is written
    // down as meant; anything else that is cut goes to the closing window. Measured right after the bake, while the
    // mesh is as baked: a gate that is shut or a variant's blocker carves it only once the engine runs. The same
    // from the saved scene without Unity, with what stands in each break: Tools/Maps/walk_joins.py.
    private void MeasureRoute()
    {
        routeAudit.Clear();
        routeLine = "";
        routeToRead = false;
        if (!AddsCampaign) return;
        var text = routeAudit;
        text.Append("\n## 경로와 걷기 메시 (단계마다 앞 자리에서 그 자리까지 좀비가 걸어갈 수 있는지, 스폰 지점이 경로에 닿는지)\n");
        text.Append("# 굽는 몸: 반지름 ").Append(WalkRadius.ToString("0.00")).Append(" m, 복셀 ").Append(WalkVoxel.ToString("0.00"))
            .Append(" m, 걸음 ").Append(StepHeight.ToString("0.00")).Append(" m. 넘는 길 ").Append(hops.Count).Append("개\n");
        if (NavMesh.CalculateTriangulation().vertices.Length == 0)
        {
            text.Append("# 못 읽음: 걷기 메시가 없어 재지 못했어요\n");
            return;
        }
        var path = new NavMeshPath();
        var cut = new List<string>();
        var lost = new List<string>();
        var onRoute = new List<Vector3>();
        int joined = 0, oneWay = 0, meantCut = 0;
        Transform before = spots.Find(each => each != null && each.name == CampaignRun.StartSpot);
        string beforeName = CampaignRun.StartSpot;
        if (before != null && Stands(before.position, SpotSearch, out Vector3 start)) onRoute.Add(start);
        foreach (var step in CampaignStep.For(sceneName))
        {
            var spot = spots.Find(each => each != null && each.name == step.Spot);
            // A step whose spot is not in the map is named with the Reach steps (ReachReport); the route goes on past it.
            if (spot == null) continue;
            bool toOn = Stands(spot.position, SpotSearch, out Vector3 b);
            if (toOn) onRoute.Add(b);
            if (before == null)
            {
                before = spot;
                beforeName = step.Spot;
                continue;
            }
            string label = step.Id + " " + beforeName + " → " + step.Spot;
            notWalked.TryGetValue(step.Id, out string meant);
            bool fromOn = Stands(before.position, SpotSearch, out Vector3 a);
            if (!fromOn || !toOn)
            {
                text.Append(meant != null ? "설계 " : "끊김 ").Append(label).Append(": 자리 '").Append(fromOn ? step.Spot : beforeName).Append("' 둘레 ")
                    .Append(SpotSearch.ToString("0.#")).Append(" m 안에 걷기 메시가 없어요").Append(meant != null ? ". " + meant : "").Append('\n');
                if (meant == null) cut.Add(step.Id);
                else meantCut++;
            }
            else if ((a - b).sqrMagnitude < .0025f) joined++;
            else
            {
                bool there = Joins(a, b, path), back = Joins(b, a, path);
                if (there && back)
                {
                    joined++;
                    text.Append("이어짐 ").Append(label).Append('\n');
                    if (meant != null) text.Append("    # 배치에는 걷지 않는 구간이라고 적혀 있어요(").Append(meant).Append("). 이어져 있으니 그 줄(NotWalked)을 지우세요\n");
                }
                else if (there)
                {
                    oneWay++;
                    text.Append("앞으로만 ").Append(label).Append(": 내려뛰는 링크로 이어지고 되돌아오는 길은 없어요\n");
                }
                else
                {
                    // Where the walk stops, and how far it still is.
                    NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path);
                    var corners = path.corners;
                    Vector3 stop = corners.Length > 0 ? corners[corners.Length - 1] : a;
                    text.Append(meant != null ? "설계 " : "끊김 ").Append(label).Append(": 메시로는 (").Append(Format(stop)).Append(")까지 가고, 남은 직선 ")
                        .Append(Vector3.Distance(stop, b).ToString("0.0")).Append(" m").Append(back ? " (거꾸로는 이어져요)" : "")
                        .Append(meant != null ? ". " + meant : "").Append('\n');
                    if (meant == null) cut.Add(step.Id + "(" + beforeName + "→" + step.Spot + ")");
                    else meantCut++;
                }
            }
            before = spot;
            beforeName = step.Spot;
        }
        int served = 0, dropped = 0, meantLost = 0;
        foreach (var point in enemySpawns)
        {
            if (point == null) continue;
            notWalked.TryGetValue(point.name, out string meant);
            if (!Stands(point.position, PointSearch, out Vector3 p))
            {
                text.Append(meant != null ? "설계 " : "메시 밖 ").Append("지점 ").Append(point.name).Append(" (").Append(Format(point.position)).Append("): ")
                    .Append(PointSearch.ToString("0.#")).Append(" m 안에 걷기 메시가 없어요").Append(meant != null ? ". " + meant : "").Append('\n');
                if (meant == null) lost.Add(point.name);
                else meantLost++;
                continue;
            }
            // The nearest places of the route first: most points walk to the nearest.
            onRoute.Sort((one, other) => (one - p).sqrMagnitude.CompareTo((other - p).sqrMagnitude));
            bool reaches = false, reached = false;
            foreach (var place in onRoute) if (Joins(p, place, path)) { reaches = true; break; }
            if (!reaches) foreach (var place in onRoute) if (Joins(place, p, path)) { reached = true; break; }
            if (reaches) served++;
            else if (reached)
            {
                dropped++;
                text.Append("내려온 땅 지점 ").Append(point.name).Append(": 경로의 자리로 올라가는 길은 없고, 분대가 내려오면 닿는 땅이에요\n");
            }
            else
            {
                text.Append(meant != null ? "설계 " : "닿지 않음 ").Append("지점 ").Append(point.name).Append(" (").Append(Format(point.position))
                    .Append("): 경로의 어느 자리와도 이어지지 않아요").Append(meant != null ? ". " + meant : "").Append('\n');
                if (meant == null) lost.Add(point.name);
                else meantLost++;
            }
        }
        text.Append("# 합계: 구간 이어짐 ").Append(joined).Append(", 앞으로만 ").Append(oneWay).Append(", 설계로 끊김 ").Append(meantCut).Append(", 끊김 ").Append(cut.Count)
            .Append(" | 스폰 지점 ").Append(enemySpawns.Count).Append("개: 경로로 감 ").Append(served).Append(", 내려온 땅 ").Append(dropped)
            .Append(", 설계 ").Append(meantLost).Append(", 닿지 않음 ").Append(lost.Count).Append('\n');
        if (cut.Count == 0 && lost.Count == 0) return;
        routeToRead = true;
        if (cut.Count > 0) routeLine += "\n좀비가 걸어갈 수 없는 구간 " + cut.Count + "곳: " + string.Join(", ", cut) + ".";
        if (lost.Count > 0) routeLine += "\n경로에 닿지 않는 스폰 지점 " + lost.Count + "개: " + string.Join(", ", lost) + ".";
    }

    private string RouteReport()
    {
        if (routeAudit.Length == 0) return "";
        audit.Append(routeAudit);
        if (routeToRead) auditToRead = true;
        return routeLine;
    }

    private static bool Stands(Vector3 at, float within, out Vector3 on)
    {
        bool found = NavMesh.SamplePosition(at, out NavMeshHit hit, within, NavMesh.AllAreas);
        on = found ? hit.position : at;
        return found;
    }

    private static bool Walks(Vector3 from, Vector3 to, NavMeshPath path)
        => NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;

    // Whether a walker gets from one place on the walk mesh to another: by the mesh and the links on it, or, failing
    // that, by way of the hops this build laid, each tried as a step of the way (a link laid in the editor may not be
    // on the walk mesh until the engine has run, and the measure must not wait for that).
    private bool Joins(Vector3 from, Vector3 to, NavMeshPath path)
    {
        if (Walks(from, to, path)) return true;
        if (hops.Count == 0) return false;
        var reached = new List<Vector3> { from };
        var taken = new HashSet<int>();
        for (int at = 0; at < reached.Count; at++)
            for (int i = 0; i < hops.Count; i++)
            {
                var hop = hops[i];
                if (hop == null) continue;
                for (int way = 0; way < 2; way++)
                {
                    if (taken.Contains(i * 2 + way)) continue;
                    if (way == 1 && !hop.bidirectional) continue;
                    Vector3 enter = hop.transform.TransformPoint(way == 0 ? hop.startPoint : hop.endPoint);
                    Vector3 leave = hop.transform.TransformPoint(way == 0 ? hop.endPoint : hop.startPoint);
                    if (!Stands(enter, HopSearch, out enter) || !Stands(leave, HopSearch, out leave) || !Walks(reached[at], enter, path)) continue;
                    taken.Add(i * 2 + way);
                    if (Walks(leave, to, path)) return true;
                    reached.Add(leave);
                }
            }
        return false;
    }

    // A cage's crowd (GimmickCage) is stood by the run on the walk mesh round the middle of its cage, when the stretch
    // it belongs to begins: a cage whose floor the bake left without walk mesh, or one too small for those it holds,
    // would stand empty or half full and say nothing, far into a chapter. So the build asks every cage store for its
    // crowds, as the run does, and counts the places the baked walk mesh gives each: samples of its circle with walk
    // mesh under them, on the cage's floor and inside its box, and how many of those lie far enough apart for a body
    // each. Fewer than it holds is named in the closing window. The same from the saved scene without Unity:
    // Tools/Maps/cage_crowd.py, which stands the crowd the game's way many times over.
    private string CrowdReport()
    {
        var crowds = new List<GimmickCage.Crowd>();
        var steps = new List<string>();
        foreach (var each in switches)
        {
            if (!(each is GimmickCage store)) continue;
            int before = crowds.Count;
            store.AddCrowds(crowds);
            for (int i = before; i < crowds.Count; i++) steps.Add(store.CrowdStep);
        }
        if (crowds.Count == 0) return "";
        audit.Append("\n## 철창에 갇힌 무리 (걷기 메시 위에 설 자리가 있어야 선다)\n");
        // Nothing to measure against when the walk mesh is not there to ask (its bake failed): said, not counted as empty.
        if (NavMesh.CalculateTriangulation().vertices.Length == 0)
        {
            audit.Append("# 못 읽음: 걷기 메시가 없어 재지 못했어요\n");
            return "";
        }
        int lacking = 0, unset = 0, wrong = 0;
        var stood = new List<Vector3>();
        for (int c = 0; c < crowds.Count; c++)
        {
            var crowd = crowds[c];
            int places = 0, sampled = 0;
            stood.Clear();
            int reach = Mathf.CeilToInt(crowd.Radius / CrowdGrid);
            for (int ix = -reach; ix <= reach; ix++)
                for (int iz = -reach; iz <= reach; iz++)
                {
                    var at = crowd.Middle + new Vector3(ix * CrowdGrid, 0f, iz * CrowdGrid);
                    if ((at - crowd.Middle).sqrMagnitude > crowd.Radius * crowd.Radius + 1e-4f) continue;
                    sampled++;
                    if (!NavMesh.SamplePosition(at, out NavMeshHit hit, CrowdFloor, NavMesh.AllAreas)) continue;
                    Vector3 ground = hit.position;
                    if (Mathf.Abs(ground.x - at.x) > CrowdUnder || Mathf.Abs(ground.z - at.z) > CrowdUnder) continue;
                    if (Mathf.Abs(ground.y - crowd.Middle.y) > CrowdFloor) continue;
                    if (Mathf.Abs(ground.x - crowd.Cage.center.x) > crowd.Cage.extents.x || Mathf.Abs(ground.z - crowd.Cage.center.z) > crowd.Cage.extents.z) continue;
                    places++;
                    bool apart = true;
                    foreach (var other in stood)
                        if ((other - ground).sqrMagnitude < CrowdApart * CrowdApart) { apart = false; break; }
                    if (apart) stood.Add(ground);
                }
            bool stepless = string.IsNullOrEmpty(steps[c]);
            bool few = stood.Count < crowd.Count;
            if (stepless) unset++;
            if (few) lacking++;
            if (few || stepless) wrong++;
            audit.Append(few || stepless ? "모자람 " : "섬 ").Append("칸 (").Append(Format(crowd.Middle)).Append("): ").Append(crowd.Count)
                .Append(" 가둠, 놓이는 구간 ").Append(stepless ? "(없음: 게임이 세우지 않아요)" : steps[c])
                .Append(", 반경 ").Append(crowd.Radius.ToString("0.00")).Append(" m, 걷기 메시 위의 자리 ").Append(places).Append('/').Append(sampled)
                .Append(", ").Append(CrowdApart.ToString("0.0")).Append(" m씩 떨어져 설 수 있는 수 ").Append(stood.Count).Append('\n');
        }
        audit.Append("# 합계: 갇힌 무리 ").Append(crowds.Count).Append("칸, 설 자리가 모자란 칸 ").Append(lacking).Append(", 구간이 없는 칸 ").Append(unset).Append('\n');
        if (wrong == 0) return "";
        auditToRead = true;
        return "\n철창에 갇힌 무리가 다 서지 못하는 칸 " + wrong + "곳(걷기 메시 위에 설 자리가 모자라거나 놓이는 구간이 없어요).";
    }

    // The campaign's Reach steps of this map: a step is done when someone is within its radius of its spot with nothing
    // of the building between their chest and the spot's (CampaignRun.Reach). A spot whose own chest-high point is
    // inside a wall or a prop can be seen from nowhere; the game then falls back to the radius alone for it, which is
    // the ball through walls the rule was made to end. So the build names every such spot: move it in the layout.
    private string ReachReport()
    {
        if (!AddsCampaign) return "";
        Physics.SyncTransforms();
        var buried = new List<string>();
        int looked = 0;
        audit.Append("\n## 도착 자리 (Reach 단계: 반경 안이고 그 자리와 사이에 벽·문·바닥이 없어야 도착)\n");
        foreach (var step in CampaignStep.For(sceneName))
        {
            if (step.Kind != CampaignStepKind.Reach) continue;
            var spot = spots.Find(each => each != null && each.name == step.Spot);
            if (spot == null)
            {
                audit.Append("없음 ").Append(step.Id).Append(": 진행 자리 '").Append(step.Spot).Append("'가 이 맵에 없어요(게임은 이 단계를 건너뛰어요)\n");
                continue;
            }
            looked++;
            Vector3 chest = spot.position + Vector3.up * CampaignRun.ReachChest;
            string inside = null;
            foreach (var solid in Physics.OverlapSphere(chest, ReachProbe, ~0, QueryTriggerInteraction.Ignore))
                if (solid.attachedRigidbody == null) { inside = solid.name; break; }
            if (inside == null) continue;
            buried.Add(step.Spot);
            audit.Append("묻힘 ").Append(step.Id).Append(" '").Append(step.Spot).Append("' (").Append(Format(spot.position)).Append("): 가슴 높이가 ")
                .Append(inside).Append(" 속이에요. 자리를 옮겨야 해요\n");
        }
        audit.Append("# 합계: 도착 자리 ").Append(looked).Append("곳, 묻힌 곳 ").Append(buried.Count).Append('\n');
        if (buried.Count == 0) return "";
        auditToRead = true;
        return "\n도착 자리가 벽이나 소품 속에 묻힌 곳 " + buried.Count + "곳: " + string.Join(", ", buried) + " (자리를 옮겨야 해요).";
    }

    // A mesh collider with no mesh blocks nothing and says nothing: the terminal's walls stood so for days (a build
    // step had destroyed the mesh its collider held, GreyboxBuilder.PackScene). Every one in the scene is named.
    private string ColliderReport()
    {
        var empty = new List<string>();
        int looked = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var solid in root.GetComponentsInChildren<MeshCollider>(true))
            {
                looked++;
                if (solid.sharedMesh == null) empty.Add(PathOf(solid.transform));
            }
        audit.Append("\n## 메시 콜라이더 (메시가 없으면 아무것도 막지 않는다)\n");
        foreach (string name in empty) audit.Append("없음 ").Append(name).Append(": 메시가 없어요\n");
        audit.Append("# 합계: 메시 콜라이더 ").Append(looked).Append("개, 메시 없는 것 ").Append(empty.Count).Append('\n');
        if (empty.Count == 0) return "";
        auditToRead = true;
        return "\n메시가 없는 메시 콜라이더 " + empty.Count + "개(아무것도 막지 않아요): " + string.Join(", ", empty.GetRange(0, Mathf.Min(empty.Count, 6))) + ".";
    }

    // What is seen blocks (the user, 2026-11-18: "4장 벽 뚫림(백룸 이탈)"). A bought building's shell that one sees at
    // walking height, its walls, its glass and the podium under its upper floor, is sampled over every upright face,
    // and a sample with nothing solid at it is a place one walks through something drawn. Measured by the build with
    // the game's own physics, so it holds for any building a mod brings; the same can be read from the saved meshes
    // without Unity (Tools/Maps/shell_cover.py).
    private string ShellReport()
    {
        if (pack == null) return "";
        Physics.SyncTransforms();
        float seen = 0f, bare = 0f;
        var byPart = new Dictionary<string, float>();
        var byPlace = new Dictionary<Vector3Int, float>();
        var corners = new List<Vector3>();
        var indices = new List<int>();
        foreach (var filter in pack.GetComponentsInChildren<MeshFilter>(true))
        {
            var part = filter.gameObject;
            var mesh = filter.sharedMesh;
            bool podium = part.name == "받침";
            if (mesh == null || !mesh.isReadable || !filter.TryGetComponent(out MeshRenderer shown) || !shown.enabled) continue;
            if (!podium && !Named(part, packParts.Walls) && !Named(part, packParts.Glass)) continue;
            var toWorld = filter.transform.localToWorldMatrix;
            mesh.GetVertices(corners);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                mesh.GetTriangles(indices, s);
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    Vector3 a = toWorld.MultiplyPoint3x4(corners[indices[i]]), b = toWorld.MultiplyPoint3x4(corners[indices[i + 1]]),
                        c = toWorld.MultiplyPoint3x4(corners[indices[i + 2]]);
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    float twice = normal.magnitude;
                    // Upright faces only: a floor or a ceiling is not walked through.
                    if (twice < 1e-6f || Mathf.Abs(normal.y) / twice > .5f) continue;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(twice) / ShellStep));
                    float each = twice * .5f / (steps * steps);
                    for (int u = 0; u < steps; u++)
                        for (int v = 0; v < steps - u; v++)
                            for (int half = 0; half < (u + v + 1 < steps ? 2 : 1); half++)
                            {
                                float fu = (u + (half + 1) / 3f) / steps, fv = (v + (half + 1) / 3f) / steps;
                                // A hair in front of the face, so the look down for a floor never starts on the face itself.
                                Vector3 at = a + (b - a) * fu + (c - a) * fv + normal * (ShellProud / twice);
                                // Only where a body walks: over something solid, knee to head.
                                if (!Physics.Raycast(at, Vector3.down, out RaycastHit floor, ShellHigh, ~0, QueryTriggerInteraction.Ignore)
                                    || floor.distance < ShellLow) continue;
                                seen += each;
                                if (Physics.CheckSphere(at, ShellNear, ~0, QueryTriggerInteraction.Ignore)) continue;
                                bare += each;
                                byPart[part.name] = (byPart.TryGetValue(part.name, out float had) ? had : 0f) + each;
                                var place = new Vector3Int(Mathf.RoundToInt(at.x * .5f) * 2, Mathf.RoundToInt(at.y), Mathf.RoundToInt(at.z * .5f) * 2);
                                byPlace[place] = (byPlace.TryGetValue(place, out float there) ? there : 0f) + each;
                            }
                }
            }
        }
        audit.Append("\n## 보이는데 막지 않는 껍데기 (팩 건물의 벽·유리·받침, 걷는 높이)\n");
        foreach (var pair in byPart) audit.Append("부품 ").Append(pair.Key).Append(": ").Append(pair.Value.ToString("0.0")).Append(" m²\n");
        var places = new List<KeyValuePair<Vector3Int, float>>(byPlace);
        places.Sort((x, y) => y.Value.CompareTo(x.Value));
        for (int i = 0; i < places.Count && i < ShellNamed * 4; i++)
            audit.Append("자리 (").Append(places[i].Key.x).Append(", ").Append(places[i].Key.y).Append(", ").Append(places[i].Key.z).Append("): ")
                .Append(places[i].Value.ToString("0.0")).Append(" m²\n");
        audit.Append("# 합계: 걷는 높이에서 보이는 껍데기 ").Append(seen.ToString("0")).Append(" m², 그중 막지 않는 곳 ").Append(bare.ToString("0.0")).Append(" m²\n");
        if (bare < 1f) return "\n건물 껍데기(벽·유리·받침): 걷는 높이의 " + seen.ToString("0") + " m²가 모두 막아요.";
        auditToRead = true;
        return "\n건물 껍데기 중 보이는데 막지 않는 곳 " + bare.ToString("0.0") + " m² (걷는 높이의 " + seen.ToString("0") + " m² 중): 그리로 걸어 나갈 수 있어요.";
    }

    // A wall one passes from its back. A mesh collider stops a body only from its front, so a wall with no collision
    // on its other side lets whoever is behind it walk in, and whoever is pushed past it stay out. From every place a
    // player can stand (the eyes over the reachable walk mesh, GreyboxBuilder.Sight) a walking body is sent each way
    // twice, as the game casts it (fronts only) and with back faces counted: where a back face would have stopped it
    // sooner, the body stands behind something solid on one side only.
    private string BackReport()
    {
        if (sightCells == null) return "";
        Physics.SyncTransforms();
        bool before = Physics.queriesHitBackfaces;
        int sent = 0, through = 0;
        var byPlace = new Dictionary<Vector3Int, int>();
        var byName = new Dictionary<string, int>();
        try
        {
            foreach (var cell in sightCells.Values)
                foreach (var eye in cell)
                {
                    Vector3 feet = eye - Vector3.up * SightEye, low = feet + Vector3.up * BodyLow, high = feet + Vector3.up * BodyHigh;
                    for (int w = 0; w < BodyWays; w++)
                    {
                        Vector3 way = Quaternion.Euler(0f, w * 360f / BodyWays, 0f) * Vector3.forward;
                        Physics.queriesHitBackfaces = false;
                        float fronts = Physics.CapsuleCast(low, high, BodyRadius, way, out RaycastHit met, BodyReach, ~0, QueryTriggerInteraction.Ignore)
                            ? met.distance : BodyReach;
                        Physics.queriesHitBackfaces = true;
                        if (!Physics.CapsuleCast(low, high, BodyRadius, way, out RaycastHit back, BodyReach, ~0, QueryTriggerInteraction.Ignore)) { sent++; continue; }
                        sent++;
                        if (back.distance + BodySlack >= fronts) continue;
                        through++;
                        Vector3 at = feet + way * back.distance;
                        var place = new Vector3Int(Mathf.RoundToInt(at.x * .5f) * 2, Mathf.RoundToInt(at.y), Mathf.RoundToInt(at.z * .5f) * 2);
                        byPlace[place] = (byPlace.TryGetValue(place, out int n) ? n : 0) + 1;
                        string name = back.collider != null ? back.collider.name : "?";
                        byName[name] = (byName.TryGetValue(name, out int m) ? m : 0) + 1;
                    }
                }
        }
        finally
        {
            Physics.queriesHitBackfaces = before;
        }
        audit.Append("\n## 뒤에서 지나가는 벽 (플레이어가 설 수 있는 자리에서 사방으로, 앞면만 볼 때와 뒷면도 볼 때)\n");
        foreach (var pair in byName) audit.Append("콜라이더 ").Append(pair.Key).Append(": ").Append(pair.Value).Append("번\n");
        var places = new List<KeyValuePair<Vector3Int, int>>(byPlace);
        places.Sort((x, y) => y.Value.CompareTo(x.Value));
        for (int i = 0; i < places.Count && i < ShellNamed * 4; i++)
            audit.Append("자리 (").Append(places[i].Key.x).Append(", ").Append(places[i].Key.y).Append(", ").Append(places[i].Key.z).Append("): ")
                .Append(places[i].Value).Append("번\n");
        audit.Append("# 합계: 보낸 몸 ").Append(sent).Append("번, 벽의 뒷면을 지나간 것 ").Append(through).Append("번\n");
        if (through == 0) return "";
        auditToRead = true;
        return "\n설 수 있는 자리에서 벽의 뒷면을 지나가는 곳이 " + byPlace.Count + "군데 있어요(한쪽만 막는 벽 뒤에 설 수 있어요).";
    }

    private static string PathOf(Transform part)
    {
        string path = part.name;
        for (var up = part.parent; up != null && up.parent != null; up = up.parent) path = up.name + "/" + path;
        return path;
    }

    // A prop judged by its seen shape (Tables/MapArt shots = mesh, ShotShape): how much of its box is air. Rays are
    // fired straight into each of its four sides and down through its top, a grid apart: every one meets the box, and
    // the share that meets no triangle of the model is the share of shots that used to stop in the air there (the
    // user, 2026-11-18: "차량 판정이 실제 외형보다 커서 허공을 쏴도 총알이 막히는"). A box all of whose rays miss has
    // a shape that is not where the box is: the build says so.
    private string ShotReport()
    {
        if (shotShapes.Count == 0 && shotUnread.Count == 0) return "";
        var byKind = new Dictionary<string, (int boxes, int rays, int missed)>();
        var empty = new List<string>();
        int rays = 0, missed = 0;
        foreach (var shape in shotShapes)
        {
            if (shape == null) continue;
            var box = shape.transform;
            Vector3 size = box.lossyScale;
            int fired = 0, miss = 0;
            // Each face of the box but its bottom: from just outside it, straight in, to the far face.
            for (int face = 0; face < 5; face++)
            {
                Vector3 inward = face == 0 ? Vector3.right : face == 1 ? Vector3.left : face == 2 ? Vector3.forward : face == 3 ? Vector3.back : Vector3.down;
                Vector3 across = face < 2 ? Vector3.forward : Vector3.right, up = face == 4 ? Vector3.forward : Vector3.up;
                float deep = Mathf.Abs(Vector3.Dot(size, inward)), wide = Mathf.Abs(Vector3.Dot(size, across)), tall = Mathf.Abs(Vector3.Dot(size, up));
                int columns = Mathf.Max(1, Mathf.RoundToInt(wide / ShotGrid)), rows = Mathf.Max(1, Mathf.RoundToInt(tall / ShotGrid));
                Vector3 way = box.TransformDirection(inward);
                for (int c = 0; c < columns; c++)
                    for (int r = 0; r < rows; r++)
                    {
                        // In the box's own space the cube runs from -0.5 to 0.5 on each side.
                        Vector3 local = -inward * .5f + across * ((c + .5f) / columns - .5f) + up * ((r + .5f) / rows - .5f);
                        Vector3 from = box.TransformPoint(local) - way * .01f;
                        fired++;
                        if (!shape.Cast(from, way, 0f, deep + .02f, out _, out _)) miss++;
                    }
            }
            rays += fired;
            missed += miss;
            string kind = box.name;
            byKind[kind] = byKind.TryGetValue(kind, out var had) ? (had.boxes + 1, had.rays + fired, had.missed + miss) : (1, fired, miss);
            if (fired > 0 && miss == fired) empty.Add(kind + " (" + Format(box.position) + ")");
        }
        audit.Append("\n## 총알이 맞는 모양 (MapArt shots = mesh: 몸은 상자에 막히고 총알은 모델 모양에 맞는다)\n");
        foreach (var pair in byKind)
            audit.Append("종류 ").Append(pair.Key).Append(": 상자 ").Append(pair.Value.boxes).Append("개, 상자에 맞는 광선 ").Append(pair.Value.rays)
                .Append("개 중 모델을 비껴가는 것 ").Append(pair.Value.missed).Append("개 (")
                .Append((pair.Value.rays > 0 ? 100f * pair.Value.missed / pair.Value.rays : 0f).ToString("0")).Append("%)\n");
        foreach (string name in empty) audit.Append("빗나감 ").Append(name).Append(": 모든 광선이 모델을 비껴가요(모양이 상자 자리에 없어요)\n");
        foreach (string name in shotUnread) audit.Append("못 읽음 ").Append(name).Append(": 읽을 메시가 없어 상자로 판정해요(스킨 모델이거나 메시가 없어요)\n");
        float share = rays > 0 ? 100f * missed / rays : 0f;
        audit.Append("# 합계: 모양으로 판정하는 상자 ").Append(shotShapes.Count).Append("개, 광선 ").Append(rays).Append("개 중 ").Append(missed)
            .Append("개(").Append(share.ToString("0")).Append("%)가 모델을 비껴감, 못 읽은 상자 ").Append(shotUnread.Count).Append("개\n");
        string line = "\n총알이 모양에 맞는 소품(차량) " + shotShapes.Count + "개: 상자에 맞는 광선의 " + share.ToString("0") + "%는 모델을 비껴가요(예전에는 허공에서 막히던 몫).";
        if (empty.Count > 0)
        {
            auditToRead = true;
            line += " 모양이 상자 자리에 없는 것 " + empty.Count + "개: " + string.Join(", ", empty.GetRange(0, Mathf.Min(empty.Count, 4))) + ".";
        }
        if (shotUnread.Count > 0) line += " 메시를 읽을 수 없어 상자로 판정하는 것 " + shotUnread.Count + "개.";
        return line;
    }
}
