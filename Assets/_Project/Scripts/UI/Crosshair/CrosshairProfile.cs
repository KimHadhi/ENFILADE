using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCrosshairProfile", menuName = "Crosshair/Profile")]
public class CrosshairProfile : ScriptableObject
{
#pragma warning disable CS0067 // only OnValidate raises it, and builds leave that out
    public event Action OnProfileChanged;
#pragma warning restore CS0067

    [Header("중앙 점(Dot)")]
    public bool showDot = true;
    public Sprite dotSprite;
    [Min(0)] public int dotSize = 2; // 0 이상의 정수로 입력 (렌더링 시 2배 적용)

    [Header("외곽 부품(Parts) 설정")]
    public bool showParts = true;
    public Sprite partSprite;
    public int partCount = 4;
    public float baseAngleOffset;
    public Vector2 partSize = new Vector2(10f, 20f);
    public float baseGap = 12f;

    [Header("외곽선")]
    [Min(0f), Tooltip("점과 부품에 두르는 선 두께(px). 0이면 끈다. 배경과 색이 비슷해도 보이게 한다.")]
    public float outlineWidth = 0f;
    public Color outlineColor = new Color(0f, 0f, 0f, .6f);

    [Header("특수 효과 (회전)")]
    public bool isRotating = false;
    public float rotationSpeed = 90f;

    [Header("특수 효과 (파스텔 무지개)")]
    public bool isRainbow = false;        // 무지개 효과 활성화 여부
    public float rainbowSpeed = 0.5f;     // 색상 순환 속도
    [Range(0f, 1f)]
    public float rainbowSaturation = 0.45f; // 채도 (낮을수록 연하고 은은한 파스텔톤)
    [Range(0f, 1f)]
    public float rainbowBrightness = 0.95f; // 밝기

    [Header("색상")]
    public Color color = Color.white;     // 무지개가 꺼져 있을 때 사용할 기본 색상

    [Header("T자 · 그림")]
    [Tooltip("CS2's T style: the part pointing straight up is left out.")]
    public bool hideTopPart;
    [Tooltip("A picture shown at the middle, under the dot and the parts (the player's own, CrosshairImages). None for no picture.")]
    public Sprite image;
    [Min(0f), Tooltip("The picture's longer side (px).")]
    public float imageSize = 32f;
    [Tooltip("The picture takes the crosshair's colour (and the rainbow) instead of keeping its own; either way it takes the colour's opacity.")]
    public bool imageTinted;

#if UNITY_EDITOR
    private void OnValidate()
    {
        OnProfileChanged?.Invoke();
    }
#endif
}