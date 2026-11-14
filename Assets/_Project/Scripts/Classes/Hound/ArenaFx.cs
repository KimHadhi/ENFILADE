using UnityEngine;

/// <summary>
/// How a Schießhund's showdown (HoundShowdown) looks and sounds where it happens, the same on every PC (ArenaNet; the
/// user's rule that one's own screen and the others' show one world). Short and lean, a teal rift for each, no flash
/// over the screen (the user's taste for special moments):
/// <list type="bullet">
/// <item>Pull: where each of the two stands, a ring opens on the ground, a slit of light stands up through them and
/// widens into an eye, streaks are drawn in toward it, and at the moment they go it snaps shut into the ground with one
/// small pop. A rising rush of air, then a low thump.</item>
/// <item>Arrive: in the arena, a smaller one opening round each as they are put down (the hound's PC alone sees it).</item>
/// <item>Return: where they left, the slit stands open as they come back and fades, a ring runs out and streaks fly off;
/// a hound coming back cheered gets a short gold burst round it as well, and the crowd's roar (Cheer).</item>
/// </list>
/// On the player's own PC their own body's slit and eye are left out: the eye there is inside them, where it would only
/// stand across the view; the ground's ring and the streaks show as everyone else's do. Sounds: a file at
/// Resources/Audio/Moments/showdown, showdown_back or cheer (a mod's, a recorded one) plays in place of the made one.
/// Numbers: Tables/ClassAbilities Schiesshund.arena* (arenaCheer: the roar's loudness).
/// </summary>
public sealed class ArenaFx : MonoBehaviour
{
    private enum Kind : byte { Pull, Arrive, Return }

    private static readonly Color Teal = PingMarks.BountyColor, Gold = new Color(1f, .74f, .3f);
    private const int Streaks = 8, Rays = 10;

    private Kind kind;
    private Vector3 feet;
    private float tall, pull, life, age;
    private bool own, cheered;
    private FxShapes shapes;
    private readonly float[] turns = new float[Streaks], heights = new float[Streaks];

    /// <summary>
    /// The rift round one about to be taken (<paramref name="feet"/>, <paramref name="tall"/> metres), going after
    /// <paramref name="pull"/> s. <paramref name="own"/>: this PC's own player's body.
    /// </summary>
    public static void Pull(Vector3 feet, float tall, float pull, bool own)
    {
        Play(Kind.Pull, feet, tall, pull, own, false);
        ArenaSound.Pull(feet, own);
    }

    /// <summary>A smaller rift round one being put down in the arena (the hound's PC).</summary>
    public static void Arrive(Vector3 feet, float tall, bool own)
    {
        Play(Kind.Arrive, feet, tall, 0f, own, false);
        if (own) ArenaSound.Back(feet, true, .7f);
    }

    /// <summary>The rift where one comes back; <paramref name="cheered"/> with the gold burst of a hound coming back strong.</summary>
    public static void Return(Vector3 feet, float tall, bool cheered, bool own)
    {
        Play(Kind.Return, feet, tall, 0f, own, cheered);
        ArenaSound.Back(feet, own, 1f);
    }

    /// <summary>The crowd's roar for a hound coming back: at this PC's ear for its own, else from where it stands.</summary>
    public static void Cheer(Vector3 at, bool own) => ArenaSound.Cheer(at, own);

    /// <summary>Starts making the roar now (another thread), so it is ready when the showdown ends.</summary>
    public static void Prewarm() => ArenaSound.Prewarm();

    private static void Play(Kind kind, Vector3 feet, float tall, float pull, bool own, bool cheered)
    {
        if (!Application.isPlaying) return;
        var host = FeelAssets.CreateRuntimeRoot("Showdown " + kind);
        host.transform.position = feet;
        var fx = host.AddComponent<ArenaFx>();
        fx.kind = kind;
        fx.feet = feet;
        fx.tall = Mathf.Clamp(tall, 1f, 4f);
        fx.pull = Mathf.Max(.05f, pull);
        fx.own = own;
        fx.cheered = cheered;
        fx.life = kind == Kind.Pull ? fx.pull + .3f : kind == Kind.Arrive ? .45f : cheered ? .8f : .6f;
        for (int i = 0; i < Streaks; i++)
        {
            fx.turns[i] = (i + Random.Range(-.3f, .3f)) * Mathf.PI * 2f / Streaks;
            fx.heights[i] = Random.Range(.12f, .85f);
        }
        fx.shapes = new FxShapes(host.transform, "Showdown rift", true);
    }

    private void Update()
    {
        // Game time, as the pull it shows: the hound goes at the very moment the eye snaps shut.
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        if (shapes == null || !shapes.Begin(Camera.main)) return;
        switch (kind)
        {
            case Kind.Pull: DrawPull(); break;
            case Kind.Arrive: DrawOpen(.75f, .55f); break;
            default: DrawOpen(1f, 1f); if (cheered) DrawGold(); break;
        }
        shapes.End();
    }

    private void OnDestroy() => shapes?.Destroy();

