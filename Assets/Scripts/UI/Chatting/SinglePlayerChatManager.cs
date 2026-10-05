using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.InputSystem;

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

    [Header("Settings")]
    [SerializeField] private float messageDisplayTime = 3f;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private int maxMessages = 30;

    private bool isChatOpen = false;
    private readonly List<ChatMessageItem> messageList = new List<ChatMessageItem>(32);
    private readonly WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();
    private Coroutine scrollCoroutine;
    private Image scrollRectImage;
    private CanvasGroup scrollbarCanvasGroup;

    private void Awake()
    {
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
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
        {
            if (!isChatOpen) OpenChat();
        }
        else if (isChatOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseChat(sendText: false);
        }

        if (isChatOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!EventSystem.current.IsPointerOverGameObject())
            {
                CloseChat(sendText: false);
            }
        }
    }

    public void OpenChat()
    {
        isChatOpen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (playerInput != null)
        {
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
        chatInput.text = string.Empty;
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
        isChatOpen = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerInput != null)
        {
            playerInput.actions.FindActionMap("Player")?.Enable();
            playerInput.actions.FindActionMap("Combat")?.Enable();
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

        chatInput.DeactivateInputField();
        chatInput.gameObject.SetActive(false);

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
        if (!string.IsNullOrWhiteSpace(text))
        {
            AddMessage($"<color=#55FF55>[T]</color> <color=#EEEEEE>Player:</color> {text}");
        }

        CloseChat(sendText: true);
    }

    public void AddMessage(string formattedMessage)
    {
        GameObject newMsgObj = Instantiate(messagePrefab, messageContainer);
        ChatMessageItem item = newMsgObj.GetComponent<ChatMessageItem>();

        item.SetMessage(formattedMessage, messageDisplayTime, fadeDuration);

        if (isChatOpen)
        {
            item.SetActiveView(true);
        }

        messageList.Add(item);

        if (messageList.Count > maxMessages)
        {
            if (messageList[0] != null) Destroy(messageList[0].gameObject);
            messageList.RemoveAt(0);
        }

        RequestScrollToBottom();
    }

    private void RequestScrollToBottom()
    {
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