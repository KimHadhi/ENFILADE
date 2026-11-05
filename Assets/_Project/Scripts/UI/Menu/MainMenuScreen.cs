using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The title screen: choose a map and a character, then play alone, make a room or find one.
/// Pressing Play in a level skips this screen, so levels can still be tested directly.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuScreen : MonoBehaviour
{
    public const string SceneName = "MainMenu";
    private string[] maps = Array.Empty<string>();
    private MainMenuPreview preview;
    private TMP_Text status, look;
    private CreateRoomView createRoom;
    private ServerBrowserView browser;
    private ProfileView profile;
    private CharacterScreenView character;

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    private void Awake()
    {
        IsOpen = true;
        Time.timeScale = 1f;
        CombatFeedHUD.ResetScore();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SettingsMenu.EnsureEventSystem();
        preview = gameObject.AddComponent<MainMenuPreview>();
        gameObject.AddComponent<SkinPortraits>(); // under the preview's light, for both character screens
    }

    private void Start()
    {
        maps = MapTable.Playable();
        Build();
        preview.Show();
        GameSettings.Changed += ShowLook; // the Esc menu's character tab can change it too
    }

    private void Update()
    {
        if (status != null && status.text != GameSession.Status) status.text = GameSession.Status;
        browser?.Tick();
    }

    private void OnDestroy()
    {
        IsOpen = false;
        GameSettings.Changed -= ShowLook;
        browser?.StopSearching();
    }

    private void Build()
    {
        var canvas = W.Rect("Canvas", transform);
        canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        var column = W.Rect("Menu", canvas);
        column.anchorMin = column.anchorMax = column.pivot = new Vector2(0f, .5f);
        column.anchoredPosition = new Vector2(120f, 0f);
        column.sizeDelta = new Vector2(600f, 0f);
        var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        W.Size(W.Label(column, "ENFILADE", 84f, W.Text), height: 124f);
        AddMapChoice(column);
        AddCharacterButton(column);
        W.Size(W.Rect("Gap", column), height: 20f);
        MenuButton(column, "혼자 하기", true, StartSolo);
        MenuButton(column, "방 만들기", false, OpenCreateRoom);
        MenuButton(column, "서버 찾기", false, () => browser.Open());
        MenuButton(column, "프로필", false, () => profile.Show());
        MenuButton(column, "설정", false, SettingsMenu.OpenFromMainMenu);
        MenuButton(column, "게임 종료", false, SettingsMenu.QuitGame);
        W.Size(W.Rect("Gap", column), height: 12f);
        status = W.Label(column, GameSession.Status, 19f, W.Accent);
        W.Size(status, height: 28f);

        createRoom = new CreateRoomView(canvas, StartHost);
        browser = new ServerBrowserView(canvas);
        profile = new ProfileView(canvas);
        character = new CharacterScreenView(canvas, column, ShowLook);
    }

    private void AddMapChoice(Transform column)
    {
        if (maps.Length == 0) return;
        var settings = GameSettings.Current;
        var names = maps.Select(MapTable.DisplayName).ToArray();
        int selected = Mathf.Max(0, Array.IndexOf(maps, settings.lastMap));
        settings.lastMap = maps[selected];
        W.Choice(W.Row(column, "맵"), names, selected, i => settings.lastMap = maps[i]);
    }

    // The worn look on a button that opens the character screen; shown when there is anything to choose.
    private void AddCharacterButton(Transform column)
    {
        var profile = PlayerBodyProfile.Load();
        var skins = profile?.Skins.Where(skin => skin != null && skin.Model != null).ToArray();
        if (skins == null || skins.Length == 0) return;
        if (skins.Length < 2 && skins[0].Variants.Count < 2) return;
        var button = W.ActionButton(W.Row(column, "캐릭터"), "", false, () => character.Show());
        W.Size(button, flexibleWidth: 1f);
        look = button.GetComponentInChildren<TMP_Text>();
        ShowLook();
    }

    private void ShowLook()
    {
        var profile = PlayerBodyProfile.Load();
        var skin = profile?.CurrentSkin;
        if (look == null || skin == null) return;
        var variant = profile.CurrentVariant;
        look.text = variant != null ? skin.DisplayName + " · " + variant.DisplayName : skin.DisplayName;
    }

    private static void MenuButton(Transform column, string text, bool primary, Action clicked)
    {
        W.Size(W.ActionButton(column, text, primary, clicked), height: 54f);
    }

    private void StartSolo()
    {
        if (maps.Length == 0 || GameSession.IsOnline) return;
        GameSettings.Save();
        SceneManager.LoadScene(GameSettings.Current.lastMap);
    }

    private void OpenCreateRoom()
    {
        if (maps.Length > 0 && !GameSession.IsOnline) createRoom.Show(GameSettings.Current.lastMap);
    }

    private void StartHost(string title, int maxPlayers, bool internet)
    {
        GameSettings.Save();
        GameSession.Host(GameSettings.Current.lastMap, title, maxPlayers, internet);
    }
}
