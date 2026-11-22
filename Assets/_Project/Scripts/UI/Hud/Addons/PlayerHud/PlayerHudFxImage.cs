using UnityEngine;
using UnityEngine.UI;

namespace HudAddons
{
    /// <summary>
    /// Lays the HUD's own picture (the render target PlayerHudFx fills) over the screen through the PlayerHudFx shader:
    /// one quad over the whole screen, or while a hit's glitch runs one quad a strip, each moved sideways (sfx.lua
    /// PostDrawHUDFX). Its mesh is only made again when what it shows changes, so a still HUD costs no rebuild.
    /// Coordinates are the overlay's screen units (top-left origin, y down), the layer it sits in stretched over the screen.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PlayerHudFxImage : Graphic
    {
        /// <summary>One strip of the glitch: its top and height, how far it moves and how far apart its copies stand (units).</summary>
        public struct Strip
        {
            public float Y, Height, X, Aberration;
        }

        private Texture picture;
        private Strip[] strips = new Strip[0];
        private int stripCount, layout = -1;
        private float width = 1f, height = 1f, amount, fringe;
        private bool fringed;
        private Color32 tint;

        public override Texture mainTexture => picture;

        /// <summary>An image stretched over the layer, its local origin at the layer's top-left.</summary>
        public static PlayerHudFxImage Create(Transform layer, Material material)
        {
            var host = new GameObject("HUD picture", typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)host.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = host.AddComponent<PlayerHudFxImage>();
            image.raycastTarget = false;
            image.material = material;
            return image;
        }

        /// <summary>
        /// What to lay this frame: the HUD's picture over a screen of <paramref name="screenWidth"/> x
        /// <paramref name="screenHeight"/> units, its red and cyan copies <paramref name="fringeShift"/> of the width to
        /// each side when <paramref name="withFringe"/>; and the glitch's strips moved by <paramref name="glitch"/> (0..1) of
        /// their way, tinted toward <paramref name="glitchTint"/> by as much (0: no glitch, one quad); a new
        /// <paramref name="glitchLayout"/> number is a new cut of strips.
        /// </summary>
        public void Show(Texture hud, float screenWidth, float screenHeight, bool withFringe, float fringeShift,
            Strip[] glitchStrips, int glitchCount, int glitchLayout, float glitch, Color32 glitchTint)
        {
            bool changed = hud != picture || screenWidth != width || screenHeight != height || withFringe != fringed
                || fringeShift != fringe || glitch != amount
                || (glitch > 0f && (glitchLayout != layout || glitchCount != stripCount || !Same(glitchTint, tint)));
            if (hud != picture)
            {
                picture = hud;
                SetMaterialDirty();
            }
            if (!changed) return;
            width = Mathf.Max(1f, screenWidth);
            height = Mathf.Max(1f, screenHeight);
            fringed = withFringe;
            fringe = fringeShift;
            amount = glitch;
            tint = glitchTint;
            layout = glitchLayout;
            if (glitch > 0f)
            {
                if (strips.Length < glitchCount) strips = new Strip[glitchCount];
                System.Array.Copy(glitchStrips, strips, glitchCount);
                stripCount = glitchCount;
            }
            else stripCount = 0;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (picture == null) return;
            if (amount <= 0f || stripCount == 0)
            {
                // The HUD as it is, its copies a hair to each side (cyan sampled to the right, red to the left).
                Band(vh, 0f, height, 0f, fringe, -fringe, Color.white, fringed);
                return;
            }
            // Each strip moves its picture x right and lays its copies its aberration to each side of it, the cyan one
            // to the right and the red one to the left, all by the glitch's share of the way; the HUD tinted pink as much.
            Color32 shade = Color32.Lerp(new Color32(255, 255, 255, 255), tint, amount);
            for (int i = 0; i < stripCount; i++)
            {
                var strip = strips[i];
                float top = Mathf.Max(0f, strip.Y), bottom = Mathf.Min(height, strip.Y + strip.Height);
                if (bottom <= top) continue;
                float move = strip.X * amount / width, apart = strip.Aberration * amount / width;
                Band(vh, top, bottom, move, -apart, apart, shade, true);
            }
        }

        // A full-width band of the screen from top to bottom (units), its picture moved right by `move` (a share of the
        // width), the copies' samples shifted by cyan and red.
        private void Band(VertexHelper vh, float top, float bottom, float move, float cyan, float red, Color32 shade, bool copies)
        {
            shade.a = copies ? (byte)255 : (byte)0;
            float v0 = 1f - top / height, v1 = 1f - bottom / height, u0 = -move, u1 = 1f - move;
            var shift = new Vector4(cyan, red, 0f, 0f);
            int start = vh.currentVertCount;
            vh.AddVert(Vertex(0f, -top, u0, v0, shade, shift));
            vh.AddVert(Vertex(width, -top, u1, v0, shade, shift));
            vh.AddVert(Vertex(width, -bottom, u1, v1, shade, shift));
            vh.AddVert(Vertex(0f, -bottom, u0, v1, shade, shift));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        private static UIVertex Vertex(float x, float y, float u, float v, Color32 shade, Vector4 shift)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = new Vector3(x, y, 0f);
            vertex.color = shade;
            vertex.uv0 = new Vector4(u, v, 0f, 0f);
            vertex.uv1 = shift;
            return vertex;
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }
}
