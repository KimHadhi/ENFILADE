using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(FrameOrder.Look)]
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
    private PlayerBody body;
    private CombatHeadMotion headMotion;
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
        TakeCombatTurn();
        // 커서가 풀려있거나(채팅창/메뉴 활성화 상태), 일시정지 중이거나 창 포커스를 잃거나 죽어 있으면 카메라 회전 중단
        if (Cursor.lockState != CursorLockMode.Locked || Time.timeScale <= 0f || !Application.isFocused
            || (movement != null && (movement.CombatInputLocked || movement.IsDead)))
            return;

        Vector2 delta = Vector2.zero;
        if (lookAction != null) delta = lookAction.ReadValue<Vector2>();
        else if (Mouse.current != null) delta = Mouse.current.delta.ReadValue();

        var settings = GameSettings.Current;
        float scale = sensitivity * settings.mouseSensitivity;
        float mouseX = delta.x * scale;
        float mouseY = delta.y * scale * (settings.invertY ? 1f : -1f);

        TryMouseTurnOnBar(mouseX);
        TryMouseRollOver(mouseX);
        SteerSlide(mouseX);
        yaw += mouseX;
        yaw = ClampLookYaw(yaw);
        pitch = ClampLookPitch(pitch + mouseY);
        TickRecoil(Time.deltaTime);
    }

    // Sliding, the view stops slideTurnLimit either side of the way the slide goes (ClampLookYaw). The mouse pushed on
    // past that edge turns the slide itself after it, up to Move.slideLookSteerRate degrees a second (user, 2026-11-10:
    // "방향을 틀면 슬라이딩도 틀기", as Apex Legends' slide steers), so the look is slowed there instead of stopped
    // dead, and the legs come round with the slide (PlayerBody.FaceSlide). Only on the ground: in the air a slide keeps
    // its line. Called before the mouse turns the view, so the arc has already moved when the view is held to it.
    private void SteerSlide(float mouseX)
    {
        if (mouseX == 0f || movement == null || movement.CurrentState != PlayerMovement.MovementState.Sliding
            || !movement.IsGrounded || movement.SlideLookSteerRate <= 0f) return;
        Vector3 direction = Vector3.ProjectOnPlane(movement.SlideDirection, Vector3.up);
        if (direction.sqrMagnitude < .0001f) return;
        if (body == null) body = GetComponent<PlayerBody>();
        float limit = body != null ? body.SlideLookYawLimit : 75f;
        float now = transform.eulerAngles.y;
        // Where this push would take the view from the way the slide faces, and how far past the edge that is.
        float wanted = Mathf.DeltaAngle(PlayerBody.SlideFacing(direction, now), now) + mouseX;
        float past = Mathf.Abs(wanted) - limit;
        // Only a push outward counts; one back toward the middle from outside the arc doesn't steer.
        if (past <= 0f || Mathf.Sign(mouseX) != Mathf.Sign(wanted)) return;
        float turn = Mathf.Sign(mouseX) * Mathf.Min(past, Mathf.Min(Mathf.Abs(mouseX), movement.SlideLookSteerRate * Time.deltaTime));
        movement.SlideDirection = Quaternion.Euler(0f, turn, 0f) * direction.normalized;
    }

    // Backlog 123: on a swing bar the view stops at the bar's look limit; the mouse pushed on past it by
    // barTurnMousePush turns the player round toward that side (ParkourTraversal.TryTurnOnBar).
    private float barTurnPush;
    private void TryMouseTurnOnBar(float mouseX)
    {
        var traversal = movement != null && movement.Abilities != null ? movement.Abilities.Traversal : null;
        if (traversal == null || traversal.Bar == null || movement.CurrentState != PlayerMovement.MovementState.BarSwing
            || traversal.BarTurning) { barTurnPush = 0f; return; }
        if (mouseX == 0f) return;
        Vector3 facing = PlayerBody.BarFacingFor(traversal.Bar, transform.forward);
        if (body == null) body = GetComponent<PlayerBody>();
        float limit = body != null ? body.BarLookYawLimit : 45f;
        float from = Mathf.DeltaAngle(Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, transform.eulerAngles.y);
        // Only pushing outward while already at the edge counts; turning back the other way starts over.
        if (Mathf.Abs(from) < limit - 1f || Mathf.Sign(mouseX) != Mathf.Sign(from)) { barTurnPush = 0f; return; }
        barTurnPush += Mathf.Abs(mouseX);
        if (barTurnPush < movement.AbilitySettings.barTurnMousePush) return;
        barTurnPush = 0f;
        // Round to face straight along the bar's other side.
        traversal.TryTurnOnBar(Mathf.Sign(from) * (180f - Mathf.Abs(from)));
    }

    // Lying prone or on the back the view stops at the neck's limit either side of the way the body faces
    // (PlayerBody.Lying); the mouse pushed on past it by lyingRollMousePush rolls the player over and carries the view
    // round, the way it was pushed, to face the way the body then faces (down the body to the feet on the back).
    private float rollPush, rollStarted = float.NegativeInfinity, rollFrom, rollAmount;
    private bool Lying => movement != null && movement.IsGrounded
        && (movement.CurrentState == PlayerMovement.MovementState.Proning || movement.CurrentState == PlayerMovement.MovementState.Supine);
    private bool RollingOver => movement != null && Time.time - rollStarted < movement.AbilitySettings.lyingRollDuration;
    private void TryMouseRollOver(float mouseX)
    {
        if (!Lying || RollingOver) { rollPush = 0f; return; }
        if (mouseX == 0f) return;
        if (body == null) body = GetComponent<PlayerBody>();
        if (body == null || !body.LyingStill) return;
        float limit = body.LyingLookLimit;
        float from = Mathf.DeltaAngle(body.LyingFacingYaw, transform.eulerAngles.y);
        // Only pushing outward while already at the edge counts; turning back the other way starts over.
        if (Mathf.Abs(from) < limit - 1f || Mathf.Sign(mouseX) != Mathf.Sign(from)) { rollPush = 0f; return; }
        rollPush += Mathf.Abs(mouseX);
        if (rollPush < movement.AbilitySettings.lyingRollMousePush) return;
        rollPush = 0f;
        if (!movement.TryRollOver()) return;
        // Rolled over, the body faces the other way: on round the way pushed until the view faces it.
        rollFrom = transform.eulerAngles.y;
        rollAmount = Mathf.Sign(from) * (180f - Mathf.Abs(from));
        rollStarted = Time.time;
    }

    // A turn combat asks of the look (a slip keeping the view on its attacker, MicroSlip.LockOn through CombatHeadMotion.Turn)
    // turns it as the mouse would, also while combat holds the mouse. Asked for this frame only; one not taken is dropped.
    private void TakeCombatTurn()
    {
        if (headMotion == null && !TryGetComponent(out headMotion)) return;
        float turn = headMotion.TakeYaw();
        if (turn == 0f || Time.timeScale <= 0f || (movement != null && movement.IsDead)) return;
        yaw = ClampLookYaw(yaw + turn);
        ApplyLookPose();
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
        yaw = ClampLookYaw(yaw + climb.y);
        pitch = ClampLookPitch(pitch - climb.x);
        recoil.Tick(dt, recoilResponse);
        ApplyLookPose();
    }
    private void ApplyLookPose()
    {
        transform.rotation = Quaternion.Euler(0f, ClampLookYaw(yaw + recoil.Value.y), 0f);
        if (lookPivot != null)
            lookPivot.localRotation = Quaternion.Euler(ClampLookPitch(pitch - recoil.Value.x), 0f, 0f);
    }
    public void ClearRecoil()
    {
        // Keep the visible aim when holstering instead of snapping back or kicking the next weapon.
        yaw = ClampLookYaw(yaw + recoil.Value.y);
        pitch = ClampLookPitch(pitch - recoil.Value.x);
        pendingClimb = Vector2.zero; recoil.Reset();
    }
    private void OnDisable() => ClearRecoil();

    private float ClampLookYaw(float candidate)
    {
        if (movement != null && movement.CurrentState == PlayerMovement.MovementState.Sliding)
        {
            Vector3 direction = Vector3.ProjectOnPlane(movement.SlideDirection, Vector3.up);
            if (direction.sqrMagnitude < .0001f) return candidate;
            if (body == null) body = GetComponent<PlayerBody>();
            float limit = body != null ? body.SlideLookYawLimit : 75f;
            // A slide carrying the player backwards (wall bounce, backward entry) keeps facing the view.
            return WithinArc(PlayerBody.SlideFacing(direction, transform.eulerAngles.y), limit, candidate);
        }
        if (movement != null && movement.CurrentState == PlayerMovement.MovementState.SupineDiving && !movement.IsGrounded)
        {
            // Turned onto the back in the air, the view stays around the way the back faces (Beatrun's jump turn).
            Vector3 back = Vector3.ProjectOnPlane(-movement.DiveBodyForward, Vector3.up);
            if (back.sqrMagnitude < .0001f) return candidate;
            if (body == null) body = GetComponent<PlayerBody>();
            return WithinArc(Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg, body != null ? body.SupineDiveLookYawLimit : 75f, candidate);
        }
        if (movement != null && (movement.CurrentState == PlayerMovement.MovementState.BarGrab
            || movement.CurrentState == PlayerMovement.MovementState.BarSwing)
            && movement.Abilities != null && movement.Abilities.Traversal.Bar != null)
        {
            // Turning round on the bar (backlog 123) the view is carried the half turn; the body follows its side.
            if (movement.Abilities.Traversal.BarTurning) return movement.Abilities.Traversal.BarTurnYaw;
            // On a swing bar the body faces along the swing; the head turns only so far from that (Beatrun's swing: 45).
            Vector3 barFacing = PlayerBody.BarFacingFor(movement.Abilities.Traversal.Bar, transform.forward);
            if (body == null) body = GetComponent<PlayerBody>();
            return WithinArc(Mathf.Atan2(barFacing.x, barFacing.z) * Mathf.Rad2Deg, body != null ? body.BarLookYawLimit : 45f, candidate);
        }
        if (movement != null && movement.CurrentState == PlayerMovement.MovementState.Mantle && movement.Abilities != null)
        {
            // Climbing up, the camera rides the head: the view turns only so far from the way the climb goes.
            Vector3 climb = movement.Abilities.Traversal.MantleFacing;
            if (climb != Vector3.zero)
            {
                if (body == null) body = GetComponent<PlayerBody>();
                return WithinArc(Mathf.Atan2(climb.x, climb.z) * Mathf.Rad2Deg, body != null ? body.MantleLookYawLimit : 45f, candidate);
            }
        }
        if (movement != null && movement.CurrentState == PlayerMovement.MovementState.HeavyLanding && movement.Abilities != null)
        {
            // Down on the ground the head turns only so far from the way the body faces.
            if (body == null) body = GetComponent<PlayerBody>();
            return WithinArc(movement.Abilities.Landing.HeavyYaw, body != null ? body.HeavyLandingLookYawLimit : 90f, candidate);
        }
        if (movement != null && movement.CurrentState == PlayerMovement.MovementState.LedgeHang && movement.Abilities != null
            && movement.Abilities.Traversal.Ledge.Surface != null)
        {
            // The hang's look clips turn the body after the view up to here, on either side of the wall.
            var ledge = movement.Abilities.Traversal.Ledge;
            Vector3 inward = -ledge.Surface.transform.TransformDirection(ledge.LocalNormal);
            if (body == null) body = GetComponent<PlayerBody>();
            return WithinArc(Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg, body != null ? body.HangLookYawLimit : 175f, candidate);
        }
        if (Lying)
        {
            if (RollingOver)
                return rollFrom + rollAmount * Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((Time.time - rollStarted) / movement.AbilitySettings.lyingRollDuration));
            if (body == null) body = GetComponent<PlayerBody>();
            if (body != null && body.LyingStill) return WithinArc(body.LyingFacingYaw, body.LyingLookLimit, candidate);
        }
        if (movement != null && movement.Abilities != null && movement.Abilities.Fixtures.TryLookCentre(out float fixtureYaw))
        {
            // On a ladder or a cable the body faces its way; the head turns only so far from that.
            if (body == null) body = GetComponent<PlayerBody>();
            return WithinArc(fixtureYaw, body != null ? body.FixtureLookYawLimit : 90f, candidate);
        }
        if (movement == null || movement.CurrentState != PlayerMovement.MovementState.WallClimb
            || movement.Abilities == null) return candidate;
        Vector3 facing = Vector3.ProjectOnPlane(-movement.Abilities.Traversal.Wall.normal, Vector3.up);
        if (facing.sqrMagnitude < 0.001f) return candidate;
        float center = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        return center + Mathf.Clamp(Mathf.DeltaAngle(center, candidate), -wallClimbYawLimit, wallClimbYawLimit);
    }

    // Ordinary mouse look stops at the arc's edge without ever turning what the arc follows. A view already outside it
    // (a sideways slide bounce, a blow from behind) is brought back smoothly. Measured from where the view is now, so a
    // quick flick past 180 degrees from the centre can't come round on the arc's other side.
    private float WithinArc(float center, float limit, float candidate)
    {
        float now = transform.eulerAngles.y, from = Mathf.DeltaAngle(center, now);
        float clamped = center + Mathf.Clamp(from + Mathf.DeltaAngle(now, candidate), -limit, limit);
        if (Mathf.Abs(from) > limit + .01f)
            return Mathf.MoveTowardsAngle(now, clamped, 360f * Time.deltaTime);
        return clamped;
    }

    private float ClampLookPitch(float candidate)
    {
        float low = minPitch, high = maxPitch;
        if (movement != null && movement.CurrentState == PlayerMovement.MovementState.Sliding)
        {
            if (body == null) body = GetComponent<PlayerBody>();
            Vector2 limits = body != null ? body.SlideLookPitchLimits : new Vector2(40f, 55f);
            low = Mathf.Max(low, -limits.x);
            high = Mathf.Min(high, limits.y);
        }
        return Mathf.Clamp(candidate, low, high);
    }
}
