using System.Collections.Generic;
using UnityEngine;

// OutfitDresser: hanging a piece that goes on as one thing the way it is worn. One part of the class; the rest is in
// OutfitDresser.cs and the files beside it.
internal static partial class OutfitDresser
{
    /// <summary>
    /// Hangs a freshly made piece that goes on as one thing (a model made for anyone, its bones its own) the way the look
    /// wears it (<paramref name="position"/>, WearPosition), on this body as it stands now, and gives it to what it
    /// follows:
    /// - a face fit goes onto the head's own surface, measured from the character's meshes as they show (FaceFit), from
    ///   whichever way round the head the position looks (the face, the back of the head);
    /// - a spot goes where the position says from its anchor (the middle of the eyes, a bone) in eye spacings along the
    ///   way the body faces, a pair twice and mirrored; a character someone measured by hand keeps that spot instead
    ///   (OutfitSpot).
    /// Then what the position's own row moves, turns and sizes goes on, and last the player's nudge
    /// (<paramref name="nudge"/>), each counted from where the step before left the piece (ItemAdjust).
    /// Nothing here is kept per character: the same row puts the same piece on any humanoid.
    /// </summary>
    private static void Hang(Transform instance, OutfitItem item, OutfitSlot slot, WearPosition position, Transform root, Animator animator,
        Renderer[] own, string skinId, ItemAdjust.Values nudge)
    {
        var record = ItemAdjust.Begin(instance, item.id);
        var toModel = root.worldToLocalMatrix;
        FaceFit.EyesOf(animator, toModel, out var eyes, out float spacing);
        float unit = FaceFit.Unit(animator, toModel, spacing);
        bool hung = position.FitsFace
            ? OnHead(instance, item, position, root, animator, own, skinId, record)
            : AtSpot(instance, item, slot, position, root, animator, skinId, eyes, unit, record);
        if (!hung) return;
        // The row's own say over the piece where its fit put it: a face fit's shift in eye spacings, turn and size; a
        // spot's size (where it is and how it is turned is the spot itself).
        var shift = position.FitsFace
            ? new ItemAdjust.Values(position.at.x * unit, position.at.y * unit, position.at.z * unit, position.scale, position.turn.y, position.turn.x, position.turn.z)
            : new ItemAdjust.Values(0f, 0f, 0f, position.scale, 0f, 0f, 0f);
        if (!shift.IsNone)
        {
            record.Apply(shift);
            record.Settle();
        }
        record.Apply(nudge);
    }

    // A rigid piece for the head (the welding mask) goes onto this head as measured from its own meshes, looking from
    // the position's way round it, then follows the head bone.
    private static bool OnHead(Transform instance, OutfitItem item, WearPosition position, Transform root, Animator animator, Renderer[] own,
        string skinId, ItemAdjust record)
    {
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var face = FaceFit.Measure(root, animator, own, position.yaw);
        if (face == null || !FaceFit.Put(instance, root, face, head))
        {
            Warn($"{item.ModelId}: {skinId}의 머리(머리 뼈와 읽을 수 있는 머리 메시)를 재지 못해 착용 위치 '{position.id}'에 맞추지 못했습니다.");
            return false;
        }
        record.Hold(instance, face.Placed, root.TransformVector(face.Frame * Vector3.right), root.TransformVector(face.Frame * Vector3.up),
            root.TransformVector(face.Frame * Vector3.forward));
        return true;
    }

    // A piece hung at a spot: from the eyes or a bone, in eye spacings along the way the body faces, turned as the
    // position says; a pair's parts on the right and the left. It follows the head (from the eyes) or the bone.
    private static bool AtSpot(Transform instance, OutfitItem item, OutfitSlot slot, WearPosition position, Transform root, Animator animator,
        string skinId, Vector3 eyes, float unit, ItemAdjust record)
    {
        bool human = animator != null && animator.isHuman;
        var toModel = root.worldToLocalMatrix;
        Transform follows;
        Vector3 from;
        if (position.AtEyes)
        {
            follows = human ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            from = eyes;
        }
        else
        {
            follows = human && position.TryBone(out var bone) ? animator.GetBoneTransform(bone) : slot.Anchor(animator);
            from = follows != null ? toModel.MultiplyPoint3x4(follows.position) : Vector3.zero;
        }
        if (follows == null)
        {
            Warn($"{item.ModelId}: 착용 위치 '{position.id}'가 따라갈 뼈({(position.AtEyes ? "Head" : string.IsNullOrEmpty(position.anchor) ? slot.bone : position.anchor)})가 "
                + $"{skinId}에게 없어 몸을 따라가지 않습니다.");
            return false;
        }
        var frame = FaceFit.FrameOf(animator, toModel);
        // Where each piece goes from its anchor, along the body's right, up and ahead (model metres), and how it is turned.
        Vector3 offset = position.at * unit, turned = position.turn;
        // Somebody measured this character's own spot for this way of wearing (its lobes): that is where it goes, from the
        // slot's bone, in metres. It was measured along the way the body faces (right from the shoulders, ahead toward the
        // eyes), so it is put on that way too: along the model's own axes, as it was until 2026-11-18, it went the wrong
        // way round on a model that stands facing the other way in its file.
        var measured = OutfitSpot.Of(position.id, skinId);
        var slotBone = measured != null ? slot.Anchor(animator) : null;
        if (slotBone != null)
        {
            follows = slotBone;
            from = toModel.MultiplyPoint3x4(slotBone.position);
            offset = measured.at;
            // Its front turned out to the side, then back toward the front by the row's angle.
            turned = new Vector3(0f, 90f - measured.turn, 0f);
        }
        var pieces = Parts(instance, position.pair || slotBone != null);
        foreach (var (part, side) in pieces)
            part.SetPositionAndRotation(
                root.TransformPoint(from + frame * new Vector3(offset.x * side, offset.y, offset.z)),
                root.rotation * frame * Quaternion.Euler(turned.x, turned.y * side, turned.z * side));
        instance.SetParent(follows, true);
        Vector3 right = root.TransformVector(frame * Vector3.right), up = root.TransformVector(frame * Vector3.up),
            ahead = root.TransformVector(frame * Vector3.forward);
        foreach (var (part, side) in pieces) record.Hold(part, part.position, right, up, ahead, side);
        return true;
    }

    // What of a model is hung: as a pair, its root's children named "… R" and "… L" (a pair of earrings), each on its
    // side; else, or when it has none so named, the whole model as one piece on the right.
    private static List<(Transform Part, int Side)> Parts(Transform instance, bool pair)
    {
        var parts = new List<(Transform, int)>();
        if (pair)
            for (int i = 0; i < instance.childCount; i++)
            {
                var child = instance.GetChild(i);
                int side = OutfitSpot.SideOf(child.name);
                if (side != 0) parts.Add((child, side));
            }
        if (parts.Count == 0) parts.Add((instance, 1));
        return parts;
    }
}
