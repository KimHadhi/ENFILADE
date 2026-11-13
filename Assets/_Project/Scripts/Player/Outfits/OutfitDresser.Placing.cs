using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// OutfitDresser: putting a piece on the body and binding it to the skeleton. One part of the class; the rest is in OutfitDresser.cs and the files beside it.
internal static partial class OutfitDresser
{
    // Makes the model on the character and joins it to the body.
    private static Placed Place(OutfitItem item, OutfitSlot slot, Transform root, GameObject source, OutfitSkeleton skeleton,
        Animator animator, Renderer[] own, string skinId)
    {
        var entry = OutfitCatalog.Model(item.id, skinId);
        var instance = Object.Instantiate(entry.Model, root, false);
        instance.name = "Outfit (" + item.ModelId + ")";
        Align(instance.transform, entry.Model.transform, source != null ? source.transform : null);
        // An outfit's file may come with an Animator (imported as a humanoid of its own): only the character's may play.
        foreach (var extra in instance.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(extra);
        int layer = root.gameObject.layer;
        foreach (var part in instance.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;

        var copy = new Placed { Entry = entry, Instance = instance };
        bool skinned = false;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            // Outfit files often carry the avatar's own body to fit the clothes on; the character has that already.
            if (CopiesOwn(renderer, root, own, root))
            {
                renderer.enabled = false;
                continue;
            }
            if (renderer is SkinnedMeshRenderer mesh)
            {
                mesh.updateWhenOffscreen = true;
                skinned = true;
            }
            copy.Renderers.Add(renderer);
        }
        if (copy.Renderers.Count == 0)
        {
            Warn($"{item.ModelId}: {entry.Model.name}에 입힐 메시가 없습니다.");
            Object.Destroy(instance);
            return null;
        }
        // A piece made for anyone (an accessory sold for any avatar) brings bones of its own, matched to none of the
        // character's by name, and hangs on the slot's spots measured for this character: the Ene earrings on the lobes.
        if (entry.ForAnyone)
        {
            // A rigid piece for the face (the welding mask) goes onto this face as measured from its own meshes, then
            // follows the head: no rows per character (FaceFit).
            if (slot.FitsFace)
            {
                var face = FaceFit.Measure(root, animator, own);
                if (face == null || !FaceFit.Put(instance.transform, root, face, slot.Anchor(animator)))
                    Warn($"{item.ModelId}: {skinId}의 얼굴(머리 뼈와 읽을 수 있는 머리 메시)을 재지 못해 {slot.id} 칸에 맞추지 못했습니다.");
                return copy;
            }
            var spot = OutfitSpot.Of(slot.id, skinId);
            if (spot == null || !spot.Hang(instance.transform, root, slot.Anchor(animator)))
                Warn($"{item.ModelId}: {skinId}의 {slot.id} 칸 자리(OutfitSpots.csv)나 뼈 '{slot.bone}'가 없어 몸을 따라가지 않습니다.");
            return copy;
        }
        if (Merge(instance.transform, skeleton, item.ModelId) > 0) return copy;
        // Nothing to follow bone by bone: a rigid piece, made standing on the character, follows its slot's bone.
        if (skinned) Warn($"{item.ModelId}: {entry.Model.name}의 뼈 이름이 캐릭터 뼈와 하나도 맞지 않아 {slot.bone}에 통째로 붙입니다.");
        var anchor = slot.Anchor(animator);
        if (anchor != null) instance.transform.SetParent(anchor, true);
        else Warn($"{item.ModelId}: {slot.id} 칸의 뼈 '{slot.bone}'를 찾지 못해 몸을 따라가지 않습니다.");
        return copy;
    }

    // Both files stood their roots at one origin, so the outfit's root goes where it stood against the character's.
    private static void Align(Transform instance, Transform itemSource, Transform characterSource)
    {
        var character = characterSource != null
            ? Matrix4x4.TRS(characterSource.localPosition, characterSource.localRotation, characterSource.localScale)
            : Matrix4x4.identity;
        var item = Matrix4x4.TRS(itemSource.localPosition, itemSource.localRotation, itemSource.localScale);
        var local = character.inverse * item;
        instance.localPosition = local.GetColumn(3);
        instance.localRotation = local.rotation;
        instance.localScale = local.lossyScale;
    }

    // How far a twin may stand from the character's bone with no correction to the bind poses (metres, degrees, share of
    // its size): the two files' skeletons, made from one avatar, agree to rounding. Further apart, Bind corrects them;
    // whether it is her bone at all is OutfitSkeleton's call.
    private const float SameSpot = .0005f, SameTurn = .1f, SameSize = .001f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        rebound.Clear();
        movedBy.Clear();
    }