    // Opening round them, drawing in, and gone with a snap at the pull's moment.
    private void DrawPull()
    {
        float t = age, open = Ease(t / .12f), shut = Mathf.Clamp01((t - pull) / .15f);
        Vector3 ground = feet + Vector3.up * .03f;
        float ring = 1.1f * open * (1f - shut * shut);
        shapes.Ring(ground, Vector3.right, Vector3.forward, ring, .045f, FxShapes.With(Teal, .85f * (1f - shut)));
        shapes.Ticks(ground, Vector3.right, Vector3.forward, ring * .78f, 12, .1f, .025f, FxShapes.With(Teal, .45f * (1f - shut)), t * 3f, 3);
        // Streaks drawn in toward them from round about.
        for (int i = 0; i < Streaks; i++)
        {
            float k = Mathf.Clamp01((t - i * .015f) / Mathf.Max(.05f, pull - .02f));
            if (k <= 0f || k >= 1f) continue;
            var way = new Vector3(Mathf.Cos(turns[i]), 0f, Mathf.Sin(turns[i]));
            Vector3 at = feet + Vector3.up * (tall * heights[i]) + way * Mathf.Lerp(1.6f, .15f, k * k);
            shapes.Glow(at, at - way * .35f, FxShapes.With(Teal, .8f * Mathf.Sin(k * Mathf.PI)), .008f, .03f);
        }
        if (!own)
        {
            // The slit stands up through them and widens to an eye, then shuts as they go.
            float wide = t < pull ? Mathf.Lerp(.02f, .36f, Ease(t / pull)) : Mathf.Lerp(.36f, 0f, Mathf.Clamp01((t - pull) / .08f));
            float strength = t < pull ? .9f : 1f - Mathf.Clamp01((t - pull) / .12f);
            Eye(wide, strength);
        }
        // One small pop on the ground where they were.
        if (t >= pull)
        {
            float k = Mathf.Clamp01((t - pull) / .2f);
            shapes.Ring(ground, Vector3.right, Vector3.forward, Mathf.Lerp(.15f, .6f, k), .03f, FxShapes.With(Color.white, .6f * (1f - k)));
        }
    }

    // Standing open as they come (back), a ring running out over the ground, streaks flying off; `size` scales it.
    private void DrawOpen(float size, float strength)
    {
        float t = age, k = Mathf.Clamp01(t / (life * .8f)), fade = 1f - k;
        Vector3 ground = feet + Vector3.up * .03f;
        shapes.Ring(ground, Vector3.right, Vector3.forward, Mathf.Lerp(.2f, 1.4f, Ease(k)) * size, .045f, FxShapes.With(Teal, .85f * fade * strength));
        for (int i = 0; i < Streaks; i++)
        {
            float s = Mathf.Clamp01(t / .3f);
            if (s >= 1f) break;
            var way = new Vector3(Mathf.Cos(turns[i]), 0f, Mathf.Sin(turns[i]));
            Vector3 at = feet + Vector3.up * (tall * heights[i]) + way * Mathf.Lerp(.5f, 1.7f, Ease(s)) * size;
            shapes.Glow(at, at + way * .3f, FxShapes.With(Teal, .7f * Mathf.Sin(s * Mathf.PI) * strength), .008f, .03f);
        }
        if (!own) Eye(Mathf.Lerp(.36f, .48f, k) * size, fade * strength);
    }

    // The gold of a hound coming back strong: short rays bursting out round its chest and a ring running out on the ground.
    private void DrawGold()
    {
        float t = age, s = Mathf.Clamp01(t / .3f), k = Mathf.Clamp01(t / (life * .9f));
        Vector3 chest = feet + Vector3.up * (tall * .62f);
        if (s < 1f)
            for (int i = 0; i < Rays; i++)
            {
                float turn = (i + .5f) * Mathf.PI * 2f / Rays + .3f;
                var way = new Vector3(Mathf.Cos(turn), Mathf.Sin(i * 1.7f) * .25f, Mathf.Sin(turn)).normalized;
                Vector3 from = chest + way * Mathf.Lerp(.6f, 1.3f, Ease(s)), to = from + way * Mathf.Lerp(.45f, .15f, s);
                shapes.Glow(from, to, FxShapes.With(Gold, .9f * (1f - s)), .01f, .04f);
            }
        shapes.Ring(feet + Vector3.up * .04f, Vector3.right, Vector3.forward, Mathf.Lerp(.4f, 2.4f, Ease(k)), .05f, FxShapes.With(Gold, .7f * (1f - k)));
    }

    // A vertical eye of light through the body: two bowed blades meeting at the feet and over the head, a white core.
    private void Eye(float wide, float strength)
    {
        if (wide <= .001f || strength <= .01f) return;
        Vector3 bottom = feet + Vector3.up * .05f, top = feet + Vector3.up * (tall + .2f), side = shapes.Right * wide;
        shapes.Blade(bottom, top, .035f, FxShapes.With(Teal, .9f * strength), side, 12, 1f);
        shapes.Blade(bottom, top, .035f, FxShapes.With(Teal, .9f * strength), -side, 12, 1f);
        shapes.Glow(bottom, top, FxShapes.With(Color.white, .55f * strength), .006f, .02f);
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }
}
