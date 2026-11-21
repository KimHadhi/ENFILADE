using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// What the player sees of an enemy's outline, drawn from the player's eye with the game's own shaders and its own outline
/// pass (EnemyOutlines, OutlinePass) without Play: every mark's look (a ping in the user's own colour and in the default
/// pink, the executable mark, a holder, a Ghost's sight, a wave's last enemies), each on the reaper and on a plain zombie,
/// at 7 m and 15 m, in the open and behind waist-high cover; an enemy marked the moment it appeared, still in its
/// capsule shell, then dressed and pinged (the test-room reaper of 2026-11-21); and whether the outline holds still while
/// the view drifts a fraction of a pixel a frame, or a pole slides in front of the enemy (Sway: the flicker the user saw
/// aiming at a pinged target the same day).
/// Each enemy is dressed as ZombieBody dresses it (the kind's body at its kind's size standing on the enemy capsule's
/// feet, the capsule shell switched off, the reaper's axe in its hand), posed from the pack's idle, and marked with the
/// look each source gives (PingMarks.LookOf, HoldReveal.LookOf, GhostSight.LookOf, EnemyReveal.LookOf, and
/// HitFlashRenderer.SetMark for the executable mark). Then:
/// - body: the pixels where the picture with the enemy differs from the one without it.
/// - drawn: the pixels the mark changes, told by how far each lies from the body: over it, 1-6 px out (the band),
///   7-20 px out, and beyond 20 px (a halo or a shape that is not the body's).
/// The report is Logs/outlines.txt, the pictures Logs/Outlines/&lt;case&gt;.png (the unmarked enemy beside the marked one,
/// cut round the body). Run from ENFILADE → 전투 → 외곽선 점검, or by making the file Temp/enfilade-outlines.request;
/// never while the game plays.
/// Why it exists: the user pinged a reaper 7 m off in the test room (2026-11-21) and saw a white pill over it instead of
/// its outline, and said the outlines as a whole read poorly on screen. A check of numbers in the tables cannot say what
/// a shader draws. Its first run (with the old outlines) drew the pill: a see-through copy of the capsule shell made
/// before the body was put on, with the ping's back-face outline laid on that copy.
/// </summary>
[InitializeOnLoad]
internal static class OutlineBench
{
    private const string Request = "Temp/enfilade-outlines.request", ReportFile = "Logs/outlines.txt", Folder = "Logs/Outlines";
    private const int Width = 1920, Height = 1080;
    // The player's view: the default field across a 16:9 screen, the eye's height standing (m), the near plane.
    private const float FieldAcross = 90f, Aspect = 16f / 9f, EyeHeight = 1.62f, NearClip = .03f;
    // A pixel counts as changed when its channels together move this much (0-255 each).
    private const int Changed = 18;
    // How far out of the body a pixel may lie and still be an outline (px), and where a halo begins.
    private const int OutlineReach = 6, HaloFrom = 20, Furthest = 64;
    // The user's own colour (settings, 2026-11-21) and the palette's first, the default.
    private static readonly Color UserWhite = Color.white, Pink = new Color32(0xFF, 0x4F, 0x78, 0xFF);
    private static readonly MethodInfo FlashFrame = typeof(HitFlashRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double next;

    static OutlineBench() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            File.Delete(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        Run();
    }

    private enum Mark { Ping, PingPink, Executable, Holder, Ghost, Wave }

    [MenuItem("ENFILADE/전투/외곽선 점검 (눈으로 본 화면)")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var text = new StringBuilder();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} enemy outlines as the player's eye sees them, drawn by the game's own shaders without Play (OutlineBench).");
        text.AppendLine($"Eye {EyeHeight.ToString("0.00", CultureInfo.InvariantCulture)} m over the floor, {FieldAcross} degrees across a 16:9 screen, {Width} x {Height}; no post-processing.");
        text.AppendLine(Shaders());
        text.AppendLine("  body    pixels where the picture with the enemy differs from the one without it");
        text.AppendLine($"  drawn   pixels the mark changes: over the body | 1-{OutlineReach} px out (outline) | {OutlineReach + 1}-{HaloFrom} px out | beyond {HaloFrom} px (not the body's shape)");
        text.AppendLine("  box     the drawn pixels' box beside the body's (w x h px)");
        text.AppendLine($"Pictures: {Folder}/<case>.png, the unmarked enemy left, the marked one right.");
        text.AppendLine();
        Directory.CreateDirectory(Folder);
        int looked = 0;
        using (var stage = new Stage())
        {
            foreach (bool cover in new[] { false, true })
            foreach (float metres in new[] { 7f, 15f })
            {
                stage.Cover(cover, metres);
                var empty = stage.Shoot();
                foreach (var kind in new[] { ZombieKind.Reaper, ZombieKind.Shambler })
                {
                    Body body = null;
                    try
                    {
                        body = stage.Zombie(kind, metres);
                        if (body == null) { text.AppendLine($"!  {kind}: no body to wear (EliteLooks / ZombieLooks)"); continue; }
                        var plain = stage.Shoot();
                        foreach (Mark mark in Enum.GetValues(typeof(Mark)))
                        {
                            string name = $"{kind.ToString().ToLowerInvariant()}.{metres:0}m{(cover ? ".cover" : "")}.{mark.ToString().ToLowerInvariant()}";
                            Put(body, mark);
                            var marked = stage.Shoot();
                            Take(body);
                            text.AppendLine(Compare(name, empty, plain, marked));
                            looked++;
                        }
                    }
                    catch (Exception error)
                    {
                        text.AppendLine($"!  {kind} {metres:0} m: not looked at ({error.GetType().Name}: {error.Message})");
                        Debug.LogException(error);
                    }
                    finally
                    {
                        body?.Dispose();
                    }
                }
                text.AppendLine();
            }
        }
        text.AppendLine("Marked as it appeared: the Ghost's sight on the enemy still in its capsule shell, then its body put on and a ping.");
        using (var stage = new Stage())
        {
            stage.Cover(false, 7f);
            var empty = stage.Shoot();
            Body body = null;
            try
            {
                body = stage.Zombie(ZombieKind.Reaper, 7f, markedEarly: true);
                var plain = stage.Shoot();
                Put(body, Mark.Ping);
                var marked = stage.Shoot();
                Take(body);
                text.AppendLine(Compare("reaper.7m.markedearly.ping", empty, plain, marked));
                looked++;
            }
            finally
            {
                body?.Dispose();
            }
        }
        text.AppendLine();
        looked += Sway(text);
        text.AppendLine($"({looked} pictures)");
        File.WriteAllText(ReportFile, text.ToString(), new UTF8Encoding(false));
        Debug.Log($"OutlineBench: {looked} pictures. {ReportFile}, {Folder}");
    }

