using System.Collections.Generic;
using IronSamiStudio.ProceduralHitReactions;
using UnityEngine;

/// <summary>
/// A zombie jolts where it is hit (the user's memo of 2026-11-10, item 22): the part the blow landed on is thrown the
/// way the shot was going for a fraction of a second and comes back, on top of whatever the body is doing. The motion
/// is worked out by the asset the user bought for it, IronSami Studio's Procedural Hit Reactions: one BodyPartReactor
/// per part (torso, head, arms, legs) under one HitReactionController, timed and eased by the asset's ReactionProfile.
/// Three things are fitted around the asset so it works on our zombies:
/// - It turns a bone in the bone's own axes, which differ from rig to rig, and a character may be any humanoid a modder
///   brings. So each reactor turns a stand-in bone of ours, whose axes are laid along the blow each time (Aim), and
///   LateUpdate carries that turn over to the real bone. The asset's direction then means the same on every rig: the
///   turn a push at that point of that part would give it.
/// - It multiplies its offset into the bone every frame and counts on an Animator Controller to pose the bone afresh in
///   between. Our zombies have no controller, and their Animator stops posing off screen. So the stand-in starts every
///   frame at rest, and the real bone takes the jolt once a frame: if nothing has posed it since the last one, that
///   one comes off first (Pose), and a jolt can never pile up on itself.
/// - Its reactors run every frame, and a horde would have hundreds: they sleep until a blow wakes the ones it needs.
/// ZombieBody builds this once it is dressed and passes it every blow of a player's (ZombieBody.Jolt.cs). Every PC
/// hears of every blow with the same point, direction and part and works its own jolt out from them, with nothing
/// more on the network: the same part, thrown the same way. Where on the part the blow landed is read from that point
/// against the body where this PC has it, so on a zombie walking across the shot the bend and the twist can differ
/// from the shooter's PC (a copy trails the host's zombie), as does the degree or so of random swing the asset rolls
/// on each PC by itself.
/// It moves no agent, slows nothing and interrupts nothing (EnemyHitReaction does those, on the host). The bone
/// hitboxes ride the jolt as they ride any pose: a hard blow to the chest carries the head some 10 cm for 0.2 s.
/// Numbers: Tables/ZombieJolt (the Default row here, and a row for each part in ZombieJoltProfile).
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ZombieJolt)]
public sealed class ZombieJolt : MonoBehaviour
{
    public const string TableName = "ZombieJolt", TableRow = "Default";

    // Parents first: the torso's turn carries the head and the arms, so it goes on before theirs.
    private const int Torso = 0, Head = 1, LeftArm = 2, RightArm = 3, LeftLeg = 4, RightLeg = 5, Parts = 6;
    // What each part's objects are called in the hierarchy, and the table row its numbers come from.
    private static readonly string[] Names = { "Torso", "Head", "Left Arm", "Right Arm", "Left Leg", "Right Leg" };
    private static readonly string[] Rows = { "Torso", "Head", "Arm", "Arm", "Leg", "Leg" };
    // The part that takes a share of a blow on each part: head and torso each other's, a limb's goes to the torso.
    private static readonly int[] Next = { Head, Torso, Torso, Torso, Torso, Torso };
    // From the top of the neck to the crown, and a limb whose end the rig doesn't give (m, for a body of the pack's size).
    private const float HeadHeight = .22f, LimbLength = .6f;
    // How far off a part's middle line counts as its edge, as a share of its length.
    private const float SideReach = .4f;
    // The asset's reactors note their rest pose in Start. They get this many of our own frames awake beside us to have
    // run it before they are put to sleep, and no blow is passed to them sooner: a reaction started before Start would
    // be worked out from a rest pose of all zeroes.
    private const int WarmFrames = 3;
    // A reactor stays awake this long past the end of its reaction (s), so its last frames are never cut.
    private const float Linger = .1f;

