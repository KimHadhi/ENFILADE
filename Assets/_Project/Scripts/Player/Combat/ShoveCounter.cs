using UnityEngine;

/// <summary>
/// Stage G&amp;B-3, at the user's word: a shambler's grab and a runner's leap aren't parried; a shove at the right moment
/// COUNTERS them, as big and sweet as a Guilty Gear Strive counter hit, and pays well. Two ways in, both judged on this
/// player's own PC:
/// - their shove (ShoveSweep, guns or fists) reaches a zombie in the last `window` seconds of a grab's or a leap's windup
///   (anyone's: shoving a shambler off a teammate counts);
/// - a grab or a leap lands on them within `grace` seconds of their shove, them facing it (the connection, or the shove
///   falling just short of a runner still in the air, can't rob them of it).
/// The reload kick (ShoveMoves) counts as a shove for both, from the press until `grace` after its foot lands.
/// The return: a grabbing shambler's posture breaks (it reels, and the execution is there for the taking); a runner is
/// slammed down out of the air for pounceKnockdown seconds, losing pounceDamageShare of its health. And the shove is
/// handed straight back (user, 2026-11-10): no wait and no fatigue left, for the gun's shove, the fists' and the reload
/// kick alike, so the next one of a pack can be met at once. The show: time catches
/// and then crawls, the view takes a heavy knock, the screen burns red, the posture-break burst goes off where they met
/// (ParryFeedback), the break cue rings out and COUNTER slams across the screen (CounterBanner). Other PCs see the burst
/// and hear it there (EnemyNetwork.ShareParry). Numbers: Tables/Player.csv, row Counter.
/// The banner has grades (user, 2026-11-10: "카운터가 강화되면 더 이펙트가 화려해져도 될 것 같은"): a lone counter shows
/// baseGrade, and each counter landed within chainSeconds of the last shows one more, so meeting a pack one after
/// another builds up to the loudest. A skill that strengthens the counter has only to raise BaseGrade.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShoveCounter : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Min(0f), Tooltip("A shove reaching a zombie in this last part of its grab's or leap's windup counters it (s).")]
    private float window = .3f;
    [SerializeField, Min(0f), Tooltip("A grab or a leap landing this soon after their shove, them facing it, is countered too (s).")]
    private float grace = .2f;
    [SerializeField, Range(0f, 180f), Tooltip("Facing within this of the attacker as they shoved (degrees either side).")]
    private float graceAngle = 70f;
    [Header("Return")]
    [SerializeField, Min(0f), Tooltip("A countered runner is slammed down for this long (s).")]
    private float pounceKnockdown = 2.5f;
    [SerializeField, Range(0f, 1f), Tooltip("A countered runner loses this share of its health (1: dead at once, friend feedback C6).")]
    private float pounceDamageShare = 1f;
    [Header("Feel")]
    [SerializeField, Min(0f), Tooltip("Time catches on contact (real s).")]
    private float hitStopSeconds = .22f;
    [SerializeField, Range(.01f, 1f)] private float hitStopScale = .03f;
    [SerializeField, Min(0f), Tooltip("Then crawls this long (real s), the counter's afterglow.")]
    private float slowSeconds = .55f;
    [SerializeField, Range(.01f, 1f)] private float slowScale = .3f;
    [SerializeField, Tooltip("View knock (degrees; up;away from the attacker;roll away).")]
    private Vector3 cameraKick = new Vector3(4f, 1.5f, 6f);
    [SerializeField, Tooltip("Brief zoom (degrees of FOV). Negative pushes in.")]
    private float cameraFov = -7f;
    [SerializeField, Range(0f, 1f), Tooltip("Decaying shake.")]
    private float cameraTrauma = .35f;
    [SerializeField, Min(0f), Tooltip("The posture-break burst's size against a parry's that breaks posture.")]
    private float burstGrowth = 1.6f;
    [SerializeField, Min(0f), Tooltip("Contact point in front of the eyes, towards the attacker (m).")]
    private float contactDistance = .7f;
    [SerializeField, Min(.2f), Tooltip("COUNTER on screen, in and out (real s).")]
    private float bannerSeconds = .85f;
    [SerializeField, Range(0, CounterBanner.Grades - 1), Tooltip("The banner's grade for a lone counter (0: the plain slash). A skill that strengthens the counter raises this.")]
    private int baseGrade;
    [SerializeField, Min(0f), Tooltip("A counter this soon after the last one (real s) shows one grade more than it did, up to the top. 0: never.")]
    private float chainSeconds = 8f;

    private PlayerMovement player;
    private Transform view;
    private CombatTimeEffects timeEffects;
    private CounterBanner banner;
    private float lastShove = float.NegativeInfinity, lastCounter = float.NegativeInfinity;
    private Vector3 shoveForward;
    private int chain;

    /// <summary>The banner's grade for a lone counter, for a skill that strengthens the counter to raise.</summary>
    public int BaseGrade { get => baseGrade; set => baseGrade = Mathf.Clamp(value, 0, CounterBanner.Grades - 1); }

    // The afterglow's own claim on time, apart from the hit stop's (CombatTimeEffects keeps one per owner).
    private Object slowOwner;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        slowOwner = this.transform;
    }

    /// <summary>A shove began (ShoveSweep): a grab or a leap landing just after it is countered.</summary>
    internal static void Shoved(PlayerMovement shover, Vector3 forward)
    {
        if (shover == null || !shover.TryGetComponent(out ShoveCounter counter)) return;
        counter.lastShove = Time.time;
        counter.shoveForward = Vector3.ProjectOnPlane(forward, Vector3.up);
    }

    /// <summary>
    /// Their shove reached this enemy (ShoveSweep): true when that was a counter, which takes the shove's place.
    /// </summary>
    internal static bool TryShoveCounter(PlayerMovement shover, MeleeCombatTarget target, Vector3 point)
    {
        if (shover == null || target == null || !shover.TryGetComponent(out ShoveCounter counter)) return false;
        var combat = target.GetComponent<EnemyCombatAI>();
        if (combat == null || !(combat.IsGrabbing || combat.IsPouncing)) return false;
        if (combat.Windup - combat.WindupElapsed > counter.window) return false;
        counter.Land(target, combat.IsPouncing, point);
        return true;
    }

    /// <summary>
    /// A grab or a leap reached them (PlayerDamageReceiver, before it takes hold): true when their shove just before
    /// counters it.
    /// </summary>
    internal bool TryHitCounter(in DamageHit hit)
    {
        if (!Counters(hit, out var target)) return false;
        Land(target, hit.Method == DamageMethod.Pounce, target.transform.position + Vector3.up);
        return true;
    }

    /// <summary>
    /// Whether a grab or a leap reaching them now would be countered, without doing it: a runner's leap that lands just
    /// short of catching them still asks, so the counter stays as forgiving as before the catch was tightened (EnemyCombatAI).
    /// </summary>
    internal bool WouldCounter(in DamageHit hit) => Counters(hit, out _);

    private bool Counters(in DamageHit hit, out MeleeCombatTarget target)
    {
        target = null;
        if (hit.Method != DamageMethod.Grab && hit.Method != DamageMethod.Pounce) return false;
        if (Time.time - lastShove > grace || hit.Source == null) return false;
        target = hit.Source.GetComponentInParent<MeleeCombatTarget>();
        if (target == null || !target.IsAvailable) return false;
        Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
        return !(to.sqrMagnitude > 1e-4f && shoveForward.sqrMagnitude > 1e-4f && Vector3.Angle(shoveForward, to) > graceAngle);
    }

    private void Land(MeleeCombatTarget target, bool airborne, Vector3 point)
    {
        lastShove = float.NegativeInfinity;
        // The shove comes straight back, whichever V it was.
        if (player != null)
        {
            player.Abilities?.Combat.RefreshShove();
            player.GetComponentInChildren<PlayerMeleeCombat>()?.RefreshShove();
            if (player.TryGetComponent(out ShoveMoves moves)) moves.RefreshKick();
        }
        Vector3 away = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
        away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        // Its grab or leap ends here and now; the host's ends with the stagger or the fall it hears of.
        target.GetComponent<EnemyCombatAI>()?.CancelAttack();
        if (airborne)
        {
            var health = target.GetComponent<EnemyHealth>();
            // At 1 it takes whatever is left, which on the host may be more than this PC's copy shows.
            if (health != null && pounceDamageShare > 0f)
                health.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),
                    pounceDamageShare >= 1f ? Mathf.Max(health.Health, health.MaximumHealth) * 2f : health.MaximumHealth * pounceDamageShare, DamageKind.Attack,
                    point, away, gameObject, false, 0f, true, isCounter: true, method: DamageMethod.Shove));
            if (target.IsAvailable) target.KnockDown(away, pounceKnockdown);
        }
        else target.ApplyPostureDamage(1e5f, 0f, true);
        Show(target, away);
    }

    private void Show(MeleeCombatTarget target, Vector3 away)
    {
        if (view == null)
        {
            var camera = GetComponentInChildren<Camera>();
            view = camera != null ? camera.transform : null;
        }
        if (timeEffects == null) timeEffects = GetComponent<CombatTimeEffects>() ?? gameObject.AddComponent<CombatTimeEffects>();
        if (hitStopSeconds > 0f) timeEffects.RequestSlowdown(this, hitStopScale, hitStopSeconds, target);
        if (slowSeconds > 0f) timeEffects.RequestSlowdown(slowOwner, slowScale, hitStopSeconds + slowSeconds, target);

        Vector3 toAttacker = view != null ? target.transform.position + Vector3.up * 1.2f - view.position : away;
        toAttacker = toAttacker.sqrMagnitude > 1e-4f ? toAttacker.normalized : away;
        float sign = view != null && Vector3.Dot(view.right, toAttacker) < 0f ? -1f : 1f;
        if (view != null)
        {
            var punch = CameraViewPunch.For(view.GetComponent<Camera>());
            punch?.ImpactPunch(new Vector3(cameraKick.x, -sign * cameraKick.y, -sign * cameraKick.z), cameraFov, cameraTrauma);
            Vector3 contact = view.position + Vector3.Slerp(view.forward, toAttacker, .6f).normalized * contactDistance - view.up * .05f;
            GetComponent<ParryFeedback>()?.CounterBurst(contact, (toAttacker * .35f + view.right * (sign * .8f) + view.up * .5f).normalized, burstGrowth);
            if (target.TryGetComponent(out EnemyNetwork network)) network.ShareParry(contact, away, true);
        }
        KillConfirmEffect.Parry(new ParryScreenLook
        {
            // Drained near-white with a deep pink edge (the HUD's colours, with the banner's slash), held through the hit stop
            // and into the crawl. It was burning red before the banner took the HUD's look (2026-11-09).
            Flash = .7f, Chromatic = .9f, Lens = -.3f, Saturation = -45f, Contrast = 22f, Bloom = 1.8f, Vignette = .42f,
            Hold = hitStopSeconds + slowSeconds * .5f, Fade = .5f, Filter = new Color(1f, .93f, .96f), VignetteColor = new Color(.3f, .02f, .1f)
        });
        CombatCueAudio.Play(gameObject, CombatCue.ParryBreak, .82f);
        HitFlashRenderer.For(target)?.WhiteFlash(.14f);
        ShowBanner(NextGrade());
    }

    // The grade of a counter landing now: the base one, and one more for each counter chained before it. Real time, since
    // the counter itself slows the game's.
    private int NextGrade()
    {
        chain = chainSeconds > 0f && Time.unscaledTime - lastCounter <= chainSeconds ? chain + 1 : 0;
        lastCounter = Time.unscaledTime;
        return baseGrade + chain;
    }

    private void ShowBanner(int grade)
    {
        if (banner == null) banner = gameObject.AddComponent<CounterBanner>();
        banner.Play(bannerSeconds, grade);
    }

    /// <summary>The banner alone at this grade, nothing countered: for looking at the grades (console, /counter).</summary>
    public void PreviewBanner(int grade) => ShowBanner(grade);
}