    // ---- Steady while the view sways ----

    // Aimed, the sight's breathing moves the picture a fraction of a pixel a frame: each frame here turns the eye (or slides
    // a pole in front of the enemy) by this share of a pixel, for this many frames.
    private const float SwayStep = .15f;
    private const int SwayFrames = 14, StripFrames = 6, StripZoom = 3;

    // The user, 2026-11-21, a pinged target looked at aimed: "아웃라인이 flickering". An outline that holds still under a
    // drift of a fraction of a pixel changes each of its pixels a little from frame to frame; one that snaps to whole
    // pixels turns whole pixels on and off: those are counted (flips), and six frames are kept side by side.
    private static int Sway(StringBuilder text)
    {
        text.AppendLine($"Steady while the view sways: {SwayFrames} frames, the eye turning (or a pole sliding in front of the enemy) {SwayStep.ToString("0.00", CultureInfo.InvariantCulture)} px a frame; the pinged reaper at 12 m.");
        text.AppendLine("  flips   pixels of the mark's own drawing (marked minus unmarked) that change by more than half between two frames: an outline that holds still has none");
        text.AppendLine("  drawn   the mark's pixels in a frame, on average");
        int pictures = 0;
        foreach (bool pole in new[] { false, true })
        using (var stage = new Stage())
        {
            Body body = null;
            try
            {
                var empty = stage.Shoot();
                body = stage.Zombie(ZombieKind.Reaper, 12f);
                float degreesPerPixel = Camera.HorizontalToVerticalFieldOfView(FieldAcross, Aspect) / Height;
                float metresPerPixel = 2f * 11f * Mathf.Tan(Camera.HorizontalToVerticalFieldOfView(FieldAcross, Aspect) * .5f * Mathf.Deg2Rad) / Height;
                float[] last = null;
                var strip = new List<Color32[]>();
                Color32[] worst = null;
                RectInt box = Nothing();
                int flipsMost = 0, flipsAll = 0, drawnAll = 0;
                for (int frame = 0; frame < SwayFrames; frame++)
                {
                    if (pole) stage.Pole(true, -.32f + frame * SwayStep * metresPerPixel, 11f);
                    else stage.Turn(frame * SwayStep * degreesPerPixel);
                    Take(body);
                    var plain = stage.Shoot();
                    Put(body, Mark.PingPink);
                    var marked = stage.Shoot();
                    pictures += 2;
                    if (frame == 0)
                        for (int i = 0; i < plain.Length; i++)
                            if (Moved(empty[i], plain[i]) || Moved(plain[i], marked[i])) Grow(ref box, i);
                    var layer = new float[plain.Length];
                    int drawn = 0;
                    for (int i = 0; i < plain.Length; i++)
                    {
                        layer[i] = (Math.Abs(marked[i].r - plain[i].r) + Math.Abs(marked[i].g - plain[i].g) + Math.Abs(marked[i].b - plain[i].b)) / 765f;
                        if (layer[i] > .1f) drawn++;
                    }
                    drawnAll += drawn;
                    if (last != null)
                    {
                        int flips = 0;
                        var marks = (Color32[])marked.Clone();
                        for (int i = 0; i < layer.Length; i++)
                            if (Math.Abs(layer[i] - last[i]) > .5f * Math.Max(.2f, Math.Max(layer[i], last[i])) && Math.Abs(layer[i] - last[i]) > .15f)
                            {
                                flips++;
                                marks[i] = new Color32(0, 255, 255, 255);
                            }
                        flipsAll += flips;
                        // The frame with the most, its flipped pixels painted cyan (the strip's last picture).
                        if (flips >= flipsMost) worst = marks;
                        flipsMost = Math.Max(flipsMost, flips);
                    }
                    last = layer;
                    if (frame < StripFrames) strip.Add(marked);
                }
                if (worst != null) strip.Add(worst);
                string name = pole ? "sway.pole" : "sway.drift";
                int steps = SwayFrames - 1;
                text.AppendLine($"   {name,-34} drawn {drawnAll / SwayFrames,6} | flips a frame: mean {flipsAll / (float)steps,7:0.0} ({100f * flipsAll / Math.Max(1, steps) / Math.Max(1, drawnAll / SwayFrames),4:0.0}% of drawn), most {flipsMost}");
                SaveStrip(name, strip, box);
            }
            catch (Exception error)
            {
                text.AppendLine($"!  sway ({(pole ? "pole" : "drift")}): not looked at ({error.GetType().Name}: {error.Message})");
                Debug.LogException(error);
            }
            finally
            {
                body?.Dispose();
            }
        }
        text.AppendLine();
        return pictures;
    }

