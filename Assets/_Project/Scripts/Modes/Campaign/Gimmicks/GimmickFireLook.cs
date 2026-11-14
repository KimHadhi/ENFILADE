using UnityEngine;

/// <summary>
/// A fire's look and sound over a patch of ground, for the map gimmicks that burn (a spilled pump's fuel, GimmickFuelTrap;
/// the car pile the road is burned clear of, BurnFire): flames rising off the patch, an orange light whose flicker is soft
/// and slow (never a strobe), and a roar. Built once on first use, then switched on and off.
/// </summary>
public sealed class GimmickFireLook
{
    private readonly ParticleSystem flames;
    private readonly Light glow;
    private readonly AudioSource roar;
    private float litAt;

    public GimmickFireLook(Transform parent, Bounds patch)
    {
        var host = new GameObject("불");
        host.transform.SetParent(parent, false);
        host.transform.position = new Vector3(patch.center.x, patch.min.y + .05f, patch.center.z);
        flames = host.AddComponent<ParticleSystem>();
        flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = flames.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(.6f, 1.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .55f, .15f, .8f), new Color(1f, .32f, .06f, .7f));
        main.maxParticles = 320;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = flames.emission;
        emission.rateOverTime = Mathf.Clamp(patch.size.x * patch.size.z * 2f, 30f, 240f);
        var shape = flames.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Max(.5f, patch.size.x), .05f, Mathf.Max(.5f, patch.size.z));
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var size = flames.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, .2f));
        var color = flames.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(.4f, .4f, .4f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .1f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(fade);
        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
        glow = new GameObject("불빛", typeof(Light)).GetComponent<Light>();
        glow.transform.SetParent(host.transform, false);
        glow.transform.localPosition = Vector3.up * 1.2f;
        glow.type = LightType.Point;
        glow.color = new Color(1f, .55f, .2f);
        glow.range = Mathf.Max(8f, patch.size.magnitude);
        glow.shadows = LightShadows.None;
        glow.enabled = false;
        roar = GimmickSounds.Loop(host, GimmickSounds.Cue.Fire, 4f, 40f);
    }

    public bool On { get; private set; }

    public void Set(bool on)
    {
        if (on == On) return;
        On = on;
        if (on) { litAt = Time.time; flames.Play(); glow.enabled = true; roar.Play(); }
        else { flames.Stop(true, ParticleSystemStopBehavior.StopEmitting); glow.enabled = false; roar.Stop(); }
    }

    /// <summary>Every frame while it burns: the light's soft flicker.</summary>
    public void Tick()
    {
        if (!On) return;
        float on = Time.time - litAt;
        glow.intensity = 3f * (.82f + .18f * Mathf.Sin(on * 5.3f) * Mathf.Sin(on * 2.9f));
    }
}
