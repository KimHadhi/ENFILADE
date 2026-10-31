using UnityEngine;

[DisallowMultipleComponent]
public sealed class MovementDebugHUD : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerMovement;

    [Header("UI Style Settings")]
    [SerializeField] private int fontSize = 15;
    [SerializeField] private Vector2 position = new Vector2(20f, 20f);
    [SerializeField] private float minWidth = 260f;

    private PlayerFootsteps footsteps;
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
        if (playerMovement == null || !playerMovement.IsInitialized) return;

        InitStyles();

        // 1. 텍스트 데이터 구성
        PlayerMovement.MovementState state = playerMovement.CurrentState;
        string stateText = $"STATE: {playerMovement.CurrentStatePath} ({playerMovement.CurrentStateDuration:F2}s)";

        Vector3 vel = playerMovement.ActualVelocity;
        float hSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
        string speedText = $"Speed: {hSpeed:F2} m/s (run {playerMovement.RunSpeed:F1})";

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
        if (playerMovement.DamageReceiver != null)
        {
            GUILayout.Label($"Health: {playerMovement.DamageReceiver.Health:F0} | Dodge: {(playerMovement.IsDodging ? "ACTIVE" : "READY / COOLDOWN")}", labelStyle);
        }
        if (footsteps == null) footsteps = playerMovement.GetComponent<PlayerFootsteps>();
        if (footsteps != null) GUILayout.Label($"Surface: {footsteps.LastSurface}", labelStyle);
        if (playerMovement.Abilities != null)
        {
            var landing = playerMovement.Abilities.Landing;
            GUILayout.Label($"Last landing: {landing.LastTier} {landing.LastImpactSpeed:F1} m/s, {landing.LastFallDistance:F1} m", labelStyle);
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private Color GetStateColor(PlayerMovement.MovementState state)
    {
        return state switch
        {
            // --- Locomotion (화이트 / 스카이블루 / 그린 계열) ---
            PlayerMovement.MovementState.Standing => new Color(0.9f, 0.9f, 0.9f), // 부드러운 흰색
            PlayerMovement.MovementState.Crouching => new Color(0.4f, 0.8f, 1.0f), // 하늘색
            PlayerMovement.MovementState.Proning => new Color(0.6f, 0.8f, 0.4f), // 라임 그린
            PlayerMovement.MovementState.Supine => new Color(0.7f, 0.4f, 1.0f), // 연보라색

            // --- Sliding (시안 / 오렌지 계열) ---
            PlayerMovement.MovementState.Sliding => new Color(0.2f, 1.0f, 0.7f), // 민트
            PlayerMovement.MovementState.HeadFirstSliding => new Color(1.0f, 0.5f, 0.1f), // 주황색
            PlayerMovement.MovementState.SupineSliding => new Color(0.9f, 0.2f, 0.4f), // 짙은 핑크/레드

            // --- Diving (옐로우 / 마젠타 계열) ---
            PlayerMovement.MovementState.Diving => new Color(1.0f, 0.9f, 0.2f), // 노란색
            PlayerMovement.MovementState.SupineDiving => new Color(1.0f, 0.3f, 0.8f), // 핫핑크

            // --- Parkour (선명한 녹색 / 청록 / 에메랄드 계열) ---
            PlayerMovement.MovementState.BarGrab => new Color(0.2f, 0.9f, 0.5f),
            PlayerMovement.MovementState.BarSwing => new Color(0.0f, 1.0f, 0.5f), // 밝은 에메랄드
            PlayerMovement.MovementState.LacheRelease => new Color(0.3f, 1.0f, 0.8f),
            PlayerMovement.MovementState.LedgeHang => new Color(1.0f, 0.7f, 0.2f), // 골드 옐로우
            PlayerMovement.MovementState.LedgeJump => new Color(1.0f, 0.8f, 0.3f),
            PlayerMovement.MovementState.Vault => new Color(0.3f, 0.8f, 1.0f), // 라이트 블루
            PlayerMovement.MovementState.Mantle => new Color(0.4f, 0.9f, 1.0f),
            PlayerMovement.MovementState.WallClimb => new Color(0.8f, 0.4f, 1.0f), // 퍼플
            PlayerMovement.MovementState.WallKick => new Color(1.0f, 0.4f, 0.9f),
            PlayerMovement.MovementState.Ladder => new Color(0.9f, 0.8f, 0.4f),
            PlayerMovement.MovementState.Zipline => new Color(0.4f, 0.7f, 1.0f),
            PlayerMovement.MovementState.Balance => new Color(0.7f, 1.0f, 0.5f),
            PlayerMovement.MovementState.BalanceFall => new Color(1.0f, 0.6f, 0.4f),

            // --- Abilities / Action (강렬한 레드 / 코랄 계열) ---
            PlayerMovement.MovementState.StepDodge => new Color(0.4f, 1.0f, 0.9f), // 청록
            PlayerMovement.MovementState.LandingRoll => new Color(0.8f, 1.0f, 0.4f), // 옐로우그린
            PlayerMovement.MovementState.HeavyLanding => new Color(0.75f, 0.45f, 0.2f), // 갈색
            PlayerMovement.MovementState.DashPush => new Color(1.0f, 0.3f, 0.3f), // 레드

            _ => Color.white
        };
    }

    private void OnDestroy()
    {
        if (backgroundTexture != null)
        {
            Destroy(backgroundTexture);
        }
    }
}
