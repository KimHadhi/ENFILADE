using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Sorts the meshes of an outfit, or of a character as its maker dressed it, into the wardrobe's slots: a shirt that
/// came with stockings, a thigh belt and necklaces arrives as things one can wear apart (the user, 2026-11-12: "왜
/// 셔츠인데 아래에 스타킹이나 가터 벨트까지 있어? 따로 분리 돼 이거?"). What a piece is:
/// - its name first: the words makers give pieces (Stocking, Choker, Necklace, Glasses, ストッキング, 스타킹 …), whole
///   words after the maker's prefix (Banshaku Shirt_, SM1_), word pairs (Leg belt, Knee pad, T-Shirts) and words run
///   together (Thighbelt);
/// - trim, named only so (a ribbon, tie, knot, tag), goes with the piece it lies on, the one with most of its vertices
///   within 2 cm: the sailor jacket's sleeve ribbons and front tie, a sock's ribbon, the name tag on a jacket;
/// - else, for an outfit's piece, where its skin hangs: its bone weights by body region, and for a head piece how far
///   it stands before the face (glasses at the eyes, a toast in the mouth is a mask). A character's own mesh is never
///   placed this way: what its name doesn't place may be its body, and stays on;
/// - underwear, named so (bra, bikini, panty, 下着 …), goes in the underwear slots, a top or a bottom by its name or
///   by where it sits (<see cref="UnderSlot"/>).
/// A slot's pieces make one item; several of different kinds (a stocking and socks) are each an item too, beside the
/// set of them that is the maker's look. A piece switched off in the prefab (the other choice of a VRChat toggle) and a
/// copy of the character's own body carried in an outfit's file are marked, for the caller to leave out.
/// Two things run it, so the rules are written once: the game's own import of BOOTH outfits (BoothPieces, which
/// writes the result into the game's tables) and the mod exporter (ModAvatarExporter, which shows it to the mod's
/// maker to correct and writes it into the mod's file). It is part of the exporter's package, so it uses nothing of
/// the game's: Unity's own types only.
/// The rules were checked on every outfit the game has (Sailor Maiden, White Board, Sera Yura, Banshaku Shirt, the
/// Ichigo shirt, the mirrored side locks, the Ene earrings) against the rows written by hand, 2026-11-12.
/// </summary>
public static partial class ModPieces
{
    // Trim lies on the piece that has one of its vertices within Reach (m) of at least HostShare of the trim's.
    private const float Reach = .02f, HostShare = .5f;
    // A head piece whose middle stands this far (m) before the head bone is on the face: glasses 5.5-6.1 cm, Sera Yura's
    // toast 9-10 cm and its jam 5.8-6.7 cm, an earring 1 cm, a beret behind it (Mayo and Ichigo, 2026-11-12).
    private const float FaceFront = .045f;
    // How many of a trim piece's vertices are tried against the pieces it may lie on.
    private const int TrimSamples = 400;

    /// <summary>One mesh of the model, measured (<see cref="Measure"/>) and then sorted (<see cref="Sort"/>).</summary>
    public sealed class Piece
    {
        public Renderer Renderer;
        /// <summary>Its object's name, and the slot it goes in: null until sorted, and after it when nothing places it.</summary>
        public string Name, Slot;
        /// <summary>Its words after the maker's prefix.</summary>
        public List<string> Words;
        /// <summary>Already in a row of the game's tables: never sorted again, only a host for new trim.</summary>
        public bool Named;
        /// <summary>Its slot was chosen by hand: never sorted, a host for trim, and an item like any other.</summary>
        public bool Given;
        /// <summary>Switched off as the prefab comes (itself or an object above it): the other choice of a toggle.</summary>
        public bool Off;
        /// <summary>A copy of the character's own mesh, carried in an outfit's file to fit the clothes on.</summary>
        public bool Copy;
        /// <summary>Named like underwear; <see cref="Both"/> when it reaches from the hips to the chest (a bodysuit).</summary>
        public bool Under, Both;
        /// <summary>Trim, and the piece it lies on (null when it lies on none and went by its own name or place).</summary>
        public bool IsTrim;
        public Piece Host;
        /// <summary>The share of its skin weight on each part of the body; null without a body to measure against.</summary>
        public Dictionary<string, float> Regions;
        public Bounds Box;
        /// <summary>How far its middle stands before the head bone (m).</summary>
        public float Front;
        /// <summary>Its vertices as made, in the model's space (null when its mesh can't be read), and a grid of them.</summary>
        public Vector3[] Points;
        internal Dictionary<Vector3Int, List<int>> Cells;
        internal string NameSlot;
    }

