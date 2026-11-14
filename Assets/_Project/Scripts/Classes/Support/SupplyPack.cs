using System;
using UnityEngine;

/// <summary>
/// A Support's supply pack in the air (the throw card, 보급 꾸러미 던지기, from the user's card feedback of 2026-11-14:
/// hold the wheel, look at a friend, F throws them a pack). A small olive pouch with a blue tag tumbles in an arc from
/// the Support's off hand to the friend's chest, homing on them as they move, a thin blue streak behind it; at the chest
/// it is caught: it shrinks into them, a soft blue ring opens there once and the pouch thumps. Every PC flies its own
/// the same way, the Support's at once and the others as PlayerNetworkCombat tells them (memory same-world-view), and
/// only the friend's own PC fills their guns when it lands there (SupportKit.Tossed). Small, short and nothing left
/// lying (memory vfx-restraint): no flash, no light, the ring a single smooth fade (memory no-strobe-blinks).
/// </summary>
[DisallowMultipleComponent]
public sealed class SupplyPack : MonoBehaviour
{
    // The pouch's size (m: across, up, through). The arc rises this much over each metre thrown, within the least and
    // most (m), so a short toss stays low and a long one lobs. It tumbles this fast (degrees a second); its streak starts
    // this far into the flight (share), so nothing streaks across the thrower's own view as it leaves the hand.
    private static readonly Vector3 Size = new Vector3(.3f, .18f, .22f);
    private const float ArcPerMetre = .11f, ArcLeast = .2f, ArcMost = 1f, Tumble = 380f, StreakFrom = .15f;
    // The catch: the pouch shrinks into the chest over Swallow (s); the ring opens from RingStart to RingWidest (m) over
    // RingSeconds (s), fading from RingAlpha to nothing; the streak's strongest alpha, how long it trails (s), how wide.
    private const float Swallow = .08f, RingStart = .12f, RingWidest = .42f, RingSeconds = .3f, RingAlpha = .5f, RingWidth = .022f,
        StreakAlpha = .35f, StreakSeconds = .14f, StreakWidth = .035f;
    // The sounds' loudness and how far they carry (m).
    private const float ThumpVolume = .55f, WhooshVolume = .3f, SoundNear = 1.5f, SoundFar = 20f;
    private const int RingPoints = 40;
    private static readonly Color Olive = new Color(.29f, .33f, .2f), Strap = new Color(.13f, .14f, .12f);
    private static Material canvasPaint, strapPaint, tagPaint;
    private static AudioClip thump, whoosh;

