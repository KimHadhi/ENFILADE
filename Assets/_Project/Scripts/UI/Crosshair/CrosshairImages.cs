using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The player's own crosshair pictures (user, 2026-11-11: "크로스헤어에 커스텀 이미지 넣을 수 있도록", as Crosshair X lets
/// any image be the crosshair). They live as PNG or JPG files in Documents\ENFILADE\Crosshairs, where a player can drop
/// them or the settings' file button copies a chosen one, and a drawing names one by its file name
/// (<see cref="CrosshairSettings.image"/>). Each is read into a sprite once, cut down to <see cref="LargestSide"/> pixels
/// on its longer side (a crosshair never needs more), and read again only when the file has changed since.
/// </summary>
public static class CrosshairImages
{
    /// <summary>A picture's longer side is cut down to this many pixels as it is read.</summary>
    public const int LargestSide = 512;
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };
    // Each file read, with the time it was last written then; a file that would not read is kept with no sprite.
    private static readonly Dictionary<string, (Sprite sprite, DateTime written)> loaded =
        new Dictionary<string, (Sprite, DateTime)>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Documents\ENFILADE\Crosshairs (the save folder's Crosshairs where there is no Documents folder).</summary>
    public static string Folder
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(documents)
                ? Path.Combine(Application.persistentDataPath, "Crosshairs")
                : Path.Combine(documents, "ENFILADE", "Crosshairs");
        }
    }

    /// <summary>The pictures in the folder by file name, A to Z.</summary>
    public static List<string> List()
    {
        var names = new List<string>();
        try
        {
            if (!Directory.Exists(Folder)) return names;
            foreach (string path in Directory.GetFiles(Folder))
                if (IsPicture(path)) names.Add(Path.GetFileName(path));
        }
        catch (Exception problem)
        {
            Debug.LogWarning($"[Crosshair] {Folder} could not be read: {problem.Message}");
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>The picture of that file name as a sprite, or null when there is none or it is not a picture.</summary>
    public static Sprite Get(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        string path = Path.Combine(Folder, Path.GetFileName(fileName));
        DateTime written;
        try { written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
        catch (Exception) { written = DateTime.MinValue; }
        // Kept while the file is as it was: a picture still alive, or one that would not read (no sprite at all).
        if (loaded.TryGetValue(fileName, out var known) && known.written == written && (known.sprite != null || ReferenceEquals(known.sprite, null)))
            return known.sprite;
        // Changed on disk: the old picture goes, and whoever showed it takes the new one with the next settings change.
        if (known.sprite != null) Destroy(known.sprite);
        var sprite = written == DateTime.MinValue ? null : Read(path);
        loaded[fileName] = (sprite, written);
        return sprite;
    }

    /// <summary>
    /// Copies a picture chosen anywhere into the folder and gives its file name there; one of the same name and the same
    /// bytes is the same picture. Null when it is not a picture.
    /// </summary>
    public static string Import(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !IsPicture(path)) return null;
            Directory.CreateDirectory(Folder);
            string name = Path.GetFileName(path), target = Path.Combine(Folder, name);
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return name;
            byte[] bytes = File.ReadAllBytes(path);
            // Another picture of the same name already there keeps its name; this one gets a number.
            for (int number = 2; File.Exists(target) && !Same(File.ReadAllBytes(target), bytes); number++)
            {
                name = Path.GetFileNameWithoutExtension(path) + " (" + number + ")" + Path.GetExtension(path);
                target = Path.Combine(Folder, name);
            }
            if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
            return Get(name) != null ? name : null;
        }
        catch (Exception problem)
        {
            Debug.LogWarning($"[Crosshair] {path} could not be copied to {Folder}: {problem.Message}");
            return null;
        }
    }

    /// <summary>Opens the folder in Windows' Explorer, making it first, so pictures can be dropped in.</summary>
    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Application.OpenURL(new Uri(Folder).AbsoluteUri);
        }
        catch (Exception problem)
        {
            Debug.LogWarning($"[Crosshair] {Folder} could not be opened: {problem.Message}");
        }
    }

    private static Sprite Read(string path)
    {
        Texture2D texture = null;
        try
        {
            if (!File.Exists(path)) return null;
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.Destroy(texture); return null; }
            texture = Shrunk(texture);
            texture.name = Path.GetFileName(path);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
        catch (Exception problem)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            Debug.LogWarning($"[Crosshair] {path} could not be read as a picture: {problem.Message}");
            return null;
        }
    }

    // Down to LargestSide on the longer side through the GPU; a smaller picture comes back as it is.
    private static Texture2D Shrunk(Texture2D texture)
    {
        int longest = Mathf.Max(texture.width, texture.height);
        if (longest <= LargestSide) return texture;
        float scale = LargestSide / (float)longest;
        int width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale)), height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var small = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            small.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            small.Apply();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(texture);
        }
        return small;
    }

    private static bool IsPicture(string path) => Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static void Destroy(Sprite sprite)
    {
        var texture = sprite.texture;
        UnityEngine.Object.Destroy(sprite);
        if (texture != null) UnityEngine.Object.Destroy(texture);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => loaded.Clear();
}