    /// <summary>
    /// Gives each piece its slot, or the piece it is trim on. Pieces <see cref="Piece.Named"/>, <see cref="Piece.Given"/>
    /// or <see cref="Piece.Under"/> keep the slot they came with. Returns how many could not be placed (no word, no
    /// body to measure, or a slot <paramref name="sortable"/> refuses); their slot is null.
    /// </summary>
    /// <param name="byBody">
    /// Place what no name places by where its skin hangs. False for a character's own meshes: one of them is the body.
    /// </param>
    public static int Sort(List<Piece> pieces, Body body, Func<string, bool> sortable, bool byBody = true)
    {
        int unknown = 0;
        foreach (var piece in pieces.Where(p => !Fixed(p)))
        {
            piece.NameSlot = NameSlot(piece.Name, piece.Words);
            // Named only by trim words, or a slot word with a trim word ("Loose socks Side Ribbon"): trim, on a piece of
            // that slot when the name says one. An outfit's piece with a trim word among others its name doesn't place
            // (Sleeve Side Cross) is trim too. A character's own mesh so named is not: one of its meshes is its body,
            // and a body called "Body_Front" must never be taken for trim on the clothes that cover it.
            piece.IsTrim = piece.NameSlot == Trim || ((byBody || piece.NameSlot != null) && piece.Words.Any(TrimWords.Contains));
            if (!piece.IsTrim) piece.Slot = piece.NameSlot ?? (byBody ? BodySlot(piece, body) : null);
        }
        var hosts = pieces.Where(p => (Fixed(p) || (!p.IsTrim && p.Slot != null)) && p.Slot != null && p.Points != null).ToList();
        foreach (var trim in pieces.Where(p => !Fixed(p) && p.IsTrim))
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
            else trim.Slot = wanted ?? (byBody ? BodySlot(trim, body) : null);
        }
        // Trim on a piece that came with its slot goes wherever that piece is, an underwear slot too.
        foreach (var piece in pieces.Where(p => !Fixed(p) && !(p.Host != null && Fixed(p.Host))))
            if (piece.Slot == null || !sortable(piece.Slot))
            {
                piece.Slot = null;
                unknown++;
            }
        return unknown;
    }

    private static bool Fixed(Piece piece) => piece.Named || piece.Given || piece.Under;

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

    /// <summary>
    /// The items the sorted pieces make (those not <see cref="Piece.Named"/>): per slot, one item of all its pieces (each
    /// with the trim on it), and when they are of different kinds each alone too. Underwear makes one item per
    /// underwear slot, the top taking the bottom too when a piece of it reaches from the hips to the chest. Each: its id
    /// (the model's id, its slot and for a single piece its name), slot, the slots it also fills, its meshes' names and
    /// its name's words after <paramref name="title"/>.
    /// </summary>
    public static IEnumerable<(string Id, string Slot, string Also, List<string> Parts, string Words)> Items(List<Piece> pieces, string modelId,
        ICollection<string> model, string title)
    {
        var hosts = pieces.Where(p => !p.Named && !p.IsTrim && p.Slot != null)
            .Concat(pieces.Where(p => !p.Named && p.IsTrim && p.Host == null && p.Slot != null)).ToList();
        foreach (var group in hosts.GroupBy(p => p.Slot, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            List<string> PartsOf(IEnumerable<Piece> those) => those.SelectMany(host => new[] { host }.Concat(pieces.Where(t => t.Host == host)))
                .Select(p => p.Name).Distinct().ToList();
            var readable = members.Select(p => Readable(p.Name, model)).ToList();
            string together = string.Join(" · ", readable.Select(words => Regex.Replace(words, @"\s*\d+$", "")).Distinct());
            bool under = IsUnder(group.Key);
            string also = under && string.Equals(group.Key, UnderTop, StringComparison.OrdinalIgnoreCase) && members.Any(p => p.Both) ? UnderBottom : "";
            yield return (modelId + "-" + group.Key, group.Key, also, PartsOf(members), title + " " + together);
            // Underwear is one thing per slot: a bikini top and its string are never worn apart.
            if (under) continue;
            int kinds = members.Select(p => KindOf(p.Words)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (members.Count < 2 || kinds < members.Count) continue;
            for (int i = 0; i < members.Count; i++)
                yield return (modelId + "-" + group.Key + "-" + Slug(readable[i]), group.Key, "", PartsOf(new[] { members[i] }), title + " " + readable[i]);
        }
    }
}
