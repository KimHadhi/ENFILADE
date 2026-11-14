using UnityEngine;

// ZombieMind: walking a round while calm (a map's corridors, PatrolRoute). One part of the class; the rest is in
// ZombieMind.cs and the files beside it.
public sealed partial class ZombieMind
{
    [Header("Round (a map's corridors: PatrolRoute)")]
    [SerializeField, Range(.05f, 1f), Tooltip("Walking its round, it goes at this share of its chase speed.")]
    private float roundSpeedShare = .28f;
    [SerializeField, Min(0f), Tooltip("At each stop of its round it stands and looks round about this long (s).")]
    private float roundPause = 3.5f;

    // Its round's stops, the one it walks to (or stands at), until when it stands there, and whether it is walking now.
    private Vector3[] round;
    private int roundAt;
    private float roundWait;
    private bool roundWalking;

    /// <summary>
    /// Calms it on a round (the map gimmick card "아파트를 암살 놀이터로": "복도엔 돌아다니는 놈"): it walks from stop to
    /// stop at a slow walk, stands at each and looks round, and round again, its eyes where it walks. Anything that stirs
    /// it takes over as for any calm zombie; calm again, it walks on to the next stop. Its back is to whoever follows.
    /// </summary>
    public void Patrol(Vector3[] stops, int start)
    {
        if (IsCopy || stops == null || stops.Length < 2) return;
        Calm();
        round = stops;
        roundAt = ((start % stops.Length) + stops.Length) % stops.Length;
        roundWait = Time.time + Random.Range(.5f, roundPause);
        roundWalking = false;
    }

    // Calm on a round, it walks it; true while it does (it doesn't look round on the spot meanwhile).
    private bool Round()
    {
        if (round == null || HoldsPose || asleep) return false;
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
        if (roundWalking)
        {
            // Stopped by something else meanwhile (it settled after a look, a hold): it stands, then goes on.
            if (agent.isStopped) { roundWalking = false; roundWait = Time.time + roundPause; return false; }
            if (agent.pathPending || agent.remainingDistance > ArriveDistance) return true;
            roundWalking = false;
            Halt();
            Post();
            roundWait = Time.time + roundPause * Random.Range(.7f, 1.3f);
            return false;
        }
        if (Time.time < roundWait) return false;
        roundAt = (roundAt + 1) % round.Length;
        SetSpeed(roundSpeedShare);
        agent.isStopped = false;
        agent.SetDestination(round[roundAt]);
        roundWalking = true;
        return true;
    }
}
