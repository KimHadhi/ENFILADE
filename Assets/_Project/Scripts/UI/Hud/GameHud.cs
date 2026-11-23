using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The HUD canvas of a play scene, built in code as each one loads (the user, 2026-11-23: "이거 유니티 에디터에 남아
/// 있네? 스프라이트 에셋까지 걍 지워 다이어트 해. 다 죽여"): until then it was GameHUD.prefab, placed by hand in each scene,
/// whose saved look (an orange ! tab, mint chips, a pink chat, stray crosshair parts) stood in the editor although Play
/// dressed it all over again. Now nothing of it is in a scene or a prefab; every piece makes its own parts here and dresses
/// them as before: the mission bar (MissionHUD, MissionBarLook), the chat (SinglePlayerChatManager, ChatFrame), the weapon
/// slots (WeaponSlotsHUD), the crosshair (DynamicCrosshairView). HudRig then puts it through the HUD camera and its sides
/// on their planes, as it did the prefab.
/// <para>Built in sceneLoaded, after the scene's own Awakes and before their Starts, so whatever asks for the mission
/// tracker or the chat in Start finds them (SceneOne).</para>
/// </summary>
public static class GameHud
{
    public const string Name = "★HUD(Canvas)";
    public const string LeftName = "Left Panel", RightName = "Right Panel", CenterName = "Center";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Plays(scene.name) || Object.FindAnyObjectByType<HudRig>(FindObjectsInactive.Include) != null) return;
        Build(scene);
    }

    // A map one plays in: the hub and every map the table offers or the build carries; not the title screen.
    private static bool Plays(string scene) => scene != MainMenuScreen.SceneName;

    private static void Build(Scene scene)
    {
        // Built switched off, so every piece is wired before any Awake runs, as when a prefab comes in.
        var root = new GameObject(Name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        root.AddComponent<HudRig>();

        var left = Side(LeftName, root.transform);
        MissionHUD.Build(left);
        SinglePlayerChatManager.Build(left);
        var center = Side(CenterName, root.transform);
        DynamicCrosshairView.BuildHud(center);
        Prompt(center);
        var right = Side(RightName, root.transform);
        WeaponSlotsHUD.Build(right);
        root.SetActive(true);
    }

    // A side: a 100 px box on the canvas's middle, which HudRig turns into a plane (HudPlane) and HudFrame keeps on screen.
    private static RectTransform Side(string name, Transform root)
    {
        var side = FeelUI.Rect(name, root);
        side.anchorMin = side.anchorMax = side.pivot = new Vector2(.5f, .5f);
        side.sizeDelta = new Vector2(100f, 100f);
        return side;
    }

    // The words under the crosshair the chat test stations show (PlayerTestInteraction finds them by name).
    private static void Prompt(Transform center)
    {
        var rect = FeelUI.Rect("InteractionPrompt", center);
        rect.anchoredPosition = new Vector2(0f, -100f);
        rect.sizeDelta = new Vector2(520f, 44f);
        var text = Text(rect, "HudText", 24f, TextAlignmentOptions.Center, HudTone.Text);
        text.gameObject.SetActive(false);
    }

    /// <summary>A text filling <paramref name="rect"/> in a role's face, taking no clicks.</summary>
    public static TextMeshProUGUI Text(RectTransform rect, string role, float size, TextAlignmentOptions alignment, Color colour)
    {
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        FontRoles.Apply(text, role);
        text.fontSize = size;
        text.alignment = alignment;
        text.color = colour;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>A child rect filling its parent, less <paramref name="inset"/> on each side.</summary>
    public static RectTransform Fill(string name, Transform parent, float inset = 0f)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        return rect;
    }

    /// <summary>A flat image over <paramref name="rect"/>.</summary>
    public static Image Image(RectTransform rect, Color colour, bool takesClicks = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = takesClicks;
        return image;
    }
}
