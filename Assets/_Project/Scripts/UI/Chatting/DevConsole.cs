using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The developer console on ` (the key left of 1), Source-style: a panel dropping from the top of the screen with the
/// game's log (Unity's messages, warnings and errors since the game started, repeats folded into one line with a count)
/// and a command line running the same commands as the chat's console channel (GameCommands), slash optional. Typing
/// lists the matching commands with their help; Tab completes the highlighted one, Up/Down walk that list or, on an
/// empty line, the commands typed before; Enter runs; ` or Esc closes. While open the cursor is free and the player's
/// controls are off, but the game keeps running (a room never pauses). One per run, made before the first scene, kept
/// across scene loads. The log is gathered from the start, so it holds what happened before the console was first opened.
/// </summary>
[DisallowMultipleComponent]
public sealed class DevConsole : MonoBehaviour
{
    private const int MostEntries = 600, MostShown = 200, MostSuggestions = 8, MostRecalled = 50, StackLines = 2;
    private const float PanelShare = .48f, Padding = 24f, SuggestionWidth = 980f;
    private enum Kind { Log, Warning, Error, Command, Output }
    private enum Filter { All, Warnings, Errors, None }
    private sealed class Entry { public Kind kind; public string text, stack; public int count = 1; }

    private static readonly Color Panel = new Color(.03f, .035f, .045f, .95f), Warning = new Color(1f, .82f, .3f),
        Error = new Color(1f, .4f, .38f);
    private static readonly List<Entry> entries = new List<Entry>();
    private static bool dirty;
    private static Filter filter;
    private static int closedFrame = -1;

    public static bool IsOpen { get; private set; }
    /// <summary>True while the console has the keyboard and on the frame it closed, so its Esc or Enter reaches nothing else.</summary>
    public static bool TakesKeyboard => IsOpen || Time.frameCount == closedFrame;

    private readonly ChatCommandRouter commands = new ChatCommandRouter();
    private readonly List<string> recalled = new List<string>();
    private readonly List<(string name, string help)> found = new List<(string name, string help)>();
    private readonly List<TextMeshProUGUI> suggestionLabels = new List<TextMeshProUGUI>();
    private readonly StringBuilder text = new StringBuilder(16384);
    private RectTransform root, suggestionBox;
    private TextMeshProUGUI log;
    private ScrollRect scroll;
    private TMP_InputField input;
    private SinglePlayerChatManager chat;
    private int recallIndex, highlight;
    private bool recalling, refocus, settingText, caretToEnd;
    private CursorLockMode cursorLock;
    private bool cursorVisible, playerMapOn, combatMapOn;
    private PlayerInput controls;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
        closedFrame = -1;
        filter = Filter.All;
        entries.Clear();
        dirty = true;
        Application.logMessageReceived -= Gather;
        Application.logMessageReceived += Gather;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create() => DontDestroyOnLoad(new GameObject("Developer Console").AddComponent<DevConsole>().gameObject);

    private static void Gather(string message, string stack, LogType type)
    {
        Kind kind = type == LogType.Log ? Kind.Log : type == LogType.Warning ? Kind.Warning : Kind.Error;
        Add(kind, message, kind == Kind.Error ? stack : null);
    }

    private static void Add(Kind kind, string message, string stack = null)
    {
        if (string.IsNullOrEmpty(message)) return;
        var last = entries.Count > 0 ? entries[entries.Count - 1] : null;
        if (last != null && last.kind == kind && last.text == message && kind != Kind.Command && kind != Kind.Output) last.count++;
        else
        {
            entries.Add(new Entry { kind = kind, text = message, stack = Trim(stack) });
            if (entries.Count > MostEntries) entries.RemoveRange(0, entries.Count - MostEntries);
        }
        dirty = true;
    }

    // The first lines of a stack trace: where it happened, not the whole way there.
    private static string Trim(string stack)
    {
        if (string.IsNullOrWhiteSpace(stack)) return null;
        var lines = stack.Split('\n');
        var kept = new StringBuilder();
        for (int i = 0, taken = 0; i < lines.Length && taken < StackLines; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (taken > 0) kept.Append('\n');
            kept.Append(line);
            taken++;
        }
        return kept.ToString();
    }

