using UnityEngine;

/// <summary>
/// The mark an Assault aims its 데인저 클로즈 with, on its own screen while the class key is held (ClassCards.DangerClose):
/// the barrage's reach drawn on the ground round where the view meets it, with a target at its middle, blue where the
/// barrage may be called and red where it can't, as the Support's barricade preview is (the user's "능력 키 꾹 누르고
/// 있으면 … 푸른색으로 … 설치 불가능하면 빨간색으로"). It breathes gently and follows the ground under it. Nobody else sees it:
/// once called, everyone sees where the shells will fall (BarrageMark).
/// </summary>
[DisallowMultipleComponent]
public sealed class StrikeAim : MonoBehaviour
{
    // The barricade preview's blue; red is the HUD's.
    private static readonly Color Blue = new Color(.3f, .64f, 1f);
    private const int Points = 48;

    private readonly Vector3[] ring = new Vector3[Points];
    private FxShapes glow;
    private Color shown;
    private bool fresh = true;

    /// <summary>A preview, hidden until shown.</summary>
    public static StrikeAim Make()
    {
        var host = FeelAssets.CreateRuntimeRoot("Danger Close Aim");
        var aim = host.AddComponent<StrikeAim>();
        aim.glow = new FxShapes(host.transform, "Light", true);
        host.SetActive(false);
        return aim;
    }

    /// <summary>Draws the reach <paramref name="radius"/> round <paramref name="at"/>: blue when it <paramref name="fits"/>, else red.</summary>
    public void Show(Vector3 at, float radius, bool fits)
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
            fresh = true;
        }
        transform.position = at;
        Color want = fits ? Blue : KitStyle.Red;
        // A quick blend, so stepping across where it may fall reads as a change, not a flicker.
        shown = fresh ? want : Color.Lerp(shown, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 24f));
        fresh = false;
        if (glow == null || !glow.Begin(Camera.main)) return;
        float breathe = .86f + .14f * Mathf.Sin(Time.unscaledTime * 5.5f);
        AssaultStrikes.RingOnGround(at, Mathf.Max(1f, radius), ring);
        AssaultStrikes.DrawRing(glow, at, ring, .09f, FxShapes.With(shown, .85f * breathe));
        AssaultStrikes.DrawRing(glow, at, ring, .5f, FxShapes.With(shown, .12f * breathe));
        Vector3 floor = at + Vector3.up * .05f;
        glow.Sigil(MomentLook.Sigil.Target, floor, Vector3.right, Vector3.forward, 1.2f, 1f, FxShapes.With(shown, .9f * breathe), .03f, .09f,
            Time.unscaledTime * .8f);
        glow.End();
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void OnDestroy() => glow?.Destroy();
}
