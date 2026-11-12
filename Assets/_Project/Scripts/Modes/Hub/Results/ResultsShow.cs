using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// A chapter's results, played in the hub (memo 37: Overwatch's play of the game, the winners of CoD Warzone standing in a
/// row, Weakest Link's titles, then KF2's one sheet).
/// 1. The lineup, as CoD Warzone shows its winners (the user, 2026-11-12: "워존 우승 하이라이트 연출처럼 게임에 참여한
///    사람들 멋있는 포즈 취하면서 한 번씩 싹 다 보여 주는 거야. 뭐 잘한 사람들은 왼쪽에다 칭호 같은 거 붙여 주고";
///    no play of the game; then "이거 영상 36초부터 나오는 것처럼", the Warzone victory video): everyone of the chapter
///    stands in a row on the stage (HubStage) as a posed copy of themselves (ResultsMannequin: their own skin, colours
///    and clothes, whether or not they are still in the room; the players' own bodies are hidden while it plays), and a
///    camera of its own cuts to each in turn, the last-ranked first and the best last. A shot opens very close on the hand
///    the player strikes their pose with (the raised fist, the V, the pointing finger, the salute: victory poses made in
///    Blender on Mayo, Tools/Blender/Poses), glides over the chest to the face, then pulls back, a little low, to the waist
///    up with the player on the right third, while their titles slam in down the left (최다 처치, 최다 치료, 구조왕 ...:
///    ResultsAwards, each with its number and its joke) and their name, class and three numbers stand at the foot, MVP
///    on the best. A light rides with the camera and another rims the player from behind. Then the whole row in their
///    poses, everyone's name under them, and the chapter's highlights counting up along the bottom.
/// 2. The sheet (ResultsSheet): every title and this player's own numbers on one page, with the class's insignia
///    climbing if the chapter took it up a rank (RankClimb, stage 6E).
/// Short and loud (the user's rule: no long slow shows): Space, Enter or a click skips the lineup, then closes the sheet.
/// Each PC plays its own from the numbers and looks it kept (MatchResults), so no PC waits for another. The player's
/// moving and fighting wait while it plays, and come back after.
/// It waits behind a black curtain until the hub is really up on this PC, then plays from its beginning (the user,
/// 2026-11-12: "그 게임 끝나고 뜨는 하이라이트 연출, 로딩하다가 다 끝나서 1초 보고 못 봄": the first frames back in the
/// hub stalled 3.4 and 4 s on shader compiles, and the show's clock ran through them). Up means: the hub's lobby runs,
/// this PC's player is in, the posed copies stand dressed and the results' camera has drawn them a few times
/// (showFrames, which compiles what it sees behind the curtain), no frame has taken longer than showSettleFrame for
/// showSettle, and nothing else holds it back (HoldBack: a loading screen or a first-use warm-up). After showWait it
/// starts whatever is missing. Once it plays, a long frame counts only showMaxStep (Step), so a hitch pauses the
/// lineup, the poses, the titles, the highlights, the sheet and the insignia instead of skipping them.
/// Real time. Numbers: Tables/Hub (show*). The console's /results replays it; /results vsign has everyone strike that
/// pose, to see one after touching it up (ArtSource/Poses/START_HERE.md).
/// </summary>
[DisallowMultipleComponent]
public sealed class ResultsShow : MonoBehaviour
{
    private const int Order = 86;
    private static ResultsShow instance;

    private sealed class Seat
    {
        public MatchResults.Entry Entry;
        public int Index;
        public Vector3 Feet;
        // Their posed copy on the stage.
        public ResultsMannequin Pose;
        // The closing row's plate under them.
        public RectTransform Plate;
        public CanvasGroup PlateGroup;
        // Their own shot: when, how long, which way round the camera goes (+1 or -1), its screen (titles down the left, name
        // at the foot), and whether they are the best.
        public float ShotAt, ShotLength, Side = 1f;
        public RectTransform Spot, Name;
        public CanvasGroup SpotGroup;
        public bool Best;
        public readonly List<RectTransform> Slams = new List<RectTransform>();
    }

    // The curtain's lift before the first shot (s), titles shown at most on a shot, and the gap between them down the left (px).
    private const float IntroSeconds = .45f, TitleGap = 108f;
    // How long a shot's camera stays on the hand while the pose is struck (the fist comes up into it), before it glides (s).
    private const float HandHold = .18f;
    private const int MostTitles = 4;

