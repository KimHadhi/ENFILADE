using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Q/E lean's numbers (Player.csv, row Body) and the lean itself, BodyLean, which PlayerBody runs at these places:
/// Update (PlayerBody.AimLook): Tune, BlendHands, SpineRoll for the look bend; LateUpdate: MoveHips before the feet are
/// planted, ClipHeadOffset in FollowHead, HangArm and Carry in Look, CameraMakesUp where the camera is placed.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Range(0f, 40f), Tooltip("Q/E with nothing in hand, at full lean: how far the spine bends sideways from the "
        + "waist (degrees). The head tilts only as far as the view rolls (Ability.leanAngle), so the neck takes back the difference.")]
    private float leanSpineBend = 20f;
    [SerializeField, Min(0f), Tooltip("Q/E with nothing in hand, at full lean: how far the hips go over toward the lean, the "
        + "weight onto that foot; the feet stay where they stood (m). The camera makes up what the body leaves of the peek.")]
    private float leanHipShift = .08f;
    [SerializeField, Min(0f), Tooltip("Q/E with the fists: the most the hips go over toward the lean (m). They go as far as the "
        + "spine's bend leaves of the peek, so the eyes reach it and the camera, which the guard is placed from, stays on them.")]
    private float leanHipReach = .2f;

    private readonly BodyLean bodyLean = new BodyLean();

    /// <summary>
    /// The roll (degrees, positive) that carries the eyes <paramref name="offset"/> (m) sideways when the spine tips as
    /// <paramref name="chain"/> shares it (BodyLean.RollFor). FPSBody tips FPS Animation Framework's lean by it.
    /// </summary>
    internal float LeanRollFor(float offset, IReadOnlyList<(Transform bone, float share)> chain) => bodyLean.RollFor(offset, chain);

    private void TuneLean() => bodyLean.Tune(leanSpineBend, leanHipShift, leanHipReach, blendTime);
}
