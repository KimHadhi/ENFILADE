using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using System;

/// <summary>
/// The chat on GameHUD's left plane: Enter opens it, Enter sends, Esc closes, Tab (the key window's 채팅 채널) swaps chat
/// and console, up and down recall what was sent. Since the 2026-11-12 redesign each line is drawn on its own plate in
/// code (ChatMessageItem; the prefab line now only lends its font), the box is placed and dressed by ChatFrame, and the
/// numbers are Tables/Chat.csv (ChatLook).
/// </summary>
public class SinglePlayerChatManager : MonoBehaviour
{
    [Header("Player Control")]
    [SerializeField] private PlayerInput playerInput;

    [Header("UI References")]
    [SerializeField] private CanvasGroup chatBoxBackground;
    [SerializeField] private TMP_InputField chatInput;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform messageContainer;
    [Tooltip("The old one-text line. Lines are built in code now (ChatMessageItem); its font is the chat's font.")]
    [SerializeField] private GameObject messagePrefab;

    [Header("Input channel")]
    [SerializeField] private ChatChannel inputChannel = ChatChannel.Note;
    [SerializeField] private Image channelAccent;
    [SerializeField] private TMP_Text channelLabel;
    [SerializeField] private ChatChannelIcon channelIcon;
    [SerializeField] private Button channelButton;

    [Header("Settings")]
    [SerializeField, Min(1)] private int maxMessages = 300;

    private readonly List<ChatEntry> history = new List<ChatEntry>();
    private readonly ChatCommandRouter commands = new ChatCommandRouter();
    private readonly ChatLook look = new ChatLook();
    private ChatFrame frame;
    private TMP_FontAsset lineFont;
    private bool commandsReady;
    private int lastClosedFrame = -1;
    private readonly List<string> submittedInputs = new List<string>();
    private int inputHistoryIndex;
    private string pendingDraft = "";
    public bool IsChatOpen => isChatOpen;
    public bool BlocksMissionInputThisFrame => isChatOpen || Time.frameCount == lastClosedFrame;
    public event Action<ChatEntry> MessageAdded;
    public ChatCommandRouter Commands { get { EnsureCommands(); return commands; } }

    private bool isChatOpen = false;
    private readonly List<ChatMessageItem> messageList = new List<ChatMessageItem>(32);
    private readonly WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();
    private Coroutine scrollCoroutine;
    private Image scrollRectImage;
    private CanvasGroup scrollbarCanvasGroup;
    private InputAction cycleChannelAction;
    private bool ownsCycleChannelAction;

    // The player is spawned at runtime, so the link is made once there is one.
    private PlayerInput Controls
    {
        get
        {
            if (playerInput == null && PlayerRoster.Local != null) playerInput = PlayerRoster.Local.GetComponent<PlayerInput>();
            return playerInput;
        }
    }

    private void OnEnable()
    {
        PlayerChat.Heard += OnHeard;
        GameTables.Reloaded += OnTablesReloaded;
        var actions = Controls != null ? Controls.actions : InputSystem.actions;
        cycleChannelAction = actions != null ? actions.FindAction("UI/CycleChatChannel", false) : null;
        ownsCycleChannelAction = cycleChannelAction != null && !cycleChannelAction.enabled;
        if (ownsCycleChannelAction) cycleChannelAction.Enable();
    }

    private void OnDisable()
    {
        PlayerChat.Heard -= OnHeard;
        GameTables.Reloaded -= OnTablesReloaded;
        if (isChatOpen) CloseChat();
        if (ownsCycleChannelAction) cycleChannelAction?.Disable();
        cycleChannelAction = null; ownsCycleChannelAction = false;
    }

    private void Awake()
    {
        EnsureCommands();
        look.Bind(this);
        lineFont = LineFont();
        if (scrollRect != null)
        {
            scrollRectImage = scrollRect.GetComponent<Image>();

            // 스크롤바의 CanvasGroup 자동 캐싱 (없으면 코드로 자동 추가)
            if (scrollRect.verticalScrollbar != null)
            {
                scrollbarCanvasGroup = scrollRect.verticalScrollbar.GetComponent<CanvasGroup>();
                if (scrollbarCanvasGroup == null)
                {
                    scrollbarCanvasGroup = scrollRect.verticalScrollbar.gameObject.AddComponent<CanvasGroup>();
                }
            }
        }
        frame = new ChatFrame((RectTransform)transform, scrollRect, chatInput, channelButton);
        frame.Dress(look);
        // The tab is coloured whole now; the prefab's thin accent line on it is the old badge's.
        if (channelAccent != null) channelAccent.enabled = false;
    }

