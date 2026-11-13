using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Point's flashbang (Throwable.Flash; the user, 2026-11-12: "포인트: 빠르게 터지고 적에게 확정 CC기를 넣을 수 있는
/// 유동적인 섬광탄"). A short fuse and a bouncy body: thrown far, banked off a wall, or cooked by holding G so it bursts in
/// the air where it should (PlayerGrenades); held out, it bursts in the hand, a stun round the Point itself. Its burst
/// stuns every zombie within its reach whatever way it faces, so long as no wall stands between (and none at all within
/// arm's reach: the bang alone does it): it stops, its swing called off, and its balance breaks (posture), so it reels
/// open to an execution; a brute only staggers a moment. In-world: an M84, the light and the bang that leave a rotten
/// brain reeling. It never blinds a player, thrower or friend (the user's rule: a hit gives a plus, a miss nothing):
/// whoever looks its way sees only a brief white pop. Every PC stuns its own copies of the zombies (each tells the host;
/// the host's is the one that counts); the thrower's PC breaks their balance too.
/// </summary>
public static class FlashBurst
{
    private static readonly List<EnemyHealth> near = new List<EnemyHealth>();

    /// <summary>A flashbang bursts here, on every PC; mine: this PC's player threw it.</summary>
    public static void Play(Vector3 at, ThrowableInfo info, PlayerMovement thrower, bool mine)
    {
        if (info == null) return;
        float radius = Mathf.Max(.5f, info.Radius);
        FlashLook.Show(at, radius);
        Throwables.Play("Flash", at + Vector3.up * .2f, 1f, 6f, 120f);
        Noise.Make(at + Vector3.up * .5f, info.Noise > 0f ? info.Noise : 30f, thrower != null ? thrower.gameObject : null);
        Stun(at, info, radius, mine);
    }

    /// <summary>Whether a flash bursting here reaches this zombie: within its reach and in its sight (or right by it).</summary>
    public static bool Reaches(Vector3 at, float radius, EnemyHealth enemy)
    {
        if (enemy == null || !enemy.IsAlive) return false;
        float distance = Vector3.Distance(at, enemy.transform.position);
        if (distance > radius) return false;
        if (distance <= Throwables.Rule("flashClose", 2.5f)) return true;
        Vector3 light = at + Vector3.up * .25f, feet = enemy.transform.position;
        // Its head or its chest in the light's line is enough.
        Vector3 head = enemy.TryGetComponent(out ZombieBody body) && body.Head != null ? body.Head.position : feet + Vector3.up * 1.6f;
        return !Throwables.Covered(light, head) || !Throwables.Covered(light, feet + Vector3.up);
    }

    private static void Stun(Vector3 at, ThrowableInfo info, float radius, bool mine)
    {
        near.Clear();
        foreach (var enemy in EnemyHealth.Living) if (Reaches(at, radius, enemy)) near.Add(enemy);
        float seconds = Mathf.Max(.2f, info.Seconds);
        foreach (var enemy in near)
        {
            if (enemy == null || !enemy.TryGetComponent(out MeleeCombatTarget body)) continue;
            body.Interrupt(seconds);
            if (mine && info.Posture > 0f) body.ApplyPostureDamage(info.Posture, seconds, true);
            DazeMark.On(enemy, seconds);
        }
        near.Clear();
    }
}

/// <summary>
/// A stunned zombie's daze (FlashBurst): a thin white ring wheels round over its head and pops in, for as long as it
/// reels, then fades. Shown on every PC for the zombies its own copy of the burst reached.
/// </summary>
internal sealed class DazeMark : MonoBehaviour
{
    private const int Points = 28;
    private const float Size = .2f, Above = .3f, Spin = 300f, Pop = .18f, Out = .35f;
    private LineRenderer ring;
    private Transform head;
    private float left, turn, age;

    internal static void On(EnemyHealth enemy, float seconds)
    {
        if (enemy == null) return;
        if (!enemy.TryGetComponent(out DazeMark mark)) mark = enemy.gameObject.AddComponent<DazeMark>();
        mark.Begin(seconds);
    }

    private void Begin(float seconds)
    {
        left = Mathf.Max(left, seconds);
        age = 0f;
        enabled = true;
        if (ring == null)
        {
            var host = new GameObject("Daze") { hideFlags = HideFlags.DontSave };
            host.transform.SetParent(transform, false);
            ring = host.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = Points;
            ring.widthMultiplier = .014f;
            ring.alignment = LineAlignment.View;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, true);
        }
        ring.enabled = true;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        left -= dt;
        age += dt;
        var health = GetComponent<EnemyHealth>();
        if (left <= 0f || health == null || !health.IsAlive)
        {
            if (ring != null) ring.enabled = false;
            enabled = false;
            return;
        }
        if (head == null && TryGetComponent(out ZombieBody body)) head = body.Head;
        Vector3 middle = (head != null ? head.position : transform.position + Vector3.up * 1.6f) + Vector3.up * Above;
        turn += Spin * dt;
        float pop = age < Pop ? Mathf.Lerp(1.7f, 1f, age / Pop) : 1f;
        float alpha = Mathf.Clamp01(left / Out) * .9f;
        ring.startColor = ring.endColor = new Color(1f, 1f, 1f, alpha);
        float size = Size * pop;
        for (int i = 0; i < Points; i++)
        {
            float angle = (turn + i * 360f / Points) * Mathf.Deg2Rad;
            // A tilted, wobbling ring, as stars round a dazed head.
            float wobble = Mathf.Sin(angle * 3f + age * 6f) * .025f;
            ring.SetPosition(i, middle + new Vector3(Mathf.Cos(angle) * size, Mathf.Sin(angle) * size * .3f + wobble, Mathf.Sin(angle) * size));
        }
    }

    private void OnDestroy()
    {
        if (ring != null) Destroy(ring.gameObject);
    }
}

