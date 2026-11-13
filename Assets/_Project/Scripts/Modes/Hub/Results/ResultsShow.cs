using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
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
public sealed partial class ResultsShow : MonoBehaviour
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
    private bool prepared, showing;
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
}
