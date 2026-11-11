using TMPro;
using UnityEngine;

/// <summary>
/// A Schießhund's bounty claimed, seen in the world by everyone (ClassKit; the user, 2026-11-11: "내 화면에서만 나오는 게
/// 아니라 다른 사람들이 볼 때도 와 개쩐다 싶게 세계에서 이펙트가"), on every PC: the hunter's at once, the others as
/// PlayerNetworkCombat tells them. The Desperado's claim is the grand one: the shot's line burns teal from the muzzle to
/// the head; the head flashes white; teal sparks burst from it and gold coins spill out of it, the bounty paid out; a
/// pillar of teal light stands up from the body into the sky, seen across the map; two shock rings run out over the
/// ground; and BOUNTY slams in over it, turned to each eye, then rises as it fades. A plain bounty, a marked kill by
/// anything else, only spills its coins with a small flash. About a second and a half, nothing left lying (the punchy
/// look: short and flashy).
/// </summary>
public sealed class BountyBurst : MonoBehaviour
{
    // How long the grand burst and a plain one last (s).
    private const float GrandSeconds = 1.7f, PlainSeconds = 1.6f;
    // The pillar's height and widest (m); the rings' widest reach (m); the word's lift over the head (m) and its rise.
    private const float PillarHeight = 34f, PillarWidth = 1.5f, RingFar = 9f, RingNear = 6f, WordLift = 1.1f, WordRise = .9f;
    private static readonly Color Gold = new Color(1f, .8f, .22f, 1f), White = new Color(1f, 1f, 1f, 1f);
    private static Texture2D coin;

    private LineRenderer beam, beamCore, pillar, pillarCore, ringFar, ringNear;
    private ParticleSystem flash, sparks, coins;
    private TextMeshPro word;
    private Vector3 head, from, ground;
    private float age, life;
    private bool grand;

    /// <summary>
    /// A bounty claimed: <paramref name="head"/> where the round went in (or the head), <paramref name="from"/> where the
    /// shot came from, <paramref name="ground"/> the feet. grand: the Desperado's own claim; else a plain bounty's coins.
    /// </summary>
    public static void Play(Vector3 head, Vector3 from, Vector3 ground, bool grand)
    {
        if (!Application.isPlaying) return;
        var host = FeelAssets.CreateRuntimeRoot(grand ? "Bounty Burst" : "Bounty Coins");
        host.transform.position = head;
        var burst = host.AddComponent<BountyBurst>();
        burst.head = head;
        burst.from = from;
        burst.ground = ground;
        burst.grand = grand;
        burst.life = grand ? GrandSeconds : PlainSeconds;
        burst.Build();
        burst.Pose(0f);
    }

