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

    [Header("Landing Tiers, Roll & Fall Damage (MovementLanding)")]
    [Tooltip("Landing on the feet faster than this is a hard landing (m/s, 12 is about 3 m): the run stops, the player "
        + "slows down and cannot sprint for a moment. Ctrl just before touchdown rolls instead.")]
    [Min(0f)] public float hardLandingSpeed = 12f;
    [Tooltip("Seconds a hard landing takes to get back to full ground speed.")]
    [Min(0f)] public float hardLandingRecoverTime = 1f;
    [Tooltip("Share of the ground speed right after a hard landing; it eases back to 1 over hardLandingRecoverTime.")]
    [Range(0f, 1f)] public float hardLandingMoveScale = .1f;
    [Tooltip("Seconds after a hard landing before Shift or a W run can build speed again.")]
    [Min(0f)] public float hardLandingNoSprintTime = .5f;
    [Tooltip("Landing faster than this after falling farther than safeFallDistance hurts and is a heavy landing: the "
        + "player goes down and gets up again without control.")]
    [Min(0f)] public float safeFallSpeed = 16f;
    [Min(0f)] public float safeFallDistance = 4f;
    [Min(0f)] public float fallDamagePerSpeed = 4f;
    [Tooltip("A heavy landing faster than this falls flat on the face instead of onto hands and knees (m/s).")]
    [Min(0f)] public float faceDownLandingSpeed = 26f;
    [Tooltip("A heavy landing moving backwards faster than this (against the view) falls onto the back (m/s).")]
    [Min(0f)] public float backLandingMinSpeed = .4f;
    [Tooltip("Seconds a heavy landing keeps the player down: onto hands and knees, onto the face, onto the back.")]
    [Min(.1f)] public float heavyLandingTime = 1.6f, faceDownLandingTime = 1.5f, backLandingTime = 2.8f;
    [Tooltip("How quickly a heavy landing's skid along the ground dies away (share of the speed per second).")]
    [Min(0f)] public float heavyLandingBraking = 10f;
    [Min(0.01f)] public float rollInputWindow = 0.5f;
    [Min(0.05f)] public float rollDuration = 1f;
    [Tooltip("Slowest a landing roll moves (m/s); a faster landing keeps its speed.")]
    [Min(0f)] public float rollSpeed = 4f;

    [Header("Big Fall")]
    [Tooltip("A fall this long from its top to the ground below is a big one: wind, heartbeat, screen warp, flailing (m).")]
    [Min(0f)] public float bigFallHeight = 8.5f;
    [Tooltip("Only falling faster than this can be a big fall (m/s), so a jump across a gap never is.")]
    [Min(0f)] public float bigFallMinSpeed = 9f;

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
    [Min(0f)] public float shoveDamage = 5f;
    [Min(0.05f)] public float shoveCooldown = 0.5f;
    [Header("Dash Push (running + V)")]
    [Tooltip("Running at least this fast (m/s), along the view, turns V into a dash push.")]
    [Min(0f)] public float dashPushMinSpeed = 5f;
    [Tooltip("How long the dash lasts unless it meets someone (s).")]
    [Min(0.05f)] public float dashPushDuration = .25f;
    [Tooltip("The dash runs at this many times the speed it started from.")]
    [Min(1f)] public float dashPushSpeedScale = 2f;
    [Tooltip("Time after a dash ends before the next one (s). V meanwhile does what it does standing (crowd shove, or the fists' shove and guard break).")]
    [Min(0f)] public float dashPushCooldown = 1f;
    [Tooltip("The dash meets someone this far ahead of the body (m).")]
    [Min(0f)] public float dashPushReach = .5f;
    [Min(0f)] public float dashPushDamage = 20f;
    [Min(0f)] public float dashPushPostureDamage = 20f;
    [Tooltip("The enemy met is thrown along the ground at the dash speed, slowing to a stop over this long (s), and falls at the end.")]
    [Min(0.05f)] public float dashPushThrowTime = .5f;
    [Tooltip("How long the thrown enemy stays down (s).")]
    [Min(0.1f)] public float dashPushKnockdownDuration = 1.5f;
    [Header("Physics Props (prop mass decides the result)")]
    [Tooltip("Force (N) the walking body leans on a PhysicsProp with. Light props reach walking speed at once; heavy ones creep.")]
    [Min(0f)] public float propPushForce = 300f;
    [Tooltip("Momentum (N·s) a V shove gives a PhysicsProp, up to Shove Push Speed.")]
    [Min(0f)] public float propShoveImpulse = 60f;
    [Tooltip("Body mass (kg) for slides, dives and dash pushes into a PhysicsProp. Much lighter props are kicked away and the move carries on; one that would take most of the speed stops the player like a wall.")]
    [Min(1f)] public float propBodyMass = 70f;
    public LayerMask interactionLayers = ~0;
}
