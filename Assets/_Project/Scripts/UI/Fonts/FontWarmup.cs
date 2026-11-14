using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Loading-time font work, so no label waits on a glyph and nothing a player types shows a box.
/// First the operating-system fallbacks join TMP's chain (FontFallbacks, Fonts.csv Fallback). Then every role face's
/// dynamic atlas is filled ahead with the characters its roles can show (Fonts.csv warm: texts = every character in
/// every table, digits, latin or none), a few characters at a time within a per-frame budget (Fonts.csv Warmup), so the
/// first kill-feed name or objective line never rasterizes mid-fight. Glyphs stay for the session; the atlases grow
/// another page when one fills (multi-atlas), and the editor never saves them (DynamicFontSaveProcessor).
/// A face's atlas is filled once, whichever roles share it. This runs in every scene's first load, menus included,
/// and is done within a few seconds of the title screen.
/// A face's OpenType tables (kerning pairs, ligatures, mark placement) are read once per face, for all its new glyphs
/// together. TMP reads them again at every call that adds glyphs, and each reading parses the font's whole GPOS table
/// (50 to 60 ms for these Korean faces whatever the number of glyphs): asked per call, the title screen's 12.4 s behind
/// the loading screen was 10.8 s of fonts (Logs/loads.txt, 2026-11-14), nearly all of it that table read 150 times over.
/// When the warm set is in, the faces stop reading the tables for glyphs added later (a name or a chat line with a
/// character no table holds): such a glyph cost a 40 to 150 ms stall the frame it first showed, for kerning it hardly
/// ever has; what was drawn ahead keeps its kerning.
/// </summary>
public sealed class FontWarmup : MonoBehaviour
{
    private const string WarmupRow = "Warmup", FallbackRow = "Fallback";
    // Characters every face draws ahead whatever its warm set: the ellipsis TMP puts on clipped text, the box it puts
    // for a missing glyph, and the separators the HUD writes between numbers.
    private const string Always = "…□·/:%+-×";
    private const string Digits = "0123456789+-−×÷/%.,:;!?()[]<>#*~·…'\" ";

    [Tooltip("Seconds after the first scene loads before warming starts.")]
    public float delay = .5f;
    [Tooltip("Milliseconds per frame spent drawing glyphs ahead.")]
    public float budgetMs = 2f;
    [Tooltip("Characters drawn per call within the budget.")]
    public int chunk = 8;
    [Tooltip("Operating-system font families tried for characters the game's faces lack (';'-separated).")]
    public string families = "";
    [Tooltip("Operating-system emoji font families (';'-separated).")]
    public string emoji = "";
    [Tooltip("Sampling point size of the operating-system fallbacks.")]
    public int pointSize = 50;

    /// <summary>Whether every role face holds its warm set.</summary>
    public static bool Done { get; private set; }

    // TMP's own step that reads a face's OpenType tables for every glyph added since it last ran. Private there, and
    // only reachable through a call that adds glyphs and asks for it each time; called by name it runs once per face.
    // Not found (another TMP version): the tables are asked for at every call, as before.
    private static readonly MethodInfo ReadFeatures = typeof(TMP_FontAsset).GetMethod("UpdateFontFeaturesForNewlyAddedGlyphs",
        BindingFlags.Instance | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
    private static bool tablesPerCall = ReadFeatures == null;
    // The faces told to stop reading their tables for later glyphs, and what each had, put back when play ends (the
    // switch is the font asset's own, and the editor keeps assets loaded between plays).
    private static readonly List<(TMP_FontAsset face, bool read)> quieted = new List<(TMP_FontAsset, bool)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Done = false;
        tablesPerCall = ReadFeatures == null;
        quieted.Clear();
        Application.quitting -= ReadTablesAgain;
        Application.quitting += ReadTablesAgain;
    }

    private static void ReadTablesAgain()
    {
        foreach (var (face, read) in quieted)
            if (face != null) face.getFontFeatures = read;
        quieted.Clear();
    }

