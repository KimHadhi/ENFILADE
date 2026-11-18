using System;
using System.Collections.Generic;
using UnityEngine;

// OutfitDresser: what is worn under what. One part of the class; the rest is in OutfitDresser.cs and the files beside it.
internal static partial class OutfitDresser
{
    // What is fitted under what on the dressed body, gathered for OutfitFit.Trim: each layer once, with every garment
    // over it. A see-through garment is over nothing: what lies under it shows.
    private sealed class Layering
    {
        public readonly List<OutfitFit.Layer> Layers = new List<OutfitFit.Layer>();
        public readonly HashSet<Renderer> Sheer = new HashSet<Renderer>();
        private readonly Dictionary<Renderer, OutfitFit.Layer> byInner = new Dictionary<Renderer, OutfitFit.Layer>();

        /// <param name="hides">Whether the garments can hide the layer for good (a mask fitted to a face only cuts what comes through it).</param>
        /// <param name="snug">Whether the layer lies snug on the skin, to be tucked in under a rim it lies on (OutfitSlot.snug).</param>
        public void Under(Renderer inner, IEnumerable<Renderer> garments, bool hides, bool snug)
        {
            if (inner is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null) return;
            foreach (var garment in garments)
            {
                if (garment == null || garment == inner || Sheer.Contains(garment)) continue;
                if (!byInner.TryGetValue(inner, out var layer))
                {
                    byInner[inner] = layer = new OutfitFit.Layer(skinned);
                    Layers.Add(layer);
                }
                layer.Add(garment, hides);
                layer.Snug |= snug;
            }
        }
    }

    // Every layer of the dressed body fitted under what is worn over it, from the meshes of whatever is worn, so no two
    // pieces need rows of their own (OutfitFit.Trim):
    // - the underwear left showing, under the clothes over it;
    // - pieces in slots worn under others (OutfitSlots.csv over: socks, stockings and tights under shoes, a shirt under
    //   a jacket), the character's own pieces counting in their slots (CharacterParts.csv) as items do;
    // - hair, ears and head ornaments under a piece fitted to the face (UnderFace);
    // - the skin under everything that covers it (UnderClothes).
    // It replaces pressing the under piece in along its normals, which crushed feet in shoes (the user, 2026-11-14).
    private static void FitLayers(GameObject model, string skinId, Animator animator, Renderer[] own, List<Worn> worn, HashSet<string> covered,
        List<Renderer> leaving, List<Renderer> underwear, List<Renderer> over)
    {
        var gone = new HashSet<Renderer>(leaving);
        var layering = new Layering();
        // The character's own pieces by their renderers' names, each under the first row that lists it.
        var rows = new Dictionary<string, OutfitCatalog.Part>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in OutfitCatalog.PartsOf(skinId))
            foreach (string name in OutfitCatalog.Split(row.parts)) rows.TryAdd(name, row);
        foreach (var piece in worn)
            foreach (var renderer in piece.Renderers)
                if (piece.Item.sheer || OutfitFit.Sheer(renderer)) layering.Sheer.Add(renderer);
        foreach (var renderer in own)
            if (renderer != null && rows.TryGetValue(renderer.name, out var row) && Drawn(renderer, gone) && (row.sheer || OutfitFit.Sheer(renderer)))
                layering.Sheer.Add(renderer);

