using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → HUD → 겹침 검사 (화면비·설정 모두): in Play, goes through every screen shape (16:9, 16:10, 21:9, 4:3), the
/// HUD's angle (0, 20, 40°) and tilt (0, 1, 2), and a full Q/E lean each way at the player's own angle and tilt; each once
/// as the game is showing it and once with every piece that can show a passing state holding it (IHudPreview: an
/// assault's time row, the new-objective alert). Each time it measures the HUD as drawn (HudOverlapCheck) and at the end
/// writes Logs/HudOverlap/report.txt, every overlap and run-off with its depth in px and the situations it shows in, and a
/// picture of each situation with a problem (overlaps outlined in red, run-offs in orange). Sessions that cannot run the
/// game read the report and the pictures. The Game view's size, the player's HUD settings and the lean come back after;
/// nothing is saved. Stopping Play stops it.
/// </summary>
public static class HudOverlapSweep
{
    private const string Title = "HUD 겹침 검사";
    private const string Folder = "Logs/HudOverlap";
    // Frames and seconds a situation is given to settle before it is measured: the canvas re-laid for a new screen, the
    // key cap stepped down under an assault's time row, the tape grown.
    private const int SettleFrames = 6;
    private const double SettleSeconds = .5;

    private struct Shape
    {
        public int Width, Height;
        public string Name;
    }

    private sealed class Situation
    {
        public Shape Shape;
        public float Angle, Tilt, Lean;
        public bool Previews;
        public string Label, Picture;
        public readonly List<HudOverlapCheck.Finding> Findings = new List<HudOverlapCheck.Finding>();
    }

    private static readonly Shape[] Shapes =
    {
        new Shape { Width = 1920, Height = 1080, Name = "16x9" },
        new Shape { Width = 1920, Height = 1200, Name = "16x10" },
        new Shape { Width = 2560, Height = 1080, Name = "21x9" },
        new Shape { Width = 1440, Height = 1080, Name = "4x3" },
    };
    private static readonly float[] Angles = { 0f, 20f, 40f }, Tilts = { 0f, 1f, 2f };

    private enum Step { Apply, Settle, Measure, Picture, Next }

    private static readonly List<Situation> situations = new List<Situation>();
    private static readonly List<IHudPreview> previews = new List<IHudPreview>();
    private static bool running, resized, previewing;
    private static int index, frameAt, sizeIndex = -1;
    private static double timeAt;
    private static Step step;
    private static float angle, tilt;
    private static string pieces;

    [MenuItem("Tools/HUD/겹침 검사 (화면비·설정 모두)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog(Title, "플레이 중에 눌러 주세요. 게임 화면에 HUD가 보이는 동안 화면비·원근·기울기·기울이기를 돌려 가며 재고 Logs/HudOverlap/report.txt에 적습니다.", "확인");
            return;
        }
        if (running) return;
        Begin();
    }

