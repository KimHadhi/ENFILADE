using System;
using UnityEngine;

/// <summary>
/// The class's own knife through an execution or a stealth kill (PlayerExecution; the user, 2026-11-13: "일단 칼은
/// 클래스별로 각자 전용 칼을 쓰게 하고 싶고"). When one starts the gun is put away (WeaponController reads HoldsHands, as it
/// does for a climb), the right hand draws the knife of the player's class (ClassKnives) and the left takes hold of the
/// victim; the stroke (KnifeStroke) waits drawn while the player dashes in, drives the blade into the neck at the
/// execution's impact, pulls it out and puts it away, and then the gun comes back. Shown the same on every PC (the user's
/// rule: what I see is what friends see): the executing player's own PC raises Phased at each step (drawn, stabbing, put
/// away) with the victim's neck and head as it measured them, PlayerNetworkCombat sends that to every other PC, and each
/// copy plays the same stroke from it (Show); the arms are posed by ExecutionKnifeArm on each body. The draw, the stab and
/// the sheath sound at the hand on every PC (the knife row's cues). Timing: the strike lands impactTime after the stab
/// begins, as the execution's hit does, never slower than the stroke was drawn; after it, the stroke runs at the
/// execution's own pace (PlayerExecution.Pace: a takedown played fast puts its knife away as fast), or, when a paired
/// execution clip keeps the player past the stroke's own rest (ArmsPosedElsewhere as the stab begins, an execution that
/// takes its time), over what is left of the execution (PlayerExecution.Duration), so the knife is put away and the gun
/// comes back as the pair ends, not while its clip is still getting the player up. A takedown begun while the knife of
/// the last one is still in hand (not yet at its sheath) starts with it drawn: quick kills one after another never go
/// back to the hip between them.
/// A knife with a draw of its own (ClassKnives.OwnDraw: its addon's, the melee slot's drawClip; the user, 2026-11-14: "칼
/// 뽑을 때 각자 애니메이션이 있을 텐데") is drawn with it: the right arm plays that clip from the eyes first, over the stroke's
/// draw or a paired clip's arm, quicker than the melee slot plays it (a takedown is quick) and quicker still when the
/// blade must go in sooner, and hands the arm back before the strike, so the stab is always the stroke's or the pair's.
/// Every PC starts it on the Draw word and times its hand-over on the Stab word, as the stroke itself.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Execution + 5)]
public sealed class ExecutionKnife : MonoBehaviour
{
    /// <summary>The stroke's steps as they go between PCs.</summary>
    public enum Phase : byte { None = 0, Draw = 1, Stab = 2, Away = 3 }

    /// <summary>Where the stroke aims: the victim's neck (the blade) and head (the left hand), as measured when it began.</summary>
    public struct Aim
    {
        public bool Has;
        public Vector3 Neck, Head;
    }

    /// <summary>On the executing player's own PC: the stroke went on to this step, aimed at this neck and head.</summary>
    public event Action<Phase, Vector3, Vector3> Phased;

    // A drawn knife waiting for its stab puts itself away after this long (s), should the word that the dash ended be lost.
    private const float DrawTimeout = 2f;
    // A stroke cut short before it is pulled out blends from where the arms are to its pulled-out moment over this (s).
    private const float CutBlend = .12f;
    // The knife never holds the hands longer than this (s): the gun comes back whatever went wrong.
    private const float MostHeld = 3f;
    // The sounds play at the hand: this high on the body when there is no hand to find (m).
    private const float SoundHeight = 1.35f;
    // A knife's own draw plays this much faster than its clip (the melee slot plays it as it is: a takedown is quicker), and
    // up to this much faster when the blade must go in sooner; past that it is cut.
    private const float DrawPace = 1.8f, DrawMostPace = 3f;
    // It gives the right arm back over this long (s), done this long before the blade goes in (s), and takes it from the
    // body's own pose over this long (s).
    private const float DrawHandOver = .15f, DrawBeforeStrike = .18f, DrawEaseIn = .08f;

