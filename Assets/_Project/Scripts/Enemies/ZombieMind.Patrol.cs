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

    // Calm on a round, it walks it; true while it does (it doesn't look round on the spot meanwhile).
    private bool Round()
    {
        if (round == null || HoldsPose) return false;
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
