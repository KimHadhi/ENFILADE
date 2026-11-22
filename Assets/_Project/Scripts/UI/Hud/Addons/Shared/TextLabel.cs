using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// One piece of text placed as Garry's Mod's draw library places it, reused frame after frame (TextPool, Painter).
    /// <para>Place: draw.SimpleText moves the text left by its width (Right) or half of it (Center) and up by its height
    /// (Bottom) or half (Center), where the height is the font's cell per line. Here the text's baseline is put at the cell
    /// top + the font's ascent, with TextMeshPro aligning each line on that baseline (Baseline alignment on a zero-size
    /// rectangle), so the horizontal shift uses the text's own advance width, as GMod's does, with no measuring pass.</para>
    /// <para>Cost: TextMeshProUGUI keeps a white vertex colour and the colour is the CanvasRenderer's (and its fallback
    /// sub-meshes'), so a fade or a colour lerp is a canvas tint, not a new text mesh; the mesh is only made again when the
    /// text, font or alignment changes. Positions move the holder only when they change.</para>
    /// <para>Outline: <see cref="TextStamp"/>s under the text, one per material the text drew with.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TextLabel : MonoBehaviour
    {
        private RectTransform holder;
        private TextMeshProUGUI text;
        private readonly List<TextStamp> stamps = new List<TextStamp>(1);
        private readonly List<TMP_SubMeshUI> subMeshes = new List<TMP_SubMeshUI>();
        private HudFont font;
        private string shown;
        private int lines = 1, alignment = -1, stampsWanted;
        private Vector2 at = new Vector2(float.NaN, float.NaN);
        private Color32 color, outlineColor;
        private bool colored, outlineColored, clipped, clipKnown;
        private float outline = -1f;
        private Rect clipRect;

        /// <summary>The TextMeshPro label itself, for a module that needs more than placing (a mask, a material).</summary>
        public TextMeshProUGUI Text => text;

        /// <summary>A label in the layer: its holder sits at the layer's top-left and is moved to each draw's baseline.</summary>
        public static TextLabel Create(string name, Transform layer)
        {
            var host = new GameObject(name, typeof(RectTransform));
            var holder = (RectTransform)host.transform;
            holder.SetParent(layer, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0f, 1f);
            holder.pivot = Vector2.zero;
            holder.sizeDelta = Vector2.zero;
            var label = host.AddComponent<TextLabel>();
            label.holder = holder;
            label.stamps.Add(TextStamp.Create(holder));
            var textHost = new GameObject("Text", typeof(RectTransform));
            var rect = (RectTransform)textHost.transform;
            rect.SetParent(holder, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            var text = textHost.AddComponent<TextMeshProUGUI>();
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.BaselineLeft;
            text.OnPreRenderText += label.Generated;
            label.text = text;
            return label;
        }

        /// <summary>
        /// Shows the text with its top-left reference at (x, y) moved by the alignment (draw.SimpleText's rules), in the
        /// colour, with GMod's stamped outline when <paramref name="outlineWidth"/> is 0 or more (negative: none).
        /// Returns surface.GetTextSize's answer: the advance width and the cell height per line.
        /// </summary>
        public Vector2 Show(string value, HudFont face, float x, float y, Color32 colour, TextAlign alignX, TextAlign alignY,
            float outlineWidth = -1f, Color32 outlineColour = default)
        {
            value ??= string.Empty;
            if (face == null) return Vector2.zero;
            if (stampsWanted > stamps.Count) AddStamps();
            bool restamp = false;
            if (!ReferenceEquals(face, font))
            {
                font = face;
                face.ApplyTo(text);
            }
            int wanted = alignX == TextAlign.Center ? 1 : alignX == TextAlign.Right ? 2 : 0;
            if (wanted != alignment)
            {
                alignment = wanted;
                text.alignment = wanted == 1 ? TextAlignmentOptions.Baseline
                    : wanted == 2 ? TextAlignmentOptions.BaselineRight : TextAlignmentOptions.BaselineLeft;
            }
            if (!string.Equals(value, shown, StringComparison.Ordinal))
            {
                shown = value;
                lines = HudFont.Lines(value);
                text.SetText(value);
                // TextMeshPro makes no mesh (and so tells no one) for an empty text: clear the outline here.
                restamp |= value.Length == 0;
            }
            if (outlineWidth != outline)
            {
                outline = outlineWidth;
                restamp = true;
            }
            if (restamp) Restamp(shown.Length == 0 ? null : text.textInfo);

            float height = face.Size * lines;
            float top = alignY == TextAlign.Center ? y - height * .5f : alignY == TextAlign.Bottom ? y - height : y;
            var spot = new Vector2(x, -(top + face.Ascent));
            if (spot != at)
            {
                at = spot;
                holder.anchoredPosition = spot;
            }
            Paint(colour);
            if (outline >= 0f) PaintOutline(outlineColour);
            return new Vector2(value.Length == 0 ? 0f : text.preferredWidth, height);
        }

        /// <summary>Clips the label to a root-canvas rectangle (QuadBatch.Clip), or not.</summary>
        public void Clip(bool on, Rect canvasRect)
        {
            if (clipKnown && on == clipped && (!on || canvasRect == clipRect)) return;
            clipKnown = true;
            clipped = on;
            clipRect = canvasRect;
            ApplyClip(text.canvasRenderer);
            foreach (var sub in subMeshes)
                if (sub != null) ApplyClip(sub.canvasRenderer);
            foreach (var stamp in stamps) ApplyClip(stamp.canvasRenderer);
        }

        /// <summary>Shown again after a frame unused: the renderers were cleared, so tint and clip are given anew.</summary>
        internal void Activate()
        {
            if (gameObject.activeSelf) return;
            gameObject.SetActive(true);
            colored = outlineColored = clipKnown = false;
        }

        internal void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        // TextMeshPro has just made the text's mesh (inside the canvas rebuild, or in ForceMeshUpdate): its fallback
        // sub-meshes may be new, so they are found again and given the clip (TextMeshPro gives them the text's tint
        // itself), and the outline is stamped from the new glyphs.
        private void Generated(TMP_TextInfo info)
        {
            subMeshes.Clear();
            text.GetComponentsInChildren(true, subMeshes);
            if (clipKnown)
                foreach (var sub in subMeshes)
                    if (sub != null) ApplyClip(sub.canvasRenderer);
            Restamp(info);
        }

        private void Restamp(TMP_TextInfo info)
        {
            int count = info == null || outline < 0f ? 0 : info.materialCount;
            // A stamp can't be made inside the rebuild TextMeshPro calls Generated from; the next Show makes it.
            if (count > stamps.Count) stampsWanted = count;
            for (int i = 0; i < stamps.Count; i++)
            {
                if (i < count && info.meshInfo[i].material != null)
                {
                    var mesh = info.meshInfo[i];
                    stamps[i].SetSource(mesh.material);
                    stamps[i].Stamp(mesh.vertices, mesh.uvs0, mesh.uvs2, mesh.vertexCount, outline);
                }
                else stamps[i].Stamp(null, null, null, 0, -1f);
            }
        }

        private void AddStamps()
        {
            while (stamps.Count < stampsWanted)
            {
                var stamp = TextStamp.Create(holder);
                stamp.transform.SetSiblingIndex(stamps.Count);
                stamp.canvasRenderer.SetColor(outlineColor);
                if (clipKnown) ApplyClip(stamp.canvasRenderer);
                stamps.Add(stamp);
            }
            Restamp(shown == null || shown.Length == 0 ? null : text.textInfo);
        }

        private void Paint(Color32 colour)
        {
            if (colored && Gm.Same(colour, color)) return;
            colored = true;
            color = colour;
            text.canvasRenderer.SetColor(colour);
            foreach (var sub in subMeshes)
                if (sub != null) sub.canvasRenderer.SetColor(colour);
        }

        private void PaintOutline(Color32 colour)
        {
            if (outlineColored && Gm.Same(colour, outlineColor)) return;
            outlineColored = true;
            outlineColor = colour;
            foreach (var stamp in stamps) stamp.canvasRenderer.SetColor(colour);
        }

        private void ApplyClip(CanvasRenderer renderer)
        {
            if (clipped) renderer.EnableRectClipping(clipRect);
            else renderer.DisableRectClipping();
        }

        private void OnDestroy()
        {
            if (text != null) text.OnPreRenderText -= Generated;
        }
    }
}
