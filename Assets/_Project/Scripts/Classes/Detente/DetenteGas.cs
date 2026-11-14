using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One cloud of the Détente's ultimate's disinfectant gas (the user, 2026-11-14: "궁극기 사용 시 쓰러진 동료 즉시 일어나면서
/// 전체 동료 위치에서 메딕 소독 가스 3초 동안 분사되게. (왜냐하면 쓰러진 동료가 일어날 때 커버를 받아야 하니까)"). What the
/// gas does to zombies is ZombieEffects.Blind's, called once a cloud on the Détente's PC; this is only how it looks and
/// sounds, the same on every PC (same-world-view): the Détente's PC draws its own at once and tells the others where
/// (PlayerNetworkCombat.SecondLifeRpc), and the puffs are laid out from the spot itself, so every PC lays out the same.
/// One idea, soft and clean: a low white-mint mist sprayed out from someone's feet to the gas's reach, and a thin line in
/// the class's mint on the ground at that reach (inside it zombies lose whoever they were after), held for the gas's
/// seconds and thinning away, over a medical spray's hiss. Nothing blinks (no-strobe-blinks): it swells once and thins
/// once. It never covers a screen (vfx-restraint): the mist keeps low, a puff fades out as it nears an eye and over the
/// middle of the view, and an eye inside a cloud sees all of it fainter, so whoever is being covered still sees out.
/// </summary>
public sealed class DetenteGas : MonoBehaviour
{
    // Puffs in a cloud; how long the spray takes to reach its edge, how long before its end it starts to thin and how
    // long it lingers thinning past its end (s); the most one puff shows (alpha).
    private const int Puffs = 24, Rate = 44100;
    private const float Spread = .45f, ThinBefore = .8f, Tail = .45f, PuffAlpha = .3f;
    // A puff is gone from an eye this near (m past its own half-size) and all there this far; the middle of the view is
    // clear inside ClearFrom degrees and the puff all there past ClearTo (both widened by the puff's own size); and how
    // much of the mist an eye inside the cloud sees.
    private const float NearGone = .5f, NearFull = 2.4f, ClearFrom = 9f, ClearTo = 21f, Inside = .55f;
    private static readonly Color Pale = new Color(.9f, 1f, .95f), MintFallback = new Color(.49f, .94f, .69f);
    // The made hiss, and the gas seconds it was made for (a file in Resources plays in its place).
    private static AudioClip spray;
    private static float sprayFor;

    // One puff of mist: which way out (rad) and how far (a share of the reach), how high its middle sits and how big it
    // is (m), how fast it drifts on out and up (m/s), how late it comes (s), its turn and spin (rad, rad/s), and how much
    // of the mint it carries (0-1).
    private struct Puff { public float Angle, Out, Height, Size, Drift, Rise, Delay, Turn, Spin, Mint; }

    private readonly Puff[] puffs = new Puff[Puffs];
    private readonly List<Vector3> vertices = new List<Vector3>(Puffs * 4);
    private readonly List<Color32> colors = new List<Color32>(Puffs * 4);
    private readonly List<Vector2> uvs = new List<Vector2>(Puffs * 4);
    private readonly List<int> triangles = new List<int>(Puffs * 6);
    private Transform mistHost;
    private Mesh mesh;
    private FxShapes lines;
    private Vector3 center;
    private Color mint;
    private float radius, seconds, age;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        spray = null;
        sprayFor = 0f;
    }

    /// <summary>
    /// A cloud bursts at <paramref name="at"/>, <paramref name="reach"/> m round, for <paramref name="lasts"/> s: its look
    /// and its hiss on this PC (what it does to zombies is the caller's ZombieEffects.Blind).
    /// </summary>
    public static void Burst(Vector3 at, float reach, float lasts)
    {
        if (!Application.isPlaying) return;
        var host = FeelAssets.CreateRuntimeRoot("Detente Gas");
        host.transform.position = at;
        var gas = host.AddComponent<DetenteGas>();
        gas.center = at;
        gas.radius = Mathf.Max(.5f, reach);
        gas.seconds = Mathf.Max(.5f, lasts);
        gas.Begin();
    }

    private void Begin()
    {
        // The class's own colour (ClassColours.csv detente, the disinfectant's mint).
        mint = KillFlairs.ClassColourOf(PlayerClass.Detente);
        if (mint.a <= 0f) mint = MintFallback;
        mint.a = 1f;
        // Rolled from where it is, so every PC lays the same puffs out round the same spot.
        var random = new System.Random(Mathf.RoundToInt(center.x * 73f + center.y * 151f + center.z * 37f));
        float Next() => (float)random.NextDouble();
        for (int i = 0; i < Puffs; i++)
            puffs[i] = new Puff
            {
                Angle = (i + .8f * Next()) * Mathf.PI * 2f / Puffs, Out = Mathf.Sqrt(.08f + .92f * Next()) * .9f,
                Height = .1f + .55f * Next(), Size = 1f + .8f * Next(), Drift = .06f + .18f * Next(), Rise = .02f + .04f * Next(),
                Delay = .12f * Next(), Turn = Next() * Mathf.PI * 2f, Spin = (Next() - .5f) * .5f, Mint = Next()
            };
        mistHost = new GameObject("Mist").transform;
        mistHost.SetParent(transform, false);
        mesh = new Mesh { name = "Detente Gas", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        mistHost.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = mistHost.gameObject.AddComponent<MeshRenderer>();
        // Matter, not light: blended, so the mist is pale over a dark place and never glows.
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, false);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        lines = new FxShapes(transform, "Edge", true);
        Hiss();
        Draw();
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= seconds + Tail) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy()
    {
        lines?.Destroy();
        if (mesh != null) Destroy(mesh);
    }

    // ---- The look ----

    private void Draw()
    {
        var view = Camera.main;
        // One swell as it sprays out and one thinning at its end, nothing in between.
        float spread = Ease.Out(age / Spread);
        float thin = Mathf.Clamp01((age - (seconds - ThinBefore)) / (ThinBefore + Tail));
        DrawMist(view != null ? view.transform : null, spread, (1f - thin) * (1f - thin));
        if (lines == null || !lines.Begin(view)) return;
        Vector3 ground = center + Vector3.up * .05f;
        // Its reach on the ground, where zombies lose their quarry: a thin mint line running out with the spray.
        float reach = radius * Mathf.Lerp(.3f, 1f, spread), shown = Ease.Out(age / .2f) * Mathf.Pow(1f - thin, 1.2f);
        lines.Ring(ground, Vector3.right, Vector3.forward, reach, .22f, FxShapes.With(mint, .14f * shown), 96);
        lines.Ring(ground, Vector3.right, Vector3.forward, reach, .05f, FxShapes.With(mint, .6f * shown), 96);
        // The spray's first push: one pale ring running out ahead of the mist, gone as it gets there.
        float push = age / Spread;
        if (push < 1f)
            lines.Ring(ground, Vector3.right, Vector3.forward, Mathf.Lerp(.2f, radius, Ease.Out(push)), .12f,
                FxShapes.With(Pale, .35f * (1f - push) * (1f - push)), 72);
        lines.End();
    }

    // The puffs, each a soft blob turned to the eye: out from the middle as the spray goes, drifting on, thinning at the
    // end. Each fades as it nears the eye and over the middle of the view; from inside the cloud all of them are fainter.
    private void DrawMist(Transform eye, float spread, float left)
    {
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
        if (eye != null && left > 0f)
        {
            Vector3 eyeAt = eye.position, right = eye.right, up = eye.up, forward = eye.forward, off = eyeAt - center;
            float inside = Mathf.Abs(off.y) < 3f && off.x * off.x + off.z * off.z < radius * radius ? Inside : 1f;
            for (int i = 0; i < Puffs; i++)
            {
                var puff = puffs[i];
                float t = age - puff.Delay;
                if (t <= 0f) continue;
                Vector3 way = new Vector3(Mathf.Cos(puff.Angle), 0f, Mathf.Sin(puff.Angle));
                float outward = radius * puff.Out * Mathf.Lerp(.3f, 1f, spread) + puff.Drift * t;
                Vector3 at = center + way * outward + Vector3.up * (puff.Height + puff.Rise * t);
                float size = puff.Size * Mathf.Lerp(.5f, 1f, Ease.Out(t / (Spread + .3f)));
                float alpha = PuffAlpha * Ease.Out(t / .25f) * left * inside * Seen(eyeAt, forward, at, size);
                if (alpha <= .004f) continue;
                float half = size * .5f, turn = puff.Turn + puff.Spin * t, cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                Vector3 across = (right * cos + up * sin) * half, upward = (up * cos - right * sin) * half;
                int first = vertices.Count;
                vertices.Add(mistHost.InverseTransformPoint(at - across - upward));
                vertices.Add(mistHost.InverseTransformPoint(at - across + upward));
                vertices.Add(mistHost.InverseTransformPoint(at + across + upward));
                vertices.Add(mistHost.InverseTransformPoint(at + across - upward));
                Color32 shade = FxShapes.With(Color.Lerp(Pale, mint, .25f + .35f * puff.Mint), alpha);
                for (int k = 0; k < 4; k++) colors.Add(shade);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }
        }
        mesh.Clear();
        if (vertices.Count == 0) return;
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    // How much of a puff this eye sees: none of it right at the eye, all of it a few metres off, and none over the middle
    // of the view, so the crosshair and the way ahead stay clear even standing in the cloud.
    private static float Seen(Vector3 eye, Vector3 forward, Vector3 at, float size)
    {
        Vector3 to = at - eye;
        float distance = to.magnitude, half = size * .5f;
        float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NearGone + half, NearFull + half, distance));
        if (near <= 0f) return 0f;
        float own = Mathf.Atan2(half, Mathf.Max(.01f, distance)) * Mathf.Rad2Deg;
        return near * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClearFrom + own * .6f, ClearTo + own * .6f, Vector3.Angle(forward, to)));
    }

    // ---- The sound ----

    // A medical spray's hiss where it bursts, for everyone near it (at the moments' volume, MomentFx.csv Sound.volume).
    private void Hiss()
    {
        var clip = Resources.Load<AudioClip>("Audio/Moments/detenteGas");
        if (clip == null)
        {
            if (spray == null || Mathf.Abs(sprayFor - seconds) > .25f) { spray = Spray(seconds); sprayFor = seconds; }
            clip = spray;
        }
        GameAudio.PlayAt(clip, center + Vector3.up * .5f, AudioChannel.Combat, Mathf.Clamp01(MomentLook.Number("Sound", "volume", .9f)) * .6f, 3f, 40f);
    }

    // A can of disinfectant emptied in one go: a bright hiss bursting in and easing off over the gas's seconds, a soft
    // push of air under its start. Made once (a file at Resources/Audio/Moments/detenteGas plays in its place).
    private static AudioClip Spray(float lasts)
    {
        float length = lasts + .35f;
        int count = Mathf.CeilToInt(Rate * length);
        var data = new float[count];
        var random = new System.Random(4127);
        float low = 0f, previous = 0f, high = 0f, air = 0f, top = 0f;
        // A band of noise from about 1.8 kHz to 7 kHz: a one-pole low-pass, then a one-pole high-pass on what it lets by.
        float lowStep = 1f - Mathf.Exp(-2f * Mathf.PI * 7000f / Rate), highKeep = Mathf.Exp(-2f * Mathf.PI * 1800f / Rate);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            low += lowStep * ((float)(random.NextDouble() * 2.0 - 1.0) - low);
            high = highKeep * (high + low - previous);
            previous = low;
            // In over 30 ms, falling back from the first push to a steady hiss that eases off, out over its last .6 s.
            float body = Mathf.Clamp01(t / .03f) * (.55f + .45f * Mathf.Exp(-t / .18f)) * Mathf.Clamp01((length - t) / .6f)
                * Mathf.Lerp(1f, .6f, Mathf.Clamp01(t / Mathf.Max(.1f, lasts)));
            float sample = high * body;
            // The push of air under its start: 60 Hz, gone in about a tenth of a second.
            air = (air + 60f / Rate) % 1f;
            sample += Mathf.Sin(air * Mathf.PI * 2f) * .35f * Mathf.Clamp01(t / .01f) * Mathf.Exp(-t / .08f);
            data[i] = sample;
            top = Mathf.Max(top, Mathf.Abs(sample));
        }
        for (int i = 0; i < count; i++) data[i] = data[i] / Mathf.Max(.001f, top) * .8f;
        var clip = AudioClip.Create("Detente Gas", count, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
