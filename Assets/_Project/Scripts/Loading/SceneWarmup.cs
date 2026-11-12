using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// The work a scene needs before anyone plays it, done behind the loading screen after every scene change (SceneLoad's,
/// Netcode's in a room, and the first scene of a run), each step told on the bar, then the screen lifts and
/// <see cref="Ready"/> goes out. In a game scene: the local player and camera come, the combat pools are made
/// (CombatWarmup: impacts, holes, blood, hit markers, damage numbers, sounds, the post-processing mixes), everything a fight
/// draws first is drawn once in front of the camera (CombatRehearsal: particles, every game shader, gore, zombie looks
/// and their ragdoll dolls, throwables, gun models), then it waits for what that set going: the editor's shader compiles
/// (ShaderUtil.anythingCompiling), a build's recorded pipeline states (PsoWarmup), the fonts' glyphs (FontWarmup). On the
/// title screen, at the start of a run: the fonts' glyphs (with a bigger share of each frame while the screen is up),
/// the character cards' pictures (SkinPortraits) and the shaders they set compiling.
/// The 18:53 run of 2026-11-12 (Logs/hitches.txt) is what this answers: 11.4 s frozen loading the hub, 9.8 s of shader
/// compiling on the chapter's first frame, 7.5 s coming back, 0.3 to 0.8 s each for the title's first card pictures and
/// 40 to 150 ms whenever a glyph first showed.
/// In the editor each load is written to Logs/loads.txt: how long each step took and what the scene's roots hold, to
/// find what builds the most at load. Numbers: Tables/Loading.csv, Warmup and Boot rows.
/// </summary>
[DisallowMultipleComponent]
public sealed class SceneWarmup : MonoBehaviour
{
    public const string TableName = "Loading", WarmupRow = "Warmup", BootRow = "Boot";

    [Header("Warmup")]
    [Tooltip("Seconds to wait for the local player and camera before warming without them.")]
    public float playerWait = 15f;
    [Tooltip("Milliseconds of a frame the rehearsal may spend putting things in front of the camera.")]
    public float budgetMs = 12f;
    [Tooltip("How far in front of the camera the rehearsal stands (m).")]
    public float distance = 3f;
    [Tooltip("Width of one rehearsal cell (m).")]
    public float cell = .3f;
    [Tooltip("Cells in a rehearsal row.")]
    public int columns = 10;
    [Tooltip("Frames everything stays drawn once it all stands, shadows included.")]
    public int renderFrames = 4;
    [Tooltip("Ragdoll dolls made ahead for each zombie rig, one a frame while everything stays drawn (ZombieCorpse keeps 6 at most at first).")]
    public int dolls = 6;
    [Tooltip("Most seconds to wait for the editor's shader compiles before lifting anyway.")]
    public float shaderWait = 45f;
    [Tooltip("Pipeline states a build makes per frame from its recording (PsoWarmup).")]
    public int psoPerFrame = 64;
    [Tooltip("Most seconds to spend on a build's recorded pipeline states.")]
    public float psoWait = 20f;
    [Tooltip("Most seconds to wait for the fonts' glyphs (FontWarmup) in a game scene.")]
    public float fontWait = 10f;

    [Header("Boot")]
    [Tooltip("The title screen at the start of a run waits behind the loading screen for glyphs, card pictures and shaders.")]
    public bool bootWait = true;
    [Tooltip("Most seconds the title screen waits for the character cards' pictures.")]
    public float portraitWait = 8f;
    [Tooltip("Most seconds the title screen waits for the fonts' glyphs.")]
    public float bootFontWait = 15f;
    [Tooltip("Milliseconds a frame FontWarmup may take while the screen is up (its own Fonts.csv number comes back after).")]
    public float bootFontBudgetMs = 40f;
    [Tooltip("Characters FontWarmup draws per call while the screen is up: each call reads the font's layout tables once.")]
    public int bootFontChunk = 64;

    // The bar from where reading the scene left it: waking, the player, the pools, the rehearsal, holding, waiting.
    private const float Woken = .5f, Standing = .55f, Pooled = .6f, Built = .8f, Held = .85f, Compiled = .95f;

    private static SceneWarmup instance;
    private static readonly List<Action> waiting = new List<Action>();
    private Coroutine running;
    private CombatRehearsal rehearsal;
    private NetworkSceneManager hooked;
    private AsyncOperation networkLoad;
    private FontWarmup raisedFonts;
    private float wokeAt, raisedBudget;
    private int raisedChunk;
    private bool firstRun = true;

