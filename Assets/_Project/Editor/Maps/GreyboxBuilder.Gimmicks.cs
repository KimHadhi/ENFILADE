using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

// GreyboxBuilder: a map's gimmicks (MapGimmicks; the user's picks of the map gimmick page, 2026-11-14): what players work
// with [F] or a shot, what changes from run to run, the sleeping crowds and the walkers on their rounds. One part of the
// class; the rest is in GreyboxBuilder.cs and the files beside it.
public sealed partial class GreyboxBuilder
{
    private readonly List<GimmickSwitch> switches = new List<GimmickSwitch>();
    private Transform gimmickRoot;
    private bool anyVariant;

    // Everything gimmicky under one root of the hierarchy (boxes that move or come and go stay under the map's terrain).
    private Transform GimmickRoot => gimmickRoot != null ? gimmickRoot : gimmickRoot = Root("◤━━ 기믹 (MapGimmicks: F로 쓰는 것, 판마다 바뀌는 것, 잠든 무리)");

    /// <summary>
    /// A switch of the map's gimmicks (a GimmickSwitch kind), at a place and turned to a yaw, listed on the map's
    /// MapGimmicks in the order made (the same on every PC). Set its fields with Configure.
    /// </summary>
    public T Switch<T>(string name, Vector3 at, float yaw = 0f) where T : GimmickSwitch
    {
        var host = new GameObject(name, typeof(T));
        host.transform.SetParent(GimmickRoot, false);
        host.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        var made = host.GetComponent<T>();
        switches.Add(made);
        return made;
    }

    /// <summary>A switch on something already built (a locked door's panel), listed with the rest.</summary>
    public T AddSwitch<T>(GameObject host) where T : GimmickSwitch
    {
        var made = host.AddComponent<T>();
        switches.Add(made);
        return made;
    }

    /// <summary>
    /// A group whose children come and go by the run's seed (GimmickVariant): keep of them stay each run. Its boxes are
    /// left out of the walk-mesh bake and carve the mesh while they stand (Blocker), so the way opens when one is gone.
    /// Same salt, same picks: two groups sharing one keep the same children (a hole's plug and its debris).
    /// </summary>
    public Transform Variant(string name, int keep, int salt)
    {
        anyVariant = true;
        var group = new GameObject(name, typeof(GimmickVariant));
        group.transform.SetParent(terrain, false);
        group.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Set(group.GetComponent<GimmickVariant>(), so =>
        {
            so.FindProperty("keep").intValue = Mathf.Max(0, keep);
            so.FindProperty("salt").intValue = salt;
        });
        return group.transform;
    }

    /// <summary>One choice of a variant group: an empty holder for what goes with it (a blocker and its dressing).</summary>
    public Transform Choice(Transform variant, string name)
    {
        var choice = new GameObject(name).transform;
        choice.SetParent(variant, false);
        return choice;
    }

    /// <summary>
    /// A solid box that may not be there (in a variant's choice, or a door that opens): out of the walk-mesh bake, it
    /// carves the mesh where it stands at run time, so zombies go round it and through once it is gone.
    /// </summary>
    public GameObject Blocker(string name, float x0, float x1, float y0, float y1, float z0, float z1, Transform under,
        Material material = null, string surface = null)
    {
        var box = Box(name, x0, x1, y0, y1, z0, z1, material, surface, true, under);
        // It may go: no walk slab on it, and the small-prop check and the decals leave it alone.
        Moving(box);
        box.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var obstacle = box.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = Vector3.one;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        return box;
    }

    /// <summary>
    /// Dresses a box that comes and goes with its switch (a cage door that bursts, a blocker that falls away) with a model
    /// of <paramref name="kind"/> (Tables/MapArt), as Dress does, but the models go under the box, so they go with it: Dress
    /// puts them under the map's dressing, which stays.
    /// </summary>
    public GameObject DressPart(GameObject box, string kind)
    {
        if (box == null) return null;
        int before = dressing != null ? dressing.childCount : 0;
        Dress(box, kind);
        if (dressing == null) return box;
        for (int i = dressing.childCount - 1; i >= before; i--) dressing.GetChild(i).SetParent(box.transform, true);
        return box;
    }