    // Frames side by side, cut round the enemy and enlarged (whole pixels, no smoothing) so a pixel that turns on and off shows.
    private static void SaveStrip(string name, List<Color32[]> frames, RectInt box)
    {
        if (box.x == int.MaxValue || frames.Count == 0) return;
        const int margin = 12, gap = 4;
        int left = Math.Max(0, box.x - margin), bottom = Math.Max(0, box.y - margin);
        int right = Math.Min(Width - 1, box.width + margin), top = Math.Min(Height - 1, box.height + margin);
        int w = (right - left + 1) * StripZoom, h = (top - bottom + 1) * StripZoom;
        var picture = new Texture2D(w * frames.Count + gap * (frames.Count - 1), h, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var pixels = new Color32[picture.width * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            for (int f = 0; f < frames.Count; f++)
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * picture.width + f * (w + gap) + x] = frames[f][(bottom + y / StripZoom) * Width + left + x / StripZoom];
            picture.SetPixels32(pixels);
            picture.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), picture.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(picture);
        }
    }

    // ---- Marks, by each source's own look ----

    private static void Put(Body body, Mark mark)
    {
        var foe = body.Root.transform;
        switch (mark)
        {
            case Mark.Ping: EnemyOutlines.Set(foe, OutlineSource.Ping, PingMarks.LookOf(UserWhite, 1f)); break;
            case Mark.PingPink: EnemyOutlines.Set(foe, OutlineSource.Ping, PingMarks.LookOf(Pink, 1f)); break;
            case Mark.Holder: EnemyOutlines.Set(foe, OutlineSource.Holder, HoldReveal.LookOf(HolderFill(), .55f, 1f)); break;
            case Mark.Ghost: EnemyOutlines.Set(foe, OutlineSource.Ghost, GhostLook()); break;
            case Mark.Wave: EnemyOutlines.Set(foe, OutlineSource.Wave, EnemyReveal.LookOf(WaveColour())); break;
            case Mark.Executable:
                var flash = HitFlashRenderer.For(foe);
                flash.SetMark(1f);
                // One frame of the flash: it lays the mark's tint on every renderer it covers.
                FlashFrame.Invoke(flash, null);
                break;
        }
    }

    private static void Take(Body body)
    {
        var foe = body.Root.transform;
        foreach (OutlineSource source in Enum.GetValues(typeof(OutlineSource))) EnemyOutlines.Clear(foe, source);
        var flash = HitFlashRenderer.For(foe);
        flash.SetMark(0f);
        FlashFrame.Invoke(flash, null);
    }

    private static OutlineLook GhostLook()
        => GhostSight.LookOf(MomentLook.ColorOf(PlayerClass.Ghost), PlayerClass.Ability("Ghost", "sightAlpha", .3f), 1f);

    // The holder's fill and a wave's colour as their tables give them.
    private static Color HolderFill()
        => ColorUtility.TryParseHtmlString(GameTables.Text(HoldReveal.TableName, HoldReveal.TableRow, "holderColor", "#FF294DB3"), out var colour)
            ? colour : new Color(1f, .16f, .3f, .7f);

    private static Color WaveColour()
        => ColorUtility.TryParseHtmlString(GameTables.Text("Waves", "Default", "revealColor", "#FF3326A1"), out var colour) ? colour : new Color(1f, .2f, .15f, .63f);

    // Whether the outline shaders compiled here (a failed one draws nothing, or magenta).
    private static string Shaders()
    {
        var line = new StringBuilder("Shaders:");
        foreach (string path in new[] { "Combat/Shaders/OutlineMask", "Combat/Shaders/OutlineLay", "Combat/Shaders/HitFlashOverlay" })
        {
            var shader = Resources.Load<Shader>(path);
            if (shader == null) { line.Append($" {path} MISSING"); continue; }
            var messages = ShaderUtil.GetShaderMessages(shader);
            int errors = 0;
            foreach (var message in messages) if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors++;
            line.Append($" {shader.name} {(errors == 0 ? "ok" : $"{errors} errors: {messages[0].message} (line {messages[0].line})")};");
        }
        return line.ToString();
    }

    // ---- Measuring ----

    private static readonly Queue<int> Open = new Queue<int>();

    private static string Compare(string name, Color32[] empty, Color32[] plain, Color32[] marked)
    {
        int count = empty.Length;
        var away = new int[count];
        int bodyPixels = 0;
        RectInt bodyBox = Nothing(), drawnBox = Nothing();
        Open.Clear();
        for (int i = 0; i < count; i++)
        {
            if (Moved(empty[i], plain[i])) { away[i] = 0; bodyPixels++; Open.Enqueue(i); Grow(ref bodyBox, i); }
            else away[i] = int.MaxValue;
        }
        // How far each pixel is from the body (steps across and up), out to Furthest.
        while (Open.Count > 0)
        {
            int at = Open.Dequeue(), x = at % Width, y = at / Width, d = away[at] + 1;
            if (d > Furthest) continue;
            if (x > 0 && away[at - 1] > d) { away[at - 1] = d; Open.Enqueue(at - 1); }
            if (x < Width - 1 && away[at + 1] > d) { away[at + 1] = d; Open.Enqueue(at + 1); }
            if (y > 0 && away[at - Width] > d) { away[at - Width] = d; Open.Enqueue(at - Width); }
            if (y < Height - 1 && away[at + Width] > d) { away[at + Width] = d; Open.Enqueue(at + Width); }
        }
        int over = 0, outline = 0, near = 0, beyond = 0;
        for (int i = 0; i < count; i++)
        {
            if (!Moved(plain[i], marked[i])) continue;
            Grow(ref drawnBox, i);
            int d = away[i];
            if (d == 0) over++;
            else if (d <= OutlineReach) outline++;
            else if (d <= HaloFrom) near++;
            else beyond++;
        }
        int drawn = over + outline + near + beyond;
        Save(name, plain, marked, bodyBox, drawnBox);
        string Share(int part) => drawn > 0 ? $"{100f * part / drawn,3:0}%" : "  -";
        string flag = drawn == 0 ? "!  " : (near + beyond) > drawn * .1f || over > bodyPixels * .5f ? "!  " : "   ";
        return $"{flag}{name,-34} body {bodyPixels,6} | drawn {drawn,6}: over {Share(over)} ({over}) | outline {Share(outline)} | {OutlineReach + 1}-{HaloFrom} {Share(near)} | beyond {Share(beyond)}"
            + $" | box {Size(drawnBox)} of body {Size(bodyBox)}";
    }

    private static bool Moved(Color32 a, Color32 b) => Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > Changed;

    private static RectInt Nothing() => new RectInt(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

    // xMin, yMin kept in x, y; xMax, yMax in width, height until Size reads them.
    private static void Grow(ref RectInt box, int index)
    {
        int x = index % Width, y = index / Width;
        box = new RectInt(Math.Min(box.x, x), Math.Min(box.y, y), Math.Max(box.width, x), Math.Max(box.height, y));
    }

    private static string Size(RectInt box) => box.x == int.MaxValue ? "-" : $"{box.width - box.x + 1}x{box.height - box.y + 1}";

    // The unmarked and the marked picture side by side, cut round both boxes with a margin.
    private static void Save(string name, Color32[] plain, Color32[] marked, RectInt bodyBox, RectInt drawnBox)
    {
        if (bodyBox.x == int.MaxValue) return;
        int left = bodyBox.x, bottom = bodyBox.y, right = bodyBox.width, top = bodyBox.height;
        if (drawnBox.x != int.MaxValue)
        {
            left = Math.Min(left, drawnBox.x); bottom = Math.Min(bottom, drawnBox.y);
            right = Math.Max(right, drawnBox.width); top = Math.Max(top, drawnBox.height);
        }
        const int margin = 40, gap = 6;
        left = Math.Max(0, left - margin); bottom = Math.Max(0, bottom - margin);
        right = Math.Min(Width - 1, right + margin); top = Math.Min(Height - 1, top + margin);
        int w = right - left + 1, h = top - bottom + 1;
        var picture = new Texture2D(w * 2 + gap, h, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var pixels = new Color32[picture.width * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int from = (bottom + y) * Width + left + x;
                pixels[y * picture.width + x] = plain[from];
                pixels[y * picture.width + w + gap + x] = marked[from];
            }
            picture.SetPixels32(pixels);
            picture.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), picture.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(picture);
        }
    }

    // ---- An enemy as ZombieBody dresses it ----

    private sealed class Body : IDisposable
    {
        public GameObject Root;
        public PlayableGraph Graph;

        public void Dispose()
        {
            if (Root != null)
                foreach (OutlineSource source in Enum.GetValues(typeof(OutlineSource))) EnemyOutlines.Clear(Root.transform, source);
            if (Graph.IsValid()) Graph.Destroy();
            if (Root != null) Object.DestroyImmediate(Root);
        }
    }

    // ---- The stage ----

    /// <summary>A scene of its own (the editor's preview scene, as GunSightBench's): a floor, two lights, the eye, and a
    /// waist-high wall between the eye and the enemy when asked.</summary>
    private sealed class Stage : IDisposable
    {
        private readonly Scene scene;
        private readonly GameObject root, cover, pole;
        private readonly Camera eye;
        private readonly RenderTexture film;
        private readonly Texture2D shot;
        private readonly Material floorLook, coverLook;
        private readonly bool wasAsync;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly SphericalHarmonicsL2[] probes;

        public Stage()
        {
            wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Outline Bench") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);

            var eyeObject = new GameObject("Eye") { hideFlags = HideFlags.HideAndDontSave };
            eyeObject.transform.SetParent(root.transform, false);
            eye = eyeObject.AddComponent<Camera>();
            eye.enabled = false;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = new Color(.16f, .18f, .21f);
            eye.allowHDR = true;
            // As the player's camera (Player.prefab: MSAA on, the pipeline's 4x): the outline's last pass reads its stencil.
            eye.allowMSAA = true;
            eye.nearClipPlane = NearClip;
            eye.farClipPlane = 200f;
            eye.useOcclusionCulling = false;
            eye.scene = scene;
            eye.aspect = Aspect;
            eye.fieldOfView = Camera.HorizontalToVerticalFieldOfView(FieldAcross, Aspect);
            eye.transform.SetPositionAndRotation(new Vector3(0f, EyeHeight, 0f), Quaternion.identity);
            var data = eye.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = true;
            film = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Outline Bench Film", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 4 };
            film.Create();
            eye.targetTexture = film;
            shot = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };

            AddLight("Key", Quaternion.Euler(40f, 30f, 0f), 1.1f);
            AddLight("Fill", Quaternion.Euler(20f, 200f, 0f), .5f);
            var flat = new SphericalHarmonicsL2();
            flat.AddAmbientLight(new Color(.4f, .42f, .46f));
            probes = new[] { flat };

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            floorLook = new Material(lit) { hideFlags = HideFlags.HideAndDontSave };
            floorLook.SetColor("_BaseColor", new Color(.34f, .34f, .33f));
            coverLook = new Material(lit) { hideFlags = HideFlags.HideAndDontSave };
            coverLook.SetColor("_BaseColor", new Color(.42f, .38f, .32f));
            var floor = Solid(PrimitiveType.Plane, "Floor", floorLook);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            cover = Solid(PrimitiveType.Cube, "Cover", coverLook);
            cover.SetActive(false);
            pole = Solid(PrimitiveType.Cube, "Pole", coverLook);
            pole.SetActive(false);
            // The game puts the pass in its cameras' queue on its own at play; the editor's cameras get it from here.
            OutlinePass.Hook();
        }

        private GameObject Solid(PrimitiveType shape, string name, Material look)
        {
            var made = GameObject.CreatePrimitive(shape);
            made.name = name;
            made.hideFlags = HideFlags.HideAndDontSave;
            if (made.TryGetComponent(out Collider solid)) Object.DestroyImmediate(solid);
            made.transform.SetParent(root.transform, false);
            var renderer = made.GetComponent<Renderer>();
            renderer.sharedMaterial = look;
            Light(renderer);
            return made;
        }

        private void AddLight(string name, Quaternion turn, float intensity)
        {
            var holder = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(root.transform, false);
            holder.transform.rotation = turn;
            var light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        // Lit by the stage alone; skinned again at every picture (outside play nothing else re-skins between renders).
        private void Light(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            renderer.GetPropertyBlock(block);
            block.CopySHCoefficientArraysFrom(probes);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>The eye turned this far to the right of straight ahead (degrees).</summary>
        public void Turn(float yaw) => eye.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        /// <summary>A pole 6 cm wide standing at (x, z) on the floor, as a sight's edge or a post in front of the enemy; or none.</summary>
        public void Pole(bool on, float x, float z)
        {
            pole.SetActive(on);
            pole.transform.localPosition = new Vector3(x, 1.5f, z);
            pole.transform.localScale = new Vector3(.06f, 3f, .06f);
        }

        /// <summary>A wall 1 m high, 2.5 m wide, 1.5 m before the enemy at this distance; or none.</summary>
        public void Cover(bool on, float metres)
        {
            cover.SetActive(on);
            cover.transform.localPosition = new Vector3(0f, .5f, metres - 1.5f);
            cover.transform.localScale = new Vector3(2.5f, 1f, .3f);
        }

        /// <summary>The kind's body as ZombieBody wears it, standing this far ahead of the eye, facing it.</summary>
        public Body Zombie(ZombieKind kind, float metres, bool markedEarly = false)
        {
            var character = kind == ZombieKind.Shambler ? FirstPlain() : EliteLooks.For(kind).character;
            if (character == null) return null;
            var body = new Body { Root = new GameObject(kind + " (Outline Bench)") { hideFlags = HideFlags.HideAndDontSave } };
            body.Root.transform.SetParent(root.transform, false);
            // The enemy stands a metre up, at its walk-mesh agent's base offset, its capsule (2 m, Enemy_Test) round that.
            body.Root.transform.SetLocalPositionAndRotation(new Vector3(0f, 1f, metres), Quaternion.Euler(0f, 180f, 0f));
            // The enemy's own capsule shell (Enemy_Test's built-in capsule), switched off once a body is worn (ZombieBody.Dress).
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var shellMesh = capsule.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(capsule);
            body.Root.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            var shell = body.Root.AddComponent<MeshRenderer>();
            shell.sharedMaterial = floorLook;
            Light(shell);
            // Marked while still a capsule (a Ghost near the enemy as it appears, before ZombieBody.Start puts its body on),
            // and drawn so: EnemyOutlines gathers the body as it stands at that frame.
            if (markedEarly)
            {
                EnemyOutlines.Set(body.Root.transform, OutlineSource.Ghost, GhostLook());
                Shoot();
            }
            shell.enabled = false;
            var model = Object.Instantiate(character, body.Root.transform);
            model.name = character.name;
            model.hideFlags = HideFlags.HideAndDontSave;
            model.transform.SetLocalPositionAndRotation(new Vector3(0f, -1f, 0f), Quaternion.identity);
            model.transform.localScale = Vector3.one * ScaleOf(kind);
            foreach (var part in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!part.gameObject.activeSelf) Object.DestroyImmediate(part.gameObject);
            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var looks = ZombieLooks.Instance;
                var clip = looks != null ? looks.idle : null;
                if (clip != null && animator.isHuman)
                {
                    body.Graph = PlayableGraph.Create("Outline Bench Pose");
                    body.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(body.Graph, "Pose", animator);
                    var pose = AnimationClipPlayable.Create(body.Graph, clip);
                    output.SetSourcePlayable(pose);
                    pose.SetTime(.5);
                    body.Graph.Evaluate(0f);
                }
                var hand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                var prop = kind == ZombieKind.Shambler ? null : EliteLooks.For(kind).handProp;
                if (hand != null && prop != null)
                {
                    // ZombieElite.Holder: on the bone, facing the body's way, sized in metres whatever the rig's scale.
                    var holder = new GameObject("Elite Hand").transform;
                    holder.SetParent(hand, false);
                    Vector3 flat = Vector3.ProjectOnPlane(model.transform.forward, Vector3.up).normalized;
                    holder.SetPositionAndRotation(hand.position, Quaternion.LookRotation(flat, Vector3.up));
                    Vector3 scale = hand.lossyScale;
                    holder.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(scale.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(scale.y)),
                        1f / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
                    Object.Instantiate(prop, holder, false);
                }
            }
            foreach (var renderer in body.Root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.hideFlags = HideFlags.HideAndDontSave;
                Light(renderer);
            }
            return body;
        }

        // The first of the plain zombies' bodies (ZombieLooks.characters leaves the elites' out).
        private static GameObject FirstPlain()
        {
            var looks = ZombieLooks.Instance;
            if (looks == null || looks.characters == null) return null;
            foreach (var character in looks.characters) if (character != null) return character;
            return null;
        }

        // The kind's size from Tables/ZombieKinds (scale), as ZombieType reads it.
        private static float ScaleOf(ZombieKind kind)
        {
            string path = TablePaths.Of("ZombieKinds");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 1f;
            int column = -1;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimStart('﻿');
                if (line.StartsWith("#")) continue;
                var cells = line.Split(',');
                if (column < 0) { column = Array.IndexOf(cells, "scale"); continue; }
                if (cells[0] == kind.ToString() && column < cells.Length
                    && float.TryParse(cells[column], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale) && scale > 0f)
                    return scale;
            }
            return 1f;
        }

        public Color32[] Shoot()
        {
            var previous = RenderTexture.active;
            try
            {
                eye.Render();
                RenderTexture.active = film;
                shot.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                shot.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }
            return shot.GetPixels32();
        }

        public void Dispose()
        {
            eye.targetTexture = null;
            film.Release();
            Object.DestroyImmediate(film);
            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(floorLook);
            Object.DestroyImmediate(coverLook);
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            ShaderUtil.allowAsyncCompilation = wasAsync;
        }
    }
}
