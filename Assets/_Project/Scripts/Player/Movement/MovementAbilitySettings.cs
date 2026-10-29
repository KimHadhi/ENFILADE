using System;
using UnityEngine;

/// <summary>Dodge, lean, parkour, fall and shove numbers. Set from Resources/Tables/Player.csv, row Ability, by field name.</summary>
[Serializable]
public sealed class MovementAbilitySettings
{
    [Header("Dodge & Lean")]
    [Min(0.05f)] public float stepDuration = 0.14f;
    [Min(0f)] public float stepDistance = 2.6f;
    [Tooltip("회피가 적 몸 안에서 끝나면 진행 방향의 안전한 출구를 찾는 탐색 거리.")]
    [Min(0f)] public float dodgeExitAssistDistance = 1.6f;
    [Min(.01f)] public float dodgeExitAssistTime = .2f;
    [Tooltip("입력부터 공격을 회피로 인정하는 게임 시간(초). 이동 종료 뒤에도 이 시간까지 인정합니다.")]
    [Min(0f)] public float stepInvulnerability = 0.18f;
    [Range(0f, 0.3f)] public float stepDodgeRecognitionPadding = 0.12f;
    [Tooltip("Q/E 입력 직후 공격을 회피로 인정하는 게임 시간(초). 짧을수록 기울이기 도중 뒤늦게 성공하는 경우가 줄어듭니다.")]
    [Min(0f)] public float microInvulnerability = 0.08f;
    [Min(0f)] public float dodgeCooldown = 0.55f;
    [Tooltip("스텝 닷지 실패 시 다음 회피까지의 시간. 회피 성공 시 공통 쿨타임이 즉시 초기화됩니다.")]
    [Min(0f)] public float stepDodgeCooldown = 1.25f;
    [Min(0f)] public float microSuccessDistance = 0.3f;
    [Tooltip("마이크로 닷지 성공 시 짧게 가속·감속하며 이동하는 실제 시간(초). 슬로모션에도 동일합니다.")]
    [Min(0.02f)] public float microSuccessDuration = 0.1f;
    [Tooltip("마이크로 닷지 성공 시 현재 기울기에서 끝까지 빠르게 이어지는 실제 시간(초). 순간 이동하지 않습니다.")]
    [Range(.02f, .2f)] public float microSuccessLeanDuration = .08f;
    [Range(0f, 30f)] public float leanAngle = 10f;
    [Range(0f, 0.5f)] public float leanOffset = 0.35f;
    [Min(1f)] public float leanSharpness = 10f;

    [Header("Step / Vault / Mantle")]
    [Range(0.05f, 0.5f)] public float stepHeight = 0.3f;
    [Tooltip("엎드리거나 누워서 이동할 때 넘을 수 있는 작은 단차 높이. 접지 중에만 적용됩니다.")]
    [Range(0f, 0.1f)] public float proneStepHeight = 0.05f;
    [Min(0.1f)] public float obstacleReach = 1.35f;
    [Min(0.3f)] public float vaultMaxHeight = 1.15f;
    [Min(0.3f)] public float mantleMaxHeight = 2.1f;
    [Min(0.2f)] public float vaultMaxDepth = 1.5f;
    [Tooltip("올라서기에 걸리는 시간(초) = 이 값 + 높이 1m마다 mantleDurationPerMeter.")]
    [Min(0.1f)] public float mantleDuration = 0.45f;
    [Min(0f)] public float mantleDurationPerMeter = 0.3f;
    [Tooltip("땅이나 공중에서 장애물에 바로 올라서기. 끄면 올라서기는 난간에 매달린 뒤에만 되고, 장애물 앞에서는 넘기만 한다.")]
    public bool mantleFromGround;
    [Range(45f, 75f)] public float slideMaxSlopeAngle = 65f;

    [Header("Ledge Hang")]
    [Min(0.1f)] public float ledgeReach = 1f;
    [Min(0f)] public float ledgeReachAboveHands = 0.3f;
    [Min(0.05f)] public float ledgeCatchDuration = 0.14f;
    [Min(0.1f)] public float ledgeRegrabDelay = 0.4f;
    [Min(0f)] public float ledgeShimmySpeed = 1.8f;
    [Min(0f)] public float airTraversalMaxFallSpeed = 8f;
    [Range(1f, 89f)] public float ledgeJumpLookAngle = 42f;
    [Min(0f)] public float ledgeJumpSpeed = 8.5f;
    [Min(0f)] public float ledgeJumpUpSpeed = 6.5f;

