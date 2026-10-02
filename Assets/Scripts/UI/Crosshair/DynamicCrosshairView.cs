using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class DynamicCrosshairView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image centerDot;
    [SerializeField] private RectTransform partsContainer;
    [SerializeField] private GameObject partPrefab;

    [Header("Current Active Profile")]
    [SerializeField] private CrosshairProfile currentProfile;

    private readonly List<RectTransform> spawnedPartRects = new List<RectTransform>();
    private readonly List<Image> spawnedPartImages = new List<Image>();

    private float dynamicSpread = 0f;
    private float currentContainerAngle = 0f;
    private static Sprite fallbackWhiteSprite;

    private void Awake()
    {
        EnsureFallbackSprite();
        if (currentProfile != null)
        {
            ApplyProfile(currentProfile);
        }
    }

    private void OnEnable()
    {
        EnsureFallbackSprite();
        SubscribeProfile(currentProfile);

        if (currentProfile != null)
        {
            ApplyProfile(currentProfile);
        }
    }

    private void OnDisable()
    {
        UnsubscribeProfile(currentProfile);
    }

    private void Update()
    {
        if (currentProfile != null && currentProfile.isRotating && partsContainer != null)
        {
            currentContainerAngle += currentProfile.rotationSpeed * Time.deltaTime;
            currentContainerAngle %= 360f;
            partsContainer.localEulerAngles = new Vector3(0f, 0f, currentContainerAngle);
        }

        if (currentProfile != null && currentProfile.isRainbow)
        {
            UpdateRainbowColor();
        }
    }

    private void UpdateRainbowColor()
    {
        float hue = Mathf.Repeat(Time.time * currentProfile.rainbowSpeed, 1f);
        Color pastelColor = Color.HSVToRGB(
            hue,
            currentProfile.rainbowSaturation,
            currentProfile.rainbowBrightness
        );

        if (centerDot != null && currentProfile.showDot)
        {
            centerDot.color = pastelColor;
        }

        int activeCount = currentProfile.showParts ? currentProfile.partCount : 0;
        for (int i = 0; i < activeCount && i < spawnedPartImages.Count; i++)
        {
            if (spawnedPartImages[i] != null)
            {
                spawnedPartImages[i].color = pastelColor;
            }
        }
    }

    private void SubscribeProfile(CrosshairProfile profile)
    {
        if (profile != null)
        {
            profile.OnProfileChanged -= HandleProfileChanged;
            profile.OnProfileChanged += HandleProfileChanged;
        }
    }

    private void UnsubscribeProfile(CrosshairProfile profile)
    {
        if (profile != null)
        {
            profile.OnProfileChanged -= HandleProfileChanged;
        }
    }

    private void HandleProfileChanged()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= ExecuteEditorApply;
        EditorApplication.delayCall += ExecuteEditorApply;
#else
        if (currentProfile != null) ApplyProfile(currentProfile);
