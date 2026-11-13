using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Done = false;

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
            for (int i = 0; i < pending.Length; i += step)
            {
                if (face == null) break;
                face.TryAddCharacters(pending.Substring(i, Mathf.Min(step, pending.Length - i)), out _, true);
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) { yield return null; clock.Restart(); }
            }
        }
        Done = true;
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
