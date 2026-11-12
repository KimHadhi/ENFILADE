using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Ghost's smoke (Throwable.Smoke; the user, 2026-11-12: "고스트: 잠입과 어그로를 피하기 위한 연막탄"). A thick grey
/// cloud swells where the canister lands and hangs for its seconds, then thins away. Zombies can't see through it: a look
/// that crosses more than GrenadeRules smokeHide metres of it sees nothing (ZombieMind's sight rays, the crouched sight
/// cones of StealthView), whoever stands inside or behind it. An awake zombie whose quarry is hidden so a moment
/// (smokeLose) loses them and goes to search where it last knew them, a horde zombie too: it has to make them out again
/// by sight or sound (ZombieMind.Lose), so a Ghost in the smoke can slip away, drop a crowd, or turn for a takedown from
/// behind. Closer than smokeSmell it finds them anyway, by smell. A horde zombie picks a player it can see over a nearer
/// one in the smoke (EnemyTargeting). Every PC has every cloud (the thrower's PC says where it burst); the host's
/// zombies are the ones it blinds. In-world: a military smoke canister, the kind that hides a section crossing a street.
/// </summary>
[DisallowMultipleComponent]
public sealed class SmokeScreen : MonoBehaviour
{
    // How long it takes to swell to full (s), how long it thins at the end (s), and how high its middle sits over where it
    // landed, as a share of its radius (the lower half is in the ground).
    private const float Swell = 1.4f, Thin = 2.5f, MiddleUp = .35f, Emitting = 1.6f;
    private const int Puffs = 42;
    private static readonly List<SmokeScreen> live = new List<SmokeScreen>();
    private static readonly Dictionary<ZombieMind, float> hiddenFor = new Dictionary<ZombieMind, float>();
    private static readonly List<ZombieMind> gone = new List<ZombieMind>();
    private static int thoughtFrame = -1;
    private static float thinkLeft;

    private Vector3 ground;
    private float full, seconds, age;
    private int emitted;
    private ParticleSystem puffs;

