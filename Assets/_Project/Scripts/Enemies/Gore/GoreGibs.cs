using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The pieces that fly off (ZombieGore): an arm or leg cut from the character's own skin, so it wears its sleeve or
/// trouser leg, and gut chunks from a burst head. Which triangles a part takes is worked out once per mesh and part from the bone weights, a part a frame
/// while zombies are about, so a cut only bakes the skin's current pose and copies those triangles out. Pieces are thrown
/// as small rigid bodies on the corpses' layer (players are told to pass through them) and sink after a while.
/// Needs the zombie meshes readable (Synty Zombies.fbx, Read/Write on).
/// </summary>
internal sealed class GoreGibs
{
    // What one part takes from one mesh: the vertices it uses and, per submesh, its triangles in its own numbering.
    private sealed class Shape
    {
        public int[] vertices;
        public int[][] triangles;
    }

    // A burst head throws no skin (an open shell showed its missing inside), so only limbs are worked out.
    private static readonly BodyPart[] Cuttable =
    {
        BodyPart.LeftUpperArm, BodyPart.LeftLowerArm, BodyPart.RightUpperArm, BodyPart.RightLowerArm,
        BodyPart.LeftUpperLeg, BodyPart.LeftLowerLeg, BodyPart.RightUpperLeg, BodyPart.RightLowerLeg,
    };
    private const int PlayerLayerFallback = 31;

    private readonly Dictionary<(Mesh, BodyPart), Shape> shapes = new Dictionary<(Mesh, BodyPart), Shape>();
    private readonly Queue<(GoreBody body, BodyPart part)> warming = new Queue<(GoreBody, BodyPart)>();
    private readonly List<GoreGib> live = new List<GoreGib>();
    private readonly List<Vector3> positions = new List<Vector3>(), normals = new List<Vector3>(), piecePositions = new List<Vector3>(),
        pieceNormals = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>(), pieceUvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();
    private Mesh baked;
    private readonly ZombieGore owner;

    public GoreGibs(ZombieGore owner) => this.owner = owner;

    /// <summary>The corpses' layer: the players' own, which everything the players test their movement against leaves out.</summary>
    public static int Layer
    {
        get
        {
            foreach (var player in PlayerRoster.All) if (player != null) return player.gameObject.layer;
            return PlayerLayerFallback;
        }
    }

    /// <summary>Works out this body's parts ahead, one a frame (Tick), unless its meshes are known already.</summary>
    public void Warm(GoreBody body)
    {
        foreach (var part in Cuttable) warming.Enqueue((body, part));
    }

    public void Tick()
    {
        while (warming.Count > 0)
        {
            var (body, part) = warming.Dequeue();
            if (body.Model == null) continue;
            bool made = false;
            for (int i = 0; i < body.Skins.Length; i++)
            {
                var skin = body.Skins[i];
                if (skin == null || skin.sharedMesh == null || shapes.ContainsKey((skin.sharedMesh, part))) continue;
                ShapeOf(body, i, part);
                made = true;
            }
            if (made) break;
        }
        for (int i = live.Count - 1; i >= 0; i--) if (live[i] == null) live.RemoveAt(i);
    }

