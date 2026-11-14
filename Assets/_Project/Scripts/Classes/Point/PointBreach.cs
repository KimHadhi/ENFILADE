using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Point's ultimate, 돌입 (Docs/Features/ClassCards.md, the Point; the user's pick of 2026-11-14: "A 돌입 같은 느낌이
/// 좋네. 포인트가 취약할 때는 엘리트 같은 체력 돼지가 왔을 때인데 이런 상황을 타개하기 위해서 나인뱅 플래쉬 같은 거 앞에
/// 터뜨리는 거 좋겠네."). The off hand throws a nine-bang where the crosshair is (PlayerGrenades.ThrowForUltimate: the
/// flashbang's quick throw, nothing taken off the belt), and where it lands it bursts nine times in a quick run, the bangs
/// a little apart round the spot. Every zombie a bang's light reaches (FlashBurst.Reaches: within its reach and no wall
/// between) is stunned for the rest of the run and Point.breachStun beyond it, and its balance breaks, open to an
/// execution: an elite as much as any, which is the point of it, the Point's way out when a heavy elite walks in. A brute
/// shakes a stun off at once (MeleeCombatTarget.Steadfast), so every bang that reaches it stuns it again and the run holds
/// it all the same.
/// It blinds no one and never whitens a screen: nine flashes in two seconds would be a strobe (the user's rule against
/// strobes, 2026-11-14). Each bang is a crack, a small warm spark and a thin ring over the floor to its reach; one soft
/// light swells over the whole run and fades. Every PC plays the run from the thrower's word (CardNet, CardFx Breach: where
/// the hand let go, where it lands, how long it flies) and stuns its own copies of the zombies, each telling the host, as a
/// flashbang does (FlashBurst); the thrower's PC breaks their balance too. Numbers: Tables/ClassAbilities Point.breach*.
/// </summary>
public static class PointBreach
{
    private const string Row = "Point";

    /// <summary>
    /// This PC's Point fires the ultimate (ClassCards.Ultimate): the off hand throws the nine-bang, or, with the hand on
    /// something else, it leaves from beside the eyes at once.
    /// </summary>
    public static void Throw(PlayerMovement point)
    {
        if (point == null || !point.IsLocal) return;
        if (point.TryGetComponent(out PlayerGrenades grenades) && grenades.ThrowForUltimate(Throwable.Flash, Launch))
        {
            // The gauge is spent: should the hand be taken before it lets go, the nine-bang goes all the same (BreachHand).
            BreachHand.Watch(grenades);
            return;
        }
        Launch(BesideEyes());
    }

    /// <summary>
    /// The hand let go here: the nine-bang flies to what the crosshair is on within Point.breachReach (or the reach's end),
    /// brought down to the floor there, and every PC is told.
    /// </summary>
    internal static void Launch(Vector3 from)
    {
        var eye = Camera.main;
        if (eye == null) return;
        Vector3 origin = eye.transform.position, look = eye.transform.forward;
        float reach = Mathf.Max(2f, PlayerClass.Ability(Row, "breachReach", 15f));
        Vector3 aimed = Physics.Raycast(origin, look, out RaycastHit seen, reach, Throwables.WorldMask, QueryTriggerInteraction.Ignore)
            ? seen.point - look * .3f : origin + look * reach;
        Vector3 landing = Floor(aimed, 30f);
        float fly = Mathf.Max(.15f, PlayerClass.Ability(Row, "breachFly", .45f));
        CardNet.Show(CardFx.Kind.Breach, from, landing - from, fly);
    }

    /// <summary>Where a throw leaves from without the hand: beside the eyes, a little down and to the left.</summary>
    internal static Vector3 BesideEyes()
    {
        var eye = Camera.main;
        if (eye == null) return Vector3.zero;
        var view = eye.transform;
        return view.position + view.forward * .35f - view.right * .16f - view.up * .12f;
    }

