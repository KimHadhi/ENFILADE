using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ProneState : MovementBaseState
{
    public ProneState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override string Name => "Prone";
    public override MovementPosture Posture => MovementPosture.Prone;

    // Backlog 134: spamming down and up flapped the body like wings. Lying down now lasts at least proneMinHold; a
    // get-up press before then is kept and carried out when it ends, and pressing again takes it back.
    private float enteredAt;
    private bool riseQueued, queuedCrouch;
    private bool Settled => Time.time - enteredAt >= ctx.AbilitySettings.proneMinHold;

    public override void Enter() { enteredAt = Time.time; riseQueued = false; }
    public override void Exit() { riseQueued = false; ctx.Actions.LeftProneAt = Time.time; }

    private int queuedFrame = -1;
    private void QueueRise(bool toCrouch)
    {
        // A second press while one waits takes it back, so the presses' count decides where the player ends up.
        // One frame's keys count once (on the back, W and Space can both ask in the same frame).
        if (queuedFrame == Time.frameCount) return;
        queuedFrame = Time.frameCount;
        riseQueued = !riseQueued;
        queuedCrouch = toCrouch;
    }

    public override bool TryJump()
    {
        if (ctx.IsDowned || ctx.HasCeilingAbove(ctx.CrouchColliderHeight)) return false;
        if (!Settled)
        {
            // A held W on the back (SupineState) asks every frame; only a fresh press counts toward the queue.
            if (ctx.Input.JumpPressed) QueueRise(false);
            return ctx.Input.JumpPressed;
        }
        ctx.ChangeState(RiseTo());
        return true;
    }

    // Getting up keeps a held crouch.
    private PlayerMovement.MovementState RiseTo() => ctx.Input.CrouchHeld || ctx.HasCeilingAbove(ctx.StandingColliderHeight)
        ? PlayerMovement.MovementState.Crouching : PlayerMovement.MovementState.Standing;

    public override void ProcessInput(Vector2 input)
    {
        // Someone downed crawls, and only a friend gets them up.
        if (ctx.IsDowned) return;
        // 포복 중 실제로 떨어지기 시작하면 다이빙으로 복귀
        if (!ctx.IsGrounded && ctx.ActualVelocity.y < -0.15f)
        {
            bool onBack = ctx.CurrentState == PlayerMovement.MovementState.Supine;
            // The dive goes the way the body lies, so one on the back faces the view as the body did (PlayerLook, PlayerBody).
            Vector3 facing = Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up).normalized;
            ctx.DiveBodyForward = onBack ? -facing : facing;
            ctx.ChangeState(onBack ? PlayerMovement.MovementState.SupineDiving : PlayerMovement.MovementState.Diving);
            return;
        }

        // A get-up asked for too early happens now that the body has settled.
        if (riseQueued && Settled)
        {
            riseQueued = false;
            if (!ctx.HasCeilingAbove(ctx.CrouchColliderHeight))
            {
                ctx.ChangeState(queuedCrouch ? PlayerMovement.MovementState.Crouching : RiseTo());
                return;
            }
        }

        bool crouchPressed = ctx.Input.CrouchHeld;
        bool crouchJustPressed = ctx.Input.CrouchPressed;

        bool diveJustPressed = ctx.InputAvailable && (
            (ctx.DiveAction != null && ctx.DiveAction.WasPressedThisFrame())
        );

        bool sprintHeld = ctx.InputAvailable && ctx.Input.SprintButtonHeld;

        bool sprintJustPressed = ctx.InputAvailable
            && ctx.SprintAction != null
            && ctx.SprintAction.WasPressedThisFrame();

        bool hasMoveInput = input.sqrMagnitude > 0.01f;
        bool wantsFastGetUp =
            (sprintHeld && hasMoveInput) || (sprintJustPressed && hasMoveInput);

        // 1. 달리기 기상 캔슬. 앉기를 누르고 있으면 3번이 앉기로 일으킨다
        if (wantsFastGetUp && Settled && !crouchPressed && !ctx.HasCeilingAbove(ctx.StandingColliderHeight))
        {
            ctx.SprintProgress = Mathf.Max(ctx.SprintProgress, 0.6f);

            Vector3 moveDirection =
                (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;

            float getUpSpeed = Mathf.Max(ctx.HorizontalVelocity.magnitude, ctx.WalkSpeed);
            ctx.HorizontalVelocity = moveDirection * getUpSpeed;

            ctx.ChangeState(PlayerMovement.MovementState.Standing);
            return;
        }

        // 2. 앉기 또는 다이빙 키를 다시 눌러 일어서기
        if ((crouchJustPressed || diveJustPressed) && !Settled)
        {
            QueueRise(crouchJustPressed);
            return;
        }
        if ((crouchJustPressed || diveJustPressed)
            && !ctx.HasCeilingAbove(ctx.CrouchColliderHeight))
        {
            ctx.ChangeState(crouchJustPressed ? PlayerMovement.MovementState.Crouching : RiseTo());
            return;
        }

        // 3. 앉기 키 유지 중 환풍구 탈출 시 앉기로 복귀
        if (crouchPressed && ctx.CurrentStateDuration > 0.15f)
        {
            Vector3 moveDir = hasMoveInput
                ? (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized
                : ctx.transform.forward;

            bool isStillHeadingIntoVent = hasMoveInput && ctx.HasVentCeilingAhead(moveDir, ctx.proneEntryLookAhead);

            // 현재 위치 + 0.35m 앞쪽 여유 공간까지 모두 확보되었을 때만 Crouching으로 전이
            bool canSafelyRise = !ctx.HasCeilingAbove(ctx.CrouchColliderHeight)
                              && (!hasMoveInput || !ctx.HasCeilingAhead(moveDir, 0.35f, ctx.CrouchColliderHeight));

            if (!isStillHeadingIntoVent && canSafelyRise)
            {
                ctx.ChangeState(PlayerMovement.MovementState.Crouching);
            }
        }
    }
}
