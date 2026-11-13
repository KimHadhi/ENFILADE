using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// GreyboxBuilder: Small props (the user's memo 48: "좀비가 못 올라가는 작은 소품"). One part of the class; the rest is in GreyboxBuilder.cs and the files beside it.
public sealed partial class GreyboxBuilder
{
    // The zombies' walk mesh (agent type 0: radius .5 m, step .75 m) has nothing on a top narrower than about twice the
    // radius, and Unity dropped every walk region under 2 m², so a 2×2 m box's top had none either. ZombieDrop climbs
    // only onto walk mesh, so a player standing on such a prop was beyond every zombie. Now: small regions stay
    // (TopRegionArea); a narrow top a zombie could climb to gets a thin walk slab in the bake only (no collider, so
    // players and shots never meet it); a top low enough is within a zombie's arm from the floor anyway; and CheckProps
    // names in the console whatever is still out of reach, so the layout can be changed (lower it to ArmHeight, or
    // past AgentHeight so a slab can go on it).
    private const float AgentRadius = .5f, AgentHeight = 2f, StepHeight = .75f, PlayerHeight = 1.8f;

    // A zombie's arm reaches a player standing on a narrow top up to this high over the floor it stands on
    // (EnemyCombatAI.attackRange 2 m, feet to feet, from the edge of its walk mesh); ZombieDrop climbs at most
    // ClimbMost and only toward a player within ClimbNear across (m).
    private const float ArmHeight = 1.5f, ClimbMost = 4.5f, ClimbNear = 2.6f;

    // The smallest walk region kept (m²), the width of a narrow top's walk strip (m) and of its slab (m).
    private const float TopRegionArea = .25f, NarrowStrip = .5f, SlabThickness = .05f;

    // Tops larger than this are places (roofs, decks, floors) with their own ways up, not props (m²).
    private const float PropArea = 60f;

    // The walk mesh from the terrain's colliders as NavMeshSurface.BuildNavMesh makes it (the same modifiers, so gates
    // and doors stay out of it, and drop links), plus the narrow props' slabs. Kept in the surface, so the scene loads it
    // as before. Returns how many slabs went in.
    private int BakeWalkMesh(NavMeshSurface surface)
    {
        var markups = new List<NavMeshBuildMarkup>();
        foreach (var modifier in terrain.GetComponentsInChildren<NavMeshModifier>())
        {
            if (!modifier.isActiveAndEnabled || !modifier.AffectsAgentType(surface.agentTypeID)) continue;
            markups.Add(new NavMeshBuildMarkup
            {
                root = modifier.transform, overrideArea = modifier.overrideArea, area = modifier.area,
                ignoreFromBuild = modifier.ignoreFromBuild, applyToChildren = modifier.applyToChildren,
                overrideGenerateLinks = modifier.overrideGenerateLinks, generateLinks = modifier.generateLinks
            });
        }
        var sources = new List<NavMeshBuildSource>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(terrain, surface.layerMask, surface.useGeometry, surface.defaultArea, true,
            markups, false, sources);
        int slabs = AddNarrowTops(sources);
        var bounds = new Bounds();
        bool any = false;
        foreach (var shape in terrain.GetComponentsInChildren<Renderer>())
        {
            // Only what collides can carry walk mesh: the city beyond the edge (seen only) would just widen the bake.
            if (!shape.TryGetComponent(out Collider _)) continue;
            if (any) bounds.Encapsulate(shape.bounds);
            else { bounds = shape.bounds; any = true; }
        }
        bounds.Expand(4f);
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(surface.GetBuildSettings(), sources, bounds, terrain.position, terrain.rotation);
        surface.RemoveData();
        surface.navMeshData = data;
        if (data == null) return slabs;
        data.name = terrain.name;
        // Added now, so CheckProps can ask the walk mesh where a zombie can go.
        surface.AddData();
        return slabs;
    }