    /// <summary>
    /// Every PC (CardFx Breach): the nine-bang flies from <paramref name="from"/> to <paramref name="from"/> +
    /// <paramref name="toLanding"/> over <paramref name="fly"/> s and bursts there. Mine: this PC's player threw it.
    /// </summary>
    internal static void Play(Vector3 from, Vector3 toLanding, float fly, bool mine, PlayerMovement thrower)
    {
        if (!Application.isPlaying) return;
        var host = FeelAssets.CreateRuntimeRoot("Nine-Bang");
        host.AddComponent<NineBang>().Begin(from, from + toLanding, Mathf.Max(.05f, fly), mine, thrower);
    }

    /// <summary>The floor under <paramref name="point"/> (from a little above it, down <paramref name="depth"/> m), or the point itself.</summary>
    internal static Vector3 Floor(Vector3 point, float depth)
    {
        Vector3 top = point + Vector3.up * .5f;
        return Physics.Raycast(top, Vector3.down, out RaycastHit ground, depth + .5f, Throwables.WorldMask, QueryTriggerInteraction.Ignore)
            ? ground.point : point;
    }
}

/// <summary>
/// A nine-bang in the Point's hand (PointBreach.Throw): should the hand go back to the gun without letting it go, or take
/// far too long, it leaves from beside the eyes all the same, so a spent gauge is never lost.
/// </summary>
internal sealed class BreachHand : MonoBehaviour
{
    private const float Longest = 1.5f;
    private PlayerGrenades grenades;
    private float age;

    internal static void Watch(PlayerGrenades grenades)
    {
        if (grenades == null) return;
        if (!grenades.TryGetComponent(out BreachHand watch)) watch = grenades.gameObject.AddComponent<BreachHand>();
        watch.grenades = grenades;
        watch.age = 0f;
        watch.enabled = true;
    }

    private void Update()
    {
        if (grenades == null || !grenades.UltimatePending) { enabled = false; return; }
        age += Time.deltaTime;
        if (!grenades.UltimateDropped && age < Longest) return;
        grenades.ForgetUltimateThrow();
        enabled = false;
        PointBreach.Launch(PointBreach.BesideEyes());
    }
}

/// <summary>A nine-bang's flight and its run of bangs on one PC (PointBreach.Play).</summary>
internal sealed class NineBang : MonoBehaviour
{
    private const string Row = "Point";
    // A sunflower's turn (degrees): each bang this far round from the last, so the run covers the spot with no clump.
    private const float GoldenAngle = 137.508f;
    // The one light over the run: up as the first bang goes, down after the last (s). The canister's jump at each bang (s, m).
    private const float LightIn = .15f, LightOut = .7f, HopSeconds = .12f, HopHeight = .1f;
    // How long a brute's daze ring shows after a bang (it shakes the stun off this soon: MeleeCombatTarget.Steadfast).
    private const float BruteDaze = .5f;
    // How long the spent canister lies there after the light is out (s).
    private const float SpentStays = 1f;
    private static readonly List<EnemyHealth> near = new List<EnemyHealth>();
    private readonly HashSet<EnemyHealth> reached = new HashSet<EnemyHealth>();
    private Vector3 from, landing, ahead;
    private Vector3[] points;
    private float fly, every, radius, stun, posture, lightPeak, noise, age, hopAge = 1f;
    private int next;
    private bool mine;
    private GameObject thrower;
    private Transform canister;
    private Light glow;

    internal void Begin(Vector3 from, Vector3 landing, float fly, bool mine, PlayerMovement thrower)
    {
        this.from = from;
        this.landing = landing;
        this.fly = fly;
        this.mine = mine;
        this.thrower = thrower != null ? thrower.gameObject : null;
        int bangs = Mathf.Clamp(Mathf.RoundToInt(PlayerClass.Ability(Row, "breachBangs", 9f)), 1, 30);
        every = Mathf.Max(.02f, PlayerClass.Ability(Row, "breachEvery", .2f));
        radius = Mathf.Max(.5f, PlayerClass.Ability(Row, "breachRadius", 6f));
        stun = Mathf.Max(.2f, PlayerClass.Ability(Row, "breachStun", 2.5f));
        posture = Mathf.Max(0f, PlayerClass.Ability(Row, "breachPosture", 999f));
        lightPeak = Mathf.Max(0f, PlayerClass.Ability(Row, "breachLight", 5f));
        float spread = Mathf.Max(0f, PlayerClass.Ability(Row, "breachSpread", 4f));
        var flash = Throwables.Of(Throwable.Flash);
        noise = flash != null && flash.Noise > 0f ? flash.Noise : 30f;
        ahead = Vector3.ProjectOnPlane(landing - from, Vector3.up);
        ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : Vector3.forward;
        points = Spread(bangs, spread);
        transform.position = landing;
        canister = new GameObject("Canister").transform;
        canister.SetParent(transform, false);
        Throwables.Dress(Throwable.Flash, canister);
        canister.position = from;
        if (lightPeak > 0f)
        {
            glow = new GameObject("Light").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.position = landing + Vector3.up * 1.2f;
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
            glow.color = new Color(1f, .95f, .86f);
            glow.range = spread + radius;
            glow.intensity = 0f;
            glow.enabled = false;
        }
    }

