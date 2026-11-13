using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// OutfitDresser: blend shapes that follow the body. One part of the class; the rest is in OutfitDresser.cs and the files beside it.
internal static partial class OutfitDresser
{
    // Shape keys "name=weight|name" on every mesh that has them: the body's shrink keys under clothes, a heel key on the
    // feet, a shirt's own key to tuck under a coat. An item's fit (<paramref name="fit"/>) doesn't stack a draw-in key on
    // skin another draws in already (Stacks).
    private static void Shape(IEnumerable<SkinnedMeshRenderer> meshes, string shapes, bool fit = false)
    {
        if (string.IsNullOrEmpty(shapes)) return;
        foreach (string pair in OutfitCatalog.Split(shapes))
        {
            int equals = pair.IndexOf('=');
            string name = (equals < 0 ? pair : pair.Substring(0, equals)).Trim();
            float weight = 100f;
            if (equals >= 0 && !float.TryParse(pair.Substring(equals + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
            {
                Warn($"셰이프키 '{pair}'의 값을 읽지 못했습니다.");
                continue;
            }
            SetShape(meshes, name, weight, fit && weight > 0f && SkinCover.Hides(name));
        }
    }

    private static void SetShape(IEnumerable<SkinnedMeshRenderer> meshes, string name, float weight, bool unstacked = false)
    {
        foreach (var mesh in meshes)
        {
            int index = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.GetBlendShapeIndex(name) : -1;
            if (index < 0 || (unstacked && Stacks(mesh, index))) continue;
            mesh.SetBlendShapeWeight(index, Mathf.Clamp(weight, 0f, 100f));
        }
    }

    // A mesh's draw-in keys (SkinCover.Hides) are ways to hide one stretch of skin, not layers to pile up: the "both
    // sides" key and the one-side keys, or a tights fit drawing the whole leg in (Ichigo's Shrink_Costume_Tights_Fit) and
    // the foot keys under shoes. On together they move that skin twice as far, out through the other side: with her
    // tights on, the Sera Yura shoes' fit put Shrink_Foot and Shrink_Toe on her body over the tights fit, and her heel
    // stood out of the shoes by up to 19 mm at 314 of their vertices; left to the tights fit, 2 vertices, 10 mm (measured
    // from the FBX files, 2026-11-12). So a fit's draw-in key doesn't go on where nearly all the skin it moves is moved by
    // a draw-in key that is on already. Mayo's six foot keys share no skin, so hers all go on as before.
    private const float StackShare = .9f;

    private static bool Stacks(SkinnedMeshRenderer renderer, int key)
    {
        var mesh = renderer.sharedMesh;
        if (renderer.GetBlendShapeWeight(key) > 0f || !(Moved(mesh, key) is bool[] mine)) return false;
        List<bool[]> others = null;
        for (int k = 0; k < mesh.blendShapeCount; k++)
            if (k != key && renderer.GetBlendShapeWeight(k) > 0f && SkinCover.Hides(mesh.GetBlendShapeName(k)) && Moved(mesh, k) is bool[] other)
                (others ??= new List<bool[]>()).Add(other);
        if (others == null) return false;
        int skin = 0, drawn = 0;
        for (int i = 0; i < mine.Length; i++)
        {
            if (!mine[i]) continue;
            skin++;
            foreach (var other in others)
                if (other[i])
                {
                    drawn++;
                    break;
                }
        }
        return skin > 0 && drawn >= skin * StackShare;
    }

    // Which of a mesh's vertices a key moves (over a millimetre), read once per mesh and key; null where it can't be read.
    private static bool[] Moved(Mesh mesh, int key)
    {
        if (movedBy.TryGetValue((mesh, key), out var known)) return known;
        int frames = mesh.isReadable ? mesh.GetBlendShapeFrameCount(key) : 0;
        if (frames == 0) return movedBy[(mesh, key)] = null;
        if (keyDeltas.Length != mesh.vertexCount) keyDeltas = new Vector3[mesh.vertexCount];
        mesh.GetBlendShapeFrameVertices(key, frames - 1, keyDeltas, null, null);
        var moved = new bool[keyDeltas.Length];
        for (int i = 0; i < moved.Length; i++) moved[i] = keyDeltas[i].sqrMagnitude > 1e-6f;
        return movedBy[(mesh, key)] = moved;
    }

    // The body's keys as dressing left them, for these names: the first of its meshes with the key set, else 0. The same
    // first-set rule as ShapesOf, so keys nothing touched follow exactly as before.
    private static Dictionary<string, float> ShapesNow(Renderer[] own, HashSet<string> names)
    {
        var shapes = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var renderer in own)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) continue;
            var mesh = skinned.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (!names.Contains(name)) continue;
                float weight = skinned.GetBlendShapeWeight(i);
                if (!shapes.TryGetValue(name, out float found) || (found == 0f && weight != 0f)) shapes[name] = weight;
            }
        }
        return shapes;
    }

    // The body's shape keys as its maker set them (a fuller chest, wider hips), by name.
    private static Dictionary<string, float> ShapesOf(Renderer[] own)
    {
        var shapes = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var renderer in own)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) continue;
            var mesh = skinned.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float weight = skinned.GetBlendShapeWeight(i);
                if (weight != 0f) shapes.TryAdd(mesh.GetBlendShapeName(i), weight);
            }
        }
        return shapes;
    }

    // Clothes made for a body carry its body-shape keys (Breast_Big, Hip_Big, Legs_Slim) and take the body's values, as
    // Modular Avatar's Blendshape Sync makes them in VRChat: a jacket made for the flat chest fits the fuller one too.
    private static void Follow(Dictionary<string, float> body, List<Worn> worn)
    {
        if (body.Count == 0) return;
        foreach (var piece in worn)
            foreach (var renderer in piece.Renderers)
            {
                if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) continue;
                var mesh = skinned.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    if (body.TryGetValue(mesh.GetBlendShapeName(i), out float weight)) skinned.SetBlendShapeWeight(i, weight);
            }
    }
}
