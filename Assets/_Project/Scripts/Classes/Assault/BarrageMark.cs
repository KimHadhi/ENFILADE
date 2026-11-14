using UnityEngine;

/// <summary>
/// Where an Assault's barrage will fall, on every PC from the moment it is called until its shells come: its reach drawn
/// on the ground round the mark in red (a 데인저 클로즈 mark, or the Assault's own spot for a 브로큰 애로우), an arc closing
/// round it as the first shell comes nearer, a target at its middle and a hairline standing up out of it, so everyone
/// knows where not to be and when (the user's rule: what one screen shows, every screen shows). It breathes gently
/// rather than blinking, and goes as the shells start landing; each shell then has its own look (CardFx Shell).
/// </summary>
public sealed class BarrageMark : MonoBehaviour
{
    // Fire called down: a warm red, apart from a CHARGE's gold and a preview's blue.
    private static readonly Color Red = new Color(1f, .32f, .24f);
    private const int Points = 64;
    // How long it takes to come up, and to go once the first shell is down (s).
    private const float Rise = .2f, Fade = .5f;

    private readonly Vector3[] ring = new Vector3[Points];
    private Vector3 middle, facing;
    private float radius, delay, age, life;
    private FxShapes glow;

    /// <summary>
    /// A barrage called on <paramref name="centre"/>: <paramref name="reach"/> is the way it is drawn facing, as long as
    /// its reach (m); its first shell comes <paramref name="delay"/> seconds from now.
    /// </summary>
    public static void Play(Vector3 centre, Vector3 reach, float delay)
    {
        if (!Application.isPlaying) return;
        var host = FeelAssets.CreateRuntimeRoot("Barrage Mark");
        host.transform.position = centre;
        host.AddComponent<BarrageMark>().Begin(centre, reach, delay);
    }

    private void Begin(Vector3 centre, Vector3 reach, float wait)
    {
        middle = centre;
        radius = Mathf.Max(1f, reach.magnitude);
        Vector3 flat = Vector3.ProjectOnPlane(reach, Vector3.up);
        facing = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        delay = Mathf.Max(.05f, wait);
        life = delay + Fade;
        // The ground does not move: its ring is set down on it once.
        AssaultStrikes.RingOnGround(middle, radius, ring);
        glow = new FxShapes(transform, "Light", true);
        Draw();
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        Draw();
    }

    private void OnDestroy() => glow?.Destroy();

    private void Draw()
    {
        if (glow == null || !glow.Begin(Camera.main)) return;
        float shown = Mathf.Clamp01(age / Rise) * (age > delay ? 1f - Mathf.Clamp01((age - delay) / Fade) : 1f);
        // A slow breath (under one a second, to about two thirds), never on and off.
        float breathe = .78f + .22f * Mathf.Sin(age * Mathf.PI * 2f * .8f);
        float closing = Mathf.Clamp01(age / delay);
        AssaultStrikes.DrawRing(glow, middle, ring, .07f, FxShapes.With(Red, .5f * shown * breathe));
        // The arc that closes round as the first shell comes nearer: whole as it lands.
        AssaultStrikes.DrawRing(glow, middle, ring, .14f, FxShapes.With(Color.Lerp(Red, Color.white, .35f), .9f * shown), closing);
        // Eight short ticks pointing in from the reach, like a sight's.
        for (int i = 0; i < Points; i += Points / 8)
        {
            Vector3 inward = Vector3.ProjectOnPlane(middle - ring[i], Vector3.up);
            if (inward.sqrMagnitude < 1e-4f) continue;
            inward.Normalize();
            glow.Strip(ring[i], ring[i] + inward * .7f, Vector3.Cross(Vector3.up, inward), .07f, FxShapes.With(Red, .7f * shown));
        }
        Vector3 floor = middle + Vector3.up * .05f;
        Vector3 across = Vector3.Cross(Vector3.up, facing);
        glow.Sigil(MomentLook.Sigil.Target, floor, across, facing, 1.4f, Mathf.Clamp01(age / .25f), FxShapes.With(Red, .85f * shown), .03f, .1f, age * .6f);
        // A hairline standing up out of its middle, so it is found from far off and over cover.
        glow.Glow(floor, floor + Vector3.up * 14f, FxShapes.With(Red, .35f * shown * breathe), .02f, .08f);
        glow.End();
    }
}
