using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An Assault's ultimate seen and heard, the same on every PC: an A-10's gun run (the user, 2026-11-14: "A10 공중 지원
/// 느낌으로"). The caller's PC sends where the lane runs and how long until its first round (CardNet, CardFx GunRun) and
/// lands the rounds' damage itself (ClassCards.GunRun); every PC lays the very same rounds (AssaultStrikes.LayRounds).
/// - From the call: the lane on the ground, a red line down its middle and its edges dashed, breathing gently.
/// - The run: the rounds walk the lane at the plane's pace, each a white-hot glint, a ring kicked out over the ground,
///   sparks and dirt thrown up, every few a burst from the effects pack (PackFx gunRunHit) and its crack. The line burns
///   away behind them. Close by, each kicks the view a little.
/// - Right after the last round: the gun's BRRRT from high behind the lane (it comes after the hits, as a real one's
///   does), and the plane itself going over: a dark outline seen from below with vapour off its wingtips, its shadow
///   sweeping the ground under it, its engines' roar.
/// One hero, the walking rounds; the plane is the beat after. Short, nothing left lying, nothing over the screen.
/// Numbers: Tables/ClassAbilities Assault.gunRun*, Tables/PackFx gunRunHit.
/// </summary>
public sealed class GunRunFx : MonoBehaviour
{
    // The lane's red (fire called down), a round's heat, the dirt it throws up, the plane's grey and its vapour.
    private static readonly Color Red = new Color(1f, .3f, .22f), Hot = new Color(1f, .78f, .42f), Dirt = new Color(.21f, .18f, .15f),
        Hull = new Color(.11f, .12f, .14f), Vapour = new Color(.92f, .95f, 1f);
    // How long a round's look lasts; how long before the plane passes over the lane's end it is first drawn, and after (s).
    private const float RoundLife = .55f, PlaneBefore = 2.1f, PlaneAfter = 1.6f;
    // The made jet sound's loudest moment, when the plane is overhead (s into it: GunRunSound.Jet).
    private const float JetPeak = 1.3f;
    // How many pieces the lane's lines are drawn in.
    private const int LanePieces = 24;
    // The plane seen from below, in metres (x across, y along its way, its middle at 0), as convex pieces: an A-10's
    // straight wings, the two engines high at the back, the tailplane and its twin fins.
    private static readonly Vector2[][] Outline =
    {
        new[] { new Vector2(0f, 8f), new Vector2(.55f, 6.6f), new Vector2(.72f, 2f), new Vector2(.62f, -5.8f), new Vector2(.38f, -8.1f),
            new Vector2(-.38f, -8.1f), new Vector2(-.62f, -5.8f), new Vector2(-.72f, 2f), new Vector2(-.55f, 6.6f) },
        new[] { new Vector2(.6f, 1.3f), new Vector2(8.75f, .55f), new Vector2(8.75f, -.75f), new Vector2(.6f, -1.6f) },
        new[] { new Vector2(-.6f, 1.3f), new Vector2(-.6f, -1.6f), new Vector2(-8.75f, -.75f), new Vector2(-8.75f, .55f) },
        new[] { new Vector2(1.15f, -2.3f), new Vector2(2.35f, -2.3f), new Vector2(2.35f, -5.3f), new Vector2(1.15f, -5.3f) },
        new[] { new Vector2(-1.15f, -2.3f), new Vector2(-1.15f, -5.3f), new Vector2(-2.35f, -5.3f), new Vector2(-2.35f, -2.3f) },
        new[] { new Vector2(-3.1f, -6.3f), new Vector2(3.1f, -6.3f), new Vector2(3.1f, -7.5f), new Vector2(-3.1f, -7.5f) },
        new[] { new Vector2(2.9f, -5.6f), new Vector2(3.3f, -5.6f), new Vector2(3.3f, -8f), new Vector2(2.9f, -8f) },
        new[] { new Vector2(-2.9f, -5.6f), new Vector2(-2.9f, -8f), new Vector2(-3.3f, -8f), new Vector2(-3.3f, -5.6f) },
    };
    private static readonly List<Vector3> scratch = new List<Vector3>();

    private Vector3 from, to, way, side, dive;
    private float delay, runTime, age, life, high, speed, climb, passAt, gap;
    private int seed, nextRound, packEvery, crackEvery;
    private Vector3[] rounds;
    private float[] shares;
    private bool[] landed;
    private readonly Vector3[] laneMiddle = new Vector3[LanePieces + 1], laneLeft = new Vector3[LanePieces + 1], laneRight = new Vector3[LanePieces + 1];
    private bool fired, roared;
    private FxShapes glow, matter;

    /// <summary>
    /// A gun run someone's PC called: its lane runs from <paramref name="to"/> − <paramref name="lane"/> to
    /// <paramref name="to"/>, and its first round lands <paramref name="delay"/> seconds from now.
    /// </summary>
    public static void Play(Vector3 to, Vector3 lane, float delay)
    {
        if (!Application.isPlaying || Vector3.ProjectOnPlane(lane, Vector3.up).sqrMagnitude < 1f) return;
        var host = FeelAssets.CreateRuntimeRoot("Gun Run");
        host.transform.position = to;
        host.AddComponent<GunRunFx>().Begin(to - lane, to, delay);
    }

    private void Begin(Vector3 start, Vector3 end, float wait)
    {
        from = start;
        to = end;
        delay = Mathf.Max(0f, wait);
        way = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
        side = Vector3.Cross(Vector3.up, way);
        seed = AssaultStrikes.SeedOf(end);
        runTime = Mathf.Max(.1f, AssaultStrikes.Number("gunRunTime", 1.1f));
        high = Mathf.Max(10f, AssaultStrikes.Number("gunRunHigh", 70f));
        speed = Mathf.Max(20f, AssaultStrikes.Number("gunRunSpeed", 160f));
        climb = Mathf.Max(0f, AssaultStrikes.Number("gunRunClimb", 14f));
        gap = Mathf.Max(0f, AssaultStrikes.Number("gunRunHeard", .12f));
        passAt = delay + runTime + Mathf.Max(0f, AssaultStrikes.Number("gunRunPass", .35f));
        packEvery = Mathf.Max(1, Mathf.RoundToInt(PackFx.Number("gunRunHit", "every", 3f)));
        crackEvery = Mathf.Max(1, Mathf.RoundToInt(AssaultStrikes.Number("gunRunCrackEvery", 2f)));
        // The rounds come down steeply from high behind, the way the plane flies: what each burst faces back along.
        dive = (way * 2.4f - Vector3.up).normalized;
        int count = AssaultStrikes.RoundCount;
        rounds = new Vector3[count];
        shares = new float[count];
        landed = new bool[count];
        AssaultStrikes.LayRounds(from, to, rounds, shares, landed);
        // The lane's lines, set down on the ground once.
        float half = Mathf.Max(0f, AssaultStrikes.Number("gunRunWidth", 3f)) * .5f, top = Mathf.Max(from.y, to.y) + 30f;
        for (int i = 0; i <= LanePieces; i++)
        {
            Vector3 spot = Vector3.Lerp(from, to, i / (float)LanePieces);
            laneMiddle[i] = OnGround(spot, top);
            laneLeft[i] = OnGround(spot - side * half, top);
            laneRight[i] = OnGround(spot + side * half, top);
        }
        life = Mathf.Max(passAt + PlaneAfter, delay + runTime + RoundLife + .1f);
        glow = new FxShapes(transform, "Light", true);
        matter = new FxShapes(transform, "Matter", false);
        GunRunSound.Prewarm();
        Draw();
    }

    private static Vector3 OnGround(Vector3 spot, float top)
        => (AssaultStrikes.Ground(new Vector3(spot.x, top, spot.z), top - spot.y + 30f, out Vector3 ground) ? ground : spot) + Vector3.up * .05f;

    // Game time, as the caller's PC lands the rounds by: the hits and their look stay together in a slowed moment.
    private void Update()
    {
        age += Time.deltaTime;
        Happen();
        if (age >= life) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy()
    {
        glow?.Destroy();
        matter?.Destroy();
    }

    // What happens once as it comes due: each round's burst, crack and kick, the gun's BRRRT, the engines' roar.
    private void Happen()
    {
        while (nextRound < rounds.Length && age >= delay + shares[nextRound] * runTime)
        {
            int i = nextRound++;
            if (!landed[i]) continue;
            if (i % packEvery == 0) PackFx.Play("gunRunHit", rounds[i], Quaternion.LookRotation(-dive));
            if (i % crackEvery == 0) GameAudio.PlayAt(GunRunSound.Hit, rounds[i], AudioChannel.Combat, .85f, 6f, 160f);
            Kick(rounds[i]);
        }
        if (!fired && age >= delay + runTime + gap)
        {
            fired = true;
            // From high behind the lane, where the gun was fired from, heard all over the map.
            GameAudio.PlayAt(GunRunSound.Brrrt, to - way * 140f + Vector3.up * high, AudioChannel.Combat, 1f, 160f, 1100f);
        }
        if (!roared && age >= passAt - JetPeak)
        {
            roared = true;
            GameAudio.PlayAt(GunRunSound.Jet, to + Vector3.up * high * .6f, AudioChannel.Combat, .85f, 120f, 1000f);
        }
    }

    // A round close by kicks this PC's player's view a little, the more the nearer.
    private static void Kick(Vector3 at)
    {
        var mine = PlayerRoster.Local;
        if (mine == null || mine.IsDead) return;
        float reach = Mathf.Max(1f, AssaultStrikes.Number("gunRunShakeReach", 14f));
        float share = 1f - Mathf.Clamp01(Vector3.Distance(mine.transform.position, at) / reach);
        if (share <= 0f) return;
        share *= share * Mathf.Max(0f, AssaultStrikes.Number("gunRunShake", .5f));
        var effects = mine.GetComponentInChildren<PlayerCameraEffects>();
        if (effects != null) effects.ApplyCameraImpulse(new Vector3(-3f * share, 0f, (Random.value < .5f ? -1.5f : 1.5f) * share), 2f * share);
    }

    private void Draw()
    {
        var eye = Camera.main;
        if (glow == null || !glow.Begin(eye)) return;
        matter.Begin(eye);
        Lane();
        for (int i = 0; i < rounds.Length; i++)
        {
            float k = age - (delay + shares[i] * runTime);
            if (landed[i] && k >= 0f && k < RoundLife) Round(i, k);
        }
        Plane();
        glow.End();
        matter.End();
    }

    // The lane on the ground from the call: a red line down its middle and its edges in short dashes, coming up, breathing
    // gently while it waits (under once a second), and burnt away behind the rounds as they walk it.
    private void Lane()
    {
        float end = delay + runTime;
        if (age >= end + .2f) return;
        float shown = Mathf.Clamp01(age / .25f) * (age > end ? 1f - Mathf.Clamp01((age - end) / .2f) : 1f);
        float breathe = .75f + .25f * Mathf.Sin(age * Mathf.PI * 2f * .9f);
        float walked = Mathf.Clamp01((age - delay) / runTime);
        Color line = FxShapes.With(Red, .8f * shown * breathe), edge = FxShapes.With(Red, .45f * shown * breathe);
        for (int i = 0; i < LanePieces; i++)
        {
            if ((i + 1) / (float)LanePieces <= walked) continue;
            glow.Strip(laneMiddle[i], laneMiddle[i + 1], side, .08f, line);
            if (i % 2 != 0) continue;
            glow.Strip(laneLeft[i], laneLeft[i + 1], side, .05f, edge);
            glow.Strip(laneRight[i], laneRight[i + 1], side, .05f, edge);
        }
    }

    // One round going off: a white-hot glint, a ring kicked out over the ground, sparks thrown up and dirt after them.
    private void Round(int i, float k)
    {
        Vector3 at = rounds[i];
        float flash = 1f - Mathf.Clamp01(k / .14f);
        if (flash > 0f) Glint(at + Vector3.up * .3f, .45f + .35f * flash, FxShapes.With(Color.Lerp(Hot, Color.white, flash), .9f * flash));
        float ring = Mathf.Clamp01(k / .3f);
        if (ring < 1f)
            glow.Ring(at + Vector3.up * .05f, Vector3.right, Vector3.forward, Mathf.Lerp(.25f, 1.6f, Ease.Out(ring)), .08f,
                FxShapes.With(Hot, .7f * (1f - ring)), 20);
        float fly = Mathf.Clamp01(k / .4f);
        if (fly < 1f)
            for (int s = 0; s < 4; s++)
            {
                Vector3 velocity = Vector3.up * (3f + 3f * Hash(i, s, 3)) + Out(i, s) * (2f + 2f * Hash(i, s, 4));
                Vector3 head = at + velocity * k + Vector3.down * (4.9f * k * k);
                glow.Blade(head - velocity.normalized * .45f, head, .035f, FxShapes.With(Hot, 1f - fly));
            }
        float dust = Mathf.Clamp01(k / RoundLife);
        if (dust < 1f)
            for (int s = 0; s < 5; s++)
            {
                Vector3 velocity = Vector3.up * (2f + 2.5f * Hash(i, s, 5)) + Out(i, s + 4) * (1.2f + 1.5f * Hash(i, s, 6));
                Vector3 head = at + velocity * k + Vector3.down * (4.9f * k * k);
                matter.Blade(Vector3.Lerp(at, head, .35f), head, .14f * (1f - .5f * dust), FxShapes.With(Dirt, .6f * (1f - dust)));
            }
    }

    // A four-pointed glint turned to the eye.
    private void Glint(Vector3 point, float size, Color color)
    {
        glow.Blade(point - glow.Up * size, point + glow.Up * size, size * .22f, color, default, 6, 1f);
        glow.Blade(point - glow.Right * size * .7f, point + glow.Right * size * .7f, size * .16f, color, default, 6, 1f);
    }

    private float Hash(int round, int part, int salt) => AssaultStrikes.Hash(seed, round * 8 + part, salt);

    // A flat way out from a round, its own for each part of it.
    private Vector3 Out(int round, int part)
    {
        float angle = Hash(round, part, 7) * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    // The plane, high over the lane on its way past just after its rounds: a dark outline seen from below, pulling up a
    // little once over its mark, vapour trailing off its wingtips, and its shadow sweeping the ground under it.
    private void Plane()
    {
        float d = age - passAt;
        if (d < -PlaneBefore || d > PlaneAfter) return;
        float shown = Mathf.Clamp01((d + PlaneBefore) / .3f) * Mathf.Clamp01((PlaneAfter - d) / .4f);
        Vector3 middle = to + way * (speed * d) + Vector3.up * (high + climb * Mathf.Max(0f, d));
        Shape(matter, middle, way, side, FxShapes.With(Hull, .92f * shown));
        float behind = Mathf.Min(speed * .6f, speed * (d + PlaneBefore));
        for (int tip = -1; tip <= 1; tip += 2)
        {
            Vector3 wing = middle + side * (8.7f * tip), trail = wing - way * behind;
            glow.Blade(wing, Vector3.Lerp(wing, trail, .4f), .6f, FxShapes.With(Vapour, .32f * shown));
            glow.Blade(Vector3.Lerp(wing, trail, .4f), trail, .6f, FxShapes.With(Vapour, .14f * shown));
        }
        float over = Mathf.Abs(d) * speed;
        if (over < 90f)
        {
            Vector3 under = new Vector3(middle.x, to.y + .06f, middle.z);
            Shape(matter, under, way, side, FxShapes.With(Color.black, .22f * shown * (1f - over / 90f)));
        }
    }

    // The plane's outline flat at `middle`, along `along` and across `across`.
    private static void Shape(FxShapes shapes, Vector3 middle, Vector3 along, Vector3 across, Color color)
    {
        foreach (var piece in Outline)
        {
            scratch.Clear();
            foreach (var point in piece) scratch.Add(middle + across * point.x + along * point.y);
            shapes.Fill(scratch, color);
        }
    }
}
