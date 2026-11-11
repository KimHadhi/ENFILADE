using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The menu's character moves its hair, tail, skirts and ribbons as its body in the game does (PlayerBodyCloth): the
/// same Cloth.csv rows for its skin and what it wears, on the same body shapes, measured from what it wears so they rest
/// on its head, its back and its clothes. The preview used to stand with all of it stiff (the user, 2026-11-12: "꼬리랑
/// 머리카락에 물리 좀 넣어 줘. 몸이랑 옷이랑 충돌 안 하게끔"); a drag that turns it now swings them out. It is made
/// with the model and goes with it, and each look is measured once (DressedBody), as a try stands the model again.
/// </summary>
[DisallowMultipleComponent]
public sealed class PreviewCloth : MonoBehaviour
{
    // One object per row, because a table's binding keeps the object it was first given.
    private readonly Dictionary<string, ClothPart> parts = new Dictionary<string, ClothPart>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hangs the cloth of this skin and what it wears (OutfitDresser.Dress) on the model standing in the menu.</summary>
    /// <param name="look">What it wears, to remember its measured body by.</param>
    internal static void Wear(GameObject model, string skin, OutfitDresser.Dressing dressing, string look)
    {
        var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman || string.IsNullOrEmpty(skin)) return;
        model.AddComponent<PreviewCloth>().Build(animator, skin, dressing, look);
    }

    private void Build(Animator animator, string skin, OutfitDresser.Dressing dressing, string look)
    {
        var cloth = new HashSet<Transform>();
        var hanging = new HashSet<Transform>();
        var rows = PlayerBodyCloth.Rows(animator, skin, id => Wears(dressing, id), Part, cloth, hanging);
        if (rows.Count == 0) return;
        var colliders = gameObject.AddComponent<PlayerBodyClothColliders>();
        colliders.ReadTable();
        SkinSample sample = null;
        DressedBody measured = null;
        HashSet<Transform> armBones = null;
        foreach (var (id, part, roots) in rows)
        {
            // Read before the simulation first moves them.
            var bones = new ClothBones(roots, part.rootTurn);
            string shapes = PlayerBodyCloth.Touches(part, roots, animator, ref armBones);
            if (part.vrc) measured ??= colliders.Measure(animator, skin + "|" + look, () => sample ??= SkinSample.Read(animator), hanging);
            var touching = PlayerBodyCloth.Touching(colliders, animator, part, shapes, bones, measured);
            bones.MeasureRoom(point => colliders.Room(point, touching));
            part.Wear(animator.transform, id, roots, touching, bones);
        }
    }

    private static bool Wears(OutfitDresser.Dressing dressing, string id) =>
        dressing != null && !string.IsNullOrEmpty(id) && (dressing.Items.Contains(id) || dressing.Models.Contains(id));

    private ClothPart Part(string id)
    {
        if (parts.TryGetValue(id, out var part)) return part;
        part = new ClothPart();
        GameTables.BindNested(ClothPart.TableName, id, this, part);
        return parts[id] = part;
    }
}
