using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;

/// <summary>
/// Multiplayer plan stage 4B-2: a knocked-down zombie falls on a doll from the corpse pool (ZombieCorpse.TakeDoll) and
/// gets up with the clip for how it lies, face down or face up (ZombieLooks.riseFaceDown/Up). The host, or the PC
/// alone, runs the doll and sends its pose the way a corpse's is sent (ZombieCorpseFall through its own player); the
/// other PCs follow it and start getting up when the host does (MeleeCombatTarget.IsLying). While it lies the enemy
/// follows its hips, its capsule is off so nobody bumps an empty space, bullets pass the doll to the bone hitboxes (so a
/// headshot still counts), a blow on the doll reaches its health, and every blow pushes the body. Getting up: it stands
/// where the clip's lying pose lines up with the doll's (head the same way, hips over hips), and eases from the doll's
/// pose into the clip. Dying while down, its corpse carries on with the same doll. Numbers: Tables/ZombieKnockdown.
/// </summary>
public sealed partial class ZombieBody
{
    public const string KnockdownTable = "ZombieKnockdown", KnockdownRow = "Default";
    // How often a fall is sent (s), and how long the doll must stay still to count as at rest (s).
    private const float SendInterval = .03f, StillTime = .2f;
    // What the get-up spot may be from where the clip puts it (m).
    private const float StandReach = .6f;
    private const string EnemyLayer = "Enemy";

