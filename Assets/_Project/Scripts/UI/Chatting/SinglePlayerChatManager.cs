using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.InputSystem;
using System;

public class SinglePlayerChatManager : MonoBehaviour
{
    [Header("Player Control")]
    [SerializeField] private PlayerInput playerInput;

    [Header("UI References")]
    [SerializeField] private CanvasGroup chatBoxBackground;
    [SerializeField] private TMP_InputField chatInput;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform messageContainer;
    [SerializeField] private GameObject messagePrefab;

    [Header("Input channel")]
    [SerializeField] private ChatChannel inputChannel = ChatChannel.Note;
    [SerializeField] private string playerDisplayName = "나";
    [SerializeField] private Image channelAccent;
    [SerializeField] private TMP_Text channelLabel;
    [SerializeField] private ChatChannelIcon channelIcon;
    [SerializeField] private Button channelButton;

    [Header("Settings")]
    [SerializeField] private float messageDisplayTime = 3f;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField, Min(1)] private int maxMessages = 300;

    private readonly List<ChatEntry> history = new List<ChatEntry>();
    private readonly ChatCommandRouter commands = new ChatCommandRouter();
    private bool commandsReady;
    private int lastClosedFrame = -1;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible, playerMapEnabled, combatMapEnabled;
    private readonly List<string> submittedInputs = new List<string>();
    private int inputHistoryIndex;
    private string pendingDraft = "";
    public IReadOnlyList<ChatEntry> History => history.AsReadOnly();
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

    private void OnEnable()
    {
        var actions = playerInput != null ? playerInput.actions : InputSystem.actions;
        cycleChannelAction = actions != null ? actions.FindAction("UI/CycleChatChannel", false) : null;
        ownsCycleChannelAction = cycleChannelAction != null && !cycleChannelAction.enabled;
        if (ownsCycleChannelAction) cycleChannelAction.Enable();
    }

    private void OnDisable()
    {
        if (isChatOpen) CloseChat(false);
        if (ownsCycleChannelAction) cycleChannelAction?.Disable();
        cycleChannelAction = null; ownsCycleChannelAction = false;
    }

    private void Awake()
    {
        EnsureCommands();
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
    }

    private void Start()
    {
        CloseChat(sendText: false);
        chatInput.onSubmit.AddListener(OnInputSubmit);
        if (channelButton != null) channelButton.onClick.AddListener(CycleInputChannel);
        RefreshInputChannel();
    }

    private void OnDestroy()
    {
        if (isChatOpen) CloseChat(false);
        if (chatInput != null) chatInput.onSubmit.RemoveListener(OnInputSubmit);
        if (channelButton != null) channelButton.onClick.RemoveListener(CycleInputChannel);
    }

    private void Update()
    {
        if (Application.isFocused && isChatOpen && cycleChannelAction != null && cycleChannelAction.WasPressedThisFrame())
            CycleInputChannel();
        if (Keyboard.current == null) return;

        if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
        {
            if (!isChatOpen && Time.frameCount != lastClosedFrame && !GamePause.IsPaused) OpenChat();
        }

        if (isChatOpen && Keyboard.current.upArrowKey.wasPressedThisFrame) RecallInput(-1);
        if (isChatOpen && Keyboard.current.downArrowKey.wasPressedThisFrame) RecallInput(1);
        if (isChatOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseChat(sendText: false);
        }

        if (isChatOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (EventSystem.current != null && !EventSystem.current.IsPointerOverGameObject())
            {
                CloseChat(sendText: false);
            }
        }
    }

    public void OpenChat()
    {
        if (isChatOpen || chatInput == null) return;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        isChatOpen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (playerInput != null)
        {
            playerMapEnabled = playerInput.actions.FindActionMap("Player")?.enabled ?? false;
            combatMapEnabled = playerInput.actions.FindActionMap("Combat")?.enabled ?? false;
            playerInput.actions.FindActionMap("Player")?.Disable();
            playerInput.actions.FindActionMap("Combat")?.Disable();
        }

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

        // 3. 스크롤바 보이기
        SetScrollbarVisible(true);

        chatInput.gameObject.SetActive(true);
        inputHistoryIndex = submittedInputs.Count;
        pendingDraft = chatInput.text;
        RefreshInputChannel();
        chatInput.ActivateInputField();

        for (int i = 0; i < messageList.Count; i++)
        {
            if (messageList[i] != null)
            {
                messageList[i].SetActiveView(true);
            }
        }

        RequestScrollToBottom();
    }

    public void CloseChat(bool sendText)
    {
        bool wasOpen = isChatOpen;
        isChatOpen = false;
        lastClosedFrame = Time.frameCount;

        if (wasOpen) { Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible; }

        if (wasOpen && playerInput != null)
        {
            if (playerMapEnabled) playerInput.actions.FindActionMap("Player")?.Enable();
            if (combatMapEnabled) playerInput.actions.FindActionMap("Combat")?.Enable();
        }

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

        for (int i = 0; i < messageList.Count; i++)
        {
            if (messageList[i] != null)
            {
                messageList[i].StartFadeCountdown(messageDisplayTime, fadeDuration);
            }
        }
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
        CloseChat(sendText: true);
    }

    public void SubmitText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 4000) text = text.Substring(0, 4000);
        submittedInputs.Add(text);
        if (submittedInputs.Count > 50) submittedInputs.RemoveAt(0);
        if (inputChannel == ChatChannel.Console || text.TrimStart().StartsWith("/"))
        {
            AddMessage(ChatChannel.Console, playerDisplayName, text);
            string result = Commands.Execute(text);
            if (!string.IsNullOrEmpty(result)) AddMessage(ChatChannel.Console, "시스템", result);
        }
        else AddMessage(ChatChannel.Note, playerDisplayName, text);
    }

    public void AddDialogue(string speaker, string text) => AddMessage(ChatChannel.Dialogue, speaker, text);
    public void AddDialogue(string speaker, string text, Color speakerColor) => AddEntry(new ChatEntry(ChatChannel.Dialogue, speaker, text, speakerColor));
    public void AddBroadcast(string text) => AddMessage(ChatChannel.Broadcast, "시스템", text);
    public void AddMessage(ChatChannel channel, string speaker, string text) => AddEntry(new ChatEntry(channel, speaker, text));

    public void AddEntry(ChatEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Body)) return;
        history.Add(entry);
        int capacity = Mathf.Max(1, maxMessages);
        if (history.Count > capacity) history.RemoveRange(0, history.Count - capacity);
        if (messagePrefab != null && messageContainer != null)
        {
            bool follow = !isChatOpen || scrollRect == null || scrollRect.verticalNormalizedPosition <= 0.03f;
            var item = Instantiate(messagePrefab, messageContainer).GetComponent<ChatMessageItem>();
            item.SetMessage(entry, messageDisplayTime, fadeDuration);
            if (isChatOpen) item.SetActiveView(true);
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

    public void AddMessage(string formattedMessage)
    {
        AddBroadcast(formattedMessage);
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
        if (channelAccent != null) channelAccent.color = color;
        if (channelLabel != null) { channelLabel.text = "[" + ChatStyle.Label(inputChannel) + "]"; channelLabel.color = color; }
        if (channelIcon != null) { channelIcon.color = color; channelIcon.SetChannel(inputChannel); }
        if (chatInput != null && chatInput.placeholder is TMP_Text placeholder)
            placeholder.text = inputChannel == ChatChannel.Console ? "/help · 명령어 입력" : "메시지 입력 · Tab 전환";
    }

    private void RecallInput(int direction)
    {
        if (submittedInputs.Count == 0) return;
        if (inputHistoryIndex == submittedInputs.Count) pendingDraft = chatInput.text;
        inputHistoryIndex = Mathf.Clamp(inputHistoryIndex + direction, 0, submittedInputs.Count);
        chatInput.text = inputHistoryIndex == submittedInputs.Count ? pendingDraft : submittedInputs[inputHistoryIndex];
        chatInput.caretPosition = chatInput.text.Length;
    }

    private void EnsureCommands()
    {
        if (commandsReady) return;
        commandsReady = true;
        PlayerConsoleCommands.Register(commands, ResolveCommandPlayer);
        CombatTestCommands.Register(commands, this);
        commands.Register("help", "명령어 목록", _ => commands.Help());
        commands.Register("clear", "현재 대화 기록 비우기", _ => { ClearHistory(); return null; });
        commands.Register("echo", "입력한 내용 출력", text => text);
        commands.Register("chat", "채팅 채널로 전환", _ => { SetInputChannel(ChatChannel.Note); return "채팅 채널로 전환했습니다."; });
        commands.Register("console", "콘솔 채널로 전환", _ => { SetInputChannel(ChatChannel.Console); return "콘솔 채널로 전환했습니다."; });
    }

    private PlayerMovement ResolveCommandPlayer()
    {
        if (playerInput != null) return playerInput.GetComponentInParent<PlayerMovement>();
        PlayerMovement local = null, any = null;
        int locals = 0, count = 0;
        foreach (var candidate in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!candidate.isActiveAndEnabled) continue;
            any = candidate; count++;
            if (candidate.gameObject.scene == gameObject.scene) { local = candidate; locals++; }
        }
        return locals == 1 ? local : locals == 0 && count == 1 ? any : null;
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
}
