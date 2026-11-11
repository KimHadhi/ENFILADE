using UnityEngine;

/// <summary>
/// The katana's stand-in until a model is dropped in Art/Models/Weapons/Katana (MeleeModels): a curved blade made in code
/// (diamond section with a ridge line, the edge strips a brighter steel than the flats, tapering to a point that rises to
/// the back), a brass collar, an oval iron guard, and a wrapped handle with its diamond pattern, made of Unity's own
/// cylinder and cube. Its axes are the ones a dropped-in model is asked for: the origin where the blade meets the guard,
/// +Z along the blade to the tip, +Y the blade's back (the edge faces -Y). No colliders: it is only seen.
/// </summary>
internal static class KatanaModel
{
    // A real katana's proportions (m): the blade's width at the collar and before the point, how far the point runs, its
    // thickness at the back, and how far the tip curves up off the handle's line (its sori).
    private const float BaseWidth = .031f, TipWidth = .024f, PointShare = .07f, BackThickness = .0072f, Curve = .022f;
    private const int Sections = 28;

    private static Material steel, edge, iron, brass, wrap;
    private static Mesh blade;
    private static float bladeMadeFor;

    /// <summary>Builds the stand-in under parent and returns its root. Lengths in metres.</summary>
    public static Transform Build(Transform parent, float bladeLength, float handleLength)
    {
        var root = new GameObject("Katana (stand-in)").transform;
        root.SetParent(parent, false);
        bladeLength = Mathf.Max(.2f, bladeLength);
        handleLength = Mathf.Max(.1f, handleLength);

        var bladePart = new GameObject("Blade", typeof(MeshFilter), typeof(MeshRenderer));
        bladePart.transform.SetParent(root, false);
        bladePart.GetComponent<MeshFilter>().sharedMesh = Blade(bladeLength);
        bladePart.GetComponent<MeshRenderer>().sharedMaterials = new[] { Steel, Edge };

        // The collar on the blade's root, then the guard, the handle and its two caps behind it.
        Part(root, "Habaki", Cube, new Vector3(0f, 0f, .015f), Quaternion.identity, new Vector3(.0088f, BaseWidth + .003f, .03f), Brass);
        var lying = Quaternion.Euler(90f, 0f, 0f);
        Part(root, "Tsuba", Cylinder, new Vector3(0f, 0f, -.003f), lying, new Vector3(.072f, .003f, .08f), Iron);
        Part(root, "Fuchi", Cylinder, new Vector3(0f, 0f, -.012f), lying, new Vector3(.027f, .006f, .034f), Iron);
        float half = Mathf.Max(.02f, (handleLength - .03f) * .5f);
        Part(root, "Tsuka", Cylinder, new Vector3(0f, 0f, -.018f - half), lying, new Vector3(.026f, half, .033f), Wrap);
        Part(root, "Kashira", Cylinder, new Vector3(0f, 0f, -handleLength + .006f), lying, new Vector3(.027f, .006f, .034f), Iron);
        return root;
    }

    private static void Part(Transform root, string name, Mesh mesh, Vector3 at, Quaternion turn, Vector3 size, Material material)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(root, false);
        part.transform.localPosition = at;
        part.transform.localRotation = turn;
        part.transform.localScale = size;
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Mesh Cube => Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    private static Mesh Cylinder => Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

