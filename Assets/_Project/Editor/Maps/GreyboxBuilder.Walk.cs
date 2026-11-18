using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// GreyboxBuilder: the body the walk mesh is baked for, and the ways over what it cannot step over. One part of the class; the rest is in GreyboxBuilder.cs and the files beside it.
public sealed partial class GreyboxBuilder
{
    // The walk mesh is baked for the body that walks it: a zombie's, the radius ZombieBody gives its capsule and its
    // agent (Tables/Enemies/ZombieCrowd bodyRadius, .28 m). Until 2026-11-18 it was baked for Unity's default agent,
    // half a metre round, left as it was when the zombie's body was made a body's depth round (ZombieCrowd, 524). The
    // layouts' openings are made for a body (doors and stair landings 1.2 m wide), and the bake takes the agent's
    // radius off both sides of every opening: 1.2 m less a metre left one voxel or none. So each storey of chapter
    // 1's flats was an island of its own (no zombie climbed or came down its stairs: the landing between two flights
    // was cut), rooms behind a door were cut off from their floor, and whether a door let the walk mesh through went
    // by where the voxels fell on it (measured from the baked meshes, Tools/Maps/walk_joins.py; the spawn session's
    // finding, item 20). One fact, how wide a zombie is, stood in two places that disagreed; now the bake reads it
    // where the body reads it.
    // The voxels are the grid the layouts are laid out on (walls, doors and floors stand at tenths of a metre), so a
    // wall's face falls on a voxel's edge and costs the walk mesh no more than the radius does. Unity's own choice, a
    // third of the radius, falls on no grid of the layout: a 1.2 m door kept one voxel or none by where it stood.
    private const float WalkVoxel = .1f;
    private static float walkRadius;

    /// <summary>The radius the walk mesh is baked for (m): a zombie's body (Tables/Enemies/ZombieCrowd bodyRadius).</summary>
    private static float WalkRadius => walkRadius > 0f ? walkRadius : walkRadius = ReadBodyRadius();

    // What the bake takes off each side of a strip at most (m): the radius in whole voxels, and a voxel more where a
    // face lies off the grid (a prop turned on the floor). A top that leaves exactly the strip it needs had walk mesh
    // or not by where the voxels fell (the rebuild of 11-14: of the toll plaza's three booths, 2 × 1.5 m, two had it
    // and '요금소 0' had none); what a top needs and the slab a narrow one gets both allow for it.
    private static float WalkTrim => (Mathf.Ceil(WalkRadius / WalkVoxel - .001f) + 1f) * WalkVoxel;

    // The body's radius as the game reads it (ZombieCrowd binds the same row); its own default when the table can't be read.
    private static float ReadBodyRadius()
        => GameTables.TryGetRow(ZombieCrowd.TableName, ZombieCrowd.TableRow, out var row)
           && TableCells.TryNumber(Text(row, "bodyRadius"), out float radius) && radius >= .1f ? radius : BodyRadius;

