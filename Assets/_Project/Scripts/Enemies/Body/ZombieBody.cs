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
    // Which of the looks' clips this zombie moves with (ZombieLooks.Pick): picked from its network id, so every PC
    // shows the same walk, idle and swing for it. Its swing's strike is measured at its first swing if the clip doesn't mark it.
    private uint clipSeed;
    private AnimationClip attackClip, walkClip, runClip, searchWalkClip;
    private bool attackMeasured;
    // The slots' salts for the pick, so one zombie's walk doesn't decide its idle.
    private const uint IdleSlot = 1, WalkSlot = 2, RunSlot = 3, AttackSlot = 4, FlinchSlot = 5, StaggerSlot = 6, CalmSlot = 7,
        AlertSlot = 8, SearchSlot = 9, NoticeSlot = 10, RouseSlot = 11, CrawlAttackSlot = 12, PoseSlot = 13, PhaseSlot = 20;
    private Vector3 lastPosition, moving;
    private int dressedFrame;
    [SerializeField, Min(1f), Tooltip("A kind that doesn't run (ZombieKinds runs 0) plays its walk up to this many times as fast " +
        "when it hurries (Tables/ZombieClips hurryRate). Past that its feet slide a little rather than look sped up.")]
    private float hurryRate = 2.4f;
    // Its kind, for whether it runs or hurries in its walk, and how far its gait has gone from walking (0) to running (1).
    private ZombieType kinds;
    private float runShare = 1f;
    private bool gaitKnown;
    // Its human bones by HumanBodyBones, read once the Animator is on: it may die with the Animator off (knocked down).
    private Transform[] human;

    private void Start()
    {
        combat = GetComponent<EnemyCombatAI>();
        target = GetComponent<MeleeCombatTarget>();
        network = GetComponent<EnemyNetwork>();
        mind = GetComponent<ZombieMind>();
        GameTables.Bind(KnockdownTable, KnockdownRow, this);
        GameTables.Bind(ClipsTable, ClipsRow, this);
        var looks = ZombieLooks.Instance;
        var character = Pick(looks);
        if (character == null || looks == null || !Dress(character)) return;
        BuildGraph(looks);
        BuildJolt();
        if (target != null) target.Interrupted += OnInterrupted;
        WatchCrawl();
        Arrive();
        // The animation probe looks at it from here (ZombieBody.Probe.cs, Logs/zombie-anim.txt).
        if (graph.IsValid()) ProbeDressed();
    }

    private void OnDestroy()
    {
        ProbeGone("removed");
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
        // A kind with a body of its own in EliteLooks always wears it, so it is known at a glance (the user, 2026-11-12:
        // the brute a riot cop, the exploder a hazmat suit, the charger a footballer, the candyman a clown, the reaper a
        // firefighter). The pack's list leaves those bodies out; the other kinds wear one of the list's.
        if (TryGetComponent(out ZombieType kind))
        {
            var elite = EliteLooks.For(kind.Kind).character;
            if (elite != null) return elite;
        }
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
        // Its own body stays as the prefab placed it; one of the pack's stands on the capsule's feet, as big as its kind
        // wants (a brute, ZombieBody.Brute.cs).
        bool own = character.transform.parent == transform;
        wearsOwn = own;
        grown = own ? 1f : GrowthWanted;
        Model = own ? character : Instantiate(ZombieLookTemplates.Of(character), transform);
        if (!own)
        {
            Model.name = character.name;
            Model.transform.localPosition = new Vector3(0f, feet, 0f);
            Model.transform.localRotation = Quaternion.identity;
            Model.transform.localScale = Vector3.one * grown;
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

        // Fitted to this look's own body (ZombieBody.Hitboxes.cs), or the fixed sizes below when its skin can't be read.
        float top = FitHitboxes(feet);
        MeasureEyes();
        // 524: a body's depth round, not a metre across (ZombieCrowd), so crowds stand close and players come right up.
        var crowd = GetComponent<ZombieCrowd>();
        if (crowd != null)
        {
            if (capsule != null) capsule.radius = crowd.BodyRadius * grown;
            if (agent != null) agent.radius = crowd.BodyRadius * grown;
        }
        float height = Mathf.Clamp(top - feet + .04f, 1.3f * grown, 2.1f * grown);
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
        if (!Mathf.Approximately(grown, 1f)) ZombieCorpse.MarkGrown(animator);
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
        ProbeGone("died");
        // The corpse takes the pose as it is, a jolt it was in included; nothing more goes onto its bones.
        StopJolt();
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

    /// <summary>
    /// The fixed sizes, for a look whose skin can't be read (FitHitboxes fits the rest): head sphere, a box per torso
    /// bone, a capsule per arm and leg bone. Returns the head's top in local y.
    /// </summary>
    private float BuildHitboxes(float feet)
    {
        Vector3 up = Model.transform.up, forward = Model.transform.forward;
        // Lengths come from the bones; the thicknesses are in metres for the pack's size, so a bigger body (a brute,
        // `grown`) gets them as much bigger.
        var head = Bone(HumanBodyBones.Head);
        float top = feet + 1.75f * grown;
        if (head != null)
        {
            Vector3 center = head.position + up * HeadLift * grown;
            var sphere = Hitbox(head, center, Quaternion.LookRotation(forward, up)).AddComponent<SphereCollider>();
            sphere.radius = HeadRadius * grown;
            Own(sphere, WeaponHitRegion.Region.Head);
            top = transform.InverseTransformPoint(center + up * HeadRadius * grown).y;
        }

        var leftArm = Bone(HumanBodyBones.LeftUpperArm);
        var rightArm = Bone(HumanBodyBones.RightUpperArm);
        float width = leftArm != null && rightArm != null
            ? Mathf.Clamp(Vector3.Distance(leftArm.position, rightArm.position) * .85f, .28f * grown, .46f * grown) : .36f * grown;
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
            Vector3 from = chain[i].position - (i == 0 ? up * PelvisDrop * grown : Vector3.zero);
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
        box.size = new Vector3(width, length + .04f * grown, TorsoDepth * grown);
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
        radius *= grown;
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
}