    /// <summary>
    /// A lamp on a breaker's circuit (GimmickBreaker): realtime only, as the map's baked light would keep its glow once it
    /// is off, and under the breaker so the bake leaves it alone.
    /// </summary>
    public Light CircuitLamp(GimmickBreaker breaker, Vector3 at, float range = 8f, float intensity = 1.6f)
    {
        var lamp = new GameObject("회로 등", typeof(Light));
        lamp.transform.SetParent(breaker.transform, false);
        lamp.transform.position = at;
        var light = lamp.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(1f, .9f, .74f);
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        return light;
    }

    /// <summary>A crowd asleep on the floor round a place, laid as the stretch of <paramref name="step"/> begins (SleeperNest).</summary>
    public void Sleepers(string step, Vector3 floor, int count, float radius, Transform under = null)
    {
        var nest = new GameObject("잠든 무리 " + count, typeof(SleeperNest));
        nest.transform.SetParent(under != null ? under : GimmickRoot, false);
        nest.transform.position = floor + Vector3.up * Lift;
        Set(nest.GetComponent<SleeperNest>(), so =>
        {
            so.FindProperty("step").stringValue = step;
            so.FindProperty("count").intValue = count;
            so.FindProperty("radius").floatValue = radius;
        });
    }

    /// <summary>A crowd shut in behind bars round a place, standing (SleeperNest caged: ZombieMind.Cage), set as the stretch of <paramref name="step"/> begins.</summary>
    public void CageCrowd(string step, Vector3 floor, int count, float radius, Transform under = null)
    {
        Sleepers(step, floor, count, radius, under);
        var nest = (under != null ? under : GimmickRoot).GetChild((under != null ? under : GimmickRoot).childCount - 1);
        nest.name = "갇힌 무리 " + count;
        Set(nest.GetComponent<SleeperNest>(), so => so.FindProperty("caged").boolValue = true);
    }

    /// <summary>A round walkers keep while calm, its stops in order (PatrolRoute), set as the stretch of <paramref name="step"/> begins.</summary>
    public void Walkers(string step, int count, Transform under, params Vector3[] stops)
    {
        var route = new GameObject("순찰 " + stops.Length + "곳", typeof(PatrolRoute));
        route.transform.SetParent(under != null ? under : GimmickRoot, false);
        for (int i = 0; i < stops.Length; i++)
        {
            var stop = new GameObject("멈춤 " + (i + 1)).transform;
            stop.SetParent(route.transform, false);
            stop.position = stops[i] + Vector3.up * Lift;
        }
        Set(route.GetComponent<PatrolRoute>(), so =>
        {
            so.FindProperty("step").stringValue = step;
            so.FindProperty("count").intValue = count;
        });
    }

    /// <summary>
    /// An empty holder under the map's terrain for boxes that stay in the walk-mesh bake and stand still until a gimmick
    /// switches them off (a road segment that falls in, GimmickCollapse): Box(..., group: it) puts them there.
    /// </summary>
    public Transform Holder(string name)
    {
        var holder = new GameObject(name).transform;
        holder.SetParent(terrain, false);
        return holder;
    }

    /// <summary>An empty place under the gimmicks' root (an item's possible spot, a landing zone).</summary>
    public Transform Mark(string name, Vector3 at, float yaw = 0f, Transform under = null)
    {
        var mark = new GameObject(name).transform;
        mark.SetParent(under != null ? under : GimmickRoot, false);
        mark.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        return mark;
    }

    // Finish: the map's MapGimmicks with its switches in order, when it has any (or anything that varies by the seed).
    private void AddGimmicks()
    {
        if (switches.Count == 0 && !anyVariant && SleeperNestsMade == 0) return;
        var host = new GameObject("맵 기믹", typeof(MapGimmicks));
        host.transform.SetParent(systems, false);
        Set(host.GetComponent<MapGimmicks>(), so => Fill(so.FindProperty("switches"), switches));
    }

    private int SleeperNestsMade => gimmickRoot != null ? gimmickRoot.GetComponentsInChildren<SleeperNest>(true).Length : 0;

    // For the build's report.
    private string GimmickReport()
        => switches.Count == 0 && !anyVariant ? "" : "\n기믹: F로 쓰는 것 " + switches.Count + "개" + (anyVariant ? ", 판마다 바뀌는 묶음이 있어요(방장이 판마다 정함)." : ".");
}
