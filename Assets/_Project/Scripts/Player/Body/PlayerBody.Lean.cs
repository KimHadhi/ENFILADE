using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Q/E lean's numbers (Player.csv, row Body) and the lean itself, BodyLean, which PlayerBody runs at these places:
/// Update (PlayerBody.AimLook): Tune, BlendHands, SpineRoll for the look bend; LateUpdate: MoveHips before the feet are
/// planted, ClipHeadOffset in FollowHead, HangArm and Carry in Look.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Range(0f, 15f), Tooltip("Q/E at a full lean, whatever is in the hands: how far the legs lean over onto that foot from "
        + "where the feet stand (degrees); the hips go over with them.")]
    private float leanLegs = 3f;
    [SerializeField, Range(0f, 15f), Tooltip("The same: how far the pelvis tips (degrees).")]
    private float leanPelvis = 4f;
    [SerializeField, Range(0f, 40f), Tooltip("The same: how far the spine bends sideways on from the pelvis (degrees). The eyes go as far "
        + "as the three carry them.")]
    private float leanSpine = 11f;

    private readonly BodyLean bodyLean = new BodyLean();

    /// <summary>The roll (degrees, positive) that bends FPS Animation Framework's hold this lean, a full one, as
    /// <paramref name="chain"/> shares it (BodyLean.GunRoll). FPSBody tips it by that.</summary>
    internal float LeanGunRoll(IReadOnlyList<(Transform bone, float share)> chain) => bodyLean.GunRoll(chain);

    private void TuneLean() => bodyLean.Tune(leanLegs, leanPelvis, leanSpine, blendTime);
}
