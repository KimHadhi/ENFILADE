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
    [Tooltip("Base sensitivity. The Esc menu's sensitivity multiplies it; Y inversion is also an Esc menu setting.")]
    [SerializeField, Range(0.01f, 1f)] private float sensitivity = 0.1f;
    [SerializeField] private float minPitch = -89f;
    [SerializeField] private float maxPitch = 89f;
    [SerializeField, Range(0f, 90f)] private float wallClimbYawLimit = 30f;

    private InputAction lookAction;
    private PlayerMovement movement;
    private float yaw;
    private float pitch;
    private readonly RecoilSpring recoil = new RecoilSpring();
    private Vector2 pendingClimb;
    private float recoilResponse = 24f;

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
        if (Cursor.lockState != CursorLockMode.Locked || Time.timeScale <= 0f || !Application.isFocused
            || (movement != null && movement.CombatInputLocked))
            return;

        Vector2 delta = Vector2.zero;
        if (lookAction != null) delta = lookAction.ReadValue<Vector2>();
        else if (Mouse.current != null) delta = Mouse.current.delta.ReadValue();

        var settings = GameSettings.Current;
        float scale = sensitivity * settings.mouseSensitivity;
        float mouseX = delta.x * scale;
        float mouseY = delta.y * scale * (settings.invertY ? 1f : -1f);

        yaw += mouseX;
        yaw = ClampWallClimbYaw(yaw);
        pitch = Mathf.Clamp(pitch + mouseY, minPitch, maxPitch);
        TickRecoil(Time.deltaTime);
    }

    public void ApplyRecoil(Vector2 kick, WeaponData data = null)
    {
        recoilResponse = data != null ? data.RecoilResponse : 24f;
        float retain = data != null ? data.RecoilRetention : .22f;
        pendingClimb += kick * retain;
        recoil.Kick(new Vector3(kick.x, kick.y, 0f) * (1f - retain), recoilResponse);
    }

    internal void TickRecoil(float dt)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        // Retained climb also arrives smoothly; ordinary mouse input remains immediate.
        Vector2 climb = pendingClimb * (1f - Mathf.Exp(-recoilResponse * dt));
        pendingClimb -= climb;
        yaw = ClampWallClimbYaw(yaw + climb.y);
        pitch = Mathf.Clamp(pitch - climb.x, minPitch, maxPitch);
        recoil.Tick(dt, recoilResponse);
        ApplyLookPose();
    }
    private void ApplyLookPose()
    {
        transform.rotation = Quaternion.Euler(0f, ClampWallClimbYaw(yaw + recoil.Value.y), 0f);
        if (lookPivot != null)
            lookPivot.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoil.Value.x, minPitch, maxPitch), 0f, 0f);
    }
    public void ClearRecoil()
    {
        // Keep the visible aim when holstering instead of snapping back or kicking the next weapon.
        yaw = ClampWallClimbYaw(yaw + recoil.Value.y);
        pitch = Mathf.Clamp(pitch - recoil.Value.x, minPitch, maxPitch);
        pendingClimb = Vector2.zero; recoil.Reset();
    }
    private void OnDisable() => ClearRecoil();

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