    private PlayerMovement movement;
    private PlayerExecution execution;
    private ExecutionKnifeArm arm;
    private ClassKnives.Knife knife;
    private KnifeStroke stroke;
    private Phase phase;
    private Aim aim;
    private KnifeStroke.Key cutFrom, last;
    // Each hand's finger joints as last shown, and where a cut-short stroke blends them from.
    private readonly FingerWays lastRightFingers = new FingerWays(), lastLeftFingers = new FingerWays(), cutRightFingers = new FingerWays(),
        cutLeftFingers = new FingerWays();
    private float clock, sincePhase, sinceStab, stabFrom, rate = 1f, impact = .35f, held, cutLeft;
    // The stroke's pace after the strike: its own (1), or what spreads the rest of it over a paired clip's rest.
    private float afterRate = 1f;
    private bool playing, cutting, stabSounded, sheathSounded, hasLast;
    private int nextArmTry;
    // The knife's own draw (null without one): where in its clip, how fast, and since the stroke began, when it has given
    // the arm back (s).
    private KnifeSwing drawClip;
    private float drawClock, drawPace = DrawPace, sinceBegin, drawEnd = -1f;

    /// <summary>The player's knife, made on them if it is not there yet (PlayerExecution and PlayerNetworkCombat both ask).</summary>
    public static ExecutionKnife On(GameObject player)
    {
        if (player == null) return null;
        return player.TryGetComponent(out ExecutionKnife knife) ? knife : player.AddComponent<ExecutionKnife>();
    }

    /// <summary>A knife stroke has this player's hands: their gun is put away until it is over (WeaponController).</summary>
    public static bool HoldsHands(PlayerMovement who) => who != null && who.TryGetComponent(out ExecutionKnife knife) && knife.playing;

