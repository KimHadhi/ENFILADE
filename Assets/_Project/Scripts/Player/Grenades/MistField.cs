using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Détente's antiseptic mist (Throwable.Mist; the user, 2026-11-12: "데탕트: 아군 보호 및 아군 살릴 때 유용할 적이
/// 접근 못하게 하는 군중제어기용 던질 무언가"). A white canister from the aid station bursts into a low dome of chlorine
/// antiseptic for its seconds. In-world: it burns rotten flesh, so zombies won't come into it; on living skin it only
/// cleans wounds. A zombie at its edge recoils coughing (its swing called off) and is shoved back out, and the ground in it
/// is cut from their paths (a carved NavMeshObstacle), so they crowd round it instead of through it: a friend down in it
/// can be raised, one hurt can be patched, in peace. A zombie holding someone in it lets go (each PC frees its own
/// player). Players standing in it heal a little a second. The host's zombies are the ones it keeps out; a brute shoved
/// at its edge only leans back. Every PC has every field (the thrower's PC says where it burst).
/// </summary>
[DisallowMultipleComponent]
public sealed class MistField : MonoBehaviour
{
    // How long it takes to spread (s), how long it fades (s), when the ground in it closes to paths (s), a step of its
    // push (s), how far outside its edge a zombie is already turned back (m), and how long the first touch stops one (s).
    private const float Spread = .5f, Fade = 1.5f, CarveAfter = .3f, PushEvery = .3f, Margin = .5f, Recoil = .7f;
    private const int Puffs = 30, RingPoints = 64;
    private static readonly List<MistField> live = new List<MistField>();

    private Vector3 center;
    private float radius, seconds, age, heal, push, healCarry, thinkLeft;
    private int emitted;
    private ParticleSystem mist;
    private LineRenderer ring;
    private NavMeshObstacle wall;
    private readonly Dictionary<MeleeCombatTarget, float> pushedAt = new Dictionary<MeleeCombatTarget, float>();
    private readonly List<MeleeCombatTarget> stale = new List<MeleeCombatTarget>();

    /// <summary>How many fields stand now, on this PC.</summary>
    public static int Count => live.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => live.Clear();

    /// <summary>A mist canister burst here (every PC): its dome, its ring and its hiss.</summary>
    public static MistField Spawn(Vector3 at, ThrowableInfo info)
    {
        var host = new GameObject("Antiseptic Mist");
        host.transform.position = at;
        var field = host.AddComponent<MistField>();
        field.center = at;
        field.radius = info != null ? Mathf.Max(1f, info.Radius) : 4.5f;
        field.seconds = info != null ? Mathf.Max(Spread + Fade, info.Seconds) : 10f;
        field.heal = info != null ? Mathf.Max(0f, info.Heal) : 4f;
        field.push = info != null ? Mathf.Max(0f, info.Push) : 5f;
        field.Build();
        live.Add(field);
        Throwables.Play("Smoke", at + Vector3.up * .2f, .7f, 3f, 40f, 1.35f);
        return field;
    }

    /// <summary>Whether this point stands in a field.</summary>
    public static bool Inside(Vector3 point)
    {
        foreach (var field in live)
            if (field != null && field.Covers(point)) return true;
        return false;
    }

    private bool Covers(Vector3 point)
    {
        Vector3 flat = Vector3.ProjectOnPlane(point - center, Vector3.up);
        return flat.sqrMagnitude <= Reach * Reach && Mathf.Abs(point.y - center.y) < 3f;
    }

