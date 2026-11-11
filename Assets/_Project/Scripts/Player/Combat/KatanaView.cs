using UnityEngine;

/// <summary>
/// The katana seen, on every PC: the stand-in made in code (KatanaModel) until a model is dropped in
/// Art/Models/Weapons/Katana (MeleeModels), held under the eyes as the guns and fists are, and the body's own hands
/// holding its handle (PlayerBodyHands.Blade): the right under the guard, the left at the pommel, the fingers round it.
/// At rest it is held up at the right, the tip toward the middle of the view; with the guard up it lies across the face;
/// running it is carried low. A move (PlayerKatana, Tables/Katana) sweeps it along its arc: drawn back to the arc's
/// start (past it, for a cut that winds up), through the arc in the hit window with its edge leading, a little past it,
/// and back. The hands follow a share of the arc's turn round the chest (handTurn) while the blade turns all of it and
/// points where the move's sweep is looking, so what it cuts is where the blade is seen. A parry swings it out to meet
/// the blow on its side; a block knocks it back. Someone else's copy plays the moves their PC sends. Put away for
/// climbing and the like, as a gun is. Numbers: Tables/Player.csv, row KatanaView (eye space: x right, y up, z ahead, m).
/// </summary>
[DisallowMultipleComponent]
public sealed class KatanaView : MonoBehaviour, ITableBound
{
    public const string Row = "KatanaView";
    // Blends (s): the guard comes up at once and goes down over GuardDown; the run carry in and out; smoothing between poses.
    private const float GuardUp = .05f, GuardDown = .14f, RunBlend = .2f, Smoothing = .035f;
    // A cut that winds up spends this share of its windup rising to the cocked pose before it sets off.
    private const float WindupRise = .7f;
    // The longest follow-through past the arc's end (s).
    private const float FollowMost = .15f;

    [Header("Blade (m)")]
    [SerializeField, Min(.2f)] private float bladeLength = .7f;
    [SerializeField, Min(.1f)] private float handleLength = .27f;
    [SerializeField, Min(.005f), Tooltip("Half the handle's thickness, where the fingers close.")]
    private float handleRadius = .015f;
    [Header("Swing")]
    [SerializeField, Tooltip("What the hands swing round (eye space, m): the chest.")]
    private Vector3 pivot = new Vector3(0f, -.3f, .05f);
    [SerializeField, Min(.05f), Tooltip("How far from the pivot the hands hold the handle's middle (m).")]
    private float gripReach = .36f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the arc's turn the hands follow; the blade turns all of it.")]
    private float handTurn = .6f;
    [SerializeField, Min(.3f), Tooltip("The blade points at the move's sweep this far out (m).")]
    private float aimDistance = 1.3f;
    [Header("Rest poses (eye space: the handle's middle; the blade's way; its edge's way)")]
    [SerializeField] private Vector3 idleGrip = new Vector3(.17f, -.3f, .32f);
    [SerializeField] private Vector3 idleBlade = new Vector3(-.3f, .8f, .5f), idleEdge = new Vector3(0f, 0f, 1f);
    [SerializeField] private Vector3 guardGrip = new Vector3(.12f, -.17f, .36f);
    [SerializeField] private Vector3 guardBlade = new Vector3(-.85f, .5f, .15f), guardEdge = new Vector3(0f, .5f, 1f);
    [SerializeField] private Vector3 runGrip = new Vector3(.22f, -.48f, .16f);
    [SerializeField] private Vector3 runBlade = new Vector3(-.15f, -.35f, -1f), runEdge = new Vector3(0f, -1f, 0f);
    [SerializeField, Min(0f), Tooltip("Moving faster than this (m/s) with no move under way, it is carried low.")]
    private float runSpeed = 4.5f;
    [Header("Hands (m back from the guard)")]
    [SerializeField, Min(0f)] private float rightHand = .035f;
    [SerializeField, Min(0f)] private float leftHand = .2f;
    [Header("A dropped-in model (MeleeModels)")]
    [SerializeField] private Vector3 modelOffset, modelTurn;
    [SerializeField, Min(.01f)] private float modelScale = 1f;

    private PlayerMovement player;
    private PlayerMeleeCombat melee;
    private PlayerKatana katana;
    private Transform root, model;
    private Renderer[] renderers;
    private Pose current, start;
    private bool posed, shown, dirty;
    private int serial = -1;
    private float guardShare, runShare;
    private float deflectAt = -1f, deflectSide = 1f;
    private bool deflectParry;

    /// <summary>The katana is out and held: the hands hold its handle (PlayerBodyHands).</summary>
    public bool IsActive => root != null && katana != null && shown && katana.IsActive;
    /// <summary>Half the handle's thickness, for the fingers closing round it (m).</summary>
    internal float HandleRadius => handleRadius;

