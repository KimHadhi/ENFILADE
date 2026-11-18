using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Détente's healing in the free left hand, on the body itself (the user, 2026-11-18: "데탕트 주사기 기준점 오류: 카메라
/// 기준이 아닌 True FPS 캐릭터 신체/손 기준으로 주사기가 사용되도록"). The syringe rode the camera on a fixed path and no arm
/// moved; from outside it hung by the right hand, beside the gun, half as big again; and a dose into oneself and one into a
/// friend looked the same. Now it is one of the arm's own motions, as the Support's hammer is (PathArm, RepairArm): the
/// left hand leaves the gun (PlayerBodyHands.LendOffHand), the gun stays in the right, and the arm plays a path the
/// Blender shove workspace sends (주사 · 나에게, 주사 · 동료에게, 치료 · 붕대: Tables/Clips/InjectSelf, InjectFriend, Bandage,
/// in GrenadeArmPath's terms, every finger joint as Blender has it), fitted to whatever arm the body has. The syringe
/// rides in the fist at its row of Tables/InjectArms.
/// A needle goes into a body, so the path alone is not enough: bodies differ, a thigh is where its own bones put it, and
/// a friend stands where they stand. At the path's strike (Blender's 타격: the moment the needle is in) the needle's tip
/// is put on the body it goes into: this body's own thigh, or the friend's upper arm on the side facing the Détente,
/// each on its real surface, clothes included (PokeBody, which reads any character's meshes), a little under it. The
/// arm starts homing on it shortly before the strike and lets go once the plunger is home (the row's reachIn, plunge,
/// reachOut), so the path Blender drew is what moves the arm and the body it meets decides only where it ends. A friend
/// further than the arm reaches is reached for as far as the arm goes.
/// The same on every PC: DetenteLooks runs it on each copy from what PlayerNetworkCombat tells them (who was dosed), so
/// friends see the hand at work where the Détente works. Each dose leaves a line in Logs/inject.txt (how far the tip ended
/// from its spot, how much of the arm's reach it took), in the editor and development builds.
/// After the hands, the point, the throw and the guard (GunFramework + 9), like the mending arm; the cloth comes after.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 9)]
public sealed class InjectArm : PathArm
{
    public const string Table = "InjectArms", Record = "inject";

    /// <summary>What the hand does: a dose into itself, a dose into a friend, or the treatment by hand.</summary>
    public enum Kind : byte { None, Self, Friend, Bandage }

    // How quickly the arm takes the thing up and goes back to the gun (s, 0 to full), never ahead of the hand lent off it.
    private const float TakeUp = .1f, PutAway = .2f;

    private Kind playing;
    private PlayerMovement patient;
    private float clock, gapAtStrike = -1f, reachAtStrike;
    private bool once, told;
    private readonly FingerWays fingers = new FingerWays();

    /// <summary>Where the thing rides in the fist: DetenteLooks puts its model under it (Wear).</summary>
    public Transform Holder => holder;
    /// <summary>What the arm is doing now.</summary>
    public Kind Playing => playing;
    /// <summary>How far home the plunger is (0 to 1): from the path's strike over the row's plunge seconds.</summary>
    public float Plunge { get; private set; }

    /// <summary>
    /// Where this body's left shoulder is now and how long its arm is from there to the wrist (m), the measure the
    /// motions' distances are given in. False while the body has no arm to pose.
    /// </summary>
    internal bool TryShoulder(out Vector3 shoulder, out float armLength)
    {
        bool has = Bind();
        shoulder = has ? arm.Upper.position : transform.position;
        armLength = has ? arm.ArmLength : 0f;
        return has;
    }

    /// <summary>The row of Tables/InjectArms for a kind.</summary>
    internal static string RowId(Kind kind) => kind == Kind.Self ? "self" : kind == Kind.Friend ? "friend" : "bandage";

