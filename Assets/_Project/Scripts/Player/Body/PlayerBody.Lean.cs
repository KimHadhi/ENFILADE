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
    [SerializeField, Min(0f), Tooltip("Q/E with the fists or a gun: the most the hips go over toward the lean (m). They go as far as the "
        + "spine's bend leaves of the peek, so the eyes reach it and the camera stays on them.")]
    private float leanHipReach = .2f;
    [SerializeField, Range(0f, 15f), Tooltip("Q/E with nothing in hand or the fists, at full lean: how far the pelvis tips the "
        + "way of the lean (degrees); the spine's bend goes on from it. The legs stay as the clip has them. 0: a level pelvis, as before.")]
    private float leanPelvisTilt = 3f;
    [SerializeField, Tooltip("Q/E with a gun out, at full lean, at the hip and down the sights alike: how far the spine bends sideways "
        + "(degrees), standing (x) and on the move (y). The hips go over as far as that leaves of the peek (leanHipReach).")]
    private Vector2 leanGunBend = new Vector2(18f, 24f);
    [SerializeField, Range(0f, 15f), Tooltip("The same: how far the pelvis tips the way of the lean (degrees).")]
    private float leanGunTilt = 4f;
    // On the move between these (m/s), over this long (s).
    private const float LeanStandsUnder = .3f, LeanMovesFrom = 1.2f, LeanMoveTime = .25f;
    private float leanMoving;

    private readonly BodyLean bodyLean = new BodyLean();

    /// <summary>The roll (degrees, positive) that bends FPS Animation Framework's hold the gun's lean, a full one, as
    /// <paramref name="chain"/> shares it (BodyLean.GunRoll). FPSBody tips it by that.</summary>
    internal float LeanGunRoll(IReadOnlyList<(Transform bone, float share)> chain) => bodyLean.GunRoll(chain);

    private void TuneLean(float dt)
    {
        leanMoving = Mathf.MoveTowards(leanMoving, Mathf.InverseLerp(LeanStandsUnder, LeanMovesFrom, HorizontalSpeed()), dt / LeanMoveTime);
        bodyLean.Tune(leanSpineBend, leanHipShift, leanHipReach, blendTime, leanPelvisTilt,
            Mathf.Lerp(leanGunBend.x, leanGunBend.y, leanMoving), leanGunTilt);
    }
}
