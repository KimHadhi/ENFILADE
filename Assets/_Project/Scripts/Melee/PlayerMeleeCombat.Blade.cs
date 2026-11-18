using UnityEngine;

/// <summary>
/// The swing's blade (PlayerMeleeCombat's part for it): what a swing reaches and cuts is the knife itself, as the arm
/// shows it on the True FPS body (the user, 2026-11-18: "공격 범위 및 히트 판정은 별도 박스 판정이 아닌 True FPS 1인칭
/// 화면에 보이는 실제 무기 모델 자체를 기준으로 처리").
/// - The cut. While the swing's clip is inside its blade marks, the blade's own line (its start to its tip, from the
///   knife model's markers: KnifeSeat) cuts what it passes through (BladeTrace). The arm is posed late in the frame,
///   after the hands and the gun, so it tells this where the blade is then (BladePosed); the cut runs from where the
///   last frame showed the blade to where this one does, both taken in the eyes' own frame and the eyes' turn between
///   the two frames with them, in steps short enough that nothing slips between (a flick of the view drags the blade
///   through its arc, as it looks). Only the part of the frame the clip spent inside its marks is cut, so the same swing
///   cuts the same at any frame rate.
/// - The step in and the reach in. A knife is short: its blade gets 0.4 to 1 m ahead of the eyes (measured from the
///   clips: KnifeSeat.Reach), and a zombie stops 1.6 m off. So a swing is aimed: it leaves at whoever stands under the
///   crosshair (a combo's strike at whom it names), the body steps in toward them until the blade can pass a little
///   into their body (never further than the knife's own lunge, ClassKnives.csv, never into them), and the arm reaches
///   the last few centimetres (the wrist goes that much further ahead through the blade's marks, MeleeKnife). The step
///   begins with the wind-up, the arm drawn back as the body goes, so the body stands there when the swing leaves, and
///   the swing aims again as it leaves (a charge may have been held a while). Nothing but the visible blade ever hits:
///   the step and the reach only bring it there. With nobody under the crosshair the swing is thrown where the player
///   stands.
/// A combo's strike aimed at someone (a riposte, a chase, a finisher) still lands on them as its blade goes live, the
/// sure hit a press made in time earns, and its blade cuts whoever else it passes.
/// Until then the hit was a sphere cast out of the eyes (1.6 m and the knife's extra reach, 0.22 m wide) with a fixed
/// 12 cm nudge forward, whatever the arm showed.
/// </summary>
public sealed partial class PlayerMeleeCombat
{
    [Header("Blade")]
    [SerializeField, Min(.05f), Tooltip("A swing is aimed at whoever stands this near the crosshair's line (m, the radius of the look's sweep).")]
    private float aimRadius = .25f;
    [SerializeField, Min(0f), Tooltip("The body steps in until the blade can pass this far into the other's body (m, past the surface of their capsule).")]
    private float bite = .25f;
    [SerializeField, Min(0f), Tooltip("The arm may reach this much further ahead than its clip to bring the blade that deep (m).")]
    private float reachIn = .15f;

    // The step in stops this near (m, from the middle of the player's capsule to the other's surface: BodyGap; the two
    // capsules about touching) and takes no less and no more than these (s); the cut between two frames goes in steps no
    // longer than BladeStep at the tip (m), no more than MostBladeSteps of them.
    private const float StepGap = .32f, StepLeast = .07f, StepMost = .2f, BladeStep = .05f;
    private const int MostBladeSteps = 24;

    private readonly BladeTrace blade = new BladeTrace();
    private readonly RaycastHit[] aimed = new RaycastHit[24];
    // The step in under way: toward whom, how much is left of it (m) and how fast (m/s).
    private MeleeCombatTarget stepAt;
    private Collider stepBody;
    private float stepLeft, stepSpeed;
    // The blade as the arm last showed it, in the eyes' own frame, and the eyes then: this frame's cut starts there.
    private Vector3 shownStart, shownTip, shownEyes;
    private Quaternion shownLook = Quaternion.identity;
    private int shownFrame = -9;
    // The stretch of the clip this frame's step ran through with the blade live, out of the whole stretch it ran
    // through, kept until the arm has been posed.
    private float liveFrom, liveTo, ranFrom, ranTo;
    private bool livePending;

    /// <summary>
    /// How much further ahead than its clip the arm reaches through the blade's marks of the swing under way (m), to
    /// bring the blade into the body it is aimed at; the same on every PC (the owner's PC sends it with the swing).
    /// </summary>
    public float SwingReachIn { get; private set; }
    /// <summary>The marks of the swing under way (MeleeKnife eases the reach in by them).</summary>
    public SwingMarks SwingMarks => swing.Marks;