    private void Start()
    {
        CloseChat();
        chatInput.onSubmit.AddListener(OnInputSubmit);
        if (channelButton != null) channelButton.onClick.AddListener(CycleInputChannel);
        RefreshInputChannel();
    }

    private void OnDestroy()
    {
        if (isChatOpen) CloseChat();
        if (chatInput != null) chatInput.onSubmit.RemoveListener(OnInputSubmit);
        if (channelButton != null) channelButton.onClick.RemoveListener(CycleInputChannel);
    }

    private void Update()
    {
        if (Application.isFocused && isChatOpen && cycleChannelAction != null && cycleChannelAction.WasPressedThisFrame())
            CycleInputChannel();
        if (Keyboard.current == null) return;

        // UI/OpenChat (Enter and the number pad's Enter unless the key window says otherwise). An open chat's own Enter
        // sends from the input field, whatever this key is; another text box's Enter is that box's (OtherBoxTyping).
        bool otherBox = OtherBoxTyping();
        if (KeyBindings.Pressed("UI/OpenChat"))
        {
            if (!isChatOpen && Time.frameCount != lastClosedFrame && !GamePause.IsPaused && !DevConsole.TakesKeyboard && !KeyBindings.Busy
                && !otherBox) OpenChat();
        }

        if (isChatOpen && Keyboard.current.upArrowKey.wasPressedThisFrame) RecallInput(-1);
        if (isChatOpen && Keyboard.current.downArrowKey.wasPressedThisFrame) RecallInput(1);
        if (isChatOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseChat();
        }

        if (isChatOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (EventSystem.current != null && !EventSystem.current.IsPointerOverGameObject())
            {
                CloseChat();
            }
        }
    }

    // After the HUD's scripts moved this frame: the box's place for this screen, then every line's pop and fade.
    private void LateUpdate()
    {
        frame?.Place(look);
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        int shownWhileClosed = Mathf.Max(0, look.Fade.lines);
        for (int i = messageList.Count - 1, rank = 0; i >= 0; i--, rank++)
        {
            var line = messageList[i];
            if (line == null) continue;
            bool moving = line.Tick(now, dt, rank, isChatOpen);
            // While closed, past the lines shown, the first line with nothing left to do has only gone ones above it.
            if (!moving && !isChatOpen && rank >= shownWhileClosed) break;
        }
    }

    // Another text box than the chat's own has the keyboard (a preset's name at the hub's mirror), or had it last frame:
    // its Enter saves there and mustn't open the chat too. TMP lets go of the focus while it handles that Enter, which may
    // come before or after this Update in the frame, so the frame it was last seen focused counts as well.
    private int otherBoxFrame = -10;

    private bool OtherBoxTyping()
    {
        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        if (selected != null && (chatInput == null || !selected.transform.IsChildOf(chatInput.transform))
            && selected.TryGetComponent(out TMP_InputField field) && field.isFocused)
            otherBoxFrame = Time.frameCount;
        return Time.frameCount - otherBoxFrame <= 1;
    }

    public void OpenChat()
    {
        if (isChatOpen || chatInput == null) return;
        isChatOpen = true;
        InputHold.Take(this);

        // 1. 전체 배경 켜기
        if (chatBoxBackground != null)
        {
            chatBoxBackground.alpha = 1f;
            chatBoxBackground.blocksRaycasts = true;
            chatBoxBackground.interactable = true;
        }

        // 2. Scroll View 배경 이미지 켜기
        if (scrollRectImage != null)
        {
            scrollRectImage.enabled = true;
        }
        frame?.ShowBox(true);

        // 3. 스크롤바 보이기
        SetScrollbarVisible(true);

        chatInput.gameObject.SetActive(true);
        inputHistoryIndex = submittedInputs.Count;
        pendingDraft = chatInput.text;
        RefreshInputChannel();
        chatInput.ActivateInputField();

        // Every line comes back while typing (ChatMessageItem.Tick sees the chat open).
        RequestScrollToBottom();
    }

