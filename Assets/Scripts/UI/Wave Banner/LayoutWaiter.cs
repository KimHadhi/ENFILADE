using UnityEngine;
using UnityEngine.UI;

public class LayoutWaiter : MonoBehaviour
{
    [SerializeField] private RectTransform layoutGroupRect;
    //유니티 븅냐링 엔진 아오~
    private void Awake()
    {
        if (layoutGroupRect == null)
            layoutGroupRect = GetComponent<RectTransform>();
    }

    private async void OnEnable()
    {
        // 1프레임 대기 (GC 할당 없음, 오브젝트 파괴 시 자동 취소)
        await Awaitable.NextFrameAsync(destroyCancellationToken);

        RebuildLayout();
    }

    public void RebuildLayout()
    {
        if (layoutGroupRect == null) return;

        Canvas.ForceUpdateCanvases();

        foreach (RectTransform child in layoutGroupRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(child);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(layoutGroupRect);
    }
}