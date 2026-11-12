using UnityEngine;

/// <summary>
/// A part of a steel target's stand (the base, hinge, post, bracket, arm and the head plate's strap; SteelTarget builds
/// them). It is solid steel, so a round stops on it and sparks (the surface SteelTarget), but it isn't the target: the
/// hit takes no health, shows no hit marker and rings nothing (the user's rule, a miss gives nothing). It answers before
/// the RangeTarget it stands under, which every hit on the target's other parts reaches.
/// </summary>
[DisallowMultipleComponent]
public sealed class SteelStand : MonoBehaviour, IDamageReceiver
{
    public DamageResult ReceiveHit(DamageHit hit) => DamageResult.Ignored;
}
