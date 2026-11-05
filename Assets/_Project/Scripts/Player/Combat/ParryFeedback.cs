using UnityEngine;

/// <summary>
/// The whole show of a successful parry, timed around one moment: a hand snaps across the view and holds while time
/// catches, the view takes a knock, the screen flashes and light breaks out at the contact point, then the attacker reels
/// and everything lets go. Parries in a row climb in pitch and size, and a parry that breaks posture gets the biggest
/// version of all of it. Presentation only: posture damage and stun stay in PlayerMeleeCombat, the posture-break slow
/// motion in PlayerPerfectDefense. Numbers come from Tables/Player.csv, row Parry.
/// </summary>
[DisallowMultipleComponent]
public sealed class ParryFeedback : MonoBehaviour
{
    [Header("Hand")]
    [SerializeField, Min(.05f), Tooltip("Whole sweep: out, hold and back (real seconds).")]
    private float handSweepSeconds = .45f;
    [SerializeField, Min(.01f), Tooltip("Snapping across (real seconds).")]
    private float handSweepAttack = .04f;
    [SerializeField, Min(0f), Tooltip("Holding the pose at full reach, inside the hit stop (real seconds).")]
    private float handSweepHold = .16f;
    [SerializeField, Tooltip("Where the sweeping hand ends up, camera space towards the attacker's side (m; across;up;forward).")]
    private Vector3 handSweep = new Vector3(.2f, .02f, .42f);
    [Header("Attacker")]
    [SerializeField, Min(0f), Tooltip("Speed the attacker is pushed back at (m/s). It covers about 0.16 m per m/s.")]
    private float enemyPushSpeed = 4f;
    [SerializeField, Min(0f), Tooltip("Push when the parry broke its posture: small, so the execution stays in reach (m/s).")]
    private float breakPushSpeed = 1.5f;
    [SerializeField, Range(0f, 60f), Tooltip("How far the attacker reels back (degrees).")]
    private float enemyLeanDegrees = 20f;
    [SerializeField, Min(.05f), Tooltip("Reeling back and rocking to a stop (s).")]
    private float enemyLeanSeconds = .6f;
    [SerializeField, Range(0f, 60f), Tooltip("Reel when the parry broke its posture (degrees).")]
    private float breakLeanDegrees = 30f;
    [SerializeField, Min(.05f), Tooltip("That reel (s).")]
    private float breakLeanSeconds = 1.5f;
    [SerializeField, Min(0f), Tooltip("White flash on the attacker's body (s). 0 turns it off.")]
    private float enemyFlashSeconds = .12f;
    [Header("View")]
    [SerializeField, Tooltip("View knock (degrees; up;away from the attacker;roll away). Lands on real time, inside the hit stop.")]
    private Vector3 cameraKick = new Vector3(1.3f, .8f, 2.2f);
    [SerializeField, Tooltip("Brief zoom (degrees of FOV). Negative pushes in.")]
    private float cameraFov = -2.4f;
    [SerializeField, Range(0f, 1f), Tooltip("Decaying shake added once time lets go.")]
    private float cameraTrauma = .12f;
    [Header("Hit stop")]
    [SerializeField, Min(0f), Tooltip("Time catches this long on contact (real seconds). 0 turns it off. The feet stay planted through it (Fists.parryRootSeconds).")]
    private float hitStopSeconds = .2f;
    [SerializeField, Min(0f), Tooltip("The same when the parry broke posture (real seconds). The posture-break slow motion follows it.")]
    private float breakHitStopSeconds = .25f;
    [SerializeField, Range(.01f, 1f)] private float hitStopScale = .02f;
    [Header("Light")]
    [SerializeField, Min(.1f), Tooltip("Contact point in front of the eyes, towards the attacker (m).")]
    private float contactDistance = .55f;
    [SerializeField, Min(0f), Tooltip("Size of the whole burst.")]
    private float vfxScale = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Brightness of the flare on contact.")]
    private float flareBrightness = .7f;
    [SerializeField, Min(0f), Tooltip("Size of the hit prefab set in Feedback (Parry Hit Effect), on top of the burst size.")]
    private float hitEffectScale = .26f;
    [SerializeField, Min(0f), Tooltip("Size of the stars set in Feedback (Parry Sparkle Effect), on top of the burst size.")]
    private float sparkleEffectScale = .3f;
    [SerializeField, Min(0f), Tooltip("Size of the red slash and its ink when a parry breaks posture, on top of the burst size.")]
    private float breakEffectScale = .3f;
    [SerializeField, Min(0)] private int sparkCount = 14;
    [SerializeField, Min(0)] private int emberCount = 6;
    [SerializeField, Min(1f), Tooltip("A posture break multiplies size and counts by this.")]
    private float breakBoost = 1.6f;
    [Header("Parries in a row")]
    [SerializeField, Min(0f), Tooltip("A parry within this long of the last one continues the chain (real seconds).")]
    private float chainWindow = 3f;
    [SerializeField, Min(1)] private int chainMax = 4;
    [SerializeField, Min(0f), Tooltip("Size added per link of the chain.")]
    private float chainGrowth = .16f;
    [SerializeField, Min(0f), Tooltip("Pitch added per link of the chain (semitones).")]
    private float chainPitchSemitones = 1.5f;
    [Header("Sound")]
    [SerializeField, Range(0f, 1f), Tooltip("Movement sounds and the voice drop to this level on contact, so the hit stands alone. 1 turns it off.")]
    private float duckLevel = .35f;
    [SerializeField, Min(.01f), Tooltip("Time they take to come back (real seconds).")]
    private float duckSeconds = .4f;
    [Header("Screen")]
    [SerializeField, Min(0f), Tooltip("Exposure spike on contact (stops).")]
    private float screenFlash = .3f;
    [SerializeField, Min(0f), Tooltip("Glow around bright things during the pulse.")]
    private float screenBloom = .9f;
    [SerializeField, Range(0f, 1f)] private float screenChromatic = .5f;
    [SerializeField, Range(-1f, 0f), Tooltip("Lens squeeze on contact.")]
    private float screenLens = -.16f;
    [SerializeField, Range(-100f, 0f), Tooltip("Colour drained from the frame.")]
    private float screenSaturation = -22f;
    [SerializeField, Min(.05f), Tooltip("Pulse fading out (s).")]
    private float screenSeconds = .22f;
    [SerializeField, Min(0f)] private float breakFlash = .5f;
    [SerializeField, Min(0f)] private float breakBloom = 1.4f;
    [SerializeField, Range(0f, 1f)] private float breakChromatic = .8f;
    [SerializeField, Range(-1f, 0f)] private float breakLens = -.26f;
    [SerializeField, Range(-100f, 0f), Tooltip("Colour drained on a break. Much past -40 washes the red slash out to pink.")]
    private float breakSaturation = -32f;
    [SerializeField, Min(.05f), Tooltip("Break pulse fading out (s). The drained colour holds through the slow motion first.")]
    private float breakSeconds = .7f;

