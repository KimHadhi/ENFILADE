using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The hub (memo 46, the user's answer: a walkable 3D hideout where guns can be fired, working like a room lobby: the
/// party stays together, readies up and chooses the chapter). What happens there, on every PC:
/// - [F] at the map table's cards picks the chapter (the host, or the player alone; a guest sees the host's pick).
/// - [F] at a locker of the class wall plays that class from now on, its guns in hand at once to try (ClassKit.Rekit).
/// - [F] at the gun rack puts that gun in the hands, full; [F] at the ammo crate fills every gun. The firing range's
///   targets (RangeTarget: steel, they ring and swing) show what each gun does to a zombie, and are practice foes: a
///   kill there stacks what the class stacks, a hit charges the ultimate and F marks one, a Schießhund's bounty too
///   (2026-11-12), while nothing of it reaches the run's feed, money or counts. A full gauge deals cards as in a
///   mission, the ultimate after all of them (PerkCardScreen), but they are practice cards (PlayerPerks.BeginPractice):
///   they and the gauge go when the hub is left or a class is picked here, so a chapter starts with the campaign's own.
/// - [F] at the stage's mirror (HubMirror: everyone sees themself in it, head and all) opens the dressing spot in front
///   of it (HubDressing, 2026-11-12): the title screen's wardrobe over a camera looking into the mirror, every pick worn
///   at once, saved and seen by the room. The Esc menu's 캐릭터 tab went for it.
/// - [F] at the ready button says this player is ready. Who is ready shows at the top middle of the HUD, everyone's
///   profile picture with a check on it once ready (HubReadyBoard, TF2's Mann vs. Machine way). Once everyone in the
///   room is ready, the count runs down
///   (Tables/Hub departSeconds) while the way out's shutter rises, and the host takes the room to the chapter; alone,
///   the button sets off at once (soloDepartSeconds). Anyone who stops being ready stops the count.
/// - Back from a chapter, its results play first (ResultsShow over the stage, MatchResults).
/// The host keeps the chapter, who is ready and the count, and tells the room (a named message on change and every
/// couple of seconds); a guest's press goes to the host, as CampaignRun's do. The builder (Editor/World/HubBuilder)
/// stands the stations (HubStation) and the targets; this fills them from the tables at play (HubLobby.Stations).
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Campaign)]
public sealed partial class HubLobby : MonoBehaviour
{
    public const string TableName = "Hub", TableRow = "Default";
    private const string StateMessage = "ENFILADE.Hub", ReadyMessage = "ENFILADE.HubReady";
    // The state is told again this often even when nothing changed, so a guest who missed a message catches up (s).
    private const float ShareEvery = 2f;
    private const string ObjectiveId = "hub.ready";

    private static HubLobby active;
    private readonly HashSet<ulong> ready = new HashSet<ulong>();
    private readonly List<ulong> gone = new List<ulong>();
    private NetworkManager network;
    private InputAction interact;
    private CampaignScreen screen;
    private MissionTracker tracker;
    private HubPanel panel;
    private HubReadyBoard readyBoard;
    private string chapter = "";
    // Seconds left before setting off; negative while not counting. The whole count it started from, for the shutter.
    private float countdown = -1f, countdownFrom = 1f, nextShare;
    private bool listening, departed, stateChanged = true;
    private int shownReady = -1, shownParty = -1;

    /// <summary>The hub is up in this scene.</summary>
    public static bool IsActive => active != null;
    /// <summary>The chapter the party will set off to.</summary>
    public static string Chapter => active != null ? active.chapter : "";
    /// <summary>Seconds left before setting off, or negative while not counting.</summary>
    public static float Countdown => active != null ? active.countdown : -1f;
    /// <summary>A station is looked at, so F works it: nothing else takes that F (PlayerPing, as CampaignRun.Offering).</summary>
    public static bool Offering => active != null && active.aimed != null && !ResultsShow.IsPlaying;

    private bool IsHost => network == null || network.IsServer;
    private ulong MySeat => network != null && network.IsListening ? network.LocalClientId : 0UL;

