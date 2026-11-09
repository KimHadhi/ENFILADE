using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Multiplayer plan stage 4A: the enemy's capsule wears a zombie. A character from ZombieLooks is put on it, the same
/// one on every PC (picked from the network id). The capsule still walks, blocks players, gets shoved and stood on as
/// before, fitted to the character's height, but it is hidden and bullets pass it: trigger hitboxes on the bones take
/// them instead, and the head box is the headshot (AttackTrace, WeaponHitRegion). Melee still sweeps the capsule.
/// Our own small playable graph animates the body from what the enemy already does, with no Animator Controller:
/// idle, walk and run by its real speed (the feet keep the clips' own pace), the swing timed so its strike frame lands
/// on the windup's hit, a flinch when it is interrupted and a reel while its posture is broken. It only reads the AI,
/// so damage, the AI and the network are unchanged; a copy on another PC animates from its own movement the same way.
/// On death (Fall, stage 4B) the character leaves the enemy and lies as its own corpse on a pooled doll (ZombieCorpse),
/// whose dolls this stocks ahead while the zombie lives. Knocked down, it falls on such a doll and gets up with a clip
/// (ZombieBody.Down.cs, stage 4B-2). It shows what the zombie knows (ZombieBody.Moods.cs, 4C), having lost a leg it
/// crawls (ZombieBody.Crawl.cs, 4D-1), and having lost an arm it swings with the other or bites (ZombieBody.Arms.cs).
/// </summary>
[DisallowMultipleComponent]
public sealed partial class ZombieBody : MonoBehaviour
{
    // The feet's pace when a clip has no root motion to measure (m/s), and how far past it a clip may speed up.
    private const float WalkFallback = 1.2f, RunFallback = 4f, MaxRate = 1.6f;
    private const float SpeedSharpness = 10f, AttackIn = .12f, AttackHold = .35f, AttackOut = .3f;
    private const float FlinchIn = .06f, FlinchOut = .15f, StaggerIn = .1f, StaggerOut = .25f;
    // Hitbox sizes (m). The head sphere sits this far up from the head bone, which is at the top of the neck.
    private const float HeadLift = .09f, HeadRadius = .13f, TorsoDepth = .24f, PelvisDrop = .12f;
    private const float UpperArm = .065f, LowerArm = .05f, UpperLeg = .09f, LowerLeg = .065f;

    private static readonly HashSet<Collider> replaced = new HashSet<Collider>(), hitboxes = new HashSet<Collider>();

    /// <summary>A capsule a zombie body stands in: bullets go past it to the bones (AttackTrace).</summary>
    public static bool Replaces(Collider collider) => collider != null && replaced.Contains(collider);
    /// <summary>A zombie's bone hitbox: a trigger that still takes bullets (AttackTrace).</summary>
    public static bool IsHitbox(Collider collider) => collider != null && hitboxes.Contains(collider);

    /// <summary>The character worn, or null while the enemy is a plain capsule or once it has fallen as a corpse.</summary>
    public GameObject Model { get; private set; }
    /// <summary>It was dressed as a zombie: its death is its own body falling, not a stand-in (CorpseTopple).</summary>
    public bool Wears { get; private set; }
    /// <summary>Its head bone, where it looks from (ZombieMind); null until dressed.</summary>
    public Transform Head => human != null ? human[(int)HumanBodyBones.Head] : null;

    private EnemyCombatAI combat;
    private MeleeCombatTarget target;
    private EnemyNetwork network;
    private Animator animator;
    private readonly List<Collider> owned = new List<Collider>(), covered = new List<Collider>();
    private PlayableGraph graph;
    private AnimationMixerPlayable top, moves;
    private AnimationClipPlayable idle, walk, run, attack, flinch, stagger, riseDown, riseUp;
    private float idleLength, walkLength, runLength, flinchLength, staggerLength, riseDownLength, riseUpLength;
    private float walkSpeed, runSpeed, attackHit;
    private float speed, phase, idleTime, attackWeight, sinceStrike, flinchTime, flinchWeight, swingWindup, swingElapsed;
    private float staggerTime, staggerWeight;
    private bool striking, flinching, swingPreparing;
    private Vector3 lastPosition, moving;
    private int dressedFrame;
    // Its human bones by HumanBodyBones, read once the Animator is on: it may die with the Animator off (knocked down).
    private Transform[] human;

