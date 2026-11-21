using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Going down (PlayerLife: health gone with a friend still standing) from on their feet, the body collapses before it lies
/// (the user, 2026-11-22, choosing where BIPER Combat Girls' falls go: "다운될 때 무너지기"): the knees give, the body
/// falls forward onto its front, and the crawl's pose takes over from where it lies. The fall is BIPER's P_Die_F, the
/// pistol version, as the downed hold their sidearm (PlayerDamageReceiver.SetDowned). The movement holds the crawl until
/// the body is down (PlayerMovement.DownedFalling, Move.downedFallTime), so the fall is never carried along the ground;
/// a crawl after that takes over at once. Death stays the ragdoll's (PlayerRagdoll), which falls the way the last blow
/// throws it. Until 0.9.298 going down blended straight into the crawl's pose within the crossfade.
/// A copy shows it the same way. Its state and its downed flag come in different messages (PlayerNetworkPose,
/// PlayerNetworkCombat), so the fall starts on the first frame of lying that both say, if that is within the crossfade:
/// past it the crawl's pose has taken over.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Downed")]
    [SerializeField, Tooltip("Going down from on the feet: the collapse onto the front before the crawl's pose (BIPER P_Die_F).")]
    private string downedFall = "P_Die_F";
    [SerializeField, Min(.01f), Tooltip("The fall's last pose turns into the crawl's this slowly (s): no clip goes from one lying "
        + "pose to the other.")]
    private float downedSettleTime = .4f;

    // The state lying began from (Enter); whether this lying's fall has been decided, and whether it is playing.
    private MovementState lyingFrom;
    private bool downedFallDecided, downedFalling;
    // Prone and crawling this frame, as FaceLying reads it (a copy's from how it moves here).
    private bool crawling;

    // From Enter, into prone.
    private void EnterProne(MovementState from)
    {
        lyingFrom = from;
        downedFallDecided = downedFalling = false;
    }

    // From ChoosePose while prone, after FaceLying: the fall, or false for the crawl's pose.
    private bool DownedFallPose()
    {
        var fall = Find(downedFall);
        if (fall == null) return false;
        if (!downedFallDecided)
        {
            if (player.IsDowned && grounded && PlayerMovement.FallsWhenDowned(lyingFrom))
            {
                downedFallDecided = downedFalling = true;
                Restart(fall);
            }
            else if (stateTime > blendTime) downedFallDecided = true;
        }
        if (!downedFalling) return false;
        // Played out, or given way to a crawl (the movement starts one only once the body is down), or got up.
        if (stateTime >= fall.Length - downedSettleTime || crawling || !player.IsDowned)
        {
            downedFalling = false;
            SlowBlend(downedSettleTime);
            return false;
        }
        fall.Target = 1f;
        return true;
    }
}