    // The blade: sections along +Z, each five points round (back left and right, the ridge either side, the edge). Each
    // face is its own strip so the ridge reads as a crease; the two strips down to the edge are their own submesh.
    private static Mesh Blade(float length)
    {
        if (blade != null && Mathf.Approximately(bladeMadeFor, length)) return blade;
        if (blade != null) Object.Destroy(blade);
        bladeMadeFor = length;
        var vertices = new System.Collections.Generic.List<Vector3>();
        var body = new System.Collections.Generic.List<int>();
        var edges = new System.Collections.Generic.List<int>();
        var ring = new Vector3[Sections + 1, 5];
        float pointFrom = 1f - PointShare;
        for (int i = 0; i <= Sections; i++)
        {
            float t = (float)i / Sections, z = t * length;
            float width, back;
            if (t <= pointFrom)
            {
                width = Mathf.Lerp(BaseWidth, TipWidth, t / pointFrom);
                back = width * .5f;
            }
            else
            {
                // The point: the edge sweeps up to meet the back, which drops a little (the kissaki's fukura).
                float p = (t - pointFrom) / PointShare;
                width = TipWidth * (1f - p * p);
                back = TipWidth * .5f - TipWidth * .12f * p * p;
            }
            float thick = Mathf.Lerp(BackThickness, .0015f, t * t);
            float y = Curve * t * t;
            float top = y + back, low = top - Mathf.Max(width, .0002f), ridge = top - (top - low) * .33f;
            ring[i, 0] = new Vector3(-thick * .3f, top, z);
            ring[i, 1] = new Vector3(thick * .3f, top, z);
            ring[i, 2] = new Vector3(thick * .5f, ridge, z);
            ring[i, 3] = new Vector3(0f, low, z);
            ring[i, 4] = new Vector3(-thick * .5f, ridge, z);
        }
        // Round the section: back, right flat, right edge, left edge, left flat (outward faces, Unity's winding).
        Strip(vertices, body, ring, 0, 1);
        Strip(vertices, body, ring, 1, 2);
        Strip(vertices, edges, ring, 2, 3);
        Strip(vertices, edges, ring, 3, 4);
        Strip(vertices, body, ring, 4, 0);
        // The root's face, hidden in the collar but closed all the same.
        int centre = vertices.Count;
        Vector3 middle = Vector3.zero;
        for (int k = 0; k < 5; k++) middle += ring[0, k];
        vertices.Add(middle / 5f);
        for (int k = 0; k < 5; k++) vertices.Add(ring[0, k]);
        for (int k = 0; k < 5; k++)
        {
            body.Add(centre);
            body.Add(centre + 1 + k);
            body.Add(centre + 1 + (k + 1) % 5);
        }
        blade = new Mesh { name = "Katana Blade (stand-in)", hideFlags = HideFlags.DontSave };
        blade.SetVertices(vertices);
        blade.subMeshCount = 2;
        blade.SetTriangles(body, 0);
        blade.SetTriangles(edges, 1);
        blade.RecalculateNormals();
        blade.RecalculateBounds();
        return blade;
    }

    // One face along the blade between two of the section's points: a then b going round, so its normal faces out.
    private static void Strip(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> triangles,
        Vector3[,] ring, int a, int b)
    {
        int start = vertices.Count;
        for (int i = 0; i <= Sections; i++)
        {
            vertices.Add(ring[i, a]);
            vertices.Add(ring[i, b]);
        }
        for (int i = 0; i < Sections; i++)
        {
            int a0 = start + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
            triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
            triangles.Add(b0); triangles.Add(a1); triangles.Add(b1);
        }
    }

    private static Material Lit(string name, Color color, float metallic, float smoothness)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        return material;
    }

    private static Material Steel => steel != null ? steel : steel = Lit("Katana Steel (stand-in)", new Color(.6f, .62f, .66f), 1f, .78f);
    private static Material Edge => edge != null ? edge : edge = Lit("Katana Edge (stand-in)", new Color(.84f, .86f, .9f), 1f, .93f);
    private static Material Iron => iron != null ? iron : iron = Lit("Katana Iron (stand-in)", new Color(.1f, .09f, .085f), .85f, .42f);
    private static Material Brass => brass != null ? brass : brass = Lit("Katana Brass (stand-in)", new Color(.78f, .6f, .32f), 1f, .72f);

    // The handle's wrap: black cord crossing over pale rayskin, which shows through in a row of diamonds down each side.
    private static Material Wrap
    {
        get
        {
            if (wrap != null) return wrap;
            wrap = Lit("Katana Wrap (stand-in)", Color.white, 0f, .25f);
            const int Across = 64, Along = 256, Diamonds = 9;
            var pattern = new Texture2D(Across, Along, TextureFormat.RGBA32, true)
                { name = "Katana Wrap (stand-in)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[Across * Along];
            var cord = new Color32(18, 17, 19, 255);
            var skin = new Color32(214, 206, 186, 255);
            for (int y = 0; y < Along; y++)
            for (int x = 0; x < Across; x++)
            {
                float u = (float)x / Across, v = (float)y / Along;
                float a = Mathf.Repeat(v * Diamonds, 1f) - .5f, b = Mathf.Repeat(u * 2f, 1f) - .5f;
                bool diamond = Mathf.Abs(a) * 1.1f + Mathf.Abs(b) * 1.7f < .3f;
                // A faint weave across the cord.
                float weave = Mathf.Repeat((u * 2f + v * Diamonds) * 3f, 1f) < .5f ? 1f : .8f;
                var c = diamond ? skin : cord;
                pixels[y * Across + x] = diamond ? c : new Color32((byte)(c.r * weave), (byte)(c.g * weave), (byte)(c.b * weave), 255);
            }
            pattern.SetPixels32(pixels);
            pattern.Apply(true, true);
            if (wrap.HasProperty("_BaseMap")) wrap.SetTexture("_BaseMap", pattern);
            if (wrap.HasProperty("_MainTex")) wrap.SetTexture("_MainTex", pattern);
            return wrap;
        }
    }
}
