using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// How a piece of the wardrobe is photographed, turned, near, moved or leaning from its automatic framing (the user,
/// 2026-11-12: "미리보기 사진 바꾸기 기능 넣어서 내가 직접 인게임에서 화면에 담길 만큼 보이는 걸로 각도나 위치 조절해서 찍어서",
/// then "그 옷을 시계 방향, 반시계 방향으로 좀 turn해서 이렇게 스타일리쉬한 각도에서도 찍을 수 있게"). All zero and zoom 1 is
/// the automatic picture: the whole piece seen from its front, a little from above, leaning a little clockwise
/// (OutfitPictureLook's Auto numbers, Tables/WardrobePictures.csv).
/// </summary>
[Serializable]
public struct OutfitFraming
{
    /// <summary>
    /// Round the piece from its front (degrees; + takes the lens round to the piece's right, so its front faces the
    /// picture's right), and from above (degrees, + looks down more than the automatic framing).
    /// </summary>
    public float yaw, pitch;
    /// <summary>How much nearer than the whole piece's fit (1 fits it, 2 is twice as near).</summary>
    public float zoom;
    /// <summary>How far the piece is moved in the picture, in halves of the picture (+ right, + up).</summary>
    public float x, y;
    /// <summary>
    /// How much more the piece leans clockwise in the picture than the automatic lean (degrees, - back the other way:
    /// minus the automatic lean stands it straight). Counted from the automatic lean, as the turns are counted from the
    /// automatic view, so a framing kept before leaning came in (or left at zero) leans as every other picture does.
    /// </summary>
    public float roll;

    public static OutfitFraming Auto => new OutfitFraming { zoom = 1f };

    public bool IsAuto => Mathf.Abs(yaw) < .01f && Mathf.Abs(pitch) < .01f && Mathf.Abs(zoom - 1f) < .001f
        && Mathf.Abs(x) < .0005f && Mathf.Abs(y) < .0005f && Mathf.Abs(roll) < .01f;
}

/// <summary>
/// The wardrobe's pictures as files, and how each piece is framed (OutfitIcons, OutfitShootView). A picture is found, in
/// this order: one the player took on this PC in a build (persistentDataPath/OutfitPictures), one baked into the game
/// (Resources/UI/OutfitPictures, Tools → Characters → 옷장 사진 모두 굽기, or a picture taken again in the editor), else
/// none: OutfitIcons takes it in its studio then. Taking a picture rendered a whole outfit model for every plate the
/// wardrobe opened; a baked one is a small file (the user, 2026-11-12: "미리 구워 놓으면 로딩 속도 더 빨라 지려나").
/// A piece's framing (Tables/OutfitPictures.csv, by piece: every colour of an item and every colour set of a character's
/// own piece share it) is used wherever the piece is photographed: the bake, the studio's fallback and the shooting
/// window. Taken again in the editor, the picture and the framing are written into the project (the editor sets
/// <see cref="ProjectWriter"/> and <see cref="FramingWriter"/>), so they ship; in a build they stay on the PC.
/// </summary>
public static class OutfitPictures
{
    /// <summary>
    /// The pictures' side (px). A plate lays its picture bigger than itself (WardrobePictures.csv Plate.size: 180 px at
    /// 1080p), so they are a little bigger than that, to stay sharp on bigger screens.
    /// </summary>
    public const int Size = 256;
    public const string ResourcesFolder = "UI/OutfitPictures";
    public const string ProjectFolder = "Assets/_Project/Resources/" + ResourcesFolder;
    public const string TableName = "OutfitPictures";
    private const string PersistentFolder = "OutfitPictures", PersistentFramings = "framings.json";

    /// <summary>
    /// The editor's writer of a picture into the project (path under Resources without the extension, the PNG's bytes),
    /// importing it at once; null in a build.
    /// </summary>
    public static Func<string, byte[], bool> ProjectWriter;
    /// <summary>The editor's writer of a piece's row in Tables/OutfitPictures.csv (null framing: the row goes); null in a build.</summary>
    public static Func<string, OutfitFraming?, bool> FramingWriter;