    // An outfit's file carries a copy of the character's skeleton (every BOOTH outfit does). Each bone of it the character
    // has too (its twin: her name, and her place unless it is a humanoid bone, OutfitSkeleton) is the character's own: the
    // outfit's meshes are bound straight to the character's bone, as Modular Avatar binds them, and whatever of the
    // outfit's own hangs from a twin (a coat's tails, a belt, a ribbon's chain) goes under the character's bone, staying
    // where it is.
    // The twins used to go under the character's bones themselves. Magica takes every transform under a cloth chain's
    // root for a particle of that chain, so a twin under Mayo's breast swung on its own inside her breast's spring and
    // the chest belt bound to it flapped apart from her (the user, 2026-11-12: "조끼랑 가슴이랑 서로 맞물리는 게 아니라
    // 걍 서로 안 맞아서 흐물거리고 휘적거려"). Bound to her bones, the belt moves exactly as they do, hair and skirt chains
    // likewise. The twins stay behind in the outfit, unused, renamed after it ("Hips [white-board]"): a second "Hips"
    // anywhere under the model makes the humanoid ambiguous ("Ambiguous Transform 'Armature/Hips' and 'Armature/Hips/Hips'
    // found in hierarchy for human bone 'Hips'") and Unity lets go of the whole avatar (0.8.115). Nothing finds them by
    // name: the sway rows name only an outfit's own bones.
    // A bone that only shares a name with one of hers is the outfit's own, like any she lacks. Chocolate rice's mirrored
    // side locks hang from "Side_root" and "R_Side" (or "L_Side"), the names of Mayo's own lock bones, 1.3 to 6 cm from
    // them: taken for hers by name alone, the mirrored lock rode the chain of the lock already on that side, shaped for
    // another lock (2026-11-12). Such a namesake keeps its chain and sways on the outfit's own row, and is renamed after
    // the model too (OutfitSkeleton.Mark; BoothImport names it so in that row): the cloth rows find bones by name on the
    // dressed body and would find hers first. Returns how many twins there were; none is a rigid piece.
    private static int Merge(Transform instance, OutfitSkeleton skeleton, string mark)
    {
        var twins = new Dictionary<Transform, Transform>();
        var namesakes = new List<Transform>();
        skeleton.Sort(instance, twins, namesakes);
        foreach (var namesake in namesakes) namesake.name = OutfitSkeleton.Mark(namesake.name, mark);
        if (twins.Count == 0) return 0;
        // Gathered before anything moves: a mesh's object may hang from a twin too.
        var meshes = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var hanging = new List<(Transform Part, Transform Under)>();
        foreach (var pair in twins)
            foreach (Transform child in pair.Key)
                if (!twins.ContainsKey(child)) hanging.Add((child, pair.Value));
        foreach (var (part, under) in hanging)
        {
            part.SetParent(under, true);
            // The character's cloth rows leave it to the outfit's: under a breast or a skirt bone it would join that
            // row's particles too (ClothBones.Collect).
            if (!part.TryGetComponent<OutfitBone>(out _)) part.gameObject.AddComponent<OutfitBone>();
        }
        foreach (var mesh in meshes) Bind(mesh, twins);
        foreach (var twin in twins.Keys) twin.name = OutfitSkeleton.Mark(twin.name, mark);
        return twins.Count;
    }

