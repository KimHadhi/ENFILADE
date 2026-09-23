using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class PlayerSupineMotion : MonoBehaviour
{
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private Transform visualRoot;

    [Header("Leg Lift Animation Tuning")]
    [Tooltip("체공 시간에 비례해 다리가 올라가는 속도")]
    [SerializeField] private float legLiftSharpness = 6.0f;
    [Tooltip("착지 시 다리가 바닥으로 내려앉는 속도")]
    [SerializeField] private float landSnapSharpness = 18.0f;

    private float currentLegLift; // 0.0 (바닥) ~ 1.0 (공중 다리 완전 들림)

    private void Awake()
    {
        if (movement == null)
            movement = GetComponentInParent<PlayerMovement>();
    }

    private void LateUpdate()
    {
        if (movement == null || visualRoot == null) return;

        float dt = Time.deltaTime;
        PlayerMovement.MovementState state = movement.CurrentState;

        // 공중 SupineDiving 상태일 때는 체공 시간에 비례해 다리를 위로 들어 올림
        if (state == PlayerMovement.MovementState.SupineDiving)
        {
            float targetLift = Mathf.Clamp01(movement.CurrentStateDuration * 2.5f);
            currentLegLift = Mathf.Lerp(currentLegLift, targetLift, 1f - Mathf.Exp(-legLiftSharpness * dt));
        }
        else
        {
            // 지상 착지 (SupineSliding, Supine 등): 빠르게 털썩 지면으로 안착
            currentLegLift = Mathf.Lerp(currentLegLift, 0f, 1f - Mathf.Exp(-landSnapSharpness * dt));
        }

        // 여기에 기존에 작업해 두셨던 본(Bone) 회전 또는 애니메이터 Weight(currentLegLift)를 적용해 주시면 됩니다.
    }
}