    // A wind-up began (the player's own PC): the body already goes at whoever the swing will be thrown at, aimed by the
    // light swing that is coming, to stand where its blade reaches by the time the wind-up is over.
    private void AimWindup()
    {
        if (knifeArm == null && player != null) knifeArm = player.GetComponent<MeleeKnife>();
        if (knifeArm == null) return;
        AimSwing(null, knifeArm.Coming, MeleeKnife.Held(player), 0f, swing.Windup / Mathf.Max(.01f, swing.Speed));
    }

    // A swing is aimed (the player's own PC), as its wind-up begins and again as it leaves: at the one a combo names,
    // else at whoever stands under the crosshair within what the blade, the step in and the arm can reach together.
    // The step is what is missing for the blade to pass `bite` into their body, taken within the time given (s), and
    // the arm's reach what the step leaves over.
    private void AimSwing(MeleeCombatTarget given, KnifeSwing clip, ClassKnives.Knife knife, float further, float within)
    {
        stepAt = null; stepBody = null; stepLeft = 0f; SwingReachIn = 0f;
        if (clip == null || !clip.Cuts || knifeArm == null || aim == null) return;
        float bladeReach = knifeArm.Reach(clip);
        float lunge = player.IsGrounded ? knife.Lunge + further : 0f;
        Collider body;
        Vector3 point;
        if (given != null)
        {
            body = given.GetComponentInChildren<Collider>();
            if (body == null || !given.IsAvailable) return;
            point = body.ClosestPoint(aim.position);
        }
        else if (!AimedBody(bladeReach + lunge + reachIn, out given, out body, out point)) return;
        Approach(Vector3.Distance(aim.position, point), BodyGap.Across(player, body, out _), bladeReach, lunge, bite, reachIn,
            out float step, out float arm);
        SwingReachIn = arm;
        if (step < .01f) return;
        float time = Mathf.Clamp(within, StepLeast, StepMost);
        stepAt = given; stepBody = body; stepLeft = step; stepSpeed = step / time;
    }

    /// <summary>
    /// How a swing goes to meet a body (the one working of it, the game's and KnifeSwingReport's): the body is
    /// <paramref name="distance"/> from the eyes and <paramref name="gap"/> from the middle of the player's capsule
    /// (m, to its surface), the blade gets <paramref name="bladeReach"/> ahead of the eyes. The body steps in until the
    /// blade can pass <paramref name="bite"/> into the other, never further than the lunge and never nearer than the two
    /// capsules about touching, and the arm reaches what is still missing, up to <paramref name="reachIn"/>. Returns how
    /// far the blade then passes into the body (m; below 0: it falls that short).
    /// </summary>
    public static float Approach(float distance, float gap, float bladeReach, float lunge, float bite, float reachIn, out float step, out float arm)
    {
        float stand = Mathf.Max(0f, bladeReach - bite);
        step = Mathf.Clamp(distance - stand, 0f, Mathf.Max(0f, Mathf.Min(lunge, gap - StepGap)));
        arm = Mathf.Clamp(distance - step - stand, 0f, Mathf.Max(0f, reachIn));
        return bladeReach + arm - (distance - step);
    }

    // Whoever stands under the crosshair within range with nothing of the world between: who, the collider met and the
    // place on it.
    private bool AimedBody(float range, out MeleeCombatTarget who, out Collider body, out Vector3 point)
    {
        who = null; body = null; point = default;
        float nearest = float.PositiveInfinity;
        int count = Physics.SphereCastNonAlloc(aim.position, aimRadius, aim.forward, aimed, range, hitLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = aimed[i];
            if (hit.distance >= nearest || hit.collider.transform.IsChildOf(player.transform)) continue;
            var candidate = hit.collider.GetComponentInParent<MeleeCombatTarget>();
            if (candidate == null || !candidate.IsAvailable) continue;
            // Someone the sweep began inside comes back with no point of their own (Unity: distance 0, the zero point).
            Vector3 at = hit.distance <= 0f && hit.point == Vector3.zero ? hit.collider.ClosestPoint(aim.position) : hit.point;
            if (!HasLineOfSight(hit.collider, at)) continue;
            nearest = hit.distance; who = candidate; body = hit.collider; point = at;
        }
        return who != null;
    }