    // Points a mesh's bones at the character's. Where the outfit's bone stood apart from the character's (two skeletons
    // that don't quite agree), the mesh's bind poses take the difference in, so nothing moves, on a copy of the mesh.
    private static void Bind(SkinnedMeshRenderer renderer, Dictionary<Transform, Transform> twins)
    {
        var bones = renderer.bones;
        Matrix4x4[] apart = null;
        bool changed = false;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null || !twins.TryGetValue(bones[i], out var own)) continue;
            if (!SameBone(bones[i], own))
            {
                apart ??= Identities(bones.Length);
                apart[i] = own.worldToLocalMatrix * bones[i].localToWorldMatrix;
            }
            bones[i] = own;
            changed = true;
        }
        if (renderer.rootBone != null && twins.TryGetValue(renderer.rootBone, out var root)) renderer.rootBone = root;
        if (!changed) return;
        if (apart != null && renderer.sharedMesh != null) renderer.sharedMesh = Rebound(renderer.sharedMesh, apart);
        renderer.bones = bones;
    }

    private static bool SameBone(Transform outfit, Transform own) =>
        (outfit.position - own.position).sqrMagnitude < SameSpot * SameSpot
        && Quaternion.Angle(outfit.rotation, own.rotation) < SameTurn
        && Mathf.Abs(Mathf.Abs(outfit.lossyScale.x) / Mathf.Max(Mathf.Abs(own.lossyScale.x), 1e-6f) - 1f) < SameSize;

    private static Matrix4x4[] Identities(int count)
    {
        var all = new Matrix4x4[count];
        for (int i = 0; i < count; i++) all[i] = Matrix4x4.identity;
        return all;
    }

    // The mesh with each bind pose moved by where its outfit bone stood on the character's: skinned to the character's
    // bone, a vertex lands where the outfit's bone put it.
    private static Mesh Rebound(Mesh mesh, Matrix4x4[] apart)
    {
        if (!mesh.isReadable) return mesh;
        int key = 17;
        foreach (var matrix in apart)
            for (int k = 0; k < 16; k++) key = key * 31 + Mathf.RoundToInt(matrix[k] * 10000f);
        if (rebound.TryGetValue((mesh, key), out var copy) && copy != null) return copy;
        var poses = mesh.bindposes;
        if (poses.Length != apart.Length) return mesh;
        for (int i = 0; i < poses.Length; i++) poses[i] = apart[i] * poses[i];
        copy = Object.Instantiate(mesh);
        copy.name = mesh.name;
        copy.bindposes = poses;
        return rebound[(mesh, key)] = copy;
    }

    /// <summary>
    /// Whether a renderer of an outfit's file is the character's own mesh shipped in it to fit the clothes on: one of
    /// <paramref name="own"/> with its name and vertex count, as large and standing in the same place, each measured in
    /// its model's frame (<paramref name="frame"/>, <paramref name="ownFrame"/>). Name and count alone took a mirrored
    /// copy for the body: Chocolate rice's mirrored side locks are Mayo's own two side locks turned to her other side,
    /// named and counted as hers (MAYO_Hair_Side_3, 737 vertices), so the wardrobe hid the only mesh the piece had and
    /// dropped it ("mayo-side-hair-1에 입힐 메시가 없습니다", the user: "마요 거울 머리 1은 선택하면 뭐 안 바뀌고",
    /// 2026-11-12). A copy's middle agrees to rounding; the mirrored locks' stand 9.5 and 14 cm from their originals. The
    /// bounds are the mesh's own, kept with it, so this costs nothing per try, and a mesh stands where its object puts its
    /// own space whether skinned (the bind poses start there) or not. The wardrobe's pictures ask it too (OutfitIcons).
    /// </summary>
    internal static bool CopiesOwn(Renderer renderer, Transform frame, Renderer[] own, Transform ownFrame)
    {
        var mesh = MeshTools.MeshOf(renderer);
        if (mesh == null) return false;
        Vector3 middle = Middle(renderer, mesh, frame);
        const float near = OutfitSkeleton.SamePlace * OutfitSkeleton.SamePlace;
        foreach (var mine in own)
        {
            if (mine == null || !string.Equals(mine.name, renderer.name, StringComparison.OrdinalIgnoreCase)
                || MeshTools.MeshOf(mine) is not Mesh theirs || theirs.vertexCount != mesh.vertexCount) continue;
            if ((theirs.bounds.size - mesh.bounds.size).sqrMagnitude <= near && (Middle(mine, theirs, ownFrame) - middle).sqrMagnitude <= near)
                return true;
        }
        return false;
    }

    // Where the middle of a mesh stands as its file made it, in its model's frame.
    private static Vector3 Middle(Renderer renderer, Mesh mesh, Transform frame) =>
        frame.InverseTransformPoint(renderer.transform.TransformPoint(mesh.bounds.center));
}