    private static void Begin()
    {
        running = true;
        situations.Clear();
        var settings = GameSettings.Current;
        angle = settings.hudAngle;
        tilt = settings.hudTilt;
        float lean = HudOverlapCheck.Current.lean * HudCamera.Settings.share;
        foreach (var shape in Shapes)
            foreach (bool held in new[] { false, true })
            {
                foreach (float a in Angles)
                    foreach (float t in Tilts)
                        situations.Add(new Situation { Shape = shape, Angle = a, Tilt = t, Previews = held });
                situations.Add(new Situation { Shape = shape, Angle = angle, Tilt = tilt, Lean = lean, Previews = held });
                situations.Add(new Situation { Shape = shape, Angle = angle, Tilt = tilt, Lean = -lean, Previews = held });
            }
        previews.Clear();
        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (behaviour is IHudPreview preview) previews.Add(preview);
        sizeIndex = SelectedSize();
        Directory.CreateDirectory(Folder);
        foreach (string old in Directory.GetFiles(Folder, "*.png")) File.Delete(old);
        pieces = null;
        index = 0;
        step = Step.Apply;
        HudOverlapCheck.Sweeping = true;
        EditorApplication.update += Tick;
        Debug.Log($"[{Title}] {situations.Count}가지를 잽니다. 게임 화면이 화면비와 설정을 바꿔 가며 움직입니다.");
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) { Finish(false); return; }
        var now = situations[index];
        switch (step)
        {
            case Step.Apply:
                Apply(now);
                frameAt = Time.frameCount;
                timeAt = EditorApplication.timeSinceStartup;
                step = Step.Settle;
                break;
            case Step.Settle:
                if (Time.frameCount - frameAt >= SettleFrames && EditorApplication.timeSinceStartup - timeAt >= SettleSeconds) step = Step.Measure;
                break;
            case Step.Measure:
                HudOverlapCheck.Measure(now.Findings);
                now.Label = HudOverlapCheck.Situation() + (now.Previews ? " · 지나가는 상태 보임" : "");
                if (pieces == null) pieces = HudOverlapCheck.Pieces();
                if (now.Findings.Count == 0) { step = Step.Next; break; }
                var overlaps = new List<Vector2[]>();
                var outside = new List<Vector2[]>();
                foreach (var finding in now.Findings)
                {
                    if (finding.OffScreen) outside.Add(finding.ShapeA);
                    else { overlaps.Add(finding.ShapeA); overlaps.Add(finding.ShapeB); }
                }
                HudOverlapMarks.Show(overlaps, outside, 5f);
                frameAt = Time.frameCount;
                step = Step.Picture;
                break;
            case Step.Picture:
                // The outlines drawn a frame first, then the frame with them in it taken.
                if (Time.frameCount - frameAt < 2) break;
                now.Picture = $"{index + 1:00}_{now.Shape.Name}_a{now.Angle:0}_t{now.Tilt:0.#}_l{now.Lean:+0.#;-0.#;0}{(now.Previews ? "_states" : "")}.png";
                ScreenCapture.CaptureScreenshot(Path.Combine(Folder, now.Picture));
                frameAt = Time.frameCount;
                step = Step.Next;
                break;
            case Step.Next:
                // A picture is written at the end of a frame; give it one before the outlines go.
                if (now.Picture != null && Time.frameCount - frameAt < 2) break;
                HudOverlapMarks.Clear();
                if (++index >= situations.Count) { Finish(true); return; }
                step = Step.Apply;
                break;
        }
    }

    private static void Apply(Situation situation)
    {
        resized |= SetResolution(situation.Shape.Width, situation.Shape.Height);
        var settings = GameSettings.Current;
        settings.hudAngle = situation.Angle;
        settings.hudTilt = situation.Tilt;
        HudCamera.LeanHeld = situation.Lean;
        if (situation.Previews != previewing)
        {
            previewing = situation.Previews;
            foreach (var preview in previews)
                if (preview is UnityEngine.Object alive && alive != null) preview.Preview(previewing);
        }
    }

    private static void Finish(bool complete)
    {
        EditorApplication.update -= Tick;
        running = false;
        if (EditorApplication.isPlaying)
        {
            foreach (var preview in previews)
                if (preview is UnityEngine.Object alive && alive != null) preview.Preview(false);
            HudOverlapMarks.Clear();
        }
        // The settings object outlives Play when the editor keeps its scripts loaded: always put the player's back.
        var settings = GameSettings.Current;
        settings.hudAngle = angle;
        settings.hudTilt = tilt;
        previews.Clear();
        previewing = false;
        HudCamera.LeanHeld = float.NaN;
        HudOverlapCheck.Sweeping = false;
        if (resized) RestoreSize();
        resized = false;
        string report = Write(complete);
        Debug.Log($"[{Title}] {(complete ? "끝" : "Play가 멈춰 중간에 그만둠")}: {Path.GetFullPath(Path.Combine(Folder, "report.txt"))}\n{report}");
    }

    // ---- The report ----

    private static string Write(bool complete)
    {
        var text = new StringBuilder();
        int measuredCount = 0, troubled = 0;
        // Each problem once, with its deepest depth and every situation it showed in.
        var worst = new Dictionary<string, HudOverlapCheck.Finding>();
        var where = new Dictionary<string, List<string>>();
        foreach (var situation in situations)
        {
            if (situation.Label == null) continue;
            measuredCount++;
            if (situation.Findings.Count > 0) troubled++;
            foreach (var finding in situation.Findings)
            {
                string key = finding.Key;
                if (!worst.TryGetValue(key, out var known) || finding.Depth > known.Depth) worst[key] = finding;
                if (!where.TryGetValue(key, out var list)) where[key] = list = new List<string>();
                list.Add($"{situation.Label} {finding.Depth:0.#} px");
            }
        }
        text.Append($"# {Title} — {DateTime.Now:yyyy-MM-dd HH:mm} · {Application.version}\n");
        text.Append($"{measuredCount}가지를 쟀고{(complete ? "" : " (Play가 멈춰 중간까지)")} 그중 {troubled}가지에 문제가 있습니다. 서로 다른 문제 {worst.Count}개.\n");
        if (!resized) text.Append("화면비를 바꾸지 못해(PlayModeWindow 없음) 지금 게임 화면 크기에서만 쟀습니다. 게임 화면 크기를 직접 바꿔 다시 돌려 주세요.\n");
        text.Append("재는 법: HudOverlapCheck(그려진 삼각형과 글자 상자를 화면 px로). 조각 나누기: Resources/Tables/Hud/HudOverlap.csv. 수치: Hud.csv Overlap.\n\n");

        var keys = new List<string>(worst.Keys);
        keys.Sort((a, b) => worst[b].Depth.CompareTo(worst[a].Depth));
        text.Append("## 문제 (깊은 것부터)\n");
        if (keys.Count == 0) text.Append("없음.\n");
        foreach (string key in keys)
        {
            var finding = worst[key];
            var list = where[key];
            text.Append($"- {finding}\n  {list.Count}가지 경우에서: {list[0]}{(list.Count > 1 ? $" 외 {list.Count - 1}가지" : "")}\n");
        }

        text.Append("\n## 경우마다\n");
        foreach (var situation in situations)
        {
            if (situation.Label == null) continue;
            text.Append($"### {situation.Label}{(situation.Picture != null ? $" — 그림 {situation.Picture}" : "")}\n");
            if (situation.Findings.Count == 0) { text.Append("문제 없음.\n"); continue; }
            foreach (var finding in situation.Findings) text.Append($"- {finding}\n");
        }

        text.Append("\n## 잰 조각 (처음 경우, 이름 (모양 수))\n").Append(pieces ?? "(없음)\n");
        string body = text.ToString();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "report.txt"), body, new UTF8Encoding(false));
        // The console gets the summary; the whole list is in the file.
        int cut = body.IndexOf("\n## 경우마다", StringComparison.Ordinal);
        return cut > 0 ? body.Substring(0, cut) : body;
    }

    // ---- The Game view's size ----

    // PlayModeWindow (Unity 2022.2+) sets the Game view's resolution; found by reflection so another Unity version only
    // loses the screen shapes, never the editor's compile.
    private static bool SetResolution(int width, int height)
    {
        var type = Type.GetType("UnityEditor.PlayModeWindow, UnityEditor");
        var set = type?.GetMethod("SetCustomRenderingResolution", BindingFlags.Public | BindingFlags.Static);
        if (set == null) return false;
        var parameters = set.GetParameters();
        if (parameters.Length != 3) return false;
        try
        {
            set.Invoke(null, new[]
            {
                Convert.ChangeType(width, parameters[0].ParameterType),
                Convert.ChangeType(height, parameters[1].ParameterType),
                (object)Title,
            });
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[{Title}] 게임 화면 크기를 {width}×{height}로 바꾸지 못했습니다: {exception.GetBaseException().Message}");
            return false;
        }
    }

    private static EditorWindow GameView()
    {
        var type = Type.GetType("UnityEditor.GameView, UnityEditor");
        if (type == null) return null;
        var views = Resources.FindObjectsOfTypeAll(type);
        return views.Length > 0 ? views[0] as EditorWindow : null;
    }

    private static PropertyInfo SizeProperty(EditorWindow view)
        => view?.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static int SelectedSize()
    {
        try
        {
            var view = GameView();
            var property = SizeProperty(view);
            return property != null ? (int)property.GetValue(view) : -1;
        }
        catch (Exception) { return -1; }
    }

    // Back to the size the player had picked in the Game view.
    private static void RestoreSize()
    {
        try
        {
            var view = GameView();
            var property = SizeProperty(view);
            if (property == null || sizeIndex < 0 || !property.CanWrite) return;
            property.SetValue(view, sizeIndex);
            view.Repaint();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[{Title}] 게임 화면 크기를 되돌리지 못했습니다(게임 화면 위 크기 목록에서 다시 골라 주세요): {exception.GetBaseException().Message}");
        }
    }
}
