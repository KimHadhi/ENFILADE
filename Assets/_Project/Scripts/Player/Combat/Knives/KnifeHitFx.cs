using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a class knife's blow lands (DamageMethod.Blade, GunFeelDirector on every PC, so friends see it as the striker does):
/// one short, bold cut of light across the blow, a few sparks flung along it and a crimson flick, gone in a tenth of a
/// second (the user's rule for hits: punchy and flashy, nothing that lingers). A heavy stab crosses a second, shorter cut;
/// a head hit burns whiter and a little longer. The blood itself is ImpactVfx's, as for a shot. Real time, so a hit stop
/// doesn't hold it on screen.
/// </summary>
public sealed class KnifeHitFx : MonoBehaviour
{
    // The cut: its length (m), thickness (m) and life (s); a head hit's is this much longer-lived and brighter.
    private const float CutLength = .42f, CutWidth = .055f, CutLife = .09f, HeadLife = 1.35f, HeadBright = 1.25f;
    // A stab's crossing cut is this much of the first, turned this far from it (degrees).
    private const float CrossShare = .6f, CrossTurn = 70f;
    // The cut sits this far toward the striker, so it shows over the body, not inside it (m).
    private const float Lift = .06f;
    // The sparks along the cut: how many, how fast (m/s) and how long they live (s).
    private const int Sparks = 7, HeavySparks = 11;
    private const float SparkSpeed = 5.5f, SparkLife = .16f;
    // The cut leans up to this far either way about the blow (degrees), so no two look alike.
    private const float Tilt = 28f;

    private static KnifeHitFx instance;
    private ParticleSystem cuts, sparks;
    private readonly List<Material> materials = new List<Material>();

    /// <summary>A knife's blow landed at <paramref name="point"/>, struck along <paramref name="direction"/> (into the body).</summary>
    public static void Play(Vector3 point, Vector3 direction, bool heavy, bool head)
    {
        if (!Application.isPlaying || point == Vector3.zero) return;
        Get().Burst(point, direction, heavy, head);
    }

    private static KnifeHitFx Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Knife Hit FX").AddComponent<KnifeHitFx>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        cuts = Emitter("Knife Cuts", 32, 0f);
        var cutLook = cuts.GetComponent<ParticleSystemRenderer>();
        cutLook.velocityScale = 0f;
        cutLook.lengthScale = CutLength / CutWidth;
        var cutFade = cuts.colorOverLifetime;
        cutFade.enabled = true;
        cutFade.color = FeelAssets.Fade(Color.white, new Color(1f, .12f, .1f, 0f));
        var cutSize = cuts.sizeOverLifetime;
        cutSize.enabled = true;
        cutSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, .35f));

        sparks = Emitter("Knife Sparks", 160, .9f);
        var sparkLook = sparks.GetComponent<ParticleSystemRenderer>();
        sparkLook.velocityScale = .025f;
        sparkLook.lengthScale = 1.2f;
        var drag = sparks.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 6f;
        drag.multiplyDragByParticleSize = false;
        var sparkFade = sparks.colorOverLifetime;
        sparkFade.enabled = true;
        sparkFade.color = FeelAssets.Fade(Color.white, new Color(.8f, .05f, .05f, 0f));
    }

    private ParticleSystem Emitter(string name, int maxParticles, float gravity)
    {
        var material = new Material(FeelAssets.Particle(FeelAssets.SoftDot, true)) { hideFlags = HideFlags.HideAndDontSave };
        materials.Add(material);
        var system = FeelAssets.CreateEmitter(name, transform, material, maxParticles, gravity, ParticleSystemRenderMode.Stretch);
        var main = system.main;
        main.useUnscaledTime = true;
        return system;
    }

    private void Burst(Vector3 point, Vector3 direction, bool heavy, bool head)
    {
        if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
        direction.Normalize();
        // Across the blow, leaning a little: the cut a blade leaves, not the line it came in on.
        Vector3 across = Vector3.Cross(Vector3.up, direction);
        if (across.sqrMagnitude < 1e-6f) across = Vector3.right;
        across = Quaternion.AngleAxis(Random.Range(-Tilt, Tilt), direction) * across.normalized;
        Vector3 at = point - direction * Lift;
        float life = CutLife * (head ? HeadLife : 1f), bright = head ? HeadBright : 1f;
        Cut(at, across, CutWidth * (heavy ? 1.25f : 1f), life, bright);
        if (heavy) Cut(at, Quaternion.AngleAxis(CrossTurn, direction) * across, CutWidth * CrossShare * 1.25f, life * .85f, bright);
        int count = heavy ? HeavySparks : Sparks;
        for (int i = 0; i < count; i++)
        {
            float side = Random.value < .5f ? -1f : 1f;
            Vector3 way = (across * side + -direction * Random.Range(.1f, .6f) + Random.insideUnitSphere * .35f).normalized;
            sparks.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = way * SparkSpeed * Random.Range(.6f, 1.2f), startSize = Random.Range(.012f, .022f),
                startLifetime = SparkLife * Random.Range(.6f, 1.1f), startColor = Color.white,
            }, 1);
        }
    }

    // A stretched particle drawn along its velocity: a tiny speed sets which way the cut lies without moving it.
    private void Cut(Vector3 at, Vector3 way, float width, float life, float bright)
    {
        cuts.Emit(new ParticleSystem.EmitParams
        {
            position = at, velocity = way * .001f, startSize = width, startLifetime = life,
            startColor = new Color(bright, bright, bright, 1f),
        }, 1);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var material in materials) if (material != null) Destroy(material);
    }
}