    // The bangs round where it landed: a sunflower's turns, each a little farther out (by the square root, so they cover
    // the spot evenly), kept on this side of any wall from the middle, and set on the floor there. The same on every PC:
    // the same spot, the same walls.
    private Vector3[] Spread(int count, float spread)
    {
        Vector3 side = Vector3.Cross(Vector3.up, ahead);
        Vector3 middle = landing + Vector3.up * .4f;
        var spots = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float turn = i * GoldenAngle * Mathf.Deg2Rad, away = spread * Mathf.Sqrt((i + .5f) / count);
            Vector3 want = middle + (ahead * Mathf.Cos(turn) + side * Mathf.Sin(turn)) * away;
            if (Physics.Linecast(middle, want, out RaycastHit wall, Throwables.WorldMask, QueryTriggerInteraction.Ignore))
                want = wall.point - (want - middle).normalized * .25f;
            spots[i] = PointBreach.Floor(want, 3f) + Vector3.up * .05f;
        }
        return spots;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        Fly(dt);
        while (next < points.Length && age >= fly + next * every) Bang(next++);
        Shine();
        if (age >= fly + (points.Length - 1) * every + LightOut + SpentStays) Destroy(gameObject);
    }

    // From the hand to where it lands on a short arc, tumbling end over end; then it lies there and jumps at each bang.
    private void Fly(float dt)
    {
        if (canister == null) return;
        if (age < fly)
        {
            float t = Mathf.Clamp01(age / fly);
            float rise = Mathf.Clamp(Vector3.Distance(from, landing) * .12f, .25f, 1.6f);
            canister.position = Vector3.Lerp(from, landing, t) + Vector3.up * (rise * 4f * t * (1f - t));
            canister.rotation = Quaternion.LookRotation(ahead) * Quaternion.AngleAxis(age * 900f, Vector3.right);
            return;
        }
        hopAge += dt;
        float hop = hopAge < HopSeconds ? Mathf.Sin(hopAge / HopSeconds * Mathf.PI) * HopHeight : 0f;
        canister.position = landing + Vector3.up * (.04f + hop);
        canister.rotation = Quaternion.LookRotation(ahead) * Quaternion.Euler(90f, 0f, 0f);
    }

    private void Bang(int i)
    {
        Vector3 at = points[i];
        hopAge = 0f;
        // A crack each, a touch apart in pitch, so the run doesn't sound like one sample on repeat.
        Throwables.Play("Flash", at + Vector3.up * .2f, .8f, 6f, 120f, 1f + ((i * 3) % 7 - 3) * .02f);
        if (i == 0) Noise.Make(at + Vector3.up * .5f, noise, thrower);
        BangSpark.Show(at, radius);
        Stun(at, i);
    }

    // Every zombie this bang's light reaches. The first time: stunned for the rest of the run and the stun beyond it, its
    // daze shown, its balance broken (on the thrower's PC; the host is told). A brute again at every bang that reaches it.
    private void Stun(Vector3 at, int i)
    {
        float hold = (points.Length - 1 - i) * every + stun;
        near.Clear();
        foreach (var enemy in EnemyHealth.Living) if (FlashBurst.Reaches(at, radius, enemy)) near.Add(enemy);
        foreach (var enemy in near)
        {
            if (enemy == null || !enemy.TryGetComponent(out MeleeCombatTarget body)) continue;
            bool first = reached.Add(enemy);
            if (!first && !body.Steadfast) continue;
            body.Interrupt(hold);
            DazeMark.On(enemy, body.Steadfast ? BruteDaze : hold);
            if (first && mine && posture > 0f) body.ApplyPostureDamage(posture, hold, true);
        }
        near.Clear();
    }

    // One soft light over the whole run, never a flash per bang: up as the first goes, held, down after the last.
    private void Shine()
    {
        if (glow == null) return;
        float last = fly + (points.Length - 1) * every;
        float share = age < fly ? 0f : Mathf.Min(Mathf.Clamp01((age - fly) / LightIn), 1f - Mathf.Clamp01((age - last) / LightOut));
        glow.intensity = lightPeak * share * share * (3f - 2f * share);
        glow.enabled = glow.intensity > .01f;
    }
}

