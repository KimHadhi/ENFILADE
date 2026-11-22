using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HudAddons
{
    /// <summary>
    /// One mesh of quads drawn with one picture, rebuilt each frame the way a HUDPaint hook draws: Clear(), add the shapes,
    /// Commit(). Coordinates are the addons' screen units (top-left origin, y down) over the layer it stretches across;
    /// picture coordinates are theirs too (u, v from the picture's top-left). Without a picture it is a flat-shape batch
    /// (Shapes): rectangles, outlines, lines, polygons and rounded boxes in their colours, and a textured call in it is a flat
    /// fill, as draw.NoTexture() makes it in GMod. With a picture, the flat calls (Rect, OutlinedRect, RoundedBox, Line) do not
    /// belong in it. Batches stacked in a layer draw in sibling order; Painter does that stacking for you.
    /// <para>No allocation per frame: the vertex lists are kept and refilled, and the mesh is only handed to the canvas when
    /// the frame's quads differ from the last (a running hash of every vertex), so a still HUD costs no canvas rebuild.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class QuadBatch : MaskableGraphic
    {
        private const ulong Seed = 14695981039346656037UL, Prime = 1099511628211UL;

        private readonly List<Vector3> positions = new List<Vector3>(64);
        private readonly List<Color32> colors = new List<Color32>(64);
        private readonly List<Vector2> uvs = new List<Vector2>(64);
        private readonly List<int> indices = new List<int>(96);
        private Texture picture;
        private ulong hash = Seed, shownHash;
        private int shownCount = -1, shownIndices = -1;
        private bool clipped;
        private Rect clipRect;

        /// <summary>The picture every quad samples; null for flat shapes.</summary>
        public Texture Picture
        {
            get => picture;
            set
            {
                if (picture == value) return;
                picture = value;
                SetMaterialDirty();
            }
        }

        public override Texture mainTexture => picture != null ? picture : Shapes.Picture;

        /// <summary>Vertices added since Clear().</summary>
        public int VertexCount => positions.Count;

        /// <summary>A batch stretched over the layer, its local origin at the layer's top-left.</summary>
        public static QuadBatch Create(string name, Transform layer)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)host.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var batch = host.AddComponent<QuadBatch>();
            batch.raycastTarget = false;
            return batch;
        }

        /// <summary>Starts this frame's shapes.</summary>
        public void Clear()
        {
            positions.Clear();
            colors.Clear();
            uvs.Clear();
            indices.Clear();
            hash = Seed;
        }

        /// <summary>Hands the frame's shapes to the canvas, unless they are the ones it already has.</summary>
        public void Commit()
        {
            if (hash == shownHash && positions.Count == shownCount && indices.Count == shownIndices) return;
            shownHash = hash;
            shownCount = positions.Count;
            shownIndices = indices.Count;
            SetVerticesDirty();
        }

        /// <summary>render.SetScissorRect: only what falls inside the root canvas's rectangle shows (Gm.CanvasRect turns screen
        /// units into it). The canvas clips in the shader, so turned quads, polygons and text clip as exactly as rectangles.</summary>
        public void Clip(bool on, Rect canvasRect)
        {
            if (on == clipped && (!on || canvasRect == clipRect)) return;
            clipped = on;
            clipRect = canvasRect;
            ApplyClip();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplyClip();
        }

        private void ApplyClip()
        {
            if (clipped) canvasRenderer.EnableRectClipping(clipRect);
            else canvasRenderer.DisableRectClipping();
        }

        protected override void UpdateGeometry()
        {
            var mesh = workerMesh;
            mesh.Clear();
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, true);
            canvasRenderer.SetMesh(mesh);
        }

        // ---- Shapes ---------------------------------------------------------------------------------------------------

        /// <summary>surface.DrawRect: a flat rectangle.</summary>
        public void Rect(float x, float y, float w, float h, Color32 color)
            => Box(x, y, x + w, y + h, Shapes.SolidU, Shapes.SolidV, Shapes.SolidU, Shapes.SolidV, color, true);

        /// <summary>surface.DrawOutlinedRect: a frame <paramref name="thickness"/> wide inside the rectangle's edge.</summary>
        public void OutlinedRect(float x, float y, float w, float h, float thickness, Color32 color)
        {
            float t = Mathf.Min(thickness, w * .5f, h * .5f);
            if (t <= 0f) return;
            Rect(x, y, w, t, color);
            Rect(x, y + h - t, w, t, color);
            Rect(x, y + t, t, h - 2f * t, color);
            Rect(x + w - t, y + t, t, h - 2f * t, color);
        }

        /// <summary>surface.DrawTexturedRect: the whole picture over the rectangle.</summary>
        public void TexturedRect(float x, float y, float w, float h, Color32 color)
            => Box(x, y, x + w, y + h, 0f, 0f, 1f, 1f, color, false);

        /// <summary>surface.DrawTexturedRectUV: part of the picture (u, v from its top-left) over the rectangle.</summary>
        public void TexturedRectUV(float x, float y, float w, float h, float u0, float v0, float u1, float v1, Color32 color)
            => Box(x, y, x + w, y + h, u0, v0, u1, v1, color, false);

        /// <summary>
        /// surface.DrawTexturedRectRotated: the picture over a w x h rectangle centred on (x, y), turned
        /// <paramref name="degrees"/> counter-clockwise on screen, GMod's sense (an addon pointing an arrow at angle a from
        /// the screen's right, measured upwards, passes a - 90 for an arrow drawn pointing up).
        /// </summary>
        public void TexturedRectRotated(float x, float y, float w, float h, float degrees, Color32 color)
        {
            float radians = degrees * Mathf.Deg2Rad, c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            float hw = w * .5f, hh = h * .5f;
            // A corner's offset (dx, dy) on a y-down screen turns counter-clockwise to (dx·c + dy·s, −dx·s + dy·c).
            int start = positions.Count;
            Turned(x, y, -hw, -hh, c, s, 0f, 0f, color);
            Turned(x, y, hw, -hh, c, s, 1f, 0f, color);
            Turned(x, y, hw, hh, c, s, 1f, 1f, color);
            Turned(x, y, -hw, hh, c, s, 0f, 1f, color);
            QuadIndices(start);
        }

        /// <summary>
        /// draw.RoundedBox / draw.RoundedBoxEx: a flat box whose chosen corners are quarter circles of the radius. As GMod
        /// does, the radius is a whole number and no more than half the box's width or height (floored); with no radius it
        /// is a plain rectangle.
        /// </summary>
        public void RoundedBox(float radius, float x, float y, float w, float h, Color32 color,
            bool topLeft = true, bool topRight = true, bool bottomLeft = true, bool bottomRight = true)
        {
            if (radius <= 0f)
            {
                Rect(x, y, w, h, color);
                return;
            }
            // math.Round is floor(x + .5); Mathf.Round would round halves to even.
            float r = Mathf.Min(Mathf.Floor(radius + .5f), Mathf.Floor(w * .5f), Mathf.Floor(h * .5f));
            if (r <= 0f)
            {
                Rect(x, y, w, h, color);
                return;
            }
            Rect(x + r, y, w - 2f * r, h, color);
            Rect(x, y + r, r, h - 2f * r, color);
            Rect(x + w - r, y + r, r, h - 2f * r, color);
            Corner(topLeft, x, y, r, 0f, 0f, 1f, 1f, color);
            Corner(topRight, x + w - r, y, r, 1f, 0f, 0f, 1f, color);
            Corner(bottomLeft, x, y + h - r, r, 0f, 1f, 1f, 0f, color);
            Corner(bottomRight, x + w - r, y + h - r, r, 1f, 1f, 0f, 0f, color);
        }

        /// <summary>surface.DrawLine: a one-unit line through the centres of its end pixels.</summary>
        public void Line(float x0, float y0, float x1, float y1, Color32 color)
        {
            float dx = x1 - x0, dy = y1 - y0, length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length <= 0f) return;
            float nx = -dy / length * .5f, ny = dx / length * .5f;
            x0 += .5f; y0 += .5f; x1 += .5f; y1 += .5f;
            int start = positions.Count;
            Vertex(x0 + nx, y0 + ny, Shapes.SolidU, Shapes.SolidV, color, true);
            Vertex(x1 + nx, y1 + ny, Shapes.SolidU, Shapes.SolidV, color, true);
            Vertex(x1 - nx, y1 - ny, Shapes.SolidU, Shapes.SolidV, color, true);
            Vertex(x0 - nx, y0 - ny, Shapes.SolidU, Shapes.SolidV, color, true);
            QuadIndices(start);
        }

        /// <summary>surface.DrawPoly: a convex polygon as a fan from its first corner, picture coordinates from the corners.</summary>
        public void Poly(ReadOnlySpan<PolyVertex> corners, Color32 color)
        {
            if (corners.Length < 3) return;
            int start = positions.Count;
            for (int i = 0; i < corners.Length; i++) Vertex(corners[i].x, corners[i].y, corners[i].u, corners[i].v, color, false);
            for (int i = 1; i < corners.Length - 1; i++)
            {
                indices.Add(start);
                indices.Add(start + i);
                indices.Add(start + i + 1);
            }
        }

        /// <summary>Any four corners (clockwise on screen from the top-left one), with their picture coordinates.</summary>
        public void Quad(PolyVertex a, PolyVertex b, PolyVertex c, PolyVertex d, Color32 color)
        {
            int start = positions.Count;
            Vertex(a.x, a.y, a.u, a.v, color, false);
            Vertex(b.x, b.y, b.u, b.v, color, false);
            Vertex(c.x, c.y, c.u, c.v, color, false);
            Vertex(d.x, d.y, d.u, d.v, color, false);
            QuadIndices(start);
        }

        // ---- Vertices -------------------------------------------------------------------------------------------------

        private void Corner(bool round, float x, float y, float r, float u0, float v0, float u1, float v1, Color32 color)
        {
            if (round) Box(x, y, x + r, y + r, u0, v0, u1, v1, color, true);
            else Rect(x, y, r, r, color);
        }

        private void Turned(float x, float y, float dx, float dy, float c, float s, float u, float v, Color32 color)
            => Vertex(x + dx * c + dy * s, y - dx * s + dy * c, u, v, color, false);

        private void Box(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Color32 color, bool shapeUv)
        {
            int start = positions.Count;
            Vertex(x0, y0, u0, v0, color, shapeUv);
            Vertex(x1, y0, u1, v0, color, shapeUv);
            Vertex(x1, y1, u1, v1, color, shapeUv);
            Vertex(x0, y1, u0, v1, color, shapeUv);
            QuadIndices(start);
        }

        // One vertex: screen units to the layer's y-up space, the addons' top-down v to a texture's bottom-up v. In a flat
        // batch a textured call samples the solid spot; only the shapes' own corner coordinates (shapeUv) reach the disc.
        private void Vertex(float x, float y, float u, float v, Color32 color, bool shapeUv)
        {
            if (picture == null && !shapeUv)
            {
                u = Shapes.SolidU;
                v = Shapes.SolidV;
            }
            positions.Add(new Vector3(x, -y, 0f));
            uvs.Add(new Vector2(u, 1f - v));
            colors.Add(color);
            hash = Mix(Mix(Mix(Mix(Mix(hash, Bits(x)), Bits(y)), Bits(u)), Bits(v)),
                (uint)(color.r | color.g << 8 | color.b << 16 | color.a << 24));
        }

        private void QuadIndices(int start)
        {
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start + 2);
            indices.Add(start + 3);
            indices.Add(start);
        }

        private static ulong Mix(ulong hash, uint value) => (hash ^ value) * Prime;

        private static uint Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);
    }
}