    /// <summary>A number from Tables/Hub (row Default).</summary>
    public static float Number(string column, float fallback)
        => GameTables.TryGetRow(TableName, TableRow, out var row) && row.TryGetValue(column, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    /// <summary>This player is ready to set off (as this PC last heard).</summary>
    public static bool IsReady(PlayerMovement someone) => active != null && active.ready.Contains(MatchResults.SeatOf(someone));

    // A class picked here (PlayerClass.Picked; its kit starts afresh in ClassKit) is tried from nothing, as a chapter
    // starts it: the practice cards go and the gauge empties, so the old class's cards and charge can't be spent on it.
    private static void OnClassPicked(string before, string now)
    {
        PlayerPerks.RestartPractice();
        var mine = PlayerRoster.Local;
        if (mine != null && mine.Ultimate != null) mine.Ultimate.SetCurrentCharge(0f);
    }

    private void Awake()
    {
        active = this;
        string wanted = HubFlow.Chosen;
        // A testing room picked before they left the menus (MapTable.IsTestingRoom) is no chapter to set off to.
        chapter = !string.IsNullOrEmpty(wanted) && wanted != HubFlow.SceneName && Application.CanStreamedLevelBeLoaded(wanted)
            && !MapTable.IsTestingRoom(wanted) ? wanted : HubFlow.FirstChapter();
    }

    private void Start()
    {
        interact = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Interact", false) : null;
        screen = gameObject.AddComponent<CampaignScreen>();
        tracker = FindFirstObjectByType<MissionTracker>();
        panel = HubPanel.Make();
        readyBoard = HubReadyBoard.Make();
        Listen();
        BuildStations();
        // A campaign seen through to its end starts over bare: perks last a campaign (PerkCardScreen).
        if (MatchResults.Pending && MatchResults.CampaignDone) PlayerPerks.Clear();
        // From here cards are practice: the range deals them as a mission would, and they go with the hub.
        PlayerPerks.BeginPractice();
        PlayerClass.Picked += OnClassPicked;
        if (MatchResults.Pending) ResultsShow.Play();
        ShowObjective();
    }

    private void OnDestroy()
    {
        PlayerClass.Picked -= OnClassPicked;
        // Leaving (for a chapter, or the title screen): the practice cards go, the campaign's are what the chapter gets.
        // The new player there starts with an empty gauge of its own.
        if (active == this) PlayerPerks.EndPractice();
        if (active == this) active = null;
        if (panel != null) Destroy(panel.gameObject);
        if (readyBoard != null) Destroy(readyBoard.gameObject);
        if (!listening || network == null || network.CustomMessagingManager == null) return;
        network.CustomMessagingManager.UnregisterNamedMessageHandler(network.IsServer ? ReadyMessage : StateMessage);
    }

    private void Listen()
    {
        network = GameSession.Network;
        if (network == null || network.CustomMessagingManager == null) return;
        listening = true;
        if (network.IsServer) network.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, ReceiveReady);
        else network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
    }

    private void Update()
    {
        if (departed) return;
        float dt = Time.unscaledDeltaTime;
        if (IsHost) { ForgetGone(); Count(dt); Share(); }
        else if (countdown > 0f) countdown = Mathf.Max(0f, countdown - dt);
        Aim();
        ShowStations();
        // The objective line keeps out of the results' way, and comes back after them.
        if (!ResultsShow.IsPlaying) ShowObjective();
        else if (shownParty >= 0) { shownParty = -1; tracker?.ClearObjective(); }
        panel?.Show(chapter, countdown);
    }

    // ---- Ready and the count (the host, or the PC alone) ----

    private void ForgetGone()
    {
        if (network == null || !network.IsListening) return;
        gone.Clear();
        foreach (ulong seat in ready) if (!network.ConnectedClients.ContainsKey(seat)) gone.Add(seat);
        foreach (ulong seat in gone) { ready.Remove(seat); stateChanged = true; }
    }

    private bool EveryoneReady()
    {
        if (network == null || !network.IsListening) return ready.Contains(0UL);
        if (network.ConnectedClientsIds.Count == 0) return false;
        foreach (ulong seat in network.ConnectedClientsIds) if (!ready.Contains(seat)) return false;
        return true;
    }

    private void Count(float dt)
    {
        if (!EveryoneReady())
        {
            if (countdown >= 0f) { countdown = -1f; stateChanged = true; }
            return;
        }
        if (countdown < 0f)
        {
            countdownFrom = countdown = Mathf.Max(0f, network == null || !network.IsListening
                ? Number("soloDepartSeconds", .8f) : Number("departSeconds", 3f));
            stateChanged = true;
            KitSound.Play(KitSound.Cue.Charge, .7f);
            return;
        }
        countdown -= dt;
        if (countdown > 0f) return;
        countdown = 0f;
        Depart();
    }

