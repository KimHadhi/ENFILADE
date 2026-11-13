using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Bound objects that must react after a table overwrote their fields (clamp, rescale, republish).</summary>
public interface ITableBound
{
    void OnTableApplied();
}

/// <summary>
/// Balance numbers live in CSV files under Data/Resources/Tables (see CsvTable for the layouts), so tuning needs no
/// recompile. Bind() overwrites an object's fields from one row: each column names a field, empty cells keep the
/// inspector value, and a row that does not exist leaves the object untouched. In the editor, saving a table calls
/// Reload(), which re-applies every live binding, during Play included.
/// A table is asked for by its file name alone ("Weapons"): the files are filed in sub-folders of Data/Resources/Tables by
/// subject, and where one stands is nobody's business but the folder's. A mod's table of the same name is laid over
/// the game's (GameMods), row by row, before anyone reads it.
/// </summary>
public static class GameTables
{
    public const string ResourcesFolder = "Tables";

    private sealed class Binding
    {
        public string Table, Id;
        public Object Owner;
        public object[] Targets;
        public bool IsAlive => Owner != null;
    }

    private static readonly Dictionary<string, CsvTable> loaded = new Dictionary<string, CsvTable>(StringComparer.OrdinalIgnoreCase);
    // The game's own table files by name, wherever they are filed under Data/Resources/Tables. Null until first asked.
    private static Dictionary<string, TextAsset> sources;
    private static readonly List<Binding> bindings = new List<Binding>();
    private static readonly HashSet<string> reported = new HashSet<string>();
    private static int pruneAt = 64;

