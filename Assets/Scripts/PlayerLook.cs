using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public sealed class PlayerLook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform lookPivot;

    [Header("Look Settings")]
    [SerializeField, Range(0.01f, 1f)] private float sensitivity = 0.1f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minPitch = -89f;
    [SerializeField] private float maxPitch = 89f;

    [Header("QA & Debugging")]
    [Tooltip("ESC를 눌렀을 때 커서 잠금을 해제하고 입력을 일시 정지할지 여부")]
    [SerializeField] private bool enableEscUnlock = true;

    private InputAction lookAction;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        var actions = InputSystem.actions;
        if (actions != null)
            lookAction = actions.FindAction("Player/Look", throwIfNotFound: false);

        if (lookPivot == null)
        {
            lookPivot = transform.Find("CrouchPivot/BobPivot/LookPivot");
            if (lookPivot == null) lookPivot = transform.Find("CrouchPivot/LookPivot");
        }

        yaw = transform.eulerAngles.y;
        if (lookPivot != null)
        {
            pitch = lookPivot.localEulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
        }
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        // 1. ESC / 좌클릭을 통한 락/언락 처리
        if (enableEscUnlock && Keyboard.current != null && Mouse.current != null)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState == CursorLockMode.None && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // 2. [가장 강력한 차단] 실제 커서가 잠겨있지 않다면(ESC 상태라면) 입력/회전 연산 무조건 무시!
        if (Cursor.lockState != CursorLockMode.Locked || Time.timeScale <= 0f || !Application.isFocused)
            return;

        Vector2 delta = Vector2.zero;
        if (lookAction != null) delta = lookAction.ReadValue<Vector2>();
        else if (Mouse.current != null) delta = Mouse.current.delta.ReadValue();

        float mouseX = delta.x * sensitivity;
        float mouseY = delta.y * sensitivity * (invertY ? 1f : -1f);

        yaw += mouseX;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        pitch = Mathf.Clamp(pitch + mouseY, minPitch, maxPitch);
        if (lookPivot != null) lookPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}