/// <summary>
/// One bang of a nine-bang (NineBang): a small warm spark that pops and is gone, and a thin ring running over the floor to
/// the bang's reach. No light of its own and nothing on the screen: nine of these in two seconds must never read as a strobe.
/// </summary>
internal sealed class BangSpark : MonoBehaviour
{
    private const float StarSeconds = .24f, RingSeconds = .3f, StarSize = 1.3f, StarAlpha = .5f, RingAlpha = .4f;
    private const int RingPoints = 40;
    // Warm, not white: a spark of the bang, never a flash's white.
    private static readonly Color Warm = new Color(1f, .9f, .75f);
    private LineRenderer star, ring;
    private float age, radius;

    internal static void Show(Vector3 at, float radius)
    {
        var host = FeelAssets.CreateRuntimeRoot("Nine-Bang Bang");
        host.transform.position = at;
        var spark = host.AddComponent<BangSpark>();
        spark.radius = radius;
        // The star is a short line as wide as it is long, facing the eye: a billboard whose colour sets how bright it is.
        spark.star = Line(host.transform, "Star", FeelAssets.Particle(FeelAssets.FlashStar, true), 2, false);
        spark.ring = Line(host.transform, "Ring", FeelAssets.Particle(Texture2D.whiteTexture, true), RingPoints, true);
        spark.ring.widthMultiplier = .035f;
    }

    private static LineRenderer Line(Transform parent, string name, Material look, int points, bool loop)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(parent, false);
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = points;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = look;
        return line;
    }

    private void LateUpdate()
    {
        age += Time.deltaTime;
        Vector3 at = transform.position;
        if (star != null)
        {
            // Out fast, then shrinking as it fades: a pop, small and warm.
            float pop = Mathf.Clamp01(age / .04f), fade = 1f - Mathf.Clamp01((age - .04f) / (StarSeconds - .04f));
            float size = StarSize * Mathf.Lerp(.4f, 1f, pop) * Mathf.Lerp(.7f, 1f, fade);
            var eye = Camera.main;
            Vector3 across = eye != null ? eye.transform.right : Vector3.right, middle = at + Vector3.up * .35f;
            star.SetPosition(0, middle - across * size * .5f);
            star.SetPosition(1, middle + across * size * .5f);
            star.widthMultiplier = size;
            star.startColor = star.endColor = new Color(Warm.r, Warm.g, Warm.b, StarAlpha * fade);
            star.enabled = fade > 0f;
        }
        if (ring != null)
        {
            float run = Mathf.Clamp01(age / RingSeconds);
            float r = Mathf.Lerp(.3f, radius, 1f - (1f - run) * (1f - run));
            ring.startColor = ring.endColor = new Color(1f, 1f, 1f, RingAlpha * (1f - run));
            Vector3 floor = at + Vector3.up * .06f;
            for (int i = 0; i < RingPoints; i++)
            {
                float angle = i * Mathf.PI * 2f / RingPoints;
                ring.SetPosition(i, floor + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
            }
        }
        if (age >= Mathf.Max(StarSeconds, RingSeconds)) Destroy(gameObject);
    }
}
