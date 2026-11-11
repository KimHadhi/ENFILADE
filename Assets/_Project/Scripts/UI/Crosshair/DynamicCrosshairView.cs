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

    private CrosshairProfile baseProfile;
    private float dynamicSpread = 0f;
    private float fireKick;
    private bool combatHidden;
    private bool savedDotActive, savedPartsActive;
    private float currentContainerAngle = 0f;
    private bool isPreview;
    private static Sprite fallbackWhiteSprite;
    // The player's own picture (CrosshairProfile.image): made the first time one shows, under the dot and the parts.
    private Image picture;

    /// <summary>Spin and rainbow on real time, so they keep moving while the game is paused (the settings preview).</summary>
    public bool UnscaledTime { get; set; }

    /// <summary>Ignores weapon spread and shot kicks, so the parts never move: the player's steady crosshair setting.</summary>
    public bool HoldShape
    {
        get => holdShape;
        set
        {
            if (holdShape == value) return;
            holdShape = value;
            fireKick = 0f;
            UpdatePartOffsets();
        }
    }
    private bool holdShape;

    /// <summary>The HUD crosshair in the scene. The settings preview never counts.</summary>
    public static DynamicCrosshairView FindHud()
    {
        foreach (var view in FindObjectsByType<DynamicCrosshairView>(FindObjectsSortMode.None))
            if (!view.isPreview) return view;
        return null;
    }

    /// <summary>Wires a crosshair built in code for the settings preview. The template is copied for each part.</summary>
    public void ConfigurePreview(Image dot, RectTransform parts, GameObject partTemplate)
    {
        centerDot = dot;
        partsContainer = parts;
        partPrefab = partTemplate;
        isPreview = true;
        UnscaledTime = true;
    }

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
            currentContainerAngle += currentProfile.rotationSpeed * (UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
            currentContainerAngle %= 360f;
            partsContainer.localEulerAngles = new Vector3(0f, 0f, currentContainerAngle);
            TurnPicture();
        }

        if (currentProfile != null && currentProfile.isRainbow)
        {
            UpdateRainbowColor();
        }

        if (fireKick > 0f)
        {
            // Snap open on the shot, close in ~0.15 s; real spread (bloom) stays in dynamicSpread.
            fireKick *= Mathf.Exp(-22f * Time.unscaledDeltaTime);
            if (fireKick < .05f) fireKick = 0f;
            UpdatePartOffsets();
        }
    }

    /// <summary>Momentary extra gap per shot, in canvas pixels. Presentation only.</summary>
    public void Kick(float pixels)
    {
        if (holdShape) return;
        fireKick = Mathf.Min(fireKick + Mathf.Max(0f, pixels), 26f);
        UpdatePartOffsets();
    }

    /// <summary>Shows this profile until the next call; null goes back to the one set on this view.</summary>
    public void UseProfile(CrosshairProfile profile)
    {
        if (baseProfile == null) baseProfile = currentProfile;
        ApplyProfile(profile != null ? profile : baseProfile);
    }

    private Color RainbowColor()
    {
        float hue = Mathf.Repeat((UnscaledTime ? Time.unscaledTime : Time.time) * currentProfile.rainbowSpeed, 1f);
        Color pastelColor = Color.HSVToRGB(
            hue,
            currentProfile.rainbowSaturation,
            currentProfile.rainbowBrightness
        );
        pastelColor.a = currentProfile.color.a; // 무지개여도 프로필의 불투명도는 지킨다
        return pastelColor;
    }

    private void UpdateRainbowColor()
    {
        Color pastelColor = RainbowColor();
        if (ring != null && ring.gameObject.activeSelf) UpdateRing();

        if (centerDot != null && currentProfile.showDot)
        {
            centerDot.color = pastelColor;
        }
        if (picture != null && picture.gameObject.activeSelf && currentProfile.imageTinted) picture.color = pastelColor;

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

    // Bars and discs get an even border; a sprite with a shape of its own keeps Unity's shifted copies, which follow it.
    private static void ApplyOutline(Image image, CrosshairProfile profile)
    {
        bool wanted = profile.outlineWidth > 0f;
        bool plain = image.sprite == fallbackWhiteSprite || CrosshairShapes.IsCircle(image.sprite);
        var border = image.GetComponent<CrosshairOutline>();
        var shifted = image.GetComponent<Outline>();
        if (wanted && plain && border == null) border = image.gameObject.AddComponent<CrosshairOutline>();
        if (wanted && !plain && shifted == null) shifted = image.gameObject.AddComponent<Outline>();
        if (border != null)
        {
            border.enabled = wanted && plain;
            border.Set(profile.outlineWidth, profile.outlineColor);
        }
        if (shifted != null)
        {
            shifted.enabled = wanted && !plain;
            shifted.effectColor = profile.outlineColor;
            shifted.effectDistance = new Vector2(profile.outlineWidth, profile.outlineWidth);
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
        ApplyParts(profile);
        // On every way out of the above: the picture, then the ring, which takes the lines' place while it shows.
        ApplyPicture();
        UpdateRing();
    }

    /// <summary>
    /// The player's own picture at the middle (CrosshairImages): its longer side imageSize pixels, turned with the shape's
    /// angle and spin, in its own colours or tinted with the crosshair's, at the colour's opacity either way.
    /// </summary>
    private void ApplyPicture()
    {
        var profile = currentProfile;
        bool show = !combatHidden && profile != null && profile.image != null;
        if (picture == null && !show) return;
        if (picture == null)
        {
            var rect = (RectTransform)new GameObject("Picture", typeof(RectTransform)).transform;
            rect.gameObject.hideFlags = HideFlags.DontSave; // never kept in a scene the editor saves
            rect.SetParent(partsContainer != null && partsContainer.parent != null ? partsContainer.parent : transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one;
            picture = rect.gameObject.AddComponent<Image>();
            picture.raycastTarget = false;
            picture.preserveAspect = true;
        }
        if (picture.gameObject.activeSelf != show) picture.gameObject.SetActive(show);
        if (!show) return;
        picture.sprite = profile.image;
        Vector2 size = profile.image.rect.size;
        picture.rectTransform.sizeDelta = size / Mathf.Max(1f, Mathf.Max(size.x, size.y)) * Mathf.Max(0f, profile.imageSize);
        // Round the same middle as the parts, wherever their container sits.
        if (partsContainer != null) picture.rectTransform.position = partsContainer.position;
        picture.color = profile.imageTinted ? (profile.isRainbow ? RainbowColor() : profile.color) : new Color(1f, 1f, 1f, profile.color.a);
        TurnPicture();
    }

    // Clockwise by the shape's angle, as the parts are, and round with their spin.
    private void TurnPicture()
    {
        if (picture == null || currentProfile == null || !picture.gameObject.activeSelf) return;
        float spin = currentProfile.isRotating ? currentContainerAngle : 0f;
        picture.rectTransform.localEulerAngles = new Vector3(0f, 0f, spin - currentProfile.baseAngleOffset);
    }

    // CS2's T style leaves out the part pointing up: the one nearer straight up than half the gap to the next, so an x's two
    // upper arms, leaning up alike, both stay.
    private static bool PointsUp(float angle, int count) => count > 0 && Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 180f / count - .5f;

    private void ApplyParts(CrosshairProfile profile)
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
            bool isDotVisible = !combatHidden && profile.showDot && profile.dotSize > 0;
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
                ApplyOutline(centerDot, profile);
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

        partsContainer.gameObject.SetActive(!combatHidden);

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
                ApplyOutline(img, profile);

                float angle = (360f / profile.partCount) * i + profile.baseAngleOffset;
                // 위아래가 반전되던 문제 수정 (-angle + 180f)
                rt.localEulerAngles = new Vector3(0f, 0f, -angle + 180f);
                if (profile.hideTopPart && PointsUp(angle, profile.partCount)) rt.gameObject.SetActive(false);
            }
            else
            {
                // 이전에 남아있던 4개나 초과 파츠는 여기서 전부 꺼짐
                if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
            }
        }

        UpdatePartOffsets();
    }

    // Temporary combat visibility must not overwrite the user's saved crosshair profile.
    public void SetCombatHidden(bool hidden)
    {
        if (combatHidden == hidden) return;
        if (hidden)
        {
            savedDotActive = centerDot != null && centerDot.gameObject.activeSelf;
            savedPartsActive = partsContainer != null && partsContainer.gameObject.activeSelf;
        }
        combatHidden = hidden;
        if (currentProfile != null) ApplyProfile(currentProfile);
        else
        {
            if (centerDot != null) centerDot.gameObject.SetActive(!hidden && savedDotActive);
            if (partsContainer != null) partsContainer.gameObject.SetActive(!hidden && savedPartsActive);
            ApplyPicture();
            UpdateRing();
        }
    }

    public void SetDynamicSpread(float spread)
    {
        dynamicSpread = spread;
        UpdatePartOffsets();
    }

    /// <summary>
    /// The radius of the circle one shot of the gun in hand fills (a shotgun's pellets), in canvas pixels; 0 for a single
    /// bullet. While it is set the crosshair is a ring of that radius in place of its lines, the centre dot staying if the
    /// crosshair has one (user, 2026-11-10: "샷건은 들었을 때 원 조준선으로 바뀌고 근거리면 원 안 쪽이 다 맞게"). The
    /// ring is where the pellets land, not a wobble, so it shows while the shape is held (HoldShape) too; the gun's own
    /// spread and a shot's kick widen it as they widen the lines.
    /// </summary>
    public void SetPatternSpread(float pixels)
    {
        pixels = Mathf.Max(0f, pixels);
        if (Mathf.Approximately(patternSpread, pixels)) return;
        patternSpread = pixels;
        UpdatePartOffsets();
    }
    private float patternSpread;
    private CrosshairRing ring;
    // The ring's line when the crosshair has no plain lines to take a thickness from (none, or drawn sprites), and the
    // least and most it may be (px). A crosshair of drawn sprites carries its own edge; its ring gets this thin dark one.
    private const float RingLine = 2.5f, ThinnestRing = 1.5f, ThickestRing = 4f, DrawnBorder = 1f;
    private static readonly Color DrawnBorderColor = new Color(0f, 0f, 0f, .5f);

    // The pellets' ring: made the first time a gun needs it, never in the editor's scene or the settings preview. While it
    // shows, the lines are hidden: a ring with lines standing on it read as neither.
    private void UpdateRing()
    {
        bool show = Application.isPlaying && !isPreview && patternSpread > 0f && !combatHidden && currentProfile != null;
        if (ring == null && !show) return;
        if (ring == null)
        {
            var parent = partsContainer != null && partsContainer.parent != null ? partsContainer.parent : transform;
            ring = CrosshairRing.Create(parent);
        }
        if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
        bool lines = partsContainer != null && currentProfile != null && currentProfile.showParts && currentProfile.partCount > 0;
        if (lines && partsContainer.gameObject.activeSelf != (!combatHidden && !show)) partsContainer.gameObject.SetActive(!combatHidden && !show);
        if (!show) return;
        // Round the same middle as the lines, wherever their container sits.
        if (partsContainer != null) ring.rectTransform.position = partsContainer.position;
        bool plain = lines && currentProfile.partSprite == null;
        float line = plain ? Mathf.Clamp(Mathf.Min(currentProfile.partSize.x, currentProfile.partSize.y), ThinnestRing, ThickestRing) : RingLine;
        bool bordered = currentProfile.outlineWidth > 0f || plain;
        float radius = patternSpread + (holdShape ? 0f : dynamicSpread + fireKick);
        ring.Set(radius, line, currentProfile.isRainbow ? RainbowColor() : currentProfile.color,
            bordered ? currentProfile.outlineWidth : DrawnBorder, bordered ? currentProfile.outlineColor : DrawnBorderColor);
    }

    private void UpdatePartOffsets()
    {
        UpdateRing();
        if (currentProfile == null || !currentProfile.showParts || spawnedPartRects == null) return;

        // 모양 고정 중에는 퍼짐(점프·이동·무기별)과 사격 반동을 무시해 항상 같은 간격을 유지한다
        float totalGap = Mathf.Max(currentProfile.baseGap, patternSpread) + (holdShape ? 0f : dynamicSpread + fireKick);

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
