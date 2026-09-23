using UnityEngine;

[DisallowMultipleComponent]
public sealed class MovementDebugHUD : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;

    [Header("UI Style Settings")]
    [SerializeField] private int fontSize = 15;
    [SerializeField] private Vector2 position = new Vector2(20f, 20f);
    [SerializeField] private float minWidth = 260f;

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
            normal = { background = backgroundTexture },
            padding = new RectOffset(16, 16, 12, 14),
            margin = new RectOffset(0, 0, 0, 0)
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize + 2,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            margin = new RectOffset(0, 0, 0, 4),
            padding = new RectOffset(0, 0, 0, 0)
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            margin = new RectOffset(0, 0, 2, 2),
            padding = new RectOffset(0, 0, 0, 0)
        };
    }

    private void OnGUI()
    {
        if (playerMovement == null) return;

        InitStyles();

        // 1. 텍스트 데이터 구성
        PlayerMovement.MovementState state = playerMovement.CurrentState;
        string stateText = $"STATE: {state.ToString().ToUpper()} ({playerMovement.CurrentStateDuration:F2}s)";

        Vector3 vel = playerMovement.ActualVelocity;
        float hSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
        string speedText = $"Speed: {hSpeed:F2} m/s";

        string groundText = playerMovement.IsGrounded ? "Ground: YES" : "Ground: AIRBORNE";

        Vector2 input = playerMovement.CurrentInput;
        string inputText = $"Input: ({input.x:F1}, {input.y:F1})";

        // 2. 가로폭 자동 계산 (가장 긴 텍스트 기준)
        float maxTextWidth = Mathf.Max(
            titleStyle.CalcSize(new GUIContent(stateText)).x,
            labelStyle.CalcSize(new GUIContent(speedText)).x,
            labelStyle.CalcSize(new GUIContent(groundText)).x,
            labelStyle.CalcSize(new GUIContent(inputText)).x
        );

        float finalWidth = Mathf.Max(minWidth, maxTextWidth + 32f);

        // 3. 높이는 제한 없이 유니티가 내용물만큼만 감싸도록 처리 (잘림 방지)
        GUILayout.BeginArea(new Rect(position.x, position.y, finalWidth, 400f));
        GUILayout.BeginVertical(boxStyle, GUILayout.Width(finalWidth));

        titleStyle.normal.textColor = GetStateColor(state);
        GUILayout.Label(stateText, titleStyle);

        labelStyle.normal.textColor = Color.white;
        GUILayout.Label(speedText, labelStyle);

        labelStyle.normal.textColor = playerMovement.IsGrounded ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        GUILayout.Label(groundText, labelStyle);

        labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        GUILayout.Label(inputText, labelStyle);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private Color GetStateColor(PlayerMovement.MovementState state)
    {
        switch (state)
        {
            case PlayerMovement.MovementState.Standing:
                return new Color(0.9f, 0.9f, 0.9f);
            case PlayerMovement.MovementState.Crouching:
                return new Color(0.4f, 0.8f, 1f);
            case PlayerMovement.MovementState.Sliding:
                return new Color(0.2f, 1f, 0.7f);
            case PlayerMovement.MovementState.Diving:
                return new Color(1f, 0.9f, 0.2f);
            case PlayerMovement.MovementState.SupineDiving:
                return new Color(1f, 0.3f, 0.8f);
            case PlayerMovement.MovementState.DiveSliding:
                return new Color(1f, 0.5f, 0.1f);
            case PlayerMovement.MovementState.SupineSliding:
                return new Color(0.9f, 0.2f, 0.4f);
            case PlayerMovement.MovementState.Supine:
                return new Color(0.7f, 0.4f, 1f);
            case PlayerMovement.MovementState.Proning:
                return new Color(0.6f, 0.8f, 0.4f);
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