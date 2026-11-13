using System;
using System.Collections.Generic;
using UnityEngine;

// OutfitDresser: pieces that take another's place or come off. One part of the class; the rest is in OutfitDresser.cs and the files beside it.
internal static partial class OutfitDresser
{
    // What of the character the item takes the place of on this character (OutfitFits.csv replaces), or null when it
    // takes its whole slot as pieces do.
    private static string Replaces(OutfitItem item, string skinId)
    {
        string names = OutfitCatalog.FitOf(item.id, skinId)?.replaces;
        return string.IsNullOrWhiteSpace(names) ? null : names;
    }

    // The character's own renderers the worn pieces take the place of go off. Chocolate rice's mirrored side locks put
    // one of Mayo's locks on her other side, right onto the lock already there: a short lock lies along the top fifth of
    // the long one (93% of its vertices within 1 cm of it, measured from the FBX files), so worn beside it the two show
    // through each other. The maker's piece is meant instead of that lock, as its bones say too: its chain carries the
    // names of that lock's. A covered piece is out of sight and takes nothing's place.
    private static void Replace(Renderer[] own, string skinId, List<Worn> worn, HashSet<string> covered)
    {
        HashSet<string> names = null;
        foreach (var piece in worn)
        {
            if (covered.Contains(piece.Slot.id) || Replaces(piece.Item, skinId) is not string replaces) continue;
            names ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.UnionWith(OutfitCatalog.Split(replaces));
        }
        if (names == null) return;
        foreach (var renderer in own)
            if (names.Contains(renderer.name)) renderer.enabled = false;
    }

    // The character's own pieces in these slots go (CharacterParts.csv): their renderers switch off and their hiding
    // keys go on, and the body's keys only those pieces held go back to 0. Underwear no longer comes on here once a dress
    // is off: it has slots of its own and is always on (WearUnderwear). Returns the keys let go.
    private static HashSet<string> TakeOff(Renderer[] own, string skinId, HashSet<string> slots)
    {
        var freed = new HashSet<string>(StringComparer.Ordinal);
        if (slots.Count == 0) return freed;
        var rows = OutfitCatalog.PartsOf(skinId);
        var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skins = new List<SkinnedMeshRenderer>();
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer skinned) skins.Add(skinned);
        foreach (var row in rows)
        {
            if (!slots.Contains(row.slot)) continue;
            off.UnionWith(OutfitCatalog.Split(row.parts));
            Shape(skins, row.shapes);
        }
        Release(skins, rows, slots, off, freed);
        if (off.Count == 0) return freed;
        foreach (var renderer in own)
            if (off.Contains(renderer.name)) renderer.enabled = false;
        return freed;
    }

    // BOOTH bodies come with the skin under their stockings and shoes drawn in (Shrink_Knee_L, Shrink_Foot_R) and the
    // feet shaped for the heels, all set on in the prefab, because they never meant those pieces to come off. Taking the
    // stockings off left Mayo's shins hollow (the user: "마요 스타킹 벗는 거 고장남", 2026-11-11), so each piece names
    // the keys it holds (CharacterParts.csv needs) and a key nothing showing holds any more goes to 0. Keys two pieces
    // share stay while either shows: the feet stay drawn in while the shoes are on, bare shins or not.
    private static void Release(List<SkinnedMeshRenderer> skins, List<OutfitCatalog.Part> rows, HashSet<string> slots,
        HashSet<string> off, HashSet<string> freed)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.needs)) continue;
            (Shows(row, slots, off) ? held : freed).UnionWith(OutfitCatalog.Split(row.needs));
        }
        freed.ExceptWith(held);
        foreach (string name in freed) SetShape(skins, name, 0f);
    }

    // A piece shows while any of its renderers is still on; one made only of keys shows while its slot is kept.
    private static bool Shows(OutfitCatalog.Part row, HashSet<string> slots, HashSet<string> off)
    {
        bool any = false;
        foreach (string piece in OutfitCatalog.Split(row.parts))
        {
            any = true;
            if (!off.Contains(piece)) return true;
        }
        return !any && !slots.Contains(row.slot);
    }
}