    private PlayerDamageReceiver receiver;
    private Transform view;
    private FistsView fists;
    private CombatTimeEffects timeEffects;
    private int chain;
    private float lastParryTime = float.NegativeInfinity, duckRemaining;

    /// <summary>How long a parry's hit stop holds time (real s); broken is a parry that broke posture.</summary>
    public float HitStopFor(bool broken) => broken ? breakHitStopSeconds : hitStopSeconds;

    private void OnEnable()
    {
        receiver = GetComponent<PlayerDamageReceiver>();
        var camera = GetComponentInChildren<Camera>();
        view = camera != null ? camera.transform : null;
        if (receiver != null) receiver.HitResolved += OnResolved;
    }
    private void OnDisable()
    {
        if (receiver != null) receiver.HitResolved -= OnResolved;
        duckRemaining = 0f;
        SetDuck(1f);
    }
    private void Update()
    {
        if (duckRemaining <= 0f || Time.timeScale <= 0f) return;
        duckRemaining = Mathf.Max(0f, duckRemaining - Time.unscaledDeltaTime);
        SetDuck(Mathf.Lerp(1f, duckLevel, duckRemaining / duckSeconds));
    }
    private static void SetDuck(float level)
    {
        GameAudio.SetDuck(AudioChannel.Movement, level);
        GameAudio.SetDuck(AudioChannel.Voice, level);
    }
    private void OnResolved(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Parried || view == null) return;
        var target = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
        bool broken = target != null && target.IsStaggered;
        chain = Time.unscaledTime - lastParryTime <= chainWindow ? Mathf.Min(chain + 1, chainMax) : 1;
        lastParryTime = Time.unscaledTime;
        float growth = (1f + chainGrowth * (chain - 1)) * (broken ? breakBoost : 1f);

        Vector3 toAttacker = hit.Source != null ? hit.Source.transform.position - view.position : -hit.Direction;
        toAttacker = toAttacker.sqrMagnitude > .0001f ? toAttacker.normalized : view.forward;
        float side = Vector3.Dot(view.right, toAttacker);
        float sign = side < 0f ? -1f : 1f;
        Vector3 contact = view.position + Vector3.Slerp(view.forward, toAttacker, .5f).normalized * contactDistance - view.up * .05f;

