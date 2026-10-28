using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a collider is made of, for footsteps and impacts. A <see cref="SurfaceType"/> on the object (or a parent)
/// decides; otherwise Resources/Tables/Surfaces.csv matches keywords against the physics material, the render
/// material and the object name, and the longest matching keyword wins. Nothing matches → Concrete.
/// Each surface may name a parent (Duct → Metal) that sound lookups fall back to.
/// </summary>
public static class Surfaces
{
    public const string Default = "Concrete";
    private const string Table = "Surfaces";

    private sealed class Row { public string Id, Parent; public string[] Keywords; }

    private static List<Row> rows;
    private static readonly Dictionary<Collider, string> cache = new Dictionary<Collider, string>();
    private static readonly List<string> chain = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        rows = null;
        cache.Clear();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() { rows = null; cache.Clear(); }

    public static string Of(RaycastHit hit) => Of(hit.collider);

    public static string Of(Collider collider)
    {
        if (collider == null) return Default;
        if (cache.TryGetValue(collider, out string known)) return known;
        var marker = collider.GetComponentInParent<SurfaceType>();
        string surface = marker != null && !string.IsNullOrWhiteSpace(marker.Surface) ? marker.Surface.Trim() : Match(collider);
        cache[collider] = surface;
        return surface;
    }

    /// <summary>The surface, then its parents, then Concrete. Reuses one list; do not keep it.</summary>
    public static IReadOnlyList<string> Fallbacks(string surface)
    {
        chain.Clear();
        string current = string.IsNullOrWhiteSpace(surface) ? Default : surface;
        while (current != null && !chain.Contains(current) && chain.Count < 8)
        {
            chain.Add(current);
            current = Find(current)?.Parent;
        }
        if (!chain.Contains(Default)) chain.Add(Default);
        return chain;
    }

    private static string Match(Collider collider)
    {
        string best = Default;
        int bestLength = 0;
        var renderer = collider.GetComponent<Renderer>();
        foreach (string name in new[]
                 {
                     collider.sharedMaterial != null ? collider.sharedMaterial.name : null,
                     renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : null,
                     collider.gameObject.name
                 })
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var row in Load())
                foreach (string keyword in row.Keywords)
                    if (keyword.Length > bestLength && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        best = row.Id;
                        bestLength = keyword.Length;
                    }
        }
        return best;
    }

    private static Row Find(string id)
    {
        foreach (var row in Load())
            if (string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)) return row;
        return null;
    }

    private static List<Row> Load()
    {
        if (rows != null) return rows;
        rows = new List<Row>();
        foreach (string id in GameTables.RowIds(Table))
        {
            if (!GameTables.TryGetRow(Table, id, out var cells)) continue;
            cells.TryGetValue("parent", out string parent);
            cells.TryGetValue("keywords", out string keywords);
            rows.Add(new Row
            {
                Id = id,
                Parent = string.IsNullOrWhiteSpace(parent) ? null : parent.Trim(),
                Keywords = (keywords ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            });
        }
        foreach (var row in rows)
            for (int i = 0; i < row.Keywords.Length; i++) row.Keywords[i] = row.Keywords[i].Trim();
        return rows;
    }
}
