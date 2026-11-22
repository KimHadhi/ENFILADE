using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HudAddons
{
    /// <summary>
    /// One surface.CreateFont: a face at a Garry's Mod size and weight, kept and shared by everyone who asks for the same.
    /// <para>GMod's size is the font's CELL height in pixels (it asks Windows for a font whose ascent + descent, usWinAscent
    /// + usWinDescent, comes to the size), while TextMeshPro sizes the em. So the TMP size is size × unitsPerEm /
    /// (winAscent + winDescent) of that face, the baseline sits winAscent / (winAscent + winDescent) of the cell down
    /// from the text's top, and surface.GetTextSize's height is the size per line. Those three numbers per face are in
    /// Tables/Hud/AddonFonts.csv (Tools/HudAddons/font_metrics.py reads them from the TTF), by the face name the addon's
    /// Lua writes, with the file the face is drawn from (FontFaces: Resources/UI/Fonts/&lt;file&gt;.ttf, made a dynamic TMP font
    /// on first use). The size is cut to a whole number, as GMod's font size is an integer.</para>
    /// <para>Weight: above 550 on a face that is not bold itself (its usWeightClass up to 550), Windows draws the face
    /// emboldened; TextMeshPro's bold style does the same here. A face the table lacks, or whose file is not in the game,
    /// is drawn with the Label role's face (reported once) at its own metrics, so sizes and places still come out right.</para>
    /// </summary>
    public sealed class HudFont
    {
        /// <summary>The faces' table: Tables/Hud/AddonFonts.csv.</summary>
        public const string TableName = "AddonFonts";
        /// <summary>surface.CreateFont's weight when none is given.</summary>
        public const int DefaultWeight = 500;
        private const int SmallestSize = 4, LargestSize = 255, BoldAbove = 550, KeptWidths = 256;

        private static readonly Dictionary<(string Face, int Size, int Weight), HudFont> made = new Dictionary<(string, int, int), HudFont>();
        private static readonly HashSet<string> reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static TextMeshProUGUI measurer;

        // Widths already measured with this font, by text: the addons measure the same few strings every frame.
        private readonly Dictionary<string, float> widths;

        /// <summary>The face name as the addon wrote it.</summary>
        public string Face { get; }
        /// <summary>The cell height in screen units: GMod's size, whole.</summary>
        public int Size { get; }
        public int Weight { get; }
        public TMP_FontAsset Asset { get; }
        /// <summary>The TextMeshPro font size that makes the cell <see cref="Size"/> tall.</summary>
        public float PointSize { get; }
        /// <summary>From the top of the cell down to the baseline, in screen units.</summary>
        public float Ascent { get; }
        /// <summary>Drawn with TextMeshPro's bold style (Windows' emboldening).</summary>
        public bool Bold { get; }
        /// <summary>TextMeshPro line spacing that makes each further line one cell lower, as draw.DrawText steps.</summary>
        public float LineSpacing { get; }
        /// <summary>A material made for <see cref="Asset"/> (its atlas), or null for the face's own.</summary>
        public Material Material { get; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            made.Clear();
            reported.Clear();
            measurer = null;
        }

        /// <summary>surface.CreateFont(name, { font = face, size = size, weight = weight }): kept and shared.</summary>
        public static HudFont Get(string face, float size, int weight = DefaultWeight)
        {
            face = (face ?? "").Trim();
            int whole = Mathf.Clamp((int)size, SmallestSize, LargestSize);
            var key = (face, whole, weight);
            if (made.TryGetValue(key, out var font) && font.Asset != null) return font;
            font = Make(face, whole, weight);
            if (font != null) made[key] = font;
            return font;
        }

        private static HudFont Make(string face, int size, int weight)
        {
            GameTables.TryGetRow(TableName, face, out var row);
            string file = row?.Text("file", "") ?? "";
            var asset = file.Length > 0 && FontFaces.Instance != null ? FontFaces.Instance.Font(file) : null;
            float units = 0f, ascent = 0f, descent = 0f, weightClass = 400f;
            if (asset != null && row != null)
            {
                units = row.Number("unitsPerEm", 0f);
                ascent = row.Number("winAscent", 0f);
                descent = row.Number("winDescent", 0f);
                weightClass = row.Number("weightClass", weightClass);
            }
            if (asset == null)
            {
                asset = FontRoles.Font(FontRoles.Label);
                if (reported.Add(face))
                    Debug.LogWarning(row == null
                        ? $"[HudAddons] Font face '{face}' has no row in {TableName}.csv: drawn with the Label role's face."
                        : $"[HudAddons] Font face '{face}' wants Resources/UI/Fonts/{file}.ttf, which is not in the game: drawn with the Label role's face.");
                if (asset == null) return null;
            }
            var info = asset.faceInfo;
            if (units <= 0f || ascent + descent <= 0f)
            {
                // No measured cell: the face's own ascent and descent stand in for Windows' (they differ little in most faces).
                units = info.pointSize;
                ascent = info.ascentLine;
                descent = -info.descentLine;
            }
            float cell = Mathf.Max(1e-3f, ascent + descent);
            bool bold = weight > BoldAbove && weightClass <= BoldAbove;
            return new HudFont(face, size, weight, asset, size * units / cell, size * ascent / cell, bold, null);
        }

        private HudFont(string face, int size, int weight, TMP_FontAsset asset, float pointSize, float ascent, bool bold, Material material)
        {
            Face = face;
            Size = size;
            Weight = weight;
            Asset = asset;
            PointSize = pointSize;
            Ascent = ascent;
            Bold = bold;
            Material = material;
            widths = new Dictionary<string, float>(StringComparer.Ordinal);
            // TextMeshPro steps a line by the face's line height (ascender − descender + line gap) plus lineSpacing in
            // hundredths of the font size; GMod steps it by the cell.
            var info = asset.faceInfo;
            float step = info.pointSize > 0f ? info.lineHeight * info.scale * pointSize / info.pointSize : size;
            LineSpacing = pointSize > 0f ? (size - step) / (pointSize * .01f) : 0f;
        }

        /// <summary>
        /// The same font drawn with another material of the same face (a glow, a softer edge: a TMP material whose atlas is
        /// <see cref="Asset"/>'s). Metrics and measuring are the font's own. Not kept: hold on to it.
        /// </summary>
        public HudFont WithMaterial(Material material) =>
            new HudFont(Face, Size, Weight, Asset, PointSize, Ascent, Bold, material);

        /// <summary>surface.GetTextSize(text): the advance width and the cell height per line, in screen units.</summary>
        public Vector2 Measure(string text) => new Vector2(Width(text), Size * Lines(text));

        /// <summary>The advance width of the text (its widest line), in screen units.</summary>
        public float Width(string text)
        {
            if (string.IsNullOrEmpty(text) || !Application.isPlaying) return 0f;
            if (widths.TryGetValue(text, out float width)) return width;
            var gauge = Measurer();
            ApplyTo(gauge);
            gauge.SetText(text);
            width = gauge.preferredWidth;
            if (widths.Count >= KeptWidths) widths.Clear();
            widths[text] = width;
            return width;
        }

        /// <summary>How many lines the text breaks into at its line feeds.</summary>
        public static int Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int lines = 1;
            foreach (char c in text)
                if (c == '\n') lines++;
            return lines;
        }

        /// <summary>Sets a label up to draw with this font.</summary>
        internal void ApplyTo(TMP_Text text)
        {
            text.font = Asset;
            text.fontSharedMaterial = Material != null ? Material : Asset.material;
            text.fontSize = PointSize;
            text.fontStyle = Bold ? FontStyles.Bold : FontStyles.Normal;
            text.lineSpacing = LineSpacing;
        }

        // A label that is never drawn (its component stays disabled), for measuring text before it is placed: GMod code
        // measures first and sizes its boxes from the answer.
        private static TextMeshProUGUI Measurer()
        {
            if (measurer != null) return measurer;
            var host = new GameObject("HudAddons Text Measure", typeof(RectTransform));
            Object.DontDestroyOnLoad(host);
            measurer = host.AddComponent<TextMeshProUGUI>();
            measurer.enabled = false;
            measurer.richText = false;
            measurer.textWrappingMode = TextWrappingModes.NoWrap;
            measurer.overflowMode = TextOverflowModes.Overflow;
            measurer.raycastTarget = false;
            return measurer;
        }
    }
}
