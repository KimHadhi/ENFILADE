using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A [SurfaceName] field as a dropdown of Surfaces.csv's surfaces, each with its note ("WoodPlank — 나무 널빤지·마루"), so a
/// surface is picked rather than typed. The list is read again whenever the tables are (GameTables.Reloaded, which saving
/// a table sets off), so a row added there shows up without code. A value the table doesn't have stays, marked missing.
/// </summary>
[CustomPropertyDrawer(typeof(SurfaceNameAttribute))]
public sealed class SurfaceNameDrawer : PropertyDrawer
{
    private const string Table = "Surfaces", NoteColumn = "#note";
    private static string[] ids;
    private static GUIContent[] labels;

    [InitializeOnLoadMethod]
    private static void Watch() => GameTables.Reloaded += () => ids = null;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }
        Load();
        string current = property.stringValue;
        int index = Array.FindIndex(ids, id => string.Equals(id, current, StringComparison.OrdinalIgnoreCase));
        string[] values = ids;
        GUIContent[] shown = labels;
        if (index < 0)
        {
            // Kept in view rather than quietly swapped for the first surface.
            values = new string[ids.Length + 1];
            shown = new GUIContent[labels.Length + 1];
            values[0] = current;
            shown[0] = new GUIContent((string.IsNullOrEmpty(current) ? "(비어 있음)" : current) + " — 표에 없음");
            Array.Copy(ids, 0, values, 1, ids.Length);
            Array.Copy(labels, 0, shown, 1, labels.Length);
            index = 0;
        }
        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(position, label, index, shown);
        if (EditorGUI.EndChangeCheck()) property.stringValue = values[picked];
        EditorGUI.showMixedValue = false;
        EditorGUI.EndProperty();
    }

    private static void Load()
    {
        if (ids != null) return;
        var found = new List<string>();
        var shown = new List<GUIContent>();
        var asset = Resources.Load<TextAsset>(GameTables.ResourcesFolder + "/" + Table);
        if (asset != null)
        {
            var records = CsvTable.ReadRecords(asset.text);
            int note = records.Count > 0 ? records[0].FindIndex(cell => cell.Trim() == NoteColumn) : -1;
            for (int r = 1; r < records.Count; r++)
            {
                string id = records[r][0].Trim();
                if (id.Length == 0 || id[0] == '#') continue;
                // A slash in a popup's label opens a submenu, so the notes' slashes are shown as division slashes.
                string text = note >= 0 && note < records[r].Count ? records[r][note].Trim().Replace('/', '∕') : "";
                found.Add(id);
                shown.Add(new GUIContent(text.Length > 0 ? id + " — " + text : id));
            }
        }
        ids = found.ToArray();
        labels = shown.ToArray();
    }
}
