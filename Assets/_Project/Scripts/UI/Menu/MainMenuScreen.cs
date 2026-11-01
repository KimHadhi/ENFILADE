using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The title screen: choose a map and a character, then start. Hosting and joining open with the network work.
/// Pressing Play in a level skips this screen, so levels can still be tested directly.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuScreen : MonoBehaviour
{
    public const string SceneName = "MainMenu";
    private const string MapsTable = "Maps";
    private string[] maps = Array.Empty<string>();
    private MainMenuPreview preview;

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    private void Awake()
    {
        IsOpen = true;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SettingsMenu.EnsureEventSystem();
        preview = gameObject.AddComponent<MainMenuPreview>();
    }

    private void Start()
    {
        maps = GameTables.RowIds(MapsTable).Where(id => Application.CanStreamedLevelBeLoaded(id)).ToArray();
        Build();
        preview.Show();
    }

    private void OnDestroy() => IsOpen = false;

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
        AddSkinChoice(column);
        W.Size(W.Rect("Gap", column), height: 20f);
        MenuButton(column, "혼자 하기", true, StartSolo);
        MenuButton(column, "방 만들기 (준비 중)", false, null);
        MenuButton(column, "참가하기 (준비 중)", false, null);
        MenuButton(column, "설정", false, SettingsMenu.OpenFromMainMenu);
        MenuButton(column, "게임 종료", false, SettingsMenu.QuitGame);
    }

    private void AddMapChoice(Transform column)
    {
        if (maps.Length == 0) return;
        var settings = GameSettings.Current;
        var names = maps.Select(MapName).ToArray();
        int selected = Mathf.Max(0, Array.IndexOf(maps, settings.lastMap));
        settings.lastMap = maps[selected];
        W.Choice(W.Row(column, "맵"), names, selected, i => settings.lastMap = maps[i]);
    }

    private void AddSkinChoice(Transform column)
    {
        var profile = PlayerBodyProfile.Load();
        var skins = profile?.Skins.Where(skin => skin != null && skin.Model != null).ToArray();
        if (skins == null || skins.Length < 2) return;
        int selected = Mathf.Max(0, Array.FindIndex(skins, skin => skin.Model == profile.Model));
        W.Choice(W.Row(column, "캐릭터"), skins.Select(skin => skin.DisplayName).ToArray(), selected, i =>
        {
            GameSettings.Current.characterSkin = skins[i].Id;
            GameSettings.Apply();
            GameSettings.Save();
            preview.Show();
        });
    }

    private static string MapName(string id) =>
        GameTables.TryGetRow(MapsTable, id, out var row) && row.TryGetValue("displayName", out string name) ? name : id;

    private static void MenuButton(Transform column, string text, bool primary, Action clicked)
    {
        var button = W.ActionButton(column, text, primary, clicked ?? (() => { }));
        W.Size(button, height: 54f);
        button.interactable = clicked != null;
    }

    private void StartSolo()
    {
        if (maps.Length == 0) return;
        GameSettings.Save();
        SceneManager.LoadScene(GameSettings.Current.lastMap);
    }
}
