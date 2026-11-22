using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// A piece of our own HUD drawn on a painter of its own in the same parts the player's HUD is made of (PlayerHud: the
    /// rounded faint black box, white text over the soft stamped outline, grey small words, the white lit box with black
    /// text), so what is not in HUD Mk II still reads as one HUD with it (the user, 2026-11-22: "다 우리 hud랑 통일성 있게").
    /// The colours come from the same table (PlayerHud.csv Colour and Class rows), the face from the HUD's font option.
    /// <para>One owner draws it every frame: Begin(), the calls, End(). Coordinates are the overlay's (1920x1080 at 16:9,
    /// top-left origin, y down); <see cref="Sway"/> is the HUD's own sway to add to a place, so the piece moves with it.</para>
    /// </summary>
    public sealed class HudSheet
    {
        public static readonly Color32 White = new Color32(255, 255, 255, 255), Ink = new Color32(12, 12, 15, 255);

        // Names cut to a width, kept until a name or the font changes.
        private readonly Dictionary<(string, int, int), string> cut = new Dictionary<(string, int, int), string>();
        private string face;
        private bool bindRead;
        private float corner, outlineWidth;
        private Color32 box, grey, outline;

        public Overlay Overlay { get; }
        public Painter Painter { get; }
        public float Width => Overlay.Width;
        public float Height => Overlay.Height;
        /// <summary>The addon's grey (its 0 · 50 · 100 and labels).</summary>
        public Color32 Grey => grey;
        /// <summary>The chosen box, lit as the addon's selector lights it.</summary>
        public static Color32 Lit => new Color32(255, 255, 255, 200);

        /// <summary>The HUD's sway this frame, to add to a place (x right, y down).</summary>
        public static Vector2 Sway => PlayerHud.Sway;

        public bool Visible
        {
            get => Overlay.Visible;
            set { if (Overlay.Visible != value) Overlay.Visible = value; }
        }

        public HudSheet(string name, int order)
        {
            Overlay = Overlay.Create(name, order);
            Painter = Overlay.AddPainter("Sheet");
        }

        public void Begin()
        {
            if (!bindRead || face != FaceNow) Read();
            Painter.Begin();
        }

        public void End() => Painter.End();

        // ---- the parts ----

        /// <summary>The HUD's box; <paramref name="strength"/> thickens its black (1 as it is).</summary>
        public void Box(float x, float y, float w, float h, float strength = 1f)
            => Painter.RoundedBox(corner, x, y, w, h, Gm.WithAlpha(box, box.a * strength));

        public void Box(float x, float y, float w, float h, Color32 colour, float radius = -1f)
            => Painter.RoundedBox(radius < 0f ? corner : radius, x, y, w, h, colour);

        public void Fill(float x, float y, float w, float h, Color32 colour)
        {
            if (w <= 0f || h <= 0f) return;
            Painter.DrawColor = colour;
            Painter.Rect(x, y, w, h);
        }

        public void Picture(Texture picture, float x, float y, float w, float h, Color32 tint)
        {
            Painter.Picture = picture;
            Painter.DrawColor = tint;
            Painter.TexturedRect(x, y, w, h);
        }

        /// <summary>Text in the HUD's face over its outline: <paramref name="cell"/> is the line's height.</summary>
        public void Text(string text, float cell, float x, float y, Color32 colour, TextAlign alignX = TextAlign.Left,
            TextAlign alignY = TextAlign.Center, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            Painter.OutlinedText(text, Font(cell, bold), x, y, colour, alignX, alignY, outlineWidth, outline);
        }

        /// <summary>Text on a lit box: no outline.</summary>
        public void Plain(string text, float cell, float x, float y, Color32 colour, TextAlign alignX = TextAlign.Left,
            TextAlign alignY = TextAlign.Center, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            Painter.Text(text, Font(cell, bold), x, y, colour, alignX, alignY);
        }

        public float Measure(string text, float cell, bool bold = false) => Font(cell, bold).Width(text);

        /// <summary>The text, or as much of it as fits <paramref name="width"/> and two dots.</summary>
        public string Fit(string text, float cell, float width, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var font = Font(cell, bold);
            if (font.Width(text) <= width) return text;
            var key = (text, (int)cell * 2 + (bold ? 1 : 0), (int)width);
            if (cut.TryGetValue(key, out string kept)) return kept;
            int length = text.Length - 1;
            while (length > 1 && font.Width(text.Substring(0, length) + "..") > width) length--;
            if (cut.Count > 64) cut.Clear();
            return cut[key] = text.Substring(0, length) + "..";
        }

        /// <summary>A check mark in a square: a short stroke down to the elbow and a long one up to the top right.</summary>
        public void Check(float x, float y, float size, Color32 colour)
        {
            float thick = size * .17f;
            Stroke(x + size * .1f, y + size * .52f, x + size * .4f, y + size * .82f, thick, colour);
            Stroke(x + size * .9f, y + size * .2f, x + size * .4f, y + size * .82f, thick, colour);
        }

        // A straight stroke carried on past its end by half its width, so two that meet there close.
        private void Stroke(float x0, float y0, float x1, float y1, float thick, Color32 colour)
        {
            var along = new Vector2(x1 - x0, y1 - y0).normalized;
            var side = new Vector2(-along.y, along.x) * thick * .5f;
            x1 += along.x * thick * .5f;
            y1 += along.y * thick * .5f;
            Painter.Quads(null).Quad(new PolyVertex(x0 - side.x, y0 - side.y), new PolyVertex(x0 + side.x, y0 + side.y),
                new PolyVertex(x1 + side.x, y1 + side.y), new PolyVertex(x1 - side.x, y1 - side.y), colour);
        }

        private HudFont Font(float cell, bool bold) => HudFont.Get(face, cell, bold ? 1000 : HudFont.DefaultWeight);

        private static string FaceNow
        {
            get
            {
                var options = GameSettings.Current.addonPlayerHud;
                return options != null && !string.IsNullOrWhiteSpace(options.font) ? options.font : PlayerHudOptions.VideoFont;
            }
        }

        private void Read()
        {
            bindRead = true;
            face = FaceNow;
            cut.Clear();
            corner = GameTables.Number(PlayerHudLook.TableName, "Hud", "corner", 6f);
            outlineWidth = GameTables.Number(PlayerHudLook.TableName, "Colour", "outlineWidth", 2f);
            box = HudTone.OwnBox;
            grey = HudTone.Grey;
            outline = HudTone.Outline;
        }
    }
}
