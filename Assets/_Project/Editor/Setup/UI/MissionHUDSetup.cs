using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Creates editable, serialized UI in the open scene; nothing is rebuilt at runtime.</summary>
[InitializeOnLoad]
public static class MissionHUDSetup
{
    private const string Request = "Library/MissionHudInstall.request";
    private const string TuneRequest = "Library/MissionHudTune.request";
    public const string ScenePath = "Assets/_Project/Scenes/Development/CombatFoundation.unity";
    private static readonly Color Mint = new Color32(87, 231, 184, 255);
    static MissionHUDSetup() { EditorApplication.update += Process; }
    private static void Process()
    {
        if (File.Exists(TuneRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(TuneRequest);
            try { Tune(); File.WriteAllText("Library/MissionHudTune.result.txt", "PASS: bar alignment, 0/50/100 percent fill geometry and 2.75-second alert; scene saved."); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Library/MissionHudTune.result.txt", "FAILED: " + e); }
        }
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Install(); MissionHUDChecks.Run(); File.WriteAllText("Library/MissionHudInstall.result.txt", "PASS: mission model, serialized UI, wave adapter and scene saved."); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("Library/MissionHudInstall.result.txt", "FAILED: " + e); }
    }

    [MenuItem("Tools/HUD/Install Mission HUD")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before installing.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CombatFoundation first.");
        var wave = Find<WaveManager>(scene);
        var old = FindName(scene, "ObjectiveHUD");
        if (wave == null || old == null) throw new InvalidOperationException("Expected WaveManager and ObjectiveHUD.");
        var canvas = old.GetComponentInParent<Canvas>();
        if (canvas == null) throw new InvalidOperationException("ObjectiveHUD must be under a Canvas.");
        if (Find<MissionHUD>(scene) != null) throw new InvalidOperationException("Mission HUD already installed. Edit its existing objects in the scene.");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/UI/Fonts_Wave/WaveFontStandard(PretendardBold).asset");
        TMP_FontAsset alarmFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/UI/Fonts_Alarm/AlarmBarFont.asset");
        if (font == null || alarmFont == null) throw new InvalidOperationException("Mission UI fonts are missing.");
        Undo.SetCurrentGroupName("Install mission HUD");
        Undo.RecordObject(old, "Replace old wave objective UI"); old.SetActive(false);
        var root = Rect("MissionHUD", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var tracker = Undo.AddComponent<MissionTracker>(root.gameObject);
        var source = Undo.AddComponent<WaveMissionSource>(root.gameObject);
        Assign(source, "waves", wave); Assign(source, "tracker", tracker);
        var hud = Undo.AddComponent<MissionHUD>(root.gameObject);
        Assign(hud, "tracker", tracker); Assign(hud, "chat", Find<SinglePlayerChatManager>(scene));
        Assign(hud, "playerInput", Find<PlayerInput>(scene));

        var bar = Box("MissionBar", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(42, -92), new Vector2(422, -44), new Color(0, 0, 0, .73f));
        bar.pivot = new Vector2(0, .5f); bar.anchoredPosition = new Vector2(42, -68);
        var badge = Rect("ObjectiveBadge", bar, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(49, 0));
        var badgeGraphic = Undo.AddComponent<MissionBadgeGraphic>(badge.gameObject); badgeGraphic.color = new Color32(239, 133, 66, 255); badgeGraphic.raycastTarget = false;
        Label("Symbol", badge, font, "!", 32, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(-8, 0), TextAlignmentOptions.Center);
        var title = Label("Title", bar, font, "목표 대기 중", 25, Vector2.zero, Vector2.one, new Vector2(61, 4), new Vector2(-16, -4));
        title.enableAutoSizing = true; title.fontSizeMin = 18; title.fontSizeMax = 25;
        var counterRoot = Box("ProgressCounter", bar, Vector2.right, Vector2.one, new Vector2(-100, 0), Vector2.zero, new Color(0, 0, 0, .25f));
        counterRoot.pivot = new Vector2(1, .5f);
        counterRoot.offsetMin = new Vector2(-100, 0); counterRoot.offsetMax = Vector2.zero;
        Box("Divider", counterRoot, Vector2.zero, Vector2.up, new Vector2(0, 9), new Vector2(2, -9), Mint);
        var counter = Label("Count", counterRoot, font, "0 / 3", 23, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-8, 0), TextAlignmentOptions.Center);
        var track = Box("ProgressTrack", bar, Vector2.zero, Vector2.right, new Vector2(49, 0), new Vector2(0, 2), new Color(1, 1, 1, .12f));
        var fill = Box("Fill", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Mint).GetComponent<Image>();
        fill.type = Image.Type.Simple; fill.fillAmount = 0;
        fill.rectTransform.anchorMax = Vector2.up;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        var key = Box("TabKey", root, Vector2.up, Vector2.up, new Vector2(44, -129), new Vector2(86, -104), new Color(Mint.r, Mint.g, Mint.b, .13f));
        Label("Label", key, font, "TAB", 15, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center).color = Mint;
        var hint = Label("BoardHint", root, font, "미션 정보", 18, Vector2.up, Vector2.up, new Vector2(96, -129), new Vector2(330, -104));
        hint.color = new Color(1, 1, 1, .65f);
        counterRoot.gameObject.SetActive(false); track.gameObject.SetActive(false);
        Assign(hud, "missionBar", bar); Assign(hud, "title", title); Assign(hud, "counter", counter);
        Assign(hud, "counterRoot", counterRoot.gameObject); Assign(hud, "progress", fill); Assign(hud, "boardHint", hint);

        var board = Box("MissionBoard", root, Vector2.zero, Vector2.up, new Vector2(-502, 100), new Vector2(-2, -151), new Color(.018f, .024f, .027f, 1));
        board.pivot = new Vector2(0, .5f);
        var group = Undo.AddComponent<CanvasGroup>(board.gameObject); group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
        Box("Accent", board, Vector2.up, Vector2.one, Vector2.zero, new Vector2(0, 3), Mint);
        Label("Heading", board, font, "미션 보드", 18, Vector2.up, Vector2.one, new Vector2(28, -55), new Vector2(-28, -24)).color = Mint;
        var boardTitle = Label("Objective", board, font, "목표 대기 중", 30, Vector2.up, Vector2.one, new Vector2(28, -156), new Vector2(-28, -76), TextAlignmentOptions.TopLeft);
        boardTitle.textWrappingMode = TextWrappingModes.Normal; boardTitle.enableAutoSizing = true; boardTitle.fontSizeMin = 20; boardTitle.fontSizeMax = 30;
        var boardCounter = Label("Progress", board, font, "", 21, Vector2.up, Vector2.one, new Vector2(28, -199), new Vector2(-28, -164)); boardCounter.color = Mint;
        Box("Divider", board, Vector2.up, Vector2.one, new Vector2(28, -223), new Vector2(-28, -222), new Color(1, 1, 1, .16f));
        Label("DetailsHeading", board, font, "상세 목표", 17, Vector2.up, Vector2.one, new Vector2(28, -270), new Vector2(-28, -242)).color = new Color(1, 1, 1, .5f);
        var description = Label("Description", board, alarmFont, "새로운 목표가 등록되면 이곳에서 상세 정보를 확인할 수 있습니다.", 23, Vector2.zero, Vector2.one, new Vector2(28, 70), new Vector2(-28, -286), TextAlignmentOptions.TopLeft);
        description.textWrappingMode = TextWrappingModes.Normal; description.lineSpacing = 12; description.color = new Color(.85f, .88f, .88f);
        description.enableAutoSizing = true; description.fontSizeMin = 16; description.fontSizeMax = 23;
        Label("CloseHint", board, font, "TAB  닫기", 16, Vector2.zero, Vector2.right, new Vector2(28, 23), new Vector2(-28, 47)).color = new Color(1, 1, 1, .45f);
        Assign(hud, "board", board); Assign(hud, "boardGroup", group); Assign(hud, "boardTitle", boardTitle);
        Assign(hud, "boardDescription", description); Assign(hud, "boardCounter", boardCounter);

        var alert = Rect("ObjectiveAlert", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-220, -243), new Vector2(220, -147));
        var alertGroup = Undo.AddComponent<CanvasGroup>(alert.gameObject); alertGroup.alpha = 0; alertGroup.blocksRaycasts = false;
        var body = Box("Body", alert, Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2), new Color(.03f, .04f, .04f, .34f));
        var reveal = Rect("TextReveal", alert, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
        Undo.AddComponent<RectMask2D>(reveal.gameObject);
        var content = Rect("Content", reveal, new Vector2(.5f, 0), new Vector2(.5f, 1), new Vector2(-220, 0), new Vector2(220, 0));
        var label = Label("Label", content, alarmFont, "새로운 목표", 19, Vector2.up, Vector2.one, new Vector2(28, -34), new Vector2(-28, -10), TextAlignmentOptions.Center);
        label.color = new Color(1, 1, 1, .75f);
        var alertTitle = Label("Title", content, alarmFont, "남은 적을 처치하세요", 30, Vector2.zero, Vector2.one, new Vector2(28, 12), new Vector2(-28, -36), TextAlignmentOptions.Center);
        alertTitle.textWrappingMode = TextWrappingModes.Normal;
        var frame = Box("CornerFrame", alert, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1, 1, 1, .95f)).GetComponent<Image>();
        frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/UI/HUD/AlarmBar/AlarmBar_CornerFrame.png");
        frame.type = Image.Type.Sliced; frame.fillCenter = false;
        var audio = Undo.AddComponent<AudioSource>(root.gameObject); audio.playOnAwake = false; audio.spatialBlend = 0; audio.ignoreListenerPause = true;
        Assign(hud, "alert", alert); Assign(hud, "alertGroup", alertGroup); Assign(hud, "alertContent", content);
        Assign(hud, "alertTitle", alertTitle); Assign(hud, "audioSource", audio); Assign(hud, "alertSound", CreateSound());
        alert.gameObject.SetActive(false);
        // The wave banner remains connected. Only the retired objective UI is disconnected.
        Assign(wave, "objectiveText", null); Assign(wave, "enemyCountText", null); Assign(wave, "progressBar", null);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save mission HUD scene.");
        Debug.Log("MISSION_HUD_INSTALLED");
    }

    [MenuItem("Tools/HUD/Align Mission Bar and Slow Alert")]
    public static void Tune()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before tuning.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CombatFoundation first.");
        var hud = Find<MissionHUD>(scene);
        if (hud == null) throw new InvalidOperationException("Install Mission HUD first.");
        var so = new SerializedObject(hud);
        so.FindProperty("openDuration").floatValue = .45f;
        so.FindProperty("emphasisDuration").floatValue = 2f;
        so.FindProperty("closeDuration").floatValue = .30f;
        so.ApplyModifiedProperties();
        var bar = (RectTransform)so.FindProperty("missionBar").objectReferenceValue;
        var counter = ((GameObject)so.FindProperty("counterRoot").objectReferenceValue).GetComponent<RectTransform>();
        var fill = (Image)so.FindProperty("progress").objectReferenceValue;
        Undo.RecordObjects(new Object[] { bar, counter, fill, fill.rectTransform }, "Align mission progress");
        float left = bar.anchoredPosition.x - bar.rect.width * bar.pivot.x;
        bar.pivot = new Vector2(0, bar.pivot.y);
        bar.anchoredPosition = new Vector2(left, bar.anchoredPosition.y);
        counter.offsetMin = new Vector2(-counter.rect.width, 0); counter.offsetMax = Vector2.zero;
        fill.sprite = null; fill.type = Image.Type.Simple;
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(fill.fillAmount, 1);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        MissionHUDChecks.CheckLayout(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save mission HUD tuning.");
        Debug.Log("MISSION_HUD_TUNED");
    }

    private static AudioClip CreateSound()
    {
        const string path = "Assets/_Project/Art/Audio/UI/MissionObjective.wav";
        if (!File.Exists(path))
        {
            ProjectAssetPaths.EnsureFolder("Assets/_Project/Art/Audio/UI");
            const int rate = 44100, samples = 21168;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / rate;
                    double envelope = Math.Min(1, t / .008) * Math.Exp(-t * 9) * Math.Min(1, (samples - i) / 400.0);
                    double value = Math.Sin(2 * Math.PI * 880 * t) * .5 + Math.Sin(2 * Math.PI * 1320 * t) * .25;
                    writer.Write((short)(value * envelope * 18000));
                }
            }
        }
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
    public static T Find<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects()) { var item = root.GetComponentInChildren<T>(true); if (item != null) return item; }
        return null;
    }
    private static GameObject FindName(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t.gameObject;
        return null;
    }
    private static void Assign(Object target, string field, Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedProperties();
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 minAnchor, Vector2 maxAnchor, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Create mission UI");
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = minAnchor; rect.anchorMax = maxAnchor; rect.offsetMin = min; rect.offsetMax = max;
        return rect;
    }
    private static RectTransform Box(string name, Transform parent, Vector2 a, Vector2 b, Vector2 min, Vector2 max, Color color)
    {
        var rect = Rect(name, parent, a, b, min, max); var image = Undo.AddComponent<Image>(rect.gameObject); image.color = color; image.raycastTarget = false; return rect;
    }
    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value, float size, Vector2 a, Vector2 b, Vector2 min, Vector2 max, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var rect = Rect(name, parent, a, b, min, max); var text = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        text.font = font; text.text = value; text.fontSize = size; text.color = Color.white; text.richText = false;
        text.alignment = alignment; text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
        return text;
    }
}
