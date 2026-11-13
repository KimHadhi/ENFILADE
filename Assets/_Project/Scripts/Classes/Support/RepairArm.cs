using System;
using System.Collections.Generic;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// A Support's mending in the free left hand (the user, 2026-11-12: "야 서포트 망치를 누가 이런 식으로 만드래. 심지어 3인칭하면
/// 카메라 기준이라 공중에 떠 잇잖아. 내가 원하는 건 남는 손으로 망치 손으로 두들기는 거라고"): while RepairLooks has a tool out, the
/// left hand leaves the gun (PlayerBodyHands.LendOffHand) and plays that tool's loop on this body's own left arm, the
/// hammer's stroke or the torch's steady hold, while the gun stays in the right hand. The loops are path tables the Blender
/// shove workspace sends ("수리 · 망치", "수리 · 토치": Tables/RepairHammer, Tables/RepairTorch, in GrenadeArmPath's terms: the
/// wrist and the elbow from the shoulder in arm lengths, the hand's run and facing, all in the view's axes), fitted to
/// whatever arm the body has from its own shoulder in its own lengths, as GrenadeArm fits a throw. The tool rides in the
/// palm at its row of Tables/RepairArms; the hammer's strike (its path's strike key) raises Struck, where RepairLooks
/// knocks. The same on every PC: RepairLooks runs on each copy from the tool PlayerNetworkCombat tells them, so friends see
/// the hand at work where the Support works. After the hands, the point, the throw and the guard (GunFramework + 9); the
/// cloth comes after, so a sleeve follows.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 9)]
public sealed class RepairArm : PathArm
{
    public const string Table = "RepairArms";
    // How quickly the arm takes the tool up and goes back to the gun (s, 0 to full), never ahead of the hand lent off it.
    private const float TakeUp = .15f, PutAway = .2f;

    /// <summary>The hammer met the planks (the path's strike key), on this PC's copy of the arm.</summary>
    public event Action Struck;

    private float clock;
    private RepairLooks.Tool tool, playing;

    /// <summary>Where the tool rides in the palm: RepairLooks puts its model under it (Wear).</summary>
    public Transform Holder => holder;

    /// <summary>The tool's row in Tables/RepairArms (its seat in the hand, its path table and its look).</summary>
    internal static string RowId(RepairLooks.Tool tool) => tool == RepairLooks.Tool.Torch ? "torch" : "hammer";

