using System.Collections.Generic;
using System.IO;
using UnityEngine;

// dev-text: file (a test's pictures for whoever fixes the legs; nothing here is shown in the game)

/// <summary>
/// Pictures of a move as the legs' test drives it (MoveTest), to see what its numbers say before naming a cause: the body
/// from outside, from its right a few metres off and following it (Logs/MoveTest/&lt;move&gt;-out.png), and through its eyes
/// (&lt;move&gt;-eye.png), 20 a second from the move's start, laid left to right and down in one sheet each. The moves are
/// named on the request's film= line (MoveTestOnRequest carries it on in the marker); filmrate=60 and filmfrom=1.3 take
/// pictures that often from that far into each move instead, for a moment seen closely.
/// </summary>
internal sealed class MoveFilm
{
    private const int Most = 64, Columns = 8, OutSize = 240, EyeWidth = 320, EyeHeight = 180;
    private const string Folder = "Logs/MoveTest";
    // Where the outside camera stands from the body, in the way it faced as the move began: to its right, a little ahead, at
    // chest height, looking at the hips.
    private static readonly Vector3 Stand = new Vector3(3.2f, 1.1f, .6f);
    private const float HipsHeight = .9f;

    private readonly HashSet<string> named;
    private readonly float rate, from;
    private readonly List<Texture2D> outside = new List<Texture2D>(), eyes = new List<Texture2D>();
    private Camera outLens, eyeLens;
    private RenderTexture outFilm, eyeFilm;
    private string filming;
    private Quaternion facing;
    private float next;

    private MoveFilm(HashSet<string> named, float rate, float from)
    {
        this.named = named;
        this.rate = rate;
        this.from = from;
    }

    /// <summary>The film the marker's film= line asks for; null when it names no move.</summary>
    internal static MoveFilm From(string marker)
    {
        var names = new HashSet<string>();
        float rate = 20f, from = 0f;
        try
        {
            foreach (string line in File.ReadAllLines(marker))
                if (line.StartsWith("film=", System.StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string name in line.Substring(5).Split(','))
                        if (name.Trim().Length > 0) names.Add(name.Trim());
                }
                else if (line.StartsWith("filmrate=", System.StringComparison.OrdinalIgnoreCase))
                    float.TryParse(line.Substring(9), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rate);
                else if (line.StartsWith("filmfrom=", System.StringComparison.OrdinalIgnoreCase))
                    float.TryParse(line.Substring(9), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out from);
        }
        catch (IOException) { }
        return names.Count > 0 ? new MoveFilm(names, Mathf.Clamp(rate, 1f, 120f), Mathf.Max(0f, from)) : null;
    }

    /// <summary>A move begins: filmed from here if it is named, the outside camera set by the way the body faces now.</summary>
    internal void Begin(string move, Transform player)
    {
        End();
        if (!named.Contains(move)) return;
        filming = move;
        facing = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
        next = from;
    }

    /// <summary>This frame, <paramref name="clock"/> seconds into the move: a picture each way when one is due.</summary>
    internal void Shoot(Transform player, float clock)
    {
        if (filming == null || clock < next || outside.Count >= Most) return;
        next += 1f / rate;
        Ready();
        Vector3 hips = player.position + Vector3.up * HipsHeight;
        outLens.transform.SetPositionAndRotation(player.position + facing * Stand, Quaternion.LookRotation(hips - (player.position + facing * Stand)));
        outside.Add(Take(outLens, outFilm));
        var view = Camera.main;
        if (view == null) return;
        eyeLens.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
        eyeLens.fieldOfView = view.fieldOfView;
        eyeLens.nearClipPlane = view.nearClipPlane;
        eyeLens.cullingMask = view.cullingMask;
        eyes.Add(Take(eyeLens, eyeFilm));
    }

    /// <summary>The move is over: its sheets are written.</summary>
    internal void End()
    {
        if (filming != null)
        {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, string.Join("_", filming.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_'));
            Sheet(outside, OutSize, OutSize, file + "-out.png");
            Sheet(eyes, EyeWidth, EyeHeight, file + "-eye.png");
        }
        filming = null;
        foreach (var picture in outside) Object.Destroy(picture);
        foreach (var picture in eyes) Object.Destroy(picture);
        outside.Clear();
        eyes.Clear();
    }

    /// <summary>The cameras and their films go with the test.</summary>
    internal void Dispose()
    {
        End();
        if (outLens != null) Object.Destroy(outLens.gameObject);
        if (eyeLens != null) Object.Destroy(eyeLens.gameObject);
        if (outFilm != null) outFilm.Release();
        if (eyeFilm != null) eyeFilm.Release();
    }

    private void Ready()
    {
        if (outLens != null) return;
        outFilm = new RenderTexture(OutSize, OutSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        eyeFilm = new RenderTexture(EyeWidth, EyeHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        outLens = Lens("Move film: outside", outFilm, 45f);
        eyeLens = Lens("Move film: eyes", eyeFilm, 60f);
    }

    private static Camera Lens(string name, RenderTexture film, float fieldOfView)
    {
        var holder = new GameObject(name);
        Object.DontDestroyOnLoad(holder);
        var lens = holder.AddComponent<Camera>();
        lens.enabled = false;
        lens.fieldOfView = fieldOfView;
        lens.nearClipPlane = .05f;
        lens.farClipPlane = 300f;
        lens.targetTexture = film;
        UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(lens).renderPostProcessing = false;
        return lens;
    }

    private static Texture2D Take(Camera lens, RenderTexture film)
    {
        var picture = new Texture2D(film.width, film.height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            lens.Render();
            RenderTexture.active = film;
            picture.ReadPixels(new Rect(0f, 0f, film.width, film.height), 0, 0);
            picture.Apply();
        }
        finally
        {
            RenderTexture.active = previous;
        }
        return picture;
    }

    // The pictures in order, left to right and down, the first at the top left.
    private static void Sheet(List<Texture2D> pictures, int width, int height, string path)
    {
        if (pictures.Count == 0) return;
        int rows = (pictures.Count + Columns - 1) / Columns;
        var sheet = new Texture2D(Columns * width, rows * height, TextureFormat.RGB24, false);
        var blank = new Color32[sheet.width * sheet.height];
        sheet.SetPixels32(blank);
        for (int i = 0; i < pictures.Count; i++)
            sheet.SetPixels(i % Columns * width, (rows - 1 - i / Columns) * height, width, height, pictures[i].GetPixels());
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Object.Destroy(sheet);
    }
}