    /// <summary>
    /// The scene (its name) is loaded and warmed and the loading screen is lifting: what should play over it can start
    /// (the hub's results show, ResultsShow). Raised once per scene change.
    /// </summary>
    public static event Action<string> Ready;

    /// <summary>The current scene has been warmed and the loading screen is lifting or gone.</summary>
    public static bool IsReady { get; private set; }

    /// <summary>Runs <paramref name="then"/> now if the scene is ready, else once it is (once).</summary>
    public static void WhenReady(Action then)
    {
        if (then == null) return;
        if (IsReady) then();
        else waiting.Add(then);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        waiting.Clear();
        Ready = null;
        IsReady = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || instance != null) return;
        var host = new GameObject("Scene Warmup");
        DontDestroyOnLoad(host);
        host.AddComponent<SceneWarmup>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        GameTables.Bind(TableName, WarmupRow, this);
        GameTables.Bind(TableName, BootRow, this);
        SceneManager.sceneLoaded += OnSceneLoaded;
        // The hub's results show waits behind its curtain while the screen is up or the warm-up still runs (whatever else
        // holds it back still does).
        var heldBefore = ResultsShow.HoldBack;
        ResultsShow.HoldBack = () => !IsReady || LoadingScreen.Visible || (heldBefore != null && heldBefore());
#if UNITY_EDITOR
        Timeline.Begin();
#endif
        // The first scene of the run has woken already: the screen covers it from its first drawn frame.
        Begin(SceneManager.GetActiveScene(), true);
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (hooked != null) hooked.OnSceneEvent -= OnNetworkScene;
        hooked = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        Begin(scene, !LoadingScreen.Visible);
    }

    private void Begin(Scene scene, bool instant)
    {
        if (running != null) StopCoroutine(running);
        if (rehearsal != null) Destroy(rehearsal.gameObject);
        LowerFonts();
        IsReady = false;
        wokeAt = Time.realtimeSinceStartup;
        LoadingScreen.Show(scene.name, instant);
        LoadingScreen.Report(SceneLoad.ReadShare, "loading.stage.wake");
        bool title = scene.name == MainMenuScreen.SceneName;
        running = StartCoroutine(title ? Title(scene.name, firstRun) : Game(scene.name));
        firstRun = false;
    }

    // ---- A room's scene changes (Netcode loads them itself) ----

    private void Update()
    {
        var network = NetworkManager.Singleton;
        var scenes = network != null && network.IsListening ? network.SceneManager : null;
        if (scenes != hooked)
        {
            if (hooked != null) hooked.OnSceneEvent -= OnNetworkScene;
            hooked = scenes;
            if (hooked != null) hooked.OnSceneEvent += OnNetworkScene;
        }
        if (networkLoad == null) return;
        if (networkLoad.isDone) networkLoad = null;
        else LoadingScreen.Report(SceneLoad.ReadShare * Mathf.Clamp01(networkLoad.progress / .9f), "loading.stage.scene");
    }

    // This PC starts loading the room's next scene: the screen comes up over the old one while Netcode reads it.
    private void OnNetworkScene(SceneEvent change)
    {
        var network = NetworkManager.Singleton;
        if (network == null || change == null || change.ClientId != network.LocalClientId) return;
        if (change.SceneEventType != SceneEventType.Load || change.LoadSceneMode != LoadSceneMode.Single) return;
        IsReady = false;
        LoadingScreen.Show(change.SceneName);
        LoadingScreen.Report(0f, "loading.stage.scene");
        networkLoad = change.AsyncOperation;
    }

    // ---- The steps ----

    private IEnumerator Game(string scene)
    {
        var steps = new Steps(scene);
        // The scene's objects run their Start before anything is asked of them.
        yield return null;
        steps.Mark("woke", wokeAt);
        LoadingScreen.Report(Woken, "loading.stage.player");

        Camera eye = null;
        for (float waited = 0f; waited < playerWait; waited += Time.unscaledDeltaTime)
        {
            var local = PlayerRoster.Local;
            eye = Camera.main;
            if (local != null && local.IsInitialized && eye != null) break;
            yield return null;
        }
        eye = Camera.main;
        steps.Mark("player");
        LoadingScreen.Report(Standing, "loading.stage.pools");

        // Each makes what it makes once a run; these are the ones not already made as the player spawned.
        CombatWarmup.Prepare();
        BashImpact.Prepare();
        MuzzleFlame.Prepare();
        CombatFeedHUD.Prewarm();
        yield return null;
        steps.Mark("pools");
        LoadingScreen.Report(Pooled, "loading.stage.looks");

        if (eye != null)
        {
            rehearsal = CombatRehearsal.Open(eye, distance, cell, columns);
            if (rehearsal != null)
            {
                yield return rehearsal.Build(budgetMs, share => LoadingScreen.Report(Mathf.Lerp(Pooled, Built, share)));
                steps.Mark($"looks ({rehearsal.Placed})");
                // Every piece is drawn a few frames (shadows too); each frame stocks one more doll for the zombies' rigs.
                int frames = Mathf.Max(1, Mathf.Max(renderFrames, dolls));
                for (int frame = 0; frame < frames; frame++)
                {
                    if (frame < dolls)
                        foreach (var rig in rehearsal.Rigs)
                            if (rig != null) ZombieCorpse.Stock(rig);
                    LoadingScreen.Report(Mathf.Lerp(Built, Held, (frame + 1f) / frames), "loading.stage.looks");
                    yield return null;
                }
                steps.Mark("drawn");
            }
        }

        yield return Compiles(steps, Held, Compiled);
        if (PsoWarmup.Available)
        {
            LoadingScreen.Report(Held, "loading.stage.pso");
            for (float waited = 0f; !PsoWarmup.Step(psoPerFrame) && waited < psoWait; waited += Time.unscaledDeltaTime)
            {
                LoadingScreen.Report(Mathf.Lerp(Held, Compiled, PsoWarmup.Share));
                yield return null;
            }
            steps.Mark("pso");
        }
        if (rehearsal != null) Destroy(rehearsal.gameObject);
        rehearsal = null;

        LoadingScreen.Report(Compiled, "loading.stage.fonts");
        for (float waited = 0f; !FontWarmup.Done && waited < fontWait; waited += Time.unscaledDeltaTime) yield return null;
        steps.Mark("fonts");
        Finish(scene, steps);
    }

    // The title screen. At the start of a run its glyphs, card pictures and shaders are made behind the screen; coming back
    // to it later finds them all made, so it only waits for the shaders.
    private IEnumerator Title(string scene, bool boot)
    {
        var steps = new Steps(scene);
        yield return null;
        steps.Mark("woke", wokeAt);
        if (boot && bootWait)
        {
            LoadingScreen.Report(Woken, "loading.stage.fonts");
            RaiseFonts();
            for (float waited = 0f; !FontWarmup.Done && waited < bootFontWait; waited += Time.unscaledDeltaTime) yield return null;
            LowerFonts();
            steps.Mark("fonts");

            LoadingScreen.Report(Standing, "loading.stage.portraits");
            // Only while the title screen takes them (a SkinPortraits is there); a run started in a level takes them later.
            var profile = FindFirstObjectByType<SkinPortraits>() != null ? PlayerBodyProfile.Load() : null;
            for (float waited = 0f; waited < portraitWait && !PortraitsTaken(profile); waited += Time.unscaledDeltaTime)
            {
                LoadingScreen.Report(Mathf.Lerp(Standing, Built, waited / Mathf.Max(.01f, portraitWait)));
                yield return null;
            }
            steps.Mark("portraits");
        }
        yield return Compiles(steps, Built, Compiled);
        Finish(scene, steps);
    }

    // FontWarmup's own pace (a call a frame within 2 ms, Fonts.csv) suits a menu in use; behind the screen it may take
    // a bigger share of each frame and more characters a call, then gets its own numbers back.
    private void RaiseFonts()
    {
        raisedFonts = FindFirstObjectByType<FontWarmup>(FindObjectsInactive.Include);
        if (raisedFonts == null) return;
        raisedBudget = raisedFonts.budgetMs;
        raisedChunk = raisedFonts.chunk;
        raisedFonts.budgetMs = Mathf.Max(raisedBudget, bootFontBudgetMs);
        raisedFonts.chunk = Mathf.Max(raisedChunk, bootFontChunk);
    }

    private void LowerFonts()
    {
        if (raisedFonts == null) return;
        raisedFonts.budgetMs = raisedBudget;
        raisedFonts.chunk = raisedChunk;
        raisedFonts = null;
    }

    // Every skin and colour set of the character screen has its card picture.
    private static bool PortraitsTaken(PlayerBodyProfile profile)
    {
        if (profile == null) return true;
        foreach (var skin in profile.Skins)
        {
            if (skin == null || skin.Model == null) continue;
            if (skin.Variants.Count == 0 && SkinPortraits.Of(skin, null) == null) return false;
            foreach (var variant in skin.Variants)
                if (SkinPortraits.Of(skin, variant) == null) return false;
        }
        return true;
    }

    // The editor draws a variant still compiling as flat cyan and goes on: the screen stays up until none is left, then
    // two frames more draw them compiled. A build compiles nothing here.
    private IEnumerator Compiles(Steps steps, float from, float to)
    {
#if UNITY_EDITOR
        if (!UnityEditor.ShaderUtil.anythingCompiling) yield break;
        LoadingScreen.Report(from, "loading.stage.shaders");
        float waited = 0f;
        for (; UnityEditor.ShaderUtil.anythingCompiling && waited < shaderWait; waited += Time.unscaledDeltaTime)
        {
            // Unknown how many are left: the bar creeps toward the step's end.
            LoadingScreen.Report(Mathf.Lerp(from, to, 1f - Mathf.Exp(-waited / 6f)));
            yield return null;
        }
        yield return null;
        yield return null;
        steps.Mark("shaders");
#else
        yield break;
#endif
    }

    private void Finish(string scene, Steps steps)
    {
        running = null;
        LoadingScreen.Hide();
        IsReady = true;
        steps.Write();
        var then = waiting.ToArray();
        waiting.Clear();
        foreach (var action in then)
        {
            try { action(); }
            catch (Exception error) { Debug.LogException(error); }
        }
        try { Ready?.Invoke(scene); }
        catch (Exception error) { Debug.LogException(error); }
    }

    // ---- What each load cost (the editor's Logs/loads.txt) ----

    private sealed class Steps
    {
        private readonly string scene;
        private readonly StringBuilder line = new StringBuilder();
        private readonly float started = Time.realtimeSinceStartup;
        private float last;

        public Steps(string scene)
        {
            this.scene = scene;
            last = started;
        }

        /// <summary>This step ended now; <paramref name="since"/>: it began then, not when the last one ended.</summary>
        public void Mark(string step, float since = -1f)
        {
            float now = Time.realtimeSinceStartup, from = since >= 0f ? since : last;
            line.Append($"  {step} {(now - from) * 1000f:0} ms");
            last = now;
        }

        public void Write()
        {
#if UNITY_EDITOR
            Timeline.Write(scene, Time.realtimeSinceStartup - started, line.ToString());
#endif
        }
    }