    /// <summary>A player's healing arm, made on their body's model the first time it is wanted; null without a humanoid body.</summary>
    internal static InjectArm Of(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out PlayerBody body)) return null;
        var model = body.BodyAnimator;
        if (model == null || !model.isHuman) return null;
        return model.TryGetComponent(out InjectArm arm) ? arm : model.gameObject.AddComponent<InjectArm>();
    }

    /// <summary>A dose goes in, once: into this body (<paramref name="into"/> null or itself) or into a friend.</summary>
    internal void Dose(PlayerMovement into)
    {
        if (!Bind()) return;
        bool self = into == null || into == movement;
        playing = self ? Kind.Self : Kind.Friend;
        patient = self ? movement : into;
        once = true;
        clock = 0f;
        Plunge = 0f;
        gapAtStrike = -1f;
        told = false;
    }

    /// <summary>The treatment by hand, this frame (called every frame while it lasts); no call eases the arm back to the gun.</summary>
    internal void Wind(PlayerMovement friend)
    {
        if (playing != Kind.Bandage || once)
        {
            playing = Kind.Bandage;
            once = false;
            clock = 0f;
            Plunge = 0f;
        }
        patient = friend;
        drivenFrame = Time.frameCount;
    }

    /// <summary>The thing's model under the fist's holder, drawn on the body's own layer so every view sees it as the body.</summary>
    internal void Wear(Transform prop)
    {
        if (prop == null || !Bind()) return;
        if (holder == null)
        {
            holder = new GameObject("Inject Tool").transform;
            holder.SetParent(transform, false);
        }
        prop.SetParent(holder, false);
        prop.localPosition = Vector3.zero;
        prop.localRotation = Quaternion.identity;
        var skin = GetComponentInChildren<SkinnedMeshRenderer>();
        int layer = skin != null ? skin.gameObject.layer : gameObject.layer;
        foreach (var part in prop.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || GamePause.IsPaused || !Bind()) return;
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return;
        var move = Move.Of(playing);
        bool live = move != null && (once ? clock < move.Length : drivenFrame >= Time.frameCount - 1);
        bool on = live && Free();
        if (hands != null) hands.LendOffHand(this, on ? 1f : 0f);
        own = Mathf.MoveTowards(own, on ? 1f : 0f, dt / (on ? TakeUp : PutAway));
        weight = hands != null ? Mathf.Min(own, hands.OffHandLent) : own;
        if (move == null || weight <= 0f || !GrenadeArmPath.Sample(move.Table, clock, out var key))
        {
            ShowHolder(false);
            if (!live && weight <= 0f) playing = Kind.None;
            return;
        }
        GrenadeArmPath.SampleFingers(move.Table, clock, fingers);
        bool aimed = TryTarget(move, out Vector3 spot, out Vector3 inward);
        Pose(move, key, aimed, spot, inward);
        Seat(move);
        ShowHolder(true);
        Plunge = move.Strike >= 0f && move.PlungeSeconds > 0f ? Mathf.Clamp01((clock - move.Strike) / move.PlungeSeconds) : 0f;
        if (!live) return;
        clock += dt;
        if (!once && move.Length > 0f && clock >= move.Length) clock = Mathf.Repeat(clock, move.Length);
        if (once && !told && move.Strike >= 0f && clock >= move.Strike + move.PlungeSeconds) Tell(move);
    }

    private void OnDisable()
    {
        ShowHolder(false);
        if (hands != null) hands.LendOffHand(this, 0f);
    }

    private void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    // The body can work with the hand: alive, up, not held, climbing or vaulting, and no G throw has the off hand (it goes
    // first, as it does over the mending arm). Walking, crouched, in the air: a dose is given wherever the class key gave it.
    private bool Free()
        => !movement.IsDead && !movement.IsDowned && !movement.IsHeld && !movement.IsParkour && !PlayerGrenades.HandBusy(movement);

    // ---- The arm's geometry, as functions of what they are given. The game poses a player's arm with them every frame,
    // and the editor's bench (InjectBench) stands each character with the very same ones, without playing: a needle
    // that points the wrong way or a thigh an arm cannot reach is a line in its report first. ----

    /// <summary>
    /// The path's own axes on a body: the way the body faces, level (a thigh stays where it is when the view tilts), or
    /// the way from the shoulder to where the needle goes (a friend), the view's when there is nowhere to aim.
    /// </summary>
    internal static Quaternion FrameFor(Move move, Quaternion view, Vector3 shoulder, bool aimed, Vector3 spot)
    {
        if (!move.Aims) return Quaternion.Euler(0f, view.eulerAngles.y, 0f);
        Vector3 way = aimed ? spot - shoulder : Vector3.zero;
        return way.sqrMagnitude > .04f ? Quaternion.LookRotation(way.normalized, Vector3.up) : view;
    }

    /// <summary>
    /// The arm where a key of the path has it from its shoulder, in the frame's axes and the arm's own lengths; then the
    /// wrist moved by <paramref name="homing"/> of what the needle's tip is from <paramref name="want"/>, the hand keeping
    /// the turn the path gave it, so the tip ends on the body it goes into.
    /// </summary>
    /// <param name="left">What is still between the tip and its spot once the arm has done all it can (m: a friend out
    /// of reach, a thigh too far for a short arm); below 0 when the arm was not wholly homed.</param>
    /// <param name="reach">How far the wrist then is from the shoulder, in arm lengths.</param>
    internal static void Stand(ArmBones arm, Move move, in GrenadeArmPath.Key key, Quaternion frame, float homing, Vector3 want,
        out float left, out float reach)
    {
        left = -1f;
        reach = 0f;
        Vector3 shoulder = arm.Upper.position;
        Vector3 wrist = shoulder + frame * (key.Wrist * arm.ArmLength), elbow = shoulder + frame * (key.Elbow * arm.ArmLength);
        Vector3 along = frame * key.Along, palm = frame * key.Palm;
        TwoBoneIK.Solve(arm.Upper, arm.Lower, arm.Hand, wrist, elbow);
        arm.TurnHand(along, palm);
        if (homing <= 0f || !SeatOf(arm, move, out Vector3 at, out Quaternion turn)) return;
        // The row's metres are the game's own, as the thing in the hand is (Seat keeps it at its size on any body).
        Vector3 gap = want - (at + turn * (Vector3.up * move.Tip));
        TwoBoneIK.Solve(arm.Upper, arm.Lower, arm.Hand, arm.Hand.position + gap * homing, elbow);
        arm.TurnHand(along, palm);
        if (homing < 1f || !SeatOf(arm, move, out at, out turn)) return;
        left = (want - (at + turn * (Vector3.up * move.Tip))).magnitude;
        reach = (arm.Hand.position - shoulder).magnitude / Mathf.Max(1e-4f, arm.ArmLength);
    }

    private Quaternion FrameOf(Move move, bool aimed, Vector3 spot) => FrameFor(move, eyes.rotation, arm.Upper.position, aimed, spot);

    // The arm where the path has it from this shoulder in this arm's lengths, then, round the strike, the needle's tip
    // homed on its spot on the body (Stand); the fingers after, and all of it eased in over the frame's own pose.
    private void Pose(Move move, in GrenadeArmPath.Key key, bool aimed, Vector3 spot, Vector3 inward)
    {
        arm.Keep();
        Quaternion frame = FrameOf(move, aimed, spot);
        float homing = aimed && move.HasTip ? Homing(move) : 0f;
        Stand(arm, move, key, frame, homing, spot + inward * move.Depth, out float left, out float reach);
        // The nearest the tip came, for the dose's line in the record.
        if (left >= 0f && (gapAtStrike < 0f || left < gapAtStrike))
        {
            gapAtStrike = left;
            reachAtStrike = reach;
        }
        // Every finger joint round the syringe as the path has it from Blender, onto this body's own hand (FingerWays); a
        // table without them closes the fingers by its curl and thumb.
        if (hands == null || !hands.WearFingerWays(animator, true, fingers, 1f)) arm.Close(frame * key.Along, frame * key.Palm, key.Curl, key.Thumb);
        arm.Ease(weight);
    }

    // How much the needle is on its way to its spot (0 the path alone, 1 on it): rising over the row's reachIn before the
    // strike, whole while the plunger goes home, falling over its reachOut after.
    private float Homing(Move move)
    {
        if (move.Strike < 0f) return 0f;
        float since = clock - move.Strike;
        if (since < 0f) return move.ReachIn > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f + since / move.ReachIn)) : 0f;
        if (since <= move.PlungeSeconds) return 1f;
        return move.ReachOut > 0f ? Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((since - move.PlungeSeconds) / move.ReachOut)) : 0f;
    }

    /// <summary>
    /// Where the thing sits as the hand is now: from the wrist along the hand's own axes (x the side away from the thumb, y
    /// out of the palm, z the fingers' way) by its row's hand lengths, its handle and its other axis along the row's ways,
    /// its grip point there (Tools/Blender/Shove seats the preview the same way). False while the hand has no axes.
    /// </summary>
    internal static bool SeatOf(ArmBones arm, Move move, out Vector3 at, out Quaternion turn)
    {
        at = default;
        turn = Quaternion.identity;
        Vector3 along = arm.AlongNow();
        Vector3 palm = arm.PalmNow(along);
        if (along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return false;
        Quaternion frame = Quaternion.LookRotation(along, palm);
        turn = frame * Quaternion.LookRotation(move.Face, move.Handle);
        at = arm.Hand.position + frame * (move.Held * arm.HandLength) - turn * (Vector3.up * move.Grip);
        return true;
    }

    private void Seat(Move move)
    {
        if (holder == null || !SeatOf(arm, move, out Vector3 at, out Quaternion turn)) return;
        // A body imported at another scale (an avatar's armature at 100) must not scale the thing with it.
        float scale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
        holder.localScale = Vector3.one / scale;
        holder.SetPositionAndRotation(at, turn);
    }

    // Where the needle goes on the body it goes into, and the way in: a point of the row's bone (this body's thigh, the
    // friend's upper arm nearer this shoulder), on the body's real surface where it is read (PokeBody: skin and whatever
    // is worn over it), else a sixth of the bone's length out from its axis. The side of a thigh is its outside and
    // front; the side of a friend's arm is the one facing this shoulder.
    private bool TryTarget(Move move, out Vector3 spot, out Vector3 inward)
    {
        spot = default;
        inward = default;
        var who = move.Kind == Kind.Self ? movement : patient;
        if (who == null) return false;
        Animator body = who == movement ? animator : who.TryGetComponent(out PlayerBody theirs) ? theirs.BodyAnimator : null;
        Vector3? ownSide = who == movement ? OwnSide(who.transform.right, who.transform.forward) : (Vector3?)null;
        return SpotOn(move, body, arm.Upper.position, ownSide, PokeBody.Of(who), out spot, out inward);
    }

    /// <summary>The side a body's own needle comes into its thigh from: its outside (the left) and front.</summary>
    internal static Vector3 OwnSide(Vector3 right, Vector3 forward) => -right * .8f + forward * .6f;

    /// <summary>
    /// Where the needle goes on <paramref name="body"/> and the way in, for the arm whose shoulder is at
    /// <paramref name="shoulder"/>. <paramref name="ownSide"/> is the side the needle comes in from when the body is the
    /// arm's own (OwnSide); null for another body, whose side is the one facing the shoulder. <paramref name="surface"/>
    /// reads the body's real surface (PokeBody); null where nothing does (the bench, a body not yet read), and the spot
    /// is then a sixth of the bone's length out from its axis.
    /// </summary>
    internal static bool SpotOn(Move move, Animator body, Vector3 shoulder, Vector3? ownSide, PokeBody surface, out Vector3 spot, out Vector3 inward)
    {
        spot = default;
        inward = default;
        if (move == null || move.Bone == HumanBodyBones.LastBone || body == null || !body.isHuman) return false;
        Transform from = null, to = null;
        if (move.NearerArm)
        {
            // The friend's arm nearer this shoulder.
            float best = float.MaxValue;
            foreach (var (upper, lower) in new[] { (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm) })
            {
                Transform a = body.GetBoneTransform(upper), b = body.GetBoneTransform(lower);
                if (a == null || b == null) continue;
                float away = (Vector3.Lerp(a.position, b.position, move.Along) - shoulder).sqrMagnitude;
                if (away >= best) continue;
                best = away;
                from = a;
                to = b;
            }
        }
        else
        {
            from = body.GetBoneTransform(move.Bone);
            to = Next(body, move.Bone);
        }
        if (from == null || to == null) return false;
        Vector3 axis = to.position - from.position;
        float length = axis.magnitude;
        if (length < 1e-4f) return false;
        axis /= length;
        Vector3 core = from.position + axis * (length * move.Along);
        // The side the needle comes in from, square to the bone.
        Vector3 side = ownSide ?? shoulder - core;
        side = Vector3.ProjectOnPlane(side, axis);
        if (side.sqrMagnitude < 1e-8f) return false;
        side.Normalize();
        inward = -side;
        spot = core + side * (length * Fallback);
        // The limb's own surface only (its skin and what is worn on it): a skirt or a coat's tail hanging over a thigh
        // hangs from bones of its own, and a needle goes through it into the leg.
        if (surface != null && surface.Prepare() && surface.Raycast(core + side * Probe, inward, Probe, out Vector3 met, out _, out _, out _, from, to))
            spot = met;
        return true;
    }

    // A limb's girth where nothing reads its surface, as a share of its length; how far out the surface is looked for (m).
    private const float Fallback = .16f, Probe = .35f;

    private static Transform Next(Animator body, HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.LeftUpperLeg: return body.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            case HumanBodyBones.RightUpperLeg: return body.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            case HumanBodyBones.LeftUpperArm: return body.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            case HumanBodyBones.RightUpperArm: return body.GetBoneTransform(HumanBodyBones.RightLowerArm);
            case HumanBodyBones.LeftLowerArm: return body.GetBoneTransform(HumanBodyBones.LeftHand);
            case HumanBodyBones.RightLowerArm: return body.GetBoneTransform(HumanBodyBones.RightHand);
            default:
                var from = body.GetBoneTransform(bone);
                return from != null && from.childCount > 0 ? from.GetChild(0) : null;
        }
    }

    // One dose, written down once its plunger is home: whose needle, into whom, how far the tip ended from its spot and
    // how much of the arm's reach that took. Read without playing along ("주사 기록 봐 줘": Logs/inject.txt).
    private void Tell(Move move)
    {
        told = true;
        if (!PlayRecord.On) return;
        string into = playing == Kind.Self ? "self" : patient != null ? patient.name : "?";
        string gap = gapAtStrike >= 0f ? $"{gapAtStrike * 100f:0.0} cm off its spot, arm at {reachAtStrike * 100f:0}% of its reach" : "no spot on the body was found (the path alone)";
        PlayRecord.Line(Record, "time  who  kind  into  bone  what the needle's tip did at the strike",
            $"{Time.time:0.00}  {movement.name}  {playing}  {into}  {(move.NearerArm ? "UpperArm" : move.Bone.ToString())}  {gap}");
    }

    // ---- A motion: its row of Tables/InjectArms and its path table ----

    internal sealed class Move
    {
        public Kind Kind;
        public string Table;
        public bool Aims, HasTip, NearerArm;
        public Vector3 Held, Handle, Face;
        public float Grip, Tip, Along, Depth, ReachIn, ReachOut, PlungeSeconds, Length, Strike = -1f;
        /// <summary>
        /// For a motion done to another body, how far from this shoulder the spot on them may be, in this arm's lengths:
        /// within Reach it is done from where the body stands, and a body further off is run to first, till the spot is
        /// Near (InjectApproach). ReachAt: how high over the ground the arm's line to them must be open (m).
        /// </summary>
        public float Near, Reach, ReachAt;
        public HumanBodyBones Bone = HumanBodyBones.LastBone;

        private static readonly Dictionary<Kind, Move> moves = new Dictionary<Kind, Move>();
        private static bool listening;

        public static Move Of(Kind kind)
        {
            if (kind == Kind.None) return null;
            if (!listening)
            {
                listening = true;
                GameTables.Reloaded += moves.Clear;
            }
            if (moves.TryGetValue(kind, out var known)) return known;
            var move = Read(kind);
            moves[kind] = move;
            return move;
        }

        private static Move Read(Kind kind)
        {
            if (!GameTables.TryGetRow(InjectArm.Table, RowId(kind), out var row)) return null;
            string tip = row.Text("tip"), bone = row.Text("target");
            var move = new Move
            {
                Kind = kind,
                Table = row.Text("table"),
                Aims = string.Equals(row.Text("frame"), "aim", StringComparison.OrdinalIgnoreCase),
                Held = new Vector3(row.Number("heldSide", 0f), row.Number("heldPalm", .35f), row.Number("heldAlong", .85f)),
                Handle = new Vector3(row.Number("handleX", -1f), row.Number("handleY", 0f), row.Number("handleZ", 0f)),
                Face = new Vector3(row.Number("faceX", 0f), row.Number("faceY", 0f), row.Number("faceZ", 1f)),
                Grip = row.Number("grip", 0f),
                HasTip = tip.Length > 0,
                Tip = row.Number("tip", 0f),
                NearerArm = string.Equals(bone, "UpperArm", StringComparison.OrdinalIgnoreCase),
                Along = Mathf.Clamp01(row.Number("targetAlong", .5f)),
                Depth = Mathf.Max(0f, row.Number("depth", 0f)),
                ReachIn = Mathf.Max(0f, row.Number("reachIn", .12f)),
                ReachOut = Mathf.Max(0f, row.Number("reachOut", .1f)),
                PlungeSeconds = Mathf.Max(0f, row.Number("plunge", .14f)),
                Near = Mathf.Max(.1f, row.Number("near", .9f)),
                Reach = Mathf.Max(.1f, row.Number("reach", 1.1f)),
                ReachAt = Mathf.Max(0f, row.Number("reachAt", 1.1f)),
            };
            move.Reach = Mathf.Max(move.Reach, move.Near);
            if (!move.NearerArm && bone.Length > 0 && Enum.TryParse(bone, true, out HumanBodyBones named)) move.Bone = named;
            if (move.NearerArm) move.Bone = HumanBodyBones.LeftUpperArm;
            if (string.IsNullOrEmpty(move.Table) || move.Handle.sqrMagnitude < 1e-6f || move.Face.sqrMagnitude < 1e-6f) return null;
            move.Handle.Normalize();
            move.Face = Vector3.ProjectOnPlane(move.Face, move.Handle).normalized;
            if (move.Face.sqrMagnitude < 1e-6f) return null;
            // The motion runs to its last key; the strike is the first key marked so (Blender's 타격).
            foreach (string key in GameTables.RowIds(move.Table))
            {
                if (!GameTables.TryGetRow(move.Table, key, out var at)) continue;
                float cycle = at.Number("cycle", -1f);
                if (cycle < 0f) continue;
                move.Length = Mathf.Max(move.Length, cycle);
                if (move.Strike < 0f && at.Number("strike", 0f) > .5f) move.Strike = cycle;
            }
            return move.Length > 0f ? move : null;
        }
    }
}
