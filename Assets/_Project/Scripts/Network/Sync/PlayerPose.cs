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
    public enum Marks : ushort
    {
        None = 0, Grounded = 1, AirCrouching = 2, BigFall = 4, LadderCaught = 8, LadderLeftHand = 16, LadderSliding = 32,
        LadderBlocked = 64, LeavesLadder = 128, BareHanded = 256,
        // Holding F to point (PlayerPoint), and having switched friends' pokes off (GameSettings.letOthersPoke): the point
        // rides the look this pose already carries, so it costs the network these two bits and nothing more.
        Pointing = 512, NoPoke = 1024,
        // The point is a tap's ping's, at the foe the ping named (PlayerNetworkCombat), not along the look; and a ping's
        // point that turning away cut short is dropping back (PlayerPoint.TurnAway), so every copy drops it alike.
        PingPoint = 2048, PointAway = 4096,
        // The legs run ahead (PlayerMovement.RunsAhead): the hips face the way the body runs, not the view.
        Ahead = 8192,
        // A run's turn back goes on (RunTurn.Turning): a copy gives its own up when this goes, as the player gave it up.
        Turning = 16384
    }

    public Vector3 Velocity;
    /// <summary>The way the state goes: a slide, a dodge, the ledge's outward side, the cable.</summary>
    public Vector3 Direction;
    /// <summary>What the state holds: a ledge's edge, a ladder's foot, the start of a cable.</summary>
    public Vector3 Anchor;
    /// <summary>Where they look. It travels here, not through NetworkTransform, whose buffer makes a turn arrive late and trail off.</summary>
    public float Yaw, Pitch;
    public float Lean, RunIntent, FallSpeed, LandImpact;
    /// <summary>Which gait the legs go, 0 walk, 1 jog, 2 sprint (MovementGait.Position): the clips the legs play.</summary>
    public float Gait;
    /// <summary>The state's own numbers, see MovementMirror.</summary>
    public float Detail, Extent;
    public byte State, LandingTier, HeavyPose, LadderSteps, Vault;
    public byte Jumps, Landings, DashHits;
    /// <summary>A run's turn back (RunTurn): turns begun (an event), its clip's place in Ability.runTurns, the clip's moment it
    /// began at (s), the way it ends facing and the way the body faced as it began (world yaw).</summary>
    public byte Turns, TurnClip;
    public float TurnFrom, TurnYaw, TurnStart;
    /// <summary>Where the stride is, so the legs step in time on every PC.</summary>
    public byte Step;
    public Marks Flags;
    /// <summary>
    /// The tick of its owner's clock of the server it was taken on, stamped only when something else changed: every PC
    /// measures from it how late updates come in and sizes the interpolation buffer to it (NetSmoothing).
    /// </summary>
    public uint Tick;
}
