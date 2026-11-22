using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Q/E lean's numbers (Player.csv, row Body) and the lean itself, BodyLean, which PlayerBody runs at these places:
/// Update (PlayerBody.AimLook): Tune, BlendHands, SpineRoll for the look bend; LateUpdate: MoveHips before the feet are
/// planted, ClipHeadOffset in FollowHead, HangArm and Carry in Look.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Min(0f), Tooltip("Q/E at a full lean, whatever is in the hands and whatever the body: how far the hips go over onto that "
        + "foot (m); the feet stay where they stood.")]
    private float leanHips = .07f;
    [SerializeField, Min(0f), Tooltip("The same: how far the eyes go sideways (m). The spine bends as far as the hips and the pelvis leave of it.")]
    private float leanEyes = .2f;
    [SerializeField, Range(0f, 15f), Tooltip("The same: how far the pelvis tips (degrees); the spine's bend goes on from it.")]
    private float leanPelvis = 5f;

    private readonly BodyLean bodyLean = new BodyLean();

    /// <summary>The roll (degrees, positive) that bends FPS Animation Framework's hold this lean, a full one, as
    /// <paramref name="chain"/> shares it (BodyLean.GunRoll). FPSBody tips it by that.</summary>
    internal float LeanGunRoll(IReadOnlyList<(Transform bone, float share)> chain) => bodyLean.GunRoll(chain);

    private void TuneLean() => bodyLean.Tune(leanHips, leanEyes, leanPelvis, blendTime);
}