    private readonly List<Seat> seats = new List<Seat>();
    // The shots in the order they play.
    private readonly List<Seat> shots = new List<Seat>();
    // The players' own renderers, hidden while the posed copies stand for them, each with how it was.
    private readonly List<(Renderer part, bool wasOff)> hidden = new List<(Renderer, bool)>();
    private readonly HashSet<Renderer> hiddenSet = new HashSet<Renderer>();
    private readonly List<Renderer> found = new List<Renderer>();
    private Image cut;
    private readonly List<(TMP_Text label, double value, ResultsAwards.Award award)> counters = new List<(TMP_Text, double, ResultsAwards.Award)>();
    private List<ResultsAwards.Won> won;
    private HubStage stage;
    private Camera view;
    private Light key, rim;
    private RectTransform canvas, header, highlights;
    private CanvasGroup lineup;
    private Image curtain;
    private PlayerMovement mine;
    private ResultsSheet sheet;
    private TMP_Text waitCaption;
    private float age, prevAge, groupAt, showEnd, highlightsAt;
    // The wait behind the curtain (real s): when it began, when the stage was set, and since when no frame has run long.
    private float createdAt, preparedAt, smoothSince;
    // Frames the results' camera has drawn since the stage was set.
    private int framesSeen;
    private bool mapsHeld, playerMapWas, combatMapWas, prepared, showing;
    // The console's /results <pose>: the next show has everyone strike it.
    private static string forcedPose;

    /// <summary>The results are on the screen, or waiting behind their curtain (HubLobby keeps [F] and its prompts off meanwhile).</summary>
    public static bool IsPlaying => instance != null;

