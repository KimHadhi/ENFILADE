using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the phoenix card's rise looks and sounds like (PhoenixRise; the user, 2026-11-14: "피닉스처럼 세컨드 찬스 주는
/// 느낌으로"). One idea, short and warm: fire running out from under the player. A ring of flame runs out over the ground
/// from their feet to the burst's reach as the zombies there fall, tongues of flame lick up along it, embers rise off the
/// ground it crossed, and a warm light swells once and goes; about a second, nothing left lying. A rush of flame and a
/// rising call carry it (a file at Resources/Audio/Moments/phoenix plays in its place, as MomentSound's stingers allow).
/// On the player's own screen a warm rim at the edges, never at the middle, eases out over the guarded moment, so they can
/// tell nothing lands until it is gone: a HUD extra on top of the world's look, never in its place (same-world-view).
/// Nothing blinks: every light rises once and falls once (no-strobe-blinks), and flames and embers fade out near the
/// middle of the view so the crosshair stays clear (vfx-restraint). The card is a lone player's, so only their PC draws it.
/// </summary>
public sealed class PhoenixFx : MonoBehaviour
{
    private const int Rate = 44100, Tongues = 28, Embers = 18;
    // How long the flame lasts (s), the light's peak, the rim's strongest alpha, and its sort order (under the damage feedback's).
    private const float FlameLife = 1.1f, LightPeak = 4f, RimPeak = .3f;
    private const int RimOrder = 23;
    private static readonly Color Ember = new Color(1f, .42f, .1f), Gold = new Color(1f, .74f, .28f), RimTint = new Color(1f, .5f, .16f);
    private static AudioClip rush;
    private static Texture2D rimTexture;

    // One tongue of flame on the ring: how late it swells (s), its turn off its even place round the ring (0-1), how far
    // in from the ring's edge it stands (0-1) and how tall it licks (m).
    private struct Flame { public float Delay, Turn, Inset, Tall; }
    // One ember: how late it lifts off (s) and how long it lives (s), which way round the ring (rad), how far out on the
    // ground it starts (a share of the reach), and how fast it rises and drifts out (m/s).
    private struct Spark { public float Delay, Life, Angle, From, Up, Drift; }

