using UnityEngine;

/// <summary>
/// The Schießhund's bounty dye bursting (Throwable.Dye; the user's pick of 2026-11-12, "현상금 염료탄"): a quick puff of
/// the hound's teal fluorescent dye, and every zombie it reaches (its row's radius, no wall between: Throwables.Covered)
/// carries a small bounty for its row's seconds (DyeMarks). The strongest elite among them takes the Schießhund's real
/// bounty mark, as F on it would (PingMarks, its earlier mark moving here). The in-world reason: a hunting dog follows
/// this dye by its smell, and it glows in the dark.
/// On every PC (GrenadeBursts): each dyes its own copies and marks the same elite, so everyone sees them; only the
/// thrower's PC gains from them.
/// </summary>
public static class DyeBurst
{
    public static void Play(Vector3 at, ThrowableInfo info, PlayerMovement thrower)
    {
        if (info == null) return;
        float radius = Mathf.Max(.5f, info.Radius);
        DyePuff.Spawn(at, radius, info.Tint);
        Throwables.Play("Smoke", at + Vector3.up * .2f, .55f, 3f, 40f, 1.7f);
        Vector3 heart = at + Vector3.up * .3f;
        EnemyHealth strongest = null;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            if ((enemy.transform.position - at).sqrMagnitude > radius * radius) continue;
            if (Throwables.Covered(heart, enemy.transform.position + Vector3.up)) continue;
            DyeMarks.Dye(enemy, thrower, info.Seconds);
            if (PlayerClass.IsElite(enemy) && (strongest == null || enemy.MaximumHealth > strongest.MaximumHealth)) strongest = enemy;
        }
        if (strongest != null && thrower != null) PingMarks.Ping(strongest, thrower, true);
    }
}

/// <summary>
/// The dye's puff: a short burst of teal powder thrown out and up from where it burst, with a few bright flecks, gone in
/// under a second (the game's hit effects are short and punchy, never lingering).
/// </summary>
internal sealed class DyePuff : MonoBehaviour
{
    private const float Life = 1.2f;
    private float age;

    internal static void Spawn(Vector3 at, float radius, Color tint)
    {
        var host = FeelAssets.CreateRuntimeRoot("Dye Puff");
        host.transform.position = at;
        host.AddComponent<DyePuff>();
        var powder = FeelAssets.CreateEmitter("Powder", host.transform, FeelAssets.Particle(FeelAssets.SoftDot, false), 64, -.02f);
        var flecks = FeelAssets.CreateEmitter("Flecks", host.transform, FeelAssets.Particle(FeelAssets.SoftDot, true), 32, .4f);
        Color soft = tint;
        soft.a = .8f;
        Color gone = tint;
        gone.a = 0f;
        var fade = powder.colorOverLifetime;
        fade.enabled = true;
        fade.color = FeelAssets.Fade(soft, gone);
        var grow = powder.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 1.4f));
        var damp = powder.limitVelocityOverLifetime;
        damp.enabled = true;
        damp.dampen = .25f;
        damp.limit = .5f;
        var bright = flecks.colorOverLifetime;
        bright.enabled = true;
        bright.color = FeelAssets.Fade(Color.Lerp(tint, Color.white, .5f), gone);
        float speed = Mathf.Clamp(radius * 1.1f, 2f, 7f);
        for (int i = 0; i < 40; i++)
        {
            Vector3 way = Random.onUnitSphere;
            way.y = Mathf.Abs(way.y) * .7f + .1f;
            powder.Emit(new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * .15f + way * .1f,
                velocity = way.normalized * Random.Range(.4f, 1f) * speed,
                startSize = Random.Range(.25f, .55f),
                startLifetime = Random.Range(.45f, .85f),
                startColor = Color.white,
            }, 1);
        }
        for (int i = 0; i < 18; i++)
        {
            Vector3 way = Random.onUnitSphere;
            way.y = Mathf.Abs(way.y) + .2f;
            flecks.Emit(new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * .15f,
                velocity = way.normalized * Random.Range(.5f, 1.2f) * speed * 1.3f,
                startSize = Random.Range(.04f, .09f),
                startLifetime = Random.Range(.3f, .6f),
                startColor = Color.white,
            }, 1);
        }
    }

    private void Update()
    {
        if ((age += Time.deltaTime) >= Life) Destroy(gameObject);
    }
}
