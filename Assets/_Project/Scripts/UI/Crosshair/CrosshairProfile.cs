using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCrosshairProfile", menuName = "Crosshair/Profile")]
public class CrosshairProfile : ScriptableObject
{
    public event Action OnProfileChanged;

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

#if UNITY_EDITOR
    private void OnValidate()
    {
        OnProfileChanged?.Invoke();
    }
#endif
}