    private FxShapes shapes;
    private Light glow;
    private GameObject rimHost;
    private RawImage rim;
    private Transform eye;
    private Vector3 feet;
    private float reach, guard, age;
    // Rolled once as it begins, so every frame draws the same flames and embers.
    private readonly Flame[] flames = new Flame[Tongues];
    private readonly Spark[] sparks = new Spark[Embers];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        rush = null;
        rimTexture = null;
    }

    /// <summary>
    /// <paramref name="who"/> rises: flame out to <paramref name="radius"/> m round where they stand, and the warm rim on
    /// this PC's screen for <paramref name="guardSeconds"/>, the moment nothing lands on them.
    /// </summary>
    public static void Play(PlayerMovement who, float radius, float guardSeconds)
    {
        if (!Application.isPlaying || who == null) return;
        Vector3 at = who.transform.position;
        var host = FeelAssets.CreateRuntimeRoot("Phoenix Rise");
        host.transform.position = at;
        var fx = host.AddComponent<PhoenixFx>();
        fx.feet = at;
        fx.reach = Mathf.Max(.5f, radius);
        fx.guard = Mathf.Max(0f, guardSeconds);
        fx.Begin();
    }

    private void Begin()
    {
        var random = new System.Random(Mathf.RoundToInt(feet.x * 73f + feet.y * 151f + feet.z * 37f));
        float Next() => (float)random.NextDouble();
        for (int i = 0; i < Tongues; i++)
            flames[i] = new Flame { Delay = .02f + .1f * Next(), Turn = .5f * Next(), Inset = .06f * Next(), Tall = .3f + .45f * Next() };
        for (int i = 0; i < Embers; i++)
            sparks[i] = new Spark
            {
                Delay = .06f + .3f * Next(), Life = .55f + .35f * Next(), Angle = Next() * Mathf.PI * 2f, From = .35f + .6f * Next(),
                Up = 1.4f + 1.4f * Next(), Drift = .3f + .4f * Next()
            };
        shapes = new FxShapes(transform, "Flame", true);
        glow = new GameObject("Light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.position = feet + Vector3.up * 1.1f;
        glow.type = LightType.Point;
        glow.shadows = LightShadows.None;
        glow.color = Ember;
        glow.range = reach + 2f;
        glow.intensity = 0f;
        Sound();
        if (guard > 0f) BuildRim();
        Draw();
    }

    private void Update()
    {
        // Game time, so the flame and the rim keep pace with the guard itself (PhoenixRise counts it on Time.time).
        age += Time.deltaTime;
        if (age >= Mathf.Max(FlameLife, guard)) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy()
    {
        shapes?.Destroy();
        if (rimHost != null) Destroy(rimHost);
    }

    // ---- The world's look ----

    private void Draw()
    {
        if (glow != null) glow.intensity = LightPeak * Window(age, .05f, .08f, .45f);
        if (rim != null)
        {
            // In over a tenth of a second, then one smooth fall to nothing as the guard runs out.
            float left = guard > 0f ? Mathf.Clamp01(1f - age / guard) : 0f;
            rim.color = FxShapes.With(RimTint, RimPeak * Ease.Out(age / .12f) * Mathf.Pow(left, .6f));
        }
        if (shapes == null) return;
        var view = Camera.main;
        eye = view != null ? view.transform : null;
        if (!shapes.Begin(view)) return;
        if (age < FlameLife)
        {
            Vector3 middle = feet + Vector3.up * .04f;
            // The ring runs out fast and eases into its reach, thinning as it goes.
            float run = Ease.Out(age / .32f), radius = Mathf.Lerp(.35f, reach, run), width = Mathf.Lerp(.32f, .1f, run);
            float a = Window(age, .03f, .14f, .42f);
            if (a > 0f)
            {
                shapes.Ring(middle, Vector3.right, Vector3.forward, radius, width * 3f, FxShapes.With(Ember, .22f * a), 96);
                shapes.Ring(middle, Vector3.right, Vector3.forward, radius, width, FxShapes.With(Gold, .85f * a), 96);
                shapes.Ring(middle, Vector3.right, Vector3.forward, radius, width * .3f, FxShapes.With(Color.white, .7f * a), 96);
                // The ground still glowing a step behind, where the fire has just passed.
                shapes.Ring(middle, Vector3.right, Vector3.forward, radius * .62f, width * .6f, FxShapes.With(Ember, .3f * a), 72);
            }
            DrawFlames(middle, radius);
            DrawSparks(middle);
        }
        shapes.End();
    }

    // Tongues of flame licking up along the running ring, each in its own moment: one swell and one fall, no flicker.
    private void DrawFlames(Vector3 middle, float radius)
    {
        for (int i = 0; i < Tongues; i++)
        {
            var flame = flames[i];
            float k = age - flame.Delay;
            if (k <= 0f || k >= .5f) continue;
            float lick = Mathf.Sin(Mathf.PI * k / .5f);
            float angle = (i + flame.Turn) * Mathf.PI * 2f / Tongues;
            Vector3 way = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 root = middle + way * (radius * (1f - flame.Inset));
            Vector3 tip = root + Vector3.up * (flame.Tall * lick) + way * (.12f * lick);
            shapes.Glow(root, tip, FxShapes.With(i % 4 == 0 ? Gold : Ember, .9f * lick * Clear(tip)), .03f, .14f);
        }
    }

    // Embers rising off the ground the fire crossed, drifting out and up, each fading on its way.
    private void DrawSparks(Vector3 middle)
    {
        for (int i = 0; i < Embers; i++)
        {
            var spark = sparks[i];
            float k = age - spark.Delay;
            if (k <= 0f || k >= spark.Life) continue;
            Vector3 way = new Vector3(Mathf.Cos(spark.Angle), 0f, Mathf.Sin(spark.Angle));
            Vector3 velocity = Vector3.up * spark.Up + way * spark.Drift;
            Vector3 point = middle + way * (reach * spark.From) + velocity * k;
            shapes.Glow(point - velocity * .05f, point, FxShapes.With(i % 3 == 0 ? Gold : Ember, (1f - k / spark.Life) * Clear(point)), .012f, .05f);
        }
    }

    // Fainter the nearer the middle of the view: none inside 10°, all of it past 22°, so nothing sits on the crosshair.
    private float Clear(Vector3 point)
    {
        if (eye == null) return 1f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 22f, Vector3.Angle(eye.forward, point - eye.position)));
    }

    // 0 before it starts, up over a (s), held h, down over b, falling fastest first: one rise and one fall.
    private static float Window(float t, float a, float h, float b)
    {
        if (t <= 0f) return 0f;
        if (t < a) return Ease.Out(t / a);
        if (t < a + h) return 1f;
        float o = (t - a - h) / Mathf.Max(.001f, b);
        return o < 1f ? (1f - o) * (1f - o) : 0f;
    }

    // ---- The rim on this PC's screen ----

    private void BuildRim()
    {
        rimHost = new GameObject("Phoenix Rim", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupOverlay(rimHost, RimOrder);
        var rect = FeelUI.Rect("Rim", rimHost.transform);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rim = rect.gameObject.AddComponent<RawImage>();
        rim.texture = RimTexture();
        rim.raycastTarget = false;
        rim.color = FxShapes.With(RimTint, 0f);
    }

    // White, clear in the middle half of the screen and thickening toward its edges and corners; the image's colour tints it.
    private static Texture2D RimTexture()
    {
        if (rimTexture != null) return rimTexture;
        const int width = 128, height = 72;
        rimTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Phoenix Rim", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width * 2f - 1f, v = (y + .5f) / height * 2f - 1f;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, 1.2f, Mathf.Sqrt(u * u + v * v)));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        rimTexture.SetPixels32(pixels);
        rimTexture.Apply(false, true);
        return rimTexture;
    }

    // ---- The sound ----

    // At this PC's ear, as one's own moment plays (MomentSound), at the moments' volume (MomentFx.csv Sound.volume).
    private static void Sound()
    {
        if (rush == null) rush = Resources.Load<AudioClip>("Audio/Moments/phoenix");
        if (rush == null) rush = Rush();
        var listener = GameAudio.Listener;
        Vector3 ear = listener != null ? listener.transform.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        GameAudio.PlayAt(rush, ear, AudioChannel.Combat, Mathf.Clamp01(MomentLook.Number("Sound", "volume", .9f)), 50f, 60f);
    }

    // A rush of flame swelling and falling away over a low thump, and a soft call rising a fifth above it: fire, then flight.
    private static AudioClip Rush()
    {
        var data = new float[(int)(Rate * 1.1f)];
        var random = new System.Random(6029);
        float low = 0f, high = 0f, previous = 0f, thump = 0f, call = 0f, over = 0f, top = 0f;
        float keep = Mathf.Exp(-2f * Mathf.PI * 120f / Rate);
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            // The flame: noise through a band opening from 250 Hz to 3 kHz as it swells, gone by about .8 s.
            float open = Mathf.Lerp(250f, 3000f, Mathf.Clamp01(t / .3f));
            low += (1f - Mathf.Exp(-2f * Mathf.PI * open / Rate)) * ((float)(random.NextDouble() * 2.0 - 1.0) - low);
            high = keep * (high + low - previous);
            previous = low;
            float sample = high * 1.6f * Mathf.Clamp01(t / .12f) * Mathf.Exp(-Mathf.Max(0f, t - .12f) / .28f);
            // The thump under it, 95 Hz falling to 40.
            thump = (thump + Mathf.Lerp(95f, 40f, Mathf.Clamp01(t / .3f)) / Rate) % 1f;
            sample += Mathf.Sin(thump * Mathf.PI * 2f) * .7f * Mathf.Exp(-t / .16f);
            // The call: a soft tone rising a fifth (523 to 784 Hz) from a tenth of a second in, its octave faint above.
            float c = t - .1f;
            if (c > 0f)
            {
                float pitch = 523f * Mathf.Pow(1.5f, Mathf.Clamp01(c / .35f));
                call = (call + pitch / Rate) % 1f;
                over = (over + pitch * 2f / Rate) % 1f;
                float swell = Mathf.Clamp01(c / .05f) * Mathf.Exp(-c / .4f);
                sample += (Mathf.Sin(call * Mathf.PI * 2f) * .16f + Mathf.Sin(over * Mathf.PI * 2f) * .05f) * swell;
            }
            data[i] = sample;
            top = Mathf.Max(top, Mathf.Abs(sample));
        }
        int fade = Rate / 40;
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] / Mathf.Max(.001f, top) * .9f * Mathf.Min(1f, (data.Length - 1 - i) / (float)fade);
        var clip = AudioClip.Create("Phoenix Rise", data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
