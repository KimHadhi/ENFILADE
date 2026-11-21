using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The addon's screen-edge glow (cl_effects.lua, Nene.DamageEffect): its vignette picture (vig.png, white with clear
/// middle) tinted for a moment and fading over Flare.seconds: red when health is hurt, grey-white when a shield took the
/// hit, lime when health comes back. At most 50/255, one soft fade, never repeated: the single soft flash the user's
/// no-blinking rule allows. It covers the whole screen under the other pieces and does not sway. The addon's view shake
/// and blur on a hit are not here: the game's own camera punch and blood (PlayerDamageFeedback) already move and mark the
/// view.
/// </summary>
public sealed class MkFlare : MkPiece
{
    private readonly RawImage glow;
    private Color tint = Color.clear;
    private float strength;

    public override bool Sways => false;

    public MkFlare(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Flare", new Vector2(.5f, .5f))
    {
        Frame.anchorMin = Vector2.zero;
        Frame.anchorMax = Vector2.one;
        Frame.offsetMin = Frame.offsetMax = Vector2.zero;
        glow = Frame.gameObject.AddComponent<RawImage>();
        glow.raycastTarget = false;
        glow.texture = MkHud.Icon("vig");
        glow.enabled = false;
    }

    /// <summary>The glow in this colour, from full.</summary>
    public void Flash(Color colour)
    {
        tint = colour;
        strength = 1f;
    }

    public override void Tick(float dt)
    {
        strength = Mathf.Max(0f, strength - dt / Mathf.Max(.01f, Look.Glow.seconds));
        bool on = strength > 0f && glow.texture != null;
        if (glow.enabled != on) glow.enabled = on;
        if (on) glow.color = new Color(tint.r, tint.g, tint.b, Look.Glow.alpha * strength);
    }
}
