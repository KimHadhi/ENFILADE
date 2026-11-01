using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// What the other PCs need to show a player: enough for the copy's body and sounds, nothing that moves it (NetworkTransform
/// does that). Only what the state in progress needs is filled in, and an event is a counter that went up, so a pose
/// that hasn't changed sends nothing and a late joiner simply reads the latest one.
/// </summary>
public struct PlayerPose : INetworkSerializeByMemcpy
{
    [Flags]
    public enum Marks : byte
    {
        None = 0, Grounded = 1, AirCrouching = 2, BigFall = 4, LadderCaught = 8, LadderLeftHand = 16, LadderSliding = 32,
        LadderBlocked = 64, LeavesLadder = 128
    }

    public Vector3 Velocity;
    /// <summary>The way the state goes: a slide, a dodge, the ledge's outward side, the cable.</summary>
    public Vector3 Direction;
    /// <summary>What the state holds: a ledge's edge, a ladder's foot, the start of a cable.</summary>
    public Vector3 Anchor;
    /// <summary>Where they look. It travels here, not through NetworkTransform, whose buffer makes a turn arrive late and trail off.</summary>
    public float Yaw, Pitch;
    public float Lean, RunIntent, FallSpeed, LandImpact;
    /// <summary>The state's own numbers, see MovementMirror.</summary>
    public float Detail, Extent;
    public byte State, LandingTier, HeavyPose, LadderSteps;
    public byte Jumps, Landings, DashHits;
    public Marks Flags;
}
