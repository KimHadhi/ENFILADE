using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The body itself round one place of it, asked where something pressed against it comes to rest: from a place inside the
/// body, how far along a line the body ends (<see cref="Rest"/>).
/// "The body ends" is told from the shapes of its meshes alone, so it holds for any character, a mod's too, and no list
/// says which meshes are what. From a place inside the body a line goes out through its skin and through whatever is worn
/// round the body there, and going the other way it goes out through the same at the back. So a surface is the body's, or
/// worn round it, where the line passes out through it and passes out through the same mesh behind as well; the
/// outermost such surface ahead is where the body ends. What that leaves out:
/// - a lump the line passes in through and out of again: a thing inside the body (one character carries a skeleton's
///   bones in hers) or beyond it (a lock of hair);
/// - what is before the body only: a bow, a collar, a badge on the chest.
/// What rests against a body is put there by this: a stock's butt on the front of the shoulder, in play on every character
/// (HoldBody) and when the gun clips are made on the reference body (GunClipRetarget), by the one measure.
/// Read once per body: which of the skinned meshes it wears have any surface near the place, and which of their
/// triangles. Posing bakes only those meshes as the body stands now, and any number of lines can be asked of one pose.
/// A mesh whose import doesn't keep Read/Write can't be read in a build and is left out (SkinSample warns of those).
/// </summary>
public sealed class BodyFront
{
    // One mesh the body wears: its vertices that are surface near the place, the bone that mainly carries each, and the
    // triangles between them (three places in Vertices each).
    private sealed class Part
    {
        public SkinnedMeshRenderer Renderer;
        public int[] Vertices, Triangles;
        public Transform[] Carriers;
        // Its longest triangle side as it was read (m).
        public float Edge;
        // Where its vertices begin in the posed list, and how many of them are there.
        public int First, Count;
    }

    // A line counts only where it passes a surface further than this ahead of where it starts (m).
    private const float Least = 1e-4f;
    // Two crossings nearer each other than this are one surface (m): a sheet made of two faces back to back, or an edge
    // two triangles share. The thinnest solid met inside a body so far was a rib 1 mm through.
    private const float Same = 1e-4f;

    private static readonly List<Vector3> Baked = new List<Vector3>();
    private static readonly List<BoneWeight> Weights = new List<BoneWeight>();
    private static readonly List<int> Kept = new List<int>(), Corners = new List<int>();
    private static readonly List<Transform> Carriers = new List<Transform>();
    private static readonly List<float> Met = new List<float>();
    // The triangles near the line asked about: three vertices of the posed list each, and which part each is of.
    private static readonly List<int> MetAt = new List<int>(), Near = new List<int>(), NearPart = new List<int>();
    // One line's crossings: how far ahead (under zero: passing in through the surface's front), at which vertex, of which part.
    private static readonly List<float> Crossings = new List<float>();
    private static readonly List<int> CrossingAt = new List<int>(), CrossingPart = new List<int>();
    // The parts a line leaves going one way.
    private static readonly List<int> Left = new List<int>(), LeftAt = new List<int>(), Behind = new List<int>();
    private static readonly List<float> LeftAhead = new List<float>();

    private readonly List<Part> parts = new List<Part>();
    // Every part's kept vertices in the world, as the body was last posed.
    private readonly List<Vector3> posed = new List<Vector3>();
    private Transform body;
    private int worn;
    // The body's size as it was read, for lengths kept from then.
    private float size = 1f;

    /// <summary>Whether any of the body's surface near the place could be read.</summary>
    public bool Measured => parts.Count > 0;

    /// <summary>How many vertices of how many meshes are the body's surface near the place.</summary>
    public int Points { get; private set; }
    public int Meshes => parts.Count;

    private BodyFront() { }