    // Framings set this run (the table's next read may lag behind its file), and a build's own, from the PC.
    private static readonly Dictionary<string, OutfitFraming> set = new Dictionary<string, OutfitFraming>(StringComparer.Ordinal);
    private static Dictionary<string, OutfitFraming> persistent;

    [Serializable]
    private sealed class Stored
    {
        public List<string> pieces = new List<string>();
        public List<OutfitFraming> framings = new List<OutfitFraming>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        set.Clear();
        persistent = null;
    }

    // ---- Keys ----

    /// <summary>An item in a colour set for a character ("" is the item's first set).</summary>
    public static string ItemKey(string skinId, string itemId, string colour) => ItemPiece(skinId, itemId) + "#" + (colour ?? "");
    /// <summary>A character's own piece of a slot in one of its colour sets ("" when it has none).</summary>
    public static string OwnKey(string skinId, string slot, string variant) => OwnPiece(skinId, slot) + "#" + (variant ?? "");
    public static string ItemPiece(string skinId, string itemId) => "item@" + skinId + "@" + itemId;
    public static string OwnPiece(string skinId, string slot) => "own@" + skinId + "@" + slot;
    /// <summary>The piece a picture's key is of, its colour left off: the framing's key.</summary>
    public static string PieceOf(string key)
    {
        int hash = key != null ? key.IndexOf('#') : -1;
        return hash < 0 ? key ?? "" : key.Substring(0, hash);
    }

    /// <summary>
    /// The picture's file under the pictures' folder, without its extension: "skin/item.id" or "skin/own.slot", then
    /// ".colour" when it isn't the first. Anything a file name can't hold becomes '_' and the name gets a short hash of
    /// the key, so no two keys share a file.
    /// </summary>
    public static string FileOf(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        string[] parts = key.Split('@');
        if (parts.Length < 3) return Safe(key, out _);
        int hash = parts[2].IndexOf('#');
        string id = hash < 0 ? parts[2] : parts[2].Substring(0, hash), colour = hash < 0 ? "" : parts[2].Substring(hash + 1);
        string name = (parts[0] == "own" ? "own." : "item.") + id + (colour.Length > 0 ? "." + colour : "");
        string skin = Safe(parts[1], out bool skinChanged), file = Safe(name, out bool nameChanged);
        if (skinChanged || nameChanged) file += "~" + Hash(key);
        return skin + "/" + file;
    }

    private static string Safe(string text, out bool changed)
    {
        var safe = new StringBuilder(text.Length);
        changed = false;
        foreach (char c in text)
        {
            bool plain = c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');
            safe.Append(plain ? c : '_');
            changed |= !plain;
        }
        return safe.ToString();
    }

    // FNV-1a over the key's characters: the same on every PC and every run.
    private static string Hash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static string PersistentPath(string file) =>
        Path.Combine(Application.persistentDataPath, PersistentFolder, file.Replace('/', Path.DirectorySeparatorChar) + ".png");

    // ---- Pictures ----

    /// <summary>
    /// The picture of this key as a file: the player's own (a build only; the editor's go into the project), else the
    /// baked one; null when there is none and it must be taken. <paramref name="owned"/> says the texture was made here,
    /// for the caller to destroy when done (a baked one is an asset of the game and is never destroyed).
    /// </summary>
    public static Texture Load(string key, out bool owned)
    {
        owned = false;
        string file = FileOf(key);
        if (file.Length == 0) return null;
        if (!Application.isEditor)
        {
            string path = PersistentPath(file);
            if (File.Exists(path))
            {
                try
                {
                    var picture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Outfit Picture " + key, wrapMode = TextureWrapMode.Clamp };
                    if (picture.LoadImage(File.ReadAllBytes(path)))
                    {
                        owned = true;
                        return picture;
                    }
                    UnityEngine.Object.Destroy(picture);
                }
                catch (Exception problem)
                {
                    Debug.LogWarning("[OutfitPictures] " + path + ": " + problem.Message);
                }
            }
        }
        return Resources.Load<Texture2D>(ResourcesFolder + "/" + file);
    }