    /// <summary>Raised after Reload() re-applied the tables.</summary>
    public static event Action Reloaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        loaded.Clear(); bindings.Clear(); reported.Clear(); pruneAt = 64; sources = null;
    }

    /// <summary>
    /// Apply the row to the targets and keep them bound for later reloads. A column is written to the first target
    /// that has a field of that name. Null targets are ignored. Returns whether the row exists.
    /// </summary>
    public static bool Bind(string table, string id, params Object[] targets)
    {
        var alive = targets.Where(t => t != null).ToArray();
        if (alive.Length == 0) return false;
        return Register(table, id, alive[0], alive);
    }

    /// <summary>
    /// Apply the row to a plain [Serializable] settings object kept in a component's field (PlayerMovement's
    /// AbilitySettings). The owner keeps the binding alive for later reloads.
    /// </summary>
    public static bool BindNested(string table, string id, Object owner, object settings)
    {
        if (owner == null || settings == null) return false;
        return Register(table, id, owner, new[] { settings });
    }

    private static bool Register(string table, string id, Object owner, object[] targets)
    {
        id ??= "";
        bool applied = Apply(table, id, owner, targets);
        if (bindings.Count >= pruneAt) { bindings.RemoveAll(b => !b.IsAlive); pruneAt = Mathf.Max(64, bindings.Count * 2); }
        // One owner may hold several rows of one table (PlayerMovement: Posture, Move, Ability).
        if (!bindings.Exists(b => b.Owner == owner && b.Table == table && string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase)))
            bindings.Add(new Binding { Table = table, Id = id, Owner = owner, Targets = targets });
        return applied;
    }

    /// <summary>Every row id of a row table, for tables read as a list (Surfaces.csv).</summary>
    public static IEnumerable<string> RowIds(string table) => Load(table)?.RowIds ?? Array.Empty<string>();

    public static bool TryGetRow(string table, string id, out IReadOnlyDictionary<string, string> row)
    {
        row = null;
        var data = Load(table);
        return data != null && data.TryGetRow(id ?? "", out row);
    }

    /// <summary>One cell as a number: the fallback when the table, the row or the cell is missing or is no number.</summary>
    public static float Number(string table, string id, string field, float fallback)
        => TryGetRow(table, id, out var row) ? row.Number(field, fallback) : fallback;

    /// <summary>One cell as a yes or a no (TableCells.TryFlag), or the fallback.</summary>
    public static bool Flag(string table, string id, string field, bool fallback)
        => TryGetRow(table, id, out var row) ? row.Flag(field, fallback) : fallback;

    /// <summary>One cell's words, or the fallback when the table, the row or the cell is missing.</summary>
    public static string Text(string table, string id, string field, string fallback = "")
        => TryGetRow(table, id, out var row) ? row.Text(field, fallback) : fallback;

    /// <summary>The game's own text of a table, with no mod laid over it, or null when there is no such table: for
    /// tools that show a table as it is written (the surface picker, /mods export).</summary>
    public static string SourceText(string table)
    {
        if (string.IsNullOrEmpty(table)) return null;
        if (!Sources().TryGetValue(table, out var asset)) return null;
        if (asset == null)
        {
            // Unloaded under us (a scene change sweeps unused assets): read the folder again.
            sources = null;
            if (!Sources().TryGetValue(table, out asset) || asset == null) return null;
        }
        return asset.text;
    }

    /// <summary>The names of the game's own tables.</summary>
    public static IEnumerable<string> SourceNames => Sources().Keys;

    /// <summary>Forget the cached tables (and which mods and table files there are) and re-apply them to every bound
    /// object that still exists.</summary>
    public static void Reload()
    {
        loaded.Clear(); reported.Clear(); sources = null;
        GameMods.Rescan();
        bindings.RemoveAll(b => !b.IsAlive);
        foreach (var binding in bindings.ToArray()) Apply(binding.Table, binding.Id, binding.Owner, binding.Targets);
        Reloaded?.Invoke();
    }

    private static bool Apply(string table, string id, Object owner, object[] targets)
    {
        if (!TryGetRow(table, id, out var row)) return false;
        foreach (var cell in row)
        {
            bool known = false;
            foreach (var target in targets)
            {
                var result = TableFieldWriter.TryWrite(target, cell.Key, cell.Value, out string error);
                if (result == TableWrite.NotFound) continue;
                known = true;
                if (result == TableWrite.Invalid) Report($"{Describe(table, id)} '{cell.Key}' = '{cell.Value}': {error}.", owner);
                break;
            }
            if (!known)
                Report($"{Describe(table, id)} '{cell.Key}' 열과 같은 이름의 필드가 "
                    + string.Join(", ", targets.Select(t => t.GetType().Name)) + "에 없습니다.", owner);
        }
        foreach (var target in targets)
            if (target is ITableBound bound) bound.OnTableApplied();
        return true;
    }

    private static CsvTable Load(string table)
    {
        // No name is no table (a row id that was never filled in), not a crash in the dictionary.
        if (string.IsNullOrEmpty(table)) return null;
        if (loaded.TryGetValue(table, out var cached)) return cached;
        string text = SourceText(table);
        CsvTable parsed = text != null ? CsvTable.Parse(text) : null;
        if (parsed != null) Tell(parsed, table + ".csv");
        // Mods in load order, each over what came before; a table only a mod has is that mod's alone.
        foreach (var layer in GameMods.TableTexts(table))
        {
            var over = CsvTable.Parse(layer.Value);
            Tell(over, $"모드 {layer.Key}의 {table}.csv");
            if (parsed == null) parsed = over;
            else parsed.Overlay(over);
        }
        if (parsed == null) Report($"Resources/{ResourcesFolder} 아래에 {table}.csv 파일이 없습니다.", null);
        return loaded[table] = parsed;
    }

    // Every table file under Data/Resources/Tables and its sub-folders, by name. Read once (and again after Reload).
    private static Dictionary<string, TextAsset> Sources()
    {
        if (sources != null) return sources;
        sources = new Dictionary<string, TextAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in Resources.LoadAll<TextAsset>(ResourcesFolder))
        {
            if (asset == null) continue;
            if (sources.ContainsKey(asset.name))
                Report($"{asset.name}.csv가 Resources/{ResourcesFolder} 아래 두 곳에 있습니다. 하나만 남겨 주세요(먼저 찾은 것을 씁니다).", asset);
            else sources[asset.name] = asset;
        }
        return sources;
    }

    // What the reader found odd in a table's text (CsvTable.Problems), said once, naming whose file it is.
    private static void Tell(CsvTable parsed, string whose)
    {
        foreach (string problem in parsed.Problems) Report($"{whose}: {problem}", null);
    }

    private static string Describe(string table, string id) => id.Length > 0 ? $"{table}.csv [{id}]" : $"{table}.csv";

    // Each distinct problem is reported once per load, so a broken cell cannot flood the console.
    private static void Report(string message, Object context)
    {
        if (reported.Add(message)) Debug.LogWarning("[Tables] " + message, context);
    }
}
