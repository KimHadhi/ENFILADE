using System.Collections;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class ChatMessageItem : MonoBehaviour
{
    private TextMeshProUGUI messageText;
    private Coroutine fadeCoroutine;

    private void Awake()
    {
        EnsureComponent();
    }

    private void EnsureComponent()
    {
        if (messageText == null)
        {
            messageText = GetComponent<TextMeshProUGUI>();
        }
    }

    // 기존처럼 3개 인자 유지
    public void SetMessage(string formattedText, float displayTime = 3f, float fadeDuration = 1f)
    {
        EnsureComponent();

        if (messageText != null)
        {
            messageText.text = formattedText;
            messageText.alpha = 1f;
        }

        StartFadeCountdown(displayTime, fadeDuration);
    }

    // 채팅창이 열려있을 땐 투명화 취소하고 100% 표시
    public void SetActiveView(bool isOpen)
    {
        if (!isOpen) return;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        EnsureComponent();
        if (messageText != null)
        {
            messageText.alpha = 1f;
        }
    }

    // 3초 대기 후 페이드아웃 실행
    public void StartFadeCountdown(float waitTime, float duration)
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        fadeCoroutine = StartCoroutine(FadeRoutine(waitTime, duration));
    }

    private IEnumerator FadeRoutine(float waitTime, float duration)
    {
        yield return new WaitForSeconds(waitTime);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (messageText != null)
            {
                messageText.alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
            }
            yield return null;
        }

        if (messageText != null)
        {
            messageText.alpha = 0f;
        }

        fadeCoroutine = null;
    }
}