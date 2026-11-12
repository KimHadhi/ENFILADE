using MagicaCloth2;
using UnityEngine;

/// <summary>
/// A soft lump of the body (breasts, a bottom, a belly, thighs) as Magica Cloth springs it (ClothPart.Flesh): firm and
/// heavy, not floating like hair. The user, 2026-11-12: "가슴 물리가 살덩이처럼 좀 탱탱하고 질량이 느껴졌으면 좋겠어.
/// 머리처럼 너무 흩날리는 느낌."
/// Magica's spring preset (MiddleSpring) and the numbers the VRChat import gave it made flesh move like hair, measured
/// on a replica of Magica's step (Mayo's breast, her game size): a breast slid on its root up to 5 cm and swung freely
/// (Magica's angle limit off), and Magica's damping takes speed off in the world, which is air drag: walking or running,
/// it trailed 5.9 cm behind where it was drawn (Ichigo's 8.5 cm), a landing swung it 10.6 cm and a quick turn 11.9 cm,
/// and it drifted back over half a second without one bounce.
/// Flesh here turns about its root as a VRChat PhysBone does (the root never slides: Magica's spring off), within its
/// maker's limit cone, with no air drag and no wind, so a steady run leaves it where it was drawn and only a change of
/// speed moves it: a damped spring of a given firmness (how many times a second it swings, Hz) and bounce (its damping
/// ratio: .5 swings back once, .25 two or three times; a real breast is about 4 Hz and .2, finite element studies of
/// free breast vibration). Measured the same way, Mayo then rings at 4.1 Hz, damping .27 (asked 4.36 and .27): a hard
/// landing drops it 3 cm, two bounces, still in .38 s; a quick turn 2.9 cm.
/// How Magica's angle restoration becomes that spring (AngleConstraint.SolverConstraint): each step it turns a bone
/// toward its drawn direction in three passes (Define.System.AngleLimitIteration), each by stiffness × .2 of the angle
/// (AngleConstraint's Convert scales it so), about a centre .1, .3 then .5 of the way out from the parent; the parent
/// being still, the bone takes (1 - centre) of each turn. One step gives back a share c of the angle, and a share
/// (velocityAttenuation) of what it gives back is kept out of the speed Magica works out from positions. With no damping:
///   angle' = angle + speed;  angle'' = (1 - c) angle';  speed'' = speed - c (1 - attenuation) angle'
/// whose two roots per step are r·e^(±iφ), r = e^(-ζωΔt), φ = ω√(1-ζ²)Δt for a spring of ω = 2π Hz and damping ζ:
///   c = 1 - r²,  c (1 - attenuation) = 1 + r² - 2r cos φ.
/// </summary>
internal static class FleshSpring
{
    // What a row may ask for: slower than this hardly springs back at all, faster outruns Magica's steps (90 a second).
    private const float LeastHz = .5f, MostHz = 12f;
    // Below this a lump would ring on for seconds; 1 is the stiffest that doesn't swing past its place.
    private const float LeastBounce = .05f, MostBounce = 1f;
    // AngleConstraint.AngleConstraintParams.Convert multiplies the stiffness by this before each pass.
    private const float RestorationScale = .2f;
    // The centre of each pass's turn, from the parent toward the bone (AngleConstraint.SolverConstraint, restorationRotRatio).
    private const float FirstCentre = .1f, LastCentre = .5f;
    // TimeManager.SimulationPower.w: the restoration is scaled by (90 / steps a second) to this power.
    private const float StepPower = 1.8f;
    // Bisection steps finding the turn per pass: far finer than a float needs.
    private const int Halvings = 40;

    /// <summary>
    /// Writes the flesh spring over a part's settings: still root, restoration and its attenuation from
    /// <paramref name="hz"/> and <paramref name="bounce"/>, no damping or wind, the angle limit on.
    /// </summary>
    internal static void Apply(ClothSerializeData data, float hz, float bounce)
    {
        var (stiffness, attenuation) = Restoration(hz, bounce, StepsPerSecond());
        // Magica's spring slides the root bone itself (BoneSpring); a PhysBone only turns it. Off, the root is a still
        // point at its drawn place, as in VRChat, and no body shape pushes it either.
        data.springConstraint.useSpring = false;
        // Magica's damping takes speed off in the world: air drag. Hair streams back in it; flesh would sit behind
        // its drawn place for as long as the body moves.
        data.damping.SetValue(0f);
        data.angleRestorationConstraint.useAngleRestoration = true;
        data.angleRestorationConstraint.stiffness.SetValue(stiffness);
        data.angleRestorationConstraint.velocityAttenuation = attenuation;
        // MiddleSpring keeps its limit off; flesh stays inside its maker's cone (angleLimit, angleCurve).
        data.angleLimitConstraint.useAngleLimit = true;
        data.wind.influence = 0f;
        data.wind.movingWind = 0f;
    }

    /// <summary>
    /// Magica's restoration stiffness (its value, the same at every depth) and velocity attenuation for a lump that
    /// swings <paramref name="hz"/> times a second with damping ratio <paramref name="bounce"/>, at
    /// <paramref name="stepsPerSecond"/> simulation steps a second.
    /// </summary>
    internal static (float Stiffness, float Attenuation) Restoration(float hz, float bounce, int stepsPerSecond)
    {
        float omega = 2f * Mathf.PI * Mathf.Clamp(hz, LeastHz, MostHz);
        float zeta = Mathf.Clamp(bounce, LeastBounce, MostBounce);
        float step = 1f / Mathf.Max(1, stepsPerSecond);
        float r = Mathf.Exp(-zeta * omega * step);
        float phi = omega * Mathf.Sqrt(Mathf.Max(0f, 1f - zeta * zeta)) * step;
        // The share of the angle one step gives back, and the part of it that carries on as speed.
        float given = 1f - r * r;
        float carried = 1f + r * r - 2f * r * Mathf.Cos(phi);
        float attenuation = Mathf.Clamp01(1f - carried / given);
        float power = Mathf.Pow(Define.System.DefaultSimulationFrequency / (float)Mathf.Max(1, stepsPerSecond), StepPower);
        return (Mathf.Clamp01(TurnPerPass(given) / (RestorationScale * power)), attenuation);
    }

    // The turn each pass makes (share of the angle) so that the passes of one step give back the share asked.
    private static float TurnPerPass(float given)
    {
        int passes = Mathf.Max(1, Define.System.AngleLimitIteration);
        float low = 0f, high = 1f;
        for (int i = 0; i < Halvings; i++)
        {
            float turn = (low + high) * .5f;
            float kept = 1f;
            for (int pass = 0; pass < passes; pass++)
            {
                float centre = passes > 1 ? Mathf.Lerp(FirstCentre, LastCentre, pass / (passes - 1f)) : FirstCentre;
                kept *= 1f - (1f - centre) * turn;
            }
            if (1f - kept < given) low = turn;
            else high = turn;
        }
        return low;
    }

    // Magica's steps a second while it runs (90 unless set), else its default.
    private static int StepsPerSecond() =>
        MagicaManager.IsPlaying() ? MagicaManager.GetSimulationFrequency() : Define.System.DefaultSimulationFrequency;
}