#if UNITY_EDITOR
    private static class Timeline
    {
        private const int TopRoots = 14;
        private static string FilePath => System.IO.Path.Combine(Application.dataPath, "..", "Logs", "loads.txt");

        public static void Begin()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                System.IO.File.WriteAllText(FilePath, $"Play started {DateTime.Now:HH:mm:ss}\n");
            }
            catch (Exception error) { Debug.LogWarning("[SceneWarmup] Logs/loads.txt: " + error.Message); }
        }

        // The steps, and which roots hold the most objects and components once the scene is warm: a scene that builds
        // thousands at load shows here (each new GameObject is an Object.SetName, each AddComponent a
        // GameObject.AddComponent in Logs/hitches.txt).
        public static void Write(string scene, float seconds, string steps)
        {
            try
            {
                var text = new StringBuilder($"\n--- {scene}: {seconds:0.00} s behind the screen\n{steps}\n");
                var roots = new Dictionary<Transform, (int objects, int parts)>();
                foreach (var part in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var root = part.root;
                    roots.TryGetValue(root, out var count);
                    roots[root] = (count.objects + 1, count.parts + part.GetComponents<Component>().Length);
                }
                int objects = roots.Values.Sum(count => count.objects), parts = roots.Values.Sum(count => count.parts);
                text.Append($"  {objects} objects, {parts} components in {roots.Count} roots. Most:\n");
                foreach (var pair in roots.OrderByDescending(pair => pair.Value.parts).Take(TopRoots))
                    text.Append($"{pair.Value.objects,8}{pair.Value.parts,8}  {pair.Key.name}{(pair.Key.gameObject.scene.name == "DontDestroyOnLoad" ? " (kept)" : "")}\n");
                System.IO.File.AppendAllText(FilePath, text.ToString());
            }
            catch (Exception error) { Debug.LogWarning("[SceneWarmup] Logs/loads.txt: " + error.Message); }
        }
    }
#endif
}