    [Header("Wall Movement")]
    [Min(0.1f)] public float wallReach = 0.85f;
    [Min(0.1f)] public float wallDuration = 1.8f;
    [Min(0f)] public float wallClimbSpeed = 3.8f;
    [Range(0f, 1f)] public float wallClimbFacingDot = 0.9f;
    [Min(0f)] public float wallContactGrace = 0.1f;
    [Min(0f)] public float wallKickOutSpeed = 6f;
    [Min(0f)] public float wallKickUpSpeed = 6f;
    [Min(0f)] public float wallReattachDelay = 0.35f;

    [Header("Swing / Lache")]
    [Min(0.1f)] public float barGrabReach = 1.1f;
    [Min(0.1f)] public float barCatchDuration = 0.2f;
    [Min(0f)] public float swingPumpAcceleration = 4f;
    [Min(0.1f)] public float swingMaxAngularSpeed = 3.5f;
    [Min(0f)] public float lacheForwardBoost = 2f;
    [Min(0f)] public float lacheUpBoost = 2f;

    [Header("Landing Roll & Fall Damage")]
    [Min(0f)] public float safeFallSpeed = 16f;
    [Min(0f)] public float safeFallDistance = 4f;
    [Min(0f)] public float fallDamagePerSpeed = 4f;
    [Min(0.01f)] public float rollInputWindow = 0.22f;
    [Min(0.05f)] public float rollDuration = 0.65f;
    [Min(0f)] public float rollSpeed = 5.5f;

    [Header("Slide / Just Slide")]
    [Min(0f)] public float slideDamage = 15f;
    [Min(0f)] public float slideBoostSpeed = 1.5f;
    [Min(0f)] public float justSlideWindow = .18f;
    [Min(0f)] public float slideBoostCooldown = .55f;
    [Range(0f, 1f)] public float slideSmallSpeedRetention = .9f;
    [Min(0f)] public float slidePushImpulse = 7f;
    [Min(0f)] public float slidePushStun = .45f;
    [Min(.1f)] public float slideKnockdownDuration = 1.2f;

    [Header("Crowd Shove")]
    [Min(.02f)] public float shoveActiveTime = .2f;
    [Min(.1f)] public float shoveSweepReach = 2.2f;
    [Range(1f, 179f)] public float shoveSweepAngle = 120f;
    [Min(0f)] public float shovePushSpeed = 8f;
    [Min(0f)] public float shoveStun = .85f;
    [Min(0f)] public float shovePostureDamage = 8f;
    [Header("Tackle / Armed Shove")]
    [Min(0.1f)] public float shoveReach = 1.5f;
    [Min(0.1f)] public float shoveRadius = 0.5f;
    [Min(0f)] public float shoveImpulse = 5f;
    [Min(0f)] public float shoveDamage = 5f;
    [Min(0.05f)] public float shoveCooldown = 0.5f;
    [Min(0f)] public float tackleMinSpeed = 6f;
    [Min(0.05f)] public float tackleDuration = 0.5f;
    [Min(0f)] public float tackleSpeed = 9f;
    [Min(0f)] public float tackleDamage = 20f;
    [Header("Physics Props (prop mass decides the result)")]
    [Tooltip("Force (N) the walking body leans on a PhysicsProp with. Light props reach walking speed at once; heavy ones creep.")]
    [Min(0f)] public float propPushForce = 300f;
    [Tooltip("Momentum (N·s) a V shove gives a PhysicsProp, up to Shove Push Speed. A tackle gives twice this.")]
    [Min(0f)] public float propShoveImpulse = 60f;
    [Tooltip("Body mass (kg) for slides, dives and tackles into a PhysicsProp. Much lighter props are kicked away and the move carries on; one that would take most of the speed stops the player like a wall.")]
    [Min(1f)] public float propBodyMass = 70f;
    public LayerMask interactionLayers = ~0;
}