    private void Start()
    {
        combat = GetComponent<EnemyCombatAI>();
        target = GetComponent<MeleeCombatTarget>();
        network = GetComponent<EnemyNetwork>();
        mind = GetComponent<ZombieMind>();
        GameTables.Bind(KnockdownTable, KnockdownRow, this);
        var looks = ZombieLooks.Instance;
        var character = Pick(looks);
        if (character == null || looks == null || !Dress(character)) return;
        BuildGraph(looks);
        if (target != null) target.Interrupted += OnInterrupted;
        WatchCrawl();
    }

    private void OnDestroy()
    {
        StopWatchingCrawl();
        PutOutEyes();
        GiveDollBack();
        if (target != null) target.Interrupted -= OnInterrupted;
        foreach (var collider in owned) hitboxes.Remove(collider);
        foreach (var collider in covered) replaced.Remove(collider);
        if (graph.IsValid()) graph.Destroy();
    }

    /// <summary>
    /// Its own humanoid body if the enemy brings one (an elite, a boss, any kind made later: it then moves, is hit, goes
    /// down and lies as a pushable corpse like any zombie); otherwise one of the pack's characters, the same on every PC:
    /// from the network id in a room, at random alone.
    /// </summary>
    private GameObject Pick(ZombieLooks looks)
    {
        var own = OwnBody();
        if (own != null) return own;
        if (looks == null || looks.characters == null || looks.characters.Length == 0) return null;
        int count = looks.characters.Length;
        int start = network != null && network.IsSpawned
            ? (int)(unchecked(network.NetworkObjectId * 2654435761UL) % (ulong)count)
            : Random.Range(0, count);
        for (int i = 0; i < count; i++)
        {
            var character = looks.characters[(start + i) % count];
            if (character != null) return character;
        }
        return null;
    }

    // The child of this enemy that holds a humanoid Animator, if the prefab was made with a body of its own.
    private GameObject OwnBody()
    {
        foreach (var found in GetComponentsInChildren<Animator>(true))
        {
            if (found == null || !found.isHuman || found.avatar == null || !found.avatar.isValid) continue;
            var top = found.transform;
            while (top.parent != null && top.parent != transform) top = top.parent;
            if (top.parent == transform) return top.gameObject;
        }
        return null;
    }

    private bool Dress(GameObject character)
    {
        var capsule = GetComponent<CapsuleCollider>();
        var agent = GetComponent<NavMeshAgent>();
        float feet = capsule != null ? capsule.center.y - capsule.height * .5f : agent != null ? -agent.baseOffset : 0f;
        // Its own body stays as the prefab placed it; one of the pack's stands on the capsule's feet.
        bool own = character.transform.parent == transform;
        Model = own ? character : Instantiate(character, transform);
        if (!own)
        {
            Model.name = character.name;
            Model.transform.localPosition = new Vector3(0f, feet, 0f);
            Model.transform.localRotation = Quaternion.identity;
            Model.transform.localScale = Vector3.one;
        }
        animator = Model.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman || !animator.avatar.isValid)
        {
            Debug.LogWarning($"[ZombieBody] {character.name} has no humanoid Animator; {name} stays a capsule.", this);
            Destroy(Model); Model = null;
            return false;
        }
        // Synty's prefabs carry every character of the pack with all but one switched off: drop the others. A body of its
        // own keeps what its maker switched off.
        if (!own)
            foreach (var part in Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!part.gameObject.activeSelf) Destroy(part.gameObject);
        SetLayer(Model.transform, gameObject.layer);
        // The agent moves it; off screen the Animator skips posing as the prefab sets (the hitboxes then hold still).
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        animator.gameObject.AddComponent<ZombieClipEvents>();
        human = RagdollCorpse.HumanBones(animator);
        FindBlendBones();
        feetHeight = feet;