    /// <summary>
    /// Something else that still needs the screen first, a loading screen or a first-use warm-up: true while it does, and
    /// the results wait behind their curtain until it is false (null: nothing holds them).
    /// </summary>
    public static System.Func<bool> HoldBack;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; HoldBack = null; forcedPose = null; }

    /// <summary>
    /// This frame's step for the results' clocks (this lineup, the poses, ResultsSheet, RankClimb): real time, but never
    /// more than Tables/Hub showMaxStep, so a hitch (a scene still loading, a shader compiling) pauses the show rather than
    /// carrying it past its steps.
    /// </summary>
    public static float Step() => Mathf.Min(Time.unscaledDeltaTime, Mathf.Max(.01f, HubLobby.Number("showMaxStep", .1f)));

    /// <summary>The console's /results: the last chapter's results again, here (a hub plays them on its stage, anywhere
    /// else the sheet alone). "/results vsign" has everyone strike that pose (any part of its name).</summary>
    public static string Command(string args)
    {
        if (instance != null) return "결과가 이미 나오고 있어요.";
        string wanted = (args ?? "").Trim();
        var pose = wanted.Length > 0 ? ResultsMannequin.Named(wanted) : null;
        if (wanted.Length > 0 && pose == null) return "그런 포즈가 없어요: " + wanted + ". 있는 포즈: " + ResultsMannequin.Names();
        // Before any chapter has ended: everyone here as they are now, to see the show and its poses at once.
        if (!MatchResults.Replay() && !MatchResults.Preview(Texts.Get("results.preview"))) return "결과를 보여 줄 사람이 없어요.";
        forcedPose = pose != null ? pose.name : null;
        Play();
        return pose != null ? "마지막 장의 결과를 다시 보여 줘요. 모두 " + pose.name + " 포즈로." : "마지막 장의 결과를 다시 보여 줘요.";
    }

    /// <summary>Plays the kept results (MatchResults), if any are waiting.</summary>
    public static void Play()
    {
        if (instance != null || !MatchResults.Pending || MatchResults.Entries.Count == 0) return;
        RuntimeSceneHierarchy.Group(new GameObject("Results Show").AddComponent<ResultsShow>().gameObject, RuntimeSceneHierarchy.Systems, "Hub");
    }

    private void Awake()
    {
        instance = this;
        won = ResultsAwards.Evaluate(MatchResults.Entries);
        stage = FindFirstObjectByType<HubStage>();
        createdAt = smoothSince = Time.realtimeSinceStartup;
        BuildCanvas();
        RenderPipelineManager.endCameraRendering += Drawn;
    }

    private void OnDestroy()
    {
        RenderPipelineManager.endCameraRendering -= Drawn;
        if (instance == this) instance = null;
        Release();
        if (view != null) Destroy(view.gameObject);
        if (canvas != null) Destroy(canvas.gameObject);
        if (sheet != null) Destroy(sheet.gameObject);
    }

    private void Update()
    {
        if (!showing)
        {
            Wait();
            return;
        }
        if (sheet != null)
        {
            // A press that skipped the lineup must not close the sheet the moment it opens.
            if ((Skipped() && sheet.Age > .4f) || sheet.Done) { MatchResults.Clear(); Destroy(gameObject); }
            return;
        }
        prevAge = age;
        float step = Step();
        age += step;
        // Anything a player put on since (a gun drawn, a friend's clothes arriving) stays out of the shot too.
        HideBodies();
        Pose();
        foreach (var seat in seats)
            if (seat.Pose != null) seat.Pose.Tick(step);
        if (Skipped() || age >= showEnd) OpenSheet();
    }

    private void LateUpdate()
    {
        if (!showing || view == null || stage == null || sheet != null) return;
        var now = ShotOn(age);
        if (now != null && now.Pose != null)
        {
            Shoot(now, age - now.ShotAt);
            return;
        }
        // The whole row in their poses, pushing in slowly.
        float push = Mathf.Clamp01((age - groupAt) / Mathf.Max(.5f, showEnd - groupAt));
        Vector3 from = stage.CameraPoint(seats.Count, view.aspect), to = stage.Focus;
        view.transform.position = Vector3.Lerp(from, to, .08f * push);
        view.transform.rotation = Quaternion.LookRotation(to - view.transform.position, Vector3.up);
        LightUp(to + Vector3.up * .35f, true);
    }

    // ---- Waiting behind the curtain until the hub is really up on this PC; the seats, the camera, the posed copies ----

    private void Wait()
    {
        float now = Time.realtimeSinceStartup;
        // A long frame (a hitch while the hub loads, a shader compiling) starts the quiet stretch over.
        if (Time.unscaledDeltaTime > HubLobby.Number("showSettleFrame", .1f)) smoothSince = now;
        bool late = now - createdAt >= HubLobby.Number("showWait", 20f);
        mine = PlayerRoster.Local;
        if (!prepared && (mine != null && mine.IsInitialized || late)) Prepare();
        if (prepared && stage != null) HideBodies();
        foreach (var seat in seats)
            if (seat.Pose != null) seat.Pose.Tick(0f);
        if (waitCaption != null) waitCaption.alpha = .4f + .25f * Mathf.Sin(now * 4f);
        if (late || Ready(now)) StartShow();
    }

    private bool Ready(float now)
    {
        if (!prepared) return false;
        // The hub's lobby is up (its scene active and its Start run); elsewhere (/results in a chapter) there is none to wait for.
        if (HubFlow.InHub && !HubLobby.IsActive) return false;
        if (HoldBack != null && HoldBack()) return false;
        // The camera has drawn the posed copies a few times, and no frame has run long since they stood there.
        if (view != null && framesSeen < Mathf.Max(1, Mathf.RoundToInt(HubLobby.Number("showFrames", 3f)))) return false;
        return now - Mathf.Max(smoothSince, preparedAt) >= HubLobby.Number("showSettle", .5f);
    }

    // The results' camera has drawn a frame.
    private void Drawn(ScriptableRenderContext context, Camera drawn)
    {
        if (drawn != null && drawn == view) framesSeen++;
    }

    // The hub is up: the show from its beginning, the curtain lifting off it.
    private void StartShow()
    {
        if (!prepared) Prepare();
        showing = true;
        age = prevAge = 0f;
        if (waitCaption != null) waitCaption.gameObject.SetActive(false);
        if (stage == null || seats.Count == 0)
        {
            // No stage in this hub: straight to the sheet.
            OpenSheet();
            return;
        }
        BuildLineup();
        // Over everything the lineup put up, so it lifts off all of it.
        curtain.transform.SetAsLastSibling();
        KitSound.Play(KitSound.Cue.Escape, .8f);
    }

    // Behind the curtain: the seats, the camera (drawing the stage from now on, so what it sees is compiled and uploaded
    // before the curtain lifts), the posed copies on their marks, and the players' own bodies out of sight.
    private void Prepare()
    {
        prepared = true;
        preparedAt = Time.realtimeSinceStartup;
        var ranked = ResultsAwards.Ranked(MatchResults.Entries, won);
        int count = ranked.Count;
        // The best in the middle, then out to either side by turns.
        int middle = (count - 1) / 2;
        for (int rank = 0; rank < count; rank++)
        {
            int offset = (rank + 1) / 2 * (rank % 2 == 1 ? 1 : -1);
            seats.Add(new Seat { Entry = ranked[rank], Index = Mathf.Clamp(middle + offset, 0, count - 1) });
        }
        // Guard against two in one place (a short row's rounding): seats in rank order take the next free place.
        var taken = new bool[count];
        foreach (var seat in seats)
        {
            int at = seat.Index;
            while (taken[at]) at = (at + 1) % count;
            taken[at] = true;
            seat.Index = at;
        }
        if (stage == null) return;
        foreach (var seat in seats) seat.Feet = stage.Slot(seat.Index, count);
        MakeCamera();
        MakePoses();
        HideBodies();
        HoldInput(true);
    }

    private void MakeCamera()
    {
        var host = new GameObject("Results Camera");
        view = host.AddComponent<Camera>();
        var main = Camera.main;
        // Drawn over the player's own camera (and the HUD it carries), which goes on underneath as it is.
        view.depth = (main != null ? main.depth : 0f) + 10f;
        view.fieldOfView = stage.FieldOfView;
        view.nearClipPlane = .05f;
        view.farClipPlane = 500f;
        view.clearFlags = CameraClearFlags.Skybox;
        var data = view.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = true;
        view.transform.position = stage.CameraPoint(seats.Count, view.aspect);
        view.transform.rotation = Quaternion.LookRotation(stage.Focus - view.transform.position, Vector3.up);
    }

    // Each seat's posed copy on its mark, turned to the stage's camera: the fist for the best who won a title, the others
    // by turns (or everyone the pose the console asked for).
    private void MakePoses()
    {
        var facing = Quaternion.Euler(0f, stage.Yaw, 0f);
        var forced = ResultsMannequin.Named(forcedPose);
        forcedPose = null;
        int turn = 0;
        for (int rank = 0; rank < seats.Count; rank++)
        {
            var seat = seats[rank];
            bool best = rank == 0 && ResultsAwards.Of(won, seat.Entry).Count > 0;
            var clip = forced != null ? forced : ResultsMannequin.PoseFor(best ? 0 : turn++, best);
            seat.Pose = ResultsMannequin.Make(seat.Entry.Look, seat.Feet, facing, 1f, clip);
        }
    }

    // The players' own bodies (and what they hold) stay out of sight while the posed copies stand for them.
    private void HideBodies()
    {
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null) continue;
            someone.GetComponentsInChildren(true, found);
            foreach (var part in found)
            {
                if (part == null || !hiddenSet.Add(part)) continue;
                hidden.Add((part, part.forceRenderingOff));
                part.forceRenderingOff = true;
            }
        }
        found.Clear();
    }

    private void ShowBodies()
    {
        foreach (var (part, wasOff) in hidden)
            if (part != null) part.forceRenderingOff = wasOff;
        hidden.Clear();
        hiddenSet.Clear();
    }

    private void HoldInput(bool hold)
    {
        var actions = InputSystem.actions;
        if (actions == null) return;
        var moves = actions.FindActionMap("Player", false);
        var combat = actions.FindActionMap("Combat", false);
        if (hold)
        {
            if (mapsHeld) return;
            mapsHeld = true;
            playerMapWas = moves != null && moves.enabled;
            combatMapWas = combat != null && combat.enabled;
            moves?.Disable();
            combat?.Disable();
            return;
        }
        if (!mapsHeld) return;
        mapsHeld = false;
        if (playerMapWas) moves?.Enable();
        if (combatMapWas) combat?.Enable();
    }

    // The player has the stage, the camera and the controls back.
    private void Release()
    {
        HoldInput(false);
        ClearStage();
        if (view != null) view.enabled = false;
    }

    // The posed copies and the show's lights go, and the players' own bodies show again.
    private void ClearStage()
    {
        foreach (var seat in seats)
        {
            if (seat.Pose != null) Destroy(seat.Pose.gameObject);
            seat.Pose = null;
        }
        if (key != null) Destroy(key.gameObject);
        if (rim != null) Destroy(rim.gameObject);
        key = rim = null;
        ShowBodies();
    }

    private static bool Skipped()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        return keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            || mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private void OpenSheet()
    {
        if (sheet != null) return;
        if (lineup != null) lineup.alpha = 0f;
        if (view != null) view.enabled = false;
        ClearStage();
        // The controls stay held under the sheet; they come back when it closes (OnDestroy).
        sheet = ResultsSheet.Open(won);
        if (curtain != null) curtain.color = Color.clear;
    }

    // ---- The camera and its lights ----

    // One player's shot (the Warzone victory video): very close on the hand they strike with, gliding over the chest to
    // the face, then back and a little low to the waist up, with them on the right third and room for their titles on
    // the left, drifting in until the cut.
    private void Shoot(Seat seat, float time)
    {
        var pose = seat.Pose;
        var root = pose.transform;
        Vector3 hand = root.TransformPoint(pose.Hand), chest = root.TransformPoint(pose.Chest), eyes = root.TransformPoint(pose.Eyes);
        float sweep = Mathf.Max(HandHold + .1f, HubLobby.Number("showSweep", .9f));
        float pull = Mathf.Max(.1f, HubLobby.Number("showPull", .5f));
        // Clear of a hand thrust at the camera (the pointing finger).
        float close = Mathf.Max(.2f, HubLobby.Number("showClose", .55f)) + pose.Reach * .8f;
        float far = Mathf.Max(close, HubLobby.Number("showFar", 1.7f));
        // Hand to face, on a curve bent through the chest when the hand is below it.
        float glide = Smooth((time - HandHold) / (sweep - HandHold));
        Vector3 bend = hand.y < chest.y - .05f ? chest : (hand + eyes) * .5f;
        Vector3 along = Bezier(hand, bend, eyes, glide);
        float back = Smooth((time - sweep) / pull);
        float settle = Mathf.Clamp01((time - sweep - pull) / Mathf.Max(.5f, seat.ShotLength - sweep - pull));
        float distance = Mathf.Lerp(close, far, back) * (1f - .06f * settle);
        // Round to one side and from a little below: wider while it glides, closing in as it settles.
        float yaw = seat.Side * Mathf.Lerp(Mathf.Lerp(24f, 12f, glide), Mathf.Lerp(9f, 5f, settle), back);
        float pitch = Mathf.Lerp(-10f, -7f, back) * Mathf.Deg2Rad;
        Vector3 facing = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        Vector3 level = Quaternion.AngleAxis(yaw, Vector3.up) * facing;
        Vector3 position = along + (level * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * distance;
        // Centred on what it glides over; then the face on the right third and a little high.
        float tanV = Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad), tanH = tanV * view.aspect;
        Vector3 right = Vector3.Cross(Vector3.up, -level).normalized;
        Vector3 framed = eyes - right * (distance * tanH / 3f) - Vector3.up * (distance * tanV * .35f);
        Vector3 look = Vector3.Lerp(along, framed, back);
        view.transform.position = position;
        view.transform.rotation = Quaternion.LookRotation(look - position, Vector3.up);
        LightUp(Vector3.Lerp(chest, eyes, .6f), false);
    }

    // The show's two lights on whom it frames: a soft one riding with the camera, and one rimming them from behind and
    // above (Hub.csv showLight, showRim; 0 turns one off).
    private void LightUp(Vector3 subject, bool wide)
    {
        if (key == null) key = MakeLight("Results Key", new Color(1f, .96f, .9f));
        if (rim == null) rim = MakeLight("Results Rim", new Color(.84f, .9f, 1f));
        Vector3 facing = Quaternion.Euler(0f, stage.Yaw, 0f) * Vector3.forward;
        key.transform.position = view.transform.position + Vector3.up * .25f;
        key.transform.rotation = Quaternion.LookRotation(subject - key.transform.position, Vector3.up);
        key.spotAngle = wide ? 70f : 46f;
        key.intensity = Mathf.Max(0f, HubLobby.Number("showLight", 2f));
        key.enabled = key.intensity > 0f;
        rim.transform.position = subject - facing * (wide ? 2.4f : 1.4f) + Vector3.up * (wide ? 2.4f : 1.5f);
        rim.transform.rotation = Quaternion.LookRotation(subject - rim.transform.position, Vector3.up);
        rim.spotAngle = wide ? 100f : 60f;
        rim.intensity = Mathf.Max(0f, HubLobby.Number("showRim", 3f));
        rim.enabled = rim.intensity > 0f;
    }

    private static Light MakeLight(string name, Color color)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = color;
        light.range = 10f;
        light.innerSpotAngle = 20f;
        light.shadows = LightShadows.None;
        return light;
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private static Vector3 Bezier(Vector3 from, Vector3 bend, Vector3 to, float t)
    {
        float u = 1f - t;
        return u * u * from + 2f * u * t * bend + t * t * to;
    }

    // ---- The lineup's screen ----

    private void BuildCanvas()
    {
        var host = new GameObject("Results Lineup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas = host.GetComponent<RectTransform>();
        var layer = host.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = Order;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        lineup = host.AddComponent<CanvasGroup>();
        lineup.blocksRaycasts = lineup.interactable = false;
        // Black until the hub is up, then it lifts.
        curtain = KitStyle.Fill(KitStyle.Stretch("Curtain", canvas), Color.black);
        var wait = FeelUI.Rect("Waiting", canvas);
        wait.anchorMin = wait.anchorMax = wait.pivot = new Vector2(1f, 0f);
        wait.anchoredPosition = new Vector2(-56f, 48f);
        wait.sizeDelta = new Vector2(520f, 30f);
        waitCaption = KitStyle.Flat(wait, Texts.Get("results.wait"), 18f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        waitCaption.fontStyle = FontStyles.Normal;
    }

    private void BuildLineup()
    {
        // Letterbox bars, as the escape's banner has them.
        Bar(true);
        Bar(false);
        header = KitStyle.TopLeft("Header", canvas, new Vector2(80f, 40f), new Vector2(900f, 120f));
        header.localRotation = Quaternion.Euler(0f, 0f, 2f);
        KitStyle.Flat(KitStyle.TopLeft("Caption", header, Vector2.zero, new Vector2(600f, 30f)), Texts.Get("results.caption"), 20f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        KitStyle.Flat(KitStyle.TopLeft("Chapter", header, new Vector2(0f, 30f), new Vector2(900f, 64f)), MatchResults.Title, 52f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        KitStyle.Flat(KitStyle.TopLeft("Time", header, new Vector2(4f, 94f), new Vector2(600f, 26f)),
            Texts.Format("results.time", Clock(MatchResults.Seconds), Escaped()), 18f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        var skip = FeelUI.Rect("Skip", canvas);
        skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(1f, 0f);
        skip.anchoredPosition = new Vector2(-56f, 104f);
        skip.sizeDelta = new Vector2(360f, 30f);
        KitStyle.Flat(skip, Texts.Get("results.skip"), 17f, TextAlignmentOptions.MidlineRight, KitStyle.Soft).fontStyle = FontStyles.Normal;

        // Each player's shot in turn, the last-ranked first and the best last; alone, one longer shot.
        float shot = Mathf.Max(.6f, HubLobby.Number("showShot", 2.4f)) * (seats.Count == 1 ? 1.4f : 1f);
        float at = IntroSeconds;
        for (int i = seats.Count - 1; i >= 0; i--)
        {
            var seat = seats[i];
            seat.ShotAt = at;
            seat.ShotLength = shot;
            seat.Side = shots.Count % 2 == 0 ? 1f : -1f;
            seat.Best = i == 0 && ResultsAwards.Of(won, seat.Entry).Count > 0;
            BuildSpot(seat);
            shots.Add(seat);
            at += shot;
        }
        // Then the whole row together, everyone's name under them and the chapter's highlights along the foot (not alone:
        // the one shot was all of them, and its highlights ride it once its camera has settled).
        groupAt = at;
        bool row = seats.Count > 1;
        showEnd = row ? groupAt + Mathf.Max(.3f, HubLobby.Number("showHold", 1.6f)) : groupAt;
        highlightsAt = row ? groupAt + .1f : seats[0].ShotAt + HubLobby.Number("showTitlesAt", .9f) + .2f;
        foreach (var seat in seats) BuildPlate(seat);
        BuildHighlights();
        // The white blink of a cut, over everything but the curtain.
        cut = KitStyle.Fill(KitStyle.Stretch("Cut", canvas), Color.clear);
    }

    private void Bar(bool top)
    {
        var bar = KitStyle.Fill(FeelUI.Rect(top ? "Top bar" : "Bottom bar", canvas), KitStyle.Ink);
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rect.pivot = new Vector2(.5f, top ? 1f : 0f);
        rect.sizeDelta = new Vector2(0f, 96f);
        rect.SetAsFirstSibling();
    }

    // One player's shot on the screen: down the left their titles, at the foot their name, class and three numbers (the
    // player stands on the right third, where the shot's camera puts them).
    private void BuildSpot(Seat seat)
    {
        seat.Spot = KitStyle.Stretch("Spot " + seat.Entry.Name, canvas);
        seat.SpotGroup = seat.Spot.gameObject.AddComponent<CanvasGroup>();
        seat.SpotGroup.alpha = 0f;
        var titles = ResultsAwards.Of(won, seat.Entry);
        for (int i = 0; i < Mathf.Min(titles.Count, MostTitles); i++)
            seat.Slams.Add(BuildTitle(seat.Spot, titles[i], i));

        // The name block at the foot on the left, above the bottom bar.
        var block = KitStyle.TopLeft("Name", seat.Spot, Vector2.zero, new Vector2(820f, 150f));
        block.anchorMin = block.anchorMax = block.pivot = new Vector2(0f, 0f);
        block.anchoredPosition = new Vector2(80f, 118f);
        seat.Name = block;
        KitStyle.Bar("Seat", block, new Vector2(0f, 14f), new Vector2(8f, 92f), seat.Entry.Color);
        KitStyle.Flat(KitStyle.TopLeft("Player", block, new Vector2(24f, 0f), new Vector2(760f, 76f)), seat.Entry.Name, 66f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        string under = ClassGrowth.TitleOf(seat.Entry.ClassId) + (seat.Entry.Escaped ? "" : "  ·  " + Texts.Get("results.left"));
        KitStyle.Flat(KitStyle.TopLeft("Class", block, new Vector2(28f, 74f), new Vector2(760f, 30f)), under, 22f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        KitStyle.Flat(KitStyle.TopLeft("Numbers", block, new Vector2(28f, 108f), new Vector2(780f, 34f)), Numbers(seat.Entry), 24f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        if (!seat.Best) return;
        // The best of the room: a pink MVP chip over the name.
        var chip = KitStyle.TopLeft("MVP", block, new Vector2(30f, -34f), new Vector2(96f, 32f));
        KitSkew.On(KitStyle.Fill(chip, KitStyle.Accent), 8f);
        KitStyle.Flat(chip, Texts.Get("results.mvp"), 20f, TextAlignmentOptions.Center, KitStyle.Ink);
    }

    // A player's three numbers under their name: kills, then what they did most of the rest (damage, or healing for one who
    // healed a lot), and headshots or revives.
    private static string Numbers(MatchResults.Entry entry)
    {
        string Stat(string id, string value) => Texts.Get(id) + "  <b>" + value + "</b>";
        var parts = new List<string> { Stat("results.mine.kills", entry.Kills.ToString("N0")) };
        parts.Add(entry.Healing > entry.Damage * .5f
            ? Stat("results.mine.healing", Mathf.RoundToInt(entry.Healing).ToString("N0"))
            : Stat("results.mine.damage", Mathf.RoundToInt(entry.Damage).ToString("N0")));
        parts.Add(entry.Revives > entry.Headshots
            ? Stat("results.mine.revives", entry.Revives.ToString("N0"))
            : Stat("results.mine.headshots", entry.Headshots.ToString("N0")));
        return string.Join("   ·   ", parts);
    }

    // The name plate under a player in the closing row: their colour, name and class.
    private void BuildPlate(Seat seat)
    {
        seat.Plate = FeelUI.Rect("Plate " + seat.Entry.Name, canvas);
        seat.Plate.sizeDelta = new Vector2(230f, 52f);
        seat.PlateGroup = seat.Plate.gameObject.AddComponent<CanvasGroup>();
        seat.PlateGroup.alpha = 0f;
        var back = KitStyle.Cut(KitStyle.Stretch("Back", seat.Plate), KitStyle.With(KitStyle.Night, .9f));
        KitSkew.On(back, 10f);
        KitStyle.Bar("Seat", seat.Plate, new Vector2(10f, 8f), new Vector2(5f, 36f), seat.Entry.Color);
        KitStyle.Flat(KitStyle.TopLeft("Name", seat.Plate, new Vector2(24f, 2f), new Vector2(200f, 30f)), seat.Entry.Name, 22f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        KitStyle.Flat(KitStyle.TopLeft("Class", seat.Plate, new Vector2(24f, 28f), new Vector2(200f, 20f)),
            ClassGrowth.TitleOf(seat.Entry.ClassId) + (seat.Entry.Escaped ? "" : "  ·  " + Texts.Get("results.left")), 14f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
    }

    // A title down the left of a player's shot: a slanted pink plate with the title and its number, its joke under it.
    private RectTransform BuildTitle(RectTransform spot, ResultsAwards.Won title, int stack)
    {
        var root = KitStyle.TopLeft("Title " + stack, spot, new Vector2(80f, 250f + stack * TitleGap), new Vector2(480f, 96f));
        root.pivot = new Vector2(0f, .5f);
        root.anchoredPosition += new Vector2(0f, -48f);
        root.localRotation = Quaternion.Euler(0f, 0f, stack % 2 == 0 ? 3f : -2f);
        var plate = KitStyle.Cut(KitStyle.TopLeft("Plate", root, Vector2.zero, new Vector2(480f, 62f)), KitStyle.Accent);
        KitSkew.On(plate, 16f);
        KitStyle.Flat(KitStyle.TopLeft("Label", root, new Vector2(20f, 2f), new Vector2(300f, 58f)), title.Award.Label, 30f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ink);
        var number = KitStyle.Flat(KitStyle.TopLeft("Number", root, new Vector2(280f, 2f), new Vector2(184f, 58f)), title.Shown, 32f,
            TextAlignmentOptions.MidlineRight, KitStyle.Ink);
        number.characterSpacing = -2f;
        if (title.Award.IsMoney)
        {
            var icon = MoneyIcon.Make(root, 26f);
            icon.color = KitStyle.Ink;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 1f);
            icon.rectTransform.anchoredPosition = new Vector2(464f - number.preferredWidth - 20f, -31f);
        }
        var joke = KitStyle.Flat(KitStyle.TopLeft("Line", root, new Vector2(22f, 64f), new Vector2(450f, 28f)), title.Award.Line, 17f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        joke.fontStyle = FontStyles.Italic;
        root.localScale = Vector3.zero;
        return root;
    }

    // Along the bottom: the chapter's highlights, counting up.
    private void BuildHighlights()
    {
        highlights = FeelUI.Rect("Highlights", canvas);
        highlights.anchorMin = highlights.anchorMax = new Vector2(.5f, 0f);
        highlights.pivot = new Vector2(.5f, 0f);
        highlights.anchoredPosition = new Vector2(0f, 14f);
        highlights.sizeDelta = new Vector2(1500f, 70f);
        var shown = new List<(string id, double value, string stat)>();
        double streak = 0d, farthest = 0d, kills = 0d, money = 0d;
        foreach (var entry in MatchResults.Entries)
        {
            streak = System.Math.Max(streak, entry.BestStreak);
            farthest = System.Math.Max(farthest, entry.Farthest);
            kills += entry.Kills;
            if (entry.IsLocal) money = entry.Earned;
        }
        if (streak > 1) shown.Add(("results.high.streak", streak, "streak"));
        if (farthest > 0) shown.Add(("results.high.farthest", farthest, "farthest"));
        shown.Add((MatchResults.Entries.Count > 1 ? "results.high.kills.team" : "results.high.kills", kills, "kills"));
        shown.Add(("results.high.money", money, "money"));
        float width = 1500f / Mathf.Max(1, shown.Count);
        var awards = ResultsAwards.All();
        for (int i = 0; i < shown.Count; i++)
        {
            var cell = KitStyle.TopLeft("Highlight", highlights, new Vector2(i * width, 0f), new Vector2(width - 16f, 70f));
            KitStyle.Flat(KitStyle.TopLeft("Label", cell, Vector2.zero, new Vector2(width - 16f, 22f)), Texts.Get(shown[i].id), 15f,
                TextAlignmentOptions.Center, KitStyle.Soft).fontStyle = FontStyles.Normal;
            var value = KitStyle.Flat(KitStyle.TopLeft("Value", cell, new Vector2(0f, 20f), new Vector2(width - 16f, 48f)), "0", 36f,
                TextAlignmentOptions.Center, KitStyle.Ice);
            var award = awards.Find(a => a.Stat == shown[i].stat) ?? new ResultsAwards.Award { Stat = shown[i].stat };
            counters.Add((value, shown[i].value, award));
        }
        highlights.localScale = Vector3.zero;
    }

    // ---- Every frame of the lineup ----

    // The shot playing at the lineup's time, or null once the whole row is up.
    private Seat ShotOn(float time)
    {
        foreach (var seat in shots)
            if (time >= seat.ShotAt && time < seat.ShotAt + seat.ShotLength) return seat;
        return null;
    }

    private void Pose()
    {
        curtain.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(age / .35f));
        float headerIn = Out((age - .1f) / .3f);
        header.anchoredPosition = new Vector2(80f - 160f * (1f - headerIn), -40f);
        var now = ShotOn(age);
        // A cut: a white blink, and the player of the new shot strikes their pose.
        float since = age - (now != null ? now.ShotAt : groupAt);
        cut.color = new Color(1f, 1f, 1f, since >= 0f && since < .14f ? .5f * (1f - since / .14f) : 0f);
        foreach (var seat in shots)
        {
            bool live = seat == now;
            float local = age - seat.ShotAt;
            if (live && prevAge < seat.ShotAt && seat.Pose != null) seat.Pose.Strike();
            seat.SpotGroup.alpha = live ? 1f : 0f;
            if (!live) continue;
            // The name slides in from the left; the titles slam on down the left one after another, as the camera settles.
            float nameIn = Out(local / .22f);
            seat.Name.anchoredPosition = new Vector2(80f - 120f * (1f - nameIn), 118f);
            for (int i = 0; i < seat.Slams.Count; i++)
            {
                var slam = seat.Slams[i];
                float slamAt = seat.ShotAt + HubLobby.Number("showTitlesAt", .9f) + i * HubLobby.Number("showTitleEvery", .16f);
                float after = age - slamAt;
                if (after < 0f) { slam.localScale = Vector3.zero; continue; }
                if (prevAge < slamAt) Slammed();
                // Down from nearly twice its size, a hair under, then home, with a shake as it lands.
                float t = Mathf.Clamp01(after / .2f);
                float size = t < .6f ? Mathf.Lerp(1.8f, .95f, t / .6f) : Mathf.Lerp(.95f, 1f, (t - .6f) / .4f);
                slam.localScale = Vector3.one * size;
                float shake = after > .1f && after < .26f ? (1f - (after - .1f) / .16f) * 6f : 0f;
                var home = new Vector2(80f, -(298f + i * TitleGap));
                slam.anchoredPosition = home + (shake > 0f ? Random.insideUnitCircle * shake : Vector2.zero);
            }
        }
        // The closing row: everyone's name under them.
        bool row = now == null && age >= groupAt && seats.Count > 1;
        if (row && prevAge < groupAt) KitSound.Play(KitSound.Cue.Escape, .5f);
        foreach (var seat in seats)
        {
            float plateIn = row ? Out((age - groupAt - seat.Index * .06f) / .2f) : 0f;
            seat.PlateGroup.alpha = plateIn;
            if (!row) continue;
            Vector2 feet = OnScreen(seat.Feet);
            seat.Plate.anchoredPosition = feet + new Vector2(0f, -46f - 20f * (1f - plateIn));
        }
        float high = Out((age - highlightsAt) / .25f);
        highlights.localScale = new Vector3(1f, high, 1f);
        float count = Mathf.Clamp01((age - highlightsAt) / .6f);
        foreach (var (label, value, award) in counters)
            label.text = ResultsAwards.Format(award, award.Unit == "m" ? value * count : System.Math.Round(value * count));
    }

    private void Slammed() => CombatCueAudio.Play(gameObject, CombatCue.ParryBreak, Random.Range(1.1f, 1.3f));

    // A point in the world on this canvas (px from its middle), through the results' camera.
    private Vector2 OnScreen(Vector3 world)
    {
        if (view == null) return Vector2.zero;
        Vector3 screen = view.WorldToScreenPoint(world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
        return local;
    }

    private static float Out(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    /// <summary>m:ss, or h:mm:ss past an hour.</summary>
    public static string Clock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return whole >= 3600 ? $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}" : $"{whole / 60}:{whole % 60:00}";
    }

    /// <summary>"3 / 4": how many made the ride of how many there were.</summary>
    public static string Escaped()
    {
        int made = 0;
        foreach (var entry in MatchResults.Entries) if (entry.Escaped) made++;
        return made + " / " + MatchResults.Entries.Count;
    }
}
