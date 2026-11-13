using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// BoothPieces: What each piece is. One part of the class; the rest is in BoothPieces.cs and the files beside it.
internal static partial class BoothPieces
{
    // The model's renderers worth sorting, measured; those rows name already come along as hosts for trim.
    private static List<Piece> Measure(BoothUnderwear.Source source, Body body, Dictionary<string, string> named,
        HashSet<string> model, out int off, out int copies)
    {
        off = copies = 0;
        var pieces = new List<Piece>();
        var root = source.Prefab.transform;
        var toCharacter = root.worldToLocalMatrix;
        foreach (var renderer in source.Prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
            var mesh = MeshTools.MeshOf(renderer);
            // Underwear is the underwear slots' (BoothUnderwear), named or not.
            if (mesh == null || BoothUnderwear.IsUnderwear(renderer.name)) continue;
            bool isNamed = named.TryGetValue(renderer.name, out string slot);
            if (!isNamed && !Shown(renderer, root))
            {
                off++;
                continue;
            }
            if (!isNamed && body != null && body.Copies(renderer, mesh))
            {
                copies++;
                continue;
            }
            var piece = new Piece { Renderer = renderer, Name = renderer.name, Named = isNamed, Slot = isNamed ? slot : null };
            piece.Words = AfterPrefix(Words(renderer.name), model);
            var matrix = toCharacter * renderer.transform.localToWorldMatrix;
            if (mesh.isReadable)
            {
                var vertices = mesh.vertices;
                piece.Points = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) piece.Points[i] = matrix.MultiplyPoint3x4(vertices[i]);
                piece.Box = BoxOf(piece.Points);
            }
            else piece.Box = MeshTools.BoxOf(mesh.bounds, matrix);
            if (!isNamed && body != null)
            {
                piece.Regions = body.Regions(renderer, mesh);
                var middle = piece.Box.center - body.Head;
                piece.Front = middle.x * body.Forward.x + middle.z * body.Forward.z;
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    // Each new piece's slot, or the piece it is trim on. Returns how many could not be placed (no word, no body to measure).
    private static int Sort(List<Piece> pieces, Body body)
    {
        int unknown = 0;
        foreach (var piece in pieces.Where(p => !p.Named))
        {
            piece.NameSlot = NameSlot(piece.Name, piece.Words);
            // Named only by trim words, or a slot word with a trim word ("Loose socks Side Ribbon"): trim, on a piece of
            // that slot when the name says one.
            piece.IsTrim = piece.NameSlot == Trim || piece.Words.Any(TrimWords.Contains);
            if (!piece.IsTrim) piece.Slot = piece.NameSlot ?? BodySlot(piece, body);
        }
        var hosts = pieces.Where(p => (p.Named || (!p.IsTrim && p.Slot != null)) && p.Points != null).ToList();
        foreach (var trim in pieces.Where(p => !p.Named && p.IsTrim))
        {
            string wanted = trim.NameSlot != Trim ? trim.NameSlot : null;
            Piece best = null;
            float bestShare = 0f;
            if (trim.Points != null && trim.Points.Length > 0)
                foreach (var host in hosts)
                {
                    if (host == trim || (wanted != null && host.Slot != wanted)) continue;
                    float share = Lies(trim, host);
                    if (share > bestShare)
                    {
                        best = host;
                        bestShare = share;
                    }
                }
            if (best != null && bestShare >= HostShare)
            {
                trim.Host = best;
                trim.Slot = best.Slot;
            }
            else trim.Slot = wanted ?? BodySlot(trim, body);
        }
        foreach (var piece in pieces.Where(p => !p.Named && !(p.Host != null && p.Host.Named)))
            if (piece.Slot == null || OutfitCatalog.Slot(piece.Slot) == null || OutfitCatalog.Slot(piece.Slot).under)
            {
                piece.Slot = null;
                unknown++;
            }
        return unknown;
    }

    // A slot from the piece's name, Trim for trim alone, or null.
    private static string NameSlot(string name, List<string> words)
    {
        for (int i = 0; i + 1 < words.Count; i++)
            foreach (var (first, second, slot) in SlotPairs)
                if (first.Contains(words[i]) && second.Contains(words[i + 1])) return slot ?? Trim;
        foreach (var (slot, slotWords) in SlotWords)
            if (words.Any(word => slotWords.Contains(word))) return slot;
        foreach (var (slot, pieces) in SlotPieces)
            if (pieces.Any(piece => name.IndexOf(piece, StringComparison.Ordinal) >= 0)) return slot;
        // Words run together (Thighbelt, Kneesocks): a long slot word inside them.
        string glued = string.Concat(words);
        foreach (var (slot, slotWords) in SlotWords)
            if (slotWords.Any(word => word.Length >= 5 && glued.IndexOf(word, StringComparison.Ordinal) >= 0)) return slot;
        if (words.Count > 0 && words.All(word => TrimWords.Contains(word) || word.Length <= 2)) return Trim;
        return null;
    }

    // Where the piece's skin hangs, for a piece its name doesn't place; null without a body to measure against.
    private static string BodySlot(Piece piece, Body body)
    {
        if (body == null || piece.Regions == null || piece.Regions.Count == 0) return null;
        float Share(params string[] regions) => regions.Sum(region => piece.Regions.TryGetValue(region, out float weight) ? weight : 0f);
        var box = piece.Box;
        if (Share("head") >= .5f)
        {
            if (piece.Front > FaceFront) return box.center.y > body.Eyes - .02f ? "eyes" : "mask";
            return box.max.y > body.Head.y + .15f ? "helmet" : "hairpin";
        }
        if (Share("foot") >= .6f) return "shoes";
        if (Share("hand", "forearm") >= .6f && Share("hand") >= .2f) return "gloves";
        if (Share("upperarm", "forearm") >= .6f) return "wrist";
        if (Share("foot", "shin", "thigh") >= .6f && box.min.y < body.Ankle + .05f) return "legs";
        if (Share("thigh", "shin") >= .6f) return "thigh";
        if (Share("neck") >= .4f || (Share("neck", "chest") >= .8f && box.min.y > body.Chest + .05f)) return "neck";
        if (Share("hips", "thigh") >= .6f) return box.size.y > .1f ? "bottom" : "waist";
        if (Share("upperarm") >= .15f) return "top";
        if (Share("chest", "belly") >= .6f) return "vest";
        return "top";
    }

    // The share of the trim's vertices (a sample) with one of the host's within Reach.
    private static float Lies(Piece trim, Piece host)
    {
        host.Cells ??= Grid(host.Points);
        int step = Mathf.Max(1, trim.Points.Length / TrimSamples), tried = 0, near = 0;
        for (int i = 0; i < trim.Points.Length; i += step)
        {
            tried++;
            if (Near(host, trim.Points[i])) near++;
        }
        return tried > 0 ? near / (float)tried : 0f;
    }

    private static Dictionary<Vector3Int, List<int>> Grid(Vector3[] points)
    {
        var cells = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < points.Length; i++)
        {
            var key = Cell(points[i]);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
            list.Add(i);
        }
        return cells;
    }

    private static Vector3Int Cell(Vector3 point) =>
        new Vector3Int(Mathf.FloorToInt(point.x / Reach), Mathf.FloorToInt(point.y / Reach), Mathf.FloorToInt(point.z / Reach));

    private static bool Near(Piece host, Vector3 point)
    {
        var key = Cell(point);
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (host.Cells.TryGetValue(new Vector3Int(key.x + x, key.y + y, key.z + z), out var list))
                        foreach (int i in list)
                            if ((host.Points[i] - point).sqrMagnitude <= Reach * Reach) return true;
        return false;
    }
}
