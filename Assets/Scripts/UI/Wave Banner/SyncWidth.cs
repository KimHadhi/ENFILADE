using UnityEngine;

[ExecuteAlways]
public class SyncWidthWithRoot : MonoBehaviour
{
    [SerializeField] private RectTransform rootRect;
    [Tooltip("전체 너비에 더하거나 뺄 픽셀 오프셋 (양수면 더 길어지고, 음수면 더 짧아짐)")]
    [SerializeField] private float widthOffset = 0f;

    private RectTransform myRect;

    private void Awake()
    {
        myRect = GetComponent<RectTransform>();
        if (rootRect == null && transform.parent != null)
        {
            rootRect = transform.root.GetComponent<RectTransform>();
        }
    }

    private void Update()
    {
        if (rootRect != null && myRect != null)
        {
            // 루트 너비에 오프셋을 반영한 목표 너비 계산
            float targetWidth = Mathf.Max(0f, rootRect.rect.width + widthOffset);

            if (myRect.rect.width != targetWidth)
            {
                myRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
            }
        }
    }
}