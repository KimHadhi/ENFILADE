using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ready shapes to start drawing from, like CS2's styles or Overwatch's reticle types, read from
/// Resources/Tables/Weapons/CrosshairPresets.csv: one row per tile in file order, the name column is the tile's name (a Texts id,
/// crosshair.preset.&lt;id&gt;, or plain words; without it the id is the name, as in older tables) and each other column
/// names a <see cref="CrosshairSettings"/> field. A preset sets only the shape and its spin; colour, opacity and outline
/// stay the player's. Nothing keeps a preset's id (the gallery finds the tile by its shape), so ids are free to change.
/// </summary>
public static class CrosshairPresets
{
    public const string TableName = "CrosshairPresets";
    private const string NameColumn = "name";

    public sealed class Preset
    {
        public readonly string Name;
        public readonly CrosshairSettings Shape;

        public Preset(string name, CrosshairSettings shape)
        {
            Name = name;
            Shape = shape;
        }
    }

    private static List<Preset> presets;

    public static IReadOnlyList<Preset> All => presets ??= Load();

    /// <summary>Gives the drawing this preset's shape and spin, keeping its colours, opacity and outline.</summary>
    public static void Apply(Preset preset, CrosshairSettings target)
    {
        var shape = preset.Shape;
        target.dotSize = shape.dotSize;
        target.roundDot = shape.roundDot;
        target.lineCount = shape.lineCount;
        target.spriteLines = shape.spriteLines;
        target.lineLength = shape.lineLength;
        target.lineThickness = shape.lineThickness;
        target.gap = shape.gap;
        target.angle = shape.angle;
        target.tStyle = shape.tStyle; // a T is a shape too: a preset without the column has all four arms
        target.spinning = shape.spinning;
        if (shape.spinning) target.spin = shape.spin; // a still preset keeps the player's spin speed for later
    }

    /// <summary>The preset the drawing still matches, or -1. What a shape hides, such as a dot preset's line size, does not count.</summary>
    public static int IndexOf(CrosshairSettings drawing)
    {
        for (int i = 0; i < All.Count; i++)
            if (Matches(All[i].Shape, drawing)) return i;
        return -1;
    }

    private static bool Matches(CrosshairSettings shape, CrosshairSettings s)
    {
        if (s.dotSize != shape.dotSize || s.lineCount != shape.lineCount || s.spinning != shape.spinning) return false;
        if (shape.dotSize > 0 && s.roundDot != shape.roundDot) return false;
        if (shape.spinning && !Mathf.Approximately(s.spin, shape.spin)) return false;
        return shape.lineCount == 0 || s.spriteLines == shape.spriteLines && Mathf.Approximately(s.lineLength, shape.lineLength)
            && Mathf.Approximately(s.lineThickness, shape.lineThickness) && Mathf.Approximately(s.gap, shape.gap)
            && Mathf.Approximately(s.angle, shape.angle) && s.tStyle == shape.tStyle;
    }

    private static List<Preset> Load()
    {
        var list = new List<Preset>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var cells)) continue;
            var shape = new CrosshairSettings();
            foreach (var cell in cells)
            {
                if (cell.Key == NameColumn) continue;
                if (TableFieldWriter.TryWrite(shape, cell.Key, cell.Value, out string error) != TableWrite.Written)
                    Debug.LogWarning($"[Tables] {TableName}.csv [{id}] '{cell.Key}' = '{cell.Value}': " +
                        (error ?? "CrosshairSettings에 이 이름의 필드가 없습니다") + ".");
            }
            shape.Clamp();
            list.Add(new Preset(cells.TryGetValue(NameColumn, out string name) ? Texts.Cell(name) : id, shape));
        }
        return list;
    }

    private static void Forget() => presets = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        presets = null;
        GameTables.Reloaded -= Forget; // saving the table in the editor shows the new tiles on the next build
        GameTables.Reloaded += Forget;
    }
}