/// <summary>
/// The flashbang's look where it bursts: a white star that blooms and is gone, a hard light for a breath, a thin ring
/// running over the floor to its reach, and on this PC's screen a brief white pop the more its player looks at it (never
/// a blinding: GrenadeRules flashLook is its most).
/// </summary>
internal sealed class FlashLook : MonoBehaviour
{
    private const float Seconds = .45f, StarSeconds = .3f, RingSeconds = .26f, PopSeconds = .2f;
    private const int RingPoints = 56;
    private static Image pop;
    private static float popStrength, popAge = 1f;
    private Transform star;
    private Light glow;
    private LineRenderer ring;
    private float age, radius;

    internal static void Show(Vector3 at, float radius)
    {
        var host = new GameObject("Flash Burst");
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Effects, "World");
        host.transform.position = at + Vector3.up * .25f;
        var look = host.AddComponent<FlashLook>();
        look.radius = radius;
        // The burst itself in the Realistic Gunslinger pack's flashbang (Tables/PackFx.csv flashBurst); the drawn white
        // star where the pack's is off or missing. The hard light, the ring to its reach and the screen's pop stay ours.
        look.Build(!GrenadePackFx.Play("flashBurst", host.transform.position, Quaternion.identity));
        Pop(at);
    }

    private void Build(bool drawnStar)
    {
        if (drawnStar)
        {
            var quad = new GameObject("Star");
            quad.transform.SetParent(transform, false);
            quad.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.FlashStar, true);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            star = quad.transform;
        }
        glow = new GameObject("Light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.shadows = LightShadows.None;
        glow.color = new Color(1f, .97f, .92f);
        glow.range = radius * 1.8f;
        glow.intensity = 14f;
        var line = new GameObject("Ring");
        line.transform.SetParent(transform, false);
        ring = line.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = RingPoints;
        ring.widthMultiplier = .05f;
        ring.alignment = LineAlignment.View;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, true);
    }

    private void LateUpdate()
    {
        age += Time.deltaTime;
        var eye = Camera.main;
        // The star faces the camera and blooms out fast, then shrinks and fades.
        float bloom = Mathf.Clamp01(age / .05f), fade = 1f - Mathf.Clamp01((age - .05f) / (StarSeconds - .05f));
        if (star != null)
        {
            if (eye != null) star.rotation = eye.transform.rotation;
            star.localScale = Vector3.one * Mathf.Lerp(.5f, 4.2f, bloom) * Mathf.Lerp(.6f, 1f, fade);
            star.gameObject.SetActive(fade > 0f);
        }
        if (glow != null) glow.intensity = 14f * Mathf.Clamp01(1f - age / .32f);
        float run = Mathf.Clamp01(age / RingSeconds);
        if (ring != null)
        {
            float r = Mathf.Lerp(.3f, radius, 1f - (1f - run) * (1f - run));
            ring.startColor = ring.endColor = new Color(1f, 1f, 1f, .85f * (1f - run));
            Vector3 middle = transform.position - Vector3.up * .2f;
            for (int i = 0; i < RingPoints; i++)
            {
                float angle = i * Mathf.PI * 2f / RingPoints;
                ring.SetPosition(i, middle + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
            }
        }
        if (age >= Seconds) Destroy(gameObject);
    }

    // The white pop on this PC's screen: strong only looking right at it and near; behind a wall, none.
    private static void Pop(Vector3 at)
    {
        var eye = Camera.main;
        var mine = PlayerRoster.Local;
        if (eye == null || mine == null || mine.IsDead) return;
        Vector3 toward = at + Vector3.up * .25f - eye.transform.position;
        float distance = toward.magnitude;
        if (distance > 35f || Throwables.Covered(eye.transform.position, at + Vector3.up * .25f)) return;
        float facing = Mathf.Clamp01((Vector3.Dot(eye.transform.forward, toward / Mathf.Max(.01f, distance)) - .1f) / .9f);
        float strength = Mathf.Clamp01(Throwables.Rule("flashLook", .55f)) * facing * Mathf.Clamp01(1.15f - distance / 35f);
        if (strength <= .02f) return;
        EnsurePop();
        popStrength = Mathf.Max(popStrength * (1f - Mathf.Clamp01(popAge / PopSeconds)), strength);
        popAge = 0f;
    }

    private static void EnsurePop()
    {
        if (pop != null) return;
        var host = new GameObject("Flash Pop", typeof(RectTransform));
        RuntimeSceneHierarchy.Keep(host, "UI");
        var canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        pop = new GameObject("White", typeof(RectTransform)).AddComponent<Image>();
        pop.transform.SetParent(host.transform, false);
        var rect = pop.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        pop.raycastTarget = false;
        pop.color = new Color(1f, 1f, 1f, 0f);
        host.AddComponent<FlashPopFade>();
    }

    /// <summary>The pop's fade, on its own canvas: real time, so a slowed game doesn't hold the white.</summary>
    private sealed class FlashPopFade : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (pop == null) return;
            popAge += Time.unscaledDeltaTime;
            float share = 1f - Mathf.Clamp01(popAge / PopSeconds);
            pop.color = new Color(1f, 1f, 1f, popStrength * share * share);
        }
    }
}
