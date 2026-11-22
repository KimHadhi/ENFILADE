using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HudAddons
{
    /// <summary>
    /// draw.SimpleTextOutlined's outline: the label's own generated glyphs stamped again on a grid of offsets around it,
    /// under the label, in the outline colour (the CanvasRenderer's colour; the copies are white). GMod draws the whole text
    /// once per offset, from −w to +w in steps of max(1, 2w/3) on each axis: sixteen copies for the addons' usual width
    /// of 2. A faint outline colour such as their black at 20/255 builds up where the copies overlap into a soft dark rim
    /// about half opaque at the glyph's edge; one SDF outline of that colour would be a flat 8% band instead. Copying the
    /// glyph quads keeps every copy as crisp as the text itself, in one mesh rebuilt only when the text is.
    /// One stamp per material of the text (its face, and each fallback face that drew some glyphs).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class TextStamp : MaskableGraphic
    {
        // Sixteen for any width GMod's loop runs over; the bound only stops a runaway width.
        private const int MostCopies = 64;
        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector4> uvs = new List<Vector4>();
        private readonly List<Vector2> uvs2 = new List<Vector2>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> indices = new List<int>();
        private Material source;

        public override Material material
        {
            get => source != null ? source : base.material;
            set => base.material = value;
        }

        public override Texture mainTexture => source != null ? source.mainTexture : base.mainTexture;

        internal static TextStamp Create(Transform parent)
        {
            var host = new GameObject("Outline", typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            var stamp = host.AddComponent<TextStamp>();
            stamp.raycastTarget = false;
            return stamp;
        }

        /// <summary>
        /// The text's material for this stamp. Applied at once rather than marked dirty: this runs while TextMeshPro
        /// generates the text, inside the canvas's graphic rebuild, where nothing may be queued for a rebuild.
        /// </summary>
        internal void SetSource(Material material)
        {
            if (source == material) return;
            source = material;
            if (IsActive()) UpdateMaterial();
        }

        /// <summary>
        /// Stamps <paramref name="vertexCount"/> generated glyph vertices (whole quads, TextMeshPro's order) on GMod's grid
        /// for an outline <paramref name="width"/> wide, and hands the mesh over at once (see SetSource for why). A
        /// negative width, or no glyphs, leaves it empty.
        /// </summary>
        internal void Stamp(Vector3[] vertices, Vector4[] uv0, Vector2[] uv2, int vertexCount, float width)
        {
            positions.Clear();
            uvs.Clear();
            uvs2.Clear();
            colors.Clear();
            indices.Clear();
            if (vertices != null && uv0 != null && uv2 != null && vertexCount >= 4 && width >= 0f)
            {
                vertexCount = Mathf.Min(vertexCount, vertices.Length, uv0.Length, uv2.Length) & ~3;
                // Lua's numeric for over doubles, exactly: −w, −w + step, ... while ≤ w (2 gives −2, −2/3, 2/3, 2).
                double step = System.Math.Max(1.0, width * 2.0 / 3.0);
                int copies = 0;
                for (double ox = -width; ox <= width && copies < MostCopies; ox += step)
                for (double oy = -width; oy <= width && copies < MostCopies; oy += step, copies++)
                {
                    var offset = new Vector3((float)ox, (float)-oy, 0f);
                    int start = positions.Count;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        positions.Add(vertices[i] + offset);
                        uvs.Add(uv0[i]);
                        uvs2.Add(uv2[i]);
                        colors.Add(White);
                    }
                    for (int quad = start; quad < start + vertexCount; quad += 4)
                    {
                        indices.Add(quad);
                        indices.Add(quad + 1);
                        indices.Add(quad + 2);
                        indices.Add(quad + 2);
                        indices.Add(quad + 3);
                        indices.Add(quad);
                    }
                }
            }
            if (IsActive()) UpdateGeometry();
        }

        protected override void UpdateGeometry()
        {
            var mesh = workerMesh;
            mesh.Clear();
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uvs2);
            mesh.SetTriangles(indices, 0, true);
            canvasRenderer.SetMesh(mesh);
        }
    }
}
