using System;
using System.Collections.Generic;
using UnityEngine;

// FieldPickups: The table. One part of the class; the rest is in FieldPickups.cs and the files beside it.
public sealed partial class FieldPickups
{
    private void LoadRows()
    {
        var guns = new Dictionary<string, WeaponData>(StringComparer.OrdinalIgnoreCase);
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon != null && !guns.ContainsKey(entry.weapon.name)) guns[entry.weapon.name] = entry.weapon;
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !Enum.TryParse(row.Text("kind"), true, out Kind kind)) continue;
            WeaponData weapon = null;
            if (kind == Kind.Gun && !guns.TryGetValue(row.Text("weapon"), out weapon)) continue;
            // A Texts row the cell names (pickup.<id>.name), or plain words; empty takes the gun's name.
            string name = Texts.Cell(row.Text("name"));
            if (string.IsNullOrEmpty(name)) name = weapon != null ? weapon.DisplayName : id;
            rows.Add(new Row
            {
                Name = name, Kind = kind, Weapon = weapon, Amount = row.Number("amount", 0f),
                SpotWeight = Mathf.Max(0f, row.Number("spot", 0f)), DropWeight = Mathf.Max(0f, row.Number("drop", 0f))
            });
            if (rows.Count >= 255) break;
        }
    }

    private int Pick(Func<Row, bool> allowed, Func<Row, float> weight)
    {
        float sum = 0f;
        for (int i = 0; i < rows.Count; i++) if (allowed(rows[i])) sum += weight(rows[i]);
        if (sum <= 0f) return -1;
        float pick = (float)random.NextDouble() * sum;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!allowed(rows[i])) continue;
            pick -= weight(rows[i]);
            if (pick <= 0f) return i;
        }
        return -1;
    }

    private int RowOf(WeaponData weapon)
    {
        for (int i = 0; i < rows.Count; i++) if (rows[i].Kind == Kind.Gun && rows[i].Weapon == weapon) return i;
        return -1;
    }
}
