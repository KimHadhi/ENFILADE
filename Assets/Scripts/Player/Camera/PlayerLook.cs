using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public sealed class PlayerLook : MonoBehaviour
{
    [Header("Optional Reference (비우면 자동 탐색)")]
    [Tooltip("비우면 CrouchPivot/BobPivot/LookPivot 또는 CrouchPivot/LookPivot을 자동 탐색합니다.")]
    [SerializeField] private Transform lookPivot;

    [Header("Look Settings")]
    [SerializeField, Range(0.01f, 1f)] private float sensitivity = 0.1f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minPitch = -89f;
    [SerializeField] private float maxPitch = 89f;
    [SerializeField, Range(0f, 90f)] private float wallClimbYawLimit = 30f;

    private InputAction lookAction;
    private PlayerMovement movement;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
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
        // 게임 시작 시 기본 마우스 가둠
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        // 커서가 풀려있거나(채팅창/메뉴 활성화 상태), 일시정지 중이거나 창 포커스를 잃으면 카메라 회전 중단
        if (Cursor.lockState != CursorLockMode.Locked || Time.timeScale <= 0f || !Application.isFocused)
            return;

        Vector2 delta = Vector2.zero;
        if (lookAction != null) delta = lookAction.ReadValue<Vector2>();
        else if (Mouse.current != null) delta = Mouse.current.delta.ReadValue();

        float mouseX = delta.x * sensitivity;
        float mouseY = delta.y * sensitivity * (invertY ? 1f : -1f);

        yaw += mouseX;
        yaw = ClampWallClimbYaw(yaw);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        pitch = Mathf.Clamp(pitch + mouseY, minPitch, maxPitch);
        if (lookPivot != null) lookPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private float ClampWallClimbYaw(float candidate)
    {
        if (movement == null || movement.CurrentState != PlayerMovement.MovementState.WallClimb
            || movement.Abilities == null) return candidate;
        Vector3 facing = Vector3.ProjectOnPlane(-movement.Abilities.Traversal.Wall.normal, Vector3.up);
        if (facing.sqrMagnitude < 0.001f) return candidate;
        float center = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        return center + Mathf.Clamp(Mathf.DeltaAngle(center, candidate), -wallClimbYawLimit, wallClimbYawLimit);
    }
}
