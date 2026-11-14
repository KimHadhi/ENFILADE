using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A road that falls in behind the players (the map gimmick card "무너지는 고가 달리기", after Left 4 Dead 2's bridge run in
/// The Parish): while its step runs (the escape), its segments give way one after another from the far end, each a few
/// seconds after the one before, so they run for it. A segment going is its standing boxes switched off (their colliders
/// and looks: whoever stands there falls) and a carve over its place in the walk mesh, while a loose copy of it tips and
/// drops to the road below and stays there as rubble. Every PC times it from when it saw the step begin; a wipe that
/// sends the run back before it puts the road back. It has no state of its own to share.
/// </summary>
public sealed class GimmickCollapse : GimmickSwitch
{
    // How long a segment takes to tip and fall (s), and how far it falls (m).
    private const float FallSeconds = 1.4f;

    [SerializeField, Tooltip("The step it runs during (CampaignSteps id).")]
    private string step = "";
    [SerializeField, Min(0f), Tooltip("After the step begins, when the first segment goes (s).")]
    private float first = 3f;
    [SerializeField, Min(.1f), Tooltip("Between one segment and the next (s).")]
    private float every = 2.6f;
    [SerializeField, Min(0f), Tooltip("How far they fall (m).")]
    private float drop = 8f;
    [SerializeField, Tooltip("Each segment's standing parts, a holder each, in the order they go (their boxes).")]
    private Transform[] segments = new Transform[0];
    [SerializeField, Tooltip("Each segment's loose copy, switched off until it goes, in the same order.")]
    private Transform[] copies = new Transform[0];
    [SerializeField, Tooltip("Each segment's carve over its place (a NavMeshObstacle), switched off until it goes.")]
    private NavMeshObstacle[] carves = new NavMeshObstacle[0];

    private Vector3[] copyFrom;
    private Quaternion[] copyTurn;
    private float[] fellAt;
    private float began = -1f;

    private void Awake()
    {
        copyFrom = new Vector3[copies.Length];
        copyTurn = new Quaternion[copies.Length];
        fellAt = new float[segments.Length];
        for (int i = 0; i < copies.Length; i++)
            if (copies[i] != null) { copyFrom[i] = copies[i].position; copyTurn[i] = copies[i].rotation; copies[i].gameObject.SetActive(false); }
        for (int i = 0; i < carves.Length; i++) if (carves[i] != null) carves[i].enabled = false;
        for (int i = 0; i < fellAt.Length; i++) fellAt[i] = -1f;
    }

    internal override void Tick(float dt, bool host)
    {
        if (CampaignRun.IsRunning(step))
        {
            if (began < 0f) began = Time.time;
            float since = Time.time - began;
            for (int i = 0; i < segments.Length; i++)
                if (fellAt[i] < 0f && since >= first + i * every) Fall(i);
        }
        else if (began >= 0f && CampaignRun.ProgressOf(step) <= 0f) Restore();
        for (int i = 0; i < copies.Length && i < fellAt.Length; i++) Tumble(i);
    }

    private void Fall(int i)
    {
        fellAt[i] = Time.time;
        if (segments[i] != null)
        {
            foreach (var part in segments[i].GetComponentsInChildren<Collider>()) part.enabled = false;
            foreach (var look in segments[i].GetComponentsInChildren<Renderer>()) look.enabled = false;
        }
        if (i < carves.Length && carves[i] != null) carves[i].enabled = true;
        if (i < copies.Length && copies[i] != null) copies[i].gameObject.SetActive(true);
        Vector3 at = i < copies.Length && copies[i] != null ? copyFrom[i] : transform.position;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Rumble, at, 1f, 10f, 90f);
        // The view shakes near it, as a blast's does.
        FragBlast.Shake(at, 25f);
    }

    // A fallen segment's copy tips off its near edge and drops, then lies where it landed.
    private void Tumble(int i)
    {
        if (copies[i] == null || fellAt[i] < 0f) return;
        float t = Mathf.Clamp01((Time.time - fellAt[i]) / FallSeconds);
        float fall = t * t;
        copies[i].SetPositionAndRotation(copyFrom[i] + Vector3.down * (drop * fall), copyTurn[i] * Quaternion.Euler(0f, 0f, 18f * fall));
    }

    // The run went back before it: the road stands again.
    private void Restore()
    {
        began = -1f;
        for (int i = 0; i < segments.Length; i++)
        {
            fellAt[i] = -1f;
            if (segments[i] != null)
            {
                foreach (var part in segments[i].GetComponentsInChildren<Collider>(true)) part.enabled = true;
                foreach (var look in segments[i].GetComponentsInChildren<Renderer>(true)) look.enabled = true;
            }
            if (i < carves.Length && carves[i] != null) carves[i].enabled = false;
            if (i < copies.Length && copies[i] != null) { copies[i].gameObject.SetActive(false); copies[i].SetPositionAndRotation(copyFrom[i], copyTurn[i]); }
        }
    }
}
