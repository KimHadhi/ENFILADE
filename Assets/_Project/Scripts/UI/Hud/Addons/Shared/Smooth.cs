using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Frame-rate independent stand-ins for the per-frame maths the addons do in HUDPaint, all on unscaled time: our menus
    /// pause the game (time scale 0), and a HUD drawn under a pause menu must still finish its fades and slides, as a
    /// Garry's Mod HUD does on a server that never pauses.
    /// </summary>
    public static class Smooth
    {
        /// <summary>CurTime() / RealTime(): seconds of unscaled time.</summary>
        public static float Now => Time.unscaledTime;

        /// <summary>FrameTime() / RealFrameTime(): this frame's unscaled seconds.</summary>
        public static float Dt => Time.unscaledDeltaTime;

        /// <summary>
        /// How much of the remaining way <c>Lerp(FrameTime() * rate, a, b)</c> covers this frame, made independent of the
        /// frame rate: 1 − e^(−rate·dt). The addons' form takes rate·dt of the way each frame, so the same HUD settled
        /// faster at 300 fps than at 60 and overshot (Lerp clamps at 1) after a long frame; this never passes the target.
        /// A rate of 0 or less stays put, as Lerp with a negative delta does.
        /// </summary>
        public static float Factor(float rate) => rate <= 0f ? 0f : 1f - Mathf.Exp(-rate * Dt);

        /// <summary><c>Lerp(FrameTime() * rate, from, to)</c>: from + (to − from)·(1 − e^(−rate·dt)).</summary>
        public static float Lerp(float rate, float from, float to) => from + (to - from) * Factor(rate);

        /// <summary>The colour form (LerpColor with a FrameTime() delta), channel by channel in 0-1 floats so slow fades
        /// do not stall on whole-number steps.</summary>
        public static Color Lerp(float rate, Color from, Color to) => Color.LerpUnclamped(from, to, Factor(rate));

        /// <summary>The two-number form, for positions and sizes eased together.</summary>
        public static Vector2 Lerp(float rate, Vector2 from, Vector2 to) => Vector2.LerpUnclamped(from, to, Factor(rate));

        /// <summary><c>math.Approach(current, target, FrameTime() * perSecond)</c>: a steady move that stops on the target.</summary>
        public static float Approach(float current, float target, float perSecond) => Gm.Approach(current, target, perSecond * Dt);
    }
}