    /// <summary>
    /// Keeps a picture where it is found first from now on: in the editor written into the project and imported (it
    /// ships), in a build on this PC. False when it couldn't be written.
    /// </summary>
    public static bool Save(string key, Texture2D picture)
    {
        if (picture == null) return false;
        string file = FileOf(key);
        if (file.Length == 0) return false;
        byte[] png = picture.EncodeToPNG();
        if (ProjectWriter != null) return ProjectWriter(ResourcesFolder + "/" + file, png);
        try
        {
            string path = PersistentPath(file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
            return true;
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitPictures] " + file + ": " + problem.Message);
            return false;
        }
    }

    // ---- Framings ----

    /// <summary>How the piece (<see cref="PieceOf"/>) is photographed: as set this run, a build's own, the table's, else automatic.</summary>
    public static OutfitFraming FramingOf(string piece)
    {
        if (string.IsNullOrEmpty(piece)) return OutfitFraming.Auto;
        if (set.TryGetValue(piece, out var chosen)) return chosen;
        if (!Application.isEditor && Persistent().TryGetValue(piece, out var own)) return own;
        if (!GameTables.TryGetRow(TableName, piece, out var row)) return OutfitFraming.Auto;
        return new OutfitFraming
        {
            yaw = Number(row, "yaw", 0f), pitch = Number(row, "pitch", 0f), zoom = Mathf.Max(.05f, Number(row, "zoom", 1f)),
            x = Number(row, "x", 0f), y = Number(row, "y", 0f), roll = Number(row, "roll", 0f)
        };
    }

    /// <summary>Keeps the piece's framing: in the editor its table row (none for the automatic one), in a build on this PC.</summary>
    public static bool SetFraming(string piece, OutfitFraming framing)
    {
        if (string.IsNullOrEmpty(piece)) return false;
        set[piece] = framing;
        if (FramingWriter != null) return FramingWriter(piece, framing.IsAuto ? (OutfitFraming?)null : framing);
        var stored = Persistent();
        if (framing.IsAuto) stored.Remove(piece);
        else stored[piece] = framing;
        try
        {
            var data = new Stored();
            foreach (var pair in stored)
            {
                data.pieces.Add(pair.Key);
                data.framings.Add(pair.Value);
            }
            string path = Path.Combine(Application.persistentDataPath, PersistentFolder, PersistentFramings);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            return true;
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitPictures] " + PersistentFramings + ": " + problem.Message);
            return false;
        }
    }

    private static Dictionary<string, OutfitFraming> Persistent()
    {
        if (persistent != null) return persistent;
        persistent = new Dictionary<string, OutfitFraming>(StringComparer.Ordinal);
        try
        {
            string path = Path.Combine(Application.persistentDataPath, PersistentFolder, PersistentFramings);
            if (!File.Exists(path)) return persistent;
            var data = JsonUtility.FromJson<Stored>(File.ReadAllText(path));
            for (int i = 0; data != null && i < data.pieces.Count && i < data.framings.Count; i++) persistent[data.pieces[i]] = data.framings[i];
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitPictures] " + PersistentFramings + ": " + problem.Message);
        }
        return persistent;
    }

    private static float Number(IReadOnlyDictionary<string, string> row, string column, float fallback) =>
        row.TryGetValue(column, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    /// <summary>A piece's row of Tables/OutfitPictures.csv (id,yaw,pitch,zoom,x,y,roll), as the editor writes it.</summary>
    public static string Row(string piece, OutfitFraming framing) =>
        string.Join(",", Quote(piece), Text(framing.yaw), Text(framing.pitch), Text(framing.zoom), Text(framing.x), Text(framing.y),
            Text(framing.roll));

    private static string Text(float value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
    private static string Quote(string text) => text.IndexOfAny(new[] { ',', '"' }) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";
}
