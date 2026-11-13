using UnityEngine;

/// <summary>
/// A CHARGE seen, on every PC. The user, 2026-11-12: "어썰트 CHARGE는 G&amp;B처럼 주위에 버프 받은 아군이랑 본인이 노란색으로
/// 빛나게", and on the special-moment picks: "동료가 노란색으로 빛나야지 … G&amp;B charge 참조".
/// Everyone the CHARGE took glows gold while it lasts, Guts &amp; Blackpowder's charging line lit up. The take: alive and
/// within its reach of where it was sounded, as that PC sees them then. The glow is light over their own body, strongest
/// at its outline (BodyShell with the MomentGlow shader), and a warm light on them. Where it was sounded, a gold ring of
/// ticks runs out over the ground to its reach (MomentFx ChargeRing, pick A).
/// This PC's own player glows too, as the others see them: their arms and body in their own view, never a tint over the
/// screen (the old gold rim is gone). It beats gently and dims over its last second. The reach and time are the CHARGE's
/// own (ClassAbilities Assault.chargeRadius, chargeSeconds), the same ClassKit.ReceiveCharge gives this PC's player.
/// Numbers for the glow: Tables/MomentFx.csv, row Charge.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChargeGlow : MonoBehaviour
{
    /// <summary>The CHARGE's gold.</summary>
    public static readonly Color Gold = new Color(1f, .8f, .22f, 1f);
    private const string Row = "Charge", GlowShader = "Combat/Shaders/MomentGlow";
    private const float FadeOut = 1f, LightRange = 3.2f;
    private static readonly int ColorId = Shader.PropertyToID("_Color"), StrengthId = Shader.PropertyToID("_Strength"),
        RimId = Shader.PropertyToID("_Rim"), FillId = Shader.PropertyToID("_Fill"), InflateId = Shader.PropertyToID("_Inflate");
    private static Material glowMaterial;

    private PlayerMovement player;
    private float until;
    private Light lamp;
    private BodyShell shell;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => glowMaterial = null;

    /// <summary>
    /// A CHARGE was sounded at <paramref name="at"/> (this PC's Assault's, or a friend's as their PC said it): everyone it
    /// takes glows for its seconds, and its reach runs out over the ground.
    /// </summary>
    public static void Sounded(Vector3 at)
    {
        if (!Application.isPlaying) return;
        float reach = PlayerClass.Ability("Assault", "chargeRadius", 15f);
        float seconds = Mathf.Max(.1f, PlayerClass.Ability("Assault", "chargeSeconds", 7f));
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
        
        until = Time.time + seconds;
        enabled = true;
        if (lamp == null)
        {
            lamp = new GameObject("Charge Light").AddComponent<Light>();
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            lamp.color = Gold;
            lamp.range = LightRange;
        }
        lamp.gameObject.SetActive(true);
        // A fresh shell each CHARGE: the outfit may have changed since the last.
        shell?.Release();
        var material = Material();
        shell = material != null ? BodyShell.Over(gameObject, material) : null;
    }

    private void Update()
    {
        float left = until - Time.time;
        if (left <= 0f || player == null || player.IsDead)
        {
            Douse();
            return;
        }
        float fade = Mathf.Clamp01(left / FadeOut), pulse = .82f + .18f * Mathf.Sin(Time.time * 7f);
        lamp.intensity = MomentLook.Number(Row, "light", 1.6f) * fade * pulse;
        if (shell == null) return;
        Color colour = MomentLook.Colour(Row, "colour", Gold) * MomentLook.Number(Row, "bloom", 2.2f);
        float strength = MomentLook.Number(Row, "strength", 1f) * fade * pulse;
        float rim = MomentLook.Number(Row, "rim", 2.2f), fill = MomentLook.Number(Row, "fill", .18f), inflate = MomentLook.Number(Row, "inflate", .006f);
        shell.Sync(block =>
        {
            block.SetColor(ColorId, colour);
            block.SetFloat(StrengthId, strength);
            block.SetFloat(RimId, rim);
            block.SetFloat(FillId, fill);
            block.SetFloat(InflateId, inflate);
        });
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
