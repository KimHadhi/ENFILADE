using System;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The player's own profile picture: any image they choose (PNG or JPG, through FilePicker), cut to a centred square,
/// shrunk to 256 px and kept as profile.png in the game's save folder. With none chosen, a profile shows the first
/// letter of the name on pink instead. In a room a small copy goes to the others (PlayerNetworkPose), whose teammate
/// cards show it (TeamHealthPanel).
/// </summary>
public static class ProfilePicture
{
    public const int Size = 256;
    private const string FileName = "profile.png";

    private static Texture2D current;
    private static bool loaded;

    /// <summary>The picture changed (chosen or removed).</summary>
    public static event Action Changed;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    /// <summary>The chosen picture, or null when there is none.</summary>
    public static Texture2D Current
    {
        get
        {
            if (!loaded) Load();
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        loaded = false;
        Changed = null;
    }

    /// <summary>Opens the file dialog and uses the image picked. False if cancelled or the file isn't an image.</summary>
    public static bool Choose()
    {
        string path = FilePicker.Open("프로필 사진 고르기", "이미지", "png", "jpg", "jpeg");
        return !string.IsNullOrEmpty(path) && Use(path);
    }

    /// <summary>Uses the image at <paramref name="path"/>: cut to a centred square, shrunk, saved.</summary>
    public static bool Use(string path)
    {
        Texture2D source = null;
        try
        {
            source = new Texture2D(2, 2);
            if (!source.LoadImage(File.ReadAllBytes(path))) return false;
            var square = Square(source);
            File.WriteAllBytes(FilePath, square.EncodeToPNG());
            Replace(square);
            return true;
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[프로필 사진] 사진을 쓰지 못했어요: " + problem.Message);
            return false;
        }
        finally
        {
            if (source != null) UnityEngine.Object.Destroy(source);
        }
    }

    /// <summary>Goes back to the letter.</summary>
    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception problem) { Debug.LogWarning("[프로필 사진] 지우지 못했어요: " + problem.Message); }
        Replace(null);
    }

    /// <summary>
    /// Dresses a profile square: the picture in <paramref name="image"/>, or, with none, pink with the name's first
    /// letter in <paramref name="letter"/>.
    /// </summary>
    public static void Paint(RawImage image, TMP_Text letter, string name)
    {
        var picture = Current;
        if (image != null)
        {
            image.texture = picture;
            image.color = picture != null ? Color.white : KitStyle.Accent;
        }
        if (letter != null) letter.text = picture != null ? "" : Initial(name);
    }

    /// <summary>
    /// The picture small enough to send to the room (friend feedback C14): a <paramref name="side"/> px JPG kept under
    /// <paramref name="most"/> bytes, or an empty array when there is no picture.
    /// </summary>
    public static byte[] SmallJpg(int side = 64, int most = 5000)
    {
        var picture = Current;
        if (picture == null) return Array.Empty<byte>();
        var target = RenderTexture.GetTemporary(side, side, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var small = new Texture2D(side, side, TextureFormat.RGB24, false);
        try
        {
            Graphics.Blit(picture, target);
            RenderTexture.active = target;
            small.ReadPixels(new Rect(0f, 0f, side, side), 0, 0);
            small.Apply();
            for (int quality = 75; ; quality -= 15)
            {
                byte[] bytes = small.EncodeToJPG(quality);
                if (bytes.Length <= most || quality <= 30) return bytes.Length <= most ? bytes : Array.Empty<byte>();
            }
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(small);
        }
    }

    /// <summary>A picture sent by someone else (SmallJpg), or null for none or one that can't be read.</summary>
    public static Texture2D FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        var texture = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Clamp };
        if (texture.LoadImage(bytes)) return texture;
        UnityEngine.Object.Destroy(texture);
        return null;
    }

    /// <summary>The first letter of <paramref name="name"/> (a whole character, even a combined one), or "?".</summary>
    public static string Initial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        return StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
    }

    private static void Load()
    {
        loaded = true;
        if (!File.Exists(FilePath)) return;
        try
        {
            var texture = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Clamp };
            if (texture.LoadImage(File.ReadAllBytes(FilePath))) current = texture;
            else UnityEngine.Object.Destroy(texture);
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[프로필 사진] 저장된 사진을 읽지 못했어요: " + problem.Message);
        }
    }

    private static void Replace(Texture2D next)
    {
        if (current != null && current != next) UnityEngine.Object.Destroy(current);
        current = next;
        loaded = true;
        Changed?.Invoke();
    }

    // The middle square of the picture, at Size × Size.
    private static Texture2D Square(Texture2D source)
    {
        int side = Mathf.Min(source.width, source.height);
        var scale = new Vector2((float)side / source.width, (float)side / source.height);
        var offset = new Vector2((1f - scale.x) * .5f, (1f - scale.y) * .5f);
        var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, target, scale, offset);
            RenderTexture.active = target;
            var square = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            square.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
            square.Apply();
            return square;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
