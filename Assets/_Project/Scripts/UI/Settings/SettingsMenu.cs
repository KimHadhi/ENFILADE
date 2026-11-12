using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The Esc menu. Esc pauses the game, frees the cursor and shows the settings; Esc or "돌아가기" resumes and saves.
/// On the main menu it is only the settings panel: there is nothing to pause, and the cursor stays free.
/// One instance is created per run and survives scene loads. Esc that closes the chat or the developer console does
/// not open it.
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsMenu : MonoBehaviour
{
    private static SettingsMenu instance;
    private SettingsMenuView view;
    private SinglePlayerChatManager chat;
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create() => DontDestroyOnLoad(new GameObject("Settings Menu").AddComponent<SettingsMenu>().gameObject);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    public static void OpenFromMainMenu() { if (instance != null) instance.Open(); }

    private void Awake()
    {
        instance = this;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        gameObject.AddComponent<GraphicRaycaster>();
        view = new SettingsMenuView(transform, Close, LeaveToMainMenu, QuitGame);
    }

    private void Update()
    {
        // Whether a box was being typed in when this ran last frame: the box may already have taken this frame's Esc.
        bool wasTyping = typing;
        typing = TypingInField();
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
        // Esc in a box (a number, a colour, a share code) only leaves it with the old value; the next Esc closes the menu.
        if (IsOpen && (typing || wasTyping)) return;
        // The key window goes first: its Esc stops listening for a key, or closes it, never the menu under it.
        if (IsOpen && KeyBindingsView.TakeEscape()) return;
        // Esc closes the developer console, the shop, the gun's customize menu, the perk cards or the hub's mirror first.
        if (DevConsole.TakesKeyboard || ShopMenu.TakesKeyboard || GunCustomizeMenu.TakesKeyboard || PerkCardScreen.TakesKeyboard
            || HubDressing.TakesKeyboard || MainMenuScreen.TakesEscape) return;
        if (IsOpen) Close();
        else if (!ChatTookEscape()) Open();
    }

    private bool typing;

    private static bool TypingInField()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.TryGetComponent(out TMPro.TMP_InputField field) && field.isFocused;
    }

    private bool ChatTookEscape()
    {
        if (chat == null) chat = FindAnyObjectByType<SinglePlayerChatManager>();
        return chat != null && chat.BlocksMissionInputThisFrame;
    }

    private void Open()
    {
        IsOpen = true;
        if (!MainMenuScreen.IsOpen && !GameSession.IsOnline) GamePause.Set(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        EnsureEventSystem();
        view.Show(true);
    }

    private void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        view.Show(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        GameSettings.Save();
        if (MainMenuScreen.IsOpen) return;
        GamePause.Set(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LeaveToMainMenu()
    {
        Close();
        if (GameSession.IsOnline) GameSession.Leave();
        else SceneLoad.Go(MainMenuScreen.SceneName);
    }

    public static void QuitGame()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Scenes without UI (the movement playground) have no EventSystem; add one to that scene only.
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var system = new GameObject("EventSystem", typeof(EventSystem));
        system.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private void OnDestroy()
    {
        if (!IsOpen) return;
        IsOpen = false;
        GamePause.Set(false);
    }
}
