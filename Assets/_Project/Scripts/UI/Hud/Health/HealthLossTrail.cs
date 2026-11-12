using UnityEngine;

/// <summary>
/// Presentation state shared by enemy and player health bars: the lost part of the bar stays visible
/// in a loss colour (yellow for small hits, orange at ~70%, red for near-total), holds briefly, then
/// drains slowly to the real value. Big single hits also shake the bar harder.
/// Pure presentation; never changes health. Tuning: Resources/Combat/Feedback (Health loss).
/// </summary>
public sealed class HealthLossTrail
{
    private const float Epsilon = .0001f;
    private float last = float.NaN, hold, shakeTime, shakeAmplitude, noiseTime, seed, flash;
    // Impact shake (enemy bars): instant snap on the hit, then a fast oscillation that dies out quickly.
    private float impactAge = -1f, impactAmplitude, impactPunch;
    private Vector2 impactDirection = Vector2.right;

    /// <summary>Health value the trail currently reaches. Draw it from the real value up to this.</summary>
    public float Trail { get; private set; } = float.NaN;
    /// <summary>Largest single loss (fraction of maximum) folded into the visible trail.</summary>
    public float Heaviness { get; private set; }
    public Vector2 ShakeOffset { get; private set; }
    /// <summary>Uniform scale for the shaking bar (impact mode only): briefly above 1 on a hit.</summary>
    public float ScalePunch { get; private set; } = 1f;
    /// <summary>Loss colour, starting white on impact and settling over trailFlashSeconds.</summary>
    public Color TrailColor => Color.Lerp(CombatFeedbackSettings.InstanceOrDefaults.HealthLossColor(Heaviness), Color.white, flash * flash);
    /// <summary>Alternate stripe colour: the trail colour lightened toward white.</summary>
    public Color StripeColor => Color.Lerp(TrailColor, Color.white, CombatFeedbackSettings.InstanceOrDefaults.trailStripeLighten);
    /// <summary>Stripe pattern offset in bar units; grows while the trail is visible.</summary>
    public float StripeScroll { get; private set; }
    public bool IsAnimating => (!float.IsNaN(Trail) && Trail > last + Epsilon) || shakeTime > 0f || impactAge >= 0f;

    private static int nextSeed;
    // Random is not allowed while Unity constructs MonoBehaviour fields, so decorrelate bars with a counter.
    public HealthLossTrail() { seed = (System.Threading.Interlocked.Increment(ref nextSeed) % 97) * 7.31f; }

    public void Reset(float health)
    {
        last = Trail = health; hold = shakeTime = shakeAmplitude = flash = 0f; Heaviness = 0f; ShakeOffset = Vector2.zero;
        impactAge = -1f; ScalePunch = 1f;
    }

