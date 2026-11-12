using System;
using System.IO;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// The pipeline states (PSOs) a build makes the first time a shader variant is drawn with a given mesh layout, blend and
/// render target. On DirectX 12 and Vulkan the driver makes each one on the frame that first needs it, a hitch the editor
/// never shows (it compiles variants instead, and SceneWarmup waits for that). CombatRehearsal makes the states of what it
/// draws; this makes every state a real game drew, from a recording (Unity 6's GraphicsStateCollection):
/// in a development build the console's /pso trace starts recording, playing records whatever is drawn, and /pso save (or
/// quitting) writes GraphicsStates/&lt;API&gt;.graphicsstate under the save folder (Application.persistentDataPath, added to
/// the earlier recording there). That file copied into Assets/StreamingAssets/GraphicsStates ships with the next build,
/// which warms all of it behind the loading screen, a few states a frame on a job (SceneWarmup, Loading.csv
/// Warmup.psoPerFrame). One file per graphics API, as Unity asks: a file for another API is left alone. The editor
/// neither records nor warms.
/// </summary>
public static class PsoWarmup
{
    private const string Folder = "GraphicsStates", Extension = ".graphicsstate";
    private static GraphicsStateCollection shipped, recording;
    private static JobHandle warming;
    private static bool looked, quitHooked;

    /// <summary>This graphics API's file name (Direct3D12.graphicsstate ...).</summary>
    public static string FileName => SystemInfo.graphicsDeviceType + Extension;

    private static string ShippedPath => Path.Combine(Application.streamingAssetsPath, Folder, FileName);
    private static string RecordedPath => Path.Combine(Application.persistentDataPath, Folder, FileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        shipped = recording = null;
        warming = default;
        looked = quitHooked = false;
    }

    /// <summary>This build ships recorded states for its graphics API (never in the editor).</summary>
    public static bool Available
    {
        get
        {
            Load();
            return shipped != null;
        }
    }

    /// <summary>Share of the shipped states made so far (1 when there are none).</summary>
    public static float Share
    {
        get
        {
            if (shipped == null) return 1f;
            float total = shipped.totalGraphicsStateCount, done = shipped.completedWarmupCount;
            return total <= 0f ? 1f : Mathf.Clamp01(done / total);
        }
    }

    /// <summary>Starts making up to <paramref name="count"/> more states on a job; true once all are made (or there are none).</summary>
    public static bool Step(int count)
    {
        Load();
        if (shipped == null) return true;
        if (!warming.IsCompleted) return false;
        warming.Complete();
        if (shipped.isWarmedUp) return true;
        warming = shipped.WarmUpProgressively(Mathf.Max(1, count), default(JobHandle));
        return false;
    }

    private static void Load()
    {
        if (looked) return;
        looked = true;
        if (Application.isEditor) return;
        string path = ShippedPath;
        if (!File.Exists(path)) return;
        try
        {
            var collection = new GraphicsStateCollection();
            if (!collection.LoadFromFile(path)) return;
            if (collection.graphicsDeviceType != SystemInfo.graphicsDeviceType)
            {
                Debug.LogWarning($"[PsoWarmup] {path} was recorded for {collection.graphicsDeviceType}, this runs {SystemInfo.graphicsDeviceType}: left alone.");
                return;
            }
            shipped = collection;
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    /// <summary>The console's /pso: status, trace (start recording), save (stop and write).</summary>
    public static string Command(string args)
    {
        string verb = (args ?? "").Trim().ToLowerInvariant();
        if (verb == "trace") return Trace();
        if (verb == "save") return Save();
        Load();
        string ships = shipped != null
            ? $"이 빌드의 기록: 상태 {shipped.totalGraphicsStateCount}개 중 {shipped.completedWarmupCount}개 데움"
            : Application.isEditor ? "편집기는 기록을 쓰지 않아요(셰이더는 로딩 화면이 컴파일을 기다림)" : $"이 빌드에는 기록이 없어요({ShippedPath})";
        string records = recording != null && recording.isTracing ? " · 지금 기록 중(/pso save)" : "";
        return ships + records + ". /pso trace 로 기록 시작, /pso save 로 저장(개발 빌드)";
    }

    private static string Trace()
    {
        if (Application.isEditor) return "편집기에서는 기록하지 않아요. 개발 빌드(Development Build)에서 /pso trace 를 입력해 주세요.";
        if (!Debug.isDebugBuild) return "기록은 개발 빌드에서만 돼요.";
        if (recording != null && recording.isTracing) return "이미 기록 중이에요. 다 돌았으면 /pso save";
        try
        {
            recording = new GraphicsStateCollection();
            // An earlier recording is kept: this one adds what it meets to it.
            if (File.Exists(RecordedPath)) recording.LoadFromFile(RecordedPath);
            if (!recording.BeginTrace())
            {
                recording = null;
                return "기록을 시작하지 못했어요.";
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            recording = null;
            return "기록을 시작하지 못했어요: " + error.Message;
        }
        if (!quitHooked)
        {
            quitHooked = true;
            Application.quitting += SaveOnQuit;
        }
        return "그래픽 상태 기록 시작. 허브·챕터를 돌며 쏘고 잡고 던져 본 뒤 /pso save (게임을 끄면 저장됨)";
    }

    private static string Save()
    {
        if (recording == null) return "기록 중이 아니에요. /pso trace 로 시작해 주세요.";
        try
        {
            if (recording.isTracing) recording.EndTrace();
            Directory.CreateDirectory(Path.GetDirectoryName(RecordedPath));
            bool saved = recording.SaveToFile(RecordedPath);
            var states = recording.totalGraphicsStateCount;
            recording = null;
            return saved
                ? $"상태 {states}개를 {RecordedPath} 에 저장했어요. 이 파일을 프로젝트의 Assets/StreamingAssets/{Folder}/ 에 넣고 빌드하면 로딩 화면에서 미리 만들어요."
                : "저장하지 못했어요.";
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            recording = null;
            return "저장하지 못했어요: " + error.Message;
        }
    }

    private static void SaveOnQuit()
    {
        if (recording != null && recording.isTracing) Debug.Log("[PsoWarmup] " + Save());
    }
}
