using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Map dressing, stage 6 (the user, 2026-11-13: "데칼 진행"): wear on the layout's boxes as URP decals (the renderer's
/// decal feature, Screen Space): leaks hanging from a wall's top edge, mud splashed at its foot, cracks on walls and
/// floors, oil on roads, grime, mud and leaves on tops. Tables/MapDecals lists the decals, grouped: a material each
/// (Assets/ThirdParty/Quixel/Decals, made by Tools/Props/build_decals.py on URP's own decal shader, the one the bullet
/// holes use), its size as laid, where on a wall it sits, how much it varies, how far it is drawn and how strongly it
/// shows. A Tables/MapSurfaces row says which groups its boxes wear and how thickly (wallDecals per metre of wall,
/// topDecals per square metre of top, "group*density;…"), so every box that wears a surface (a building's look, the
/// map's Concrete or Road) takes its share with no line per map. Every spot comes from the box's place, the same on
/// every build and every PC. A decal is kept only where its whole rectangle lies on that face, nothing stands before it
/// (rays to its middle and corners must meet this box first) and nothing else is inside the thin box it paints (an
/// overlap test), so none smears over a corner, a ledge, a floor or a wall standing on the floor: URP's decal shader
/// paints whatever is inside its box, straight along its depth. Static projectors under one root; each row's far
/// distance lets the small ones go at range.
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string DecalTable = "MapDecals";
    // How deep a decal's box is (m): it paints what lies within half of this of the face, so a body passing (its capsule
    // stays 0.3 m off a wall) or a foot (a sole's thickness) hardly wears it.
    private const float DecalDepth = .2f;
    // A decal's box ends this short of a face's side edges, and this far over the floor or under the top (m).
    private const float DecalEdge = .12f, DecalLift = .03f;
    // Walls lower or shorter than this, and tops narrower, get none (a kerb, a rail, a parapet's top).
    private const float DecalLeastWall = .8f, DecalLeastTop = 1.2f;
    // At most this many of one group on one face or one top (a city block's ground would otherwise take hundreds where
    // nobody walks), and in all, so a dense table can't slow a map.
    private const int DecalMostPerFace = 8, DecalMostPerTop = 40, DecalMost = 1600;
    // A decal fades over this share of its far distance.
    private const float DecalFade = .8f;

    private sealed class DecalKind
    {
        public string Id, Align;
        public Material Material;
        public float Width, Height, Vary, Far, Opacity;
        public bool Spin;
    }

    // The surface row each material was written from (Surface), so a box's decals follow the surface it wears.
    private readonly Dictionary<Material, string> surfaceRows = new Dictionary<Material, string>();
    private Dictionary<string, List<DecalKind>> decalGroups;
    private readonly HashSet<string> decalMissing = new HashSet<string>();
    private Transform decalRoot;
    private int decalsLaid, decalsSkipped;

    // Every box of the layout that wears a surface with decals takes them (see the class), once the layout stands.
    private void Decals()
    {
        Physics.SyncTransforms();
        foreach (var block in blocks)
        {
            if (decalsLaid >= DecalMost) break;
            if (block.Moves || block.Yaw != 0f || block.Shape == null) continue;
            // A dressed box (its model is the look) or an unseen stop shows no face of its own.
            if (!block.Shape.TryGetComponent(out Renderer seen) || !seen.enabled || seen.sharedMaterial == null) continue;
            if (!surfaceRows.TryGetValue(seen.sharedMaterial, out string rowId) || !GameTables.TryGetRow(SurfaceTable, rowId, out var row)) continue;
            if (!block.Shape.TryGetComponent(out Collider own)) continue;
            string walls = Text(row, "wallDecals"), tops = Text(row, "topDecals");
            if (walls.Length > 0) WallDecals(block, own, walls);
            if (tops.Length > 0 && !block.NoStand) TopDecals(block, own, tops);
        }
    }

    // A box's four sides: decals by the metre, each group's spots from the face's own place.
    private void WallDecals(Block block, Collider own, string spec)
    {
        Bounds box = block.Box;
        if (box.size.y < DecalLeastWall) return;
        var faces = new[]
        {
            (normal: Vector3.right, along: Vector3.forward, length: box.size.z, at: new Vector3(box.max.x, box.center.y, box.center.z)),
            (normal: Vector3.left, along: Vector3.forward, length: box.size.z, at: new Vector3(box.min.x, box.center.y, box.center.z)),
            (normal: Vector3.forward, along: Vector3.right, length: box.size.x, at: new Vector3(box.center.x, box.center.y, box.max.z)),
            (normal: Vector3.back, along: Vector3.right, length: box.size.x, at: new Vector3(box.center.x, box.center.y, box.min.z)),
        };
        foreach (var face in faces)
        {
            if (face.length < DecalLeastWall) continue;
            foreach (var (group, density) in DecalSpec(spec))
            {
                var kinds = DecalGroup(group);
                if (kinds == null) continue;
                uint state = SpotHash(face.at) ^ StableHash(group) | 1u;
                int count = Mathf.Min(DecalMostPerFace, Draw(face.length * density, ref state));
                for (int i = 0; i < count && decalsLaid < DecalMost; i++)
                {
                    var kind = kinds[(int)(Next(ref state) % (uint)kinds.Count)];
                    float grow = 1f + (Unit(Next(ref state)) * 2f - 1f) * kind.Vary;
                    float width = kind.Width * grow, height = kind.Height * grow;
                    float room = face.length * .5f - width * .5f - DecalEdge;
                    float shift = (Unit(Next(ref state)) * 2f - 1f) * room, pick = Unit(Next(ref state));
                    if (room < 0f || !WallHeight(kind, box, face.at + face.along * shift, face.normal, height, pick, out float middle))
                    {
                        decalsSkipped++;
                        continue;
                    }
                    var point = new Vector3(face.at.x, middle, face.at.z) + face.along * shift;
                    if (!FaceClear(own, point, face.normal, face.along, Vector3.up, width, height)) { decalsSkipped++; continue; }
                    Project(kind, point, face.normal, 0f, new Vector2(width, height));
                }
            }
        }
    }

    // Where a wall decal's middle sits: hanging from the top edge (top), standing on the floor before the wall (foot: that
    // floor must be the wall's own foot, so a parapet's outer face over the street gets none), or anywhere between.
    private static bool WallHeight(DecalKind kind, Bounds box, Vector3 onFace, Vector3 normal, float height, float pick, out float middle)
    {
        float top = box.max.y - DecalLift, bottom = box.min.y + DecalLift;
        middle = 0f;
        switch (kind.Align)
        {
            case "top":
                middle = top - height * .5f;
                return middle - height * .5f >= bottom;
            case "foot":
            {
                var from = new Vector3(onFace.x, Mathf.Min(box.max.y, box.min.y + 3f), onFace.z) + normal * .15f;
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit floor, 4.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false;
                if (floor.point.y < box.min.y - .05f || floor.point.y > box.min.y + 1.5f) return false;
                middle = floor.point.y + DecalLift + height * .5f;
                return middle + height * .5f <= top;
            }
            default:
            {
                float low = bottom + .3f + height * .5f, high = top - height * .5f;
                if (high < low) return false;
                middle = Mathf.Lerp(low, high, pick);
                return true;
            }
        }
    }

    // A box's top: decals by the square metre, turned at random where the row says so, kept off its edges by their
    // turned corners' reach.
    private void TopDecals(Block block, Collider own, string spec)
    {
        Bounds box = block.Box;
        float sx = box.size.x, sz = box.size.z, top = box.max.y;
        if (Mathf.Min(sx, sz) < DecalLeastTop) return;
        foreach (var (group, density) in DecalSpec(spec))
        {
            var kinds = DecalGroup(group);
            if (kinds == null) continue;
            uint state = SpotHash(new Vector3(box.center.x, top, box.center.z)) ^ StableHash(group) ^ 0x9E3779B9u | 1u;
            int count = Mathf.Min(DecalMostPerTop, Draw(sx * sz * density, ref state));
            for (int i = 0; i < count && decalsLaid < DecalMost; i++)
            {
                var kind = kinds[(int)(Next(ref state) % (uint)kinds.Count)];
                float grow = 1f + (Unit(Next(ref state)) * 2f - 1f) * kind.Vary;
                float width = kind.Width * grow, height = kind.Height * grow;
                float turn = kind.Spin ? Unit(Next(ref state)) * 360f : 0f;
                float reach = Mathf.Sqrt(width * width + height * height) * .5f + DecalEdge;
                float x = Unit(Next(ref state)) * 2f - 1f, z = Unit(Next(ref state)) * 2f - 1f;
                if (reach * 2f > Mathf.Min(sx, sz)) { decalsSkipped++; continue; }
                var point = new Vector3(box.center.x + x * (sx * .5f - reach), top, box.center.z + z * (sz * .5f - reach));
                var spin = Quaternion.AngleAxis(turn, Vector3.up);
                if (!FaceClear(own, point, Vector3.up, spin * Vector3.right, spin * Vector3.forward, width, height)) { decalsSkipped++; continue; }
                Project(kind, point, Vector3.up, turn, new Vector2(width, height));
            }
        }
    }

    // The decal's rectangle (width along across, height along up, on the face through point) lies on this box's face
    // with nothing before it: rays from just off the face to its middle and corners meet this box first, at the face;
    // and nothing else is inside the half of its box that stands off the face.
    private static bool FaceClear(Collider own, Vector3 point, Vector3 normal, Vector3 across, Vector3 up, float width, float height)
    {
        const float off = .25f;
        float hx = width * .5f - .02f, hy = height * .5f - .02f;
        for (int i = 0; i < 5; i++)
        {
            Vector3 spot = point + (i == 0 ? Vector3.zero : across * (i % 2 == 1 ? hx : -hx) + up * (i < 3 ? hy : -hy));
            if (!Physics.Raycast(spot + normal * off, -normal, out RaycastHit hit, off + .1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider != own || Mathf.Abs(hit.distance - off) > .03f) return false;
        }
        var turn = Quaternion.LookRotation(-normal, up);
        float half = DecalDepth * .25f;
        foreach (var other in Physics.OverlapBox(point + normal * (half + .004f), new Vector3(width * .5f, height * .5f, half), turn,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (other != own) return false;
        return true;
    }

    // One projector: its box centred on the surface, painting along its forward into it; the texture's up runs up a wall,
    // and on a floor along the turn.
    private DecalProjector Project(DecalKind kind, Vector3 point, Vector3 normal, float turn, Vector2 size)
    {
        decalRoot ??= Root("◤━━ 데칼 (Tables/MapDecals)");
        var host = new GameObject(kind.Id);
        host.transform.SetParent(decalRoot, false);
        bool flat = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .9f;
        Vector3 up = flat ? Quaternion.AngleAxis(turn, Vector3.up) * Vector3.forward : Quaternion.AngleAxis(turn, normal) * Vector3.up;
        host.transform.SetPositionAndRotation(point, Quaternion.LookRotation(-normal, up));
        var projector = host.AddComponent<DecalProjector>();
        projector.material = kind.Material;
        projector.pivot = Vector3.zero;
        projector.size = new Vector3(size.x, size.y, DecalDepth);
        projector.drawDistance = kind.Far;
        projector.fadeScale = DecalFade;
        projector.fadeFactor = kind.Opacity;
        decalsLaid++;
        return projector;
    }

    // A group's decals from Tables/MapDecals (rows in the table's order), read once per build; null and named in the report
    // when the group has none or its materials are missing.
    private List<DecalKind> DecalGroup(string group)
    {
        if (decalGroups == null)
        {
            decalGroups = new Dictionary<string, List<DecalKind>>();
            foreach (string id in GameTables.RowIds(DecalTable))
            {
                if (!GameTables.TryGetRow(DecalTable, id, out var row)) continue;
                string path = "Assets/" + Text(row, "material");
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    decalMissing.Add(id + " (" + path + ")");
                    continue;
                }
                var kind = new DecalKind
                {
                    Id = id, Align = Text(row, "align").ToLowerInvariant(), Material = material,
                    Width = Mathf.Max(.05f, Number(row, "width", 1f)), Height = Mathf.Max(.05f, Number(row, "height", 1f)),
                    Vary = Mathf.Clamp(Number(row, "vary", .3f), 0f, .9f), Far = Mathf.Max(5f, Number(row, "far", 40f)),
                    Opacity = Mathf.Clamp01(Number(row, "opacity", 1f)), Spin = Number(row, "spin", 0f) > .5f
                };
                string name = Text(row, "group");
                if (!decalGroups.TryGetValue(name, out var list)) decalGroups[name] = list = new List<DecalKind>();
                list.Add(kind);
            }
        }
        if (decalGroups.TryGetValue(group, out var kinds) && kinds.Count > 0) return kinds;
        decalMissing.Add(group);
        return null;
    }

    // "group*density;…" (a group alone counts 0.05).
    private static List<(string group, float density)> DecalSpec(string text)
    {
        var spec = new List<(string, float)>();
        foreach (string part in text.Split(';'))
        {
            string item = part.Trim();
            if (item.Length == 0) continue;
            int star = item.IndexOf('*');
            string group = (star < 0 ? item : item.Substring(0, star)).Trim();
            float density = .05f;
            if (star >= 0 && !float.TryParse(item.Substring(star + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out density)) continue;
            if (group.Length > 0 && density > 0f) spec.Add((group, density));
        }
        return spec;
    }

    // How many of an expected count: the whole part, and one more as often as the rest says.
    private static int Draw(float expected, ref uint state)
    {
        int whole = Mathf.FloorToInt(Mathf.Max(0f, expected));
        return whole + (Unit(Next(ref state)) < expected - whole ? 1 : 0);
    }

    // A string's hash that is the same on every run (string.GetHashCode needn't be).
    private static uint StableHash(string text)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in text) h = (h ^ c) * 16777619u;
            return h;
        }
    }

    // For the build's report.
    private string DecalReport()
    {
        if (decalsLaid == 0 && decalMissing.Count == 0) return "";
        return "\n데칼(Tables/MapDecals): " + decalsLaid + "개, 자리가 맞지 않아 뺀 자리 " + decalsSkipped + "곳" +
               (decalMissing.Count > 0 ? " (못 찾은 데칼: " + string.Join(", ", decalMissing) + ")" : "");
    }
}