    [SerializeField, Min(0f), Tooltip("Momentum the knockdown gives the upper body (N·s).")]
    private float knockPush = 30f;
    [SerializeField, Min(0f), Tooltip("How hard the muscles hold its pose as it goes down (Hz).")]
    private float muscleHold = 6f;
    [SerializeField, Min(0f), Tooltip("How long the muscles take to give out (s).")]
    private float muscleSeconds = .25f;
    [SerializeField, Min(0f), Tooltip("What stays in the joints once the muscles have gone (1/s).")]
    private float jointFriction = 1.5f;
    [SerializeField, Min(0f), Tooltip("Momentum a blow gives the body while it lies, per point of damage (N·s).")]
    private float pushPerDamage = 1f;
    [SerializeField, Min(0f), Tooltip("Momentum of even the weakest blow (N·s).")]
    private float leastPush = 8f;
    [SerializeField, Min(0f), Tooltip("Speed one blow can add to one body part (m/s).")]
    private float mostPushSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("It gets up only once slower than this (m/s).")]
    private float settleSpeed = .25f;
    [SerializeField, Min(0f), Tooltip("It lies at least this long (s), or the knockdown's own time if longer.")]
    private float shortestDown = .8f;
    [SerializeField, Min(.01f), Tooltip("Easing from the doll's pose into the getting-up clip (s).")]
    private float riseBlend = .25f;
    [SerializeField, Min(.01f), Tooltip("Easing from the end of the getting-up clip into walking (s). It moves and fights again from then.")]
    private float riseOut = .25f;
    [SerializeField, Min(1f), Tooltip("Lying knocked down it takes this many times the damage (friend feedback C7: knocking it down pays).")]
    private float lyingDamage = 2f;

    /// <summary>What a blow to it is worth while it lies knocked down (EnemyHealth).</summary>
    internal float LyingDamage => Mathf.Max(1f, lyingDamage);

    private static readonly Dictionary<ulong, ZombieBody> downById = new Dictionary<ulong, ZombieBody>();
    // Where each getting-up clip's lying body is at its start, in the body's own space: hips across the floor, and
    // which way the head lies (degrees about up). Every zombie of a pack shares its proportions.
    private static readonly Dictionary<AnimationClip, (Vector2 hips, float head)> riseShapes =
        new Dictionary<AnimationClip, (Vector2, float)>();

    private RagdollCorpse doll;
    private PlayerNetworkCorpse sender;
    private NavMeshAgent agent;
    private CorpsePose heard;
    // Human bones, parents first, and their turns as the doll left them, to ease from into the clip.
    private Transform[] blendBones;
    private Quaternion[] fromTurns, animatedTurns;
    private Vector3 fromHips;
    private (Vector2 hips, float head) riseDownShape, riseUpShape;
    private float feetHeight, downAge, stillFor, nextSend, riseTime, riseWeight, blendLeft;
    private ulong downId;
    private bool down, rising, dollRuns, hasHeard, sentRest, riseFaceDown, agentWasOn;

    /// <summary>On the ground, not yet getting up.</summary>
    public bool IsLying => down && !rising;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearDown()
    {
        downById.Clear();
        riseShapes.Clear();
    }

    /// <summary>A knocked-down zombie whose fall another PC runs, by its enemy's network id.</summary>
    internal static bool Follows(ulong id, out ZombieBody body)
        => downById.TryGetValue(id, out body) && body != null && body.doll != null && !body.dollRuns;

    // ---- Going down ------------------------------------------------------------------------------------------

    /// <summary>The host, or the PC alone: it goes down this way, and this PC runs its doll.</summary>
    internal void KnockDown(Vector3 direction)
    {
        if (Model == null || down) return;
        GoDown(true);
        if (doll == null) return;
        doll.Launch(moving, 0f, 0f);
        doll.Brace(muscleHold, muscleSeconds, jointFriction);
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f) doll.Knock(flat.normalized * knockPush);
    }

    private void GoDown(bool runs)
    {
        down = true;
        rising = false;
        dollRuns = runs;
        hasHeard = sentRest = false;
        downAge = stillFor = riseWeight = blendLeft = 0f;
        // Whatever it was doing stops.
        striking = flinching = false;
        attackWeight = flinchWeight = staggerWeight = 0f;
        StopJolt();
        // Nobody bumps where it stood; on the PC running it, the agent lets go of the body.
        foreach (var collider in covered) collider.enabled = false;
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        agentWasOn = runs && agent != null && agent.enabled;
        if (agentWasOn) agent.enabled = false;
        doll = ZombieCorpse.TakeDoll(animator, human);
        if (doll == null) return;
        animator.writeDefaultValuesOnDisable = false;
        animator.enabled = false;
        doll.Lay(runs, muscleSeconds);
        doll.Struck += OnDollStruck;
        foreach (var shape in doll.Shapes) replaced.Add(shape);
        foreach (var player in PlayerRoster.All)
            if (player != null && player.TryGetComponent(out CharacterController walker) && walker.enabled) doll.PassThrough(walker);
        SkinsFollow(true);
        downId = network != null && network.IsSpawned ? network.NetworkObjectId : 0UL;
        if (downId == 0UL) return;
        downById[downId] = this;
        if (!runs && ZombieCorpse.TakeWaiting(downId, out var early)) Show(early);
    }

    /// <summary>A copy: the host's zombie went down or started getting up.</summary>
    private void WatchDown()
    {
        if (network == null || !network.IsCopy || target == null || Model == null) return;
        bool lying = target.IsLying;
        if (lying && !down) GoDown(false);
        else if (!lying && down && !rising) BeginRise(false, Vector3.zero, Quaternion.identity);
    }

    /// <summary>The fall the host runs, for this copy to follow.</summary>
    internal void Show(in ZombieCorpseFall fall)
    {
        heard = fall.Pose;
        hasHeard = true;
    }

    // ---- Lying -------------------------------------------------------------------------------------------------

    private void LateUpdate()
    {
        if (down && doll != null) Lie(Time.deltaTime);
        else if (blendLeft > 0f) EaseFromDoll(Time.deltaTime);
        // After the doll has placed the bones: a brute's limp, landing and weak head (ZombieBody.Legs.cs).
        LegsLate();
    }

    private void Lie(float dt)
    {
        if (!dollRuns)
        {
            if (hasHeard) doll.Follow(heard, dt);
            doll.PoseBody();
            return;
        }
        // The enemy goes where its body lies, so it gets up there and its copies follow it there. First, because
        // moving the enemy carries the bones, and the doll then puts them where they lie.
        FollowHips();
        doll.PoseBody();
        stillFor = doll.Settled(settleSpeed) ? stillFor + dt : 0f;
        bool still = stillFor >= StillTime;
        if (downId != 0UL && (!still || !sentRest) && (still || Time.time >= nextSend)) Send(still);
    }

    private void FollowHips()
    {
        Vector3 hips = human[(int)HumanBodyBones.Hips].position;
        Vector3 at = new Vector3(hips.x, transform.position.y, hips.z);
        if (FloorBelow(hips + Vector3.up * .5f, out float floor)) at.y = floor - feetHeight;
        transform.position = at;
    }

    private static readonly RaycastHit[] floorHits = new RaycastHit[16];
    private static int floorMask;

    // The floor under a point: the world (maps build it on Default), not a body lying on it, a doll or a player.
    private static bool FloorBelow(Vector3 from, out float y)
    {
        y = from.y;
        // The mask worked out once (GetMask allocates; this runs every frame for each zombie lying down).
        if (floorMask == 0) floorMask = ~LayerMask.GetMask(EnemyLayer);
        int count = Physics.RaycastNonAlloc(from, Vector3.down, floorHits, 3f, floorMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = floorHits[i];
            if (hit.distance >= nearest || hit.collider.GetComponentInParent<RagdollCorpse>() != null
                || hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
            nearest = hit.distance;
            y = hit.point.y;
        }
        return !float.IsPositiveInfinity(nearest);
    }

    private void Send(bool rested)
    {
        nextSend = Time.time + SendInterval;
        sentRest = rested;
        if (sender == null)
        {
            var local = PlayerRoster.Local;
            if (local == null || !local.TryGetComponent(out sender)) return;
        }
        sender.SendZombieFall(new ZombieCorpseFall(downId, doll.Capture(), rested));
    }

    /// <summary>A blow landed on it (EnemyHealth, where its doll runs): lying, it pushes the body.</summary>
    internal void Hit(in DamageHit hit)
    {
        if (!down || doll == null || !dollRuns || hit.Direction.sqrMagnitude < 1e-4f) return;
        doll.Push(hit.Point, hit.Direction.normalized * Mathf.Max(leastPush, hit.Amount * pushPerDamage), mostPushSpeed);
        stillFor = 0f;
    }

    // A blow on the doll (a kick, a punch) is a blow on the zombie. Bullets go past the doll to the hitboxes.
    private void OnDollStruck(DamageHit hit)
    {
        if (TryGetComponent(out EnemyHealth health)) health.ReceiveHit(hit);
    }

    // ---- Getting up --------------------------------------------------------------------------------------------

    /// <summary>
    /// The host, or the PC alone, each tick of the knockdown: held while the knockdown lasts, then once it lies still
    /// and there is room it gets up. False once it stands, when it moves and fights again.
    /// </summary>
    internal bool TickDown(float dt, bool held)
    {
        if (!down) return false;
        if (rising) return true;
        downAge += dt;
        if (doll == null)
        {
            if (held) return true;
            Stand();
            return false;
        }
        if (held || downAge < shortestDown || stillFor < StillTime) return true;
        if (!TryRiseSpot(out var place, out var turn)) return true;
        BeginRise(true, place, turn);
        return true;
    }

    /// <summary>Stands at once, without the clip: grabbed for an execution, its posture reset, switched off.</summary>
    internal void EndDown()
    {
        if (!down) return;
        if (doll != null)
        {
            Vector3 hips = human[(int)HumanBodyBones.Hips].position;
            HoldDollPose();
            GiveDollBack();
            if (dollRuns && NavMesh.SamplePosition(hips, out var nav, 1.5f, NavMesh.AllAreas))
                transform.position = nav.position - Vector3.up * feetHeight;
        }
        rising = false;
        riseWeight = 0f;
        Stand();
    }

    private bool LiesFaceDown()
    {
        Vector3 hips = human[(int)HumanBodyBones.Hips].position;
        var neck = human[(int)HumanBodyBones.Neck] != null ? human[(int)HumanBodyBones.Neck] : human[(int)HumanBodyBones.Head];
        var left = human[(int)HumanBodyBones.LeftUpperArm];
        var right = human[(int)HumanBodyBones.RightUpperArm];
        if (neck == null || left == null || right == null) return true;
        // The way the chest faces: across the shoulders, then up the spine.
        Vector3 chest = Vector3.Cross(right.position - left.position, neck.position - hips);
        return chest.y < 0f;
    }

    // Where to stand so the clip's lying body lies on the doll's: its head the same way, its hips over the doll's.
    private bool TryRiseSpot(out Vector3 place, out Quaternion turn)
    {
        place = transform.position;
        Vector3 hips = human[(int)HumanBodyBones.Hips].position, head = human[(int)HumanBodyBones.Head].position;
        // A crawler pushes up into its crawl; anyone else gets up as it lies.
        var shape = crawling ? riseCrawlShape : LiesFaceDown() ? riseDownShape : riseUpShape;
        Vector3 lying = Vector3.ProjectOnPlane(head - hips, Vector3.up);
        float yaw = (lying.sqrMagnitude > 1e-4f ? Mathf.Atan2(lying.x, lying.z) * Mathf.Rad2Deg : transform.eulerAngles.y) - shape.head;
        turn = Quaternion.Euler(0f, yaw, 0f);
        Vector3 feet = hips - turn * new Vector3(shape.hips.x, 0f, shape.hips.y);
        feet.y = FloorBelow(new Vector3(feet.x, hips.y + .5f, feet.z), out float floor) ? floor : hips.y;
        if (!NavMesh.SamplePosition(feet, out var nav, StandReach, NavMesh.AllAreas)) return false;
        place = nav.position - Vector3.up * feetHeight;
        // Nobody may be standing in the way of it.
        var capsule = covered.Count > 0 ? covered[0] as CapsuleCollider : null;
        float radius = capsule != null ? Mathf.Max(.1f, capsule.radius - .05f) : .3f;
        float height = capsule != null ? capsule.height : 1.8f;
        Vector3 bottom = nav.position + Vector3.up * (radius + .05f), top = nav.position + Vector3.up * Mathf.Max(radius + .05f, height - radius);
        foreach (var other in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!other.transform.IsChildOf(transform)
                && (other.GetComponentInParent<PlayerMovement>() != null || other.GetComponentInParent<MeleeCombatTarget>() != null))
                return false;
        return true;
    }

    /// <summary>
    /// Out of the doll into the getting-up clip. Placed (the host, or alone): the enemy stands where the clip lines up
    /// with the doll; copies have their place from the host.
    /// </summary>
    private void BeginRise(bool placed, Vector3 place, Quaternion turn)
    {
        riseFaceDown = doll == null || LiesFaceDown();
        HoldDollPose();
        GiveDollBack();
        if (placed) transform.SetPositionAndRotation(place, turn);
        rising = true;
        riseTime = 0f;
        riseWeight = 1f;
        lastPosition = transform.position;
    }

    /// <summary>From Update: the clip runs on, and near its end the zombie stands and moves again.</summary>
    private void TickRise(float dt)
    {
        if (riseWeight <= 0f) return;
        riseTime += dt;
        RisePlayable().SetTime(riseTime);
        float length = RiseLength;
        if (rising)
        {
            if (riseTime >= length - riseOut) Stand();
            return;
        }
        riseWeight = Mathf.MoveTowards(riseWeight, 0f, dt / riseOut);
    }

    private void Stand()
    {
        down = rising = false;
        foreach (var collider in covered) collider.enabled = true;
        // Switched back on where the enemy now stands, the agent finds the navmesh under it.
        if (agentWasOn && agent != null) agent.enabled = true;
        agentWasOn = false;
        lastPosition = transform.position;
    }

    // Every human bone as the doll left it, and the Animator back on: the clip takes over from that pose (EaseFromDoll).
    private void HoldDollPose()
    {
        if (blendBones == null) return;
        if (fromTurns == null || fromTurns.Length != blendBones.Length)
        {
            fromTurns = new Quaternion[blendBones.Length];
            animatedTurns = new Quaternion[blendBones.Length];
        }
        for (int i = 0; i < blendBones.Length; i++) fromTurns[i] = blendBones[i].rotation;
        fromHips = blendBones[0].position;
        blendLeft = riseBlend;
        if (animator != null) animator.enabled = true;
    }

    // After the Animator has posed the clip: ease from the doll's pose into it. The clip's turns are read first, then
    // set parents first, so each bone ends where it should whatever its parent did.
    private void EaseFromDoll(float dt)
    {
        blendLeft = Mathf.Max(0f, blendLeft - dt);
        float share = blendLeft / riseBlend;
        share *= share;
        for (int i = 0; i < blendBones.Length; i++) animatedTurns[i] = blendBones[i].rotation;
        Vector3 animatedHips = blendBones[0].position;
        for (int i = 0; i < blendBones.Length; i++) blendBones[i].rotation = Quaternion.Slerp(animatedTurns[i], fromTurns[i], share);
        blendBones[0].position = Vector3.Lerp(animatedHips, fromHips, share);
    }

    private void GiveDollBack()
    {
        if (downId != 0UL && downById.TryGetValue(downId, out var self) && self == this) downById.Remove(downId);
        if (doll == null) return;
        doll.Struck -= OnDollStruck;
        foreach (var shape in doll.Shapes) replaced.Remove(shape);
        SkinsFollow(false);
        ZombieCorpse.GiveDoll(doll);
        doll = null;
    }

    /// <summary>Dying while down: the doll it lies on, for its corpse to carry on with. Null when standing.</summary>
    private RagdollCorpse HandOverDoll()
    {
        if (!down || doll == null) return null;
        var lying = doll;
        lying.Struck -= OnDollStruck;
        foreach (var shape in lying.Shapes) replaced.Remove(shape);
        if (downId != 0UL && downById.TryGetValue(downId, out var self) && self == this) downById.Remove(downId);
        doll = null;
        down = rising = false;
        return lying;
    }

    // Lying, the body leaves the box the rig draws it in (sized from the Root bone at the feet): the box follows the bones.
    private void SkinsFollow(bool follow)
    {
        if (Model == null) return;
        foreach (var skin in Model.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.updateWhenOffscreen = follow;
    }

    // ---- Set up ---------------------------------------------------------------------------------------------------

    // Human bones, parents first, for easing out of the doll.
    private void FindBlendBones()
    {
        var found = new List<Transform>();
        foreach (var bone in human) if (bone != null && !found.Contains(bone)) found.Add(bone);
        found.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
        blendBones = found.ToArray();

        int Depth(Transform bone)
        {
            int depth = 0;
            for (var at = bone; at != null; at = at.parent) depth++;
            return depth;
        }
    }

    // Poses the getting-up clip's first frame once to see where its lying body is (then cached for the pack).
    private (Vector2 hips, float head) RiseShape(AnimationClip clip, int input)
    {
        if (clip == null) return (Vector2.zero, 0f);
        if (riseShapes.TryGetValue(clip, out var known)) return known;
        for (int i = 0; i < top.GetInputCount(); i++) top.SetInputWeight(i, i == input ? 1f : 0f);
        top.GetInput(input).SetTime(0d);
        graph.Evaluate(0f);
        var space = Model.transform;
        Vector3 hips = space.InverseTransformPoint(human[(int)HumanBodyBones.Hips].position);
        Vector3 head = space.InverseTransformPoint(human[(int)HumanBodyBones.Head].position);
        var shape = (new Vector2(hips.x, hips.z), Mathf.Atan2(head.x - hips.x, head.z - hips.z) * Mathf.Rad2Deg);
        riseShapes[clip] = shape;
        return shape;
    }
}
