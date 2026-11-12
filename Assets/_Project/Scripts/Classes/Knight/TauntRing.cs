using UnityEngine;

/// <summary>
/// A Knight's taunt seen (ClassKit.ReceiveTaunt, on every PC): a pink ring sweeps out over the ground from the Knight to
/// the taunt's reach and fades, so the team sees how far it pulled. Gone in half a second, as the HUD's hits are
/// (short and flashy, nothing left lying about).
/// </summary>
public sealed class TauntRing : MonoBehaviour
{
    private const int Points = 72;
    // How long it sweeps (s), how high over the ground it is drawn (m), how thick it starts and ends (m), how far out it
    // starts (m).
    private const float Seconds = .5f, Lift = .1f, WidthFrom = .5f, WidthTo = .12f, StartReach = 1f;

    private LineRenderer line;
    private Vector3 at;
    private float reach, age;

    public static void Show(Vector3 at, float reach)
    {
        if (!Application.isPlaying || reach <= 0f) return;
        var host = new GameObject("Taunt Ring") { hideFlags = HideFlags.DontSave };
        var ring = host.AddComponent<TauntRing>();
        ring.at = at + Vector3.up * Lift;
        ring.reach = reach;
        var line = ring.line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = Points;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        // Laid on the ground rather than turned to the eye, so it reads as a circle on the floor from any height.
        line.alignment = LineAlignment.TransformZ;
        host.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        ring.Draw(0f);
    }

    private void Update()
    {
        age += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(age / Seconds);
        Draw(k);
        if (k >= 1f) Destroy(gameObject);
    }

    // Out fast and easing, fading as it goes, thinning as it widens.
    private void Draw(float k)
    {
        float eased = KitReveal.EaseOut(k), radius = Mathf.Lerp(StartReach, reach, eased);
        line.widthMultiplier = Mathf.Lerp(WidthFrom, WidthTo, eased);
        Color color = KitStyle.With(KitStyle.Accent, .85f * (1f - k * k));
        line.startColor = line.endColor = color;
        for (int i = 0; i < Points; i++)
        {
            float angle = 2f * Mathf.PI * i / Points;
            line.SetPosition(i, at + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }
}