    [SerializeField, Tooltip("Zombies jolt where they are hit.")]
    private bool react = true;
    [SerializeField, Min(.01f), Tooltip("A blow that takes this share of its health jolts it at full strength. A shotgun's pellets on one part count as one blow.")]
    private float strongShare = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("The strength of even the weakest blow.")]
    private float least = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("What a blow's strength is multiplied by on a zombie that stands firm (a brute).")]
    private float steadfast = .45f;
    [SerializeField, Range(0f, 80f), Tooltip("The most one blow turns a part (degrees).")]
    private float mostAngle = 32f;

    // One profile per table row, shared by every zombie: the table keeps them up to date, the reactors only read them.
    private static readonly Dictionary<string, ZombieJoltProfile> shared = new Dictionary<string, ZombieJoltProfile>();

    private readonly Transform[] bones = new Transform[Parts], tips = new Transform[Parts];
    private readonly Transform[] frames = new Transform[Parts], stands = new Transform[Parts];
    private readonly BodyPartReactor[] reactors = new BodyPartReactor[Parts];
    private readonly ZombieJoltProfile[] profiles = new ZombieJoltProfile[Parts];
    // Each real bone's local turn before and after its jolt went on last (Pose).
    private readonly Quaternion[] clean = new Quaternion[Parts], dirty = new Quaternion[Parts];
    private readonly float[] until = new float[Parts], force = new float[Parts], blowShare = new float[Parts];
    private readonly int[] blowFrame = new int[Parts], aimFrame = new int[Parts];
    private readonly bool[] awake = new bool[Parts], wrote = new bool[Parts];
    private HitReactionController controller;
    private Transform model;
    private int warming = WarmFrames;
    private bool warm;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => shared.Clear();

    /// <summary>
    /// Sets the asset up on a dressed zombie, under the enemy and not under its body: the body goes on to be the corpse,
    /// and its copies become dolls, and neither should carry reactors. Null if the rig lacks a spine or a head.
    /// </summary>
    internal static ZombieJolt Build(Transform enemy, Transform body, Transform[] human)
    {
        if (enemy == null || body == null || human == null || human.Length < (int)HumanBodyBones.LastBone) return null;
        if (human[(int)HumanBodyBones.Spine] == null || human[(int)HumanBodyBones.Head] == null) return null;
        var holder = new GameObject("Jolt") { layer = enemy.gameObject.layer };
        holder.transform.SetParent(enemy, false);
        var jolt = holder.AddComponent<ZombieJolt>();
        jolt.SetUp(body, human);
        return jolt;
    }

    private void SetUp(Transform body, Transform[] human)
    {
        model = body;
        GameTables.Bind(TableName, TableRow, this);
        // The asset's reactors look for their controller above them when they start. With no Animator beside it they
        // leave layer weights alone (our zombies have no Animator Controller to have layers). Blows go straight to the
        // part's reactor (Aim): the controller would find it by comparing object names, which makes garbage every blow.
        controller = gameObject.AddComponent<HitReactionController>();
        controller.reactors = new List<BodyPartReactor>(Parts);
        var neck = human[(int)HumanBodyBones.Neck];
        bones[Torso] = human[(int)HumanBodyBones.Spine];
        bones[Head] = neck != null ? neck : human[(int)HumanBodyBones.Head];
        bones[LeftArm] = human[(int)HumanBodyBones.LeftUpperArm];
        bones[RightArm] = human[(int)HumanBodyBones.RightUpperArm];
        bones[LeftLeg] = human[(int)HumanBodyBones.LeftUpperLeg];
        bones[RightLeg] = human[(int)HumanBodyBones.RightUpperLeg];
        // Where each part ends. The torso ends at the head bone, the top of the neck; the head is measured from that.
        tips[Torso] = human[(int)HumanBodyBones.Head];
        tips[LeftArm] = human[(int)HumanBodyBones.LeftHand];
        tips[RightArm] = human[(int)HumanBodyBones.RightHand];
        tips[LeftLeg] = human[(int)HumanBodyBones.LeftFoot];
        tips[RightLeg] = human[(int)HumanBodyBones.RightFoot];
        for (int i = 0; i < Parts; i++)
        {
            // The frame is turned to each blow (Aim); the stand-in under it is the "bone" the asset turns.
            var frame = new GameObject(Names[i] + " Frame") { layer = gameObject.layer }.transform;
            frame.SetParent(transform, false);
            var stand = new GameObject(Names[i]) { layer = gameObject.layer }.transform;
            stand.SetParent(frame, false);
            profiles[i] = Profile(Rows[i]);
            var reactor = stand.gameObject.AddComponent<BodyPartReactor>();
            reactor.bone = stand;
            reactor.profile = profiles[i];
            controller.reactors.Add(reactor);
            frames[i] = frame;
            stands[i] = stand;
            reactors[i] = reactor;
            blowFrame[i] = aimFrame[i] = -1;
        }
    }

    private static ZombieJoltProfile Profile(string row)
    {
        if (shared.TryGetValue(row, out var known) && known != null) return known;
        var made = ScriptableObject.CreateInstance<ZombieJoltProfile>();
        made.name = "Zombie Jolt " + row;
        made.hideFlags = HideFlags.HideAndDontSave;
        // Aim hands the asset its direction ready-made in the stand-in's own axes, so it takes it as given. A new blow
        // always takes over from the last, from a share of where that one had got to (carryOverScale).
        made.useRotation = true;
        made.useTranslation = false;
        made.additive = true;
        made.rotationMode = ReactionMode.RandomAndDirection;
        made.useContactPointDirection = false;
        made.useLocalHitDirection = false;
        made.autoInvertBackHits = false;
        made.offsetsFromCurrentPose = false;
        made.hitAcceptance = ReactionProfile.HitAcceptance.RestartTimer;
        made.blendResetMode = ReactionProfile.BlendResetMode.ScaledCarryOver;
        GameTables.Bind(TableName, row, made);
        shared[row] = made;
        return made;
    }

    /// <summary>
    /// A blow landed on the zombie while its Animator poses it (ZombieBody.Jolt, on every PC). Firm: it stands firm
    /// against blows (a brute) and barely gives. Pace: how fast this zombie's own time runs (EnemyNetwork.Pace).
    /// </summary>
    internal void Hit(in CombatDamageReport report, bool firm, float pace)
    {
        if (!react || !warm || model == null) return;
        var hit = report.Hit;
        Vector3 along = hit.Direction;
        if (along.sqrMagnitude < 1e-6f) return;
        along.Normalize();
        int part = PartOf(hit.Part);
        if (bones[part] == null) part = Torso;
        // A shotgun's pellets on one part are one blow, as heavy as all of them together.
        float share = report.Dealt / Mathf.Max(1f, report.MaximumHealth);
        if (blowFrame[part] == Time.frameCount) share += blowShare[part];
        blowFrame[part] = Time.frameCount;
        blowShare[part] = share;
        float strength = Mathf.Clamp(share / Mathf.Max(.01f, strongShare), least, 1f) * (firm ? steadfast : 1f);
        bool placed = hit.Part != BodyPart.None;
        // The asset counts in the game's time. A zombie slowed on its own (slow motion in a room) holds the jolt as much
        // longer, so the jolt doesn't run ahead of the body.
        float hold = 1f / Mathf.Clamp(pace, .1f, 1f);
        Aim(part, hit.Point, placed, true, along, strength, hold);
        // The part next to it takes a share as a blow of its own at the same point (a struck arm jerks its shoulder
        // back, a struck leg buckles the body over it), unless it is in the middle of a harder one.
        int next = Next[part];
        float passed = strength * profiles[part].follow;
        if (passed > .01f && !(awake[next] && force[next] > passed)) Aim(next, hit.Point, placed, false, along, passed, hold);
    }

    private static int PartOf(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.Head: return Head;
            case BodyPart.LeftUpperArm: case BodyPart.LeftLowerArm: return LeftArm;
            case BodyPart.RightUpperArm: case BodyPart.RightLowerArm: return RightArm;
            case BodyPart.LeftUpperLeg: case BodyPart.LeftLowerLeg: return LeftLeg;
            case BodyPart.RightUpperLeg: case BodyPart.RightLowerLeg: return RightLeg;
            default: return Torso;
        }
    }

    /// <summary>
    /// Starts the part's reactor on the turn a push at that point would give the part: about its joint, further the
    /// further along the part the blow landed, with a twist if it landed off to one side. Own: the blow landed on this
    /// part itself, not on the part next to it.
    /// </summary>
    private void Aim(int part, Vector3 point, bool placed, bool own, Vector3 along, float strength, float hold)
    {
        var bone = bones[part];
        var profile = profiles[part];
        var reactor = reactors[part];
        if (bone == null || profile == null || reactor == null || strength <= 0f) return;
        Vector3 joint = bone.position;
        Measure(part, joint, out Vector3 length, out float reach);
        Vector3 arm = point - joint;
        // A blow with no place of its own (a fist, a kick), or one on this part whose point is off it (a copy's body
        // lags the PC that judged the hit), lands on its middle. A blow on the part next to it is meant to be off this
        // one: its real leverage is kept, so a shot low in the belly and one in the chest pull the head opposite ways.
        if (!placed || (own && arm.sqrMagnitude > reach * reach * 2.25f)) arm = length * (reach * .5f);
        float far = Vector3.Dot(arm, length);
        Vector3 aside = arm - length * far;
        // Turning about the lever carries the part's far end along the blow; turning about its length twists it. Both
        // are what the blow's own leverage about the joint would do (arm x along), in degrees from the table.
        Vector3 lever = Vector3.Cross(length, along);
        float bent = profile.push + profile.bend * Mathf.Clamp(far / reach, -1f, 1f);
        float twisted = profile.twist * Mathf.Clamp(Vector3.Dot(Vector3.Cross(aside, along), length) / (reach * SideReach), -1f, 1f);
        Vector3 turn = lever * bent + length * twisted;
        // The asset scales its random swing, its blend-in speed and the direction's share of the mix by the reactor's
        // intensity. Half of the strength (its root) goes there and the other half onto the angle: a blow half as
        // strong then turns the part half as far, and still snaps instead of easing in at half speed.
        float size = turn.magnitude, drive = Mathf.Sqrt(strength), angle = Mathf.Min(size, mostAngle) * drive;
        Vector3 about = size > .01f ? turn / size : lever.sqrMagnitude > 1e-6f ? lever.normalized : model.right;
        // The stand-in's axes for this blow: x is the axis to turn about. The asset turns its bone to "look along" the
        // direction it is given, which about x is a plain pitch, so the direction below asks for exactly this angle
        // whatever way the real bone's own axes point.
        Vector3 square = Mathf.Abs(Vector3.Dot(about, Vector3.up)) < .9f ? Vector3.up : Vector3.forward;
        Vector3 ahead = Vector3.Cross(about, square).normalized;
        frames[part].rotation = Quaternion.LookRotation(ahead, Vector3.Cross(ahead, about));
        stands[part].localRotation = Quaternion.identity;
        float radians = angle * Mathf.Deg2Rad;
        reactor.intensityMultiplier = drive;
        reactor.durationMultiplier = hold;
        reactor.enabled = true;
        // Every React takes the reaction under way down to carryOverScale of where it had got to. A shotgun's pellets
        // on one part are one blow aimed again and again in one frame: only the first of them may take it down, or a
        // shell landing in the middle of a jolt would drop it to nothing for a frame (0.5 to the power of the pellets).
        // The profile is every zombie's, so its number is put back at once.
        bool again = aimFrame[part] == Time.frameCount;
        aimFrame[part] = Time.frameCount;
        float carry = profile.carryOverScale;
        if (again) profile.carryOverScale = 1f;
        reactor.React(new Vector3(0f, -Mathf.Sin(radians), Mathf.Cos(radians)), point);
        profile.carryOverScale = carry;
        until[part] = Time.time + profile.reactionDuration * reactor.durationMultiplier
            + 1f / Mathf.Max(.5f, profile.blendOutSpeed) + Linger;
        force[part] = strength;
        awake[part] = true;
        enabled = true;
    }

    // The part as it is posed now: which way it runs from its joint (a unit vector) and how long it is (m).
    private void Measure(int part, Vector3 joint, out Vector3 length, out float reach)
    {
        float size = Mathf.Max(.01f, Mathf.Abs(model.lossyScale.y));
        Vector3 span;
        if (part == Head)
        {
            // Up the neck, the way the head sits on the torso, as far as the crown.
            span = tips[Torso].position - bones[Torso].position;
            reach = Vector3.Distance(joint, tips[Torso].position) + HeadHeight * size;
        }
        else if (tips[part] != null)
        {
            span = tips[part].position - joint;
            reach = span.magnitude;
        }
        else
        {
            span = -model.up;
            reach = LimbLength * size;
        }
        if (span.sqrMagnitude < 1e-6f) span = part == Torso || part == Head ? model.up : -model.up;
        length = span.normalized;
        reach = Mathf.Max(reach, .05f * size);
    }

    // After the zombie's own pose work and after the reactors have turned their stand-ins (both at 0, FrameOrder).
    private void LateUpdate()
    {
        if (!warm)
        {
            if (--warming > 0) return;
            warm = true;
            for (int i = 0; i < Parts; i++) if (reactors[i] != null) reactors[i].enabled = false;
        }
        bool busy = false;
        for (int i = 0; i < Parts; i++)
        {
            bool turned = awake[i];
            Quaternion turn = Quaternion.identity;
            if (turned)
            {
                // The stand-in's turn in its frame, as a turn in the world, and the stand-in back at rest for next frame.
                Quaternion frame = frames[i].rotation;
                turn = frame * stands[i].localRotation * Quaternion.Inverse(frame);
                stands[i].localRotation = Quaternion.identity;
                if (Time.time >= until[i])
                {
                    reactors[i].enabled = false;
                    awake[i] = false;
                    force[i] = 0f;
                }
            }
            Pose(i, turned, turn);
            busy |= awake[i] || wrote[i];
        }
        if (!busy) enabled = false;
    }

    // Puts this frame's jolt on the real bone, about the bone's own joint.
    private void Pose(int part, bool turned, Quaternion turn)
    {
        var bone = bones[part];
        if (bone == null) { wrote[part] = false; return; }
        // Still exactly as the last jolt left it: nothing posed it since (the Animator is off screen, or switched off).
        // That jolt comes off first, so the new one goes onto the same pose and not onto the old jolt.
        if (wrote[part] && Same(bone.localRotation, dirty[part])) bone.localRotation = clean[part];
        wrote[part] = false;
        // No turn to speak of (the reaction has eased out): the bone is left exactly as it was posed.
        if (!turned || turn == Quaternion.identity) return;
        clean[part] = bone.localRotation;
        bone.rotation = turn * bone.rotation;
        dirty[part] = bone.localRotation;
        wrote[part] = true;
    }

    // To the last bit: Quaternion's own == calls nearly equal turns equal, and a freshly posed bone can be that near.
    private static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    /// <summary>
    /// The body stops being the Animator's (it goes down on the doll, or dies): the jolt it is in stays in the pose the
    /// doll starts from, and nothing more is added.
    /// </summary>
    internal void Stop()
    {
        for (int i = 0; i < Parts; i++)
        {
            // Still warming up, the reactors are left to start; they have nothing to play yet.
            if (warm && reactors[i] != null) reactors[i].enabled = false;
            if (stands[i] != null) stands[i].localRotation = Quaternion.identity;
            awake[i] = wrote[i] = false;
            force[i] = 0f;
        }
        if (warm) enabled = false;
    }
}