    // Every dynamic face loaded now (the role faces, their fallbacks, the operating system's) adds later glyphs
    // without reading its tables.
    private static void QuietTables()
    {
        foreach (var face in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (face == null || face.atlasPopulationMode == AtlasPopulationMode.Static || !face.getFontFeatures) continue;
            quieted.Add((face, true));
            face.getFontFeatures = false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        // Hidden but not DontSave: the editor must destroy it with the play session's other objects.
        var host = new GameObject("Font Warmup") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(host);
        host.AddComponent<FontWarmup>();
    }

    private IEnumerator Start()
    {
        GameTables.Bind(FontRoles.TableName, WarmupRow, this);
        GameTables.Bind(FontRoles.TableName, FallbackRow, this);
        FontFallbacks.Install(families, emoji, pointSize);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay));

        // Which characters each face draws ahead: the union of its roles' warm sets.
        var wanted = new Dictionary<TMP_FontAsset, HashSet<char>>();
        var order = new List<TMP_FontAsset>();
        string tableText = null;
        foreach (string role in FontRoles.Roles)
        {
            string set = FontRoles.WarmSet(role).Trim().ToLowerInvariant();
            if (set == "none") continue;
            var face = FontRoles.Font(role);
            if (face == null || face.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
            if (!wanted.TryGetValue(face, out var chars)) { wanted[face] = chars = new HashSet<char>(Always); order.Add(face); }
            if (set == "texts")
            {
                if (tableText == null)
                {
                    var collected = new StringBuilder();
                    yield return CollectTables(collected);
                    tableText = collected.ToString();
                }
                chars.UnionWith(tableText);
                chars.UnionWith(Digits);
                AddLatin(chars);
            }
            else if (set == "latin") { AddLatin(chars); chars.UnionWith(Digits); }
            else chars.UnionWith(Digits);
        }

        var clock = Stopwatch.StartNew();
        int step = Mathf.Clamp(chunk, 1, 256);
        foreach (var face in order)
        {
            if (face == null) continue;
            string pending = Missing(face, wanted[face]);
            int had = face.glyphTable != null ? face.glyphTable.Count : 0;
            for (int i = 0; i < pending.Length; i += step)
            {
                if (face == null) break;
                bool last = i + step >= pending.Length;
                // Glyphs only: the face's tables are read once, after its last glyphs, in the same frame as the call
                // that drew them (TMP has the font file open then), and only when the face gained a glyph.
                face.TryAddCharacters(pending.Substring(i, Mathf.Min(step, pending.Length - i)), out _, tablesPerCall);
                if (last && !tablesPerCall && face.glyphTable != null && face.glyphTable.Count > had && !ReadTables(face)) tablesPerCall = true;
                if (last || clock.Elapsed.TotalMilliseconds >= budgetMs) { yield return null; clock.Restart(); }
            }
        }
        QuietTables();
        Done = true;
    }

    private static bool ReadTables(TMP_FontAsset face)
    {
        try
        {
            ReadFeatures.Invoke(face, null);
            return true;
        }
        catch (System.Exception problem)
        {
            UnityEngine.Debug.LogWarning("[Fonts] " + face.name + ": its OpenType tables could not be read in one go ("
                + (problem.InnerException ?? problem).Message + "); they are read at every call from here on.");
            return false;
        }
    }

    // Every character in every table's cells (CsvTable leaves out '#' columns and rows), one table per frame.
    private static IEnumerator CollectTables(StringBuilder into)
    {
        var seen = new HashSet<char>();
        foreach (var asset in Resources.LoadAll<TextAsset>(GameTables.ResourcesFolder))
        {
            var table = CsvTable.Parse(asset.text);
            foreach (string id in table.RowIds)
            {
                if (!table.TryGetRow(id, out var row)) continue;
                foreach (string cell in row.Values)
                    foreach (char c in cell)
                        if (c >= ' ' && !char.IsSurrogate(c) && seen.Add(c)) into.Append(c);
            }
            yield return null;
        }
    }

    // The wanted characters the face does not hold yet. A character its file lacks stays out of reach: the fallback
    // chain draws it, and TryAddCharacters skips it quietly.
    private static string Missing(TMP_FontAsset face, HashSet<char> chars)
    {
        var lookup = face.characterLookupTable;
        var builder = new StringBuilder(chars.Count);
        foreach (char c in chars)
            if (c >= ' ' && !char.IsSurrogate(c) && (lookup == null || !lookup.ContainsKey(c))) builder.Append(c);
        return builder.ToString();
    }

    private static void AddLatin(HashSet<char> chars)
    {
        for (char c = ' '; c <= '~'; c++) chars.Add(c);
    }
}
