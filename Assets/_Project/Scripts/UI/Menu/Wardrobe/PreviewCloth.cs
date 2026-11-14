using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The menu's character moves its hair, tail, skirts and ribbons as its body in the game does (PlayerBodyCloth): the
/// same Cloth.csv rows for its skin and what it wears, on the same body shapes, measured from what it wears so they rest
/// on its head, its back and its clothes. The preview used to stand with all of it stiff (the user, 2026-11-12: "꼬리랑
/// 머리카락에 물리 좀 넣어 줘. 몸이랑 옷이랑 충돌 안 하게끔"); a drag that turns it now swings them out. It is made
/// with the model and goes with it, and each look is measured once (DressedBody), as a try stands the model again.
/// The cloth is hung once the look has stood a moment (RestBefore), not in the frame the model is made: the wardrobe
/// stands a model for every choice the pointer rests on, and hanging some thirty pieces of cloth on each was part of
/// the wait felt at every try (the user, 2026-11-14: "그 미리 보기 할 때 렉 걸리는 거"). A look the pointer only passes
/// over is put away before it ever costs that, and one brought back from the shelf rests again first. Like the body in
/// the game, it is then hung on the model as its idle has posed it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PreviewCloth : MonoBehaviour
{
    // How long a look stands before its cloth is hung (s, real time).
    private const float RestBefore = .15f;

    // One object per row, because a table's binding keeps the object it was first given.
    private readonly Dictionary<string, ClothPart> parts = new Dictionary<string, ClothPart>(StringComparer.OrdinalIgnoreCase);
    // What is still to be hung, and when.
    private Animator animator;
    private string skin, look;
    private OutfitDresser.Dressing dressing;
    private bool waiting;
    private float hangAt;

    /// <summary>Hangs the cloth of this skin and what it wears (OutfitDresser.Dress) on the model standing in the menu, once it has stood a moment.</summary>
    /// <param name="look">What it wears, to remember its measured body by.</param>
    internal static void Wear(GameObject model, string skin, OutfitDresser.Dressing dressing, string look)
    {
        var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman || string.IsNullOrEmpty(skin)) return;
        var cloth = model.AddComponent<PreviewCloth>();
        cloth.animator = animator;
        cloth.skin = skin;
        cloth.dressing = dressing;
        cloth.look = look;
        cloth.waiting = true;
        cloth.hangAt = Time.unscaledTime + RestBefore;
    }

    // Brought back from the shelf with its cloth not hung yet, it rests again first.
    private void OnEnable()
    {
        if (waiting) hangAt = Time.unscaledTime + RestBefore;
    }

    private void Update()
    {
        if (!waiting || Time.unscaledTime < hangAt) return;
        waiting = false;
        // Nothing more to do each frame; the component stays, holding its rows' bindings.
        enabled = false;
        if (animator == null) return;
        long began = WardrobeCost.Begin();
        Build(animator, skin, dressing, look);
        WardrobeCost.End(WardrobeCost.Part.Cloth, began);
        dressing = null;
    }

    private void Build(Animator animator, string skin, OutfitDresser.Dressing dressing, string look)
    {
        var cloth = new HashSet<Transform>();
        var hanging = new HashSet<Transform>();
        var springs = new HashSet<Transform>();
        var rows = PlayerBodyCloth.Rows(animator, skin, id => Wears(dressing, id), Part, cloth, hanging, springs);
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
            if (part.vrc) measured ??= colliders.Measure(animator, skin + "|" + look, () => sample ??= SkinSample.Read(animator), hanging, springs);
            var touching = PlayerBodyCloth.Touching(colliders, animator, part, shapes, bones, measured);
            bones.MeasureRoom(point => colliders.Room(point, touching));
            part.Wear(animator.transform, id, roots, touching, bones);
        }
        WardrobeCost.Count(WardrobeCost.Tally.ClothParts, rows.Count);
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