        // The skin first, against every piece as it was made: a piece fitted under another becomes another mesh for
        // each outfit, and the skin measured against that would be measured again for each.
        if (OutfitFit.FitsSkin) UnderClothes(animator, own, worn, covered, gone, rows, layering);
        foreach (var renderer in underwear)
            if (Drawn(renderer, gone)) layering.Under(renderer, over, true, true);
        foreach (var slot in OutfitCatalog.Slots)
        {
            if (string.IsNullOrEmpty(slot.over) || covered.Contains(slot.id)) continue;
            var outer = PiecesIn(slot.id, skinId, own, worn, gone);
            if (outer.Count == 0) continue;
            foreach (string under in slot.OverSlots)
            {
                if (covered.Contains(under)) continue;
                bool snug = OutfitCatalog.Slot(under) is { snug: true };
                foreach (var inner in PiecesIn(under, skinId, own, worn, gone)) layering.Under(inner, outer, true, snug);
            }
        }
        UnderFace(model.transform, skinId, animator, own, worn, covered, layering);
        OutfitFit.Trim(model, animator, layering.Layers);
    }

    // The skin under whatever covers it: the character's own meshes that are no piece of its outfit and don't hang from
    // the head, under every piece drawn solid in a slot that covers skin. A body comes with keys that draw its skin in
    // under its own clothes (SkinCover) and each fitted outfit names the keys it needs (OutfitFits.csv), but a piece
    // nobody wrote rows for, or a body with no such keys, had its skin clipping through: now skin that can't be seen is
    // simply not drawn, on any body under any piece. A character with no rows at all keeps its meshes whole: there its
    // body and its clothes can't be told apart. A face is never touched: glasses and masks are not clothes, and what a
    // lens would hide still shows through it.
    private static void UnderClothes(Animator animator, Renderer[] own, List<Worn> worn, HashSet<string> covered, HashSet<Renderer> gone,
        Dictionary<string, OutfitCatalog.Part> rows, Layering layering)
    {
        if (rows.Count == 0) return;
        var clothes = new List<Renderer>();
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer && rows.TryGetValue(renderer.name, out var row) && Drawn(renderer, gone)
                && CoversSkin(OutfitCatalog.Slot(row.slot))) clothes.Add(renderer);
        foreach (var piece in worn)
        {
            if (covered.Contains(piece.Slot.id) || !CoversSkin(piece.Slot)) continue;
            foreach (var renderer in piece.Renderers)
                if (renderer is SkinnedMeshRenderer && Drawn(renderer, gone)) clothes.Add(renderer);
        }
        if (clothes.Count == 0) return;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer skinned && !rows.ContainsKey(renderer.name) && Drawn(renderer, gone)
                && (head == null || !HeadMeshes.OnHead(skinned, head)))
                layering.Under(renderer, clothes, true, true);
    }

    // Whether pieces in the slot cover skin for good: clothes on the body, not hair or a tail (SkinCover.IsBare), and
    // nothing worn on the head, where a face shows through glasses and beside a mask.
    private static bool CoversSkin(OutfitSlot slot) =>
        slot != null && !SkinCover.IsBare(slot.id) && !string.Equals(slot.group, "head", StringComparison.OrdinalIgnoreCase);

    // What hangs from the head under the pieces fitted over it (Worn.OnHead: a face fit, on the face or round the back of
    // the head, wherever the look wears it): the character's own head meshes (hair,
    // ears, the face) and items worn in the head's slots. A piece the face piece covers whole (FaceFit.csv hide, as
    // SkinCover measures it: a hairpin under the mask) goes out of sight; of the rest, what comes out through the face
    // piece is left out (OutfitFit.Trim, which hides nothing behind it: a face shows beside a mask). Measured on the
    // FBX files (2026-11-13), a few hair vertices stood out through the welding mask on each character, nothing of a
    // face.
    private static void UnderFace(Transform root, string skinId, Animator animator, Renderer[] own, List<Worn> worn, HashSet<string> covered,
        Layering layering)
    {
        var onFace = new List<Renderer>();
        foreach (var piece in worn)
            if (piece.OnHead && !covered.Contains(piece.Slot.id)) onFace.AddRange(piece.Renderers);
        if (onFace.Count == 0) return;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        var under = new List<Renderer>();
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer skinned && renderer.enabled && renderer.gameObject.activeInHierarchy && HeadMeshes.OnHead(skinned, head))
                under.Add(renderer);
        foreach (var piece in worn)
        {
            if (piece.OnHead || covered.Contains(piece.Slot.id) || !string.Equals(piece.Slot.group, "head", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var renderer in piece.Renderers)
                if (renderer is SkinnedMeshRenderer && renderer.enabled) under.Add(renderer);
        }
        float hide = FaceFit.HideShare;
        foreach (var renderer in under)
        {
            if (SkinCover.Covers(root, skinId, renderer, onFace) >= hide) renderer.enabled = false;
            else layering.Under(renderer, onFace, false, false);
        }
    }

    // Pieces take the body's shape keys they lack (OutfitFit.Follow): every piece of clothing drawn, the character's own
    // and the outfit's, against the character's skin (its own meshes that are no piece and don't hang from the head). A
    // piece doesn't follow a key it holds on the body itself (a shoe its heel: CharacterParts.csv needs, OutfitFits.csv
    // shapes); a rigid piece made for anyone (a mask, earrings) follows nothing, nor does what only lies on the body
    // (hair, a tail: SkinCover.IsBare), which its own bones swing. A character with no rows has no pieces to tell from
    // its body.
    private static void FollowBody(GameObject model, string skinId, Animator animator, Renderer[] own, List<Worn> worn, HashSet<string> covered,
        List<Renderer> leaving, Dictionary<string, float> came)
    {
        var parts = OutfitCatalog.PartsOf(skinId);
        if (parts.Count == 0) return;
        var gone = new HashSet<Renderer>(leaving);
        var held = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var lying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in parts)
            foreach (string name in OutfitCatalog.Split(row.parts))
            {
                if (SkinCover.IsBare(row.slot))
                {
                    lying.Add(name);
                    continue;
                }
                if (!held.TryGetValue(name, out var keys)) held[name] = keys = new HashSet<string>(StringComparer.Ordinal);
                keys.UnionWith(KeyNames(row.needs));
                keys.UnionWith(KeyNames(row.shapes));
            }
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var skin = new List<SkinnedMeshRenderer>();
        var pieces = new List<OutfitFit.Follower>();
        foreach (var renderer in own)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null || !Drawn(renderer, gone)) continue;
            if (held.TryGetValue(renderer.name, out var keys)) pieces.Add(new OutfitFit.Follower(skinned, keys, true));
            else if (!lying.Contains(renderer.name) && (head == null || !HeadMeshes.OnHead(skinned, head))) skin.Add(skinned);
        }
        foreach (var piece in worn)
        {
            if (covered.Contains(piece.Slot.id) || SkinCover.IsBare(piece.Slot.id)
                || OutfitCatalog.Model(piece.Item.id, skinId) is not { ForAnyone: false }) continue;
            var keys = new HashSet<string>(KeyNames(OutfitCatalog.FitOf(piece.Item.id, skinId)?.shapes), StringComparer.Ordinal);
            foreach (var renderer in piece.Renderers)
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null && Drawn(renderer, gone))
                    pieces.Add(new OutfitFit.Follower(skinned, keys, false));
        }
        OutfitFit.Follow(model, skinId, animator, skin, pieces, came);
    }

    // The key names of a "name=weight|name" cell.
    private static IEnumerable<string> KeyNames(string shapes)
    {
        foreach (string pair in OutfitCatalog.Split(shapes))
        {
            int equals = pair.IndexOf('=');
            string name = (equals < 0 ? pair : pair.Substring(0, equals)).Trim();
            if (name.Length > 0) yield return name;
        }
    }

    // What shows in a slot: the item worn there, else the character's own pieces of it (CharacterParts.csv) still drawn.
    private static List<Renderer> PiecesIn(string slot, string skinId, Renderer[] own, List<Worn> worn, HashSet<Renderer> gone)
    {
        var found = new List<Renderer>();
        foreach (var piece in worn)
            if (string.Equals(piece.Slot.id, slot, StringComparison.OrdinalIgnoreCase))
                foreach (var renderer in piece.Renderers)
                    if (Drawn(renderer, gone)) found.Add(renderer);
        if (found.Count > 0) return found;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in OutfitCatalog.PartsOf(skinId))
            if (string.Equals(part.slot, slot, StringComparison.OrdinalIgnoreCase)) names.UnionWith(OutfitCatalog.Split(part.parts));
        foreach (var renderer in own)
            if (renderer != null && names.Contains(renderer.name) && Drawn(renderer, gone)) found.Add(renderer);
        return found;
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