    /// <summary>
    /// Reads <paramref name="body"/>'s surface within <paramref name="within"/> (m) of <paramref name="near"/> as it stands
    /// now. It is posed as read.
    /// </summary>
    public static BodyFront Read(Transform body, Vector3 near, float within)
    {
        var front = new BodyFront { body = body };
        if (body == null) return front;
        front.worn = Worn(body);
        front.size = Mathf.Max(1e-6f, Mathf.Abs(body.lossyScale.x));
        float reach = within * within;
        var baked = new Mesh { name = "Body front measurement", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = renderer.sharedMesh;
                if (!renderer.enabled || mesh == null || !mesh.isReadable) continue;
                renderer.BakeMesh(baked, true);
                baked.GetVertices(Baked);
                mesh.GetBoneWeights(Weights);
                var bones = renderer.bones;
                // Baked in the renderer's own space with its scale taken out: its own matrix places it, whatever its scale.
                Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                Kept.Clear();
                Carriers.Clear();
                // Each vertex's place among the kept ones; none for one that isn't.
                var place = new int[Baked.Count];
                int first = front.posed.Count;
                for (int i = 0; i < Baked.Count; i++)
                {
                    place[i] = -1;
                    Vector3 at = toWorld.MultiplyPoint3x4(Baked[i]);
                    if ((at - near).sqrMagnitude > reach) continue;
                    // The vertex's main bone (Unity keeps the heaviest first).
                    int bone = i < Weights.Count ? Weights[i].boneIndex0 : -1;
                    place[i] = Kept.Count;
                    Kept.Add(i);
                    Carriers.Add(bones != null && bone >= 0 && bone < bones.Length ? bones[bone] : null);
                    front.posed.Add(at);
                }
                Corners.Clear();
                float edge = 0f;
                if (Kept.Count > 0)
                {
                    var triangles = mesh.triangles;
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        int a = place[triangles[t]], b = place[triangles[t + 1]], c = place[triangles[t + 2]];
                        if (a < 0 || b < 0 || c < 0) continue;
                        Corners.Add(a);
                        Corners.Add(b);
                        Corners.Add(c);
                        Vector3 pa = front.posed[first + a], pb = front.posed[first + b], pc = front.posed[first + c];
                        edge = Mathf.Max(edge, Mathf.Max((pb - pa).sqrMagnitude, Mathf.Max((pc - pb).sqrMagnitude, (pa - pc).sqrMagnitude)));
                    }
                }
                if (Corners.Count > 0)
                    front.parts.Add(new Part
                    {
                        Renderer = renderer, Vertices = Kept.ToArray(), Carriers = Carriers.ToArray(), Triangles = Corners.ToArray(),
                        Edge = Mathf.Sqrt(edge), First = first, Count = Kept.Count
                    });
                else front.posed.RemoveRange(first, front.posed.Count - first);
                baked.Clear();
            }
        }
        finally
        {
            Release(baked);
        }
        front.Points = front.posed.Count;
        return front;
    }

    /// <summary>
    /// Whether this still reads <paramref name="of"/> as it is dressed: the same body wearing the same meshes. A piece put
    /// on or taken off since asks for a new reading.
    /// </summary>
    public bool Reads(Transform of) => of != null && of == body && Worn(of) == worn;

    /// <summary>Takes the body's surface as it stands now: what Rest answers from until the next pose.</summary>
    public void Pose()
    {
        posed.Clear();
        if (parts.Count == 0) return;
        var baked = new Mesh { name = "Body front measurement", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            foreach (var part in parts)
            {
                var renderer = part.Renderer;
                part.First = posed.Count;
                part.Count = 0;
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
                renderer.BakeMesh(baked, true);
                baked.GetVertices(Baked);
                // A mesh swapped for another since it was read is not the surface that was read.
                if (Baked.Count > part.Vertices[part.Vertices.Length - 1])
                {
                    Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                    foreach (int index in part.Vertices) posed.Add(toWorld.MultiplyPoint3x4(Baked[index]));
                    part.Count = part.Vertices.Length;
                }
                baked.Clear();
            }
        }
        finally
        {
            Release(baked);
        }
    }

    /// <summary>
    /// Where a flat pad pushed against the body comes to rest: how far ahead of <paramref name="inside"/> (a place inside
    /// the body), along <paramref name="along"/> (unit), the body ends, in metres. Five lines are asked, through the place
    /// and <paramref name="pad"/> (m) to each side of it and over and under it (<paramref name="up"/> is roughly up), and the
    /// middle one of where they leave the body is the answer: a seam or a hole one line slips through, or a fold one line
    /// catches, doesn't move it. <paramref name="met"/> is the vertex it is at, for <see cref="Named"/>. False where no line
    /// leaves the body.
    /// </summary>
    public bool Rest(Vector3 inside, Vector3 along, Vector3 up, float pad, out float ahead, out int met)
    {
        Vector3 side = Vector3.Cross(up, along);
        side = side.sqrMagnitude > 1e-8f ? side.normalized : Vector3.right;
        Vector3 over = Vector3.Cross(along, side);
        FindNear(inside, along, pad);
        Met.Clear();
        MetAt.Clear();
        for (int line = 0; line < 5; line++)
        {
            Vector3 from = inside + (line == 1 ? side : line == 2 ? -side : line == 3 ? over : line == 4 ? -over : Vector3.zero) * pad;
            if (!Leaves(from, along, out float at, out int vertex)) continue;
            // Kept in order, nearest first.
            int place = Met.Count;
            while (place > 0 && Met[place - 1] > at) place--;
            Met.Insert(place, at);
            MetAt.Insert(place, vertex);
        }
        ahead = 0f;
        met = -1;
        if (Met.Count == 0) return false;
        ahead = Met[Met.Count / 2];
        met = MetAt[Met.Count / 2];
        return true;
    }

    /// <summary>What of the body a vertex Rest met is: the mesh, and the bone that mainly carries it there (for the reports).</summary>
    public string Named(int met)
    {
        foreach (var part in parts)
        {
            if (met < part.First || met >= part.First + part.Count) continue;
            var carrier = part.Carriers[met - part.First];
            return $"{(part.Renderer != null ? part.Renderer.name : "a mesh taken off")} on {(carrier != null ? carrier.name : "no bone")}";
        }
        return "nothing";
    }

    /// <summary>
    /// Every surface the one line from <paramref name="inside"/> along <paramref name="along"/> passes and how far ahead
    /// (cm), nearest first, + where it goes out through the surface's front and - where it comes in through it, for the
    /// reports: what Rest chose among, as the body was last posed.
    /// </summary>
    public string Through(Vector3 inside, Vector3 along)
    {
        FindNear(inside, along, 0f);
        Cross(inside, along);
        if (Crossings.Count == 0) return "nothing";
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < Crossings.Count && i < 12; i++)
        {
            if (i > 0) text.Append(", ");
            string name = "?";
            foreach (var part in parts)
                if (CrossingAt[i] >= part.First && CrossingAt[i] < part.First + part.Count && part.Renderer != null) name = part.Renderer.name;
            text.Append($"{name} {(Crossings[i] > 0f ? "+" : "-")}{Mathf.Abs(Crossings[i]) * 100f:0.0}");
        }
        return text.ToString();
    }

    // The triangles with a corner near enough the line for it, or one pad to its side, to pass through them: one look at each
    // triangle here saves testing nearly all of them for every line.
    private void FindNear(Vector3 inside, Vector3 along, float pad)
    {
        Near.Clear();
        NearPart.Clear();
        float grown = body != null ? Mathf.Abs(body.lossyScale.x) / size : 1f;
        for (int index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (part.Count == 0) continue;
            float reach = pad + part.Edge * grown;
            reach *= reach;
            var corners = part.Triangles;
            for (int t = 0; t < corners.Length; t += 3)
            {
                Vector3 offset = posed[part.First + corners[t]] - inside;
                if ((offset - along * Vector3.Dot(offset, along)).sqrMagnitude > reach) continue;
                Near.Add(part.First + corners[t]);
                Near.Add(part.First + corners[t + 1]);
                Near.Add(part.First + corners[t + 2]);
                NearPart.Add(index);
            }
        }
    }

    // Where a line from a place inside the body leaves the body: the outermost surface ahead that it passes out through, of
    // a mesh it passes out through behind as well. Where no mesh is round the place so (a body whose front and back are
    // different meshes), the first surface it passes out through.
    private bool Leaves(Vector3 from, Vector3 along, out float ahead, out int met)
    {
        Cross(from, -along);
        LeftThrough();
        Behind.Clear();
        Behind.AddRange(Left);
        Cross(from, along);
        LeftThrough();
        ahead = 0f;
        met = -1;
        for (int i = 0; i < Left.Count; i++)
        {
            if (!Behind.Contains(Left[i])) continue;
            ahead = LeftAhead[i];
            met = LeftAt[i];
        }
        if (met < 0 && Left.Count > 0)
        {
            ahead = LeftAhead[0];
            met = LeftAt[0];
        }
        return met >= 0;
    }

    // Of the crossings just found (Cross), the surfaces the line passes out through that it had not first passed in
    // through, nearest first: which part each is of (Left), how far (LeftAhead) and at which vertex (LeftAt). What the line
    // passes in through it is inside until it passes out of it again: a lump, not a surface it leaves. Crossings at one
    // place are one surface: an edge two triangles share, or a sheet of two faces back to back, which is passed out
    // through like any surface.
    private static void LeftThrough()
    {
        Left.Clear();
        LeftAhead.Clear();
        LeftAt.Clear();
        int within = 0;
        for (int i = 0; i < Crossings.Count;)
        {
            float at = Mathf.Abs(Crossings[i]);
            bool goesOut = false, comesIn = false;
            int vertex = -1, part = -1;
            for (; i < Crossings.Count && Mathf.Abs(Crossings[i]) - at < Same; i++)
            {
                if (Crossings[i] < 0f) comesIn = true;
                else if (!goesOut)
                {
                    goesOut = true;
                    vertex = CrossingAt[i];
                    part = CrossingPart[i];
                }
            }
            if (!goesOut) within++;
            else if (within > 0)
            {
                // Inside a lump: this is its far side, unless it is a sheet inside it.
                if (!comesIn) within--;
            }
            else
            {
                Left.Add(part);
                LeftAhead.Add(at);
                LeftAt.Add(vertex);
            }
        }
    }

    // Every crossing of the line with the triangles near it, nearest first (Crossings: under zero for one that comes in
    // through the surface's front).
    private void Cross(Vector3 from, Vector3 along)
    {
        Crossings.Clear();
        CrossingAt.Clear();
        CrossingPart.Clear();
        for (int t = 0, n = 0; t < Near.Count; t += 3, n++)
        {
            // Möller and Trumbore's test.
            Vector3 a = posed[Near[t]];
            Vector3 ab = posed[Near[t + 1]] - a, ac = posed[Near[t + 2]] - a;
            Vector3 p = Vector3.Cross(along, ac);
            float det = Vector3.Dot(ab, p);
            if (det > -1e-12f && det < 1e-12f) continue;
            float inv = 1f / det;
            Vector3 s = from - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) continue;
            Vector3 q = Vector3.Cross(s, ab);
            float v = Vector3.Dot(along, q) * inv;
            if (v < 0f || u + v > 1f) continue;
            float at = Vector3.Dot(ac, q) * inv;
            if (at <= Least) continue;
            // Unity's triangles wind clockwise seen from their front: the line goes out through a front it meets from behind.
            float signed = Vector3.Dot(Vector3.Cross(ab, ac), along) > 0f ? at : -at;
            int place = Crossings.Count;
            while (place > 0 && Mathf.Abs(Crossings[place - 1]) > at) place--;
            Crossings.Insert(place, signed);
            CrossingAt.Insert(place, Near[t]);
            CrossingPart.Insert(place, NearPart[n]);
        }
    }

    // What the body wears now, as one number: every shown skinned mesh and which mesh it shows.
    private static int Worn(Transform body)
    {
        int worn = 17;
        foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!renderer.enabled || renderer.sharedMesh == null) continue;
            unchecked { worn = (worn * 31 + renderer.GetInstanceID()) * 31 + renderer.sharedMesh.GetInstanceID(); }
        }
        return worn;
    }

    // The editor measures bodies too (the gun bake, the hold check), where only DestroyImmediate may be called.
    private static void Release(Mesh mesh)
    {
        if (Application.isPlaying) Object.Destroy(mesh);
        else Object.DestroyImmediate(mesh);
    }
}