    private void Depart()
    {
        departed = true;
        if (HubFlow.Depart(chapter)) return;
        // The chapter can't be loaded (not in the build any more): everyone stays, unready.
        departed = false;
        countdown = -1f;
        ready.Clear();
        stateChanged = true;
        AlertHUD.Danger(Texts.Get("hub.alert.cannot"));
    }

    // This PC's player pressed the ready button.
    private void ToggleReady()
    {
        bool now = !ready.Contains(MySeat);
        KitSound.Play(now ? KitSound.Cue.Buy : KitSound.Cue.Refuse, .7f);
        if (IsHost) { SetReady(MySeat, now); return; }
        // Shown at once here; the host's word follows.
        if (now) ready.Add(MySeat); else ready.Remove(MySeat);
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)(now ? 1 : 0));
        network.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer);
    }

    private void SetReady(ulong seat, bool now)
    {
        bool changed = now ? ready.Add(seat) : ready.Remove(seat);
        if (changed) stateChanged = true;
    }

    // A guest pressed their ready button.
    private void ReceiveReady(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte now);
        if (!departed) SetReady(sender, now != 0);
    }

    // ---- Telling the room ----

    private void Share()
    {
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        if (!stateChanged && Time.unscaledTime < nextShare) return;
        stateChanged = false;
        nextShare = Time.unscaledTime + ShareEvery;
        int count = Mathf.Min(ready.Count, 255);
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<FixedString64Bytes>() + sizeof(float) + sizeof(byte)
            + count * sizeof(ulong), Allocator.Temp);
        writer.WriteValueSafe(new FixedString64Bytes(chapter.Length > 60 ? chapter.Substring(0, 60) : chapter));
        writer.WriteValueSafe(countdown);
        writer.WriteValueSafe((byte)count);
        int written = 0;
        foreach (ulong seat in ready)
        {
            if (written++ >= count) break;
            writer.WriteValueSafe(seat);
        }
        network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer);
    }

    // A guest follows the host's hub.
    private void ReceiveState(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out FixedString64Bytes picked);
        reader.ReadValueSafe(out float left);
        reader.ReadValueSafe(out byte count);
        ready.Clear();
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong seat);
            ready.Add(seat);
        }
        string now = picked.ToString();
        if (now != chapter && now.Length > 0) { chapter = now; HubFlow.Chosen = now; }
        if (left >= 0f && countdown < 0f) { countdownFrom = Mathf.Max(left, .01f); KitSound.Play(KitSound.Cue.Charge, .7f); }
        countdown = left;
    }

    // ---- The objective line: how many are ready ----

    private void ShowObjective()
    {
        if (tracker == null) return;
        int party = 0, readyNow = 0;
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !(someone.IsLocal || someone.IsNetworkDriven)) continue;
            party++;
            if (IsReady(someone)) readyNow++;
        }
        if (party == shownParty && readyNow == shownReady) return;
        bool first = shownParty < 0;
        shownParty = party;
        shownReady = readyNow;
        string title = Texts.Format("hub.objective.title", MapTable.DisplayName(chapter));
        string detail = party > 1 ? Texts.Get("hub.objective.room") : Texts.Get("hub.objective.solo");
        if (first) tracker.SetObjective(new MissionObjective(ObjectiveId, title, detail, MissionProgressDisplay.Count, readyNow, Mathf.Max(1, party)), false);
        else tracker.SetProgress(ObjectiveId, readyNow, Mathf.Max(1, party));
    }

    /// <summary>The chapter changed: the objective's title says the new one.</summary>
    private void RenameObjective()
    {
        shownParty = -1;
        if (tracker != null) tracker.ClearObjective();
    }

    // As the mouse look: not while the cursor is free (chat, menus), the window is in the background, the console is
    // open, the results are playing, or the mirror's dressing spot is open (or was shut by this very [F]).
    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard
        && !ShopMenu.TakesKeyboard && !PerkCardScreen.TakesKeyboard && !ResultsShow.IsPlaying && !HubDressing.TakesKeyboard;
}
