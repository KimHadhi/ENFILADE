using UnityEngine;

/// <summary>
/// A Knight's cover step seen (KnightCover, on every PC): a white streak from where the Knight stood to the friend's
/// side, a bright flash where it lands, gone in a third of a second, as the HUD's hits are (short and flashy, nothing
/// left lying about). The Knight's own PC draws it as it steps; the others as PlayerNetworkCombat tells them.
/// </summary>
public sealed class CoverStreak : MonoBehaviour
{
    // How long it shows (s), how high over the feet it is drawn (m), how thick it starts (m).
    private const float Seconds = .35f, Lift = 1.1f, Width = .22f;

    private LineRenderer line;
    private Light flash;
    private float age;

    public static void Show(Vector3 from, Vector3 to)
    {
        if (!Application.isPlaying) return;
        var host = new GameObject("Cover Streak") { hideFlags = HideFlags.DontSave };
        var streak = host.AddComponent<CoverStreak>();
        var line = streak.line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
        line.SetPosition(0, from + Vector3.up * Lift);
        line.SetPosition(1, to + Vector3.up * Lift);
        streak.flash = new GameObject("Flash").AddComponent<Light>();
        streak.flash.transform.SetParent(host.transform, false);
        streak.flash.transform.position = to + Vector3.up * Lift;
        streak.flash.type = LightType.Point;
        streak.flash.shadows = LightShadows.None;
        streak.flash.color = Color.white;
        streak.flash.range = 5f;
        streak.Draw(0f);
    }

    private void Update()
    {
        age += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(age / Seconds);
        Draw(k);
        if (k >= 1f) Destroy(gameObject);
    }

    // Thin and fading as it goes; the far end (the landing) brightest.
    private void Draw(float k)
    {
        float fade = 1f - k * k;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, Width * .3f * fade), new Keyframe(1f, Width * fade));
        line.startColor = KitStyle.With(Color.white, .35f * fade);
        line.endColor = KitStyle.With(Color.white, .95f * fade);
        flash.intensity = 3f * fade;
    }
}