    private void Build()
    {
        var glow = FeelAssets.Particle(Texture2D.whiteTexture, true);
        var teal = PingMarks.BountyColor;
        flash = Emitter("Flash", FeelAssets.Particle(FeelAssets.SoftDot, true), 4, 0f, ParticleSystemRenderMode.Billboard);
        var flashColor = flash.colorOverLifetime; flashColor.enabled = true;
        flashColor.color = FeelAssets.Fade(White, new Color(teal.r, teal.g, teal.b, 0f));
        var flashSize = flash.sizeOverLifetime; flashSize.enabled = true;
        flashSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .5f, 1f, 1.3f));
        flash.Emit(new ParticleSystem.EmitParams { position = head, startSize = grand ? 5f : 2.2f, startLifetime = grand ? .32f : .2f,
            startColor = White, velocity = Vector3.zero }, 1);

        sparks = Emitter("Sparks", FeelAssets.Particle(FeelAssets.SoftDot, true), 160, .5f, ParticleSystemRenderMode.Stretch);
        var stretch = sparks.GetComponent<ParticleSystemRenderer>();
        stretch.velocityScale = .04f; stretch.lengthScale = 1.6f;
        var sparkSize = sparks.sizeOverLifetime; sparkSize.enabled = true;
        sparkSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var sparkDrag = sparks.limitVelocityOverLifetime; sparkDrag.enabled = true; sparkDrag.drag = 4f; sparkDrag.multiplyDragByParticleSize = false;
        int sparkCount = grand ? 90 : 24;
        for (int i = 0; i < sparkCount; i++)
            sparks.Emit(new ParticleSystem.EmitParams
            {
                position = head, velocity = Random.onUnitSphere * Random.Range(6f, grand ? 20f : 11f),
                startSize = Random.Range(.05f, .12f), startLifetime = Random.Range(.25f, .65f),
                startColor = Random.value < .3f ? White : teal
            }, 1);

        // The bounty paid out: gold coins thrown up out of the head, turning as they fall.
        coins = Emitter("Coins", FeelAssets.Particle(Coin, false), 80, 1.7f, ParticleSystemRenderMode.Billboard);
        var turn = coins.rotationOverLifetime; turn.enabled = true;
        turn.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
        var coinFade = coins.colorOverLifetime; coinFade.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(White, 0f), new GradientColorKey(White, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .75f), new GradientAlphaKey(0f, 1f) });
        coinFade.color = new ParticleSystem.MinMaxGradient(fade);
        int coinCount = grand ? 42 : 18;
        for (int i = 0; i < coinCount; i++)
        {
            Vector2 side = Random.insideUnitCircle * Random.Range(1.2f, grand ? 4.5f : 3f);
            coins.Emit(new ParticleSystem.EmitParams
            {
                position = head, velocity = new Vector3(side.x, Random.Range(4f, grand ? 9f : 6.5f), side.y),
                startSize = Random.Range(.13f, .2f), startLifetime = Random.Range(1f, 1.5f), startColor = Gold,
                rotation = Random.Range(0f, 360f)
            }, 1);
        }
        if (!grand) return;

        beam = Line("Beam", glow, LineAlignment.View, 2);
        beamCore = Line("Beam core", glow, LineAlignment.View, 2);
        pillar = Line("Pillar", glow, LineAlignment.View, 2);
        pillarCore = Line("Pillar core", glow, LineAlignment.View, 2);
        ringFar = Ring("Ring", glow);
        ringNear = Ring("Ring inside", glow);

        // BOUNTY over the body, in the counter's own type with a white rim, the mark's teal.
        word = new GameObject("BOUNTY", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
        word.transform.SetParent(transform, false);
        var font = GunFeelSettings.Instance.scoreFont != null ? GunFeelSettings.Instance.scoreFont : CombatUIAssets.ResolveFont(null);
        if (font != null) word.font = font;
        var rim = CombatUIAssets.Outlined(word.font, White, .22f);
        if (rim != null) word.fontSharedMaterial = rim;
        word.text = "BOUNTY";
        word.fontSize = 11f;
        word.fontStyle = FontStyles.Bold | FontStyles.Italic;
        word.alignment = TextAlignmentOptions.Center;
        word.textWrappingMode = TextWrappingModes.NoWrap;
        word.rectTransform.sizeDelta = new Vector2(12f, 3f);
        word.color = teal;
    }

    private void Update()
    {
        // Real time: the hunter's own screen may be slowed by the kill.
        age += Time.unscaledDeltaTime;
        Pose(age);
        if (age >= life) Destroy(gameObject);
    }

    private void Pose(float t)
    {
        if (!grand) return;
        var teal = PingMarks.BountyColor;

        // The shot's line, muzzle to head: wide at once, gone in a third of a second.
        float shot = Mathf.Clamp01(t / .35f);
        Stretch(beam, from, head, Mathf.Lerp(.32f, 0f, shot), Tint(teal, (1f - shot) * .9f));
        Stretch(beamCore, from, head, Mathf.Lerp(.1f, 0f, shot), Tint(White, 1f - shot));

        // The pillar: up from the feet into the sky at once, widest for a moment, then thinning away.
        float rise = KitReveal.EaseOut(Mathf.Clamp01(t / .12f)), thin = Mathf.Clamp01((t - .1f) / 1f);
        Vector3 top = ground + Vector3.up * (PillarHeight * rise);
        float width = PillarWidth * (t < .1f ? Mathf.Lerp(.4f, 1f, t / .1f) : 1f - thin * thin);
        Stretch(pillar, ground, top, width, Tint(teal, .75f * (1f - thin)));
        Stretch(pillarCore, ground, top, width * .28f, Tint(White, 1f - thin));

        // Two shock rings running out over the ground, the second a moment after the first.
        Circle(ringFar, Mathf.Clamp01(t / .55f), RingFar, .5f, teal);
        Circle(ringNear, Mathf.Clamp01((t - .12f) / .5f), RingNear, .3f, White);

        // BOUNTY: slammed in from large, held, then rising as it fades; always turned to this PC's eye.
        if (word == null) return;
        float slam = KitReveal.EaseOut(Mathf.Clamp01(t / .15f)), leave = Mathf.Clamp01((t - (life - .55f)) / .55f);
        word.transform.position = head + Vector3.up * (WordLift + WordRise * leave * leave);
        word.transform.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, slam);
        var eye = Camera.main;
        if (eye != null)
        {
            Vector3 away = word.transform.position - eye.transform.position;
            if (away.sqrMagnitude > 1e-4f) word.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
        word.alpha = 1f - leave;
    }

    private static Color Tint(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    private static void Stretch(LineRenderer line, Vector3 a, Vector3 b, float width, Color color)
    {
        if (line == null) return;
        line.enabled = width > .001f && color.a > .003f;
        if (!line.enabled) return;
        line.SetPosition(0, a);
        line.SetPosition(1, b);
        line.widthMultiplier = width;
        line.startColor = line.endColor = color;
    }

    // A ring out over the ground to <reach>, thinning and fading as it goes.
    private void Circle(LineRenderer line, float k, float reach, float thick, Color color)
    {
        if (line == null) return;
        line.enabled = k > 0f && k < 1f;
        if (!line.enabled) return;
        float radius = Mathf.Lerp(.4f, reach, KitReveal.EaseOut(k));
        line.widthMultiplier = Mathf.Lerp(thick, .04f, k);
        line.startColor = line.endColor = Tint(color, .9f * (1f - k * k));
        int points = line.positionCount;
        Vector3 at = ground + Vector3.up * .12f;
        for (int i = 0; i < points; i++)
        {
            float angle = 2f * Mathf.PI * i / points;
            line.SetPosition(i, at + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private ParticleSystem Emitter(string name, Material material, int most, float gravity, ParticleSystemRenderMode mode)
    {
        var system = FeelAssets.CreateEmitter(name, transform, material, most, gravity, mode);
        return system;
    }

    private LineRenderer Line(string name, Material material, LineAlignment alignment, int points)
    {
        var host = new GameObject(name);
        host.transform.SetParent(transform, false);
        var line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points;
        line.alignment = alignment;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        line.enabled = false;
        return line;
    }

    // A loop laid flat on the ground (the line faces up), as the taunt's ring is.
    private LineRenderer Ring(string name, Material material)
    {
        var line = Line(name, material, LineAlignment.TransformZ, 72);
        line.loop = true;
        line.numCapVertices = 0;
        line.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        return line;
    }

    // A gold coin: a bright disc with a darker rim and a raised ring inside, white so the particle's colour tints it.
    private static Texture2D Coin
    {
        get
        {
            if (coin != null) return coin;
            const int Size = 64;
            coin = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
                { name = "Bounty Coin", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + .5f) / Size * 2f - 1f, v = (y + .5f) / Size * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                float alpha = Mathf.Clamp01((1f - r) * Size * .5f);
                float shade = r > .82f ? .62f : Mathf.Abs(r - .6f) < .06f ? .78f : 1f - .18f * (u + v) * .5f;
                byte level = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
                pixels[y * Size + x] = new Color32(level, level, level, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            coin.SetPixels32(pixels);
            coin.Apply(true, true);
            return coin;
        }
    }
}