    private PlayerMovement target;
    private Vector3 from, end, axis;
    private float seconds, age, lift, caughtAge = -1f;
    private Action onCaught;
    private Transform model;
    private TrailRenderer streak;
    private LineRenderer ring;
    private Color tint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        canvasPaint = strapPaint = tagPaint = null;
        thump = whoosh = null;
    }

    /// <summary>
    /// Flies a pack from <paramref name="start"/> to <paramref name="friend"/>'s chest in <paramref name="flight"/> seconds,
    /// following them as they move, and calls <paramref name="caught"/> when it lands there (never if they are gone by
    /// then: it lands where they last were, and only the look plays).
    /// </summary>
    public static void Fly(Vector3 start, PlayerMovement friend, float flight, Action caught)
    {
        if (!Application.isPlaying || friend == null) return;
        var host = FeelAssets.CreateRuntimeRoot("Supply Pack");
        host.transform.position = start;
        var pack = host.AddComponent<SupplyPack>();
        pack.target = friend;
        pack.from = start;
        pack.end = ChestOf(friend);
        pack.seconds = Mathf.Max(.05f, flight);
        pack.lift = Mathf.Clamp(Vector3.Distance(start, pack.end) * ArcPerMetre, ArcLeast, ArcMost);
        pack.onCaught = caught;
        pack.tint = MomentLook.ColorOf(PlayerClass.Support);
        // A tumble about a tilted axis, as a bag spins off the hand, never the same twice.
        pack.axis = (Vector3.right + UnityEngine.Random.insideUnitSphere * .5f).normalized;
        pack.Build();
        GameAudio.PlayAt(Whoosh(), start, AudioChannel.Combat, WhooshVolume, SoundNear, SoundFar);
    }

    // Where a friend catches it: their body's chest (any humanoid, standing, crouched or down), or a chest's height over
    // their feet when the body isn't there.
    private static Vector3 ChestOf(PlayerMovement who)
    {
        if (who.TryGetComponent(out PlayerBody body) && body.BodyAnimator != null && body.BodyAnimator.isHuman)
        {
            var bone = body.BodyAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (bone == null) bone = body.BodyAnimator.GetBoneTransform(HumanBodyBones.Chest);
            if (bone != null) return bone.position;
        }
        return who.transform.position + Vector3.up * 1.1f;
    }

    // The pouch (a box with a strap round it and a blue tag on its face) and its streak.
    private void Build()
    {
        model = new GameObject("Pouch").transform;
        model.SetParent(transform, false);
        Box("Body", Vector3.zero, Size, Paint(ref canvasPaint, Olive, false));
        Box("Strap", Vector3.zero, new Vector3(.035f, Size.y + .01f, Size.z + .01f), Paint(ref strapPaint, Strap, false));
        Box("Tag", new Vector3(Size.x * .25f, 0f, Size.z * .5f + .004f), new Vector3(.07f, .05f, .006f), Paint(ref tagPaint, tint, true));
        streak = gameObject.AddComponent<TrailRenderer>();
        streak.time = StreakSeconds;
        streak.minVertexDistance = .05f;
        streak.widthMultiplier = StreakWidth;
        streak.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        streak.colorGradient = Fade(tint, StreakAlpha);
        streak.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        streak.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        streak.receiveShadows = false;
        streak.emitting = false;
    }

    private void Box(string name, Vector3 at, Vector3 size, Material material)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(model, false);
        part.transform.localPosition = at;
        part.transform.localScale = size;
        part.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        if (caughtAge >= 0f)
        {
            Caught();
            return;
        }
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / seconds);
        if (target != null) end = ChestOf(target);
        // The straight way to the chest as it is now, raised by a parabola that is zero at both ends.
        transform.position = Vector3.Lerp(from, end, t) + Vector3.up * (4f * lift * t * (1f - t));
        model.Rotate(axis, Tumble * Time.deltaTime, Space.Self);
        if (t >= StreakFrom) streak.emitting = true;
        if (t < 1f) return;
        // Landed: the friend's own PC takes what is in it (SupportKit.Tossed); everyone sees and hears the catch.
        caughtAge = 0f;
        streak.emitting = false;
        GameAudio.PlayAt(Thump(), end, AudioChannel.Combat, ThumpVolume, SoundNear, SoundFar);
        Ring();
        if (target != null) onCaught?.Invoke();
        onCaught = null;
    }

    // After the catch: the pouch shrinks into the chest, the ring opens and fades once, and then it is all gone.
    private void Caught()
    {
        caughtAge += Time.deltaTime;
        if (target != null) end = ChestOf(target);
        transform.position = end;
        model.localScale = Vector3.one * Mathf.Clamp01(1f - caughtAge / Swallow);
        float share = Mathf.Clamp01(caughtAge / RingSeconds);
        // Eased out: quick to open, slow to settle, and its alpha falls off smoothly to nothing.
        if (ring != null) Spread(Mathf.Lerp(RingStart, RingWidest, 1f - (1f - share) * (1f - share)), RingAlpha * (1f - share) * (1f - share));
        if (caughtAge >= Mathf.Max(RingSeconds, StreakSeconds)) Destroy(gameObject);
    }

    // A thin ring lying flat round the chest, opened by Spread.
    private void Ring()
    {
        ring = new GameObject("Catch Ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = RingPoints;
        ring.widthMultiplier = RingWidth;
        ring.alignment = LineAlignment.TransformZ;
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        Spread(RingStart, RingAlpha);
    }

    // The ring at this radius (m) and alpha; its points are written again rather than its frame scaled, so the line keeps
    // its width as it opens.
    private void Spread(float radius, float alpha)
    {
        for (int i = 0; i < RingPoints; i++)
        {
            float angle = 2f * Mathf.PI * i / RingPoints;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
        var color = tint;
        color.a = alpha;
        ring.startColor = ring.endColor = color;
    }

    private static Gradient Fade(Color color, float alpha)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    // The pouch's canvas and strap, and its tag lit in the Support's blue, made once each.
    private static Material Paint(ref Material made, Color color, bool glow)
    {
        if (made != null) return made;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        made = new Material(shader) { name = "Supply Pack (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", color);
        if (made.HasProperty("_Color")) made.SetColor("_Color", color);
        if (made.HasProperty("_Smoothness")) made.SetFloat("_Smoothness", glow ? .5f : .12f);
        if (glow && made.HasProperty("_EmissionColor"))
        {
            made.EnableKeyword("_EMISSION");
            made.SetColor("_EmissionColor", color * 1.4f);
        }
        return made;
    }

    // The pouch caught against a chest, made once: a soft low thud and a short rustle of canvas.
    private static AudioClip Thump()
    {
        if (thump != null) return thump;
        const int Rate = 44100;
        var data = new float[Rate * 22 / 100];
        var random = new System.Random(2417);
        float held = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / Rate;
            float thud = Mathf.Sin(2f * Mathf.PI * 115f * t) * Mathf.Exp(-t * 30f);
            held = Mathf.Lerp(held, (float)(random.NextDouble() * 2.0 - 1.0), .25f);
            float rustle = held * Mathf.Exp(-t * 22f) * .45f;
            data[i] = (thud * .85f + rustle) * .8f;
        }
        thump = AudioClip.Create("Supply Pack Thump", data.Length, 1, Rate, false);
        thump.SetData(data, 0);
        return thump;
    }

    // The pouch leaving the hand, made once: a short breath of air that swells and falls away.
    private static AudioClip Whoosh()
    {
        if (whoosh != null) return whoosh;
        const int Rate = 44100;
        var data = new float[Rate * 28 / 100];
        var random = new System.Random(7351);
        float low = 0f, lower = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            // Two soft low-passes, the difference a rough band of air.
            low = Mathf.Lerp(low, noise, .3f);
            lower = Mathf.Lerp(lower, noise, .05f);
            data[i] = (low - lower) * Mathf.Sin(Mathf.PI * t) * .9f;
        }
        whoosh = AudioClip.Create("Supply Pack Whoosh", data.Length, 1, Rate, false);
        whoosh.SetData(data, 0);
        return whoosh;
    }
}
