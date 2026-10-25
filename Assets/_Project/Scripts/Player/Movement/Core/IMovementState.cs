using UnityEngine;

public interface IMovementState
{
    /// <summary> 상태 진입 시 1회 호출 </summary>
    void Enter();

    /// <summary> 상태 탈출 시 1회 호출 </summary>
    void Exit();

    /// <summary> 플레이어 입력 및 상태 전환(Transition) 검사 </summary>
    void ProcessInput(Vector2 input);

    /// <summary> 일반 로직 업데이트 (타이머, 파라미터 등) </summary>
    void UpdateLogic(float dt);

    /// <summary> 수평 속도(horizontalVelocity) 등 이동 물리 연산 </summary>
    void UpdatePhysics(float dt);

    /// <summary> 충돌 처리 (벽 반동, 문 파괴 등) </summary>
    void OnColliderHit(ControllerColliderHit hit);

    /// <summary> 착지 이벤트. 처리하지 않은 이벤트는 부모 상태로 전달 </summary>
    void OnLanding();
    /// <summary> 점프 또는 기상 입력을 소비했으면 true </summary>
    bool TryJump();

    /// <summary> 중력 적용 여부 (파쿠르, 특수 액션 시 false로 오버라이드 가능) </summary>
    bool CanApplyGravity { get; }
}
