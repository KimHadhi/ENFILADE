using UnityEngine;

/// <summary>
/// A CHARGE seen, on every PC. The user, 2026-11-12: "어썰트 CHARGE는 G&amp;B처럼 주위에 버프 받은 아군이랑 본인이 노란색으로
/// 빛나게", and on the special-moment picks: "동료가 노란색으로 빛나야지 … G&amp;B charge 참조".
/// Everyone the CHARGE took glows gold while it lasts, Guts &amp; Blackpowder's charging line lit up. The take: alive and
/// within its reach of where it was sounded, as that PC sees them then. The glow is light over their own body, strongest
/// at its outline (BodyShell with the MomentGlow shader), and a warm light on them. Where it was sounded, a gold ring of
/// ticks runs out over the ground to its reach (MomentFx ChargeRing, pick A).
/// Bright only as it is sounded. The user, 2026-11-14, aiming through a rifle washed gold in a CHARGE: "돌격 버프가 이 정도로
/// 노란색으로 빛나는 건 썼을 때 잠깐이면 됨. 그 뒤론 노란 윤곽선과 좀 덜 해진 노란 빛으로 아군을 벽 넘어서 볼 수 있게끔 하면
/// 되지 이건 조준이 아예 안 되잖아 노래져서." So the full glow holds a moment (Charge.flash) and eases (Charge.settle) to a
/// thin gold outline and a fainter light (Charge.hold*) for the rest of it, and a friend's shows through walls at
/// Charge.through of itself. This PC's own player keeps the glow as the others see it, but once it settles none of it
/// lies within Charge.selfNear of their own eyes, so their hands and gun stay plain to aim past (their legs, and their
/// body in third person, still glow), and no gold lamp lights their own arms (Charge.selfLight).
/// It breathes gently (Charge.pulse, Charge.beat, never faster than MostBeat: no strobes) and dims over its last moment.
/// The reach and time are the CHARGE's own as the Assault's PC sent them (ClassAbilities Assault.chargeRadius,
/// chargeSeconds, with its 돌격 나팔 card), the same ClassKit.ReceiveCharge gives this PC's player.
/// Numbers for the glow: Tables/MomentFx.csv, row Charge.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChargeGlow : MonoBehaviour
{
    /// <summary>The CHARGE's gold.</summary>
    public static readonly Color Gold = new Color(1f, .8f, .22f, 1f);
    private const string Row = "Charge", GlowShader = "Combat/Shaders/MomentGlow";
    // The fastest it may breathe (Hz): a slow swell, never a flicker (the user's strobe complaint, memory no-strobe-blinks).
    private const float MostBeat = 1.6f;
    private static readonly int ColorId = Shader.PropertyToID("_Color"), StrengthId = Shader.PropertyToID("_Strength"),
        RimId = Shader.PropertyToID("_Rim"), FillId = Shader.PropertyToID("_Fill"), InflateId = Shader.PropertyToID("_Inflate"),
        ThroughId = Shader.PropertyToID("_Through"), NearId = Shader.PropertyToID("_Near"), NearWidthId = Shader.PropertyToID("_NearWidth");
    private static Material glowMaterial;

    private PlayerMovement player;
    private float since, until;
    private Light lamp;
    private BodyShell shell;
    // This frame's look, handed to the shell by one delegate made once (no closure each frame).
    private Color colour;
    private float strength, rim, fill, inflate, through, near, nearWidth;
    private System.Action<MaterialPropertyBlock> apply;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => glowMaterial = null;

    /// <summary>
    /// A CHARGE was sounded at <paramref name="at"/> (this PC's Assault's, or a friend's as their PC said it): everyone it
    /// takes glows for its <paramref name="seconds"/>, and its <paramref name="reach"/> runs out over the ground. Both are
    /// the Assault's PC's own (its 돌격 나팔 card lengthens it), sent with it (PlayerNetworkCombat.ChargeRpc), so the glow
    /// lasts as long on every PC.
    /// </summary>
    public static void Sounded(Vector3 at, float seconds, float reach)
    {
        if (!Application.isPlaying) return;
        seconds = Mathf.Max(.1f, seconds);
        foreach (var player in PlayerRoster.All)
        {
            if (player == null || !player.IsInitialized || player.IsDead) continue;
            if ((player.transform.position - at).sqrMagnitude > reach * reach) continue;
            if (!player.TryGetComponent(out ChargeGlow glow)) glow = player.gameObject.AddComponent<ChargeGlow>();
            glow.Kindle(player, seconds);
        }
        if (MomentLook.On(Row)) MomentFx.Play(MomentFx.Kind.ChargeRing, at, Vector3.forward, reach, Gold);
    }

    private static Material Material()
    {
        if (glowMaterial != null) return glowMaterial;
        var shader = Resources.Load<Shader>(GlowShader);
        if (shader == null) return null;
        return glowMaterial = new Material(shader) { name = "Charge Glow (Runtime)", hideFlags = HideFlags.HideAndDontSave };
    }

    private void Kindle(PlayerMovement who, float seconds)
    {
        player = who;
        since = Time.time;
        until = since + seconds;
        enabled = true;
        if (lamp == null)
        {
            lamp = new GameObject("Charge Light").AddComponent<Light>();
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            lamp.color = Gold;
        }
        lamp.range = Mathf.Max(.1f, MomentLook.Number(Row, "lightRange", 3.2f));
        lamp.gameObject.SetActive(true);
        // A fresh shell each CHARGE: the outfit may have changed since the last.
        shell?.Release();
        var material = Material();
        shell = material != null ? BodyShell.Over(gameObject, material) : null;
        // A friend's outline is meant to show through walls.
        if (shell != null && !who.IsLocal) shell.SeenThroughWalls();
    }

    private void Update()
    {
        float now = Time.time, left = until - now;
        if (left <= 0f || player == null || player.IsDead)
        {
            Douse();
            return;
        }
        bool self = player.IsLocal;
        // From the moment it was sounded (0) to its quiet look (1): the full glow holds a moment, then eases down smoothly.
        float settled = Mathf.SmoothStep(0f, 1f, (now - since - MomentLook.Number(Row, "flash", .5f))
            / Mathf.Max(.01f, MomentLook.Number(Row, "settle", .6f)));
        float fade = Mathf.Clamp01(left / Mathf.Max(.01f, MomentLook.Number(Row, "fadeOut", 1f)));
        float depth = Mathf.Clamp01(MomentLook.Number(Row, "pulse", .3f));
        float beat = Mathf.Clamp(MomentLook.Number(Row, "beat", 1.1f), 0f, MostBeat);
        float pulse = 1f - depth * .5f * (1f - Mathf.Sin(now * beat * 2f * Mathf.PI));
        float light = Mathf.Lerp(MomentLook.Number(Row, "light", 1.6f),
            self ? MomentLook.Number(Row, "selfLight", 0f) : MomentLook.Number(Row, "holdLight", .5f), settled);
        lamp.intensity = Mathf.Max(0f, light) * fade * pulse;
        lamp.enabled = lamp.intensity > .001f;
        if (shell == null) return;
        colour = MomentLook.Colour(Row, "colour", Gold) * Mathf.Lerp(MomentLook.Number(Row, "bloom", 2.2f), MomentLook.Number(Row, "holdBloom", 1.4f), settled);
        strength = Mathf.Lerp(MomentLook.Number(Row, "strength", 1f), MomentLook.Number(Row, "holdStrength", .6f), settled) * fade * pulse;
        rim = Mathf.Lerp(MomentLook.Number(Row, "rim", 2.2f), MomentLook.Number(Row, "holdRim", 4f), settled);
        fill = Mathf.Clamp01(Mathf.Lerp(MomentLook.Number(Row, "fill", .18f), MomentLook.Number(Row, "holdFill", .05f), settled));
        inflate = MomentLook.Number(Row, "inflate", .006f);
        // A friend shows through walls; this PC's own player is never behind one from their own eyes.
        through = self ? 0f : Mathf.Clamp01(MomentLook.Number(Row, "through", .8f));
        // This PC's own player: clear round their eyes as it settles, the clear reach growing out from them, so the hands
        // and the gun before them stay plain to aim past.
        near = self ? Mathf.Max(0f, MomentLook.Number(Row, "selfNear", .9f)) * settled : 0f;
        nearWidth = Mathf.Max(.01f, MomentLook.Number(Row, "selfNearWidth", .35f));
        shell.Sync(apply ??= Apply);
    }

    private void Apply(MaterialPropertyBlock block)
    {
        block.SetColor(ColorId, colour);
        block.SetFloat(StrengthId, strength);
        block.SetFloat(RimId, rim);
        block.SetFloat(FillId, fill);
        block.SetFloat(InflateId, inflate);
        block.SetFloat(ThroughId, through);
        block.SetFloat(NearId, near);
        block.SetFloat(NearWidthId, nearWidth);
    }

    // Over: the light out, the glow gone.
    private void Douse()
    {
        if (lamp != null) lamp.gameObject.SetActive(false);
        shell?.Release();
        shell = null;
        enabled = false;
    }

    private void OnDisable()
    {
        if (shell != null) Douse();
    }

    private void OnDestroy() => shell?.Release();
}
