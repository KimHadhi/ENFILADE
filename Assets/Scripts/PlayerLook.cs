using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class PlayerLook : MonoBehaviour
{
    [Header("References")]
    [Tooltip("상하 회전을 담당하는 LookPivot (CameraPivot)")]
    [SerializeField] private Transform pitchPivot;

    [Header("Mouse Look")]
    [SerializeField, Min(0f)] private float sensitivity = 0.1f;
    [SerializeField, Range(0f, 89f)] private float pitchLimit = 89f;
    [SerializeField] private bool invertY;
    [SerializeField] private bool lockOnEnable = true;

    private InputAction lookAction;
    private float yaw;
    private float pitch;
    private bool lookEnabled = true;
    private bool cursorCaptured;
    private bool applicationPaused;
    private int inputBlockedThroughFrame = -1;

    public Quaternion YawRotation => Quaternion.Euler(0f, yaw, 0f);
    public Transform PitchPivot => pitchPivot;
    public float Pitch => pitch;

    private bool InputAvailable =>
        isActiveAndEnabled && lookEnabled &&
        Application.isFocused && !applicationPaused &&
        Time.timeScale > 0f && lookAction != null && lookAction.enabled;

    public bool CanLook =>
        InputAvailable && cursorCaptured &&
        Cursor.lockState == CursorLockMode.Locked &&
        Time.frameCount > inputBlockedThroughFrame;

    private void Awake()
    {
        if (pitchPivot == null)
        {
            pitchPivot = transform.Find("LookPivot");
            if (pitchPivot == null)
                pitchPivot = transform.Find("CameraPivot");
        }

        if (pitchPivot == null || !pitchPivot.IsChildOf(transform))
        {
            Debug.LogError("[PlayerLook] Player 하위의 LookPivot(또는 CameraPivot)을 연결하세요.", this);
            enabled = false;
            return;
        }

        var actions = InputSystem.actions;
        lookAction = actions != null ? actions.FindAction("Player/Look", throwIfNotFound: false) : null;

        if (lookAction == null)
        {
            Debug.LogError("[PlayerLook] Project-wide Actions에 'Player/Look' 액션을 설정하세요.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (pitchPivot == null || lookAction == null)
            return;

        ReadCurrentRotation();
        ApplyRotation();

        if (lockOnEnable)
            CaptureCursor();
    }

    private void Update()
    {
        if (!InputAvailable)
        {
            ReleaseCursor();
            return;
        }

#if UNITY_EDITOR
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            ReleaseCursor();
            return;
        }
#endif

        if (!cursorCaptured || Cursor.lockState != CursorLockMode.Locked)
        {
            ReleaseCursor();
            if (Mouse.current?.leftButton.wasPressedThisFrame == true)
                CaptureCursor();
            return;
        }

        if (!CanLook)
            return;

        Vector2 delta = lookAction.ReadValue<Vector2>();

        yaw = Mathf.Repeat(yaw + delta.x * sensitivity, 360f);
        pitch = Mathf.Clamp(
            pitch + delta.y * sensitivity * (invertY ? 1f : -1f),
            -pitchLimit,
            pitchLimit);

        // Movement가 실행되기 전에 Player 루트 Yaw 회전 적용
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void LateUpdate()
    {
        // 카메라 피치 회전 적용
        if (pitchPivot != null)
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    public void SetLookEnabled(bool value)
    {
        if (value && !lookEnabled)
            ReadCurrentRotation();

        lookEnabled = value;

        if (value)
            CaptureCursor();
        else
            ReleaseCursor();
    }

    public void CaptureCursor()
    {
        if (!InputAvailable)
            return;

        if (cursorCaptured && Cursor.lockState == CursorLockMode.Locked)
            return;

        cursorCaptured = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        inputBlockedThroughFrame = Time.frameCount + 1;
    }

    public void ReleaseCursor()
    {
        if (!cursorCaptured)
            return;

        cursorCaptured = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        inputBlockedThroughFrame = Time.frameCount + 1;
    }

    public void SetAngles(float yawDegrees, float pitchDegrees = 0f)
    {
        yaw = Mathf.Repeat(yawDegrees, 360f);
        pitch = Mathf.Clamp(pitchDegrees, -pitchLimit, pitchLimit);
        ApplyRotation();
        inputBlockedThroughFrame = Time.frameCount + 1;
    }

    private void ReadCurrentRotation()
    {
        yaw = Mathf.Repeat(transform.eulerAngles.y, 360f);
        if (pitchPivot != null)
        {
            pitch = Mathf.Clamp(
                Mathf.DeltaAngle(0f, pitchPivot.localEulerAngles.x),
                -pitchLimit, pitchLimit);
        }
    }

    private void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (pitchPivot != null)
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ReleaseCursor();
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        if (paused)
            ReleaseCursor();
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }
}