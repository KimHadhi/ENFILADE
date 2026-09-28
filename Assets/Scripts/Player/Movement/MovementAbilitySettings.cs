using System;
using UnityEngine;

[Serializable]
public sealed class MovementAbilitySettings
{
    [Header("Dodge & Lean")]
    [Min(0.05f)] public float stepDuration = 0.14f;
    [Min(0f)] public float stepDistance = 3.2f;
    [Min(0f)] public float stepInvulnerability = 0.18f;
    [Range(0f, 0.3f)] public float stepDodgeRecognitionPadding = 0.12f;
    [Min(0f)] public float microInvulnerability = 0.16f;
    [Min(0f)] public float dodgeCooldown = 0.55f;
    [Min(0f)] public float microSuccessDistance = 0.3f;
    [Min(0.02f)] public float microSuccessDuration = 0.1f;
    [Range(0f, 30f)] public float leanAngle = 14f;
    [Range(0f, 0.5f)] public float leanOffset = 0.2f;
    [Min(1f)] public float leanSharpness = 18f;

    [Header("Step / Vault / Mantle")]
    [Range(0.05f, 0.5f)] public float stepHeight = 0.3f;
    [Min(0.1f)] public float obstacleReach = 1.35f;
    [Min(0.3f)] public float vaultMaxHeight = 1.15f;
    [Min(0.3f)] public float mantleMaxHeight = 2.1f;
    [Min(0.2f)] public float vaultMaxDepth = 1.5f;
    [Range(0.01f, 0.2f)] public float vaultReferenceLerp = 0.1f;
    [Range(0.01f, 0.2f)] public float mantleReferenceLerp = 0.075f;
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

    [Header("Shove / Tackle")]
    [Min(0.1f)] public float shoveReach = 1.5f;
    [Min(0.1f)] public float shoveRadius = 0.5f;
    [Min(0f)] public float shoveImpulse = 5f;
    [Min(0f)] public float shoveDamage = 5f;
    [Min(0.05f)] public float shoveCooldown = 0.5f;
    [Min(0f)] public float tackleMinSpeed = 6f;
    [Min(0.05f)] public float tackleDuration = 0.5f;
    [Min(0f)] public float tackleSpeed = 9f;
    [Min(0f)] public float tackleDamage = 20f;
    public LayerMask interactionLayers = ~0;
}