    // The project's walk-mesh agent (type 0, what the zombies use): as round as a zombie's body, its voxels the
    // layouts' grid, with links down ledges and across gaps. Written into the project's settings, so every bake for
    // that agent agrees: this build's, a surface's own Bake button, the Hound's arena at run time (HoundArena).
    private static void FitWalkAgent()
    {
        walkRadius = ReadBodyRadius();
        var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
        var agents = settings.FindProperty("m_Settings");
        for (int i = 0; agents != null && i < agents.arraySize; i++)
        {
            var agent = agents.GetArrayElementAtIndex(i);
            if (agent.FindPropertyRelative("agentTypeID").intValue != 0) continue;
            agent.FindPropertyRelative("agentRadius").floatValue = walkRadius;
            var manual = agent.FindPropertyRelative("manualCellSize");
            if (manual != null)
            {
                if (manual.propertyType == SerializedPropertyType.Boolean) manual.boolValue = true;
                else manual.intValue = 1;
            }
            var cell = agent.FindPropertyRelative("cellSize");
            if (cell != null) cell.floatValue = WalkVoxel;
            agent.FindPropertyRelative("ledgeDropHeight").floatValue = DropHeight;
            agent.FindPropertyRelative("maxJumpAcrossDistance").floatValue = JumpGap;
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    // A bake's settings as this build means them, whatever the project's settings read back as.
    private static NavMeshBuildSettings WalkSettings(NavMeshBuildSettings settings)
    {
        settings.agentRadius = WalkRadius;
        settings.overrideVoxelSize = true;
        settings.voxelSize = WalkVoxel;
        return settings;
    }

    // ---- Ways over low things ----

    // A hop's two ends lie this far onto the walk mesh, past what the bake trims off beside the thing (m); its links
    // are of the walk mesh's "Jump" area, as the ones the bake makes across gaps are.
    private const float HopLand = .15f;
    private const int HopArea = 2;
    private readonly List<NavMeshLink> hops = new List<NavMeshLink>();

    /// <summary>
    /// A way over a low thing for the zombies. The walk mesh steps over nothing higher than its agent's step
    /// (StepHeight), and the bake lays its own links only down ledges and across gaps, so a thing laid across a way for
    /// the players to vault cuts the walk mesh in two: nothing follows the squad over it and nothing comes at them
    /// from beyond it. (Measured on the mesh baked 11-15: chapter 3's river tunnel, three pipes a metre high from wall
    /// to wall, was four islands, and the spawn points between the pipes could send to nobody.) This lays a link
    /// across <paramref name="low"/>, a box of the layout, over its thinner side and both ways, as wide as the box is
    /// long less what the bake trims at each end, from the floor before it to the floor past it. A zombie crosses it
    /// in an arc that clears the thing (ZombieDrop). The build's audit says whether each stretch of the route is
    /// joined, a hop counted.
    /// </summary>
    public void Hop(GameObject low)
    {
        var block = BlockOf(low);
        if (block == null)
        {
            Debug.LogWarning("[Greybox] " + sceneName + ": Hop wants a solid box of the layout, '" + (low != null ? low.name : "null") + "' is none.", low);
            return;
        }
        bool overX = block.Size.x <= block.Size.z;
        float thin = overX ? block.Size.x : block.Size.z, length = overX ? block.Size.z : block.Size.x;
        float reach = thin * .5f + WalkTrim + HopLand, width = length - 2f * WalkTrim;
        if (width <= 0f)
        {
            Debug.LogWarning("[Greybox] " + sceneName + ": '" + low.name + "' is too short to hop (" + length.ToString("0.0") + " m): a body walks round it.", low);
            return;
        }
        var over = new GameObject(low.name + " 넘는 길");
        over.transform.SetParent(terrain, false);
        // The link's own z runs over the thing and its own x along it, so its width lies along the thing either way
        // the engine reads a link's width.
        over.transform.SetPositionAndRotation(new Vector3(block.Box.center.x, block.Box.min.y, block.Box.center.z),
            Quaternion.Euler(0f, block.Yaw + (overX ? 90f : 0f), 0f));
        var link = over.AddComponent<NavMeshLink>();
        link.agentTypeID = 0;
        link.startPoint = new Vector3(0f, 0f, -reach);
        link.endPoint = new Vector3(0f, 0f, reach);
        link.width = width;
        link.bidirectional = true;
        link.area = HopArea;
        hops.Add(link);
    }

    // After the bake: the hops are laid again, on the walk mesh that is there now (they were made before it was).
    private void RelayHops()
    {
        foreach (var link in hops) if (link != null) link.UpdateLink();
    }

    // ---- What is meant not to be walked ----

    private readonly Dictionary<string, string> notWalked = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Says of a stretch of the route (by its step's id: the way from the spot before it to its own) or of a spawn
    /// point (by its name) that zombies are not meant to walk it, and why: a gap the players jump, a cable they ride,
    /// a lorry they climb over. The build's audit names every stretch the walk mesh does not join and every spawn
    /// point that reaches no place of the route (GreyboxBuilder.Audit, '경로와 걷기 메시'); one said here is written
    /// down as meant and kept out of the closing window, and one said here that the walk mesh does join is named as a
    /// line to take out of the layout.
    /// </summary>
    public void NotWalked(string stepOrPoint, string why) => notWalked[stepOrPoint] = why;
}
