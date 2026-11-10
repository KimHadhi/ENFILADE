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
    [Range(0f, 30f)] public float leanAngle = 18f;
    [Range(0f, 0.5f)] public float leanOffset = 0.35f;
    [Min(1f)] public float leanSharpness = 10f;

    [Header("Step / Vault / Mantle")]
    [Range(0.05f, 0.5f)] public float stepHeight = 0.3f;
    [Tooltip("엎드리거나 누워서 이동할 때 넘을 수 있는 작은 단차 높이. 접지 중에만 적용됩니다.")]
    [Range(0f, 0.1f)] public float proneStepHeight = 0.05f;
    [Tooltip("Down on the ground, getting up by key waits at least this long (s); a press before then gets up when it ends, a second press takes it back (backlog 134).")]
    [Min(0f)] public float proneMinHold = 0.45f;
    [Tooltip("After getting up, going down again in place waits this long (s) (backlog 134).")]
    [Min(0f)] public float proneReentryDelay = 0.35f;
    [Tooltip("A dive whose direction is at least this sideways (0-1, dot with the view's right) is a side dive: the body lies on its side, head leading (backlog 135).")]
    [Range(0.3f, 1f)] public float sideDiveMinSideways = 0.7f;
    [Tooltip("Lying down, pushing the mouse this far (degrees) past the view's limit (PlayerBody.lyingLookLimit) rolls the player over, front to back or back to front.")]
    [Min(0f)] public float lyingRollMousePush = 25f;
    [Tooltip("How long rolling over carries the view round (s).")]
    [Min(0.05f)] public float lyingRollDuration = 0.3f;
    [Tooltip("넘기 세 종류의 이동 속도 배율. 1이면 Beatrun 원본.")]
    [Min(0.1f)] public float vaultRate = 1f;
    [Tooltip("달리며 낮은 장애물 앞에서 일찍 누른 점프가 넘기가 걸리는 자리까지 빠르게 달려가 넘는 최대 거리(m). 0이면 Beatrun처럼 약 1.2m 안에서 눌러야 넘는다.")]
    [Min(0f)] public float vaultSurgeDistance = 3f;
    [Tooltip("그렇게 달려갈 때의 가장 느린 속도(m/s). 이미 더 빠르면 그 속도를 유지한다.")]
    [Min(1f)] public float vaultSurgeSpeed = 11f;
    [Tooltip("달려가기 시작할 때 카메라를 미는 힘: 앞으로 숙이는 회전(도/초), 시야각을 넓히는 양(도/초). 화면 흔들림 설정을 따른다.")]
    public Vector2 vaultSurgeKick = new Vector2(25f, 120f);
    [Tooltip("긴 넘기(kong)를 클립의 손이 장애물을 짚는 대로 옮긴다(WarpedTraversal). 끄면 Beatrun 직선 경로.")]
    public bool warpKong = true;
    [Tooltip("보통 넘기(over)를 클립의 손이 장애물을 짚는 대로 옮긴다.")]
    public bool warpOver = false;
    [Tooltip("높은 넘기를 클립의 손이 장애물을 짚는 대로 옮긴다.")]
    public bool warpOverHigh = false;
    [Tooltip("손 짚는 넘기에서 클립 재생 속도의 범위: 가장 느리게;가장 빠르게. 달리던 속도에 맞추되 이 안에서만.")]
    public Vector2 vaultWarpRates = new Vector2(.7f, 1.8f);
    [Tooltip("손 짚는 넘기의 접근 속도로 보는 가장 느린 속도(m/s). 걷다가 넘어도 클립이 늘어지지 않게.")]
    [Min(.1f)] public float vaultWarpLeastSpeed = 3f;
    [Tooltip("손바닥을 장애물 앞 모서리에서 위쪽 면 안으로 이만큼 들여 짚는다(m).")]
    [Min(0f)] public float vaultPalmInset = .04f;
    [Tooltip("손 짚는 자리까지 발이 가야 할 거리가 이보다 멀면 손 짚는 넘기를 하지 않고 Beatrun 직선 경로로 넘는다(m).")]
    [Min(.1f)] public float vaultWarpReach = 2.5f;
    [Tooltip("바로 올라타기와 높은 올라서기의 이동 속도 배율. 1이면 Beatrun 원본.")]
    [Min(0.1f)] public float mountRate = 1f;
    [Tooltip("매달린 뒤 올라가기의 이동 속도 배율. 1이면 Beatrun 원본(약 0.93초).")]
    [Min(0.1f)] public float heaveRate = 1f;
    [Tooltip("바·사다리 꼭대기처럼 경로를 따로 쓰는 올라서기의 시간(초) = 이 값 + 높이 1m마다 mantleDurationPerMeter.")]
    [Min(0.1f)] public float mantleDuration = 0.45f;
    [Min(0f)] public float mantleDurationPerMeter = 0.3f;
    [Range(45f, 75f)] public float slideMaxSlopeAngle = 65f;

    [Header("Ledge Hang")]
    [Min(0.1f)] public float ledgeReach = 1f;
    [Min(0f)] public float ledgeReachAboveHands = 0.3f;
    [Min(0.05f)] public float ledgeCatchDuration = 0.14f;
    [Min(0.1f)] public float ledgeRegrabDelay = 0.4f;
    [Min(0f)] public float ledgeShimmySpeed = 1.8f;
    [Min(0f)] public float airTraversalMaxFallSpeed = 8.75f;
    [Range(1f, 89f)] public float ledgeJumpLookAngle = 42f;
    [Min(0f)] public float ledgeJumpSpeed = 8.5f;
    [Min(0f)] public float ledgeJumpUpSpeed = 6.5f;

    [Header("Wall Movement")]
    [Min(0.1f)] public float wallReach = 0.85f;
    [Tooltip("벽 오르기 한 번의 기본 시간(초). Beatrun 원본 1.5. 오르는 속도가 남은 시간에 따라 줄어든다(WallClimbPace).")]
    [Min(0.1f)] public float wallClimbTime = 1.5f;
    [Tooltip("벽 오르기 속도 배율. 1이면 Beatrun 원본. 오르는 높이가 이 배율만큼 바뀐다.")]
    [Min(0f)] public float wallClimbRate = 1f;
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
    [Tooltip("Hand over hand along a bar, sideways input (m/s). Slows as the swing speeds up (backlog 122).")]
    [Min(0f)] public float barShimmySpeed = 1.2f;
    [Tooltip("Hanging nearly still, pushing the mouse this far (degrees) past the bar's look limit turns the player round (backlog 123).")]
    [Min(0f)] public float barTurnMousePush = 25f;
    [Tooltip("How long the half turn round on the bar takes (s).")]
    [Min(0.05f)] public float barTurnDuration = 0.35f;

    [Header("Ladder (MovementFixtures, LadderState)")]
    [Tooltip("Farthest out from the rungs a ladder can be taken hold of (m).")]
    [Min(0f)] public float ladderReach = .9f;
    [Tooltip("How far beyond its rails a ladder can be taken hold of (m).")]
    [Min(0f)] public float ladderSideReach = .2f;
    [Tooltip("The view must be within this of facing the ladder (degrees).")]
    [Range(1f, 90f)] public float ladderFacingAngle = 30f;
    [Tooltip("Where the body stands on a ladder, out from the plane of its rungs (m).")]
    [Min(0f)] public float ladderStandOff = .27f;
    [Tooltip("Seconds stepping onto a ladder from the ground takes, before the first rung.")]
    [Min(.01f)] public float ladderEnterTime = .5f;
    [Tooltip("Seconds being pulled onto a ladder caught in the air takes; seconds before the first rung after one.")]
    [Min(.01f)] public float ladderCatchTime = .2f, ladderCatchDelay = .75f;
    [Tooltip("Height of one rung (m); seconds moving up it; seconds from one rung to the next.")]
    [Min(.01f)] public float ladderRungHeight = .39f, ladderRungTime = .25f, ladderRungInterval = .35f;
    [Tooltip("Speed of the slide down with S (m/s).")]
    [Min(.1f)] public float ladderSlideSpeed = 8.5f;
    [Tooltip("The feet stop this far below the top of the rails; from there the player climbs off (m).")]
    [Min(0f)] public float ladderTopGap = 1.875f;
    [Tooltip("Where the climb off the top looks for a floor: this far in from the body, down from this far above the feet (m).")]
    [Min(0f)] public float ladderExitReach = .875f, ladderExitProbeHeight = 2.5f;
    [Tooltip("Seconds the climb off the top takes.")]
    [Min(.1f)] public float ladderExitDuration = 1.5f;
    [Tooltip("Seconds getting onto a ladder from its top takes: out over the edge, then down onto the top rungs (backlog 141).")]
    [Min(.1f)] public float ladderTopEnterTime = 1.1f;
    [Tooltip("Getting on from the top (Interact, looking out over it), how far back from the ladder on the floor up there it can be taken (m).")]
    [Min(0f)] public float ladderTopEnterReach = .9f;
    [Tooltip("Speed the player leaves a ladder with on letting go (m/s).")]
    [Min(0f)] public float ladderDropSpeed = 2f;
    [Tooltip("Seconds after letting go before a ladder can be taken hold of again.")]
    [Min(0f)] public float ladderRegrabDelay = .4f;

    [Header("Zipline (MovementFixtures, ZiplineState)")]
    [Tooltip("The cable catches the player within this distance sideways (m).")]
    [Min(0f)] public float ziplineReach = .8f;
    [Tooltip("...and this far above their feet: lowest; highest (m).")]
    public Vector2 ziplineReachHeight = new Vector2(1.44f, 3.6f);
    [Tooltip("The feet hang this far below the cable (m).")]
    [Min(0f)] public float ziplineHangLength = 1.875f;
    [Tooltip("Seconds being pulled in under the cable takes.")]
    [Min(.01f)] public float ziplineCatchTime = .2f;
    [Tooltip("Top speed of the ride (m/s); how fast it builds (m/s²). The ride starts at the speed of the run-up.")]
    [Min(0f)] public float ziplineMaxSpeed = 18.75f, ziplineAcceleration = 6.25f;
    [Tooltip("Share of the ride's speed kept on letting go.")]
    [Range(0f, 1f)] public float ziplineReleaseShare = .75f;
    [Tooltip("Seconds after letting go before a cable catches the player again.")]
    [Min(0f)] public float ziplineRegrabDelay = .75f;

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
    [Tooltip("Slower than this across the ground (m/s), or pulling back, a landing roll lands on the spot instead of rolling. "
        + "Below the walk's start speed, so a walk off a drop rolls too; Beatrun's 200 units/s is about the walk speed itself.")]
    [Min(0f)] public float rollLandSpeed = 2f;
    [Tooltip("Seconds a landing on the spot holds the player there.")]
    [Min(0.05f)] public float rollLandTime = 0.6f;
    [Tooltip("Share of the fall damage a landing on the spot still takes: with no run to roll into, the legs take it. A big fall can't be rolled at all, as in Beatrun.")]
    [Range(0f, 1f)] public float rollInPlaceDamageShare = .5f;
    [Tooltip("Seconds a landing on the spot that hurt holds the player there, the legs getting over it before they walk.")]
    [Min(0.05f)] public float rollLandHurtTime = 1f;
    [Tooltip("Seconds getting up off the back keeps the player from moving off while the body gets up, as Beatrun's "
        + "JumpTurnRecovery does. The hands stay free: shooting, aiming, the shove and the fists work.")]
    [Min(0f)] public float supineRiseTime = 1f;

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
    [Tooltip("Shove fatigue (L4D2): from this shove in a row, gun or fists, the wait before the next one grows.")]
    [Min(1)] public int shoveFatigueStart = 4;
    [Tooltip("At this shove in a row the wait reaches shoveFatigueInterval, and stays there while the spam goes on.")]
    [Min(1)] public int shoveFatigueMost = 7;
    [Tooltip("The longest wait between shoves (s).")]
    [Min(0f)] public float shoveFatigueInterval = 1.2f;
    [Tooltip("Each this long without a shove (s) takes one shove off the count.")]
    [Min(.05f)] public float shoveFatigueRecovery = 1f;
    [Header("Dash Push (running + V)")]
    [Tooltip("Running at least this fast (m/s), along the view, turns V into a dash push.")]
    [Min(0f)] public float dashPushMinSpeed = 5f;
    [Tooltip("How long the dash lasts unless it meets someone (s).")]
    [Min(0.05f)] public float dashPushDuration = .25f;
    [Tooltip("The dash runs at this many times the speed it started from.")]
    [Min(1f)] public float dashPushSpeedScale = 2f;
    [Tooltip("Time after a dash ends before the next one (s). V meanwhile does what it does standing (crowd shove, or the fists' shove and guard break).")]
    [Min(0f)] public float dashPushCooldown = 1f;
    [Tooltip("A dash push counts as this many shoves toward shove fatigue, which the shoves share.")]
    [Min(0f)] public float dashPushFatigueCost = 2f;
    [Tooltip("Longest wait before the next dash once shove fatigue is at its most (s); fresh, it is dashPushCooldown.")]
    [Min(0f)] public float dashPushFatigueCooldown = 4f;
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
