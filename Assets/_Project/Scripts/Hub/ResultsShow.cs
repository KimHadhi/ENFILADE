using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// A chapter's results, played in the hub over the players themselves (memo 37: Overwatch's play of the game, the
/// winners of CoD Warzone standing in a row, Weakest Link's titles, then KF2's one sheet).
/// 1. The lineup, as CoD Warzone shows its winners (the user, 2026-11-12: "워존 우승 하이라이트 연출처럼 게임에 참여한
///    사람들 멋있는 포즈 취하면서 한 번씩 싹 다 보여 주는 거야. 뭐 잘한 사람들은 왼쪽에다 칭호 같은 거 붙여 주고";
///    no play of the game): back in the hub everyone stands in a row on the stage (HubStage) in their own skin and
///    clothes, and a camera of its own cuts to each in turn, the last-ranked first and the best last: close and a little
///    low, the player on the right third striking their pose, their titles slamming in down the left (최다 처치, 최다
///    치료, 구조왕, 가장 많이 쓰러짐 ...: ResultsAwards, each with its number and its joke), their name, class and three
///    numbers at the foot, MVP on the best. Then the whole row with everyone's name under them, and the chapter's
///    highlights counting up along the bottom (longest streak, farthest headshot, the team's kills, the money made).
/// 2. The sheet (ResultsSheet): every title and this player's own numbers on one page, with the class's insignia
///    climbing if the chapter took it up a rank (RankClimb, stage 6E).
/// Short and loud (the user's rule: no long slow shows): Space, Enter or a click skips the lineup, then closes the sheet.
/// Each PC plays its own from the numbers it kept (MatchResults) and puts only its own player on the stage; the others
/// are put there by their own PCs. The player's moving and fighting wait while it plays, and come back after.
/// It waits behind a black curtain until the hub is really up on this PC, then plays from its beginning (the user,
/// 2026-11-12: "그 게임 끝나고 뜨는 하이라이트 연출, 로딩하다가 다 끝나서 1초 보고 못 봄": the first frames back in the
/// hub stalled 3.4 and 4 s on shader compiles, and the show's clock ran through them). Up means: the hub's lobby runs,
/// this PC's player stands on its mark, every friend of the lineup who is in the room stands dressed on theirs (or has
/// had showWaitOthers to get there: a slow PC, or someone who left), the results' camera has drawn them a few times
/// (showFrames, which compiles what it sees behind the curtain), no frame has taken longer than showSettleFrame for
/// showSettle, and nothing else holds it back (HoldBack: a loading screen or a first-use warm-up). After showWait it
/// starts whatever is missing. Each PC starts its own show when it is ready; a fast PC waits for a slow friend's body
/// only that short while, and no PC waits for another's show. Once it plays, a long frame counts only showMaxStep
/// (Step), so a hitch pauses the lineup, the titles, the highlights, the sheet and the insignia instead of skipping them.
/// Real time. Numbers: Tables/Hub (show*).
/// </summary>
// Before the look (FrameOrder.Look): the turn to the camera is asked of it the frame it is taken (CombatHeadMotion.Turn).
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Look - 1)]
public sealed class ResultsShow : MonoBehaviour
{
    private const int Order = 86;
    private static ResultsShow instance;

    private sealed class Seat
    {
        public MatchResults.Entry Entry;
        public int Index;
        public Vector3 Feet;
        // The closing row's plate under them.
        public RectTransform Plate;
        public CanvasGroup PlateGroup;
        // Their own shot: when, how long, its screen (titles down the left, name at the foot), and whether they are the best.
        public float ShotAt, ShotLength;
        public RectTransform Spot, Name;
        public CanvasGroup SpotGroup;
        public bool Best;
        public readonly List<RectTransform> Slams = new List<RectTransform>();
    }

    // The curtain's lift before the first shot (s), titles shown at most on a shot, and the gap between them down the left (px).
    private const float IntroSeconds = .45f, TitleGap = 108f;
    private const int MostTitles = 4;

    private readonly List<Seat> seats = new List<Seat>();
    // The shots in the order they play.
    private readonly List<Seat> shots = new List<Seat>();
    private Image cut;
    private readonly List<(TMP_Text label, double value, ResultsAwards.Award award)> counters = new List<(TMP_Text, double, ResultsAwards.Award)>();
    private List<ResultsAwards.Won> won;
    private HubStage stage;
    private Camera view;
    private RectTransform canvas, header, highlights;
    private CanvasGroup lineup;
    private Image curtain;
    private PlayerMovement mine;
    private PlayerBody body;
    private ResultsSheet sheet;
    private TMP_Text waitCaption;
    private float age, prevAge, groupAt, showEnd, highlightsAt;
    // The wait behind the curtain (real s): when it began, when the seats were set, since when no frame has run long,
    // and since when everyone of the lineup who is here has stood on their mark (negative: not yet).
    private float createdAt, preparedAt, smoothSince, inPlaceAt = -1f;
    // Frames the results' camera has drawn since everyone stood on their mark.
    private int framesSeen;
    private bool placed, mapsHeld, playerMapWas, combatMapWas, prepared, showing;