    private Shape ShapeOf(GoreBody body, int skinIndex, BodyPart part)
    {
        var skin = body.Skins[skinIndex];
        var mesh = skin.sharedMesh;
        if (mesh == null) return null;
        if (shapes.TryGetValue((mesh, part), out var known)) return known;
        var root = body.Bone(BodyParts.Root(part));
        var rest = body.RestBones[skinIndex];
        Shape shape = null;
        var weights = root != null ? mesh.boneWeights : null;
        if (weights != null && weights.Length == mesh.vertexCount && rest != null)
        {
            var inPart = new bool[rest.Length];
            for (int i = 0; i < rest.Length; i++) inPart[i] = rest[i] != null && rest[i].IsChildOf(root);
            var share = new float[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                share[v] = In(inPart, w.boneIndex0) * w.weight0 + In(inPart, w.boneIndex1) * w.weight1
                    + In(inPart, w.boneIndex2) * w.weight2 + In(inPart, w.boneIndex3) * w.weight3;
            }
            var map = new int[weights.Length];
            for (int v = 0; v < map.Length; v++) map[v] = -1;
            var used = new List<int>();
            var lists = new int[mesh.subMeshCount][];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var source = mesh.GetTriangles(s);
                var kept = new List<int>();
                for (int t = 0; t + 2 < source.Length; t += 3)
                {
                    int a = source[t], b = source[t + 1], c = source[t + 2];
                    // A triangle mostly on the part's bones goes with it.
                    if (share[a] + share[b] + share[c] < 1.5f) continue;
                    kept.Add(Index(map, used, a)); kept.Add(Index(map, used, b)); kept.Add(Index(map, used, c));
                }
                lists[s] = kept.ToArray();
            }
            if (used.Count > 0) shape = new Shape { vertices = used.ToArray(), triangles = lists };
        }
        shapes[(mesh, part)] = shape;
        return shape;
    }

    private static float In(bool[] inPart, int bone) => bone >= 0 && bone < inPart.Length && inPart[bone] ? 1f : 0f;

    private static int Index(int[] map, List<int> used, int vertex)
    {
        if (map[vertex] < 0) { map[vertex] = used.Count; used.Add(vertex); }
        return map[vertex];
    }

    /// <summary>
    /// Throws the part off as one piece, cut from the skin as it stands now: away along the shot, up a little, spinning.
    /// Call before GoreBody.Hide, while the skin still has it.
    /// </summary>
    public void Sever(GoreBody body, BodyPart part, Vector3 direction, float speed, Vector3 carried)
    {
        var root = body.Bone(BodyParts.Root(part));
        if (root == null) return;
        for (int i = 0; i < body.Skins.Length; i++)
        {
            if (!Bake(body, i, part, out var shape)) continue;
            var gib = Build(body.Skins[i], shape, "Gore " + part, BodyParts.IsLeg(part) ? 7f : 2.5f);
            if (gib == null) continue;
            var velocity = direction * speed * Random.Range(.75f, 1.1f) + Vector3.up * Random.Range(1f, 2.2f) + carried;
            Throw(gib, velocity, Random.insideUnitSphere * 14f);
            // Its torn end, where it left the body.
            owner.Fx.Cap(gib.transform, root.position, (root.position - root.parent.position).normalized * -1f,
                BodyParts.CutRadius(part));
        }
    }

    // The skin's current pose into `baked` and its vertices into the lists, if the part has a shape on this skin.
    private bool Bake(GoreBody body, int skinIndex, BodyPart part, out Shape shape)
    {
        shape = null;
        var skin = body.Skins[skinIndex];
        if (skin == null || !skin.gameObject.activeInHierarchy) return false;
        shape = ShapeOf(body, skinIndex, part);
        if (shape == null) return false;
        if (baked == null) baked = new Mesh { name = "Gore Bake", hideFlags = HideFlags.HideAndDontSave };
        skin.BakeMesh(baked, true);
        baked.GetVertices(positions);
        baked.GetNormals(normals);
        baked.GetUVs(0, uvs);
        return positions.Count > 0;
    }

    // A rigid piece from the baked vertices: the shape's triangles, pivot at their
    // middle, in the skin's materials. Null when nothing is left.
    private GoreGib Build(SkinnedMeshRenderer skin, Shape shape, string name, float mass)
    {
        piecePositions.Clear(); pieceNormals.Clear(); pieceUvs.Clear();
        var map = new Dictionary<int, int>();
        var lists = new List<int>[shape.triangles.Length];
        Vector3 sum = Vector3.zero;
        for (int s = 0; s < shape.triangles.Length; s++)
        {
            var source = shape.triangles[s];
            List<int> kept = null;
            for (int t = 0; t + 2 < source.Length; t += 3)
            {
                int a = shape.vertices[source[t]], b = shape.vertices[source[t + 1]], c = shape.vertices[source[t + 2]];
                if (a >= positions.Count || b >= positions.Count || c >= positions.Count) continue;
                kept ??= new List<int>();
                kept.Add(Remap(map, a, ref sum)); kept.Add(Remap(map, b, ref sum)); kept.Add(Remap(map, c, ref sum));
            }
            lists[s] = kept;
        }
        if (piecePositions.Count < 3) return null;
        Vector3 middle = sum / piecePositions.Count;
        for (int v = 0; v < piecePositions.Count; v++) piecePositions[v] -= middle;

        var mesh = new Mesh { name = name };
        mesh.SetVertices(piecePositions);
        if (pieceNormals.Count == piecePositions.Count) mesh.SetNormals(pieceNormals);
        if (pieceUvs.Count == piecePositions.Count) mesh.SetUVs(0, pieceUvs);
        var materials = new List<Material>();
        var skinMaterials = skin.sharedMaterials;
        int sub = 0;
        for (int s = 0; s < lists.Length; s++) if (lists[s] != null) sub++;
        mesh.subMeshCount = sub;
        sub = 0;
        for (int s = 0; s < lists.Length; s++)
        {
            if (lists[s] == null) continue;
            mesh.SetTriangles(lists[s], sub++, false);
            materials.Add(skinMaterials.Length > 0 ? skinMaterials[Mathf.Min(s, skinMaterials.Length - 1)] : null);
        }
        mesh.RecalculateBounds();
        if (pieceNormals.Count != piecePositions.Count) mesh.RecalculateNormals();

        var host = new GameObject(name) { layer = Layer };
        host.transform.SetPositionAndRotation(skin.transform.position + skin.transform.rotation * middle, skin.transform.rotation);
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials.ToArray();
        var box = host.AddComponent<BoxCollider>();
        Vector3 size = mesh.bounds.size;
        box.center = mesh.bounds.center;
        box.size = new Vector3(Mathf.Max(.04f, size.x * .8f), Mathf.Max(.04f, size.y * .8f), Mathf.Max(.04f, size.z * .8f));
        var rigid = host.AddComponent<Rigidbody>();
        rigid.mass = mass;
        rigid.interpolation = RigidbodyInterpolation.Interpolate;
        rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var gib = host.AddComponent<GoreGib>();
        gib.Begin(owner, mesh, true);
        Keep(gib);
        return gib;

        int Remap(Dictionary<int, int> lookup, int vertex, ref Vector3 total)
        {
            if (lookup.TryGetValue(vertex, out int index)) return index;
            index = piecePositions.Count;
            lookup[vertex] = index;
            piecePositions.Add(positions[vertex]);
            total += positions[vertex];
            if (vertex < normals.Count) pieceNormals.Add(normals[vertex]);
            if (vertex < uvs.Count) pieceUvs.Add(uvs[vertex]);
            return index;
        }
    }

    /// <summary>A gut chunk thrown from a burst: the RVFX pack's mesh and material on a small rigid body.</summary>
    public void Gut(Vector3 at, Vector3 velocity, float size, int pick)
    {
        var library = GoreLibrary.Instance;
        if (library == null || !library.Gut(pick, out var mesh, out var material)) return;
        var host = new GameObject("Gore Gut") { layer = Layer };
        host.transform.SetPositionAndRotation(at, Random.rotation);
        float scale = size / Mathf.Max(.01f, mesh.bounds.size.magnitude);
        host.transform.localScale = Vector3.one * scale;
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        host.AddComponent<MeshRenderer>().sharedMaterial = material;
        var ball = host.AddComponent<SphereCollider>();
        ball.center = mesh.bounds.center;
        ball.radius = mesh.bounds.extents.magnitude * .5f;
        var rigid = host.AddComponent<Rigidbody>();
        rigid.mass = .3f;
        rigid.interpolation = RigidbodyInterpolation.Interpolate;
        var gib = host.AddComponent<GoreGib>();
        gib.Begin(owner, null, false);
        Keep(gib);
        Throw(gib, velocity, Random.insideUnitSphere * 18f);
    }

    private void Throw(GoreGib gib, Vector3 velocity, Vector3 spin)
    {
        var rigid = gib.GetComponent<Rigidbody>();
        rigid.linearVelocity = velocity;
        rigid.angularVelocity = spin;
        // Players walk through pieces, as through corpses.
        var mine = gib.GetComponent<Collider>();
        foreach (var player in PlayerRoster.All)
        {
            if (player == null) continue;
            foreach (var collider in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, collider, true);
        }
    }

    // Past the most allowed, the oldest starts sinking at once.
    private void Keep(GoreGib gib)
    {
        live.Add(gib);
        int over = 0;
        for (int i = 0; i < live.Count; i++) if (live[i] != null && !live[i].Sinking) over++;
        over -= owner.MostGibs;
        for (int i = 0; i < live.Count && over > 0; i++)
        {
            if (live[i] == null || live[i].Sinking) continue;
            live[i].SinkNow();
            over--;
        }
    }
}
