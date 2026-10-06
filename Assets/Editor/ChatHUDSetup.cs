using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ChatHUDSetup
{
    private const string Request = "Library/ChatHudInstall.request";
    private static bool editReady = !Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
    static ChatHUDSetup()
    {
        EditorApplication.update += Process;
        EditorApplication.playModeStateChanged += state => editReady = state == PlayModeStateChange.EnteredEditMode;
    }
    private static void Process()
    {
        if (Application.isPlaying && Time.timeSinceLevelLoad > 1f && File.Exists("Library/ChatHudPlayCheck.request"))
        {
            File.Delete("Library/ChatHudPlayCheck.request");
            try { CheckRuntime(); File.WriteAllText("Library/ChatHudPlayCheck.result.txt", "PASS: input submit/close, retained history, restore visibility, channel button, command output, wrapping and literal formatting."); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Library/ChatHudPlayCheck.result.txt", "FAILED: " + e); }
        }
        if (!editReady || !File.Exists(Request) || Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Check(); Install(); File.WriteAllText("Library/ChatHudInstall.result.txt", "PASS: chat data, commands, literal text, history, UI setup and scene save."); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("Library/ChatHudInstall.result.txt", "FAILED: " + e); }
    }

    [MenuItem("Tools/HUD/Connect Chat Channels")]
    public static void Install()
    {
        if (!editReady || Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before setup.");
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CombatFoundation.unity");
        var manager = Find<SinglePlayerChatManager>(scene);
        if (manager == null) throw new InvalidOperationException("Open CombatFoundation with ChatManager first.");
        var so = new SerializedObject(manager);
        var input = so.FindProperty("chatInput").objectReferenceValue as TMP_InputField;
        if (input == null || input.textComponent == null) throw new InvalidOperationException("Chat input is not connected.");
        Undo.SetCurrentGroupName("Connect chat channels");
        var parent = input.transform as RectTransform;
        var badge = Rect("InputChannelBadge", parent, new Vector2(0,0), new Vector2(0,1), new Vector2(0,0), new Vector2(100,0));
        Image badgeBackground = Component<Image>(badge.gameObject);
        badgeBackground.color = new Color(1,1,1,.025f);
        Button button = Component<Button>(badge.gameObject);
        button.targetGraphic = badgeBackground;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var accent = Component<Image>(Rect("Accent", badge, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(3,0)).gameObject);
        accent.raycastTarget = false;
        var iconRect = Rect("Icon", badge, new Vector2(0,.5f), new Vector2(0,.5f), new Vector2(14,-7), new Vector2(28,7));
        Component<CanvasRenderer>(iconRect.gameObject);
        var icon = Component<ChatChannelIcon>(iconRect.gameObject); icon.raycastTarget = false;
        var labelRect = Rect("Channel", badge, Vector2.zero, Vector2.one, new Vector2(37,0), new Vector2(-5,0));
        var label = Component<TextMeshProUGUI>(labelRect.gameObject);
        label.font = input.textComponent.font; label.fontSize = 16; label.fontStyle = FontStyles.Normal;
        label.alignment = TextAlignmentOptions.MidlineLeft; label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false;
        var divider = Component<Image>(Rect("Divider", badge, Vector2.right, Vector2.one, new Vector2(-1,7), new Vector2(0,-7)).gameObject);
        divider.color = new Color(1,1,1,.12f); divider.raycastTarget = false;
        Undo.RecordObject(input.textViewport, "Reserve channel badge space");
        input.textViewport.offsetMin = new Vector2(110,4); input.textViewport.offsetMax = new Vector2(-10,-4);
        Undo.RecordObject(input, "Chat input settings");
        input.richText = false; input.characterLimit = 4000;
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.customCaretColor = true; input.caretColor = Color.white;
        if (input.targetGraphic != null) { Undo.RecordObject(input.targetGraphic, "Chat input background"); input.targetGraphic.color = new Color(.055f,.07f,.105f,.94f); }
        Undo.RecordObject(input.textComponent, "Chat input text");
        input.textComponent.color = new Color(.93f,.94f,.97f); input.textComponent.fontStyle = FontStyles.Normal;
        input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        if (input.placeholder is TMP_Text placeholder)
        {
            Undo.RecordObject(placeholder, "Chat placeholder"); placeholder.fontStyle = FontStyles.Normal;
            placeholder.color = new Color(.64f,.68f,.75f,.85f); placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        }
        so.FindProperty("channelAccent").objectReferenceValue = accent;
        so.FindProperty("channelLabel").objectReferenceValue = label;
        so.FindProperty("channelIcon").objectReferenceValue = icon;
        so.FindProperty("channelButton").objectReferenceValue = button;
        // Increase the old 30-line cap so dialogue remains useful as a session log.
        if (so.FindProperty("maxMessages").intValue == 30) so.FindProperty("maxMessages").intValue = 300;
        so.ApplyModifiedProperties(); manager.RefreshInputChannel();
        var content = so.FindProperty("messageContainer").objectReferenceValue as RectTransform;
        var layout = content != null ? content.GetComponent<VerticalLayoutGroup>() : null;
        if (layout != null)
        {
            Undo.RecordObject(layout, "Message spacing"); layout.padding = new RectOffset(10,10,10,8); layout.spacing = 5;
        }
        var scroll = so.FindProperty("scrollRect").objectReferenceValue as ScrollRect;
        if (scroll != null && scroll.TryGetComponent<Image>(out var background))
        { Undo.RecordObject(background, "Chat history background"); background.color = new Color(.065f,.08f,.12f,.76f); }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save chat scene.");
        Debug.Log("CHAT_HUD_CONNECTED: channel accent, icon, badge, chat/console input and structured history.");
    }

    [MenuItem("Tools/HUD/Run Chat Checks")]
    public static void Check()
    {
        var router = new ChatCommandRouter();
        router.Register("echo", "Echo", text => text);
        Require(router.Execute("/EcHo hello world") == "hello world", "Command arguments/case");
        Require(router.Execute("missing").Contains("/help"), "Unknown command feedback");
        router.Register("fail", "Failure", _ => throw new Exception());
        Require(router.Execute("fail").Contains("실패"), "Command error isolation");
        Require(ChatStyle.Label(ChatChannel.Note) == "채팅", "Default channel label");
        Require(!ChatStyle.Literal("</NOPARSE><size=999>x").Contains("</NOPARSE>"), "Literal text prevents formatting injection");
        var root = new GameObject("Chat checks");
        try
        {
            var manager = root.AddComponent<SinglePlayerChatManager>();
            manager.SubmitText("기록해 둘 내용");
            manager.AddDialogue("Alex", "문을 확인해 줘.", Color.green);
            manager.AddBroadcast("목표가 갱신되었습니다.");
            Require(manager.History.Count == 3 && manager.History[1].Channel == ChatChannel.Dialogue, "Separate channel data");
            Require(manager.History[1].Speaker == "Alex" && manager.History[1].SpeakerColor == Color.green, "Speaker identity/color");
            manager.SetInputChannel(ChatChannel.Console); manager.SubmitText("echo test");
            Require(manager.History[manager.History.Count - 1].Body == "test", "Console routes command");
            manager.SubmitText("/chat"); manager.SubmitText("ordinary chat");
            Require(manager.History[manager.History.Count - 1].Channel == ChatChannel.Note, "Switch back to chat");
            var so = new SerializedObject(manager); so.FindProperty("maxMessages").intValue = 2; so.ApplyModifiedProperties();
            manager.AddDialogue("NPC", "one"); manager.AddDialogue("NPC", "two"); manager.AddDialogue("NPC", "three");
            Require(manager.History.Count == 2 && manager.History[0].Body == "two", "History cap evicts oldest");
            manager.SubmitText("/clear"); Require(manager.History.Count == 0, "Clear history");
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log("CHAT_HUD_CHECKS_PASSED: structured messages, speaker colors, channels, command parsing, history cap and clear.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void CheckRuntime()
    {
        var manager = Find<SinglePlayerChatManager>(SceneManager.GetSceneByPath("Assets/Scenes/CombatFoundation.unity"));
        Require(manager != null, "Runtime chat manager");
        var so = new SerializedObject(manager);
        var input = (TMP_InputField)so.FindProperty("chatInput").objectReferenceValue;
        var button = (Button)so.FindProperty("channelButton").objectReferenceValue;
        var label = (TMP_Text)so.FindProperty("channelLabel").objectReferenceValue;
        var content = (RectTransform)so.FindProperty("messageContainer").objectReferenceValue;
        manager.OpenChat(); input.text = "입력 테스트"; input.onSubmit.Invoke(input.text);
        Require(!manager.IsChatOpen && manager.History.Count == 1 && input.text == "", "Submit closes and records");
        manager.OpenChat();
        Require(content.GetChild(0).GetComponent<TMP_Text>().alpha == 1f, "Reopen reveals history");
        button.onClick.Invoke(); Require(label.text == "[콘솔]", "Channel button switches");
        input.text = "echo 콘솔 응답 테스트"; input.onSubmit.Invoke(input.text);
        Require(manager.History[manager.History.Count - 1].Body == "콘솔 응답 테스트", "Runtime command response");
        manager.SetInputChannel(ChatChannel.Note); manager.ClearHistory(); manager.OpenChat();
        manager.AddMessage(ChatChannel.Note, "나", "북쪽 문은 나중에 다시 확인하자.");
        manager.AddDialogue("Alex", "저쪽 문은 잠겨 있어. 다른 길을 찾아보자.");
        manager.AddDialogue("Mira", "잠깐, 아래층에서 소리가 들렸어. 놓친 대사는 이 창에서 다시 확인할 수 있어.", new Color32(210,173,255,255));
        manager.AddBroadcast("새로운 목표: 통제실로 이동");
        manager.AddMessage(ChatChannel.Team, "지원", "입구를 확인했습니다.");
        manager.AddMessage(ChatChannel.All, "나", "확인했어.");
        manager.AddMessage(ChatChannel.Console, "시스템", "/help로 사용 가능한 명령어를 확인하세요.");
        Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        var items = content.GetComponentsInChildren<ChatMessageItem>();
        bool wrapped = false;
        foreach (var item in items)
        {
            var text = item.GetComponent<TMP_Text>(); text.ForceMeshUpdate();
            if (item.Entry != null && item.Entry.Speaker == "Mira") wrapped = text.textInfo.lineCount > 1;
        }
        Require(wrapped, "Long NPC dialogue wraps in real prefab");
        // Validate plain text through the actual TMP parser, independently of visible examples.
        var sample = new GameObject("Literal text check", typeof(RectTransform), typeof(TextMeshProUGUI));
        sample.transform.SetParent(content, false);
        try
        {
            var text = sample.GetComponent<TextMeshProUGUI>(); text.font = input.textComponent.font;
            text.text = ChatStyle.Literal("<size=999>literal</size>"); text.ForceMeshUpdate();
            Require(text.GetParsedText().Contains("<size=999>"), "User markup stays literal");
        }
        finally { sample.SetActive(false); Object.Destroy(sample); }
        Debug.Log("CHAT_RUNTIME_CHECKS_PASSED");
    }
    private static T Find<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects()) { var item = root.GetComponentInChildren<T>(true); if (item != null) return item; }
        return null;
    }
    private static T Component<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? Undo.AddComponent<T>(go);
    private static RectTransform Rect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 min, Vector2 max)
    {
        var rect = parent.Find(name) as RectTransform;
        if (rect == null)
        {
            var go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Create channel decoration");
            go.layer = parent.gameObject.layer; rect = go.transform as RectTransform; rect.SetParent(parent, false);
        }
        Undo.RecordObject(rect, "Channel layout"); rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.offsetMin = min; rect.offsetMax = max;
        return rect;
    }
}