        float top = BuildHitboxes(feet);
        MeasureEyes();
        // 524: a body's depth round, not a metre across (ZombieCrowd), so crowds stand close and players come right up.
        var crowd = GetComponent<ZombieCrowd>();
        if (crowd != null)
        {
            if (capsule != null) capsule.radius = crowd.BodyRadius;
            if (agent != null) agent.radius = crowd.BodyRadius;
        }
        float height = Mathf.Clamp(top - feet + .04f, 1.3f, 2.1f);
        if (capsule != null)
        {
            capsule.height = Mathf.Max(height, capsule.radius * 2f);
            capsule.center = new Vector3(capsule.center.x, feet + capsule.height * .5f, capsule.center.z);
        }
        if (agent != null) agent.height = height;
        foreach (var collider in GetComponents<Collider>())
            if (!collider.isTrigger) { replaced.Add(collider); covered.Add(collider); }
        if (TryGetComponent(out MeshRenderer shell)) shell.enabled = false;
        lastPosition = transform.position;
        dressedFrame = Time.frameCount;
        Wears = true;
        return true;
    }

    /// <summary>
    /// Death: the character leaves the enemy (which is removed or hidden as before) and falls as its own corpse.
    /// Simulated: this PC runs the fall; runner is the player whose PC does.
    /// </summary>
    internal void Fall(in DamageHit hit, bool simulated, PlayerMovement runner)
    {
        if (Model == null || animator == null) return;
        if (target != null) target.Interrupted -= OnInterrupted;
        // Down already, the corpse carries on with the doll it lies on.
        var lying = HandOverDoll();
        blendLeft = 0f;
        if (graph.IsValid()) graph.Destroy();
        // A hit flash still running on the enemy lets go of the body first, so the corpse's own kill flash shows.
        if (TryGetComponent(out HitFlashRenderer flash)) flash.enabled = false;
        foreach (var collider in owned)
        {
            hitboxes.Remove(collider);
            if (collider != null) Destroy(collider.gameObject);
        }
        owned.Clear();
        PutOutEyes();
        var body = Model;
        Model = null;
        ulong id = network != null && network.IsSpawned ? network.NetworkObjectId : 0UL;
        ZombieCorpse.Lay(body, animator, human, hit, simulated, id, runner, moving, lying);
        animator = null;
    }

    private static void SetLayer(Transform part, int layer)
    {
        part.gameObject.layer = layer;
        foreach (Transform child in part) SetLayer(child, layer);
    }

    // ---- Hitboxes ----------------------------------------------------------------------------------------------

    /// <summary>Head sphere, a box per torso bone, a capsule per arm and leg bone. Returns the head's top in local y.</summary>
    private float BuildHitboxes(float feet)
    {
        Vector3 up = Model.transform.up, forward = Model.transform.forward;
        var head = Bone(HumanBodyBones.Head);
        float top = feet + 1.75f;
        if (head != null)
        {
            Vector3 center = head.position + up * HeadLift;
            var sphere = Hitbox(head, center, Quaternion.LookRotation(forward, up)).AddComponent<SphereCollider>();
            sphere.radius = HeadRadius;
            Own(sphere, WeaponHitRegion.Region.Head);
            top = transform.InverseTransformPoint(center + up * HeadRadius).y;
        }

        var leftArm = Bone(HumanBodyBones.LeftUpperArm);
        var rightArm = Bone(HumanBodyBones.RightUpperArm);
        float width = leftArm != null && rightArm != null
            ? Mathf.Clamp(Vector3.Distance(leftArm.position, rightArm.position) * .85f, .28f, .46f) : .36f;
        // Each torso box rides its own bone, so a hunched spine carries the chest box with it.
        var chain = new List<Transform>();
        foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
                     HumanBodyBones.Neck })
        {
            var found = Bone(bone);
            if (found != null && !chain.Contains(found)) chain.Add(found);
        }
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            Vector3 from = chain[i].position - (i == 0 ? up * PelvisDrop : Vector3.zero);
            Box(chain[i], from, chain[i + 1].position, width, forward);
        }

        Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, UpperArm);
        Limb(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, LowerArm);
        Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, UpperArm);
        Limb(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, LowerArm);
        Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, UpperLeg);
        Limb(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, LowerLeg);
        Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, UpperLeg);
        Limb(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, LowerLeg);
        return top;
    }

    private Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);

    private void Box(Transform bone, Vector3 from, Vector3 to, float width, Vector3 forward)
    {
        Vector3 axis = to - from;
        float length = axis.magnitude;
        if (length < .02f) return;
        Vector3 facing = Vector3.ProjectOnPlane(forward, axis);
        if (facing.sqrMagnitude < 1e-4f) facing = Vector3.ProjectOnPlane(Model.transform.right, axis);
        var box = Hitbox(bone, (from + to) * .5f, Quaternion.LookRotation(facing, axis)).AddComponent<BoxCollider>();
        box.size = new Vector3(width, length + .04f, TorsoDepth);
        Own(box, WeaponHitRegion.Region.Body);
    }

    private void Limb(HumanBodyBones start, HumanBodyBones end, float radius)
    {
        var from = Bone(start);
        var to = Bone(end);
        if (from == null || to == null) return;
        Vector3 axis = to.position - from.position;
        float length = axis.magnitude;
        if (length < .02f) return;
        var limb = Hitbox(from, (from.position + to.position) * .5f, Quaternion.LookRotation(axis)).AddComponent<CapsuleCollider>();
        limb.direction = 2;
        limb.radius = radius;
        limb.height = length + radius;
        Own(limb, WeaponHitRegion.Region.Body);
    }

    private GameObject Hitbox(Transform bone, Vector3 position, Quaternion rotation)
    {
        var box = new GameObject("Hitbox") { layer = gameObject.layer };
        box.transform.SetParent(bone, false);
        box.transform.SetPositionAndRotation(position, rotation);
        // Sizes are in metres whatever scale the rig's bones carry.
        Vector3 scale = bone.lossyScale;
        box.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(1e-4f, Mathf.Abs(scale.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
        return box;
    }

    private void Own(Collider collider, WeaponHitRegion.Region region)
    {
        collider.isTrigger = true;
        collider.gameObject.AddComponent<WeaponHitRegion>().Kind = region;
        hitboxes.Add(collider);
        owned.Add(collider);
    }

    // ---- Animation ---------------------------------------------------------------------------------------------

    private void BuildGraph(ZombieLooks looks)
    {
        var rest = Or(looks.idle, looks.walk);
        if (rest == null) return;
        graph = PlayableGraph.Create(name + " (Zombie)");
        // The Animator poses it as usual, but every clip stands still (speed 0) and its time is set here each frame,
        // so slow motion, the windup and the feet follow the game rather than the clock.
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Body", animator);
        // Idle and walk (inputs 0 and 1) are mood mixers (ZombieBody.Moods.cs); run is plain.
        moves = AnimationMixerPlayable.Create(graph, 3);
        run = Clip(Or(looks.run, Or(looks.walk, rest)), moves, 2, out runLength);
        top = AnimationMixerPlayable.Create(graph, 20);
        graph.Connect(moves, 0, top, 0);
        attack = Clip(Or(looks.attack, rest), top, 1, out float attackLength);
        flinch = Clip(Or(looks.flinch, rest), top, 2, out flinchLength);
        stagger = Clip(Or(looks.stagger, Or(looks.flinch, rest)), top, 3, out staggerLength);
        // Without a getting-up clip it eases from the doll straight into standing.
        riseDown = Clip(Or(looks.riseFaceDown, rest), top, 4, out riseDownLength);
        riseUp = Clip(Or(looks.riseFaceUp, Or(looks.riseFaceDown, rest)), top, 5, out riseUpLength);
        BuildMoods(looks, rest);
        BuildCrawl(looks, rest);
        BuildBite(looks, rest);
        BuildLeap(looks, rest);
        output.SetSourcePlayable(top);
        walkSpeed = RootSpeed(looks.walk, WalkFallback);
        runSpeed = Mathf.Max(walkSpeed + .5f, RootSpeed(looks.run, RunFallback));
        searchWalkSpeed = Mathf.Min(walkSpeed, RootSpeed(looks.searchWalk, walkSpeed));
        attackHit = HitTime(looks.attack, attackLength);
        graph.Play();
        riseDownShape = RiseShape(looks.riseFaceDown, 4);
        riseUpShape = RiseShape(Or(looks.riseFaceUp, looks.riseFaceDown), 5);
        riseCrawlShape = RiseShape(looks.toCrawl, 11);
        Weigh();
    }

    private static AnimationClip Or(AnimationClip clip, AnimationClip fallback) => clip != null ? clip : fallback;

    private AnimationClipPlayable Clip(AnimationClip clip, AnimationMixerPlayable mixer, int input, out float length)
    {
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(true);
        playable.SetSpeed(0d);
        graph.Connect(playable, 0, mixer, input);
        length = Mathf.Max(.05f, clip.length);
        return playable;
    }

    /// <summary>How fast the clip's own root travels (m/s): the pace at which its feet don't slide.</summary>
    private float RootSpeed(AnimationClip clip, float fallback)
    {
        if (clip == null) return fallback;
        Vector3 average = clip.averageSpeed;
        float pace = new Vector2(average.x, average.z).magnitude * animator.humanScale;
        return pace > .3f && pace < 9f ? pace : fallback;
    }

    /// <summary>The strike frame: the clip's own AnimationEvent_AttackHit, else a third of the way in.</summary>
    private static float HitTime(AnimationClip clip, float length)
    {
        if (clip != null)
            foreach (var cue in clip.events)
                if (cue.functionName != null && cue.functionName.EndsWith("AttackHit")) return Mathf.Clamp(cue.time, .05f, length);
        return length * .35f;
    }

    private void OnInterrupted()
    {
        flinching = true;
        flinchTime = 0f;
    }

    private void Update()
    {
        if (!graph.IsValid()) return;
        float real = Time.deltaTime;
        if (real <= 0f) return;
        float pace = network != null ? network.Pace : 1f;
        float dt = real * pace;
        WatchDown();
        SettleArms();
        Moods(dt);
        Move(real, pace, dt);
        Act(dt);
        ArmsTick();
        LeapTick(dt);
        EyesTick();
        CrawlTick(dt);
        TickRise(dt);
        Weigh();
        // From the frame after dressing, once the pack's spare meshes are gone, so a doll's copy of the body is light.
        if (Time.frameCount > dressedFrame) ZombieCorpse.Stock(animator);
    }

    /// <summary>Idle, walk and run by the body's real speed, the walk and run on one shared step so they blend.</summary>
    private void Move(float real, float pace, float dt)
    {
        Vector3 step = transform.position - lastPosition;
        lastPosition = transform.position;
        // In slow motion it covers less ground; the legs show the same gait, slowed (dt), not a walk.
        float measured = new Vector2(step.x, step.z).magnitude / real / Mathf.Max(.05f, pace);
        if (measured > 20f) measured = 0f; // a placement (spawn, getting up), not a stride
        // What a corpse carries on with if it dies now.
        moving = measured > 0f ? step / real : Vector3.zero;
        speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-SpeedSharpness * real));
        // Searching, the slower search walk sets the pace.
        float stepSpeed = StepSpeed, walking, running;
        if (speed <= stepSpeed) { walking = Mathf.SmoothStep(0f, 1f, speed / stepSpeed); running = 0f; }
        else { running = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(stepSpeed, runSpeed, speed)); walking = 1f - running; }
        moves.SetInputWeight(0, 1f - walking - running);
        moves.SetInputWeight(1, walking);
        moves.SetInputWeight(2, running);
        float rate = speed > runSpeed ? Mathf.Min(speed / runSpeed, MaxRate) : 1f;
        phase = Mathf.Repeat(phase + dt * rate / Mathf.Lerp(StepLength, runLength, running), 1f);
        walk.SetTime(phase * walkLength);
        searchWalk.SetTime(phase * searchWalkLength);
        run.SetTime(phase * runLength);
        idleTime = Mathf.Repeat(idleTime + dt, idleLength);
        idle.SetTime(idleTime);
    }

    private void Act(float dt)
    {
        // The swing follows the windup: its strike frame lands when the windup hits, however long the windup is.
        swingPreparing = combat != null && combat.IsPreparingAttack;
        if (swingPreparing)
        {
            swingWindup = combat.Windup;
            swingElapsed = Mathf.Min(combat.WindupElapsed, swingWindup);
            sinceStrike = 0f;
            striking = true;
            attackWeight = Mathf.MoveTowards(attackWeight, 1f, dt / AttackIn);
        }
        else if (striking)
        {
            // Follow through a moment, then hand back to the legs.
            sinceStrike += dt;
            if (sinceStrike > AttackHold) attackWeight = Mathf.MoveTowards(attackWeight, 0f, dt / AttackOut);
            if (attackWeight <= 0f) striking = false;
        }
        attack.SetTime(SwingTime(attackHit));

        if (flinching)
        {
            flinchTime += dt;
            bool over = flinchTime >= flinchLength - FlinchOut;
            flinchWeight = Mathf.MoveTowards(flinchWeight, over ? 0f : 1f, dt / (over ? FlinchOut : FlinchIn));
            if (over && flinchWeight <= 0f) flinching = false;
        }
        flinch.SetTime(Mathf.Min(flinchTime, flinchLength));

        bool reeling = target != null && target.IsStaggered && !target.IsKnockedDown;
        if (reeling && staggerWeight <= 0f) staggerTime = 0f;
        if (reeling) staggerTime += dt;
        staggerWeight = Mathf.MoveTowards(staggerWeight, reeling ? 1f : 0f, dt / (reeling ? StaggerIn : StaggerOut));
        stagger.SetTime(Mathf.Min(staggerTime, staggerLength));
    }

    // Where in the swing clip it is: before the strike, its strike frame lands when the windup does; after, it follows through.
    private float SwingTime(float hitAt)
    {
        if (!swingPreparing) return hitAt + sinceStrike;
        return swingWindup <= hitAt ? hitAt - swingWindup + swingElapsed : hitAt * swingElapsed / Mathf.Max(.01f, swingWindup);
    }

    /// <summary>
    /// Getting up over reel over flinch over a mood's one-off clip (notice, rouse) over swing over the legs, always adding
    /// up to one (less would sag toward the rest pose). Crawling, the legs, swing and flinch are the crawl's own, and it
    /// neither reels nor shows its moods.
    /// </summary>
    private void Weigh()
    {
        // Holding a player (G&B-1), its hold covers all but getting up.
        float rise = Mathf.Clamp01(riseWeight), hold = holdWeight * (1f - rise), rest = 1f - rise - hold, crawl = crawlShare, stand = 1f - crawl;
        // A runner's leap and its landing (ZombieBody.Leap.cs) cover the rest of the standing body.
        float jumping = Mathf.Clamp01(leapWeight + landWeight) * stand * rest;
        rest -= jumping;
        float leapShare = leapWeight + landWeight > 0f ? leapWeight / (leapWeight + landWeight) : 1f;
        top.SetInputWeight(LeapInput, jumping * leapShare);
        top.SetInputWeight(LandInput, jumping * (1f - leapShare));
        float reel = staggerWeight * stand;
        float flinched = flinchWeight * (1f - reel);
        float cueShown = cueWeight * stand;
        float cue = cueShown * (1f - reel) * (1f - flinchWeight);
        float swing = attackWeight * (1f - reel) * (1f - flinchWeight) * (1f - cueShown);
        float legs = Mathf.Max(0f, 1f - reel - flinched - cue - swing);
        top.SetInputWeight(0, legs * stand * rest);
        top.SetInputWeight(8, legs * crawl * rest);
        // With an arm gone its swing is the one for the arm it kept (ZombieBody.Arms.cs, input 12); a bite is its own (13).
        float standSwing = swing * stand * rest;
        bool special = swingIsBite || swingIsGrab || swingIsFeed;
        top.SetInputWeight(1, armSwingOn || special ? 0f : standSwing);
        top.SetInputWeight(12, armSwingOn && !special ? standSwing : 0f);
        top.SetInputWeight(13, swingIsBite ? standSwing : 0f);
        float grabbing = swingIsGrab && !swingIsBite ? standSwing : 0f;
        float kneeling = swingIsFeed && !swingIsBite && !swingIsGrab ? standSwing : 0f;
        top.SetInputWeight(14, grabbing);
        top.SetInputWeight(16, kneeling);
        top.SetInputWeight(15, hold * (1f - feedShare));
        top.SetInputWeight(17, hold * feedShare);
        FaceGrabs(grabbing, hold * (1f - feedShare), kneeling, hold * feedShare);
        top.SetInputWeight(9, swing * crawl * rest);
        top.SetInputWeight(2, flinched * stand * rest);
        top.SetInputWeight(10, flinched * crawl * rest);
        top.SetInputWeight(3, reel * rest);
        top.SetInputWeight(6, cueRouse ? 0f : cue * rest);
        top.SetInputWeight(7, cueRouse ? cue * rest : 0f);
        top.SetInputWeight(4, !crawling && riseFaceDown ? rise : 0f);
        top.SetInputWeight(5, !crawling && !riseFaceDown ? rise : 0f);
        top.SetInputWeight(11, crawling ? rise : 0f);
    }
}
