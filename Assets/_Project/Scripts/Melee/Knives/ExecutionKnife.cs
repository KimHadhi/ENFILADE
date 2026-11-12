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
/// begins, as the execution's hit does, never slower than the stroke was drawn; after it, the stroke runs at its own pace,
/// or, when a paired execution clip has the arms (ArmsPosedElsewhere as the stab begins), over what is left of the
/// execution (PlayerExecution.Duration), so the knife is put away and the gun comes back as the pair ends, not while its
/// clip is still getting the player up.
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

    private PlayerMovement movement;
    private PlayerExecution execution;
    private ExecutionKnifeArm arm;
    private ClassKnives.Knife knife;
    private KnifeStroke stroke;
    private Phase phase;
    private Aim aim;
    private KnifeStroke.Key cutFrom, last;
    private float clock, sincePhase, sinceStab, stabFrom, rate = 1f, impact = .35f, held, cutLeft;
    // The stroke's pace after the strike: its own (1), or what spreads the rest of it over a paired clip's rest.
    private float afterRate = 1f;
    private bool playing, cutting, stabSounded, sheathSounded, hasLast;
    private int nextArmTry;

    /// <summary>The player's knife, made on them if it is not there yet (PlayerExecution and PlayerNetworkCombat both ask).</summary>
    public static ExecutionKnife On(GameObject player)
    {
        if (player == null) return null;
        return player.TryGetComponent(out ExecutionKnife knife) ? knife : player.AddComponent<ExecutionKnife>();
    }

    /// <summary>A knife stroke has this player's hands: their gun is put away until it is over (WeaponController).</summary>
    public static bool HoldsHands(PlayerMovement who) => who != null && who.TryGetComponent(out ExecutionKnife knife) && knife.playing;

    /// <summary>A stroke is playing on this PC's copy of the player.</summary>
    public bool IsOut => playing;

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
    internal bool Current(out KnifeStroke.Key key, out Aim at, out ClassKnives.Knife inHand)
    {
        key = default;
        at = aim;
        inHand = knife;
        if (!playing || stroke == null || !stroke.Sample(clock, out key)) return false;
        if (cutting && hasLast) key = KnifeStroke.Blend(cutFrom, key, 1f - Mathf.Clamp01(cutLeft / CutBlend));
        last = key;
        hasLast = true;
        return true;
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

    // Puts the class's knife in hand and starts the stroke at its draw.
    private void Begin(Aim at)
    {
        knife = ClassKnives.Of(ClassKnives.ClassOf(movement));
        stroke = KnifeStroke.Of(knife.Motion);
        if (stroke == null) { playing = false; return; }
        if (arm == null) arm = ExecutionKnifeArm.Of(movement);
        if (arm != null) arm.Wear(knife);
        impact = execution != null ? execution.ImpactTime : .35f;
        afterRate = 1f;
        aim = at;
        playing = true;
        phase = Phase.Draw;
        clock = sincePhase = held = 0f;
        cutting = hasLast = stabSounded = sheathSounded = false;
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
            // A paired clip has the arms (ExecutionPairPlayer): the pull out, the putting away and the gun's return
            // fill what is left of the execution, which ends with the pair, so nothing ends before its clip does.
            float after = ArmsPosedElsewhere && execution != null ? execution.Duration - impact : -1f;
            afterRate = after > .05f ? Mathf.Clamp((stroke.Length - stroke.Strike) / after, .25f, 4f) : 1f;
        }
        else if (next == Phase.Away && clock < stroke.Out)
        {
            // Cut short before it was pulled out (the victim lost, the player hit): from the arms as they are to the
            // stroke's pulled-out moment, and on from there.
            if (hasLast)
            {
                cutFrom = last;
                cutting = true;
                cutLeft = CutBlend;
            }
            clock = stroke.Out;
        }
        phase = next;
        sincePhase = 0f;
    }

    private void Advance(float dt)
    {
        held += dt;
        sincePhase += dt;
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