    /// <summary>The results are on the screen, or waiting behind their curtain (HubLobby keeps [F] and its prompts off meanwhile).</summary>
    public static bool IsPlaying => instance != null;

    /// <summary>
    /// Something else that still needs the screen first, a loading screen or a first-use warm-up: true while it does, and
    /// the results wait behind their curtain until it is false (null: nothing holds them).
    /// </summary>
    public static System.Func<bool> HoldBack;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; HoldBack = null; }

    /// <summary>
    /// This frame's step for the results' clocks (this lineup, ResultsSheet, RankClimb): real time, but never more than
    /// Tables/Hub showMaxStep, so a hitch (a scene still loading, a shader compiling) pauses the show rather than
    /// carrying it past its steps.
    /// </summary>
    public static float Step() => Mathf.Min(Time.unscaledDeltaTime, Mathf.Max(.01f, HubLobby.Number("showMaxStep", .1f)));

    /// <summary>The console's /results: the last chapter's results again, here (a hub plays them on its stage, anywhere
    /// else the sheet alone).</summary>
    public static string Command(string args)
    {
        if (instance != null) return "결과가 이미 나오고 있어요.";
        if (!MatchResults.Replay()) return "아직 마친 장이 없어요. 장을 하나 끝내면 다시 볼 수 있어요.";
        Play();
        return "마지막 장의 결과를 다시 보여 줘요.";
    }

    /// <summary>Plays the kept results (MatchResults), if any are waiting.</summary>
    public static void Play()
    {
        if (instance != null || !MatchResults.Pending || MatchResults.Entries.Count == 0) return;
        new GameObject("Results Show").AddComponent<ResultsShow>();
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
        age += Step();
        Hold();
        Pose();
        if (Skipped() || age >= showEnd) OpenSheet();
    }

    private void LateUpdate()
    {
        if (!showing || view == null || stage == null || sheet != null) return;
        var now = ShotOn(age);
        if (now != null)
        {
            // Close and a little low on the shot's player, who stands on the right third; it pushes in and drifts round them.
            float t = Mathf.Clamp01((age - now.ShotAt) / now.ShotLength);
            Vector3 facing = Vector3.ProjectOnPlane(stage.transform.forward, Vector3.up).normalized;
            float swing = (now.Index % 2 == 0 ? 1f : -1f) * Mathf.Lerp(-4f, 4f, t);
            Vector3 toCamera = Quaternion.Euler(0f, swing, 0f) * facing;
            float distance = Mathf.Lerp(2.7f, 2.3f, 1f - (1f - t) * (1f - t));
            Vector3 head = now.Feet + Vector3.up * 1.4f;
            view.transform.position = now.Feet + toCamera * distance + Vector3.up * 1.15f;
            Vector3 right = Vector3.Cross(Vector3.up, -toCamera).normalized;
            Vector3 aim = head - right * .6f;
            view.transform.rotation = Quaternion.LookRotation(aim - view.transform.position, Vector3.up);
            return;
        }
        // The whole row, pushing in slowly.
        float push = Mathf.Clamp01((age - groupAt) / Mathf.Max(.5f, showEnd - groupAt));
        Vector3 from = stage.CameraPoint(seats.Count, view.aspect), to = stage.Focus;
        view.transform.position = Vector3.Lerp(from, to, .08f * push);
        view.transform.rotation = Quaternion.LookRotation(to - view.transform.position, Vector3.up);
    }

    // ---- Waiting behind the curtain until the hub is really up on this PC; the seats, the camera, the player on the stage ----

    private void Wait()
    {
        float now = Time.realtimeSinceStartup;
        // A long frame (a hitch while the hub loads, a shader compiling) starts the quiet stretch over.
        if (Time.unscaledDeltaTime > HubLobby.Number("showSettleFrame", .1f)) smoothSince = now;
        bool late = now - createdAt >= HubLobby.Number("showWait", 20f);
        mine = PlayerRoster.Local;
        if (!prepared && (mine != null && mine.IsInitialized || late)) Prepare();
        Hold();
        if (waitCaption != null) waitCaption.alpha = .4f + .25f * Mathf.Sin(now * 4f);
        if (late || Ready(now)) StartShow();
    }

    private bool Ready(float now)
    {
        if (!prepared) return false;
        // The hub's lobby is up (its scene active and its Start run); elsewhere (/results in a chapter) there is none to wait for.
        if (HubFlow.InHub && !HubLobby.IsActive) return false;
        if (HoldBack != null && HoldBack()) return false;
        // Everyone of the lineup who is in the room on their mark, dressed; a friend who is slow or gone has a while only.
        if (!OthersInPlace() && now - preparedAt < HubLobby.Number("showWaitOthers", 5f)) { inPlaceAt = -1f; return false; }
        if (inPlaceAt < 0f) { inPlaceAt = now; framesSeen = 0; }
        // The camera has drawn them all a few times, and no frame has run long since they were all there.
        if (view != null && framesSeen < Mathf.Max(1, Mathf.RoundToInt(HubLobby.Number("showFrames", 3f)))) return false;
        return now - Mathf.Max(smoothSince, inPlaceAt) >= HubLobby.Number("showSettle", .5f);
    }

    // Every friend of the lineup who is in the room stands dressed on their mark (their own PC puts them there). Alone,
    // or without a stage, there is nobody to wait for.
    private bool OthersInPlace()
    {
        if (stage == null) return true;
        foreach (var seat in seats)
        {
            if (seat.Entry.IsLocal) continue;
            PlayerMovement friend = null;
            foreach (var someone in PlayerRoster.All)
                if (someone != null && someone.IsNetworkDriven && MatchResults.SeatOf(someone) == seat.Entry.Client) { friend = someone; break; }
            if (friend == null || !friend.TryGetComponent(out PlayerBody theirs) || theirs.BodyModel == null) return false;
            Vector3 apart = friend.transform.position - seat.Feet;
            apart.y = 0f;
            if (apart.sqrMagnitude > .8f * .8f) return false;
        }
        return true;
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
    // before the curtain lifts) and this PC's player on its mark.
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
        PlaceMine();
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

    // This PC's player goes to their place on the stage and turns to the camera; the rest are put there by their own PCs.
    private void PlaceMine()
    {
        if (mine == null || !mine.IsInitialized) return;
        ulong seatId = MatchResults.SeatOf(mine);
        foreach (var seat in seats)
        {
            if (!seat.Entry.IsLocal && seat.Entry.Client != seatId) continue;
            mine.MoveTo(seat.Feet + Vector3.up * .05f);
            placed = true;
            break;
        }
        body = mine.GetComponent<PlayerBody>();
        if (body != null) body.ShowFromOutside(true);
        HoldInput(true);
    }

    // Standing still on the stage, turned to the camera.
    private void Hold()
    {
        if (!placed || mine == null || stage == null) return;
        float off = Mathf.DeltaAngle(mine.transform.eulerAngles.y, stage.Yaw);
        if (Mathf.Abs(off) > .5f) CombatHeadMotion.For(mine)?.Turn(off * Mathf.Min(1f, Time.unscaledDeltaTime * 12f));
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
        if (body != null) body.ShowFromOutside(false);
        body = null;
        if (view != null) view.enabled = false;
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
        if (body != null) { body.ShowFromOutside(false); body = null; }
        // The controls stay held under the sheet; they come back when it closes (OnDestroy).
        sheet = ResultsSheet.Open(won);
        if (curtain != null) curtain.color = Color.clear;
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
        float shot = Mathf.Max(.6f, HubLobby.Number("showShot", 1.8f)) * (seats.Count == 1 ? 1.4f : 1f);
        float at = IntroSeconds;
        for (int i = seats.Count - 1; i >= 0; i--)
        {
            var seat = seats[i];
            seat.ShotAt = at;
            seat.ShotLength = shot;
            seat.Best = i == 0 && ResultsAwards.Of(won, seat.Entry).Count > 0;
            BuildSpot(seat);
            shots.Add(seat);
            at += shot;
        }
        // Then the whole row together, everyone's name under them and the chapter's highlights along the foot (not alone:
        // the one shot was all of them, and its highlights ride it).
        groupAt = at;
        bool row = seats.Count > 1;
        showEnd = row ? groupAt + Mathf.Max(.3f, HubLobby.Number("showHold", 1.6f)) : groupAt;
        highlightsAt = row ? groupAt + .1f : seats[0].ShotAt + .6f;
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
            if (live && prevAge < seat.ShotAt) StrikePose(seat);
            seat.SpotGroup.alpha = live ? 1f : 0f;
            if (!live) continue;
            // The name slides in from the left; the titles slam on down the left one after another.
            float nameIn = Out(local / .22f);
            seat.Name.anchoredPosition = new Vector2(80f - 120f * (1f - nameIn), 118f);
            for (int i = 0; i < seat.Slams.Count; i++)
            {
                var slam = seat.Slams[i];
                float slamAt = seat.ShotAt + HubLobby.Number("showTitlesAt", .3f) + i * HubLobby.Number("showTitleEvery", .16f);
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

    // The shot's player strikes their pose: chin up to the camera (CalloutGesture plays on every PC's copy of a body, so
    // each PC poses whoever its own show is looking at; with a gun in hand only the head and chest move).
    private void StrikePose(Seat seat)
    {
        var someone = PlayerOf(seat);
        if (someone == null || view == null) return;
        var gesture = someone.GetComponentInChildren<CalloutGesture>();
        if (gesture != null) gesture.Play(CalloutGesture.Kind.Jut, view.transform.position, true);
    }

    // The player standing for a seat: this PC's own, or a friend's copy by their seat in the room.
    private PlayerMovement PlayerOf(Seat seat)
    {
        if (seat.Entry.IsLocal) return mine;
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.IsNetworkDriven && MatchResults.SeatOf(someone) == seat.Entry.Client) return someone;
        return null;
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