    // Its reach now: it spreads in and shrinks a little as it fades.
    private float Reach => radius * Mathf.Lerp(.4f, 1f, Mathf.Clamp01(age / Spread)) * (age > seconds - Fade ? Mathf.Lerp(1f, .7f, (age - (seconds - Fade)) / Fade) : 1f);

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        Emit();
        DrawRing();
        if (age >= CarveAfter && wall != null && !wall.enabled && age < seconds - Fade) wall.enabled = true;
        if (wall != null && wall.enabled && age >= seconds - Fade) wall.enabled = false;
        if ((thinkLeft -= dt) <= 0f)
        {
            thinkLeft = .1f;
            KeepOut();
        }
        Mend(dt);
        if (age >= seconds) Destroy(gameObject);
    }

    // ---- Zombies ----

    // Every zombie this PC runs (the host's, or alone) at or in its edge recoils and is shoved back out. Each PC's own
    // player held by one in it is let go: that PC stuns its copy, which tells the host (PlayerHold lets a stunned holder go).
    private void KeepOut()
    {
        float reach = Reach, now = Time.time;
        var mine = PlayerRoster.Local;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || !enemy.TryGetComponent(out MeleeCombatTarget body)) continue;
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - center, Vector3.up);
            float distance = away.magnitude;
            if (distance > reach + Margin || Mathf.Abs(enemy.transform.position.y - center.y) > 3f) continue;
            away = distance > .05f ? away / distance : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            bool copy = enemy.TryGetComponent(out EnemyNetwork network) && network.IsCopy;
            if (copy)
            {
                // A copy here: only a hold on this PC's own player is this PC's to break.
                if (mine != null && enemy.TryGetComponent(out ZombieGrip held) && held.IsHolding && held.Held == mine)
                {
                    body.Interrupt(Recoil + .5f);
                    body.ReceiveShove(away * push, enemy.transform.position + Vector3.up);
                }
                continue;
            }
            bool first = !pushedAt.TryGetValue(body, out float last);
            if (!first && now - last < PushEvery) continue;
            pushedAt[body] = now;
            if (first || (enemy.TryGetComponent(out ZombieGrip grip) && grip.IsHolding)) body.Interrupt(Recoil);
            body.ReceiveShove(away * push, enemy.transform.position + Vector3.up);
        }
        // Forget those long out of it, so a later touch recoils them again.
        stale.Clear();
        foreach (var pair in pushedAt) if (pair.Key == null || now - pair.Value > 2f) stale.Add(pair.Key);
        foreach (var body in stale) pushedAt.Remove(body);
    }

    // ---- Players ----

    // This PC's own player, on their feet in it, heals a little a second: the antiseptic cleans their wounds. Paid out in
    // whole points.
    private void Mend(float dt)
    {
        var mine = PlayerRoster.Local;
        if (heal <= 0f || mine == null || mine.IsDead || mine.IsDowned || mine.DamageReceiver == null || !Covers(mine.transform.position))
            return;
        var receiver = mine.DamageReceiver;
        if (receiver.Health >= receiver.MaximumHealth - .01f) { healCarry = 0f; return; }
        healCarry += heal * dt;
        if (healCarry < 1f) return;
        float whole = Mathf.Floor(healCarry);
        healCarry -= whole;
        receiver.Heal(whole);
    }

    // ---- Look ----

    private void Build()
    {
        mist = gameObject.AddComponent<ParticleSystem>();
        mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = mist.main;
        main.duration = seconds;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Puffs + 4;
        main.startLifetime = seconds;
        main.startSpeed = 0f;
        var emission = mist.emission;
        emission.enabled = false;
        var shape = mist.shape;
        shape.enabled = false;
        var color = mist.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .06f), new GradientAlphaKey(1f, .8f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(fade);
        var renderer = GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 10f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, false);
        mist.Play();
        var line = new GameObject("Edge");
        line.transform.SetParent(transform, false);
        ring = line.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = RingPoints;
        ring.widthMultiplier = .045f;
        ring.alignment = LineAlignment.View;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        // The ground in it closes to the zombies' paths a moment after it bursts (CarveAfter), once those in it are shoved.
        wall = gameObject.AddComponent<NavMeshObstacle>();
        wall.shape = NavMeshObstacleShape.Capsule;
        wall.radius = radius;
        wall.height = 3f;
        wall.center = Vector3.up * 1.5f;
        wall.carving = true;
        wall.carveOnlyStationary = true;
        wall.enabled = false;
    }

    // A low pale dome: the puffs come out over the first moments, round and low.
    private void Emit()
    {
        if (mist == null || emitted >= Puffs) return;
        int due = Mathf.Min(Puffs, Mathf.CeilToInt(Puffs * Mathf.Clamp01(age / Spread)));
        for (; emitted < due; emitted++)
        {
            Vector2 flat = Random.insideUnitCircle * radius * .85f;
            var at = center + new Vector3(flat.x, Random.Range(.15f, 1.3f), flat.y);
            mist.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = new Vector3(flat.x, 0f, flat.y).normalized * Random.Range(.05f, .2f) + Vector3.up * .04f,
                startSize = Random.Range(2f, 3.2f) * Mathf.Clamp(radius / 4.5f, .7f, 1.5f),
                startLifetime = Mathf.Max(1f, seconds - age),
                startColor = new Color(Random.Range(.82f, .9f), Random.Range(.93f, .98f), Random.Range(.93f, 1f), .38f),
                rotation = Random.Range(0f, 360f),
            }, 1);
        }
    }

    // The edge on the ground, breathing a little, so the line zombies won't cross reads at a glance.
    private void DrawRing()
    {
        if (ring == null) return;
        float reach = Reach, breathe = .7f + .3f * Mathf.Sin(age * 4f);
        float alpha = Mathf.Clamp01(age / .2f) * Mathf.Clamp01((seconds - age) / Fade) * .75f * breathe;
        ring.startColor = ring.endColor = new Color(.78f, 1f, .96f, alpha);
        Vector3 middle = center + Vector3.up * .05f;
        for (int i = 0; i < RingPoints; i++)
        {
            float angle = i * Mathf.PI * 2f / RingPoints;
            ring.SetPosition(i, middle + new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach));
        }
    }

    private void OnDestroy() => live.Remove(this);
}
