using System.Collections.Generic;
using UnityEngine;

/// <summary>One parry's burst, in the view's plane at the moment of contact.</summary>
public struct ParryBurst
{
    public Vector3 Point, Spray, Right, Up;
    public float Scale, FlareBrightness, HitEffectScale, SparkleEffectScale, BreakEffectScale;
    public int Sparks, Embers;
    /// <summary>The parry broke posture: a crossed flare, a red slash over black ink and crimson embers.</summary>
    public bool Break;
}

/// <summary>
/// Parry burst, one layer after the other rather than one blob: a flare on contact, the hit flipbook with flying stars, then
/// sparks and a few lingering embers. A parry that breaks posture adds the execution icon's own look, a red slash over black ink. The
/// flipbooks are prefabs set in Feedback, so any pack's can be tried there. Runs on real time so a hit stop cannot hold it
/// back. Colours go above 1, so the screen pulse's bloom catches them. Created on first use during Play.
/// </summary>
[DisallowMultipleComponent]
public sealed class ParryVfx : MonoBehaviour
{
    // The show after contact, real seconds.
    private const float HitAt = .012f, InkAt = .02f, SlashAt = .035f, SparksAt = .06f, EmbersAt = .12f;
    private const float Hot = 1.9f;
    // Spawned prefabs go away after this long, whatever the prefab does itself.
    private const float EffectLifetime = 3f;
    // The hit flipbook's main streak points this way on the sheet (degrees, counter-clockwise from the view's right).
    private const float HitStreakAngle = -48f;
    // The slash keeps near the execution icon's diagonal; the ink sits a little off it, so it shows along one edge (degrees).
    private const float SlashTilt = 10f, InkOffset = 9f;
    private const string Folder = "Combat/Parry/";
    private static readonly Color Gold = new Color(1f, .78f, .42f, 1f);
    private static readonly Color Crimson = new Color(1f, .3f, .22f, 1f);
    private static readonly Color Pale = new Color(1f, .95f, .86f, 1f);
    private static ParryVfx instance;

    private sealed class Show { public ParryBurst Burst; public float Age, Tilt; public int Step; }

    private readonly List<Show> shows = new List<Show>();
    private readonly List<Material> materials = new List<Material>();
    private ParticleSystem flare, sparks, embers;
    private Transform holder;

    private static ParryVfx Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Parry VFX").AddComponent<ParryVfx>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        flare = Emitter("Parry Flare", Sprite("ParryFlare"), 24, 0f);
        Fade(flare); Grow(flare, 1f, 1.25f);

        sparks = Emitter("Parry Sparks", FeelAssets.SoftDot, 300, .9f, ParticleSystemRenderMode.Stretch);
        var stretch = sparks.GetComponent<ParticleSystemRenderer>();
        stretch.velocityScale = .03f; stretch.lengthScale = 1.4f;
        Grow(sparks, 1f, .2f);
        var sparkDrag = sparks.limitVelocityOverLifetime; sparkDrag.enabled = true; sparkDrag.drag = 5f; sparkDrag.multiplyDragByParticleSize = false;
        var sparkColor = sparks.colorOverLifetime; sparkColor.enabled = true;
        sparkColor.color = FeelAssets.Fade(Color.white, new Color(1f, .45f, .16f, 0f));

        embers = Emitter("Parry Embers", FeelAssets.SoftDot, 120, -.03f);
        Grow(embers, 1f, .35f); Fade(embers);
        var drift = embers.noise; drift.enabled = true; drift.strength = .35f; drift.frequency = 1.4f; drift.scrollSpeed = .4f;
        var emberDrag = embers.limitVelocityOverLifetime; emberDrag.enabled = true; emberDrag.drag = 2.4f; emberDrag.multiplyDragByParticleSize = false;