#endif
    }

    private void EnsureFallbackSprite()
    {
        if (fallbackWhiteSprite == null)
        {
            Texture2D tex = Texture2D.whiteTexture;
            fallbackWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }

    // 씬에 남아있는 기존 자식들을 먼저 풀로 긁어오는 동기화 함수
    private void CollectExistingHierarchyParts()
    {
        spawnedPartRects.Clear();
        spawnedPartImages.Clear();

        if (partsContainer == null) return;

        for (int i = 0; i < partsContainer.childCount; i++)
        {
            Transform child = partsContainer.GetChild(i);
            if (child.gameObject == partPrefab) continue;

            RectTransform rt = child.GetComponent<RectTransform>();
            Image img = child.GetComponent<Image>();

            if (rt != null && img != null)
            {
                spawnedPartRects.Add(rt);
                spawnedPartImages.Add(img);
            }
        }
    }

    public void ApplyProfile(CrosshairProfile profile)
    {
        if (currentProfile != profile)
        {
            UnsubscribeProfile(currentProfile);
            currentProfile = profile;
            SubscribeProfile(currentProfile);
        }

        if (currentProfile == null) return;

        EnsureFallbackSprite();

        // 정적 모드로 올 때 회전 컨테이너 원복
        if (!profile.isRotating && partsContainer != null)
        {
            currentContainerAngle = 0f;
            partsContainer.localEulerAngles = Vector3.zero;
        }

        // 1. Center Dot 처리
        if (centerDot != null)
        {
            // dotSize가 0이면 점을 그리지 않도록 추가 처리 가능
            bool isDotVisible = profile.showDot && profile.dotSize > 0;
            centerDot.gameObject.SetActive(isDotVisible);

            if (isDotVisible)
            {
                centerDot.sprite = profile.dotSprite != null ? profile.dotSprite : fallbackWhiteSprite;
                centerDot.type = Image.Type.Simple;

                RectTransform dotRect = centerDot.rectTransform;
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);

                // [수정] 입력받은 정수값에 2를 곱해 짝수 픽셀(2, 4, 6, 8...)로 크기 지정
                float renderedSize = profile.dotSize * 2f;
                dotRect.sizeDelta = new Vector2(renderedSize, renderedSize);

                dotRect.anchoredPosition = Vector2.zero;
                dotRect.localScale = Vector3.one;

                centerDot.color = profile.color;
            }
        }

        // 2. Parts 처리
        if (partsContainer == null || partPrefab == null) return;

        // 계층 구조에 남아있는 자식 오브젝트들을 먼저 회수
        CollectExistingHierarchyParts();

        if (!profile.showParts || profile.partCount <= 0)
        {
            partsContainer.gameObject.SetActive(false);
            return;
        }

        partsContainer.gameObject.SetActive(true);

        // 계층 구조에 있던 걸 다 가져와도 모자랄 때만 신규 생성
        while (spawnedPartRects.Count < profile.partCount)
        {
            GameObject obj = Instantiate(partPrefab, partsContainer);
            RectTransform rt = obj.GetComponent<RectTransform>();
            Image img = obj.GetComponent<Image>();

            if (rt == null) rt = obj.AddComponent<RectTransform>();
            if (img == null) img = obj.AddComponent<Image>();

            spawnedPartRects.Add(rt);
            spawnedPartImages.Add(img);
        }

        Sprite partSpriteToUse = profile.partSprite != null ? profile.partSprite : fallbackWhiteSprite;

        // 필요한 개수만 켜고, 씬에 남아있던 잉여분은 즉시 비활성화
        for (int i = 0; i < spawnedPartRects.Count; i++)
        {
            RectTransform rt = spawnedPartRects[i];
            Image img = spawnedPartImages[i];

            if (rt == null) continue;

            if (i < profile.partCount)
            {
                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);

                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;
                rt.sizeDelta = profile.partSize;

                img.sprite = partSpriteToUse;
                img.type = Image.Type.Simple;
                img.color = profile.color;

                float angle = (360f / profile.partCount) * i + profile.baseAngleOffset;
                // 위아래가 반전되던 문제 수정 (-angle + 180f)
                rt.localEulerAngles = new Vector3(0f, 0f, -angle + 180f);
            }
            else
            {
                // 이전에 남아있던 4개나 초과 파츠는 여기서 전부 꺼짐
                if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
            }
        }

        UpdatePartOffsets();
    }

    public void SetDynamicSpread(float spread)
    {
        dynamicSpread = spread;
        UpdatePartOffsets();
    }

    private void UpdatePartOffsets()
    {
        if (currentProfile == null || !currentProfile.showParts || spawnedPartRects == null) return;

        float totalGap = currentProfile.baseGap + dynamicSpread;

        for (int i = 0; i < currentProfile.partCount; i++)
        {
            if (i >= spawnedPartRects.Count || spawnedPartRects[i] == null) continue;

            float angleDeg = (360f / currentProfile.partCount) * i + currentProfile.baseAngleOffset;
            float angleRad = angleDeg * Mathf.Deg2Rad;

            Vector2 dir = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad));
            spawnedPartRects[i].anchoredPosition = dir * totalGap;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SubscribeProfile(currentProfile);

        if (currentProfile == null || partsContainer == null || partPrefab == null) return;

        EditorApplication.delayCall -= ExecuteEditorApply;
        EditorApplication.delayCall += ExecuteEditorApply;
    }

    private void ExecuteEditorApply()
    {
        if (this == null || gameObject == null) return;
        ApplyProfile(currentProfile);
    }
#endif
}