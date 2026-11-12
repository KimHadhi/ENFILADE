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
/// 1. The lineup: back in the hub everyone stands in a row on the stage (HubStage), in their own skin and clothes, the
///    one with the most titles in the middle, and a camera of its own looks at them. Names come up under them; then the
///    titles slam on under the names one by one, the best player's last (최다 처치, 구조왕, 가장 많이 쓰러짐 ...:
///    ResultsAwards), each with its number and its joke; alone, the player's best three, stacked beside them. A row of the chapter's
///    highlights counts up along the bottom (longest streak, farthest headshot, the team's kills, the money made).
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
        public RectTransform Plate;
        public CanvasGroup PlateGroup;
        public readonly List<RectTransform> Slams = new List<RectTransform>();
        public readonly List<float> SlamAt = new List<float>();
    }

    private readonly List<Seat> seats = new List<Seat>();
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
    private float age, prevAge, lastSlam, highlightsAt;
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
        if (Skipped() || age >= lastSlam + HubLobby.Number("showHold", 1.6f)) OpenSheet();
    }

    private void LateUpdate()
    {
        if (!showing || view == null || stage == null || sheet != null) return;
        // A slow push in toward the row while the titles land.
        float push = Mathf.Clamp01(age / Mathf.Max(1f, lastSlam + 1.5f));
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

        // The titles slam on from the last-ranked up to the best, the best last; alone, the player's best three.
        float at = HubLobby.Number("showTitlesAt", .8f), every = HubLobby.Number("showTitleEvery", .42f);
        int most = seats.Count == 1 ? 3 : 1;
        var order = new List<Seat>(seats);
        order.Reverse();
        lastSlam = at;
        foreach (var seat in seats) BuildPlate(seat);
        foreach (var seat in order)
        {
            var titles = ResultsAwards.Of(won, seat.Entry);
            if (titles.Count == 0)
            {
                seat.Slams.Add(BuildTitle(seat, null, 0));
                seat.SlamAt.Add(lastSlam = at);
                at += every;
                continue;
            }
            for (int i = 0; i < Mathf.Min(most, titles.Count); i++)
            {
                seat.Slams.Add(BuildTitle(seat, titles[i], i));
                seat.SlamAt.Add(lastSlam = at);
                at += every;
            }
        }
        highlightsAt = lastSlam + .25f;
        BuildHighlights();
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

    // The name plate under a player: their colour, name and class.
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

    // A title's slanted pink plate over a player: the title, its number, its joke. None won: a kind word instead.
    private RectTransform BuildTitle(Seat seat, ResultsAwards.Won title, int stack)
    {
        var root = FeelUI.Rect("Title " + seat.Entry.Name + " " + stack, canvas);
        root.sizeDelta = new Vector2(280f, 96f);
        root.localRotation = Quaternion.Euler(0f, 0f, stack % 2 == 0 ? 4f : -3f);
        var plate = KitStyle.Cut(KitStyle.TopLeft("Plate", root, Vector2.zero, new Vector2(280f, 58f)), title != null ? KitStyle.Accent : KitStyle.Ice);
        KitSkew.On(plate, 16f);
        string label = title != null ? title.Award.Label : Texts.Get("results.award.none");
        string line = title != null ? title.Award.Line : Texts.Get("results.award.none.line");
        KitStyle.Flat(KitStyle.TopLeft("Label", root, new Vector2(16f, 2f), new Vector2(170f, 54f)), label, 24f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ink);
        if (title != null)
        {
            var number = KitStyle.Flat(KitStyle.TopLeft("Number", root, new Vector2(150f, 2f), new Vector2(120f, 54f)), title.Shown, 26f,
                TextAlignmentOptions.MidlineRight, KitStyle.Ink);
            number.characterSpacing = -2f;
            if (title.Award.IsMoney)
            {
                var icon = MoneyIcon.Make(root, 22f);
                icon.color = KitStyle.Ink;
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 1f);
                icon.rectTransform.anchoredPosition = new Vector2(270f - number.preferredWidth - 16f, -29f);
            }
        }
        var joke = KitStyle.Flat(KitStyle.TopLeft("Line", root, new Vector2(18f, 60f), new Vector2(260f, 26f)), line, 15f,
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

    private void Pose()
    {
        curtain.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(age / .35f));
        float headerIn = Out((age - .1f) / .3f);
        header.anchoredPosition = new Vector2(80f - 160f * (1f - headerIn), -40f);
        // A long row is small on the screen: its plates shrink to keep apart.
        float apart = seats.Count > 1 ? Mathf.Abs(OnScreen(stage.Slot(1, seats.Count)).x - OnScreen(stage.Slot(0, seats.Count)).x) : 400f;
        float fit = Mathf.Clamp(apart / 300f, .5f, 1f);
        bool alone = seats.Count == 1;
        foreach (var seat in seats)
        {
            Vector2 feet = OnScreen(seat.Feet), chest = OnScreen(seat.Feet + Vector3.up * 1.25f);
            float plateIn = Out((age - .3f - seat.Index * .07f) / .25f);
            seat.PlateGroup.alpha = plateIn;
            seat.Plate.localScale = Vector3.one * fit;
            seat.Plate.anchoredPosition = feet + new Vector2(0f, (-46f - 20f * (1f - plateIn)) * fit);
            for (int i = 0; i < seat.Slams.Count; i++)
            {
                var slam = seat.Slams[i];
                float since = age - seat.SlamAt[i];
                if (since < 0f) { slam.localScale = Vector3.zero; continue; }
                if (prevAge < seat.SlamAt[i]) Slammed();
                // Down from nearly twice its size, a hair under, then home, with a shake as it lands.
                float t = Mathf.Clamp01(since / .22f);
                float size = t < .6f ? Mathf.Lerp(1.9f, .94f, t / .6f) : Mathf.Lerp(.94f, 1f, (t - .6f) / .4f);
                slam.localScale = Vector3.one * size * fit;
                float shake = since > .12f && since < .3f ? (1f - (since - .12f) / .18f) * 7f : 0f;
                // Alone, the titles stack beside the player; in a row each sits under its player's name.
                Vector2 home = alone ? chest + new Vector2(320f, 110f - i * 108f) : feet + new Vector2(0f, -150f * fit);
                slam.anchoredPosition = home + (shake > 0f ? Random.insideUnitCircle * shake : Vector2.zero);
            }
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
