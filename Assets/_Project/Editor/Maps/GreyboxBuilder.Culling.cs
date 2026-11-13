using UnityEditor;
using UnityEngine;

// GreyboxBuilder: Culling (the user's memo 40: "최적화, 컬링"). One part of the class; the rest is in GreyboxBuilder.cs and the files beside it.
public sealed partial class GreyboxBuilder
{
    // What of the layout never moves is static: batched into a few draws per material (Batching Static), hiding what is
    // behind it from the camera (Occluder Static, solid boxes) and left undrawn while something hides it (Occludee
    // Static). Gates, doors and pushback groups move, so they stay dynamic. The occlusion data is baked from these, and
    // anything that moves (zombies, players, pickups, effects) is culled against it at run time as well, since every
    // renderer allows dynamic occlusion unless it says otherwise: a horde behind a building or a tunnel wall costs no
    // drawing. Contribute GI only for a map that bakes probes (GreyboxBuilder.Probes): its solid boxes and its models,
    // taking their light from the probes, not lightmaps; Unity 6 never bakes lighting by itself, only the build does.
    // Smaller occluders than this are not used to hide anything, and the gaps it may see through (m); larger values bake
    // faster and coarser. The maps are big boxes, so a coarse bake is enough.
    private const float SmallestOccluder = 4f, SmallestHole = .25f, BackfaceThreshold = 100f;

    private void MarkStatic()
    {
        // Dressing (GreyboxBuilder.Dress): batched and hidden behind walls, but too small and too varied to hide much.
        // Everything that stays put is in the baked reflection probes' pictures (GreyboxBuilder.Reflections) too.
        if (dressing != null)
            foreach (var shape in dressing.GetComponentsInChildren<Renderer>())
            {
                var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                TakesPartInBake(shape, ref flags);
                GameObjectUtility.SetStaticEditorFlags(shape.gameObject, flags);
            }
        foreach (var shape in terrain.GetComponentsInChildren<Renderer>())
        {
            var part = shape.gameObject;
            if (MovesWithGroup(part))
            {
                // It moves: its surface is mapped in its own space, or the texture would slide over it as it goes.
                shape.sharedMaterials = MovingMaterials(shape.sharedMaterials);
                continue;
            }
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            // A dressed box's grey look is hidden (GreyboxBuilder.Dress): its model may not fill it, so it hides nothing.
            // Glass is seen through: as an occluder it would hide the room behind it (the tower's lanes), and in the
            // probe bake it would shut the light out. The boxes with no collider (trims, neon, the city beyond the edge)
            // are left out of the bake too: the city would stretch the probe volume to its far side.
            if (shape.enabled && part.TryGetComponent(out Collider _) && !seeThrough.Contains(part))
            {
                flags |= StaticEditorFlags.OccluderStatic;
                TakesPartInBake(shape, ref flags);
            }
            GameObjectUtility.SetStaticEditorFlags(part, flags);
        }
    }

    private static bool BakeOcclusion()
    {
        StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
        StaticOcclusionCulling.smallestHole = SmallestHole;
        StaticOcclusionCulling.backfaceThreshold = BackfaceThreshold;
        bool done = StaticOcclusionCulling.Compute();
        if (!done) Debug.LogWarning("[Greybox] occlusion culling did not bake; the scene still works, only without it.");
        return done;
    }
}
