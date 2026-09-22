using UnityEngine;

[DisallowMultipleComponent]
public sealed class MovementDebugHUD : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;

    [Header("UI Style Settings")]
    [SerializeField] private int fontSize = 16;
    [SerializeField] private Vector2 position = new Vector2(20f, 20f);
    [SerializeField] private Vector2 boxSize = new Vector2(240f, 130f);

    private GUIStyle labelStyle;
    private GUIStyle titleStyle;
    private GUIStyle boxStyle;
    private Texture2D backgroundTexture;

    private void Awake()
    {
        if (playerMovement == null)
            playerMovement = FindFirstObjectByType<PlayerMovement>();
    }

    private void InitStyles()
    {
        if (labelStyle != null) return;

        backgroundTexture = new Texture2D(1, 1);
        backgroundTexture.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.1f, 0.85f));
        backgroundTexture.Apply();

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = backgroundTexture }
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 2,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft
        };
    }

    private void OnGUI()
    {
        if (playerMovement == null) return;

        InitStyles();

        // 디버그 HUD 배경 박스
        GUI.Box(new Rect(position.x, position.y, boxSize.x, boxSize.y), GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(position.x + 12f, position.y + 10f, boxSize.x - 24f, boxSize.y - 20f));

        // 1. 현재 상태 (색상 강조)
        PlayerMovement.MovementState state = playerMovement.CurrentState;
        Color stateColor = GetStateColor(state);
        titleStyle.normal.textColor = stateColor;
        GUILayout.Label($"STATE: {state.ToString().ToUpper()}", titleStyle);

        GUILayout.Space(6f);

        // 2. 수평 이동 속도
        Vector3 vel = playerMovement.ActualVelocity;
        float hSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
        labelStyle.normal.textColor = Color.white;
        GUILayout.Label($"Speed: {hSpeed:F2} m/s", labelStyle);

        // 3. 접지 여부
        labelStyle.normal.textColor = playerMovement.IsGrounded ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        GUILayout.Label(playerMovement.IsGrounded ? "Ground: YES" : "Ground: AIRBORNE", labelStyle);

        // 4. 입력 벡터
        Vector2 input = playerMovement.CurrentInput;
        labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        GUILayout.Label($"Input: ({input.x:F1}, {input.y:F1})", labelStyle);

        GUILayout.EndArea();
    }

    private Color GetStateColor(PlayerMovement.MovementState state)
    {
        switch (state)
        {
            case PlayerMovement.MovementState.Standing:
                return new Color(0.9f, 0.9f, 0.9f); // 기본 흰색
            case PlayerMovement.MovementState.Crouching:
                return new Color(0.4f, 0.8f, 1f);   // 연하늘색
            case PlayerMovement.MovementState.Sliding:
                return new Color(0.2f, 1f, 0.7f);   // 민트/에메랄드
            case PlayerMovement.MovementState.Diving:
                return new Color(1f, 0.9f, 0.2f);   // 노란색
            case PlayerMovement.MovementState.DiveSliding:
                return new Color(1f, 0.5f, 0.1f);   // 오렌지색
            case PlayerMovement.MovementState.SupineSliding:
                return new Color(1f, 0.3f, 0.6f);   // 핫핑크/자주색 (등 슬라이딩 강조)
            case PlayerMovement.MovementState.Supine:
                return new Color(0.7f, 0.4f, 1f);   // 보라색 (백 크롤)
            case PlayerMovement.MovementState.Proning:
                return new Color(0.6f, 0.8f, 0.4f); // 카키/연두색
            default:
                return Color.white;
        }
    }

    private void OnDestroy()
    {
        if (backgroundTexture != null)
        {
            Destroy(backgroundTexture);
        }
    }
}