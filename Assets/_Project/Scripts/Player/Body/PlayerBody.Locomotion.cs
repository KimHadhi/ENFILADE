using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Ground locomotion, from walk and run cycles each measured on this body as it is made (GaitClips: how fast its feet carry
/// the body, which way, where the left foot comes down), so it fits any character and any cycle without numbers per clip:
/// - Each way of travel (ahead, right, back, left) lists its cycles; the speed blends the two either side of it by their
///   measured speeds. The two ways either side of the heading share the weight so the blended feet push straight back
///   along the travel: a slow side cycle takes more of a diagonal than a fast forward one, so neither skates sideways.
/// - Every cycle shares one step: each stride starts with the left foot down, wherever its file starts or however many
///   strides it holds.
/// - The pace follows a person's (gaitCadence by speed, for this body's leg) within gaitRate of the cycle's own, and the
///   stride is stretched for the rest (gaitStretch; FootPlanting.Stride), so the feet cover the ground the body covers and
///   stay where they come down. Until 0.8.260 the cycles played at their own pace whatever the speed and the feet skated
///   30-60% of it, with short quick steps (the user, 2026-11-12: "우스꽝스럽고 초보 같고 급하던데").
/// - Running fast the hips turn toward the way of travel (hipTurn, from hipTurnSpeeds), as a person running sideways or
///   back at a slant does, and the spine turns the chest back to the view (SpineLook, or FPSAF's Look with a gun), so the
///   legs run a forward or back cycle instead of side-stepping at 5 m/s.
/// - With a gun out the idle is the gun's stance; aiming, the gun's aim cycles take over as far as they reach the speed.
/// - Crouched, the crouch cycles, facing the view.
/// Every PC works this out from the same speed and step (PlayerBody.Pace), so teammates see the same legs.
/// </summary>
public sealed partial class PlayerBody
{
    // The cycles of every stance are rows of Tables/Player/BodyMoves.csv (MoveSets), not fields: a set lists its cycles in
    // any order and the body measures which way and how fast each one goes (GaitClips).

    [Header("Stride")]
    [SerializeField, Tooltip("A person's steps a second by speed for a leg of gaitLeg: pairs of speed (m/s); steps a second.")]
    private float[] gaitCadence = { 0f, 1.5f, 1.4f, 1.9f, 2.2f, 2.3f, 2.6f, 2.6f, 3.5f, 2.75f, 5f, 2.95f, 7f, 3.3f, 9f, 3.85f };
    [SerializeField, Min(.3f), Tooltip("The leg (hip joint to ankle, m) gaitCadence is for: a shorter one steps faster at a speed.")]
    private float gaitLeg = .9f;
    [SerializeField, Tooltip("How much slower (x) and faster (y) than its own pace a cycle may play.")]
    private Vector2 gaitRate = new Vector2(.85f, 1.2f);
    [SerializeField, Tooltip("How much shorter (x) and longer (y) than its own a cycle's stride may be stretched.")]
    private Vector2 gaitStretch = new Vector2(.8f, 1.3f);
    [SerializeField, Range(0f, .3f), Tooltip("The most the hips come down for a stretched stride's leg to reach, as a share of the leg.")]
    private float gaitStretchDrop = .08f;
    [SerializeField, Tooltip("Running, the most the hips turn toward the way of travel (degrees): x heading ahead or to a side, y heading back.")]
    private Vector2 hipTurn = new Vector2(70f, 45f);
    [SerializeField, Tooltip("The hips start turning toward the way of travel at x and turn all the way from y (m/s).")]
    private Vector2 hipTurnSpeeds = new Vector2(3f, 5f);

    [Header("Turning on the spot")]
    [SerializeField] private string turnLeft = "mx_turn_left", turnRight = "mx_turn_right";
    [SerializeField, Range(10f, 120f), Tooltip("Standing still, the legs stay planted until the view has turned this far "
        + "from them (degrees); the chest twists toward the view meanwhile. Then the legs step round.")]
    private float turnStartAngle = 60f;
    [SerializeField, Min(10f), Tooltip("How fast the legs turn round to the view (degrees per second).")]
    private float turnSpeed = 150f;
    // Off by default (user, 2026-11-09): the clips' own turn is cut out (inPlaceYaw) and the body turns the legs itself,
    // but a clip's legs still swing as though its hips had turned, by the clip's angle over its 0.8 s, while the body
    // turns about 60 degrees at turnSpeed with the hips kept on the standing clip. So mid-step the legs swung aside, and
    // snapped back when the clip let go.
    [SerializeField, Tooltip("Step round with the turn clips on the legs. Off, the legs turn on the spot as they stand.")]
    private bool turnSteps;
    [SerializeField, Min(0f), Tooltip("Turning on the spot without steps, how softly the legs start and stop (s).")]
    private float turnEaseTime = .08f;
    [SerializeField, Min(.1f), Tooltip("Playback speed of the turn clips while the legs step round.")]
    private float turnClipSpeed = 1.5f;
    [SerializeField, Min(0f), Tooltip("How quickly the legs follow a change in the way and speed of travel (s). Quick A/D or "
        + "W taps even out instead of flipping the stride from one side to the other.")]
    private float strideFollowTime = .12f;

    // The table of the stances' cycles (Tables/Player/BodyMoves.csv): a row a set, its idle and its cycles. "plain" and
    // "crouch" stand on Body.idle and Body.crouchIdle; a gun's sets are "<kind>", "<kind>.aim", "<kind>.crouch" and
    // "<kind>.crouch.aim", and the kind row's ammo cell names the ammunition (Weapons.csv ammo) that takes them, "*" for any
    // the other kinds don't name.
    private const string MovesTable = "BodyMoves", PlainSetId = "plain", CrouchSetId = "crouch", AnyAmmunition = "*";
    // Cycles measured within this many degrees of each other go the same way: one way's walk, jog and run.
    private const float SameWay = 15f;
    // Two measured ways this far apart or less blend into every heading between them (a four-way set's are 90 degrees apart).
    private const float WaysJoin = 120f;
    // A way with no neighbour that near carries a heading within half an eight-way set's step of it, and none from a whole
    // step off: further round, its feet would push the body sideways off the travel, which the other set's cycles carry.
    private const float LoneWayReach = 22.5f, LoneWayEnd = 45f;

    // One way of travel in a set: where its cycles' feet carry the body (degrees, 0 ahead, 90 right), slowest cycle first.
    private sealed class Way
    {
        public float Angle;
        public readonly List<Channel> Cycles = new List<Channel>();
    }

    // A stance and its cycles, grouped by the way each was measured to go on this body, by angle round from ahead.
    private sealed class MoveSet
    {
        public Channel Idle;
        public readonly List<Way> Ways = new List<Way>();
    }

    // A gun kind's sets: held at the hip, aimed, and both crouched; any may be missing (null), and what a set can't carry
    // goes to the next set down, the plain or crouch set last.
    private sealed class GunSets
    {
        public MoveSet Hip, Aim, Crouch, CrouchAim;
    }

    private MoveSet plainSet, crouchSet;
    // The gun kinds' sets by kind, and the kind each ammunition takes (AnyAmmunition for the rest).
    private readonly Dictionary<string, GunSets> gunSets = new Dictionary<string, GunSets>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> kindOfAmmunition = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    // This frame's sets that may have the legs, first asked first, how much each wants, and what each was given.
    private readonly List<MoveSet> asking = new List<MoveSet>(4);
    private readonly List<float> wanting = new List<float>(4);
    private float legYaw, turnVelocity;
    private Vector3 stride;
    private int turning;
    private bool legsPlanted, runsBack;
    // This body's leg, hip joint to ankle (m), for the person's cadence (gaitCadence).
    private float legLength = .9f;
    // This frame's blend of cycles: their weight, weight by stride time (s), and weight by the velocity their feet carry the
    // body (m/s, in the body's own frame: x right, y ahead).
    private float mixWeight, mixStride;
    private Vector2 mixVelocity;
    // What the feet stretch the stride by (PlantFeet), the frame it was worked out on (a landing or a jump plays no cycles and
    // stretches nothing), and the numbers behind it for the /gait readout.
    private FootPlanting.Stride strideStretch;
    private int strideFrame = -1;
    private float gaitSpeed, gaitNatural, gaitRateNow = 1f, gaitStretchNow = 1f;
    // How much of the body was on its cycles this frame rather than standing, for the run's calm camera (PlayerBody.Look).
    private float runCalmShare;

    /// <summary>This frame's stride stretch for the feet, none unless the walk and run cycles played this frame.</summary>
    private FootPlanting.Stride StrideNow => strideFrame == Time.frameCount ? strideStretch : FootPlanting.Stride.None;

    private void AddLocomotionChannels()
    {
        legLength = LegOf(animator);
        plainSet = SetFromTable(PlainSetId, idle) ?? new MoveSet();
        crouchSet = SetFromTable(CrouchSetId, crouchIdle) ?? new MoveSet();
        gunSets.Clear();
        kindOfAmmunition.Clear();
        foreach (string id in GameTables.RowIds(MovesTable))
        {
            if (id.IndexOf('.') >= 0 || id.StartsWith("#", System.StringComparison.Ordinal)
                || string.Equals(id, PlainSetId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, CrouchSetId, System.StringComparison.OrdinalIgnoreCase)) continue;
            gunSets[id] = new GunSets
            {
                Hip = SetFromTable(id, null), Aim = SetFromTable(id + ".aim", null),
                Crouch = SetFromTable(id + ".crouch", null), CrouchAim = SetFromTable(id + ".crouch.aim", null)
            };
            if (GameTables.TryGetRow(MovesTable, id, out var row))
                foreach (string ammunition in row.Text("ammo").Split(new[] { ';', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                    kindOfAmmunition[ammunition] = id;
        }
        turning = 0;
        strideCount = 0;
        strideStretch = FootPlanting.Stride.None;
    }

    private static float LegOf(Animator animator)
    {
        var upper = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : null;
        var lower = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg) : null;
        var foot = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        if (upper == null || lower == null || foot == null) return .9f;
        float leg = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position);
        return leg > .2f ? leg : .9f;
    }

    // A set as its table row has it: its idle (the row's, else the stance's own), and its cycles grouped by the way each
    // one's feet were measured to carry this body. Null for a row that isn't there or holds nothing this body can play.
    private MoveSet SetFromTable(string id, string stanceIdle)
    {
        if (!GameTables.TryGetRow(MovesTable, id, out var row)) return null;
        var set = new MoveSet();
        string still = row.Text("idle");
        if (string.IsNullOrEmpty(still)) still = stanceIdle;
        if (!string.IsNullOrEmpty(still))
        {
            AddChannel(still, 0f);
            byName.TryGetValue(still, out set.Idle);
        }
        foreach (string clip in row.Text("moves").Split(new[] { ';', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var cycle = AddCycle(clip);
            if (cycle == null) continue;
            if (cycle.Gait == null || !cycle.Gait.Moves)
            {
                Report($"'{clip}' was listed as a walk or run but its feet don't carry the body (GaitClips): left out.");
                continue;
            }
            float angle = Mathf.Repeat(Mathf.Atan2(cycle.Gait.Way.x, cycle.Gait.Way.y) * Mathf.Rad2Deg, 360f);
            Way way = null;
            foreach (var known in set.Ways)
                if (Mathf.Abs(Mathf.DeltaAngle(known.Angle, angle)) <= SameWay) { way = known; break; }
            if (way == null)
            {
                way = new Way { Angle = angle };
                set.Ways.Add(way);
            }
            if (!way.Cycles.Contains(cycle)) way.Cycles.Add(cycle);
        }
        foreach (var way in set.Ways)
        {
            way.Cycles.Sort((a, b) => a.Gait.Speed.CompareTo(b.Gait.Speed));
            // The way is where its cycles go together: the mean of their measured ways.
            Vector2 sum = Vector2.zero;
            foreach (var cycle in way.Cycles) sum += cycle.Gait.Way;
            if (sum.sqrMagnitude > 1e-6f) way.Angle = Mathf.Repeat(Mathf.Atan2(sum.x, sum.y) * Mathf.Rad2Deg, 360f);
        }
        set.Ways.Sort((a, b) => a.Angle.CompareTo(b.Angle));
        return set.Idle == null && set.Ways.Count == 0 ? null : set;
    }

    // A cycle polished in the Blender run workspace (ArtSource/Run, "게임에 보내기") comes into the profile as "enf_" and the
    // cycle's name, on Kazusa's avatar; where it is there it plays instead of the cycle the table lists, so one export changes
    // the game and deleting it changes it back.
    private const string PolishedPrefix = "enf_";

    // A walk or run cycle: on the shared step, measured on this body.
    private Channel AddCycle(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        if (!clipName.StartsWith(PolishedPrefix, System.StringComparison.OrdinalIgnoreCase)
            && profile.Find(PolishedPrefix + clipName) != null)
            clipName = PolishedPrefix + clipName;
        AddChannel(clipName, 1f);
        if (!byName.TryGetValue(clipName, out var channel) || !channel.Synced) return null;
        if (channel.Gait == null && channel.Playable.IsValid())
            channel.Gait = GaitClips.For(loadedModel, channel.Playable.GetAnimationClip(), animator);
        return channel;
    }

    // The turn clips play on the legs alone (TurnLayer, the kick's leg mask): the hips, spine and head stay on the standing
    // clip, so the steps don't rock the head, and with it the camera on the eyes (item 312). The hips' turn comes from the
    // body's own yaw (targetYaw), not the clip.
    private AnimationMixerPlayable turnMixer;
    private AnimationClipPlayable turnLeftClip, turnRightClip;
    private float turnLayerWeight, turnLayerTarget;

    // From BuildLayers, after BuildKick has made the leg mask.
    private void BuildTurn()
    {
        layers.SetLayerMaskFromAvatarMask(TurnLayer, legMask);
        turnMixer = AnimationMixerPlayable.Create(graph, 2);
        graph.Connect(turnMixer, 0, layers, TurnLayer);
        turnLeftClip = TurnClip(turnLeft, 0);
        turnRightClip = TurnClip(turnRight, 1);
        turnLayerWeight = turnLayerTarget = 0f;
        layers.SetInputWeight(TurnLayer, 0f);
    }

    private AnimationClipPlayable TurnClip(string clipName, int input)
    {
        if (string.IsNullOrEmpty(clipName)) return default;
        var clip = profile.Find(clipName);
        if (clip == null)
        {
            Report($"clip '{clipName}' is not in the PlayerBody profile.");
            return default;
        }
        var playable = ClipPlayable(clip);
        playable.SetSpeed(0);
        graph.Connect(playable, 0, turnMixer, input);
        return playable;
    }

    private void TeardownTurn()
    {
        turnMixer = default;
        turnLeftClip = turnRightClip = default;
        turnLayerWeight = turnLayerTarget = 0f;
    }

    // From BlendLayers: the legs step round while TurnOnTheSpot asked for it this frame, and ease back otherwise.
    private void BlendTurn(float dt)
    {
        if (!turnMixer.IsValid()) return;
        float target = dead ? 0f : turnLayerTarget;
        turnLayerTarget = 0f;
        turnLayerWeight = Mathf.MoveTowards(turnLayerWeight, target, dt / blendTime);
        layers.SetInputWeight(TurnLayer, turnLayerWeight);
    }

    // The gun kind's sets in the hands now, by the ammunition the gun takes (BodyMoves.csv ammo; "*" for the rest), or
    // null with no gun out. A friend's copy has the same gun out, so the same sets.
    private GunSets GunInHand()
    {
        if (weapons == null) weapons = player.GetComponent<WeaponController>();
        var gun = weapons != null && weapons.ShowsWeapon ? weapons.EquippedData : null;
        if (gun == null || gunSets.Count == 0) return null;
        if (!kindOfAmmunition.TryGetValue(gun.Ammunition, out string kind) && !kindOfAmmunition.TryGetValue(AnyAmmunition, out kind))
            return null;
        return gunSets.TryGetValue(kind, out var sets) ? sets : null;
    }

    // The cycles are measured in the Animator's own units; its scale (the fit to the camera) makes them metres.
    private float GaitScale => animator != null ? Mathf.Abs(animator.transform.lossyScale.y) : 1f;

    private void Locomotion(bool crouched, float dt)
    {
        // Followed in the world, so turning the view doesn't drag the legs behind it.
        Vector3 travel = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
        stride = Vector3.Lerp(stride, travel, strideFollowTime > 0f ? 1f - Mathf.Exp(-dt / strideFollowTime) : 1f);
        Vector3 local = transform.InverseTransformDirection(stride);
        local.y = 0f;
        float speed = local.magnitude;
        float heading = speed > StillTravel ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
        float scale = GaitScale;
        mixWeight = mixStride = 0f;
        mixVelocity = Vector2.zero;

        // A gun out asks for its aimed set as far as the sights are up and for its own set otherwise; what they can't
        // carry the stance's own set (plain or crouched) does.
        var gun = GunInHand();
        float aim = gun != null ? AimBlend() : 0f;
        asking.Clear();
        wanting.Clear();
        float legs = heading, turn = 0f;
        MoveSet stance;
        if (crouched)
        {
            stance = crouchSet;
            if (gun != null) { Ask(gun.CrouchAim, aim); Ask(gun.Crouch, 1f); }
        }
        else
        {
            stance = plainSet;
            turn = HipTurn(heading, speed);
            legs = Mathf.DeltaAngle(0f, heading - turn);
            if (gun != null) { Ask(gun.Aim, aim); Ask(gun.Hip, 1f); }
        }
        float moving = MixSets(stance, legs, speed, scale);
        if (!crouched)
        {
            if (TurnOnTheSpot(speed < StillTravel, dt)) turnLayerTarget = 1f - moving;
            if (!legsPlanted) targetYaw = turn;
        }
        Pace(speed, legs, moving, dt);
    }

    // Below this the travel has no heading: the body stands (m/s).
    private const float StillTravel = .3f;

    private void Ask(MoveSet set, float want)
    {
        if (set == null || want <= 0f) return;
        asking.Add(set);
        wanting.Add(want);
    }

    // Shares the legs among the sets asking for them, first asked first: each takes what it wants of what is left, as far
    // as its cycles can carry this travel (Carries); the stance's own set has the rest. Before 0.9.240 a gun's set was its
    // aim cycles alone, taken only while aiming (or by Body.gunHipMoves), and a set missing any of four ways fell back
    // whole to the plain one. How much of the body moves rather than stands comes back.
    private float MixSets(MoveSet stance, float heading, float speed, float scale)
    {
        float left = 1f, moving = 0f;
        for (int i = 0; i < asking.Count && left > 1e-4f; i++)
        {
            float share = left * Mathf.Clamp01(wanting[i]) * Carries(asking[i], heading, speed, scale);
            if (share <= 0f) continue;
            moving += Mix(asking[i], stance, share, heading, speed, scale);
            left -= share;
        }
        if (left > 1e-4f) moving += Mix(stance, stance, left, heading, speed, scale);
        return moving;
    }

    // A set's share: on its idle as far as the body stands (the stance's idle for a set with none of its own), on its
    // cycles for the rest. Returns the moving part of the share.
    private float Mix(MoveSet set, MoveSet stance, float share, float heading, float speed, float scale)
    {
        float moving = share * MovingShare(set, heading, speed, scale);
        MixIdle(set.Idle != null ? set : stance, share - moving);
        Ways(set, heading, speed, moving, scale);
        return moving;
    }

    private void MixIdle(MoveSet set, float weight)
    {
        if (set.Idle != null && weight > 0f) set.Idle.Target += weight;
    }

    // How much of this travel a set can carry (0-1): standing, all of it (on its idle); moving, as far as its ways reach
    // round to the heading (Reach) and its fastest cycles there reach the speed, played as fast and stretched as long as
    // they may. Past that the next set down runs on under the same upper body.
    private float Carries(MoveSet set, float heading, float speed, float scale)
    {
        if (speed < StillTravel) return 1f;
        if (!Pair(set, heading, out var a, out var b, out float toward)) return 0f;
        float top = Mathf.Lerp(a.Cycles[a.Cycles.Count - 1].Gait.Speed, b.Cycles[b.Cycles.Count - 1].Gait.Speed, toward)
            * scale * gaitRate.y * gaitStretch.y;
        return Reach(set, heading) * (1f - Mathf.InverseLerp(top * .85f, top * 1.1f, speed));
    }

    // Running fast the hips turn toward the way of travel: ahead up to past the side, back beyond, with a margin either way
    // so a heading near the line doesn't flip from one to the other. Slow, the side and back cycles step facing the view.
    private const float BackFrom = 112f, BackMargin = 10f;
    private float HipTurn(float heading, float speed)
    {
        float share = Mathf.InverseLerp(hipTurnSpeeds.x, Mathf.Max(hipTurnSpeeds.x + .01f, hipTurnSpeeds.y), speed);
        float away = Mathf.Abs(heading);
        if (runsBack ? away < BackFrom - BackMargin : away > BackFrom + BackMargin) runsBack = !runsBack;
        if (share <= 0f) return 0f;
        float turn = runsBack ? Mathf.Clamp(Mathf.DeltaAngle(180f, heading), -hipTurn.y, hipTurn.y)
            : Mathf.Clamp(heading, -hipTurn.x, hipTurn.x);
        return turn * share;
    }

    // How much of the body moves rather than stands: below the slowest the way's slowest cycle can go (at its slowest pace
    // and shortest stride, gaitRate.x x gaitStretch.x), the stance shares it. Measured against the cycle's own speed, a
    // creep under a crouched walk's 2.3 m/s stood a third of the way on the crouch idle, feet planted in mid-stride.
    private float MovingShare(MoveSet set, float heading, float speed, float scale)
    {
        if (!Pair(set, heading, out var a, out var b, out float toward)) return 0f;
        float slow = Mathf.Lerp(a.Cycles[0].Gait.Speed, b.Cycles[0].Gait.Speed, toward) * scale
            * Mathf.Min(gaitRate.x, gaitRate.y) * Mathf.Min(gaitStretch.x, gaitStretch.y);
        return Mathf.Clamp01(speed / Mathf.Max(.1f, slow));
    }

    // The two ways of a set either side of a heading (0 ahead, 90 right), round the circle: how far the heading is from the
    // first toward the second (0-1) and how far apart they are (degrees). A set of one way has it as both.
    private static bool Around(MoveSet set, float heading, out Way from, out Way to, out float toward, out float gap)
    {
        from = to = null;
        toward = 0f;
        gap = 360f;
        int count = set.Ways.Count;
        if (count == 0) return false;
        from = to = set.Ways[0];
        if (count == 1) return true;
        float at = Mathf.Repeat(heading, 360f);
        for (int i = 0; i < count; i++)
        {
            Way a = set.Ways[i], b = set.Ways[(i + 1) % count];
            float span = Mathf.Repeat(b.Angle - a.Angle, 360f), off = Mathf.Repeat(at - a.Angle, 360f);
            if (off > span) continue;
            from = a;
            to = b;
            gap = span;
            toward = span > 1e-3f ? off / span : 0f;
            return true;
        }
        return true;
    }

    // The ways a heading blends: the two either side while they join (WaysJoin), else the nearer one alone.
    private static bool Pair(MoveSet set, float heading, out Way from, out Way to, out float toward)
    {
        if (!Around(set, heading, out from, out to, out toward, out float gap)) return false;
        if (from != to && gap > WaysJoin)
        {
            if (toward > .5f) from = to;
            else to = from;
            toward = 0f;
        }
        return true;
    }

    // How far a set's ways reach round to a heading (0-1): all the way between two that join; within LoneWayReach of a way
    // with no joining neighbour on that side, none from LoneWayEnd.
    private static float Reach(MoveSet set, float heading)
    {
        if (!Around(set, heading, out var a, out var b, out float toward, out float gap)) return 0f;
        if (a != b && gap <= WaysJoin) return 1f;
        float off = a == b ? Mathf.Abs(Mathf.DeltaAngle(a.Angle, heading)) : Mathf.Min(toward, 1f - toward) * gap;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LoneWayReach, LoneWayEnd, off));
    }

    // Shares the weight between the two ways either side of the heading so their blended feet push back along it: each
    // way's cycles at this speed carry the body their own way at their own speed, and the share is the one whose sum of the
    // two points down the heading. Shared by angle alone, a fast forward run and a slow side step blended into a stride
    // that pointed well off the travel, and the feet slid sideways.
    private void Ways(MoveSet set, float heading, float speed, float weight, float scale)
    {
        if (weight <= 0f || !Pair(set, heading, out var a, out var b, out float toward)) return;
        if (a == b)
        {
            MixLadder(a.Cycles, speed, weight, scale);
            return;
        }
        float share = toward;
        if (toward > 1e-4f && toward < 1f - 1e-4f)
        {
            float rad = heading * Mathf.Deg2Rad;
            var way = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 first = LadderVelocity(a.Cycles, speed, scale), second = LadderVelocity(b.Cycles, speed, scale);
            float x = Cross(first, way), y = Cross(second, way);
            if (Mathf.Abs(x - y) > 1e-4f) share = Mathf.Clamp01(x / (x - y));
        }
        MixLadder(a.Cycles, speed, weight * (1f - share), scale);
        MixLadder(b.Cycles, speed, weight * share, scale);

        static float Cross(Vector2 p, Vector2 q) => p.x * q.y - p.y * q.x;
    }

    // The two cycles either side of the speed by their measured speeds; below the slowest or above the fastest, that one.
    // Named MixLadder, not Ladder: inside PlayerBody a member called Ladder hides the Ladder fixture type (PlayerBody.LadderFeet).
    private void MixLadder(List<Channel> cycles, float speed, float weight, float scale)
    {
        if (weight <= 0f || cycles.Count == 0) return;
        Bracket(cycles, speed, scale, out int low, out int high, out float t);
        MixCycle(cycles[low], weight * (1f - t), scale);
        if (high != low) MixCycle(cycles[high], weight * t, scale);
    }

    private static Vector2 LadderVelocity(List<Channel> cycles, float speed, float scale)
    {
        Bracket(cycles, speed, scale, out int low, out int high, out float t);
        return Velocity(cycles[low], scale) * (1f - t) + Velocity(cycles[high], scale) * t;
    }

    private static void Bracket(List<Channel> cycles, float speed, float scale, out int low, out int high, out float t)
    {
        low = high = 0;
        t = 0f;
        int last = cycles.Count - 1;
        if (last <= 0 || speed <= cycles[0].Gait.Speed * scale) return;
        if (speed >= cycles[last].Gait.Speed * scale) { low = high = last; return; }
        for (int i = 0; i < last; i++)
        {
            float a = cycles[i].Gait.Speed * scale, b = cycles[i + 1].Gait.Speed * scale;
            if (speed > b) continue;
            low = i;
            high = i + 1;
            t = b - a > 1e-3f ? (speed - a) / (b - a) : 1f;
            return;
        }
    }

    private static Vector2 Velocity(Channel cycle, float scale) => cycle.Gait.Way * (cycle.Gait.Speed * scale);

    private void MixCycle(Channel cycle, float weight, float scale)
    {
        if (weight <= 0f) return;
        cycle.Target += weight;
        mixWeight += weight;
        mixStride += weight * cycle.Gait.Stride;
        mixVelocity += weight * Velocity(cycle, scale);
    }

    // The step's pace and the stride's stretch. The blended cycles carry the body at their own speed along the travel and
    // step at their own pace; a person at this speed steps at gaitCadence's pace (for this leg), so the cycles play that much
    // faster or slower, within gaitRate, and the stride is stretched by what is left, within gaitStretch. If the stretch runs
    // out the pace makes up the rest, within gaitRate again; anything past that the feet slide.
    private void Pace(float speed, float legs, float moving, float dt)
    {
        if (player.IsNetworkDriven) FollowStep(dt);
        gaitSpeed = speed;
        runCalmShare = moving;
        if (mixWeight <= 0f)
        {
            strideStretch = FootPlanting.Stride.None;
            gaitNatural = 0f;
            gaitRateNow = gaitStretchNow = 1f;
            return;
        }
        float strideTime = Mathf.Max(.05f, mixStride / mixWeight);
        float rad = legs * Mathf.Deg2Rad;
        float along = Vector2.Dot(mixVelocity / mixWeight, new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)));
        float rate = 1f, stretch = 1f;
        if (along > .05f && speed > .05f)
        {
            float lo = Mathf.Min(gaitRate.x, gaitRate.y), hi = Mathf.Max(gaitRate.x, gaitRate.y);
            float shortest = Mathf.Min(gaitStretch.x, gaitStretch.y), longest = Mathf.Max(gaitStretch.x, gaitStretch.y);
            rate = Mathf.Clamp(CadenceFor(speed) * strideTime * .5f, lo, hi);
            stretch = speed / (rate * along);
            if (stretch > longest || stretch < shortest)
            {
                stretch = Mathf.Clamp(stretch, shortest, longest);
                rate = Mathf.Clamp(speed / (stretch * along), lo, hi);
            }
        }
        phase += dt * rate / strideTime;
        if (phase >= 1f)
        {
            strideCount += Mathf.FloorToInt(phase);
            phase -= Mathf.Floor(phase);
        }
        gaitNatural = along;
        gaitRateNow = rate;
        gaitStretchNow = stretch;
        // Not while a layer has the legs (a reload kick, the steps of a turn): their feet aren't the cycles' stride.
        float legsOwn = (1f - kickWeight) * (1f - turnLayerWeight);
        strideStretch = new FootPlanting.Stride(stride, stretch, moving * legsOwn, gaitStretchDrop * legLength);
        strideFrame = Time.frameCount;
    }

    // A person's steps a second at this speed (gaitCadence), for this body's leg: alike in build, a shorter leg at the same
    // speed runs as a longer one does at a speed higher by the root of their ratio, and steps that much faster.
    private float CadenceFor(float speed)
    {
        float like = Mathf.Sqrt(Mathf.Max(.3f, gaitLeg) / Mathf.Max(.2f, legLength));
        float at = speed * like;
        var points = gaitCadence;
        if (points == null || points.Length < 2) return 2.8f * like;
        float steps = points[1];
        for (int i = 0; i + 3 < points.Length; i += 2)
        {
            if (at <= points[i]) { steps = points[i + 1]; break; }
            steps = points[i + 3];
            if (at <= points[i + 2])
            {
                float t = points[i + 2] - points[i] > 1e-4f ? (at - points[i]) / (points[i + 2] - points[i]) : 1f;
                steps = Mathf.Lerp(points[i + 1], points[i + 3], t);
                break;
            }
        }
        return steps * like;
    }

    /// <summary>This frame's run for the /gait averages: the speed (m/s), steps a minute, the stride's stretch and how fast the
    /// feet still slide (m/s); false when no walk or run cycle played this frame.</summary>
    internal bool TryGaitNow(out float speed, out float steps, out float stretch, out float slide)
    {
        float strideTime = mixWeight > 0f ? mixStride / mixWeight : 0f;
        speed = gaitSpeed;
        steps = strideTime > 0f ? 120f * gaitRateNow / strideTime : 0f;
        stretch = gaitStretchNow;
        slide = Mathf.Max(0f, gaitSpeed - gaitNatural * gaitRateNow * gaitStretchNow);
        return strideFrame == Time.frameCount && mixWeight > 0f;
    }

    /// <summary>The stride as this body runs it now, for the /gait readout: the speed, the speed its cycles carry it at their own
    /// pace, the pace against theirs, the stretch, steps a minute and how fast the feet still slide (m/s).</summary>
    internal string GaitReadout()
    {
        TryGaitNow(out _, out float steps, out _, out float slide);
        if (mixWeight <= 0f) slide = 0f;
        var text = new System.Text.StringBuilder();
        text.Append($"속도 {gaitSpeed:0.00} m/s · 클립 제 속도 {gaitNatural:0.00} · 박자 ×{gaitRateNow:0.00} · 보폭 ×{gaitStretchNow:0.00}");
        text.Append($" · 분당 {steps:0}걸음 · 미끄러짐 {slide:0.00} m/s · 골반 {yaw:0}° · 다리 {legLength:0.00} m");
        foreach (var channel in channels)
            if (channel.Gait != null && channel.Target > .05f)
                text.Append($"\n  {channel.Playable.GetAnimationClip().OrNull()?.name} {channel.Target:0.00} ({channel.Gait.Speed * GaitScale:0.00} m/s, "
                    + $"분당 {120f / Mathf.Max(.01f, channel.Gait.Stride):0}걸음)");
        return text.ToString();
    }

    // Standing still, the legs keep their place while the view turns and turn round once it has turned far enough
    // (the spine twists the chest toward the view meanwhile): on the spot, or stepping with the turn clips (turnSteps).
    // True while the legs step (BlendTurn plays the clip on them).
    private bool TurnOnTheSpot(bool still, float dt)
    {
        if (!still) { turning = 0; return false; }
        float look = transform.eulerAngles.y, limit = turnStartAngle + 30f;
        float offset = Mathf.Clamp(Mathf.DeltaAngle(look, legYaw), -limit, limit);
        if (turning == 0 && Mathf.Abs(offset) > turnStartAngle)
        {
            // Legs left behind on the left turn round to the right, and the other way round.
            turning = offset < 0f ? 1 : -1;
            turnVelocity = 0f;
            var start = turning > 0 ? turnRightClip : turnLeftClip;
            if (turnSteps && start.IsValid()) { start.SetTime(0); start.SetDone(false); }
        }
        bool stepping = false;
        if (turning != 0)
        {
            offset = turnSteps ? Mathf.MoveTowards(offset, 0f, turnSpeed * dt)
                : Mathf.SmoothDamp(offset, 0f, ref turnVelocity, turnEaseTime, turnSpeed, dt);
            var clip = turning > 0 ? turnRightClip : turnLeftClip;
            var other = turning > 0 ? turnLeftClip : turnRightClip;
            if (turnSteps && clip.IsValid())
            {
                if (clip.GetTime() >= clip.GetAnimationClip().length) clip.SetTime(0);
                clip.SetSpeed(turnClipSpeed);
                turnMixer.SetInputWeight(turning > 0 ? 1 : 0, 1f);
                turnMixer.SetInputWeight(turning > 0 ? 0 : 1, 0f);
                if (other.IsValid()) other.SetSpeed(0);
                stepping = true;
            }
            if (Mathf.Abs(offset) < 1f) turning = 0;
        }
        legYaw = look + offset;
        targetYaw = offset;
        legsPlanted = true;
        return stepping;
    }
}
