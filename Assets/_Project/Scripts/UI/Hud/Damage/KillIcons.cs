using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The picture that pops under the crosshair when the local player kills, chosen by how the kill happened, and the one a
/// broken posture shows (the user's seven icons, 2026-11-21: "이거 인 게임에 넣어 주세요"). Each row of
/// Data/Resources/Tables/Hud/KillIcons.csv says what it is for (event: kill or break), the <see cref="KillMarks"/> a kill
/// needs for it (all of them; none = any kill), how much it matters against the others, its picture and its colour. A kill
/// takes the most important row it meets, so a knife kill shows the knife and only a plain shot the skull.
/// Pictures: Resources/UI/KillIcons/&lt;picture&gt;.png, white with their shadow drawn in, tinted by the row's colour; a PNG of
/// the same name in Documents\ENFILADE\KillIcons comes first, so a player or a mod can redraw one. HitMarkerHUD draws them;
/// their size and timing are HitMarkers.csv's Icon rows.
/// </summary>
public static class KillIcons
{
    public const string TableName = "KillIcons", ResourcesFolder = "UI/KillIcons";

    /// <summary>What a row is shown for: a kill (chosen by its marks) or a hit that broke a foe's posture.</summary>
    public enum Event { Kill, Break }

    /// <summary>One row of the table.</summary>
    public sealed class Row
    {
        public string Id, Picture = "";
        public Event For;
        public KillMarks When;
        public int Priority;
        public bool On = true;
        public Color Colour = Color.white;
    }

    private static List<Row> rows;
    private static readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    // Pictures read from the Documents folder, which this class made and so destroys.
    private static readonly List<Texture2D> made = new List<Texture2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Forget();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // A reloaded table may name other pictures, and a redrawn PNG in Documents shows from the next kill.
    private static void Forget()
    {
        rows = null;
        pictures.Clear();
        foreach (var picture in made)
            if (picture != null)
            {
                if (Application.isPlaying) Object.Destroy(picture);
                else Object.DestroyImmediate(picture);
            }
        made.Clear();
    }

    /// <summary>The row a kill under these marks shows, or null when none that is on matches.</summary>
    public static Row ForKill(KillMarks marks)
    {
        foreach (var row in All())
            if (row.On && row.For == Event.Kill && (marks & row.When) == row.When) return row;
        return null;
    }

    /// <summary>The row a broken posture shows, or null.</summary>
    public static Row ForBreak()
    {
        foreach (var row in All())
            if (row.On && row.For == Event.Break) return row;
        return null;
    }

    /// <summary>Of two rows (either may be null), the one that matters more; the first on a tie.</summary>
    public static Row Better(Row first, Row second)
        => first == null ? second : second == null ? first : second.Priority > first.Priority ? second : first;

    /// <summary>Documents\ENFILADE\KillIcons (the save folder's KillIcons where there is no Documents folder).</summary>
    public static string Folder
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(documents)
                ? Path.Combine(Application.persistentDataPath, "KillIcons")
                : Path.Combine(documents, "ENFILADE", "KillIcons");
        }
    }

    /// <summary>A row's picture: the Documents folder's, else the game's; null (nothing shown) when neither has it.</summary>
    public static Texture2D Picture(Row row)
    {
        if (row == null || string.IsNullOrEmpty(row.Picture)) return null;
        if (pictures.TryGetValue(row.Picture, out var known)) return known;
        var picture = FromFolder(row.Picture) ?? Resources.Load<Texture2D>(ResourcesFolder + "/" + row.Picture);
        if (picture == null) Debug.LogWarning($"[KillIcons] {TableName}.csv [{row.Id}] picture '{row.Picture}': no {row.Picture}.png in Resources/{ResourcesFolder} or {Folder}.");
        return pictures[row.Picture] = picture;
    }

    /// <summary>Reads the table and every picture it names ahead (CombatWarmup through HitMarkerHUD.Prepare), so the first
    /// kill loads nothing.</summary>
    public static void Prewarm()
    {
        foreach (var row in All()) if (row.On) Picture(row);
    }

    private static Texture2D FromFolder(string name)
    {
        string path;
        try
        {
            path = Path.Combine(Folder, name + ".png");
            if (!File.Exists(path)) return null;
        }
        catch (Exception)
        {
            return null;
        }
        try
        {
            var picture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "Kill icon " + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            if (!picture.LoadImage(File.ReadAllBytes(path), true))
            {
                Object.Destroy(picture);
                Debug.LogWarning($"[KillIcons] {path} is not a picture Unity can read (PNG).");
                return null;
            }
            made.Add(picture);
            return picture;
        }
        catch (Exception problem)
        {
            Debug.LogWarning($"[KillIcons] {path} could not be read: {problem.Message}");
            return null;
        }
    }

    /// <summary>Every row, the most important first.</summary>
    public static IReadOnlyList<Row> All()
    {
        if (rows != null) return rows;
        rows = new List<Row>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var cells)) continue;
            var row = new Row { Id = id };
            if (cells.TryGetValue("event", out string happening) && happening.Trim().Length > 0 && !Enum.TryParse(happening.Trim(), true, out row.For))
                Debug.LogWarning($"[KillIcons] {TableName}.csv [{id}] event: '{happening}' is not one of {string.Join(", ", Enum.GetNames(typeof(Event)))}.");
            if (cells.TryGetValue("when", out string when))
                foreach (string part in when.Split(';', ',', ' ', '+'))
                    if (part.Length > 0 && Enum.TryParse(part.Trim(), true, out KillMarks mark)) row.When |= mark;
                    else if (part.Trim().Length > 0) Debug.LogWarning($"[KillIcons] {TableName}.csv [{id}] when: '{part}' is not a KillMarks name.");
            row.Priority = cells.Whole("priority", 0);
            row.On = cells.Flag("on", true);
            row.Picture = cells.Text("picture", "").Trim();
            if (cells.TryGetValue("colour", out string colour) && colour.Trim().Length > 0)
            {
                if (ColorUtility.TryParseHtmlString(colour.Trim(), out var parsed)) row.Colour = parsed;
                else Debug.LogWarning($"[KillIcons] {TableName}.csv [{id}] colour: '{colour}' is not #RRGGBB.");
            }
            rows.Add(row);
        }
        // Stable: rows of equal priority keep the table's order.
        var ordered = new List<Row>(rows);
        rows.Sort((a, b) => b.Priority != a.Priority ? b.Priority.CompareTo(a.Priority) : ordered.IndexOf(a).CompareTo(ordered.IndexOf(b)));
        return rows;
    }
}
