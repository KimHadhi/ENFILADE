using System;
using System.Collections.Generic;
using UnityEngine;

// OutfitDresser: what is worn under what. One part of the class; the rest is in OutfitDresser.cs and the files beside it.
internal static partial class OutfitDresser
{
    // What hangs from the head under the face pieces worn (OutfitSlot.FitsFace): the character's own head meshes (hair,
    // ears, the face) and items worn in the head's slots. A piece the face piece covers whole (FaceFit.csv hide, as
    // SkinCover measures it: a hairpin under the mask) goes out of sight; the rest is pressed in under it where it stands
    // out through it (UnderwearFit.Press), on copies of its meshes. Measured on the FBX files (2026-11-13), a few hair
    // vertices stood out through the welding mask on each character, nothing of a face.
    private static void UnderFace(Transform root, string skinId, Animator animator, Renderer[] own, List<Worn> worn, HashSet<string> covered)
    {
        var onFace = new List<Renderer>();
        foreach (var piece in worn)
            if (piece.Slot.FitsFace && !covered.Contains(piece.Slot.id)) onFace.AddRange(piece.Renderers);
        if (onFace.Count == 0) return;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        var under = new List<Renderer>();
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer skinned && renderer.enabled && renderer.gameObject.activeInHierarchy && HeadMeshes.OnHead(skinned, head))
                under.Add(renderer);
        foreach (var piece in worn)
        {
            if (piece.Slot.FitsFace || covered.Contains(piece.Slot.id) || !string.Equals(piece.Slot.group, "head", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var renderer in piece.Renderers)
                if (renderer is SkinnedMeshRenderer && renderer.enabled) under.Add(renderer);
        }
        var showing = new List<Renderer>();
        float hide = FaceFit.HideShare;
        foreach (var renderer in under)
        {
            if (SkinCover.Covers(root, skinId, renderer, onFace) >= hide) renderer.enabled = false;
            else showing.Add(renderer);
        }
        UnderwearFit.Press(root, showing, onFace);
    }

    // The character's own underwear in the underwear slots nothing else took (CharacterParts.csv rows of those slots):
    // on the skin whatever else is worn. Its maker kept it switched off under the clothes it came in (Mayo's bra and
    // panties, Ichigo's), so it is switched on here, and the rest of the outfit decides how much of it shows
    // (UnderwearFit). Before Replace: an item may name a piece of it that it takes the place of.
    private static void WearUnderwear(Transform root, Renderer[] own, string skinId, HashSet<string> emptied)
    {
        foreach (var row in OutfitCatalog.PartsOf(skinId))
        {
            if (emptied.Contains(row.slot) || OutfitCatalog.Slot(row.slot) is not { under: true }) continue;
            var names = new HashSet<string>(OutfitCatalog.Split(row.parts), StringComparer.OrdinalIgnoreCase);
            var wearing = new List<Renderer>();
            foreach (var renderer in own)
                if (renderer != null && names.Contains(renderer.name)) wearing.Add(renderer);
            SwitchOn(wearing, root);
        }
    }

    // Renderers switched on with their objects, and those objects' parents up to (not including) <paramref name="top"/>.
    private static void SwitchOn(List<Renderer> renderers, Transform top)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            for (var part = renderer.transform; part != null && part != top; part = part.parent)
            {
                if (part.gameObject.activeSelf) continue;
                part.gameObject.SetActive(true);
            }
            renderer.enabled = true;
        }
    }

    // The underwear on the body, each piece's renderers together (an item, or the character's own in its slot), and what
    // is worn over it: every other piece drawn solid but the bare ones (hair, a tail) and those on their way off. A
    // character with no rows has only its items told apart; its own meshes are its body.
    private static (List<List<Renderer>> Under, List<Renderer> Over) Layers(Renderer[] own, string skinId, List<Worn> worn,
        HashSet<string> covered, List<Renderer> leaving)
    {
        var under = new List<List<Renderer>>();
        var over = new List<Renderer>();
        var seen = new HashSet<Renderer>();
        var gone = new HashSet<Renderer>(leaving);
        foreach (var row in OutfitCatalog.PartsOf(skinId))
        {
            var slot = OutfitCatalog.Slot(row.slot);
            if (slot == null || SkinCover.IsBare(slot.id)) continue;
            var names = new HashSet<string>(OutfitCatalog.Split(row.parts), StringComparer.OrdinalIgnoreCase);
            var piece = new List<Renderer>();
            foreach (var renderer in own)
                if (renderer != null && names.Contains(renderer.name) && Drawn(renderer, gone) && seen.Add(renderer)) piece.Add(renderer);
            if (piece.Count == 0) continue;
            if (slot.under) under.Add(piece);
            else over.AddRange(piece);
        }
        foreach (var picked in worn)
        {
            if (covered.Contains(picked.Slot.id) || SkinCover.IsBare(picked.Slot.id)) continue;
            var piece = new List<Renderer>();
            foreach (var renderer in picked.Renderers)
                if (Drawn(renderer, gone) && seen.Add(renderer)) piece.Add(renderer);
            if (piece.Count == 0) continue;
            if (picked.Slot.under) under.Add(piece);
            else over.AddRange(piece);
        }
        return (under, over);
    }

    internal static bool Drawn(Renderer renderer, HashSet<Renderer> gone) =>
        renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && !gone.Contains(renderer);

    // The look's items that exist, sit in their own slot and were made for this character, in the slots' order. Anything
    // else (an item this PC doesn't have, or one for another character) leaves its slot as the character has it.
    private static List<(OutfitSlot Slot, OutfitItem Item, string Colour)> Picks(OutfitLook look, string skinId)
    {
        var found = new List<(OutfitSlot, OutfitItem, string)>();
        foreach (var slot in OutfitCatalog.Slots)
        {
            if (!look.TryGet(slot.id, out var pick) || pick.IsOff) continue;
            var item = OutfitCatalog.Item(pick.Item);
            if (item == null || !string.Equals(item.slot, slot.id, StringComparison.OrdinalIgnoreCase)) continue;
            if (OutfitCatalog.Model(item.id, skinId)?.Model == null) continue;
            found.Add((slot, item, pick.Colour));
        }
        return found;
    }
}