        var hold = new GameObject("Parry Effect Holder");
        hold.SetActive(false);
        holder = hold.transform;
        holder.SetParent(transform, false);
    }

    private static Texture2D Sprite(string name)
    {
        var texture = Resources.Load<Texture2D>(Folder + name);
        if (texture == null) Debug.LogWarning($"[Parry] Resources/{Folder}{name} 텍스처가 없습니다.");
        return texture;
    }
    // A missing sprite leaves its layer out; without a texture it would draw a solid white quad.
    private ParticleSystem Emitter(string name, Texture texture, int maxParticles, float gravity, ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
    {
        if (texture == null) return null;
        var material =new Material(FeelAssets.Particle(texture, true)) { hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(Hot, Hot, Hot, 1f));
        materials.Add(material);
        var system = FeelAssets.CreateEmitter(name, transform, material, maxParticles, gravity, mode);
        var main = system.main; main.useUnscaledTime = true;
        return system;
    }
    private static void Fade(ParticleSystem system)
    {
        if (system == null) return;
        var fade = system.colorOverLifetime; fade.enabled = true;
        fade.color = FeelAssets.Fade(Color.white, new Color(1f, 1f, 1f, 0f));
    }
    private static void Grow(ParticleSystem system, float from, float to)
    {
        if (system == null) return;
        var size = system.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var material in materials) if (material != null) Destroy(material);
    }

    public static void Play(in ParryBurst burst)
    {
        if (!Application.isPlaying) return;
        var self = Get();
        self.EmitFlare(burst);
        self.shows.Add(new Show { Burst = burst, Tilt = Random.Range(-SlashTilt, SlashTilt) });
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        float dt = Time.unscaledDeltaTime;
        var feedback = CombatFeedbackSettings.InstanceOrDefaults;
        for (int i = shows.Count - 1; i >= 0; i--)
        {
            var show = shows[i];
            show.Age += dt;
            if (show.Step == 0 && show.Age >= HitAt)
            {
                Spawn(feedback.parryHitEffect, show.Burst, show.Burst.HitEffectScale, HitStreakAngle - SprayAngle(show.Burst));
                Spawn(feedback.parrySparkleEffect, show.Burst, show.Burst.SparkleEffectScale);
                show.Step = 1;
            }
            if (show.Step == 1 && show.Age >= InkAt)
            {
                if (show.Burst.Break) Spawn(feedback.parryBreakInkEffect, show.Burst, show.Burst.BreakEffectScale, show.Tilt + InkOffset);
                show.Step = 2;
            }
            if (show.Step == 2 && show.Age >= SlashAt)
            {
                if (show.Burst.Break) Spawn(feedback.parryBreakSlashEffect, show.Burst, show.Burst.BreakEffectScale, show.Tilt);
                show.Step = 3;
            }
            if (show.Step == 3 && show.Age >= SparksAt) { EmitSparks(show.Burst); show.Step = 4; }
            if (show.Step == 4 && show.Age >= EmbersAt) { EmitEmbers(show.Burst); shows.RemoveAt(i); }
        }
    }

    // Screen angle of the spray, degrees counter-clockwise from the view's right.
    private static float SprayAngle(in ParryBurst burst)
        => Mathf.Atan2(Vector3.Dot(burst.Spray, burst.Up), Vector3.Dot(burst.Spray, burst.Right)) * Mathf.Rad2Deg;

    // Built under the inactive holder, so nothing in it starts before it is set up: pack prefabs carry a looping sound
    // that plays on awake, and the parry's cues already cover this. Rotation is clockwise on screen, in degrees; without
    // one the prefab keeps its own.
    private void Spawn(GameObject prefab, in ParryBurst burst, float scale, float? rotation = null)
    {
        if (prefab == null) return;
        var effect = Instantiate(prefab, burst.Point, Quaternion.LookRotation(Vector3.Cross(burst.Right, burst.Up), burst.Up), holder);
        effect.transform.localScale *= burst.Scale * scale;
        foreach (var audio in effect.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main; main.useUnscaledTime = true;
            if (rotation.HasValue) main.startRotation = rotation.Value * Mathf.Deg2Rad;
        }
        effect.transform.SetParent(transform, true);
        Destroy(effect, EffectLifetime);
    }

    private void EmitFlare(in ParryBurst burst)
    {
        if (flare == null) return;
        var emit = new ParticleSystem.EmitParams
        {
            position = burst.Point, velocity = Vector3.zero, startSize = 1.5f * burst.Scale, startLifetime = .1f,
            startColor = new Color(Pale.r, Pale.g, Pale.b, burst.FlareBrightness), rotation = Random.Range(-4f, 4f)
        };
        flare.Emit(emit, 1);
        if (!burst.Break) return;
        emit.rotation += 90f; emit.startSize *= .8f;
        flare.Emit(emit, 1);
    }

    private void EmitSparks(in ParryBurst burst)
    {
        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < burst.Sparks; i++)
        {
            emit.position = burst.Point + Random.insideUnitSphere * .012f;
            // Most fly with the sweep; a few leave the fan so it never reads as a neat cone.
            float scatter = i % 4 == 0 ? .75f : .32f;
            emit.velocity = Vector3.Slerp(burst.Spray, Random.onUnitSphere, Random.Range(0f, scatter)).normalized * Random.Range(1.6f, 7f);
            emit.startSize = Random.Range(.005f, .012f);
            emit.startLifetime = Random.Range(.14f, .36f);
            emit.startColor = Random.value < .3f ? Pale : Gold;
            sparks.Emit(emit, 1);
        }
    }

    private void EmitEmbers(in ParryBurst burst)
    {
        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < burst.Embers; i++)
        {
            emit.position = burst.Point + (burst.Right * Random.Range(-.16f, .16f) + burst.Up * Random.Range(-.1f, .12f)) * burst.Scale;
            emit.velocity = burst.Up * Random.Range(.08f, .4f) + burst.Spray * Random.Range(.05f, .6f) + Random.insideUnitSphere * .16f;
            emit.startSize = Random.Range(.005f, .012f);
            emit.startLifetime = Random.Range(.6f, 1.25f);
            emit.startColor = burst.Break && i % 2 == 0 ? Crimson : Gold;
            embers.Emit(emit, 1);
        }
    }
}