    /// <summary>A player's mending arm, made on their body's model the first time it is wanted; null without a humanoid body.</summary>
    internal static RepairArm Of(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out PlayerBody body)) return null;
        var model = body.BodyAnimator;
        if (model == null || !model.isHuman) return null;
        return model.TryGetComponent(out RepairArm arm) ? arm : model.gameObject.AddComponent<RepairArm>();
    }

    /// <summary>This frame's tool (RepairLooks, every frame while one is out); none, or no call, eases the arm back to the gun.</summary>
    internal void Drive(RepairLooks.Tool held)
    {
        tool = held;
        drivenFrame = Time.frameCount;
    }

    /// <summary>The tool's model under the palm's holder, drawn on the body's own layer so every view sees it as the body.</summary>
    internal void Wear(Transform prop)
    {
        if (prop == null || !Bind()) return;
        if (holder == null)
        {
            holder = new GameObject("Repair Tool").transform;
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
        bool on = drivenFrame >= Time.frameCount - 1 && tool != RepairLooks.Tool.None && Free();
        if (hands != null) hands.LendOffHand(this, on ? 1f : 0f);
        own = Mathf.MoveTowards(own, on ? 1f : 0f, dt / (on ? TakeUp : PutAway));
        weight = hands != null ? Mathf.Min(own, hands.OffHandLent) : own;
        if (on && tool != playing)
        {
            playing = tool;
            clock = 0f;
        }
        var loop = Loop.Of(playing);
        if (weight <= 0f || loop == null || !GrenadeArmPath.Sample(loop.Table, clock, out var key))
        {
            ShowHolder(false);
            return;
        }
        Pose(key);
        Seat(loop);
        ShowHolder(true);
        // On round the loop; the strike sounds once a stroke, while the arm is at work.
        float before = clock;
        clock += dt;
        bool wrapped = loop.Length > 0f && clock >= loop.Length;
        if (wrapped) clock = Mathf.Repeat(clock, loop.Length);
        if (on && weight >= .5f && loop.Strike >= 0f
            && (wrapped ? before < loop.Strike || clock >= loop.Strike : before < loop.Strike && clock >= loop.Strike))
            Struck?.Invoke();
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

    private bool Bind()
    {
        if (bound) return true;
        movement = GetComponentInParent<PlayerMovement>();
        hands = movement != null ? movement.GetComponent<PlayerBodyHands>() : null;
        animator = GetComponent<Animator>();
        if (movement == null || animator == null || !animator.isHuman) return false;
        upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (upper == null || lower == null || hand == null) return false;
        turnedCount = 0;
        turned[turnedCount++] = upper;
        turned[turnedCount++] = lower;
        turned[turnedCount++] = hand;
        for (int finger = 0; finger < 5; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = animator.GetBoneTransform(FirstJoints[finger] + joint);
                fingers[finger * 3 + joint] = bone;
                if (bone != null) turned[turnedCount++] = bone;
            }
        var middleJoint = fingers[Index * 3 + 1];
        var lastJoint = fingers[Index * 3 + 2];
        jointAxis = middleJoint != null && lastJoint != null
            ? Quaternion.Inverse(middleJoint.rotation) * (lastJoint.position - middleJoint.position).normalized
            : Vector3.forward;
        handAxis = Quaternion.Inverse(hand.rotation) * (hand.position - lower.position).normalized;
        armLength = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
        var knuckle = Knuckle();
        handLength = knuckle != null ? Vector3.Distance(hand.position, knuckle.position) : armLength * .17f;
        bound = armLength > 1e-3f && handLength > 1e-4f;
        return bound;
    }

    // The body can work with the hand: alive, up, standing or crouched (walking too), not held, climbing or vaulting, and
    // no G throw has the off hand (it goes first, as it does over the hold-F point: the manager, 2026-11-12).
    private bool Free()
    {
        if (movement.IsDead || movement.IsDowned || movement.IsHeld || movement.IsParkour) return false;
        if (PlayerGrenades.HandBusy(movement)) return false;
        var state = movement.CurrentState;
        return state == MovementState.Standing || state == MovementState.Crouching;
    }

    private void Pose(in GrenadeArmPath.Key key)
    {
        for (int i = 0; i < turnedCount; i++) framed[i] = turned[i].localRotation;
        Quaternion view = eyes.rotation;
        Vector3 shoulder = upper.position;
        // The wrist where the path has it from this shoulder, in this arm's lengths; the elbow toward the path's elbow.
        TwoBoneIK.Solve(upper, lower, hand, shoulder + view * (key.Wrist * armLength), shoulder + view * (key.Elbow * armLength));
        Vector3 along = view * key.Along, palm = view * key.Palm;
        TurnHand(along, palm);
        Fingers(along, palm, key.Curl, key.Thumb);
        if (weight < 1f)
            for (int i = 0; i < turnedCount; i++) turned[i].localRotation = Quaternion.Slerp(framed[i], turned[i].localRotation, weight);
    }

    // The tool in the palm as the hand finally is this frame: from the wrist along the hand's own axes (x the side away
    // from the thumb, y out of the palm, z the fingers' way) by its row's hand lengths, its handle and working end along
    // the row's ways, its grip point there (Tools/Blender/Shove/repair_moves.py seats the preview the same way).
    private void Seat(Loop loop)
    {
        if (holder == null) return;
        Vector3 along = AlongNow();
        Vector3 palm = PalmNow(along);
        if (along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return;
        Quaternion frame = Quaternion.LookRotation(along, palm);
        Quaternion turn = frame * Quaternion.LookRotation(loop.Face, loop.Handle);
        Vector3 at = hand.position + frame * (loop.Held * handLength);
        // A body imported at another scale (an avatar's armature at 100) must not scale the tool with it.
        float scale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
        holder.localScale = Vector3.one / scale;
        holder.SetPositionAndRotation(at - turn * (Vector3.up * loop.Grip), turn);
    }

    private void ShowHolder(bool show)
    {
        if (holder != null && holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
    }

    // The four fingers closed round the handle by the path's curl, shared over their joints; the thumb across the palm and
    // on round it as far as the path closes it (GrenadeArm.Fingers, the whole of both).
    private void Fingers(Vector3 along, Vector3 palm, float curl, float thumb)
    {
        Vector3 axis = Vector3.Cross(along, palm);
        if (axis.sqrMagnitude < 1e-10f) return;
        axis.Normalize();
        for (int finger = Index; finger <= Little; finger++)
        {
            float angle = 0f;
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = fingers[finger * 3 + joint];
                if (bone == null) break;
                angle += curl * CurlShare[joint];
                Turn(bone, JointWay(finger, joint), Quaternion.AngleAxis(angle, axis) * along, 1f);
            }
        }
        float close = Mathf.Clamp01(thumb / 90f);
        if (close <= 0f || fingers[Thumb * 3] == null) return;
        Vector3 over = (along * .5f + palm * .6f - axis * .45f).normalized;
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = fingers[Thumb * 3 + joint];
            if (bone == null) break;
            Vector3 bendAxis = Vector3.Cross(over, palm);
            if (joint > 0 && bendAxis.sqrMagnitude > 1e-8f) over = Quaternion.AngleAxis(joint == 1 ? 15f : 20f, bendAxis.normalized) * over;
            Turn(bone, JointWay(Thumb, joint), over, close);
        }
    }

    // ---- A tool's loop: its row of Tables/RepairArms and its path table ----

    private sealed class Loop
    {
        public string Table;
        public Vector3 Held, Handle, Face;
        public float Grip, Length, Strike = -1f;

        private static readonly Dictionary<RepairLooks.Tool, Loop> loops = new Dictionary<RepairLooks.Tool, Loop>();
        private static bool listening;

        public static Loop Of(RepairLooks.Tool tool)
        {
            if (tool == RepairLooks.Tool.None) return null;
            if (!listening)
            {
                listening = true;
                GameTables.Reloaded += loops.Clear;
            }
            if (loops.TryGetValue(tool, out var known)) return known;
            var loop = Read(RowId(tool));
            loops[tool] = loop;
            return loop;
        }

        private static Loop Read(string id)
        {
            if (!GameTables.TryGetRow(RepairArm.Table, id, out var row)) return null;
            var loop = new Loop
            {
                Table = row.Text("table"),
                Held = new Vector3(row.Number("heldSide", 0f), row.Number("heldPalm", .35f), row.Number("heldAlong", .8f)),
                Handle = new Vector3(row.Number("handleX", -1f), row.Number("handleY", 0f), row.Number("handleZ", 0f)),
                Face = new Vector3(row.Number("faceX", 0f), row.Number("faceY", 0f), row.Number("faceZ", 1f)),
                Grip = row.Number("grip", 0f),
            };
            if (string.IsNullOrEmpty(loop.Table) || loop.Handle.sqrMagnitude < 1e-6f || loop.Face.sqrMagnitude < 1e-6f) return null;
            loop.Handle.Normalize();
            loop.Face = Vector3.ProjectOnPlane(loop.Face, loop.Handle).normalized;
            if (loop.Face.sqrMagnitude < 1e-6f) return null;
            // The loop runs to its last key; the strike is the first key marked so (Blender's 타격).
            foreach (string key in GameTables.RowIds(loop.Table))
            {
                if (!GameTables.TryGetRow(loop.Table, key, out var at)) continue;
                float cycle = at.Number("cycle", -1f);
                if (cycle < 0f) continue;
                loop.Length = Mathf.Max(loop.Length, cycle);
                if (loop.Strike < 0f && at.Number("strike", 0f) > .5f) loop.Strike = cycle;
            }
            return loop.Length > 0f ? loop : null;
        }
    }
}