    /// <summary>
    /// Where a hand holds the handle (world): the point on the handle's line under the palm, the way the fingers go round it
    /// (toward the edge) and the way the palm faces (onto the handle). The right hand is under the guard, the left at the pommel.
    /// </summary>
    internal bool Grip(bool left, out Vector3 point, out Vector3 fingers, out Vector3 palm)
    {
        point = fingers = palm = Vector3.zero;
        if (!IsActive) return false;
        point = root.position - root.forward * (left ? leftHand : rightHand);
        fingers = -root.up;
        palm = left ? root.right : -root.right;
        return true;
    }

    private void OnEnable()
    {
        player = GetComponentInParent<PlayerMovement>();
        melee = GetComponent<PlayerMeleeCombat>();
        katana = GetComponent<PlayerKatana>();
        GameTables.Bind(PlayerMovement.TableName, Row, this);
        if (katana != null) { katana.Deflected -= OnDeflected; katana.Deflected += OnDeflected; }
    }

    private void OnDisable()
    {
        if (katana != null) katana.Deflected -= OnDeflected;
        Show(false);
        posed = false;
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    // A table edit may change the blade's length: the stand-in is made again.
    void ITableBound.OnTableApplied() => dirty = true;

    private void LateUpdate()
    {
        if (!Application.isPlaying || player == null) return;
        if (katana == null) katana = GetComponent<PlayerKatana>();
        if (melee == null) melee = GetComponent<PlayerMeleeCombat>();
        if (katana == null) return;
        bool show = katana.IsActive && HandsFree;
        // Made the first time it is drawn, so a player who never holds one carries nothing for it.
        if (root == null) { if (!show) return; Build(); }
        else if (dirty) MakeModel();
        dirty = false;
        if (show != shown) Show(show);
        if (!show) { posed = false; return; }

        float dt = Time.deltaTime;
        var rest = Rest(dt);
        var target = rest;
        bool cutting = false;
        var attack = katana.Attack;
        if (katana.Serial != serial)
        {
            serial = katana.Serial;
            start = posed ? current : rest;
        }
        if (attack != null) target = Swing(attack, katana.Elapsed, rest, out cutting);
        target = Deflect(target);
        if (!posed) { current = target; posed = true; }
        // Through the cut the blade is where the sweep is; elsewhere it eases, so a cut stopped short or a guard taken
        // mid-swing doesn't snap.
        else current = cutting ? target : Blend(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, dt) / Smoothing));
        root.localPosition = current.position;
        root.localRotation = current.rotation;
    }

    // Not while a move needs both hands (a climb, a ladder, a roll) or once dead: put away as a gun is (TraversalHands).
    private bool HandsFree => !player.IsDead && PlayerBodyHands.HoldsWeapons(player.CurrentState)
        && TraversalHands.For(player) != TraversalHandUse.Bare;

    // ---- Poses ----

    // At rest: held up, carried low at a run, across the face with the guard up, and out of the slot as it is drawn.
    private Pose Rest(float dt)
    {
        bool guarding = melee != null && (melee.IsGuarding || melee.IsParryActive);
        guardShare = Mathf.MoveTowards(guardShare, guarding ? 1f : 0f, dt / (guarding ? GuardUp : GuardDown));
        Vector3 moving = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
        bool running = moving.magnitude >= runSpeed && katana.Move == KatanaMove.None && !guarding;
        runShare = Mathf.MoveTowards(runShare, running ? 1f : 0f, dt / RunBlend);
        var carried = Still(runGrip, runBlade, runEdge);
        var pose = Blend(Still(idleGrip, idleBlade, idleEdge), carried, Smooth(runShare));
        pose = Blend(pose, Still(guardGrip, guardBlade, guardEdge), Smooth(guardShare));
        float drawn = katana.DrawShare;
        return drawn < 1f ? Blend(carried, pose, Smooth(drawn)) : pose;
    }

    private Pose Swing(KatanaAttack attack, float t, Pose rest, out bool cutting)
    {
        cutting = false;
        var open = Arc(attack, 0f);
        if (t < attack.hitStart)
        {
            float u = attack.hitStart > 0f ? Mathf.Clamp01(t / attack.hitStart) : 1f;
            if (attack.windup <= 0f) return Blend(start, open, Smooth(u));
            // Up to the cocked pose past the arc's start, then off onto it as the cut begins.
            var cocked = Arc(attack, -attack.windup);
            return u < WindupRise ? Blend(start, cocked, Smooth(u / WindupRise))
                : Blend(cocked, open, EaseIn((u - WindupRise) / (1f - WindupRise)));
        }
        if (t <= attack.hitEnd) { cutting = true; return Arc(attack, attack.ArcAt(t)); }
        float follow = Mathf.Min(FollowMost, Mathf.Max(0f, attack.duration - attack.hitEnd) * .4f);
        float v = follow > 0f ? Mathf.Clamp01((t - attack.hitEnd) / follow) : 1f;
        float through = 1f + attack.follow * EaseOut(v);
        if (v < 1f) { cutting = true; return Arc(attack, through); }
        float recover = attack.duration - attack.hitEnd - follow;
        float w = recover > 0f ? Mathf.Clamp01((t - attack.hitEnd - follow) / recover) : 1f;
        return Blend(Arc(attack, through), rest, Smooth(w));
    }

    // Where the blade is at s along a move's arc: the hands round the pivot a share of the turn, the blade pointing at
    // where the sweep looks, its edge leading the way it travels.
    private Pose Arc(KatanaAttack attack, float s)
    {
        Vector3 blade = BladeWay(attack, s);
        Vector3 edge = Vector3.ProjectOnPlane(BladeWay(attack, s + .03f) - BladeWay(attack, s - .03f), blade);
        if (edge.sqrMagnitude < 1e-8f) edge = Vector3.ProjectOnPlane(Vector3.down, blade);
        var hands = Quaternion.Euler(-Mathf.LerpUnclamped(attack.pitch.x, attack.pitch.y, s) * handTurn,
            Mathf.LerpUnclamped(attack.yaw.x, attack.yaw.y, s) * handTurn, 0f) * Vector3.forward;
        Vector3 grip = pivot + hands * gripReach + Vector3.forward * (attack.thrust * Mathf.Sin(Mathf.PI * Mathf.Clamp01(s)));
        return new Pose(grip + blade * (handleLength * .5f), Quaternion.LookRotation(blade, -edge.normalized));
    }

    private Vector3 BladeWay(KatanaAttack attack, float s)
    {
        Vector3 to = attack.ArcTurn(s) * Vector3.forward * aimDistance - pivot;
        return to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.forward;
    }

    // A rest pose from the handle's middle, the blade's way and its edge's way.
    private Pose Still(Vector3 grip, Vector3 blade, Vector3 edge)
    {
        Vector3 way = blade.sqrMagnitude > 1e-6f ? blade.normalized : Vector3.forward;
        Vector3 facing = Vector3.ProjectOnPlane(edge, way);
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.ProjectOnPlane(Vector3.down, way);
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.ProjectOnPlane(Vector3.forward, way);
        return new Pose(grip + way * (handleLength * .5f), Quaternion.LookRotation(way, -facing.normalized));
    }

    // The blade meets a parried blow on its side, out from the guard, and holds there through the hit stop (real time);
    // a block knocks it back toward the face.
    private Pose Deflect(Pose pose)
    {
        if (deflectAt < 0f) return pose;
        float elapsed = Time.unscaledTime - deflectAt;
        float rise = deflectParry ? .04f : .03f, hold = deflectParry ? .16f : .06f, fall = deflectParry ? .25f : .15f;
        float amount;
        if (elapsed < rise) amount = 1f - Mathf.Pow(1f - elapsed / rise, 3f);
        else if (elapsed < rise + hold) amount = 1f;
        else if (elapsed < rise + hold + fall) amount = Mathf.Pow(1f - (elapsed - rise - hold) / fall, 2f);
        else { deflectAt = -1f; return pose; }
        var met = Still(guardGrip, guardBlade, guardEdge);
        var turn = Quaternion.AngleAxis(deflectSide * (deflectParry ? 28f : 10f), Vector3.up);
        met.position = pivot + turn * (met.position - pivot) + Vector3.forward * (deflectParry ? .06f : -.04f);
        met.rotation = turn * met.rotation;
        return Blend(pose, met, amount);
    }

    private void OnDeflected(bool parry, float side)
    {
        deflectAt = Time.unscaledTime;
        deflectParry = parry;
        // Almost dead ahead the sides take turns, as the fists' parry hands do.
        deflectSide = Mathf.Abs(side) > .08f ? Mathf.Sign(side) : -deflectSide;
    }

    private static Pose Blend(Pose a, Pose b, float t)
        => new Pose(Vector3.LerpUnclamped(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, Mathf.Clamp01(t)));
    private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
    private static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t; }
    private static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }

    // ---- The model ----

    private void Build()
    {
        var eyes = PlayerView.EyesOf(player);
        root = new GameObject("Katana").transform;
        root.SetParent(eyes, false);
        MakeModel();
    }

    private void MakeModel()
    {
        if (model != null) Destroy(model.gameObject);
        var linked = MeleeModels.For(MeleeWeapons.Katana);
        if (linked != null)
        {
            var copy = Instantiate(linked, root, false);
            copy.name = linked.name;
            // Only seen: nothing of it may stop a bullet, a step or the blade's own sweep.
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            model = copy.transform;
            model.localPosition = modelOffset;
            model.localRotation = Quaternion.Euler(modelTurn);
            model.localScale = Vector3.one * Mathf.Max(.01f, modelScale);
        }
        else model = KatanaModel.Build(root, bladeLength, handleLength);
        renderers = model.GetComponentsInChildren<Renderer>(true);
        Show(shown);
    }

    private void Show(bool show)
    {
        shown = show;
        if (renderers == null) return;
        foreach (var part in renderers) if (part != null) part.enabled = show;
    }
}