    /// <summary>
    /// Something else poses the arms through this execution (a paired execution clip on the body): the knife still rides in
    /// the right fist as the stroke holds it, but ExecutionKnifeArm leaves the arms to that clip. Set it on every PC that
    /// plays the clip, for as long as it plays; the stroke's end clears it.
    /// </summary>
    public bool ArmsPosedElsewhere { get; set; }

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        execution = GetComponent<PlayerExecution>();
        // The same knife in the melee slot, in place of the fists (MeleeKnife), on every PC's copy of the player too.
        if (Application.isPlaying) MeleeKnife.On(gameObject);
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
    }

    // The execution's own events come on the PC that runs it: the player's own. The host's copy of someone else's raises
    // Ended too (when their word that it ended arrives), and that copy follows its owner's Show instead.
    private void OnStarted(MeleeCombatTarget victim)
    {
        if (movement != null && !movement.IsLocal) return;
        Begin(Measure(victim));
        if (playing) Phased?.Invoke(Phase.Draw, aim.Neck, aim.Head);
    }

    private void OnEnded()
    {
        if (movement != null && !movement.IsLocal) return;
        if (!playing) return;
        if (phase != Phase.Away) Go(Phase.Away);
        Phased?.Invoke(Phase.Away, aim.Neck, aim.Head);
    }

    /// <summary>On every other PC: the owner's stroke went on to this step, aimed there (PlayerNetworkCombat).</summary>
    internal void Show(Phase next, Vector3 neck, Vector3 head)
    {
        var told = new Aim { Has = neck != Vector3.zero || head != Vector3.zero, Neck = neck, Head = head };
        switch (next)
        {
            case Phase.Draw:
                Begin(told);
                break;
            case Phase.Stab:
                // A stab whose draw was lost begins there.
                if (!playing) Begin(told);
                if (!playing) return;
                aim = told;
                if (phase == Phase.Draw) Go(Phase.Stab);
                break;
            case Phase.Away:
                if (playing && phase != Phase.Away) Go(Phase.Away);
                break;
        }
    }

    /// <summary>The arms now, where the stroke aims and the knife in hand (ExecutionKnifeArm, each frame); false when none plays.</summary>
    internal bool Current(out KnifeStroke.Key key, out Aim at, out ClassKnives.Knife inHand, FingerWays right = null,
        FingerWays left = null)
    {
        key = default;
        at = aim;
        inHand = knife;
        if (!playing || stroke == null || !stroke.Sample(clock, out key)) return false;
        // Each hand's finger joints as the stroke has them (FingerWays; none in a stroke drawn by curls).
        stroke.SampleFingers(clock, lastRightFingers, lastLeftFingers);
        if (cutting && hasLast)
        {
            float share = 1f - Mathf.Clamp01(cutLeft / CutBlend);
            key = KnifeStroke.Blend(cutFrom, key, share);
            FingerWays.Blend(cutRightFingers, lastRightFingers, share, lastRightFingers);
            FingerWays.Blend(cutLeftFingers, lastLeftFingers, share, lastLeftFingers);
        }
        last = key;
        hasLast = true;
        right?.CopyFrom(lastRightFingers);
        left?.CopyFrom(lastLeftFingers);
        return true;
    }

    /// <summary>
    /// The knife's own draw now (its row's drawClip, the right arm from the eyes as the melee slot plays it) and how much of
    /// the right arm it has: all of it once eased in, none by the time it has handed the arm back (ExecutionKnifeArm, each
    /// frame, after the stroke or a paired clip posed the arms). False without a draw of its own or once it is over.
    /// </summary>
    internal bool DrawCurrent(out KnifeSwing.Key key, out float weight, FingerWays fingers = null)
    {
        key = default;
        weight = 0f;
        if (fingers != null) fingers.Has = false;
        if (!playing || drawClip == null || sinceBegin >= drawEnd || !drawClip.Sample(drawClock, out key)) return false;
        drawClip.SampleFingers(drawClock, fingers);
        weight = Mathf.Clamp01((drawEnd - sinceBegin) / DrawHandOver) * Mathf.Clamp01(sinceBegin / DrawEaseIn);
        return weight > 0f;
    }

    private void Update()
    {
        // Its knife put on before the first execution, so the first draw never waits on loading its model.
        if (arm == null && Time.frameCount >= nextArmTry)
        {
            nextArmTry = Time.frameCount + 30;
            arm = ExecutionKnifeArm.Of(movement);
            if (arm != null) arm.Wear(ClassKnives.Of(ClassKnives.ClassOf(movement)));
        }
        if (!playing) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        // The player's own PC: the dash in is over (or there was none), so the stab goes now.
        if (phase == Phase.Draw && movement != null && movement.IsLocal && execution != null && execution.IsExecuting && !execution.IsDashing)
        {
            // From where the dash left the player: the victim may have turned or sunk.
            if (execution.Victim != null) aim = Measure(execution.Victim);
            Go(Phase.Stab);
            Phased?.Invoke(Phase.Stab, aim.Neck, aim.Head);
        }
        Advance(dt);
    }

    // Puts the class's knife in hand and starts the stroke at its draw; with the last stroke's knife still in hand (being
    // put away, not yet at the sheath) it starts drawn instead, the arms eased from where they are.
    private void Begin(Aim at)
    {
        bool inHand = playing && stroke != null && knife != null && phase == Phase.Away && clock < stroke.Sheath && hasLast;
        var from = last;
        knife = ClassKnives.Of(ClassKnives.ClassOf(movement));
        var next = KnifeStroke.Of(knife.Motion);
        inHand &= next == stroke;
        stroke = next;
        if (stroke == null) { playing = false; return; }
        if (arm == null) arm = ExecutionKnifeArm.Of(movement);
        if (arm != null) arm.Wear(knife);
        impact = execution != null ? execution.ImpactTime : .35f;
        afterRate = 1f;
        aim = at;
        playing = true;
        phase = Phase.Draw;
        sincePhase = held = 0f;
        stabSounded = sheathSounded = false;
        drawClock = sinceBegin = 0f;
        drawPace = DrawPace;
        if (inHand)
        {
            clock = stroke.Ready;
            cutFrom = from;
            cutRightFingers.CopyFrom(lastRightFingers);
            cutLeftFingers.CopyFrom(lastLeftFingers);
            cutting = true;
            cutLeft = CutBlend;
            drawClip = null;
            drawEnd = -1f;
            return;
        }
        clock = 0f;
        cutting = hasLast = false;
        // Its own draw first, when its row names one; it is timed to give the arm back once the stab's timing is known.
        drawClip = knife.OwnDraw ? KnifeSwing.Of(knife.DrawClip) : null;
        drawEnd = drawClip != null ? drawClip.Length / drawPace : -1f;
        Sound(knife.DrawSound);
    }

    private void Go(Phase next)
    {
        if (!playing || stroke == null) return;
        if (next == Phase.Stab)
        {
            // The strike lands impactTime from now: from wherever the draw got to, at its own pace if that is enough,
            // else as much faster as it needs; a short way first waits where it is.
            stabFrom = clock;
            sinceStab = 0f;
            // Read now, not when the draw began: whatever times this execution (a paired clip) has had its say by now.
            if (execution != null) impact = execution.ImpactTime;
            float lead = Mathf.Max(0f, stroke.Strike - stabFrom);
            rate = lead > impact ? lead / Mathf.Max(.01f, impact) : 1f;
            // After the strike the stroke (the pull out, the putting away, and with its end the gun's return) keeps the
            // execution's pace: a takedown played fast lets the player go right after its kill, and its knife goes
            // away as fast behind a player who already walks on. Where a paired clip keeps the player longer than that
            // (ExecutionPairPlayer, an execution that takes its time), it fills what is left of the execution instead,
            // so nothing ends before its clip does and the gun comes back as the player is let go.
            float pace = execution != null ? Mathf.Max(.1f, execution.Pace) : 1f;
            float rest = stroke.Length - stroke.Strike;
            float kept = ArmsPosedElsewhere && execution != null ? execution.Duration - impact : 0f;
            afterRate = kept * pace > rest ? Mathf.Max(.25f, rest / kept) : pace;
            FitDraw();
        }
        else if (next == Phase.Away && clock < stroke.Out)
        {
            // Cut short before it was pulled out (the victim lost, the player hit): from the arms as they are to the
            // stroke's pulled-out moment, and on from there; a draw of its own still going lets go of the arm now.
            if (hasLast)
            {
                cutFrom = last;
                cutRightFingers.CopyFrom(lastRightFingers);
                cutLeftFingers.CopyFrom(lastLeftFingers);
                cutting = true;
                cutLeft = CutBlend;
            }
            clock = stroke.Out;
            if (drawEnd > sinceBegin + DrawHandOver) drawEnd = sinceBegin + DrawHandOver;
        }
        phase = next;
        sincePhase = 0f;
    }

    // The knife's own draw gives the arm back DrawBeforeStrike before the blade goes in (impact from now): the rest of its
    // clip as much faster as that needs, up to DrawMostPace; cut where even that is not enough, still handed over eased.
    private void FitDraw()
    {
        if (drawClip == null || sinceBegin >= drawEnd) return;
        float latest = Mathf.Max(sinceBegin + impact - DrawBeforeStrike, sinceBegin + DrawHandOver * .5f);
        float left = drawClip.Length - drawClock;
        if (sinceBegin + left / drawPace <= latest) return;
        float room = latest - sinceBegin;
        drawPace = Mathf.Clamp(left / Mathf.Max(.01f, room), drawPace, DrawMostPace);
        drawEnd = Mathf.Min(sinceBegin + left / drawPace, latest);
    }

    private void Advance(float dt)
    {
        held += dt;
        sincePhase += dt;
        sinceBegin += dt;
        if (drawClip != null) drawClock = Mathf.Min(drawClip.Length, drawClock + dt * drawPace);
        float before = clock;
        switch (phase)
        {
            case Phase.Draw:
                clock = Mathf.Min(stroke.Ready, clock + dt);
                if (sincePhase > DrawTimeout) Go(Phase.Away);
                break;
            case Phase.Stab:
                sinceStab += dt;
                clock = sinceStab < impact
                    ? Mathf.Max(stabFrom, stroke.Strike - (impact - sinceStab) * rate)
                    : stroke.Strike + (sinceStab - impact) * afterRate;
                break;
            default:
                clock += dt;
                break;
        }
        if (cutting && (cutLeft -= dt) <= 0f) cutting = false;
        if (!stabSounded && phase != Phase.Away && before < stroke.Strike && clock >= stroke.Strike)
        {
            stabSounded = true;
            Sound(knife.StabSound);
        }
        if (!sheathSounded && clock >= stroke.Sheath)
        {
            sheathSounded = true;
            Sound(knife.SheathSound);
        }
        if (clock >= stroke.Length || held > MostHeld) Stop();
    }

    private void Stop()
    {
        playing = cutting = hasLast = false;
        ArmsPosedElsewhere = false;
        phase = Phase.None;
        clock = 0f;
        drawClip = null;
    }

    private void Sound(string cue)
    {
        if (string.IsNullOrEmpty(cue)) return;
        BodyBreakSounds.Play(cue, transform.position + Vector3.up * SoundHeight);
    }

    // The victim's neck and head as its body has them now (a humanoid's bones), else from its body's bounds.
    private static Aim Measure(MeleeCombatTarget victim)
    {
        var at = new Aim();
        if (victim == null) return at;
        var animator = victim.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (neck != null && head != null)
            {
                at.Has = true;
                at.Neck = neck.position;
                at.Head = head.position;
                return at;
            }
        }
        var body = victim.GetComponentInChildren<Collider>();
        if (body == null) return at;
        var bounds = body.bounds;
        at.Has = true;
        at.Head = new Vector3(bounds.center.x, bounds.max.y - .12f, bounds.center.z);
        at.Neck = at.Head + Vector3.down * .18f;
        return at;
    }
}