    public void CloseChat()
    {
        bool wasOpen = isChatOpen;
        isChatOpen = false;
        lastClosedFrame = Time.frameCount;

        if (wasOpen) InputHold.Release(this);

        // 1. 전체 배경 끄기
        if (chatBoxBackground != null)
        {
            chatBoxBackground.alpha = 0f;
            chatBoxBackground.blocksRaycasts = false;
            chatBoxBackground.interactable = false;
        }

        // 2. Scroll View 배경 이미지 끄기
        if (scrollRectImage != null)
        {
            scrollRectImage.enabled = false;
        }
        frame?.ShowBox(false);

        // 3. 스크롤바 완전 투명화 및 클릭 차단
        SetScrollbarVisible(false);

        if (chatInput != null)
        {
            // TMP can remain selected after hiding, especially after reactivation on channel change.
            // Clear only this chat's selection, leaving other UI owners alone.
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(chatInput.transform))
                events.SetSelectedGameObject(null);
            chatInput.DeactivateInputField();
            chatInput.gameObject.SetActive(false);
        }

        // The lines that were read stay a moment longer, then go as their time says; the newest are back at the bottom
        // even if the history was scrolled up.
        float now = Time.unscaledTime;
        for (int i = 0; i < messageList.Count; i++)
            if (messageList[i] != null) messageList[i].Linger(now);
        RequestScrollToBottom();
    }

    private void SetScrollbarVisible(bool isVisible)
    {
        if (scrollbarCanvasGroup != null)
        {
            scrollbarCanvasGroup.alpha = isVisible ? 1f : 0f;
            scrollbarCanvasGroup.blocksRaycasts = isVisible;
            scrollbarCanvasGroup.interactable = isVisible;
        }
    }

    private void OnInputSubmit(string text)
    {
        if (!isChatOpen) return;
        SubmitText(text);
        chatInput.text = string.Empty;
        CloseChat();
    }

    public void SubmitText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 4000) text = text.Substring(0, 4000);
        submittedInputs.Add(text);
        if (submittedInputs.Count > 50) submittedInputs.RemoveAt(0);
        string speaker = GameSettings.Current.DisplayName;
        var me = PlayerRoster.Local;
        if (inputChannel == ChatChannel.Console || text.TrimStart().StartsWith("/"))
        {
            // What was typed is this player's (a pink chevron); the answer is the console's.
            AddEntry(new ChatEntry(ChatChannel.Console, speaker, text, null, me));
            string result = Commands.Execute(text);
            if (!string.IsNullOrEmpty(result)) AddMessage(ChatChannel.Console, Texts.Get("chat.system"), result);
            return;
        }
        AddEntry(new ChatEntry(ChatChannel.Note, speaker, text, null, me));
        if (me != null && me.TryGetComponent(out PlayerChat chat)) chat.Say(text);
    }

    // A friend's words carry their player, so the line shows their picture and seat colour.
    private void OnHeard(PlayerChat from, string speaker, string text)
        => AddEntry(new ChatEntry(ChatChannel.Note, speaker, text, null, from != null ? from.GetComponent<PlayerMovement>() : null));

    public void AddDialogue(string speaker, string text, Color speakerColor) => AddEntry(new ChatEntry(ChatChannel.Dialogue, speaker, text, speakerColor));
    public void AddBroadcast(string text) => AddMessage(ChatChannel.Broadcast, Texts.Get("chat.system"), text);
    public void AddMessage(ChatChannel channel, string speaker, string text) => AddEntry(new ChatEntry(channel, speaker, text));

    public void AddEntry(ChatEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Body)) return;
        history.Add(entry);
        int capacity = Mathf.Max(1, maxMessages);
        if (history.Count > capacity) history.RemoveRange(0, history.Count - capacity);
        if (messageContainer != null)
        {
            bool follow = !isChatOpen || scrollRect == null || scrollRect.verticalNormalizedPosition <= 0.03f;
            var item = ChatMessageItem.Create(messageContainer, lineFont, look);
            item.Show(entry, ChatFrame.LineWidth(look), Time.unscaledTime);
            messageList.Add(item);
            while (messageList.Count > capacity)
            {
                if (messageList[0] != null) { messageList[0].gameObject.SetActive(false); Destroy(messageList[0].gameObject); }
                messageList.RemoveAt(0);
            }
            if (follow) RequestScrollToBottom();
        }
        MessageAdded?.Invoke(entry);
    }

    public void ClearHistory()
    {
        history.Clear();
        foreach (var item in messageList)
            if (item != null) { item.gameObject.SetActive(false); Destroy(item.gameObject); }
        messageList.Clear();
    }

    public void SetInputChannel(ChatChannel channel)
    {
        inputChannel = channel == ChatChannel.Console ? ChatChannel.Console : ChatChannel.Note;
        RefreshInputChannel();
    }

    public void CycleInputChannel()
    {
        SetInputChannel(inputChannel == ChatChannel.Note ? ChatChannel.Console : ChatChannel.Note);
        if (isChatOpen && chatInput != null) chatInput.ActivateInputField();
    }

    public void RefreshInputChannel()
    {
        Color color = ChatStyle.ColorFor(inputChannel);
        if (channelLabel != null) channelLabel.text = ChatStyle.Label(inputChannel);
        if (channelIcon != null) channelIcon.SetChannel(inputChannel);
        frame?.PaintTab(color, channelLabel, channelIcon);
        if (chatInput != null && chatInput.placeholder is TMP_Text placeholder)
        {
            // The hint names the key the key window has on 채팅 채널, not a fixed Tab.
            string key = KeyBindings.Name("UI/CycleChatChannel");
            if (string.IsNullOrEmpty(key) || key == "?") key = "Tab";
            placeholder.text = Texts.Format(inputChannel == ChatChannel.Console ? "chat.placeholder.console" : "chat.placeholder.chat", key);
        }
    }

    private void RecallInput(int direction)
    {
        if (submittedInputs.Count == 0) return;
        if (inputHistoryIndex == submittedInputs.Count) pendingDraft = chatInput.text;
        inputHistoryIndex = Mathf.Clamp(inputHistoryIndex + direction, 0, submittedInputs.Count);
        chatInput.text = inputHistoryIndex == submittedInputs.Count ? pendingDraft : submittedInputs[inputHistoryIndex];
        chatInput.caretPosition = chatInput.text.Length;
    }

    // Chat.csv was saved: the box takes the new numbers at once, lines from the next one on.
    private void OnTablesReloaded()
    {
        frame?.Dress(look);
        RefreshInputChannel();
    }

    // The chat's own font, the one its prefab line carried (ChattingFont): the lines are built in code now, in the same type.
    private TMP_FontAsset LineFont()
    {
        if (messagePrefab != null && messagePrefab.TryGetComponent(out TMP_Text text) && text.font != null) return text.font;
        if (chatInput != null && chatInput.textComponent != null && chatInput.textComponent.font != null) return chatInput.textComponent.font;
        return CombatUIAssets.ResolveFont(null);
    }

    private void EnsureCommands()
    {
        if (commandsReady) return;
        commandsReady = true;
        GameCommands.Register(commands, () => this);
        commands.Register("clear", "현재 대화 기록 비우기", _ => { ClearHistory(); return null; });
        commands.Register("chat", "채팅 채널로 전환", _ => { SetInputChannel(ChatChannel.Note); return Texts.Get("chat.switched.chat"); });
        commands.Register("console", "콘솔 채널로 전환", _ => { SetInputChannel(ChatChannel.Console); return Texts.Get("chat.switched.console"); });
    }

    private void RequestScrollToBottom()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || messageContainer == null || scrollRect == null) return;
        if (scrollCoroutine != null)
        {
            StopCoroutine(scrollCoroutine);
        }
        scrollCoroutine = StartCoroutine(ScrollToBottomRoutine());
    }

    private IEnumerator ScrollToBottomRoutine()
    {
        yield return waitForEndOfFrame;

        LayoutRebuilder.ForceRebuildLayoutImmediate(messageContainer);
        Canvas.ForceUpdateCanvases();

        scrollRect.verticalNormalizedPosition = 0f;
        scrollCoroutine = null;
    }
    public static class SceneConsoleCommands
    {
        public static string LoadMap(string args)
        {
            string sceneName = args.Trim();

            if (string.IsNullOrWhiteSpace(sceneName))
                return "사용법: /map 씬이름";

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
                return $"씬을 찾을 수 없습니다: {sceneName}";

            if (GameSession.IsOnline)
                return "방에 있는 동안에는 맵을 바꿀 수 없습니다.";

            SceneLoad.Go(sceneName);

            return $"씬 이동 중: {sceneName}";
        }
    }
}
