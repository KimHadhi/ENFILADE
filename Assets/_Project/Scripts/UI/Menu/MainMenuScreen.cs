using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The title screen, laid out like an FPS lobby (the user's pick on 2026-11-09, after Payday 3 and Overwatch style
/// menus) in the menus' kit (KitStyle: the HUD's near-black, white and one pink; rangefinder marks). It opens like a
/// shutter (MainMenuIntro) onto: a top bar with the tilted name, tabs (플레이, 캐릭터, 기록, 설정), the player's chip and
/// quit (MainMenuTopBar); the ways to play stacked on the left as big slanted plates with a rail (MainMenuModes) and
/// the worn look under them; the chosen map's picture card at the top right (MainMenuMapCard) and the big "출격" button
/// at the bottom right saying what it will start (MainMenuLaunch); a bottom bar with key hints, radio calls and the
/// time (MainMenuTicker). The chosen character stands in the middle (MainMenuPreview) on a night rooftop inside the
/// rangefinder rings (MainMenuBackdrop), and dragging across the empty screen turns it. The lobby is drawn through the
/// HUD camera (HudCamera) so it has real depth, the way the in-game HUD's planes turn: the ways to play and the right
/// side lean inwards like two wings and tilt a little after the mouse, and the top bar tips back. Enter presses
/// "출격" when no window is up. The windows behind the buttons
/// (making a room, the server list, the profile) and the pages behind the tabs (the wardrobe, MainMenuWardrobe; the classes; the
/// records) are their own. Pressing Play in a level skips
/// this screen, so levels can still be tested directly.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuScreen : MonoBehaviour
{
    public const string SceneName = "MainMenu";
    // Layout (px at 1920x1080): how far everything keeps from the screen's sides, where the ways to play start (under
    // the top bar), where the map card starts, and how high the launch button sits over the bottom bar.
    private const float Edge = 56f, ModesGap = 150f, CardGap = 70f, LaunchGap = 50f, LookHeight = 40f;
    // How far the side panels turn inwards (°), how much more they turn after the mouse at the screen's edge, how far
    // they tip up or down after it, how far the top bar tips back, and how quickly they follow (1/s).
    private const float PanelYaw = 16f, PanelFollow = 8f, PanelPitch = 6f, BarPitch = 8f, LeanFollow = 5f;

    private string[] maps = Array.Empty<string>();
    private int mapIndex, characterTab = -1, recordsTab = -1, classesTab = -1;
    private MainMenuPreview preview;
    private RectTransform canvas;
    private TMP_Text lookLabel;
    private MainMenuTopBar topBar;
    private MainMenuTicker bottomBar;
    private MainMenuModes modes;
    private MainMenuMapCard mapCard;
    private MainMenuLaunch launch;
    private RectTransform right;
    private KitReveal rightReveal;
    private MainMenuRecords records;
    private MainMenuClasses classes;
    // The guns' skins and kill counts (MainMenuWeapons), and its tab.
    private MainMenuWeapons weapons;
    private int weaponsTab = -1;
    private CreateRoomView createRoom;
    private ServerBrowserView browser;
    private ProfileView profile;
    private MainMenuWardrobe wardrobe;
    private Vector2 lean;

    private static MainMenuScreen instance;
    private static int escapeFrame = -1;

    public static bool IsOpen { get; private set; }

    /// <summary>A window or page of the lobby takes Esc (it closes, and the Esc menu waits).</summary>
    public static bool TakesEscape => instance != null && !SettingsMenu.IsOpen && (instance.AnyWindowOpen() || Time.frameCount == escapeFrame);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
        instance = null;
        escapeFrame = -1;
    }

    private void Awake()
    {
        instance = this;
        IsOpen = true;
        Time.timeScale = 1f;
        CombatFeedHUD.ResetScore();
        PlayerPerks.Clear(); // perks last a campaign; back at the title screen it starts bare
        MatchResults.Clear(); // a chapter's results wait for the hub it leads back to, not for a later visit from here
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SettingsMenu.EnsureEventSystem();
        preview = gameObject.AddComponent<MainMenuPreview>();
        gameObject.AddComponent<SkinPortraits>(); // under the preview's light, for both character screens
    }

    private void Start()
    {
        maps = MapTable.Playable();
        mapIndex = CurrentMap();
        Build();
        preview.Show();
        GameSettings.Changed += ShowLook; // the Esc menu's character tab can change it too
        ProfilePicture.Changed += ShowPicture;
        MainMenuIntro.Play(canvas, Open);
    }

    // The shutter has started to open: everything builds itself in.
    private void Open()
    {
        topBar.Open(0f);
        bottomBar.Open(.05f);
        modes.Reveal.Open();
        modes.PointRail();
        rightReveal.Open();
        mapCard.Show(mapIndex, .3f);
        SayWhatLaunches();
    }

    private void Update()
    {
        if (topBar != null)
        {
            if (topBar.Status.text != GameSession.Status) topBar.Status.text = GameSession.Status;
            string shown = GameSettings.Current.DisplayName;
            if (topBar.PlayerName.text != shown)
            {
                topBar.PlayerName.text = shown;
                ShowPicture();
            }
        }
        browser?.Tick();
        LeanPanels();
        CloseByEscape();
        LaunchByKey();
    }

    private bool AnyWindowOpen() => createRoom != null && (createRoom.IsOpen || browser.IsOpen || profile.IsOpen || records.IsOpen || wardrobe.IsOpen
        || (weapons != null && weapons.IsOpen));

    // Esc closes the window or page that is up (back to the lobby) before the Esc menu would open; on the records, a
    // chapter's page goes back to the totals first.
    private void CloseByEscape()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || SettingsMenu.IsOpen || DevConsole.TakesKeyboard) return;
        if (createRoom.IsOpen) createRoom.Close();
        else if (browser.IsOpen) browser.Close();
        else if (profile.IsOpen) profile.Close();
        else if (records.ShowsChapter) records.ShowTotals();
        else if (wardrobe.ShowsSlot) wardrobe.ShowSlots();
        else if (records.IsOpen || wardrobe.IsOpen || (weapons != null && weapons.IsOpen)) ShowPlay();
        else return;
        escapeFrame = Time.frameCount;
    }

    // The two wings lean inwards and turn a little after the mouse, as the in-game HUD's planes do.
    private void LeanPanels()
    {
        if (modes == null || right == null) return;
        var mouse = Mouse.current;
        Vector2 aim = Vector2.zero;
        if (mouse != null && Screen.width > 0 && Screen.height > 0)
        {
            var at = mouse.position.ReadValue();
            aim = new Vector2(Mathf.Clamp(at.x / Screen.width - .5f, -.5f, .5f), Mathf.Clamp(at.y / Screen.height - .5f, -.5f, .5f));
        }
        lean = Vector2.Lerp(lean, aim, 1f - Mathf.Exp(-LeanFollow * Time.unscaledDeltaTime));
        float pitch = -lean.y * PanelPitch;
        modes.Root.localRotation = Quaternion.Euler(pitch, -PanelYaw + lean.x * PanelFollow, 0f);
        right.localRotation = Quaternion.Euler(pitch, PanelYaw + lean.x * PanelFollow, 0f);
    }

    // Enter presses "출격" when nothing else has the keyboard.
    private void LaunchByKey()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || launch == null) return;
        if (!keyboard.enterKey.wasPressedThisFrame && !keyboard.numpadEnterKey.wasPressedThisFrame) return;
        if (SettingsMenu.IsOpen || DevConsole.TakesKeyboard || records.IsOpen || wardrobe.IsOpen || createRoom.IsOpen || browser.IsOpen
            || profile.IsOpen) return;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.GetComponent<TMP_InputField>() != null) return;
        launch.Press();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        IsOpen = false;
        GameSettings.Changed -= ShowLook;
        ProfilePicture.Changed -= ShowPicture;
        browser?.StopSearching();
    }

    private void Build()
    {
        canvas = FeelUI.Rect("Canvas", transform);
        canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();
        // Through the HUD camera, so the side panels lean in real depth.
        HudCamera.Adopt(canvas.GetComponent<Canvas>());
        MainMenuBackdrop.Make(transform, preview);

        BuildShade();
        BuildFrame();

        // 플레이 brings the lobby back from the wardrobe, the classes or the records; the other windows close themselves.
        var tabs = new List<(string, Action)> { (Texts.Get("menu.tab.play"), ShowPlay) };
        if (CanChooseCharacter())
        {
            characterTab = tabs.Count;
            tabs.Add((Texts.Get("menu.tab.character"), OpenCharacter));
        }
        // The classes (stage 7): which one this player plays from the next run.
        classesTab = tabs.Count;
        tabs.Add((Texts.Get("menu.tab.classes"), OpenClasses));
        weaponsTab = tabs.Count;
        tabs.Add((Texts.Get("menu.tab.weapons"), OpenWeapons));
        recordsTab = tabs.Count;
        tabs.Add((Texts.Get("menu.tab.records"), OpenRecords));
        // Settings is a gear beside quit at the top right, not a tab.
        topBar = MainMenuTopBar.Make(canvas, Edge, tabs, () => profile.Show(), SettingsMenu.OpenFromMainMenu, SettingsMenu.QuitGame);
        topBar.transform.localRotation = Quaternion.Euler(BarPitch, 0f, 0f);
        bottomBar = MainMenuTicker.Make(canvas, Edge, new List<(string, string)>
        {
            ("Esc", Texts.Get("menu.hint.settings")),
            (Texts.Get("menu.hint.drag.key"), Texts.Get("menu.hint.drag")),
        });

        float modesTop = MainMenuTopBar.Height + ModesGap;
        float modesHeight = 1080f - modesTop - MainMenuTicker.Height - 30f;
        modes = MainMenuModes.Make(canvas, new Vector2(Edge, modesTop), modesHeight, new List<(MainMenuModes.Mode, string, string)>
        {
            (MainMenuModes.Mode.Solo, Texts.Get("menu.mode.solo"), Texts.Get("menu.mode.solo.note")),
            (MainMenuModes.Mode.Host, Texts.Get("menu.mode.host"), Texts.Get("menu.mode.host.note")),
            (MainMenuModes.Mode.Browse, Texts.Get("menu.mode.browse"), Texts.Get("menu.mode.browse.note")),
        }, _ => SayWhatLaunches(), Launch);
        BuildLook(modesHeight - LookHeight);

        right = KitStyle.Stretch("Right", canvas);
        right.pivot = new Vector2(1f, .5f); // the right wing turns about the screen's right edge
        rightReveal = right.gameObject.AddComponent<KitReveal>();
        rightReveal.Hide();
        mapCard = MainMenuMapCard.Make(right, Edge, MainMenuTopBar.Height + CardGap, maps, PickMap);
        launch = MainMenuLaunch.Make(right, Edge, MainMenuTicker.Height + LaunchGap, () => Launch(modes.Chosen));
        rightReveal.Card(mapCard.Root, mapCard.Lid, .12f);
        rightReveal.Card(launch.Root, launch.Lid, .2f);
        records = MainMenuRecords.Make(canvas, Edge, MainMenuTopBar.Height, MainMenuTicker.Height);
        classes = MainMenuClasses.Make(canvas, Edge, MainMenuTopBar.Height, MainMenuTicker.Height);
        weapons = MainMenuWeapons.Make(canvas, Edge, MainMenuTopBar.Height, MainMenuTicker.Height);

        createRoom = new CreateRoomView(canvas, StartHost);
        browser = new ServerBrowserView(canvas);
        profile = new ProfileView(canvas);
        // The wardrobe takes the lobby's place as the records do, the character left standing in the middle.
        wardrobe = MainMenuWardrobe.Make(canvas, Edge, MainMenuTopBar.Height, MainMenuTicker.Height, preview);
        KitGrain.Make(canvas);
        ShowLook();
        ShowPicture();
        SayWhatLaunches();
    }

    // Dark screen edges, deeper behind the left and right so their type reads, over the character; the shade is also
    // the empty screen a drag turns the character on.
    private void BuildShade()
    {
        var shade = KitStyle.Stretch("Shade", canvas);
        var edges = shade.gameObject.AddComponent<RawImage>();
        edges.texture = KitShapes.Vignette;
        edges.color = KitStyle.With(KitStyle.Night, .9f);
        edges.raycastTarget = true; // catches the drags
        shade.gameObject.AddComponent<MainMenuDrag>().Preview = preview;
        Side("Left Shade", false, 900f, .8f);
        Side("Right Shade", true, 820f, .72f);
    }

    private void Side(string name, bool right, float width, float alpha)
    {
        var side = FeelUI.Rect(name, canvas);
        side.anchorMin = new Vector2(right ? 1f : 0f, 0f);
        side.anchorMax = new Vector2(right ? 1f : 0f, 1f);
        side.pivot = new Vector2(right ? 1f : 0f, .5f);
        side.sizeDelta = new Vector2(width, 0f);
        var image = side.gameObject.AddComponent<RawImage>();
        image.texture = KitShapes.Fade;
        image.color = KitStyle.With(KitStyle.Night, alpha);
        image.uvRect = right ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        image.raycastTarget = false;
    }

    // Faint viewfinder corners round the screen, inside the top and bottom bars.
    private void BuildFrame()
    {
        const float inset = 22f, arm = 30f;
        for (int i = 0; i < 4; i++)
        {
            bool right = i % 2 == 1, bottom = i >= 2;
            var corner = FeelUI.Rect("Frame Corner", canvas);
            corner.anchorMin = corner.anchorMax = corner.pivot = new Vector2(right ? 1f : 0f, bottom ? 0f : 1f);
            corner.anchoredPosition = new Vector2(right ? -inset : inset,
                bottom ? inset + MainMenuTicker.Height : -(inset + MainMenuTopBar.Height));
            corner.sizeDelta = Vector2.zero;
            corner.localScale = new Vector3(right ? -1f : 1f, bottom ? -1f : 1f, 1f);
            KitStyle.Bar("Across", corner, Vector2.zero, new Vector2(arm, 1f), KitStyle.Faint);
            KitStyle.Bar("Down", corner, Vector2.zero, new Vector2(1f, arm), KitStyle.Faint);
        }
    }

    // The worn look at the foot of the ways to play: a small slanted chip that opens the wardrobe.
    private void BuildLook(float y)
    {
        var look = KitStyle.TopLeft("Look", modes.Root, new Vector2(0f, y), new Vector2(340f, LookHeight));
        look.localRotation = Quaternion.Euler(0f, 0f, 2f);
        var back = KitStyle.Fill(look, KitStyle.With(KitStyle.Ice, .09f));
        back.raycastTarget = true;
        KitSkew.On(back, 10f);
        var icon = KitButtons.IconImage(look, "Character", 18f, KitStyle.Ice);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(24f, 0f);
        lookLabel = KitStyle.Flat(KitStyle.TopLeft("Text", look, new Vector2(44f, 0f), new Vector2(280f, LookHeight)), "", 16f,
            TextAlignmentOptions.MidlineLeft);
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", look), KitStyle.Flash);
        KitSkew.On(lid, 10f);
        modes.Reveal.Card(look, lid, .32f);
        if (characterTab < 0) return;
        var button = look.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(OpenCharacter);
        look.gameObject.AddComponent<KitHover>();
    }

    private void OpenCharacter()
    {
        if (characterTab < 0) return;
        CloseRecords();
        CloseClasses();
        CloseWeapons();
        topBar.SetActive(characterTab);
        if (wardrobe.IsOpen) return;
        wardrobe.Open();
        modes.Root.gameObject.SetActive(false);
        right.gameObject.SetActive(false);
    }

    private void CloseWardrobe()
    {
        if (wardrobe == null || !wardrobe.IsOpen) return;
        wardrobe.Close();
        modes.Root.gameObject.SetActive(true);
        modes.Reveal.Open();
        modes.PointRail();
        right.gameObject.SetActive(true);
        rightReveal.Open();
        ShowLook();
        SayWhatLaunches();
    }

    // Back to the lobby itself from whichever page is up.
    private void ShowPlay()
    {
        CloseWardrobe();
        CloseRecords();
        CloseClasses();
        CloseWeapons();
        topBar.SetActive(0);
    }

    // The records take the lobby's place: the ways to play and the right side step aside under them.
    private void OpenRecords()
    {
        CloseWardrobe();
        CloseClasses();
        CloseWeapons();
        topBar.SetActive(recordsTab);
        if (records.IsOpen) return;
        records.Open();
        modes.Root.gameObject.SetActive(false);
        right.gameObject.SetActive(false);
    }

    // The classes take the lobby's place as the records do.
    private void OpenClasses()
    {
        CloseWardrobe();
        CloseRecords();
        CloseWeapons();
        topBar.SetActive(classesTab);
        classes.Open();
        modes.Root.gameObject.SetActive(false);
        right.gameObject.SetActive(false);
    }

    // The guns' skins and kill counts take the lobby's place as the classes do.
    private void OpenWeapons()
    {
        CloseWardrobe();
        CloseRecords();
        CloseClasses();
        topBar.SetActive(weaponsTab);
        if (weapons.IsOpen) return;
        weapons.Open();
        modes.Root.gameObject.SetActive(false);
        right.gameObject.SetActive(false);
    }

    private void CloseWeapons()
    {
        if (weapons == null || !weapons.IsOpen) return;
        weapons.Close();
        modes.Root.gameObject.SetActive(true);
        modes.Reveal.Open();
        modes.PointRail();
        right.gameObject.SetActive(true);
        rightReveal.Open();
        SayWhatLaunches();
    }

    private void CloseClasses()
    {
        if (classes == null || !classes.IsOpen) return;
        classes.Close();
        modes.Root.gameObject.SetActive(true);
        modes.Reveal.Open();
        modes.PointRail();
        right.gameObject.SetActive(true);
        rightReveal.Open();
        SayWhatLaunches();
    }

    private void CloseRecords()
    {
        if (records == null || !records.IsOpen) return;
        records.Close();
        modes.Root.gameObject.SetActive(true);
        modes.Reveal.Open();
        modes.PointRail();
        right.gameObject.SetActive(true);
        rightReveal.Open();
    }

    // What "출격" will start, under its name.
    private void SayWhatLaunches()
    {
        if (launch == null || modes == null) return;
        string map = maps.Length > 0 ? MapTable.DisplayName(maps[mapIndex]) : Texts.Get("menu.map.empty");
        // With the hub built, both start there with this map on its table (memo 46).
        bool hub = HubFlow.IsBuilt;
        launch.Say(modes.Chosen switch
        {
            MainMenuModes.Mode.Host => Texts.Format(hub ? "menu.launch.host.hub" : "menu.launch.host", map),
            MainMenuModes.Mode.Browse => Texts.Get("menu.launch.browse"),
            _ => Texts.Format(hub ? "menu.launch.solo.hub" : "menu.launch.solo", map),
        });
    }

    private void Launch(MainMenuModes.Mode mode)
    {
        // From the character screen or the records "출격" goes back to the lobby first: the room's window opened behind the
        // character screen (friend feedback C12).
        if (wardrobe.IsOpen || records.IsOpen || weapons.IsOpen) ShowPlay();
        switch (mode)
        {
            case MainMenuModes.Mode.Host: OpenCreateRoom(); break;
            case MainMenuModes.Mode.Browse: browser.Open(); break;
            default: StartSolo(); break;
        }
    }

    private int CurrentMap()
    {
        if (maps.Length == 0) return 0;
        var settings = GameSettings.Current;
        int now = Array.IndexOf(maps, settings.lastMap);
        if (now < 0) { now = 0; settings.lastMap = maps[0]; }
        return now;
    }

    private void PickMap(int step)
    {
        if (maps.Length == 0) return;
        mapIndex = (mapIndex + step + maps.Length) % maps.Length;
        GameSettings.Current.lastMap = maps[mapIndex];
        GameSettings.Save();
        mapCard.Show(mapIndex, 0f);
        SayWhatLaunches();
    }

    // There is something to choose: more than one character, or more than one look of the only one.
    private static bool CanChooseCharacter()
    {
        var skins = PlayerBodyProfile.Load()?.Skins.Where(skin => skin != null && skin.Model != null).ToArray();
        return skins != null && skins.Length > 0 && (skins.Length > 1 || skins[0].Variants.Count > 1);
    }

    private void ShowLook()
    {
        var bodies = PlayerBodyProfile.Load();
        var skin = bodies?.CurrentSkin;
        if (skin == null) return;
        var variant = bodies.CurrentVariant;
        if (lookLabel != null) lookLabel.text = variant != null ? skin.DisplayName + "  ·  " + variant.DisplayName : skin.DisplayName;
    }

    // The top bar's square: the player's own picture, or their name's first letter.
    private void ShowPicture()
    {
        if (topBar != null) ProfilePicture.Paint(topBar.Portrait, topBar.Initial, GameSettings.Current.DisplayName);
    }

    // Alone into the hub, where the chosen map is already on the map table (memo 46); straight to the map without one.
    private void StartSolo()
    {
        if (maps.Length == 0 || GameSession.IsOnline) return;
        HubFlow.EnterSolo();
    }

    private void OpenCreateRoom()
    {
        if (maps.Length > 0 && !GameSession.IsOnline) createRoom.Show(GameSettings.Current.lastMap);
    }

    // A room opens in the hub too: the party gathers, readies up and sets off together (HubLobby).
    private void StartHost(string title, int maxPlayers, bool internet)
    {
        GameSettings.Save();
        GameSession.Host(HubFlow.StartScene, title, maxPlayers, internet);
    }
}
