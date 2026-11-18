using System;
using UnityEngine;

/// <summary>
/// Paired executions and takedowns (the user, 2026-11-12: "처형 애니메이션 만들어서 넣어 줘 … 몇 놈은 누워 있거나 앉아 있거나
/// 일어서 있던데 이거 각각 맞춰서"): when an execution proper begins (PlayerExecution, any dash in over), the player's own PC
/// plays the pair the execution's rule names (PlayerExecution.Rule, Tables/ExecutionRules.csv: found at the press for the
/// zombie's posture, standing, sitting, eating, crouched, lying face down or on its back, and for the side the player
/// came from; the pairs themselves are Tables/Executions.csv), lays the pair's root on the zombie (its feet;
/// over its body when it lies knocked down), and plays both clips on one clock: the player's on the body
/// (PlayerBody.Execution), the zombie's on the zombie (ZombieBody.Executed). The execution takes the pair's kill and length
/// (PlayerExecution.Retime), so the blow lands on the pair's kill frame, on the bone the blade meets, and the dead body goes
/// limp into its ragdoll from the clip's pose. Every PC plays the same pair at the same place: the owner's PC sends it
/// (Began, PlayerNetworkCombat), every other PC's copy of the player and of the zombie plays it from there (Show), caught up
/// by the time the word took. The executor's own PC decides that it may be done; the host holds the zombie for them and says
/// no only when someone else got to it first (EnemyNetwork), and an execution turned down that way ends the pair everywhere
/// (Stopped).
/// While a hand holds the zombie (the table's l*/r* windows) it is kept on that bone whatever bodies play the pair (a bigger
/// brute, another character): a two-bone reach after everything else has posed the arms. A state with no pair, or whose
/// clips are missing, keeps the old execution. The knife in hand, the gun put away and the knife's sounds are the class
/// knife's (ExecutionKnife), which leaves the arms to a playing pair (ExecutionKnife.ArmsPosedElsewhere, handed over eased
/// both ways) and spreads the rest of its stroke after the kill over what is left of the pair, so the knife is put away
/// and the gun comes back as the pair ends.
/// How fast it plays and when it lets the player go are its rule's (ExecutionRules.Timing; the user, 2026-11-18: "전체 암살
/// 모션을 《카운터 스트라이크》 칼 백스탭처럼 빠르고 간결하게 단축"): the clips run at the pace that lands the blade at the
/// rule's killIn, and at its freeAfter past the kill the player is free while the clips ease out on a clock that keeps
/// running (Release), so what is left of the motion fades on a body that already walks on. "The player is free" and
/// "the pair is over" were one moment until then, and a takedown held the player a second past its kill. The owner's PC
/// sends the pace and the moment with the pair, so every PC lets go at the same frame of the clips.
/// It runs right after the execution, so a dash's end begins the pair in the same frame and the pair's timing is in before
/// the class knife's stab reads it (ExecutionKnife, Execution + 5), and before the movement, the body and the zombies; the
/// hands' holds run after the hands' IK and the gun's framework, just before the class knife seats the blade in the fist
/// (ExecutionPairHands).
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Execution + 2)]
public sealed class ExecutionPairPlayer : MonoBehaviour
{
    /// <summary>A pair as it goes to the other PCs: which, on whom, laid where, begun when (the server's clock), how fast,
    /// and where in its clips it lets the player go (s of the clips).</summary>
    public readonly struct Shared
    {
        public readonly byte Pair;
        public readonly ulong Victim;
        public readonly Vector3 Anchor;
        public readonly float Yaw;
        public readonly double Clock;
        public readonly float Pace;
        public readonly float Free;

        public Shared(byte pair, ulong victim, Vector3 anchor, float yaw, double clock, float pace, float free)
        {
            Pair = pair; Victim = victim; Anchor = anchor; Yaw = yaw; Clock = clock; Pace = pace; Free = free;
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
    // The pair has let the player go and only eases out (Release): its clock runs on until the body has left the clip.
    private bool released;
    // The pair's clock, in the clips' own seconds, how fast it runs (its rule's killIn, and a Ghost's 빠른 칼 on top:
    // card, GhostCards.TakedownPace) and where on it the player is let go; pace and free go with the pair, so every PC
    // plays it as fast and lets go at the same frame.
    private float clock, pace = 1f, free, card = 1f;
    private Vector3 anchor, spot;
    private Quaternion anchorTurn = Quaternion.identity;
    private float grown = 1f;
    private readonly Collider[] ignored = new Collider[8];
    private int ignoredCount;
    private Hand left, right;
    // The zombie's bones the pair uses, kept from its start: they go on into the corpse when the kill lays it down, so the
    // blood and the knife's last frames in the neck find them whatever ran first that frame. A hand's, one per grab.
    private Transform hitBone;
    private Transform[] leftBones, rightBones;
    // The player clip's fingers as Blender has them (Tables/Clips/<clip>_fingers), kept past the pair's end while the
    // clip eases out; null for a clip that has none.
    private FingerTrack fingers;
    private readonly FingerWays ways = new FingerWays();
    private PlayerBodyHands hands;

    // A hand's grabs: whether each has begun, the bone's turn when it did and where the wrist goes from that bone.
    private struct Hand
    {
        public bool[] Held;
        public Quaternion[] BoneAt;
        public Vector3[] Offset;

        public Hand(int grabs)
        {
            Held = new bool[grabs];
            BoneAt = new Quaternion[grabs];
            Offset = new Vector3[grabs];
        }
    }

    /// <summary>The player's pair player, made on them if missing (PlayerExecution and PlayerNetworkCombat both ask).</summary>
    public static ExecutionPairPlayer For(GameObject player)
    {
        if (player == null) return null;
        return player.TryGetComponent(out ExecutionPairPlayer pairs) ? pairs : player.AddComponent<ExecutionPairPlayer>();
    }

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
        card = Mathf.Max(.1f, GhostCards.TakedownPace(movement, victim));
    }

    // The execution let the player go (PlayerExecution.Duration, the pair's own free moment), or was cut short before it
    // (its victim gone before the blow, this player dead): the pair eases out either way, and only a cut-short one has
    // to be told to the other PCs, which let go on their own clocks otherwise.
    private void OnEnded()
    {
        waiting = false;
        if (!playing || !ownsPair || released) return;
        bool early = clock < free - .05f;
        Release();
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
        clock += dt * pace;
        if (body == null) body = GetComponent<PlayerBody>();
        if (body != null) body.SetPairedTime(clock);
        if (zombie != null && zombie.BodyRoot != null) zombie.SetExecutedTime(clock);
        if (released)
        {
            // Easing out: the clips go on moving under the fade until the body has left them (or they run out).
            if (clock >= pair.Length || body == null || !body.PlaysPaired) Finish();
            return;
        }
        if (!bled && clock >= pair.Kill) Bleed();
        if (ownsPair)
        {
            Align(dt);
            Aim();
            return;
        }
        // A copy lets go on its own clock, caught up to the owner's, where the owner's does; an early end comes by ShowStop.
        if (clock >= free) Release();
    }

    // ---- The owner's pair -------------------------------------------------------------------------------------------

    private void BeginOwn(MeleeCombatTarget victim)
    {
        if (victim == null || !victim.TryGetComponent(out ZombieBody target) || !target.Wears) return;
        // The pair is the one named by the rule this execution began by (PlayerExecution.Rule, Tables/ExecutionRules.csv):
        // found for what the zombie was and where the player stood at the press, so what was offered is what plays. A rule
        // that names no pair, or whose clips are missing, keeps the knife's own stab.
        var chosen = ExecutionPairs.Find(execution.Rule?.Pair);
        if (chosen == null || !Clips(chosen, out var playerClip, out var zombieClip)) return;
        // Where its body lies now, for laying the pair over it (the dash in may have taken a moment).
        target.ExecutionPosture(out bool lying, out Vector3 hips, out Vector3 head);
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
        // How fast it plays and when it lets go: the rule's, over this pair's own kill and length (and a Ghost's card).
        var timing = ExecutionRules.Timing(execution.Rule, chosen.Kill, chosen.Length, card);
        if (!Begin(chosen, target, playerClip, zombieClip, at, yaw, 0f, timing.Pace, timing.Free * timing.Pace)) return;
        ownsPair = true;
        // The execution's own timers run in real seconds: the blow at the pair's kill, the player let go at its free moment.
        execution.Retime(timing.Kill, timing.Free, timing.Pace);
        IgnoreVictim(target);
        ulong id = target.TryGetComponent(out Unity.Netcode.NetworkObject network) && network.IsSpawned ? network.NetworkObjectId : 0UL;
        var manager = Unity.Netcode.NetworkManager.Singleton;
        double now = manager != null && manager.IsListening ? manager.ServerTime.Time : 0d;
        Began?.Invoke(new Shared(chosen.Index, id, at, yaw, now, pace, free));
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

    // late: how far into its clips it begins (s of the clips: a copy catching up). speed: how fast it plays. lets: where in
    // its clips it lets the player go.
    private bool Begin(ExecutionPair chosen, ZombieBody target, AnimationClip playerClip, AnimationClip zombieClip, Vector3 at, float yaw,
        float late, float speed, float lets)
    {
        if (body == null) body = GetComponent<PlayerBody>();
        if (body == null) return false;
        Stop();
        pair = chosen;
        pace = Mathf.Max(.1f, speed);
        free = Mathf.Clamp(lets, 0f, chosen.Length);
        zombie = target;
        hitBone = target != null ? target.HumanBone(chosen.HitBone) : null;
        leftBones = Bones(target, chosen.LeftHolds);
        rightBones = Bones(target, chosen.RightHolds);
        fingers = FingerTrack.Of(playerClip.name);
        grown = target != null ? Mathf.Max(.1f, target.Grown) : 1f;
        anchor = at;
        anchorTurn = Quaternion.Euler(0f, yaw, 0f);
        spot = anchor + anchorTurn * new Vector3(pair.Offset.x, 0f, pair.Offset.y) * grown;
        var spotTurn = anchorTurn * Quaternion.Euler(0f, pair.Yaw, 0f);
        // The clips' blend in and the slide onto the pair's place are part of the motion: at a faster pace they take as
        // much less real time (they are counted in real seconds on the bodies). The ease out is the bodies' own return.
        if (!body.BeginPaired(playerClip, spot, spotTurn, pair.Length, pair.BlendIn / pace, pair.BlendOut, pair.Align / pace)) return false;
        if (zombie != null) zombie.BeginExecuted(zombieClip, anchor, anchorTurn, pair.Length, pair.BlendIn / pace, pair.BlendOut, pair.Align / pace);
        // The class knife (ExecutionKnife) keeps riding the fist but leaves the arms to the clip; its stroke's end clears it.
        if (TryGetComponent(out ExecutionKnife knife)) knife.ArmsPosedElsewhere = true;
        playing = true;
        released = false;
        clock = Mathf.Clamp(late, 0f, pair.Length);
        bled = clock > pair.Kill;
        left = new Hand(chosen.LeftHolds.Length);
        right = new Hand(chosen.RightHolds.Length);
        return true;
    }

    private static Transform[] Bones(ZombieBody target, ExecutionPair.Contact[] holds)
    {
        var bones = new Transform[holds.Length];
        for (int i = 0; i < holds.Length; i++) bones[i] = target != null ? target.HumanBone(holds[i].Bone) : null;
        return bones;
    }

    /// <summary>
    /// On every other PC: the owner's PC began this pair on that zombie, laid there, <paramref name="late"/> seconds ago,
    /// playing at <paramref name="speed"/> and letting the player go at <paramref name="lets"/> seconds of its clips
    /// (PlayerNetworkCombat; 0 or less: at its end). The host's copy of a remote executor takes the pair's timing too.
    /// </summary>
    internal void Show(byte index, GameObject victim, Vector3 at, float yaw, float late, float speed = 1f, float lets = 0f)
    {
        var chosen = ExecutionPairs.At(index);
        if (chosen == null || !Clips(chosen, out var playerClip, out var zombieClip)) return;
        ZombieBody target = victim != null && victim.TryGetComponent(out ZombieBody found) && found.Wears ? found : null;
        speed = Mathf.Max(.1f, speed);
        if (lets <= 0f) lets = chosen.Length;
        if (!Begin(chosen, target, playerClip, zombieClip, at, yaw, late * speed, speed, lets)) return;
        ownsPair = false;
        // Timed from now, as the owner's knife stab that came with this word begins here now: its strike on the pair's
        // kill and its letting go where the pair lets go as this copy plays them (ExecutionKnife reads them), in real seconds.
        if (execution != null) execution.Retime(chosen.Kill / speed - late, free / speed - late, speed);
    }

    /// <summary>On every other PC: the owner's pair was cut short (the host said no, its victim was gone before the blow).</summary>
    internal void ShowStop() => Release();

    // The pair lets the player go, at its own moment or cut short: the capsule meets the victim's again, both bodies
    // start easing out of their clips and the class knife has the arms again; the clock runs on meanwhile (Update), so
    // the motion goes on under the fade.
    private void Release()
    {
        GiveBackVictim();
        if (!playing || released) return;
        released = true;
        if (body != null) body.EndPaired();
        if (zombie != null && zombie.BodyRoot != null) zombie.EndExecuted();
        // The class knife's arms again, eased in from where the clip left them (ExecutionKnifeArm): its stroke pulls the
        // blade out and puts it away from here.
        if (TryGetComponent(out ExecutionKnife knife)) knife.ArmsPosedElsewhere = false;
        // The execution's own timing again (a copy that runs none keeps the pair's until now).
        if (execution != null) execution.ForgetPair();
        ownsPair = false;
    }

    // The bodies have left the clips: nothing of the pair is kept.
    private void Finish()
    {
        playing = released = false;
        zombie = null;
        hitBone = null;
        leftBones = rightBones = null;
    }

    // At once, with no clock to run on: another pair begins, or this component goes.
    private void Stop()
    {
        Release();
        Finish();
    }

    // ---- The hands' holds, after the arms are posed and before the knife is seated (every PC) -----------------------

    /// <summary>From ExecutionPairHands, after the arms' poses and before the knife's seat: the hands kept on their holds.</summary>
    internal void KeepHands()
    {
        if (!playing || pair == null || body == null || Time.deltaTime <= 0f) return;
        var animator = body.BodyAnimator;
        if (animator == null || !animator.isHuman) return;
        Keep(ref left, pair.LeftHolds, leftBones, animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        Keep(ref right, pair.RightHolds, rightBones, animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
    }

    /// <summary>
    /// From ExecutionPairHands, after the holds: both hands' fingers as the pair's player clip has them in Blender, every
    /// joint (FingerTrack), as much as the clip has the body, its ease out included. A humanoid clip keeps a finger's bend
    /// and spread but not a segment's twist, the outer joints' side swing or the thumb's roll, and the hands' own passes
    /// (HandGrip.RelaxThumb, KeepHuman, before this) replace or clamp what it does carry; this puts every joint back, on
    /// any character's hand (the user, 2026-11-14: "네, 다 고쳐"). The knife's own draw (ExecutionKnifeArm, after this)
    /// still has the right hand while it plays. A clip with no track keeps its fingers as before.
    /// </summary>
    internal void WearFingers()
    {
        if (fingers == null || body == null) return;
        float weight = body.PairedWeight;
        if (weight <= 0f) return;
        var animator = body.BodyAnimator;
        if (animator == null || !animator.isHuman) return;
        if (hands == null && !TryGetComponent(out hands)) return;
        float time = body.PairedClipTime;
        if (fingers.Sample(true, time, ways)) hands.WearFingerWays(animator, true, ways, weight);
        if (fingers.Sample(false, time, ways)) hands.WearFingerWays(animator, false, ways, weight);
    }

    // A hand on its holds: at each grab's start the bone's turn is noted, and from then the wrist goes where the pair had
    // it from that bone, carried as the bone turns, eased on and off at the grab's ends. A hand that lets go and takes hold
    // again is kept on every grab; where one grab eases off as the next eases on, the stronger leads. On the body it was
    // made on this asks for next to nothing; on a bigger zombie or another character it keeps the hand on the head.
    private void Keep(ref Hand hand, ExecutionPair.Contact[] holds, Transform[] bones, Animator animator, HumanBodyBones upperBone,
        HumanBodyBones lowerBone, HumanBodyBones handBone)
    {
        if (holds == null || bones == null || hand.Held == null) return;
        int grab = -1;
        float weight = 0f;
        for (int i = 0; i < holds.Length && i < bones.Length && i < hand.Held.Length; i++)
        {
            var hold = holds[i];
            if (!hold.Valid || bones[i] == null || clock < hold.From || clock > hold.To + HoldOut) { hand.Held[i] = false; continue; }
            if (!hand.Held[i])
            {
                hand.Held[i] = true;
                hand.BoneAt[i] = bones[i].rotation;
                hand.Offset[i] = anchorTurn * hold.Offset * grown;
            }
            float on = Mathf.Min(Mathf.Clamp01((clock - hold.From) / HoldIn), Mathf.Clamp01((hold.To + HoldOut - clock) / HoldOut));
            if (on >= weight)
            {
                weight = on;
                grab = i;
            }
        }
        // Let go, the hold fades with the clip it belongs to.
        if (released) weight *= body.PairedWeight;
        if (grab < 0 || weight <= 0f) return;
        var upper = animator.GetBoneTransform(upperBone);
        var lower = animator.GetBoneTransform(lowerBone);
        var end = animator.GetBoneTransform(handBone);
        if (upper == null || lower == null || end == null) return;
        var bone = bones[grab];
        Vector3 target = bone.position + bone.rotation * Quaternion.Inverse(hand.BoneAt[grab]) * hand.Offset[grab];
        Quaternion turned = end.rotation;
        TwoBoneIK.Solve(upper, lower, end, Vector3.Lerp(end.position, target, weight), lower.position);
        // The hand keeps the turn the clip gave it.
        end.rotation = turned;
    }
}