    // The step in, a frame's worth: toward where they are now, never nearer than the two bodies about touching.
    private void TickStep(float dt)
    {
        if (stepLeft <= 0f) return;
        if (!player.IsGrounded || stepAt == null || stepBody == null || !stepAt.IsAvailable || swing.Now == SwingTimeline.Phase.Idle)
        { stepLeft = 0f; return; }
        float gap = BodyGap.Across(player, stepBody, out Vector3 toward);
        float move = Mathf.Min(stepSpeed * dt, stepLeft, Mathf.Max(0f, gap - StepGap));
        if (move <= 0f) { stepLeft = 0f; return; }
        stepLeft -= move;
        player.Motor.AddDisplacement(toward * move);
    }

    // The blade of the swing under way goes live (its clip has reached the mark), once a swing, on the player's own PC.
    // A combo's strike aimed at someone lands on them here, when they are within the move's reach of the eyes with nothing
    // solid between, and the blade leaves them be; everyone else is the blade's own to cut.
    private void OpenBlade()
    {
        if (!swingJudged || bladeOpened || swing.Now != SwingTimeline.Phase.Swing || swing.ClipTime < swing.Marks.From) return;
        bladeOpened = true;
        var at = swingAt; swingAt = null;
        blade.Begin(player, aim, swingDamage, swingPosture, swingTargets, MeleeKnife.Held(player).BladeRadius, hitLayers, OnPunchLanded, OnPunchWallHit);
        if (at != null && StrikeTarget(at, swingDamage, swingPosture, swingReach)) blade.Spare(at.GetComponent<IDamageReceiver>());
    }

    // What this step ran through of the blade's marks, kept for when the arm has been posed (BladePosed, late in the frame).
    private void NoteLive()
    {
        if (!blade.Open || !swing.Live(out float from, out float to) || !swing.Stepped(out float ranA, out float ranB)) return;
        liveFrom = from; liveTo = to; ranFrom = ranA; ranTo = ranB;
        livePending = true;
    }

    /// <summary>
    /// The knife arm has put the blade where this frame shows it, from its start to its tip (ExecutionKnifeArm, after the
    /// hands and the gun): on the player's own PC the swing's cut runs from where the last frame showed it to here.
    /// </summary>
    internal void BladePosed(Vector3 start, Vector3 tip)
    {
        if (player == null || player.IsNetworkDriven || aim == null) return;
        Vector3 eyes = aim.position;
        Quaternion look = aim.rotation, back = Quaternion.Inverse(look);
        Vector3 nowStart = back * (start - eyes), nowTip = back * (tip - eyes);
        // Not shown the frame before (the knife just came out): the cut starts where it is.
        if (Time.frameCount - shownFrame > 1) { shownStart = nowStart; shownTip = nowTip; shownEyes = eyes; shownLook = look; }
        if (livePending && blade.Open)
        {
            float whole = ranTo - ranFrom;
            float first = whole > 1e-6f ? Mathf.Clamp01((liveFrom - ranFrom) / whole) : 1f, last = whole > 1e-6f ? Mathf.Clamp01((liveTo - ranFrom) / whole) : 1f;
            // How far the tip went this frame: by the arm, by the view turning and by the body moving.
            float travel = Vector3.Distance(shownTip, nowTip) + Quaternion.Angle(shownLook, look) * Mathf.Deg2Rad * nowTip.magnitude
                + Vector3.Distance(shownEyes, eyes);
            int steps = Mathf.Clamp(Mathf.CeilToInt(travel * (last - first) / BladeStep), 1, MostBladeSteps);
            for (int i = 0; i <= steps && blade.Open; i++)
            {
                float share = Mathf.Lerp(first, last, i / (float)steps);
                Quaternion turn = Quaternion.Slerp(shownLook, look, share);
                Vector3 from = Vector3.Lerp(shownEyes, eyes, share);
                blade.Pass(from + turn * Vector3.Lerp(shownStart, nowStart, share), from + turn * Vector3.Lerp(shownTip, nowTip, share));
            }
        }
        livePending = false;
        shownStart = nowStart; shownTip = nowTip; shownEyes = eyes; shownLook = look; shownFrame = Time.frameCount;
        if (blade.Open && (swing.Now != SwingTimeline.Phase.Swing || swing.ClipTime >= swing.Marks.To)) blade.End();
    }

    // A frame's live stretch nobody posed a blade for (a body with no arm to hold a knife): cut where the clip alone puts it.
    private void CutWithoutArm()
    {
        if (!livePending) return;
        if (knifeArm != null && knifeArm.BladeByClip(aim, out Vector3 start, out Vector3 tip)) BladePosed(start, tip);
        livePending = false;
    }

    // The swing's blade and its step in end here (the swing let go of, or the next one leaving).
    private void EndBlade()
    {
        blade.End();
        stepAt = null; stepBody = null;
        stepLeft = 0f;
        livePending = false;
    }
}