    private void Awake()
    {
        GameCommands.Register(commands, FindChat);
        commands.Register("clear", "콘솔 기록 비우기", _ => { entries.Clear(); dirty = true; return null; });
        commands.Register("log", "all / warn / error / off — 콘솔에 보일 Unity 로그 (명령과 결과는 늘 보인다)", args =>
        {
            switch (args.Trim().ToLowerInvariant())
            {
                case "all": filter = Filter.All; break;
                case "warn": case "warning": case "warnings": filter = Filter.Warnings; break;
                case "error": case "errors": filter = Filter.Errors; break;
                case "off": case "none": filter = Filter.None; break;
                default: return "사용법: log all | warn | error | off · 지금: " + FilterName();
            }
            dirty = true;
            return "Unity 로그: " + FilterName();
        });
        commands.Register("close", "콘솔 닫기", _ => { Close(); return null; });
        Build();
        root.gameObject.SetActive(false);
    }

    private static string FilterName() => filter switch
    {
        Filter.Warnings => "경고와 오류만",
        Filter.Errors => "오류만",
        Filter.None => "끔",
        _ => "모두",
    };

    private SinglePlayerChatManager FindChat()
    {
        if (chat == null) chat = FindAnyObjectByType<SinglePlayerChatManager>();
        return chat;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100; // over the Esc menu (1000)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        gameObject.AddComponent<GraphicRaycaster>();

        root = W.Rect("Console", transform);
        root.anchorMin = new Vector2(0f, 1f - PanelShare); root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        W.Fill(root, Panel);
        var edge = W.Rect("Edge", root);
        edge.anchorMin = Vector2.zero; edge.anchorMax = new Vector2(1f, 0f); edge.pivot = new Vector2(.5f, 1f);
        edge.sizeDelta = new Vector2(0f, 2f);
        W.Fill(edge, W.Accent);

        var body = W.Rect("Body", root);
        W.Stretch(body);
        var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)Padding, (int)Padding, 14, 14);
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

        var header = W.Rect("Header", body);
        W.Size(header, height: 26f, flexibleHeight: 0f);
        var title = W.Label(header, "개발자 콘솔", 19f, W.Accent);
        W.Stretch(title.rectTransform);
        var hint = W.Label(header, "` 또는 Esc 닫기 · Tab 자동 완성 · 위/아래 고르기·이전 명령 · help 명령어 목록 · log 로그 거르기",
            15f, W.Muted, TextAlignmentOptions.MidlineRight);
        W.Stretch(hint.rectTransform);

        var content = W.ScrollArea(body);
        scroll = content.GetComponentInParent<ScrollRect>();
        log = W.Label(content, "", 17f, W.Text, TextAlignmentOptions.TopLeft);
        log.textWrappingMode = TextWrappingModes.Normal;
        log.richText = true;

        var line = W.Rect("Line", body);
        W.Size(line, height: 36f, flexibleHeight: 0f);
        var row = line.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 10f; row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = false; row.childForceExpandHeight = true;
        var prompt = W.Label(line, ">", 21f, W.Accent, TextAlignmentOptions.Center);
        prompt.richText = false;
        W.Size(prompt, width: 18f);
        input = W.TextInput(line, "", Edited);
        input.richText = false;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.navigation = new Navigation { mode = Navigation.Mode.None }; // Up/Down are the console's, not the menu's
        input.onValidateInput = (_, _, c) => c == '`' || c == '~' || c == '\t' ? '\0' : c; // the toggle key and Tab
        input.onSubmit.AddListener(Run);

        suggestionBox = W.Rect("Suggestions", root);
        suggestionBox.anchorMin = suggestionBox.anchorMax = Vector2.zero;
        suggestionBox.pivot = new Vector2(0f, 1f);
        suggestionBox.anchoredPosition = new Vector2(Padding + 28f, -2f);
        suggestionBox.sizeDelta = new Vector2(SuggestionWidth, 0f);
        W.Fill(suggestionBox, new Color(.07f, .08f, .1f, .97f));
        var list = suggestionBox.gameObject.AddComponent<VerticalLayoutGroup>();
        list.padding = new RectOffset(12, 12, 8, 8);
        list.spacing = 2f;
        list.childControlWidth = list.childControlHeight = true;
        list.childForceExpandWidth = true; list.childForceExpandHeight = false;
        suggestionBox.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        for (int i = 0; i < MostSuggestions; i++)
        {
            var label = W.Label(suggestionBox, "", 17f, W.Muted);
            W.Size(label, height: 26f);
            suggestionLabels.Add(label);
        }
        suggestionBox.gameObject.SetActive(false);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.backquoteKey.wasPressedThisFrame)
        {
            if (IsOpen) Close();
            else if (!SomethingElseTyping()) Open();
            return;
        }
        if (!IsOpen) return;
        if (keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (keyboard.tabKey.wasPressedThisFrame) Complete();
        if (keyboard.upArrowKey.wasPressedThisFrame) Walk(-1);
        if (keyboard.downArrowKey.wasPressedThisFrame) Walk(1);
    }

    private void LateUpdate()
    {
        if (!IsOpen) return;
        if (refocus && input != null)
        {
            refocus = false;
            input.ActivateInputField();
        }
        // After the field's own key handling, which moves the caret on Up/Down.
        if (caretToEnd && input != null)
        {
            caretToEnd = false;
            input.caretPosition = input.stringPosition = input.text.Length;
        }
        if (dirty) Redraw();
    }

    // The chat or a menu's text box already has the keyboard: ` is a letter there.
    private bool SomethingElseTyping()
    {
        var chatNow = FindChat();
        if (chatNow != null && chatNow.IsChatOpen) return true;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return false;
        if (selected.TryGetComponent(out TMP_InputField field) && field.isFocused) return true;
        return selected.TryGetComponent(out InputField legacy) && legacy.isFocused;
    }

    private void Open()
    {
        IsOpen = true;
        cursorLock = Cursor.lockState;
        cursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        controls = PlayerRoster.Local != null ? PlayerRoster.Local.GetComponent<PlayerInput>() : null;
        if (controls != null)
        {
            var player = controls.actions.FindActionMap("Player");
            var combat = controls.actions.FindActionMap("Combat");
            playerMapOn = player != null && player.enabled;
            combatMapOn = combat != null && combat.enabled;
            player?.Disable();
            combat?.Disable();
        }
        SettingsMenu.EnsureEventSystem();
        root.gameObject.SetActive(true);
        SetInput("");
        recallIndex = recalled.Count;
        recalling = false;
        refocus = true;
        dirty = true;
    }

    private void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        closedFrame = Time.frameCount;
        var events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null
            && events.currentSelectedGameObject.transform.IsChildOf(root)) events.SetSelectedGameObject(null);
        input.DeactivateInputField();
        root.gameObject.SetActive(false);
        Cursor.lockState = cursorLock;
        Cursor.visible = cursorVisible;
        if (controls != null)
        {
            if (playerMapOn) controls.actions.FindActionMap("Player")?.Enable();
            if (combatMapOn) controls.actions.FindActionMap("Combat")?.Enable();
        }
        controls = null;
    }

    private void Run(string line)
    {
        if (!IsOpen) return;
        refocus = true;
        if (string.IsNullOrWhiteSpace(line)) return;
        line = line.Trim();
        recalled.Remove(line);
        recalled.Add(line);
        if (recalled.Count > MostRecalled) recalled.RemoveAt(0);
        recallIndex = recalled.Count;
        recalling = false;
        SetInput("");
        Add(Kind.Command, "> " + line);
        string result = commands.Execute(line);
        if (!string.IsNullOrEmpty(result)) Add(Kind.Output, result);
        scroll.verticalNormalizedPosition = 0f; // a command always shows its answer
    }

    private void Edited(string value)
    {
        if (settingText) return;
        recalling = false;
        recallIndex = recalled.Count;
        Suggest(value);
    }

    // Only while typing the command's name: once its arguments start, the list would only be in the way.
    private void Suggest(string value)
    {
        string typed = value ?? "";
        bool naming = !recalling && typed.Trim().Length > 0 && typed.TrimStart().IndexOf(' ') < 0;
        if (naming) commands.Find(typed, found, MostSuggestions);
        else found.Clear();
        highlight = 0;
        ShowSuggestions();
    }

    private void ShowSuggestions()
    {
        suggestionBox.gameObject.SetActive(found.Count > 0);
        for (int i = 0; i < suggestionLabels.Count; i++)
        {
            var label = suggestionLabels[i];
            bool used = i < found.Count;
            label.gameObject.SetActive(used);
            if (!used) continue;
            bool lit = i == highlight;
            string name = Hex(lit ? W.Accent : W.Text);
            label.text = $"<color=#{name}>{found[i].name}</color>  <color=#{Hex(W.Muted)}><noparse>{Escape(found[i].help)}</noparse></color>";
            label.fontStyle = lit ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    private void Complete()
    {
        if (found.Count == 0) return;
        SetInput(found[Mathf.Clamp(highlight, 0, found.Count - 1)].name + " ");
        found.Clear();
        ShowSuggestions();
    }

    // With suggestions showing, Up/Down pick among them; otherwise they walk the commands typed before.
    private void Walk(int by)
    {
        if (found.Count > 0 && !recalling)
        {
            highlight = (highlight + by + found.Count) % found.Count;
            ShowSuggestions();
            return;
        }
        if (recalled.Count == 0) return;
        recalling = true;
        recallIndex = Mathf.Clamp(recallIndex + by, 0, recalled.Count);
        SetInput(recallIndex < recalled.Count ? recalled[recallIndex] : "");
        found.Clear();
        ShowSuggestions();
    }

    private void SetInput(string value)
    {
        settingText = true;
        input.text = value;
        input.caretPosition = input.stringPosition = value.Length;
        settingText = false;
        caretToEnd = true;
    }

    private void Redraw()
    {
        dirty = false;
        bool following = scroll.verticalNormalizedPosition <= .02f || log.text.Length == 0;
        text.Clear();
        int shown = 0, first = entries.Count;
        while (first > 0 && shown < MostShown)
        {
            first--;
            if (Shows(entries[first].kind)) shown++;
        }
        for (int i = first; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!Shows(entry.kind)) continue;
            if (text.Length > 0) text.Append('\n');
            Append(entry);
        }
        if (text.Length == 0) text.Append($"<color=#{Hex(W.Muted)}>help 를 입력하면 명령어 목록이 나옵니다.</color>");
        log.text = text.ToString();
        if (!following) return;
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;
    }

    private static bool Shows(Kind kind) => kind switch
    {
        Kind.Command or Kind.Output => true,
        Kind.Log => filter == Filter.All,
        Kind.Warning => filter == Filter.All || filter == Filter.Warnings,
        _ => filter != Filter.None,
    };

    private void Append(Entry entry)
    {
        Color color = entry.kind switch
        {
            Kind.Warning => Warning,
            Kind.Error => Error,
            Kind.Command => W.Accent,
            Kind.Log => W.Muted,
            _ => W.Text,
        };
        text.Append("<color=#").Append(Hex(color)).Append("><noparse>").Append(Escape(entry.text)).Append("</noparse>");
        if (entry.count > 1) text.Append("  <b>×").Append(entry.count).Append("</b>");
        text.Append("</color>");
        if (entry.stack != null)
            text.Append("\n<size=80%><color=#").Append(Hex(W.Muted)).Append("><noparse>").Append(Escape(entry.stack))
                .Append("</noparse></color></size>");
    }

    // Inside <noparse> only its own closing tag could end it early.
    private static string Escape(string value) => value.Replace("</noparse>", "</ noparse>");

    private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

    private void OnDestroy()
    {
        if (IsOpen) Close();
    }
}