    /// <summary>How many clouds hang now, on this PC.</summary>
    public static int Count => live.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        live.Clear();
        hiddenFor.Clear();
        thoughtFrame = -1;
        thinkLeft = 0f;
    }

    /// <summary>A smoke canister burst here (every PC): its cloud and its hiss.</summary>
    public static SmokeScreen Spawn(Vector3 at, ThrowableInfo info)
    {
        var host = new GameObject("Smoke Screen");
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Effects, "World");
        host.transform.position = at;
        var cloud = host.AddComponent<SmokeScreen>();
        cloud.ground = at;
        cloud.full = info != null ? Mathf.Max(1f, info.Radius) : 5f;
        cloud.seconds = info != null ? Mathf.Max(Swell + Thin, info.Seconds) : 15f;
        cloud.Build();
        live.Add(cloud);
        // The canister's burst in the Realistic Gunslinger pack's smoke (Tables/PackFx.csv smokeBurst: its wind ring,
        // the first billow, the grit) over our own cloud, which is the one that hangs and hides (its seconds, its reach).
        GrenadePackFx.Play("smokeBurst", at, Quaternion.identity);
        Throwables.Play("Smoke", at + Vector3.up * .2f, .9f, 3f, 55f);
        Throwables.Play("SmokeHiss", at + Vector3.up * .2f, .6f, 2f, 30f, 1f, host.transform);
        return cloud;
    }

    // The cloud's radius now: it swells in and thins out at its end.
    private float Radius
    {
        get
        {
            float swell = Mathf.Lerp(.35f, 1f, Mathf.SmoothStep(0f, 1f, age / Swell));
            float thin = age > seconds - Thin ? Mathf.Lerp(1f, .4f, (age - (seconds - Thin)) / Thin) : 1f;
            return full * swell * thin;
        }
    }

    private Vector3 Middle => ground + Vector3.up * (full * MiddleUp);

    // ---- What it hides ----

    /// <summary>How much of the line from a to b runs through smoke (m), all the clouds together.</summary>
    public static float Through(Vector3 a, Vector3 b)
    {
        if (live.Count == 0) return 0f;
        Vector3 line = b - a;
        float length = line.magnitude;
        if (length < 1e-4f) return 0f;
        Vector3 way = line / length;
        float total = 0f;
        foreach (var cloud in live)
        {
            if (cloud == null) continue;
            Chord(cloud.Middle, cloud.Radius, a, way, out float enter, out float leave);
            total += Mathf.Max(0f, Mathf.Min(length, leave) - Mathf.Max(0f, enter));
        }
        return total;
    }

    /// <summary>A look from a to b is lost in smoke (more than GrenadeRules smokeHide metres of it on the way).</summary>
    public static bool Hides(Vector3 a, Vector3 b) => live.Count > 0 && Through(a, b) >= HideLength;

    /// <summary>How far along this ray a look carries before smoke hides what lies past (range when none does).</summary>
    public static float ClearFor(Vector3 from, Vector3 way, float range)
    {
        if (live.Count == 0) return range;
        float hide = HideLength, best = range;
        foreach (var cloud in live)
        {
            if (cloud == null) continue;
            Chord(cloud.Middle, cloud.Radius, from, way, out float enter, out float leave);
            float at = Mathf.Max(0f, enter) + hide;
            if (at <= leave && at < best) best = at;
        }
        return best;
    }

    /// <summary>This point stands in a cloud.</summary>
    public static bool Inside(Vector3 point)
    {
        foreach (var cloud in live)
            if (cloud != null && (point - cloud.Middle).sqrMagnitude <= cloud.Radius * cloud.Radius) return true;
        return false;
    }

    private static float HideLength => Mathf.Max(.2f, Throwables.Rule("smokeHide", 1.6f));

    // Where a line (from, unit way) enters and leaves a sphere, as distances along it; both +inf when it misses.
    private static void Chord(Vector3 middle, float radius, Vector3 from, Vector3 way, out float enter, out float leave)
    {
        float along = Vector3.Dot(middle - from, way);
        float off = (middle - (from + way * along)).sqrMagnitude, r = radius * radius;
        if (off >= r) { enter = leave = float.PositiveInfinity; return; }
        float half = Mathf.Sqrt(r - off);
        enter = along - half;
        leave = along + half;
    }

    // ---- The host's zombies ----

    // Once a frame, whichever cloud gets there first; the work itself every few tenths of a second.
    private static void Think(float dt)
    {
        if (thoughtFrame == Time.frameCount) return;
        thoughtFrame = Time.frameCount;
        if ((thinkLeft -= dt) > 0f) return;
        float step = .2f;
        thinkLeft = step;
        float lose = Mathf.Max(.1f, Throwables.Rule("smokeLose", .8f)), smell = Throwables.Rule("smokeSmell", 2.2f);
        gone.Clear();
        foreach (var pair in hiddenFor) if (pair.Key == null || !pair.Key.Living || !pair.Key.IsAwake) gone.Add(pair.Key);
        foreach (var mind in gone) hiddenFor.Remove(mind);
        foreach (var mind in ZombieMind.All)
        {
            if (mind == null || !mind.IsAwake || !mind.Living) continue;
            // Only where it thinks: the host, or alone. A copy follows its host.
            if (mind.TryGetComponent(out EnemyNetwork network) && network.IsCopy) continue;
            if (!mind.TryGetComponent(out EnemyTargeting targeting)) continue;
            var quarry = targeting.Player;
            if (quarry == null) { hiddenFor.Remove(mind); continue; }
            Vector3 toward = quarry.transform.position - mind.transform.position;
            bool near = new Vector2(toward.x, toward.z).magnitude <= smell;
            if (near || !Hides(mind.EyePoint, Chest(quarry)))
            {
                hiddenFor.Remove(mind);
                continue;
            }
            hiddenFor.TryGetValue(mind, out float hidden);
            hidden += step;
            if (hidden < lose) { hiddenFor[mind] = hidden; continue; }
            hiddenFor.Remove(mind);
            mind.Lose(quarry.transform.position);
        }
    }

    /// <summary>Whether a zombie's eyes can't make this player out for smoke (EnemyTargeting picks someone it can see).</summary>
    public static bool HidesFrom(Component zombie, PlayerMovement player)
    {
        if (live.Count == 0 || zombie == null || player == null) return false;
        Vector3 eye = zombie.TryGetComponent(out ZombieMind mind) ? mind.EyePoint : zombie.transform.position + Vector3.up * 1.5f;
        Vector3 toward = player.transform.position - zombie.transform.position;
        if (new Vector2(toward.x, toward.z).magnitude <= Throwables.Rule("smokeSmell", 2.2f)) return false;
        return Hides(eye, Chest(player));
    }

    private static Vector3 Chest(PlayerMovement player) => player.transform.position + Vector3.up * 1.2f;

    // ---- The cloud ----

    private void Build()
    {
        puffs = gameObject.AddComponent<ParticleSystem>();
        puffs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = puffs.main;
        main.duration = seconds;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Puffs + 8;
        main.startLifetime = seconds;
        main.startSpeed = 0f;
        main.gravityModifier = -.004f;
        var emission = puffs.emission;
        emission.enabled = false;
        var shape = puffs.shape;
        shape.enabled = false;
        // Each puff comes in, holds, and fades over its last quarter; it grows a little as it drifts.
        var color = puffs.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .07f), new GradientAlphaKey(1f, .72f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(fade);
        var size = puffs.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .55f, 1f, 1.15f));
        var drift = puffs.velocityOverLifetime;
        drift.enabled = false;
        var renderer = GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        // Standing in it, the puffs fill the view: never clamped to a share of the screen.
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 10f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, false);
        puffs.Play();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        Emit();
        Think(dt);
        if (age >= seconds) Destroy(gameObject);
    }

    // The puffs come out over the first Emitting seconds, a little further out each, so the cloud billows up and out.
    private void Emit()
    {
        if (puffs == null || emitted >= Puffs) return;
        int due = Mathf.Min(Puffs, Mathf.CeilToInt(Puffs * Mathf.Clamp01(age / Emitting)));
        for (; emitted < due; emitted++)
        {
            float share = (emitted + 1f) / Puffs;
            Vector2 flat = Random.insideUnitCircle * full * Mathf.Lerp(.15f, .78f, share);
            float up = Random.Range(.25f, Mathf.Lerp(1.2f, 2.6f, share));
            var at = ground + new Vector3(flat.x, up, flat.y);
            float grey = Random.Range(.5f, .64f);
            puffs.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = new Vector3(flat.x, .25f, flat.y).normalized * Random.Range(.05f, .25f),
                startSize = Random.Range(2.6f, 4.2f) * Mathf.Clamp(full / 5f, .6f, 1.6f),
                startLifetime = Mathf.Max(1f, seconds - age),
                startColor = new Color(grey, grey * 1.01f, grey * 1.03f, .9f),
                rotation = Random.Range(0f, 360f),
            }, 1);
        }
    }

    private void OnDestroy() => live.Remove(this);
}
