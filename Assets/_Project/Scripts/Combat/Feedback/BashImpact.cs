using UnityEngine;

/// <summary>
/// A bash landing, after For Honor's bashes and shoulder charges (user, 2026-11-11 #16): a white-hot flash where they
/// met, a shock ring thrown out from it, short streaks flying off the way the blow went and, near the floor, a puff of
/// dust; on this PC's own player the view takes a small knock as well. Over in about a sixth of a second (the user's
/// rule: hit effects bang and are gone). Played for shoves, the reload kick, the crouched sweep, dash pushes, the guard
/// break, the shove counter and the beat chain's finisher, each at its own size; friends' bashes show from their copies'
/// shove sound (PlayerShoveAudio). Real time, so the hit stops that come with them don't hold it. Created on first use.
/// </summary>
[DisallowMultipleComponent]
public sealed class BashImpact : MonoBehaviour
{
    private const float Hot = 2.2f;
    private static readonly Color Pale = new Color(1f, .96f, .9f, 1f);
    private static readonly Color Dust = new Color(.55f, .52f, .48f, .3f);
    private static BashImpact instance;

    private ParticleSystem flash, ring, streaks, dust;
    private int knockFrame = -1, frame = -1, playedThisFrame;

    private static BashImpact Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Bash Impact VFX").AddComponent<BashImpact>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        flash = Emitter("Bash Flash", FeelAssets.FlashStar, true, 16, ParticleSystemRenderMode.Billboard);
        Fade(flash); Grow(flash, 1f, 1.3f);
        ring = Emitter("Bash Ring", FeelAssets.Ring, true, 16, ParticleSystemRenderMode.Billboard);
        Fade(ring); Grow(ring, .15f, 1f);
        streaks = Emitter("Bash Streaks", FeelAssets.SoftDot, true, 160, ParticleSystemRenderMode.Stretch);
        var stretch = streaks.GetComponent<ParticleSystemRenderer>();
        stretch.velocityScale = .045f; stretch.lengthScale = 1.2f;
        Fade(streaks);
        var drag = streaks.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 6f; drag.multiplyDragByParticleSize = false;
        dust = Emitter("Bash Dust", FeelAssets.SoftDot, false, 60, ParticleSystemRenderMode.Billboard);
        Fade(dust); Grow(dust, .5f, 1f);
        var settle = dust.limitVelocityOverLifetime; settle.enabled = true; settle.drag = 4f; settle.multiplyDragByParticleSize = false;
    }

    private ParticleSystem Emitter(string name, Texture texture, bool additive, int most, ParticleSystemRenderMode mode)
    {
        var system = FeelAssets.CreateEmitter(name, transform, FeelAssets.Particle(texture, additive), most, 0f, mode);
        var main = system.main; main.useUnscaledTime = true;
        return system;
    }

    private static void Fade(ParticleSystem system)
    {
        var fade = system.colorOverLifetime; fade.enabled = true;
        fade.color = FeelAssets.Fade(Color.white, new Color(1f, 1f, 1f, 0f));
    }

    private static void Grow(ParticleSystem system, float from, float to)
    {
        var size = system.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    // The floor within reach under the point, past the bodies there (their own hit boxes would put the dust in mid-air).
    private static readonly RaycastHit[] below = new RaycastHit[12];
    private static bool Floor(Vector3 point, out RaycastHit floor)
    {
        floor = default;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * .1f, Vector3.down, below, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (below[i].distance >= nearest || below[i].collider.GetComponentInParent<IDamageReceiver>() != null) continue;
            nearest = below[i].distance; floor = below[i];
        }
        return nearest < float.PositiveInfinity;
    }

    /// <summary>Makes the emitters now (this PC's player at spawn, DodgeFlash), so the first bash in a fight costs nothing.</summary>
    public static void Prepare()
    {
        if (Application.isPlaying) Get();
    }

    /// <summary>
    /// A bash met someone at point, going along push. size scales the whole (1: a shove). by: the player who bashed; when
    /// it is this PC's own, the view takes a small knock (once a frame, however many it met).
    /// </summary>
    public static void Play(Vector3 point, Vector3 push, float size = 1f, PlayerMovement by = null)
    {
        if (!Application.isPlaying) return;
        Get().Burst(point, push, Mathf.Max(.2f, size), by);
    }

    private void Burst(Vector3 point, Vector3 push, float size, PlayerMovement by)
    {
        // A crowd shove meeting a dozen at once lights the first few; more would only blur it.
        if (frame != Time.frameCount) { frame = Time.frameCount; playedThisFrame = 0; }
        if (++playedThisFrame > 4) return;
        Vector3 along = push.sqrMagnitude > 1e-4f ? push.normalized : Vector3.up;
        var emit = new ParticleSystem.EmitParams
        {
            position = point, velocity = Vector3.zero, startSize = .5f * size, startLifetime = .09f,
            startColor = new Color(Hot, Hot, Hot, .9f), rotation = Random.Range(0f, 360f)
        };
        flash.Emit(emit, 1);
        emit.startSize = 1.25f * size; emit.startLifetime = .16f; emit.startColor = new Color(Hot, Hot, Hot, .85f);
        ring.Emit(emit, 1);
        var spark = new ParticleSystem.EmitParams();
        int count = Mathf.RoundToInt(10 * size);
        for (int i = 0; i < count; i++)
        {
            spark.position = point + Random.insideUnitSphere * .03f;
            // Most fly on with the blow; some burst out every way, so it reads as a shock rather than a spray.
            Vector3 way = i % 3 == 0 ? Random.onUnitSphere : Vector3.Slerp(along, Random.onUnitSphere, Random.Range(.15f, .55f)).normalized;
            spark.velocity = way * Random.Range(3.5f, 9f) * Mathf.Sqrt(size);
            spark.startSize = Random.Range(.012f, .022f) * size;
            spark.startLifetime = Random.Range(.07f, .15f);
            spark.startColor = i % 4 == 0 ? new Color(Hot, Hot * .85f, Hot * .6f, 1f) : new Color(Pale.r * Hot, Pale.g * Hot, Pale.b * Hot, 1f);
            streaks.Emit(spark, 1);
        }
        // Near the floor the feet kick up a little dust.
        if (Floor(point, out RaycastHit floor))
        {
            var puff = new ParticleSystem.EmitParams();
            for (int i = 0; i < 5; i++)
            {
                Vector3 flat = Random.insideUnitCircle;
                puff.position = floor.point + new Vector3(flat.x, .05f, flat.y) * .25f * size;
                puff.velocity = (new Vector3(flat.x, .4f, flat.y) * 1.2f + Vector3.ProjectOnPlane(along, Vector3.up) * .8f) * size;
                puff.startSize = Random.Range(.25f, .4f) * size;
                puff.startLifetime = Random.Range(.35f, .55f);
                puff.startColor = Dust;
                dust.Emit(puff, 1);
            }
        }
        if (by == null || !by.IsLocal || by.IsNetworkDriven || knockFrame == Time.frameCount) return;
        knockFrame = Time.frameCount;
        var view = by.GetComponentInChildren<Camera>();
        CameraViewPunch.For(view).OrNull()?.ImpactPunch(new Vector3(-1.1f * size, Random.Range(-.8f, .8f), Random.Range(-1.4f, 1.4f)), -1.2f * size, .06f * size);
    }
}
