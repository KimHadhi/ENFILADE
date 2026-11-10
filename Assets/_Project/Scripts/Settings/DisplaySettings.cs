using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puts the game window on the chosen monitor at the chosen resolution and window mode (<see cref="GameSettings"/>
/// monitorName/monitorIndex, resolutionWidth/Height, windowMode) before the first scene and whenever they change.
/// A monitor change moves the window first and sets the resolution once the move is done, since each monitor offers its
/// own resolutions. Builds only: in the editor the Game view keeps its own size, so the choices are only saved.
/// </summary>
public static class DisplaySettings
{
    private const int SmallestWidth = 1024;
    private static readonly List<DisplayInfo> layout = new List<DisplayInfo>();
    private static bool moving;
    private static (string name, int index, int width, int height, WindowMode mode) applied;

    /// <summary>Raised after the window has moved to another monitor, whose resolutions the menu then lists.</summary>
    public static event Action Moved;

    /// <summary>True where the choices take effect; false in the editor.</summary>
    public static bool Applies => !Application.isEditor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Moved = null;
        moving = false;
        applied = default;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
        GameSettings.Changed += Apply;
        Apply();
    }

    /// <summary>The monitors Windows reports, in its order.</summary>
    public static IReadOnlyList<DisplayInfo> Monitors()
    {
        layout.Clear();
        try { Screen.GetDisplayLayout(layout); }
        catch (Exception) { layout.Clear(); }
        return layout;
    }

    /// <summary>The place in <see cref="Monitors"/> of the saved monitor, or of the one the window is on.</summary>
    public static int ChosenMonitor()
    {
        var monitors = Monitors();
        int saved = Find(GameSettings.Current, monitors);
        if (saved >= 0) return saved;
        var current = Screen.mainWindowDisplayInfo;
        for (int i = 0; i < monitors.Count; i++)
            if (monitors[i].Equals(current)) return i;
        return 0;
    }

    public static string MonitorLabel(DisplayInfo monitor, int index)
    {
        string name = string.IsNullOrWhiteSpace(monitor.name) ? Texts.Get("settings.screen.monitor.unnamed") : monitor.name.Trim();
        return $"{index + 1}. {name} ({monitor.width} × {monitor.height})";
    }

    /// <summary>The resolutions the window's monitor offers, largest first, one per size.</summary>
    public static List<Vector2Int> Resolutions()
    {
        var sizes = new List<Vector2Int>();
        foreach (var resolution in Screen.resolutions)
        {
            var size = new Vector2Int(resolution.width, resolution.height);
            if (size.x >= SmallestWidth && !sizes.Contains(size)) sizes.Add(size);
        }
        var native = Native();
        if (native.x > 0 && !sizes.Contains(native)) sizes.Add(native);
        sizes.Sort((a, b) =>
        {
            int byArea = (b.x * b.y).CompareTo(a.x * a.y);
            return byArea != 0 ? byArea : b.x.CompareTo(a.x);
        });
        return sizes;
    }

    /// <summary>The monitor's own resolution, which a saved 0 x 0 stands for.</summary>
    public static Vector2Int Native()
    {
        var current = Screen.mainWindowDisplayInfo;
        return current.width > 0 ? new Vector2Int(current.width, current.height) : new Vector2Int(Screen.currentResolution.width,
            Screen.currentResolution.height);
    }

    /// <summary>"2560 × 1440 (QHD)": the size with the name people know it by, where it has one.</summary>
    public static string ResolutionLabel(Vector2Int size)
    {
        string known = KnownName(size);
        return known != null ? $"{size.x} × {size.y} ({known})" : $"{size.x} × {size.y}";
    }

    private static string KnownName(Vector2Int size)
    {
        switch ((size.x, size.y))
        {
            case (1280, 720): case (1366, 768): return "HD";
            case (1600, 900): return "HD+";
            case (1920, 1080): return "FHD";
            case (1920, 1200): return "WUXGA";
            case (2560, 1080): return Texts.Get("settings.screen.resolution.ultrawide_fhd");
            case (2560, 1440): return "QHD";
            case (2560, 1600): return "WQXGA";
            case (3440, 1440): return Texts.Get("settings.screen.resolution.ultrawide_qhd");
            case (3840, 1600): return Texts.Get("settings.screen.resolution.ultrawide_4k");
            case (3200, 1800): return "QHD+";
            case (3840, 2160): return "4K UHD";
            case (5120, 1440): return Texts.Get("settings.screen.resolution.dual_qhd");
            case (5120, 2880): return "5K";
            default: return null;
        }
    }

    // Only what this class owns: other settings changing (a volume slider) must not reset the window.
    private static void Apply()
    {
        if (!Applies || moving) return;
        var s = GameSettings.Current;
        var wanted = (s.monitorName, s.monitorIndex, s.resolutionWidth, s.resolutionHeight, s.windowMode);
        if (wanted == applied) return;
        applied = wanted;

        var monitors = Monitors();
        int target = Find(s, monitors);
        if (target >= 0 && !monitors[target].Equals(Screen.mainWindowDisplayInfo))
        {
            MoveTo(monitors[target], s);
            return;
        }
        SetMode(s);
    }

    private static void MoveTo(DisplayInfo monitor, GameSettings s)
    {
        Vector2Int size = Size(s, new Vector2Int(monitor.width, monitor.height));
        // A window lands centred; a full screen fills the monitor from its corner.
        var corner = s.windowMode == WindowMode.Windowed
            ? new Vector2Int(Mathf.Max(0, (monitor.width - size.x) / 2), Mathf.Max(0, (monitor.height - size.y) / 2))
            : Vector2Int.zero;
        moving = true;
        AsyncOperation move;
        try { move = Screen.MoveMainWindowTo(monitor, corner); }
        catch (Exception e)
        {
            moving = false;
            Debug.LogWarning($"[Display] Could not move the window to {monitor.name}: {e.Message}");
            SetMode(s);
            return;
        }
        move.completed += _ =>
        {
            moving = false;
            SetMode(GameSettings.Current);
            Moved?.Invoke();
            Apply(); // only acts on a choice made while the window was moving
        };
    }

    private static void SetMode(GameSettings s)
    {
        Vector2Int size = Size(s, Native());
        FullScreenMode mode = ToFullScreenMode(s.windowMode);
        if (Screen.width == size.x && Screen.height == size.y && Screen.fullScreenMode == mode) return;
        Screen.SetResolution(size.x, size.y, mode);
    }

    private static Vector2Int Size(GameSettings s, Vector2Int native) =>
        s.resolutionWidth > 0 && s.resolutionHeight > 0 ? new Vector2Int(s.resolutionWidth, s.resolutionHeight) : native;

    public static FullScreenMode ToFullScreenMode(WindowMode mode) => mode switch
    {
        WindowMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
        WindowMode.Windowed => FullScreenMode.Windowed,
        _ => FullScreenMode.FullScreenWindow,
    };

    // By place and name first, so two monitors of one model stay apart; then by name, in case the order changed.
    private static int Find(GameSettings s, IReadOnlyList<DisplayInfo> monitors)
    {
        if (string.IsNullOrEmpty(s.monitorName)) return -1;
        if (s.monitorIndex >= 0 && s.monitorIndex < monitors.Count && monitors[s.monitorIndex].name == s.monitorName)
            return s.monitorIndex;
        for (int i = 0; i < monitors.Count; i++)
            if (monitors[i].name == s.monitorName) return i;
        return -1;
    }
}
