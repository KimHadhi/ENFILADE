using UnityEngine;

/// <summary>
/// A Knight's cover step seen, on every PC (KnightCover). The user's note on the special-moment picks: "순간이동 느낌이
/// 안 나게 이동할 때는 회피처럼 빠른 잔상이 있어서 아 저기서 저기로 이동했구나 느낌이 나게".
/// It reads as a dash, not a teleport. Afterimages of the Knight's own character stand along the way from where they stood
/// to the friend's side, each faintly see-through in its own colours as the dodge's are (BodyBake, the DodgeGhost shader).
/// They come one after another and fade the same way, under a thin white streak, with a small flash where it lands.
/// The Knight's own PC leaves out the one over their own head, since their eyes are in it. Gone in under half a second.
/// The Knight's own PC draws it as it steps; the others as PlayerNetworkCombat tells them.
/// Numbers: Tables/MomentFx.csv, row Cover.
/// </summary>
public sealed class CoverStreak : MonoBehaviour
{
    // How long the streak shows (s), how high over the feet it is drawn (m), how thick it starts (m).
    private const float Seconds = .35f, Lift = 1.1f, Width = .08f;

    private LineRenderer line;
    private Light flash;
    private float age;
    private BodyBake bake;
    private Vector3 from, to;
    private bool own;

    /// <summary>The step from <paramref name="start"/> to <paramref name="end"/>, with <paramref name="knight"/>'s afterimages along it.</summary>
    public static void Show(Vector3 start, Vector3 end, GameObject knight)
    {
        if (!Application.isPlaying) return;
        var host = new GameObject("Cover Streak") { hideFlags = HideFlags.DontSave };
        var streak = host.AddComponent<CoverStreak>();
        streak.from = start;
        streak.to = end;
        var line = streak.line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
        line.SetPosition(0, start + Vector3.up * Lift);
        line.SetPosition(1, end + Vector3.up * Lift);
        streak.flash = new GameObject("Flash").AddComponent<Light>();
        streak.flash.transform.SetParent(host.transform, false);
        streak.flash.transform.position = end + Vector3.up * Lift;
        streak.flash.type = LightType.Point;
        streak.flash.shadows = LightShadows.None;
        streak.flash.color = Color.white;
        streak.flash.range = 3.5f;
        if (knight != null && MomentLook.On("Cover"))
        {
            streak.bake = new BodyBake();
            if (!streak.bake.Bake(knight.transform, Mathf.RoundToInt(MomentLook.Number("Cover", "vertexBudget", 150000f)))) streak.bake = null;
            var mover = knight.GetComponent<PlayerMovement>();
            streak.own = mover != null && mover.IsLocal && !mover.IsNetworkDriven;
        }
        streak.Draw(0f);
    }

    private void Update()
    {
        age += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(age / Seconds);
        Draw(k);
        Ghosts();
        if (age >= Mathf.Max(Seconds, MomentLook.Number("Cover", "ghostsFor", .45f))) Destroy(gameObject);
    }

    private void OnDestroy() => bake?.Release();

    // Thin and fading as it goes; the far end (the landing) brightest.
    private void Draw(float k)
    {
        float fade = 1f - k * k;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, Width * .3f * fade), new Keyframe(1f, Width * fade));
        line.startColor = KitStyle.With(Color.white, .3f * fade);
        line.endColor = KitStyle.With(Color.white, .8f * fade);
        flash.intensity = 1.6f * fade;
    }

    // The afterimages along the way, the earliest first, each fading as the dodge's do.
    private void Ghosts()
    {
        if (bake == null || !bake.Ready) return;
        int count = Mathf.Clamp(Mathf.RoundToInt(MomentLook.Number("Cover", "ghosts", 4f)), 1, 8);
        float every = MomentLook.Number("Cover", "every", .03f), seconds = MomentLook.Number("Cover", "ghostSeconds", .3f);
        float alpha = MomentLook.Number("Cover", "ghostAlpha", .5f);
        for (int i = 0; i < count; i++)
        {
            float share = count == 1 ? 1f : i / (float)(count - 1);
            // The last stands where the Knight is: their own eyes are inside that one.
            if (own && share > .8f) continue;
            float since = age - i * every;
            if (since < 0f) continue;
            float left = 1f - since / seconds;
            if (left <= 0f) continue;
            Vector3 at = Vector3.Lerp(from, to, share * .92f);
            bake.Draw(Matrix4x4.Translate(at - bake.BakedAt), alpha * left * left, new Color(1f, 1f, 1f, 0f));
        }
    }
}
