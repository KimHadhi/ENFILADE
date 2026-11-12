using System;
using UnityEngine;

/// <summary>
/// Paired executions and takedowns (the user, 2026-11-12: "처형 애니메이션 만들어서 넣어 줘 … 몇 놈은 누워 있거나 앉아 있거나
/// 일어서 있던데 이거 각각 맞춰서"): when an execution proper begins (PlayerExecution, any dash in over), the player's own PC
/// picks the pair for what the zombie is doing (ZombieBody.ExecutionState: standing from behind or in front, sitting,
/// eating, crouched, lying face down or on its back; Tables/Executions.csv), lays the pair's root on the zombie (its feet;
/// over its body when it lies knocked down), and plays both clips on one clock: the player's on the body
/// (PlayerBody.Execution), the zombie's on the zombie (ZombieBody.Executed). The execution takes the pair's kill and length
/// (PlayerExecution.Retime), so the blow lands on the pair's kill frame, on the bone the blade meets, and the dead body goes
/// limp into its ragdoll from the clip's pose. Every PC plays the same pair at the same place: the owner's PC sends it
/// (Began, PlayerNetworkCombat), every other PC's copy of the player and of the zombie plays it from there (Show), caught up
/// by the time the word took. The host decides as ever: it holds the zombie for the executor or says no (EnemyNetwork), and
/// a refused execution ends the pair everywhere (Stopped).
/// While a hand holds the zombie (the table's l*/r* windows) it is kept on that bone whatever bodies play the pair (a bigger
/// brute, another character): a two-bone reach after everything else has posed the arms. A state with no pair, or whose
/// clips are missing, keeps the old execution. The knife in hand, the gun put away and the knife's sounds are the class
/// knife's (ExecutionKnife), which leaves the arms to a playing pair (ExecutionKnife.ArmsPosedElsewhere, handed over eased
/// both ways) and spreads the rest of its stroke after the kill over what is left of the pair, so the knife is put away
/// and the gun comes back as the pair ends.
/// It runs right after the execution, so a dash's end begins the pair in the same frame and the pair's timing is in before
/// the class knife's stab reads it (ExecutionKnife, Execution + 5), and before the movement, the body and the zombies; the
/// hands' holds run after the hands' IK and the gun's framework, just before the class knife seats the blade in the fist
/// (ExecutionPairHands).
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Execution + 2)]
public sealed class ExecutionPairPlayer : MonoBehaviour
{
    /// <summary>A pair as it goes to the other PCs: which, on whom, laid where, begun when (the server's clock).</summary>
    public readonly struct Shared
    {
        public readonly byte Pair;
        public readonly ulong Victim;
        public readonly Vector3 Anchor;
        public readonly float Yaw;
        public readonly double Clock;

        public Shared(byte pair, ulong victim, Vector3 anchor, float yaw, double clock)
        {
            Pair = pair; Victim = victim; Anchor = anchor; Yaw = yaw; Clock = clock;
        }
    }

    // A hand eases onto its hold and off it over these (s).
    private const float HoldIn = .05f, HoldOut = .1f;

    /// <summary>On the player's own PC: a pair began (to send to the others).</summary>
    public event Action<Shared> Began;
    /// <summary>On the player's own PC: a pair ended before its time (to stop it on the others).</summary>
    public event Action Stopped;

    private PlayerMovement movement;
    private PlayerExecution execution;
    private PlayerBody body;
    private ExecutionPair pair;
    private ZombieBody zombie;
    private bool playing, waiting, ownsPair, bled;
    private float clock;
    private Vector3 anchor, spot;
    private Quaternion anchorTurn = Quaternion.identity;
    private float grown = 1f;
    private readonly Collider[] ignored = new Collider[8];
    private int ignoredCount;
    private Hand left, right;
    // The zombie's bones the pair uses, kept from its start: they go on into the corpse when the kill lays it down, so the
    // blood and the knife's last frames in the neck find them whatever ran first that frame.
    private Transform hitBone, leftBone, rightBone;

    private struct Hand
    {
        public bool Held;
        public Quaternion BoneAt;
        public Vector3 Offset;
    }

    /// <summary>The player's pair player, made on them if missing (PlayerExecution and PlayerNetworkCombat both ask).</summary>
    public static ExecutionPairPlayer For(GameObject player)
    {
        if (player == null) return null;
        return player.TryGetComponent(out ExecutionPairPlayer pairs) ? pairs : player.AddComponent<ExecutionPairPlayer>();
    }

