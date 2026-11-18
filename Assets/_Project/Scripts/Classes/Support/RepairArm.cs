using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Support's mending in the free left hand (the user, 2026-11-12: "야 서포트 망치를 누가 이런 식으로 만드래. 심지어 3인칭하면
/// 카메라 기준이라 공중에 떠 잇잖아. 내가 원하는 건 남는 손으로 망치 손으로 두들기는 거라고"): while RepairLooks has a tool out, the
/// left hand leaves the gun (PlayerBodyHands.LendOffHand) and plays that tool's loop on this body's own left arm, the
/// hammer's stroke or the torch's steady hold, while the gun stays in the right hand. The loops are path tables the Blender
/// shove workspace sends ("수리 · 망치", "수리 · 토치": Tables/RepairHammer, Tables/RepairTorch, in GrenadeArmPath's terms: the
/// wrist and the elbow from the shoulder in arm lengths, the hand's run and facing, all in the view's axes, and every
/// finger joint round the tool as Blender has it: FingerWays, onto the body's own hand through
/// PlayerBodyHands.WearFingerWays; a table from before 0.9.61 closes the fingers by its curl and thumb), fitted to
/// whatever arm the body has from its own shoulder in its own lengths, as GrenadeArm fits a throw. The tool rides in the
/// palm at its row of Tables/RepairArms; the hammer's strike (its path's strike key) raises Struck, where RepairLooks
/// knocks. The same on every PC: RepairLooks runs on each copy from the tool PlayerNetworkCombat tells them, so friends see
/// the hand at work where the Support works. After the hands, the point, the throw and the guard (GunFramework + 9); the
/// cloth comes after, so a sleeve follows.
/// The left hand is asked of its owner (HandOwner; Tables/Player/HandUses.csv support.repair) for as long as a tool is
/// out, and the arm is up only while the owner says so, whose yes is what lends the hand off the gun. Until 2026-11-18
/// a list here (Free) asked the body and the grenade, and knew nothing of a reload.
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
    // The loop's fingers this frame (every joint from Blender: FingerWays).
    private readonly FingerWays fingers = new FingerWays();

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
        // The hand for it, said again every frame a tool is out: SupportKit took it under this arm's name before it
        // mended anything; on a friend's copy the arm asks here.
        if (held != RepairLooks.Tool.None && Bind() && owner != null) owner.Take(HandUses.Repair, this, false);
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
        // Up while the owner says the hand is the tool's and the body is in a state the arm can be up in (its yes takes
        // the hand off the gun: HandOwner.Arm).
        bool on = drivenFrame >= Time.frameCount - 1 && tool != RepairLooks.Tool.None && owner != null && owner.Arm(HandUses.Repair);
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
        GrenadeArmPath.SampleFingers(loop.Table, clock, fingers);
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
        if (owner != null) owner.Give(this);
    }

    private void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    private void Pose(in GrenadeArmPath.Key key)
    {
        arm.Keep();
        Quaternion view = eyes.rotation;
        Vector3 shoulder = arm.Upper.position;
        // The wrist where the path has it from this shoulder, in this arm's lengths; the elbow toward the path's elbow.
        TwoBoneIK.Solve(arm.Upper, arm.Lower, arm.Hand, shoulder + view * (key.Wrist * arm.ArmLength), shoulder + view * (key.Elbow * arm.ArmLength));
        Vector3 along = view * key.Along, palm = view * key.Palm;
        arm.TurnHand(along, palm);
        // Every finger joint round the tool as the loop has it from Blender, onto this body's own hand (FingerWays); a table
        // from before them closes the fingers by its curl and thumb.
        if (hands == null || !hands.WearFingerWays(animator, true, fingers, 1f)) arm.Close(along, palm, key.Curl, key.Thumb);
        arm.Ease(weight);
    }

    // The tool in the palm as the hand finally is this frame: from the wrist along the hand's own axes (x the side away
    // from the thumb, y out of the palm, z the fingers' way) by its row's hand lengths, its handle and working end along
    // the row's ways, its grip point there (Tools/Blender/Shove/repair_moves.py seats the preview the same way).
    private void Seat(Loop loop)
    {
        if (holder == null) return;
        Vector3 along = arm.AlongNow();
        Vector3 palm = arm.PalmNow(along);
        if (along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return;
        Quaternion frame = Quaternion.LookRotation(along, palm);
        Quaternion turn = frame * Quaternion.LookRotation(loop.Face, loop.Handle);
        Vector3 at = arm.Hand.position + frame * (loop.Held * arm.HandLength);
        // A body imported at another scale (an avatar's armature at 100) must not scale the tool with it.
        float scale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
        holder.localScale = Vector3.one / scale;
        holder.SetPositionAndRotation(at - turn * (Vector3.up * loop.Grip), turn);
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
