using UnityEngine;

/// <summary>
/// How a move's own clip carries the body over an obstacle on its hands, on the body that plays it: when the hands meet
/// the obstacle, when they leave it, and where they are from the player's feet at any moment of the clip as the body is
/// posed now. The body measures it from the clip itself (PlayerBody, ObstacleClipContacts), so any clip that puts its hands
/// down works, including ones swapped in later; WarpedTraversal moves the player by it.
/// </summary>
public interface IObstacleMotion
{
    /// <summary>The clip's length (s).</summary>
    float Length { get; }
    /// <summary>Clip time (s) when the hands are down on the obstacle.</summary>
    float PlantTime { get; }
    /// <summary>Clip time (s) when the first of them leaves it.</summary>
    float ReleaseTime { get; }
    /// <summary>From the capsule's feet to the planting hands (their middle), in the world, at <paramref name="clipTime"/>.</summary>
    Vector3 HandsFromFeet(float clipTime);
    /// <summary>How far the wrist (the hand bone) sits above a top the palm rests on (m), from the character's own hand.</summary>
    float WristAboveTop { get; }
}

/// <summary>The body that plays the moves (PlayerBody), asked by the movement before a vault starts.</summary>
public interface IObstacleMotionSource
{
    /// <summary>
    /// Picks the clip this obstacle will play and returns how its hands carry the body, or false when the clip puts no
    /// hands down or nothing could be measured.
    /// </summary>
    bool TryObstacleMotion(VaultKind kind, out IObstacleMotion motion);
    /// <summary>
    /// How the clip that climbs off a ladder's top (by the hand that went up last) carries the body on its hands onto the
    /// floor up there, or false when its hands put down on nothing or nothing could be measured.
    /// </summary>
    bool TryLadderExitMotion(bool leftHand, out IObstacleMotion motion);
}