    // A walk slab over each narrow top a zombie could climb to: AgentHeight to ClimbMost over its floor (a slab lower
    // than AgentHeight would also shut the floor beside the prop to the zombies, since its overhang is too low to
    // stand under), room above it to stand, too narrow or too small for walk mesh of its own. The slab is wide enough
    // that NarrowStrip is left after the bake trims the agent's radius off its edges, and drop links come off it.
    private int AddNarrowTops(List<NavMeshBuildSource> sources)
    {
        int added = 0;
        foreach (var prop in blocks)
        {
            if (prop.Moves || prop.NoStand || !IsProp(prop) || Headroom(prop) < AgentHeight) continue;
            float height = prop.Box.max.y - FloorUnder(prop);
            if (height < AgentHeight || height > ClimbMost || HasRoomToWalk(prop)) continue;
            Vector3 size = prop.Size, top = prop.Box.center + Vector3.up * (size.y * .5f - SlabThickness * .5f);
            float minimum = 2f * AgentRadius + NarrowStrip;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(Mathf.Max(size.x, minimum), SlabThickness, Mathf.Max(size.z, minimum)),
                // Turned with the prop, so the slab lies along its top.
                transform = Matrix4x4.TRS(top, Quaternion.Euler(0f, prop.Yaw, 0f), Vector3.one),
                area = 0,
                generateLinks = true
            });
            added++;
        }
        return added;
    }

    // After the bake: every prop a player can stand on and no zombie can get to, named in the console with the prop as
    // its context (click the line to find it). Returns how many.
    private int CheckProps()
    {
        int stranded = 0;
        wideTops.Clear();
        foreach (var prop in blocks)
        {
            if (prop.Moves || prop.NoStand || !IsProp(prop) || Headroom(prop) < PlayerHeight) continue;
            float floor = FloorUnder(prop), top = prop.Box.max.y, height = top - floor;
            if (height <= ArmHeight || height > ClimbMost) continue;
            Vector3 centre = new Vector3(prop.Box.center.x, top, prop.Box.center.z);
            bool walk = NavMesh.SamplePosition(centre + Vector3.up * .1f, out NavMeshHit hit, .8f, NavMesh.AllAreas)
                        && Mathf.Abs(hit.position.y - top) < .3f;
            if (!walk)
            {
                stranded++;
                // Only a top the layout means to be stood on gets here (props nobody should stand on are capped, see
                // PropSolid), so each of these is a perch to fix in the layout or the table.
                string why = Headroom(prop) < AgentHeight ? "위가 2 m보다 낮게 막혀 좀비가 설 수 없어요(위를 비우거나, 올라설 것이 아니면 MapArt solid를 cap으로)"
                    : height < AgentHeight ? "높이 1.5~2 m의 좁은 꼭대기: 1.5 m 이하로 낮추거나 2 m 넘게 높이세요"
                    : "꼭대기에 좀비 길이 생기지 않았어요(옆의 더 높은 벽에 붙어 있으면 그래요: 벽에서 띄우거나, 올라설 것이 아니면 MapArt solid를 cap으로)";
                Debug.LogWarning("[Greybox] " + sceneName + ": 좀비가 못 가는 소품 '" + prop.Shape.name + "' (" + Format(centre) +
                                 ", 바닥에서 " + height.ToString("0.0") + " m): " + why + ".", prop.Shape);
                continue;
            }
            // A top so wide that a player in its middle stands farther than ClimbNear from any zombie at its foot: not
            // wrong (zombies come up at its edge), so the closing window names it and the console stays quiet.
            if (Mathf.Min(prop.Size.x, prop.Size.z) * .5f + AgentRadius > ClimbNear) wideTops.Add(prop.Shape.name);
        }
        return stranded;
    }

    // The wide tops CheckProps found, for the closing window.
    private readonly List<string> wideTops = new List<string>();

    private string WideTopReport()
        => wideTops.Count == 0 ? "" : "\n가운데로는 좀비가 기어오르지 않는 넓은 꼭대기(ZombieDrop " + ClimbNear + " m, 가장자리로는 올라와요): "
                                       + string.Join(", ", wideTops);

    private static string Format(Vector3 at) => at.x.ToString("0.#") + ", " + at.y.ToString("0.#") + ", " + at.z.ToString("0.#");

    // Small enough to be a prop, and higher than a step over its floor.
    private bool IsProp(Block prop)
        => prop.Size.x * prop.Size.z <= PropArea && prop.Box.max.y - FloorUnder(prop) > StepHeight;

    // Its top keeps walk mesh of its own: NarrowStrip both ways and more than the smallest region once the bake has
    // trimmed the agent's radius off every edge.
    private static bool HasRoomToWalk(Block prop)
    {
        float x = prop.Size.x - 2f * AgentRadius, z = prop.Size.z - 2f * AgentRadius;
        return x >= NarrowStrip && z >= NarrowStrip && x * z >= 2f * TopRegionArea;
    }

    // The highest top of the layout under this block, at or below its bottom: what it stands on (0, the ground, if none).
    private float FloorUnder(Block prop)
    {
        float floor = 0f;
        foreach (var other in blocks)
            if (other != prop && !other.Moves && other.Box.max.y <= prop.Box.min.y + .02f && Overlaps(prop.Box, other.Box))
                floor = Mathf.Max(floor, other.Box.max.y);
        return floor;
    }

    // How much room there is over its top before something else of the layout (m; very much if nothing).
    private float Headroom(Block prop)
    {
        float top = prop.Box.max.y, room = 100f;
        foreach (var other in blocks)
            if (other != prop && other.Box.min.y >= top - .02f && Overlaps(prop.Box, other.Box))
                room = Mathf.Min(room, other.Box.min.y - top);
        return room;
    }

    // The two footprints share some floor, more than a touch along an edge.
    private static bool Overlaps(Bounds a, Bounds b)
        => a.min.x < b.max.x - .05f && a.max.x > b.min.x + .05f && a.min.z < b.max.z - .05f && a.max.z > b.min.z + .05f;

    private void AddWaves()
    {
        var waves = new GameObject("웨이브", typeof(WaveManager));
        waves.transform.SetParent(systems, false);
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        Set(waves.GetComponent<WaveManager>(), so =>
        {
            so.FindProperty("enemyPrefab").objectReferenceValue = enemy != null ? enemy.GetComponent<EnemyHealth>() : null;
            var points = so.FindProperty("spawnPoints");
            points.arraySize = enemySpawns.Count;
            for (int i = 0; i < enemySpawns.Count; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = enemySpawns[i];
            so.FindProperty("autoStart").boolValue = false;
        });
    }

    // The chapter's run of objectives (CampaignRun): its steps come from Tables/CampaignSteps by the scene's name.
    private void AddCampaign()
    {
        var run = new GameObject("캠페인 진행", typeof(CampaignRun));
        run.transform.SetParent(systems, false);
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        Set(run.GetComponent<CampaignRun>(), so =>
        {
            so.FindProperty("enemyPrefab").objectReferenceValue = enemy != null ? enemy.GetComponent<EnemyHealth>() : null;
            Fill(so.FindProperty("spots"), spots);
            Fill(so.FindProperty("spawns"), enemySpawns);
            Fill(so.FindProperty("cans"), cans);
            Fill(so.FindProperty("doors"), doors);
            Fill(so.FindProperty("shops"), shops);
            // A shot fuel can's blast sound (friend feedback C17); the blast and its fire are drawn in code (CanBlastEffect).
            so.FindProperty("canBlastSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(CanBlastSound);
        });
    }

    private const string CanBlastSound = "Assets/ThirdParty/Vefects/Combat Flipbook VFX URP/Audio/WAV/Sergi/SFX_Vefects_Explosion_01_One_Shot_01.wav";

    private static void Fill<T>(SerializedProperty array, List<T> items) where T : Object
    {
        array.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private void Dress(GameObject shape, Material material, string surface, bool solid)
    {
        shape.GetComponent<Renderer>().sharedMaterial = material != null ? material : Concrete;
        if (!solid) Object.DestroyImmediate(shape.GetComponent<Collider>());
        if (string.IsNullOrEmpty(surface)) return;
        Set(shape.AddComponent<SurfaceType>(), so => so.FindProperty("surface").stringValue = surface);
    }

    private T Fixture<T>(string name, Vector3 position, float yaw) where T : Component
    {
        var fixture = new GameObject(name, typeof(T));
        fixture.transform.SetParent(parkour, false);
        fixture.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return fixture.GetComponent<T>();
    }

    internal static void Set(Object target, System.Action<SerializedObject> change)
    {
        var so = new SerializedObject(target);
        change(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private Transform Root(string name)
    {
        var root = new GameObject(name).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        return root;
    }

    private static Material Load(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + path);
        if (material == null) Debug.LogWarning("[Greybox] material not found: " + Materials + path);
        return material;
    }

    // A window now, or while RebuildAll runs, a paragraph of its closing window.
    private void Tell(string message)
    {
        if (batch != null) batch.Append("■ ").Append(title).Append('\n').Append(message).Append("\n\n");
        else EditorUtility.DisplayDialog(title, message, "확인");
    }
}
