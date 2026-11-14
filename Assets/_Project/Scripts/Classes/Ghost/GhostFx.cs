using UnityEngine;

/// <summary>
/// A Ghost's 암살 연쇄 step seen, on every PC (GhostNet; the user: "걔 쪽으로 바람 이펙트와 함께 텔레포트하면서"): a short gust
/// from where it stood to behind its next zombie. A few thin pale lines of wind sweep along the way at once and thin out
/// behind it, a small swirl opens on the floor where it lands, and a soft rush of air goes with it (made here once). Gone
/// in under half a second, nothing left lying, nothing over the screen (the user's taste for short, clean moments,
/// 2026-11-12). The Ghost's own PC leaves out the last stretch, where its eyes now are.
/// </summary>
public sealed class GhostFx : MonoBehaviour
{
    // How long it shows (s), how long its head takes to run the whole way (s), how many lines of wind, and how far short of
    // the eyes the Ghost's own lines stop (m).
    private const float Life = .42f, Run = .11f, Short = 1.3f;
    private const int Lines = 5, Rate = 44100;
    private static AudioClip rush;

    private FxShapes shapes;
    private Vector3 from, to;
    private float age;
    private bool mine;
    private int seed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => rush = null;

    /// <summary>The step from <paramref name="start"/> to <paramref name="end"/> (feet); <paramref name="own"/>: this PC's Ghost took it.</summary>
    public static void Streak(Vector3 start, Vector3 end, bool own)
    {
        if (!Application.isPlaying || (end - start).sqrMagnitude < .01f) return;
        var host = FeelAssets.CreateRuntimeRoot("Ghost Chain");
        host.transform.position = end;
        var fx = host.AddComponent<GhostFx>();
        fx.from = start;
        fx.to = end;
        fx.mine = own;
        fx.seed = Mathf.Abs(Mathf.RoundToInt(end.x * 73f + end.y * 151f + end.z * 37f)) % 9973;
        fx.shapes = new FxShapes(host.transform, "Wind", true);
        fx.Draw();
        // The rush of air: at the ear for one's own step, else where it lands.
        if (own)
        {
            var listener = GameAudio.Listener;
            Vector3 ear = listener != null ? listener.transform.position : Camera.main != null ? Camera.main.transform.position : end;
            GameAudio.PlayAt(Rush(), ear, AudioChannel.Combat, .55f, 50f, 60f);
        }
        else GameAudio.PlayAt(Rush(), end + Vector3.up, AudioChannel.Combat, .6f, 3f, 40f);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= Life) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy() => shapes?.Destroy();

    // The lines run from where it stood, their heads at the landing in a blink, their tails following and thinning out.
    private void Draw()
    {
        if (shapes == null || !shapes.Begin()) return;
        Vector3 path = to - from;
        float length = path.magnitude;
        Vector3 way = path / Mathf.Max(.01f, length);
        Vector3 side = Vector3.Cross(Vector3.up, way);
        side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
        float head = Ease.Out(age / Run), tail = Ease.InOut((age - .04f) / (Life * .8f));
        float fade = 1f - Mathf.Clamp01((age - Life * .5f) / (Life * .5f));
        float end = mine ? Mathf.Clamp01(1f - Short / Mathf.Max(.01f, length)) : 1f;
        Color tint = MomentLook.ColorOf(PlayerClass.Ghost);
        var random = new System.Random(seed);
        for (int i = 0; i < Lines; i++)
        {
            float lift = .4f + 1.2f * (float)random.NextDouble(), across = ((float)random.NextDouble() - .5f) * .8f;
            float bend = ((float)random.NextDouble() - .5f) * .6f, width = Mathf.Lerp(.02f, .045f, (float)random.NextDouble());
            float a = Mathf.Min(tail, end), b = Mathf.Min(head, end);
            if (b - a < .01f) continue;
            Vector3 line = from + side * across + Vector3.up * lift;
            shapes.Blade(line + path * a, line + path * b, width, FxShapes.With(tint, .5f * fade), side * bend * (b - a), 10, 1f);
        }
        // A small swirl opening on the floor where it lands, as the gust settles.
        float swirl = Mathf.Clamp01((age - Run * .5f) / .3f);
        if (swirl > 0f && swirl < 1f)
            shapes.Ring(to + Vector3.up * .05f, Vector3.right, Vector3.forward, Mathf.Lerp(.3f, .95f, Ease.Out(swirl)), .035f,
                FxShapes.With(tint, .45f * (1f - swirl)), 40);
        shapes.End();
    }

    // A soft rush of air: noise through a low pass that closes as it passes, swelling in and dying away.
    private static AudioClip Rush()
    {
        if (rush != null) return rush;
        int count = (int)(Rate * .4f);
        var data = new float[count];
        var random = new System.Random(6211);
        float low = 0f, lower = 0f, top = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float open = Mathf.Lerp(.32f, .03f, Mathf.Clamp01(t / .34f));
            low += ((float)random.NextDouble() * 2f - 1f - low) * open;
            lower += (low - lower) * open * .5f;
            float swell = Mathf.Clamp01(t / .05f) * Mathf.Exp(-Mathf.Max(0f, t - .05f) / .11f);
            data[i] = (low - lower * .5f) * swell;
            top = Mathf.Max(top, Mathf.Abs(data[i]));
        }
        int fadeOut = Rate / 100;
        for (int i = 0; i < count; i++)
            data[i] = data[i] / Mathf.Max(.0001f, top) * .8f * Mathf.Min(1f, (count - 1 - i) / (float)fadeOut);
        rush = AudioClip.Create("Ghost Rush", count, 1, Rate, false);
        rush.SetData(data, 0);
        rush.hideFlags = HideFlags.HideAndDontSave;
        return rush;
    }
}