        float variance = CombatFeedbackSettings.InstanceOrDefaults.hitPitchVariance;
        CombatCueAudio.Play(gameObject, broken ? CombatCue.ParryBreak : CombatCue.Parry,
            Mathf.Pow(2f, (chain - 1) * chainPitchSemitones / 12f) * (1f + Random.Range(-variance, variance)));
        duckRemaining = duckSeconds;
        SetDuck(duckLevel);
        if (fists == null) fists = GetComponentInChildren<FistsView>();
        fists?.Deflect(side, handSweepSeconds, handSweepAttack, handSweepHold, handSweep);
        var punch = CameraViewPunch.For(view.GetComponent<Camera>());
        float knock = broken ? 1.5f : 1f;
        punch?.ImpactPunch(new Vector3(cameraKick.x, -sign * cameraKick.y, -sign * cameraKick.z) * knock, cameraFov * knock, cameraTrauma * knock);
        KillConfirmEffect.Parry(broken ? BreakLook() : ContactLook());
        Burst(contact, (toAttacker * .35f + view.right * (sign * .8f) + view.up * .5f).normalized, growth, broken);
        if (target != null) Reel(target, toAttacker, contact, broken);

        float stop = broken ? breakHitStopSeconds : hitStopSeconds;
        if (stop > 0f)
        {
            if (timeEffects == null) timeEffects = GetComponent<CombatTimeEffects>();
            timeEffects?.RequestSlowdown(this, hitStopScale, stop);
        }
        if (broken) chain = 0;
    }
    private void Burst(Vector3 point, Vector3 spray, float growth, bool broken) => ParryVfx.Play(new ParryBurst
    {
        Point = point, Right = view.right, Up = view.up, Spray = spray,
        Scale = vfxScale * growth, FlareBrightness = flareBrightness, HitEffectScale = hitEffectScale, SparkleEffectScale = sparkleEffectScale,
        BreakEffectScale = breakEffectScale, Break = broken,
        Sparks = Mathf.RoundToInt(sparkCount * growth), Embers = Mathf.RoundToInt(emberCount * growth)
    });
    private void Reel(MeleeCombatTarget target, Vector3 toAttacker, Vector3 contact, bool broken)
    {
        if (enemyFlashSeconds > 0f) HitFlashRenderer.For(target)?.WhiteFlash(enemyFlashSeconds);
        Vector3 away = Vector3.ProjectOnPlane(toAttacker, Vector3.up);
        if (away.sqrMagnitude < .0001f) return;
        away.Normalize();
        target.ReceiveShove(away * (broken ? breakPushSpeed : enemyPushSpeed), contact);
        EnemyParryRecoil.For(target)?.Kick(away, broken ? breakLeanDegrees : enemyLeanDegrees, broken ? breakLeanSeconds : enemyLeanSeconds);
        if (target.TryGetComponent(out EnemyNetwork network)) network.ShareParry(contact, away, broken);
    }
    /// <summary>
    /// Someone else's parry, seen from here: the burst where it happened, its sound from there and the attacker reeling.
    /// The hand, the knock in the view and the screen pulse belong to whoever parried.
    /// </summary>
    internal void ShowOtherParry(MeleeCombatTarget target, Vector3 contact, Vector3 away, bool broken)
    {
        if (view == null || target == null) return;
        Burst(contact, (away * .35f + Vector3.up * .5f).normalized, broken ? breakBoost : 1f, broken);
        CombatCueAudio.Play(target.gameObject, broken ? CombatCue.ParryBreak : CombatCue.Parry, 1f, true);
        if (enemyFlashSeconds > 0f) HitFlashRenderer.For(target)?.WhiteFlash(enemyFlashSeconds);
        EnemyParryRecoil.For(target)?.Kick(away, broken ? breakLeanDegrees : enemyLeanDegrees, broken ? breakLeanSeconds : enemyLeanSeconds);
    }
    private ParryScreenLook ContactLook() => new ParryScreenLook
    {
        // Snap, hold while time is caught, let go with it.
        Flash = screenFlash, Chromatic = screenChromatic, Lens = screenLens, Saturation = screenSaturation, Contrast = 12f, Bloom = screenBloom,
        Vignette = .14f, Hold = Mathf.Max(.04f, hitStopSeconds), Fade = screenSeconds, Filter = Color.white, VignetteColor = new Color(.16f, .09f, .02f)
    };
    private ParryScreenLook BreakLook() => new ParryScreenLook
    {
        // The drained colour holds through the hit stop and the slow motion that follows it.
        Flash = breakFlash, Chromatic = breakChromatic, Lens = breakLens, Saturation = breakSaturation, Contrast = 18f, Bloom = breakBloom,
        Vignette = .34f, Hold = breakHitStopSeconds + .34f, Fade = breakSeconds, Filter = new Color(1f, .95f, .86f), VignetteColor = new Color(.2f, .05f, .03f)
    };
}
