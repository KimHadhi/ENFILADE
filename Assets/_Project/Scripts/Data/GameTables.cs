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
/// Balance numbers live in CSV files under Resources/Tables (see CsvTable for the layouts), so tuning needs no
/// recompile. Bind() overwrites an object's fields from one row: each column names a field, empty cells keep the
/// inspector value, and a row that does not exist leaves the object untouched. In the editor, saving a table calls
/// Reload(), which re-applies every live binding, during Play included.
/// </summary>
public static class GameTables
{
    public const string ResourcesFolder = "Tables";

    private sealed class Binding
    {
        public string Table, Id;
        public Object[] Targets;
        public bool IsAlive => Targets[0] != null;
    }

    private static readonly Dictionary<string, CsvTable> loaded = new Dictionary<string, CsvTable>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Binding> bindings = new List<Binding>();
    private static readonly HashSet<string> reported = new HashSet<string>();
    private static int pruneAt = 64;

    /// <summary>Raised after Reload() re-applied the tables.</summary>
    public static event Action Reloaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        loaded.Clear(); bindings.Clear(); reported.Clear(); pruneAt = 64;
    }

    /// <summary>
    /// Apply the row to the targets and keep them bound for later reloads. A column is written to the first target
    /// that has a field of that name. Null targets are ignored. Returns whether the row exists.
    /// </summary>
    public static bool Bind(string table, string id, params Object[] targets)
    {
        var alive = targets.Where(t => t != null).ToArray();
        if (alive.Length == 0) return false;
        bool applied = Apply(table, id ?? "", alive);
        if (bindings.Count >= pruneAt) { bindings.RemoveAll(b => !b.IsAlive); pruneAt = Mathf.Max(64, bindings.Count * 2); }
        if (!bindings.Exists(b => b.Targets[0] == alive[0] && b.Table == table))
            bindings.Add(new Binding { Table = table, Id = id ?? "", Targets = alive });
        return applied;
    }

    public static bool TryGetRow(string table, string id, out IReadOnlyDictionary<string, string> row)
    {
        row = null;
        var data = Load(table);
        return data != null && data.TryGetRow(id ?? "", out row);
    }

    /// <summary>Forget the cached tables and re-apply them to every bound object that still exists.</summary>
    public static void Reload()
    {
        loaded.Clear(); reported.Clear();
        bindings.RemoveAll(b => !b.IsAlive);
        foreach (var binding in bindings.ToArray()) Apply(binding.Table, binding.Id, binding.Targets);
        Reloaded?.Invoke();
    }

    private static bool Apply(string table, string id, Object[] targets)
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
                if (result == TableWrite.Invalid) Report($"{Describe(table, id)} '{cell.Key}' = '{cell.Value}': {error}.", target);
                break;
            }
            if (!known)
                Report($"{Describe(table, id)} '{cell.Key}' 열과 같은 이름의 필드가 "
                    + string.Join(", ", targets.Select(t => t.GetType().Name)) + "에 없습니다.", targets[0]);
        }
        foreach (var target in targets)
            if (target is ITableBound bound) bound.OnTableApplied();
        return true;
    }

    private static CsvTable Load(string table)
    {
        if (loaded.TryGetValue(table, out var cached)) return cached;
        var asset = Resources.Load<TextAsset>(ResourcesFolder + "/" + table);
        if (asset == null) Report($"Resources/{ResourcesFolder}/{table}.csv 파일이 없습니다.", null);
        return loaded[table] = asset != null ? CsvTable.Parse(asset.text, table) : null;
    }

    private static string Describe(string table, string id) => id.Length > 0 ? $"{table}.csv [{id}]" : $"{table}.csv";

    // Each distinct problem is reported once per load, so a broken cell cannot flood the console.
    private static void Report(string message, Object context)
    {
        if (reported.Add(message)) Debug.LogWarning("[Tables] " + message, context);
    }
}
