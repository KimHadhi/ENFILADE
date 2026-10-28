using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// The Esc menu. Esc pauses the game, frees the cursor and shows the settings; Esc or "돌아가기" resumes and saves.
/// One instance is created per run and survives scene loads. Esc that closes the chat does not open it.
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsMenu : MonoBehaviour
{
    private SettingsMenuView view;
    private SinglePlayerChatManager chat;
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create() => DontDestroyOnLoad(new GameObject("Settings Menu").AddComponent<SettingsMenu>().gameObject);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    private void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        gameObject.AddComponent<GraphicRaycaster>();
        view = new SettingsMenuView(transform, Close, Quit);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
        if (IsOpen) Close();
        else if (!ChatTookEscape()) Open();
    }

    private bool ChatTookEscape()
    {
        if (chat == null) chat = FindAnyObjectByType<SinglePlayerChatManager>();
        return chat != null && chat.BlocksMissionInputThisFrame;
    }

    private void Open()
    {
        IsOpen = true;
        GamePause.Set(true);
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
        GamePause.Set(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Quit()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Scenes without UI (the movement playground) have no EventSystem; add one to that scene only.
    private static void EnsureEventSystem()
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
