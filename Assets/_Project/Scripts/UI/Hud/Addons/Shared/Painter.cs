using System;
using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// A layer drawn the way a Garry's Mod HUDPaint hook draws: in call order, with a current draw colour, picture, alpha
    /// multiplier and clip, so a ported paint function keeps its calls and their order. Begin(), the calls, End().
    /// <para>Under it, calls in a row that share a picture (flat shapes all share one) go into one QuadBatch; a text, or a
    /// change of picture or clip, starts the next piece. The n-th piece of a frame reuses the n-th of the last one, so a HUD
    /// drawing the same things each frame keeps its pieces, their textures and their order, and only re-sends meshes whose
    /// quads moved. Pieces fall into sibling order only when the sequence changes.</para>
    /// <para>Coordinates are screen units of the overlay (1920x1080 at 16:9, top-left origin, y down); colours are 0-255.
    /// Unlike GMod's surface library, positions are not cut to whole pixels: on screens other than 1080p a whole unit is not
    /// a whole pixel, and the cut would only make moving pieces step.</para>
    /// </summary>
    public sealed class Painter
    {
        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private readonly RectTransform layer;
        private readonly List<QuadBatch> batches = new List<QuadBatch>();
        private readonly TextPool texts;
        // This frame's pieces in draw order, and the order the layer's children were last put in.
        private readonly List<Transform> order = new List<Transform>(), placed = new List<Transform>();
        private int batchesUsed, batchesShown;
        private QuadBatch open;
        private Canvas root;
        private bool clipped;
        private Rect clipRect;

        /// <summary>surface.SetDrawColor's colour, for Rect, OutlinedRect, the textured calls, Line and Poly. RoundedBox leaves
        /// its colour here, as draw.RoundedBox does.</summary>
        public Color32 DrawColor { get; set; } = White;

        /// <summary>surface.SetMaterial's picture for the textured calls and Poly; null is draw.NoTexture() (flat white,
        /// tinted by DrawColor). A picture that failed to load (Pictures.Get gave null) therefore draws flat.</summary>
        public Texture Picture { get; set; }

        /// <summary>surface.SetAlphaMultiplier: every colour's alpha is multiplied by it, text and outlines included.</summary>
        public float AlphaScale { get; set; } = 1f;

        /// <summary>The layer this painter draws in (stretched over the overlay, origin top-left).</summary>
        public RectTransform Layer => layer;

        public Painter(RectTransform layer)
        {
            this.layer = layer;
            texts = new TextPool(layer);
        }

        /// <summary>surface.SetDrawColor(r, g, b, a) with 0-255 numbers, clamped and cut as GMod takes them.</summary>
        public void SetColor(float r, float g, float b, float a = 255f) => DrawColor = Gm.Rgba(r, g, b, a);

        /// <summary>draw.NoTexture().</summary>
        public void NoTexture() => Picture = null;

        /// <summary>
        /// render.SetScissorRect(x0, y0, x1, y1, true): what is drawn after this shows only inside the rectangle (screen
        /// units). It also stands in for the stencil masks some addons draw (a bar shown only inside its segments): clip
        /// each segment's fill to that segment.
        /// </summary>
        public void Clip(float x0, float y0, float x1, float y1)
        {
            if (root == null)
            {
                var canvas = layer.GetComponentInParent<Canvas>();
                if (canvas == null) return;
                root = canvas.rootCanvas;
            }
            clipped = true;
            clipRect = Gm.CanvasRect(layer, root, x0, y0, x1, y1);
            texts.Clip(true, clipRect);
            open = null;
        }

        /// <summary>render.SetScissorRect(0, 0, 0, 0, false): no clip from here on.</summary>
        public void NoClip()
        {
            if (!clipped) return;
            clipped = false;
            texts.Clip(false, clipRect);
            open = null;
        }

        /// <summary>Starts a frame: the draw state goes back to white, no picture, alpha 1, no clip.</summary>
        public void Begin()
        {
            batchesUsed = 0;
            open = null;
            order.Clear();
            texts.Begin();
            DrawColor = White;
            Picture = null;
            AlphaScale = 1f;
            clipped = false;
        }

        /// <summary>Ends the frame: hands changed meshes to the canvas, hides what was not drawn, fixes the order.</summary>
        public void End()
        {
            for (int i = 0; i < batchesUsed; i++) batches[i].Commit();
            for (int i = batchesUsed; i < batchesShown; i++)
                if (batches[i].gameObject.activeSelf) batches[i].gameObject.SetActive(false);
            batchesShown = batchesUsed;
            texts.End();
            if (SameOrder()) return;
            for (int i = 0; i < order.Count; i++) order[i].SetSiblingIndex(i);
            placed.Clear();
            placed.AddRange(order);
        }

        // ---- surface / draw calls -------------------------------------------------------------------------------------

        /// <summary>surface.DrawRect.</summary>
        public void Rect(float x, float y, float w, float h) => Quads(null).Rect(x, y, w, h, Tint(DrawColor));

        /// <summary>surface.DrawOutlinedRect (thickness 1 unless given).</summary>
        public void OutlinedRect(float x, float y, float w, float h, float thickness = 1f)
            => Quads(null).OutlinedRect(x, y, w, h, thickness, Tint(DrawColor));

        /// <summary>surface.DrawTexturedRect with the current picture.</summary>
        public void TexturedRect(float x, float y, float w, float h) => Quads(Picture).TexturedRect(x, y, w, h, Tint(DrawColor));

        /// <summary>surface.DrawTexturedRectUV with the current picture (u, v from its top-left).</summary>
        public void TexturedRectUV(float x, float y, float w, float h, float u0, float v0, float u1, float v1)
            => Quads(Picture).TexturedRectUV(x, y, w, h, u0, v0, u1, v1, Tint(DrawColor));

        /// <summary>surface.DrawTexturedRectRotated: centred on (x, y), turned counter-clockwise on screen by degrees.</summary>
        public void TexturedRectRotated(float x, float y, float w, float h, float degrees)
            => Quads(Picture).TexturedRectRotated(x, y, w, h, degrees, Tint(DrawColor));

        /// <summary>surface.DrawLine.</summary>
        public void Line(float x0, float y0, float x1, float y1) => Quads(null).Line(x0, y0, x1, y1, Tint(DrawColor));

        /// <summary>surface.DrawPoly with the current picture (flat when there is none).</summary>
        public void Poly(ReadOnlySpan<PolyVertex> corners) => Quads(Picture).Poly(corners, Tint(DrawColor));

        /// <summary>draw.RoundedBox / draw.RoundedBoxEx. Like GMod's, it leaves DrawColor at its colour.</summary>
        public void RoundedBox(float radius, float x, float y, float w, float h, Color32 color,
            bool topLeft = true, bool topRight = true, bool bottomLeft = true, bool bottomRight = true)
        {
            DrawColor = color;
            Quads(null).RoundedBox(radius, x, y, w, h, Tint(color), topLeft, topRight, bottomLeft, bottomRight);
        }

        /// <summary>draw.SimpleText: returns the text's width and height (surface.GetTextSize).</summary>
        public Vector2 Text(string value, HudFont font, float x, float y, Color32 color,
            TextAlign alignX = TextAlign.Left, TextAlign alignY = TextAlign.Top)
            => Label().Show(value, font, x, y, Tint(color), alignX, alignY);

        /// <summary>draw.SimpleTextOutlined: the text over GMod's stamped outline (see TextStamp).</summary>
        public Vector2 OutlinedText(string value, HudFont font, float x, float y, Color32 color, TextAlign alignX, TextAlign alignY,
            float outlineWidth, Color32 outlineColor)
            => Label().Show(value, font, x, y, Tint(color), alignX, alignY, Mathf.Max(0f, outlineWidth), Tint(outlineColor));

        /// <summary>draw.DrawText: lines at line feeds, one cell apart, each aligned on x, the first line's top at y.</summary>
        public Vector2 TextLines(string value, HudFont font, float x, float y, Color32 color, TextAlign alignX = TextAlign.Left)
            => Label().Show(value, font, x, y, Tint(color), alignX, TextAlign.Top);

        /// <summary>A quad batch of the current piece sequence for drawing that the calls above do not cover (arcs built
        /// from QuadBatch.Quad, say); it holds <paramref name="picture"/> (null: flat shapes) and the current clip.
        /// Colours given to it directly do not get AlphaScale.</summary>
        public QuadBatch Quads(Texture picture)
        {
            if (open != null && open.Picture == picture) return open;
            if (batchesUsed == batches.Count) batches.Add(QuadBatch.Create("Quads " + batches.Count, layer));
            var batch = batches[batchesUsed++];
            if (!batch.gameObject.activeSelf) batch.gameObject.SetActive(true);
            batch.Clear();
            batch.Picture = picture;
            batch.Clip(clipped, clipRect);
            order.Add(batch.transform);
            return open = batch;
        }

        private TextLabel Label()
        {
            open = null;
            var label = texts.Take();
            order.Add(label.transform);
            return label;
        }

        private Color32 Tint(Color32 color)
        {
            if (AlphaScale != 1f) color.a = Gm.Byte(color.a * AlphaScale);
            return color;
        }

        private bool SameOrder()
        {
            if (order.Count != placed.Count) return false;
            for (int i = 0; i < order.Count; i++)
                if (order[i] != placed[i]) return false;
            return true;
        }
    }
}
