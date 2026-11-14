using UnityEngine;

/// <summary>
/// A zombie lying or sitting in its calm pose turns its head and shoulders to look at what it heard (ZombieMind.Glance: a
/// stone, a knock, a faint step, a TV) where it is, instead of getting up for it, and turns back when that is over. Its
/// sight goes the same way (ZombieMind.LookForward), and so does the stealth view's cone. There was no clip for it, so it
/// is made here on the bones after the pose's clip has placed them: a turn about the up axis shared out over the chest,
/// the neck and the head (the head turns the whole way, the chest only a little so the hands on the floor hardly move), as
/// far as gazeTurn either side and at gazeSpeed. That fits any humanoid in any pose with nothing made for it (a modder's
/// zombie too). Every PC turns its own copy the same way: the host's mind decides and tells the others
/// (EnemyNetwork.ShareGaze). The user, 2026-11-15, of the crowd lying face down: "필요한 애니메이션이 있으면 직접
/// 만들도록 한다~ ... 그냥 엎드린 걸로 하고 돌 던지면 그 쪽으로 시선 돌리게 해 줘". Numbers: Tables/ZombieClips
/// (gazeTurn, gazeSpeed).
/// </summary>
public sealed partial class ZombieBody
{
    // The bones the turn is shared out over, chest to head, and each one's share of it (they add up to the whole turn).
    private static readonly HumanBodyBones[] GazeChain = { HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head };
    private static readonly float[] GazeShares = { .1f, .35f, .55f };
    // Below this (degrees) the head counts as turned back.
    private const float GazeRest = .05f;

    [SerializeField, Range(0f, 150f), Tooltip("Lying or sitting in its calm pose, how far it turns its head and shoulders to look at something it heard (degrees either side).")]
    private float gazeTurn = 80f;
    [SerializeField, Min(1f), Tooltip("How fast it turns its head and shoulders there and back (degrees a second).")]
    private float gazeSpeed = 150f;

    private float gazeYaw;
    private readonly Transform[] gazeBones = new Transform[3];
    // Each bone's turn of the chain as this body's rig has it (a missing chest or neck passes its share on up).
    private readonly float[] gazeShare = new float[3];
    // Each bone as the clip left it this frame, and as the turn left it: a bone the clip didn't pose again since (a body
    // out of sight) is put back first, so the turn never piles up on itself.
    private readonly Quaternion[] gazeFrom = new Quaternion[3], gazeSet = new Quaternion[3];
    private GameObject gazeModel;
    private bool gazeTurned;

    /// <summary>How far round its head and shoulders are turned now, on this PC (degrees about up; plus is to its right).</summary>
    internal float GazeYaw => gazeYaw;

    // After the clip and everything else has posed the bones (LateUpdate): toward what it gazes at while it holds its pose,
    // else back to where the clip has it.
    private void GazeLate(float dt)
    {
        float target = 0f;
        if (mind != null && mind.HoldsPose && !down && Model != null && (enemyHealth == null || enemyHealth.IsAlive)
            && mind.Gazing(out Vector3 at))
        {
            Vector3 way = Vector3.ProjectOnPlane(at - transform.position, Vector3.up);
            Vector3 ahead = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (way.sqrMagnitude > .01f && ahead.sqrMagnitude > 1e-4f)
                target = Mathf.Clamp(Vector3.SignedAngle(ahead, way, Vector3.up), -gazeTurn, gazeTurn);
        }
        gazeYaw = Mathf.MoveTowards(gazeYaw, target, gazeSpeed * dt);
        bool turn = Mathf.Abs(gazeYaw) >= GazeRest;
        if (!turn && !gazeTurned) return;
        if (!BindGaze()) { gazeTurned = false; return; }
        for (int i = 0; i < gazeBones.Length; i++)
        {
            var bone = gazeBones[i];
            if (bone == null) continue;
            if (gazeTurned && bone.localRotation == gazeSet[i]) bone.localRotation = gazeFrom[i];
            gazeFrom[i] = bone.localRotation;
        }
        gazeTurned = turn;
        if (!turn) return;
        // Parent first: each bone's turn carries the ones below it, so the head ends turned by the whole of it.
        for (int i = 0; i < gazeBones.Length; i++)
        {
            var bone = gazeBones[i];
            if (bone == null || gazeShare[i] <= 0f) continue;
            bone.rotation = Quaternion.AngleAxis(gazeYaw * gazeShare[i], Vector3.up) * bone.rotation;
            gazeSet[i] = bone.localRotation;
        }
    }

    // The chain's bones on this body's rig, found again whenever it is dressed anew. A rig without an upper chest turns its
    // chest, and a missing neck's share goes to the head.
    private bool BindGaze()
    {
        if (gazeModel == Model && gazeModel != null) return gazeBones[2] != null;
        gazeModel = Model;
        gazeTurned = false;
        for (int i = 0; i < gazeBones.Length; i++) { gazeBones[i] = null; gazeShare[i] = 0f; }
        if (Model == null || animator == null) return false;
        for (int i = 0; i < GazeChain.Length; i++)
        {
            var bone = Bone(GazeChain[i]);
            if (bone == null && GazeChain[i] == HumanBodyBones.UpperChest) bone = Bone(HumanBodyBones.Chest);
            gazeBones[i] = bone;
        }
        if (gazeBones[2] == null) return false;
        float carried = 0f;
        for (int i = 0; i < gazeBones.Length; i++)
        {
            carried += GazeShares[i];
            if (gazeBones[i] == null) continue;
            gazeShare[i] = carried;
            carried = 0f;
        }
        return true;
    }
}
