using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Labels for one layer, reused frame after frame the way a HUDPaint hook draws text: Begin(), one call per text, End()
    /// hides whatever this frame did not draw. The n-th text of a frame takes the n-th label, so a HUD that draws the same
    /// texts in the same order every frame keeps each label on its text and rebuilds nothing. For text between shapes in
    /// one draw order, use Painter (it takes its labels from a pool like this one).
    /// </summary>
    public sealed class TextPool
    {
        private readonly Transform layer;
        private readonly List<TextLabel> labels = new List<TextLabel>();
        private int used, shown;
        private bool clipped;
        private Rect clipRect;

        public TextPool(Transform layer)
        {
            this.layer = layer;
        }

        /// <summary>Starts a frame: no label is spoken for yet, and nothing is clipped.</summary>
        public void Begin()
        {
            used = 0;
            clipped = false;
        }

        /// <summary>Hides the labels this frame did not use.</summary>
        public void End()
        {
            for (int i = used; i < shown; i++) labels[i].Hide();
            shown = used;
        }

        /// <summary>Clips the texts drawn after this to a root-canvas rectangle (Gm.CanvasRect), or stops clipping.</summary>
        public void Clip(bool on, Rect canvasRect)
        {
            clipped = on;
            clipRect = canvasRect;
        }

        /// <summary>draw.SimpleText: returns the text's width and height (surface.GetTextSize).</summary>
        public Vector2 Text(string value, HudFont font, float x, float y, Color32 color,
            TextAlign alignX = TextAlign.Left, TextAlign alignY = TextAlign.Top)
            => Take().Show(value, font, x, y, color, alignX, alignY);

        /// <summary>draw.SimpleTextOutlined: the text over GMod's stamped outline <paramref name="outlineWidth"/> wide.</summary>
        public Vector2 OutlinedText(string value, HudFont font, float x, float y, Color32 color, TextAlign alignX, TextAlign alignY,
            float outlineWidth, Color32 outlineColor)
            => Take().Show(value, font, x, y, color, alignX, alignY, Mathf.Max(0f, outlineWidth), outlineColor);

        /// <summary>draw.DrawText: lines split at line feeds, each one cell below the last, each aligned on x; the top at y.</summary>
        public Vector2 TextLines(string value, HudFont font, float x, float y, Color32 color, TextAlign alignX = TextAlign.Left)
            => Take().Show(value, font, x, y, color, alignX, TextAlign.Top);

        /// <summary>The next label of this frame, shown and clipped as the pool is; for Painter and for callers that place
        /// the label themselves (TextLabel.Show).</summary>
        public TextLabel Take()
        {
            if (used == labels.Count) labels.Add(TextLabel.Create("Text " + labels.Count, layer));
            var label = labels[used++];
            label.Activate();
            label.Clip(clipped, clipRect);
            if (used > shown) shown = used;
            return label;
        }
    }
}