    /// <summary>The pair playing on this PC's copy of the player, or null.</summary>
    public ExecutionPair Playing => playing ? pair : null;

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        execution = GetComponent<PlayerExecution>();
        body = GetComponent<PlayerBody>();
        if (Application.isPlaying && !TryGetComponent(out ExecutionPairHands _)) gameObject.AddComponent<ExecutionPairHands>();
        if (execution == null) return;
        execution.Started -= OnStarted;
        execution.Started += OnStarted;
        execution.Ended -= OnEnded;
        execution.Ended += OnEnded;
    }

    private void OnDisable()
    {
        if (execution != null)
        {
            execution.Started -= OnStarted;
            execution.Ended -= OnEnded;
        }
        Stop();
        waiting = false;
    }

    // The execution's own events come on the PC that runs it: the player's own (the host's copy of someone else's raises
    // Ended too, when their word that it ended arrives, and plays what their PC sent instead).
    private void OnStarted(MeleeCombatTarget victim)
    {
        if (movement != null && !movement.IsLocal) return;
        Stop();
        waiting = true;
    }

    private void OnEnded()
    {
        waiting = false;
        if (!playing || !ownsPair) return;
        bool early = clock < pair.Length - .05f;
        Stop();
        if (early) Stopped?.Invoke();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        // The player's own PC: the dash in is over (or there was none); the pair begins where the player is now.
        if (waiting && execution != null && execution.IsExecuting && !execution.IsDashing)
        {
            waiting = false;
            BeginOwn(execution.Victim);
        }
        if (!playing || dt <= 0f) return;
        clock += dt;
        if (body == null) body = GetComponent<PlayerBody>();
        if (body != null) body.SetPairedTime(clock);
        if (zombie != null && zombie.BodyRoot != null) zombie.SetExecutedTime(clock);
        if (!bled && clock >= pair.Kill) Bleed();
        if (ownsPair)
        {
            Align(dt);
            Aim();
            return;
        }
        // A copy ends on its own clock, caught up to the owner's, as the owner's ends; an early end comes by ShowStop.
        if (clock >= pair.Length) Stop();
    }

    // ---- The owner's pair -------------------------------------------------------------------------------------------

    private void BeginOwn(MeleeCombatTarget victim)
    {
        if (victim == null || !victim.TryGetComponent(out ZombieBody target) || !target.Wears) return;
        string state = target.ExecutionState(transform.position, out bool lying, out Vector3 hips, out Vector3 head);
        var chosen = ExecutionPairs.Find(state);
        if (chosen == null || !Clips(chosen, out var playerClip, out var zombieClip)) return;
        var root = target.BodyRoot;
        if (root == null) return;
        float size = Mathf.Max(.1f, target.Grown);
        Vector3 at = root.position;
        float yaw = target.transform.eulerAngles.y;
        if (lying)
        {
            // Laid over the body where it lies: the clip's head the way the body's head lies, its hips over the body's.
            Vector3 lie = Vector3.ProjectOnPlane(head - hips, Vector3.up);
            if (lie.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(lie.x, lie.z) * Mathf.Rad2Deg - chosen.StartHeadYaw;
            at = hips - Quaternion.Euler(0f, yaw, 0f) * new Vector3(chosen.StartHips.x, 0f, chosen.StartHips.y) * size;
            at.y = root.position.y;
        }
        else if (chosen.Turn > 0f)
        {
            // Standing, it turns its share of the way to have the player where the pair puts them (all of it when it is
            // broken and faces them for the blow); the player walks round the rest.
            Vector3 toward = Vector3.ProjectOnPlane(transform.position - at, Vector3.up);
            if (toward.sqrMagnitude > 1e-4f)
            {
                float want = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg - Mathf.Atan2(chosen.Offset.x, chosen.Offset.y) * Mathf.Rad2Deg;
                yaw += Mathf.DeltaAngle(yaw, want) * chosen.Turn;
            }
        }
        if (!Begin(chosen, target, playerClip, zombieClip, at, yaw, 0f)) return;
        ownsPair = true;
        execution.Retime(chosen.Kill, chosen.Length);
        IgnoreVictim(target);
        ulong id = target.TryGetComponent(out Unity.Netcode.NetworkObject network) && network.IsSpawned ? network.NetworkObjectId : 0UL;
        var manager = Unity.Netcode.NetworkManager.Singleton;
        double now = manager != null && manager.IsListening ? manager.ServerTime.Time : 0d;
        Began?.Invoke(new Shared(chosen.Index, id, at, yaw, now));
    }

    // The pair's two clips from the player body's clip library (Art/Animations/Executions; PlayerBodyProfileBuilder lists them).
    private static bool Clips(ExecutionPair chosen, out AnimationClip playerClip, out AnimationClip zombieClip)
    {
        var profile = PlayerBodyProfile.Load();
        playerClip = profile != null ? profile.Find(chosen.PlayerClip) : null;
        zombieClip = profile != null ? profile.Find(chosen.ZombieClip) : null;
        return playerClip != null && zombieClip != null;
    }

    // The capsule slides onto the pair's place as the body does (PlayerBody.PlacePaired), so the eyes' camera, the
    // hitboxes and the next steps are where the body is; past the align it holds there.
    private void Align(float dt)
    {
        if (movement == null || movement.Motor == null) return;
        Vector3 gap = Vector3.ProjectOnPlane(spot - transform.position, Vector3.up);
        if (gap.sqrMagnitude < 1e-6f) return;
        float remaining = Mathf.Max(dt, pair.Align - clock);
        movement.Motor.AddDisplacement(gap * Mathf.Clamp01(dt / remaining));
    }

    // Where the blow lands and which way it sends the body: on the bone the blade meets, the pair's way.
    private void Aim()
    {
        if (execution == null || hitBone == null) return;
        execution.Strike(hitBone.position, anchorTurn * pair.Fall);
    }

    // The kill frame's blood where the blade meets the bone, the pair's way, on every PC at its own copy's kill frame (the
    // killing blow itself lands where the execution runs; the body may still be falling elsewhere: what shows is the same).
    private void Bleed()
    {
        bled = true;
        if (hitBone == null) return;
        Vector3 along = anchorTurn * pair.Fall;
        along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.down;
        ImpactPool.Play("BloodHead", hitBone.position - along * .03f, -along, .9f);
        ImpactVfx.Blood(hitBone.position, along, true, true, false, zombie != null ? zombie : (Component)hitBone);
    }

    // The player's capsule passes the victim's while the pair puts them close (GiveBackVictim undoes it when it ends).
    private void IgnoreVictim(ZombieBody target)
    {
        GiveBackVictim();
        if (target == null || !TryGetComponent(out CharacterController walker)) return;
        foreach (var collider in target.GetComponents<Collider>())
            if (collider != null && !collider.isTrigger && ignoredCount < ignored.Length)
            {
                Physics.IgnoreCollision(walker, collider, true);
                ignored[ignoredCount++] = collider;
            }
    }

    private void GiveBackVictim()
    {
        if (ignoredCount > 0 && TryGetComponent(out CharacterController walker))
            for (int i = 0; i < ignoredCount; i++)
                if (ignored[i] != null) Physics.IgnoreCollision(walker, ignored[i], false);
        for (int i = 0; i < ignoredCount; i++) ignored[i] = null;
        ignoredCount = 0;
    }

    // ---- Every PC ---------------------------------------------------------------------------------------------------

    private bool Begin(ExecutionPair chosen, ZombieBody target, AnimationClip playerClip, AnimationClip zombieClip, Vector3 at, float yaw,
        float late)
    {
        if (body == null) body = GetComponent<PlayerBody>();
        if (body == null) return false;
        Stop();
        pair = chosen;
        zombie = target;
        hitBone = target != null ? target.HumanBone(chosen.HitBone) : null;
        leftBone = target != null && chosen.LeftHand.Valid ? target.HumanBone(chosen.LeftHand.Bone) : null;
        rightBone = target != null && chosen.RightHand.Valid ? target.HumanBone(chosen.RightHand.Bone) : null;
        grown = target != null ? Mathf.Max(.1f, target.Grown) : 1f;
        anchor = at;
        anchorTurn = Quaternion.Euler(0f, yaw, 0f);
        spot = anchor + anchorTurn * new Vector3(pair.Offset.x, 0f, pair.Offset.y) * grown;
        var spotTurn = anchorTurn * Quaternion.Euler(0f, pair.Yaw, 0f);
        if (!body.BeginPaired(playerClip, spot, spotTurn, pair.Length, pair.BlendIn, pair.BlendOut, pair.Align)) return false;
        if (zombie != null) zombie.BeginExecuted(zombieClip, anchor, anchorTurn, pair.Length, pair.BlendIn, pair.BlendOut, pair.Align);
        // The class knife (ExecutionKnife) keeps riding the fist but leaves the arms to the clip; its stroke's end clears it.
        if (TryGetComponent(out ExecutionKnife knife)) knife.ArmsPosedElsewhere = true;
        playing = true;
        clock = Mathf.Clamp(late, 0f, pair.Length);
        bled = clock > pair.Kill;
        left = right = default;
        return true;
    }

    /// <summary>
    /// On every other PC: the owner's PC began this pair on that zombie, laid there, <paramref name="late"/> seconds ago
    /// (PlayerNetworkCombat). The host's copy of a remote executor takes the pair's timing too, so it holds the zombie for
    /// as long as the pair lasts.
    /// </summary>
    internal void Show(byte index, GameObject victim, Vector3 at, float yaw, float late)
    {
        var chosen = ExecutionPairs.At(index);
        if (chosen == null || !Clips(chosen, out var playerClip, out var zombieClip)) return;
        ZombieBody target = victim != null && victim.TryGetComponent(out ZombieBody found) && found.Wears ? found : null;
        if (!Begin(chosen, target, playerClip, zombieClip, at, yaw, late)) return;
        ownsPair = false;
        // Timed from now, as the owner's knife stab that came with this word begins here now: its strike on the pair's
        // kill and its end on the pair's end as this copy plays them (ExecutionKnife reads them).
        if (execution != null) execution.Retime(chosen.Kill - late, chosen.Length - late);
    }

    /// <summary>On every other PC: the owner's pair ended early (the host said no, or it was cut short).</summary>
    internal void ShowStop() => Stop();

    private void Stop()
    {
        GiveBackVictim();
        if (!playing) return;
        playing = false;
        if (body != null) body.EndPaired();
        if (zombie != null && zombie.BodyRoot != null) zombie.EndExecuted();
        // The class knife's arms again, eased in from where the clip left them (ExecutionKnifeArm), should its stroke still
        // be going (a pair cut short); a whole pair's end is the stroke's end too.
        if (TryGetComponent(out ExecutionKnife knife)) knife.ArmsPosedElsewhere = false;
        // The execution's own timing again (a copy that runs none keeps the pair's until now).
        if (execution != null) execution.ForgetPair();
        ownsPair = false;
        zombie = null;
        hitBone = leftBone = rightBone = null;
    }

    // ---- The hands' holds, after the arms are posed and before the knife is seated (every PC) -----------------------

    /// <summary>From ExecutionPairHands, after the arms' poses and before the knife's seat: the hands kept on their holds.</summary>
    internal void KeepHands()
    {
        if (!playing || pair == null || body == null || Time.deltaTime <= 0f) return;
        var animator = body.BodyAnimator;
        if (animator == null || !animator.isHuman) return;
        Keep(ref left, pair.LeftHand, leftBone, animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        Keep(ref right, pair.RightHand, rightBone, animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
    }

    // A hand on its hold: at the hold's start the bone's turn is noted, and from then the wrist goes where the pair had it
    // from that bone, carried as the bone turns, eased on and off at the window's ends. On the body it was made on this
    // asks for next to nothing; on a bigger zombie or another character it keeps the hand on the head.
    private void Keep(ref Hand hand, ExecutionPair.Contact contact, Transform bone, Animator animator, HumanBodyBones upperBone,
        HumanBodyBones lowerBone, HumanBodyBones handBone)
    {
        if (!contact.Valid || clock < contact.From || clock > contact.To + HoldOut) { hand.Held = false; return; }
        var upper = animator.GetBoneTransform(upperBone);
        var lower = animator.GetBoneTransform(lowerBone);
        var end = animator.GetBoneTransform(handBone);
        if (bone == null || upper == null || lower == null || end == null) return;
        if (!hand.Held)
        {
            hand.Held = true;
            hand.BoneAt = bone.rotation;
            hand.Offset = anchorTurn * contact.Offset * grown;
        }
        float weight = Mathf.Min(Mathf.Clamp01((clock - contact.From) / HoldIn), Mathf.Clamp01((contact.To + HoldOut - clock) / HoldOut));
        if (weight <= 0f) return;
        Vector3 target = bone.position + bone.rotation * Quaternion.Inverse(hand.BoneAt) * hand.Offset;
        Quaternion turned = end.rotation;
        TwoBoneIK.Solve(upper, lower, end, Vector3.Lerp(end.position, target, weight), lower.position);
        // The hand keeps the turn the clip gave it.
        end.rotation = turned;
    }
}