    /// <param name="extraLoss">Damage absorbed elsewhere (e.g. shield). Adds to the shake, not to the trail.</param>
    /// <param name="impact">Enemy-style hit reaction (snap + fast decaying rattle + scale punch) instead of the soft wobble.</param>
    public void Tick(float health, float maximum, float dt, float shakeMax, float extraLoss = 0f, bool impact = false)
    {
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        maximum = Mathf.Max(1f, maximum);
        dt = Mathf.Max(0f, dt);
        if (float.IsNaN(last) || float.IsNaN(Trail)) { Reset(health); return; }

        float loss = last - health;
        if (health > Trail) { Trail = health; Heaviness = 0f; } // healed, respawned or maximum raised
        if (loss > Epsilon)
        {
            float fraction = loss / maximum;
            // Still draining from an earlier hit: keep the heavier colour of the two.
            Heaviness = Trail > last + Epsilon ? Mathf.Max(Heaviness, fraction) : fraction;
            hold = settings.trailHoldSeconds;
            flash = settings.trailFlashSeconds > 0f ? 1f : 0f;
        }
        else if (flash > 0f) flash = Mathf.Max(0f, flash - dt / Mathf.Max(.001f, settings.trailFlashSeconds));
        float shakeLoss = Mathf.Max(0f, loss) + Mathf.Max(0f, extraLoss);
        bool kicked = shakeLoss > Epsilon;
        if (kicked)
        {
            if (impact) ImpactKick(shakeLoss / maximum, shakeMax, settings);
            else Kick(shakeLoss / maximum, shakeMax, settings);
        }
        last = health;

        if (Trail > health)
        {
            if (hold > 0f) hold -= dt;
            else
            {
                // Slow base drain plus a catch-up term, so big chunks drain visibly but never linger forever.
                float remaining = (Trail - health) / maximum;
                float speed = maximum * (settings.trailDrainPerSecond + remaining * settings.trailDrainCatchUp);
                Trail = Mathf.MoveTowards(Trail, health, speed * dt);
            }
        }
        if (Trail <= health + Epsilon) { Trail = health; Heaviness = 0f; flash = 0f; }
        else StripeScroll += settings.trailStripeScroll * dt;

        if (impactAge >= 0f)
        {
            // The hit frame shows the full snap; the rattle advances from the next frame on.
            if (!kicked) impactAge += dt;
            float life = Mathf.Max(.05f, settings.enemyShakeSeconds);
            if (impactAge >= life) { impactAge = -1f; ShakeOffset = Vector2.zero; ScalePunch = 1f; }
            else
            {
                float t = impactAge;
                float envelope = Mathf.Exp(-t * settings.enemyShakeDamping) * (1f - t / life);
                float phase = 2f * Mathf.PI * settings.enemyShakeFrequency * t;
                Vector2 across = new Vector2(-impactDirection.y, impactDirection.x);
                ShakeOffset = impactDirection * (impactAmplitude * envelope * Mathf.Cos(phase))
                    + across * (impactAmplitude * .25f * envelope * Mathf.Sin(phase * 1.37f));
                ScalePunch = 1f + impactPunch * Mathf.Exp(-t * 20f);
            }
        }
        else if (shakeTime > 0f)
        {
            shakeTime = Mathf.Max(0f, shakeTime - dt);
            noiseTime += dt * 38f;
            float decay = shakeTime / Mathf.Max(.01f, settings.shakeSeconds);
            float amplitude = shakeAmplitude * decay * decay;
            ShakeOffset = new Vector2(
                (Mathf.PerlinNoise(seed, noiseTime) * 2f - 1f) * amplitude,
                (Mathf.PerlinNoise(seed + 17.3f, noiseTime) * 2f - 1f) * amplitude * .55f);
            if (shakeTime <= 0f) ShakeOffset = Vector2.zero;
        }
    }

    private void ImpactKick(float fraction, float shakeMax, CombatFeedbackSettings settings)
    {
        // Scales with the share of maximum health lost: 5% ≈ 20%, 20% ≈ 40%, 70% ≈ 82% of the maximum amplitude.
        float weight = Mathf.Pow(Mathf.Clamp01(fraction), .55f);
        float remaining = 0f;
        if (impactAge >= 0f)
        {
            float life = Mathf.Max(.05f, settings.enemyShakeSeconds);
            remaining = impactAmplitude * Mathf.Exp(-impactAge * settings.enemyShakeDamping) * Mathf.Max(0f, 1f - impactAge / life);
        }
        impactAmplitude = Mathf.Max(remaining, Mathf.Max(0f, shakeMax) * weight);
        impactPunch = Mathf.Max(0f, settings.enemyShakeScalePunch) * Mathf.Clamp01(fraction / .5f);
        // Mostly sideways, a random tilt and side each hit so repeated hits do not look identical.
        float angle = Random.Range(-25f, 25f) * Mathf.Deg2Rad;
        impactDirection = new Vector2(Mathf.Cos(angle) * (Random.value < .5f ? -1f : 1f), Mathf.Sin(angle));
        impactAge = 0f;
    }

    private void Kick(float fraction, float shakeMax, CombatFeedbackSettings settings)
    {
        // Small hits barely nudge; heavy ones shake hard. 70% ≈ 80% of the maximum amplitude.
        float amplitude = Mathf.Max(0f, shakeMax) * Mathf.Pow(Mathf.Clamp01(fraction), .6f);
        float current = shakeTime > 0f ? shakeAmplitude * Mathf.Pow(shakeTime / Mathf.Max(.01f, settings.shakeSeconds), 2f) : 0f;
        shakeAmplitude = Mathf.Max(current, amplitude);
        shakeTime = settings.shakeSeconds;
    }
}
