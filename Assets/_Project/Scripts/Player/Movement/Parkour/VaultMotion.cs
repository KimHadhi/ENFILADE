using UnityEngine;

/// <summary>
/// How Beatrun carries the player along a vault or climb: the per-kind lerp rates of vaulting.lua and climb.lua, in its
/// 66.66 Hz ticks, and when it hands control back. Speeds are in its units per second (see PlayerMovement.HullUnit).
/// Ability.vaultRate, mountRate and heaveRate scale the rates; 1 is the original.
/// </summary>
public static class VaultMotion
{
    private const float TickRate = 66.66f;
    /// <summary>What Beatrun's spring off a vault reaches: 350 units/s up under its 600 units/s² gravity.</summary>
    public const float SpringHeight = 102f;
    /// <summary>How long a move takes to reach its starting place at a wall (s), as Beatrun eases the model there.</summary>
    public const float ApproachTime = .2f;

    /// <summary>The lerp after dt seconds.</summary>
    public static float Step(VaultKind kind, float lerp, float dt, float entrySpeed, MovementAbilitySettings settings)
    {
        float tick = dt * TickRate, rate;
        switch (kind)
        {
            case VaultKind.StepUp:
                return Mathf.Lerp(lerp, 1f, .075f * tick * settings.mountRate);
            case VaultKind.Onto:
                rate = .075f * tick * settings.mountRate;
                if (lerp > .01f && lerp < .65f) rate *= lerp / .5f;
                return Mathf.Lerp(lerp, 1f, rate);
            case VaultKind.Over:
                rate = .06f * .75f * tick * settings.vaultRate;
                if (lerp > .1f && lerp < .25f) rate *= Mathf.Max(.5f, .5f + entrySpeed / 375f * .3f - .2f);
                return Mathf.MoveTowards(lerp, 1f, rate);
            case VaultKind.Kong:
                rate = .06f * tick * settings.vaultRate * Mathf.Max(.75f, .75f + entrySpeed / 350f * .3f - .2f);
                return Mathf.MoveTowards(lerp, 1f, rate);
            case VaultKind.OverHigh:
                rate = .03f * tick * settings.vaultRate;
                if (lerp < .0575f) rate *= .1f;
                if (lerp > .3f) rate *= 1.5f;
                return Mathf.MoveTowards(lerp, 1f, rate);
            case VaultKind.OntoHigh:
                rate = .03f * tick * settings.mountRate;
                rate *= lerp < .0575f ? .1f : lerp < .7f ? .8f : .6f;
                return Mathf.MoveTowards(lerp, 1f, rate);
            default:
                rate = 1.25f * dt * settings.heaveRate;
                return Mathf.MoveTowards(lerp, 1f, lerp > .5f ? rate * .75f : rate);
        }
    }

    /// <summary>The move is over whatever the player is touching.</summary>
    public static bool Arrived(VaultKind kind, float lerp)
        => lerp >= (kind == VaultKind.StepUp || kind == VaultKind.Onto ? .8f : 1f);

    /// <summary>No move ends before this lerp unless something cut it short.</summary>
    public const float EarliestLeave = .4f;

    /// <summary>A body already clear of the obstacle may leave early.</summary>
    public static bool MayLeave(VaultKind kind, float lerp)
    {
        switch (kind)
        {
            case VaultKind.StepUp:
            case VaultKind.Onto: return lerp > EarliestLeave;
            case VaultKind.Over: return lerp > .5f;
            case VaultKind.OntoHigh: return lerp > .75f;
            default: return false;
        }
    }

    public static float ExitSpeed(VaultKind kind, float entrySpeed)
    {
        switch (kind)
        {
            case VaultKind.Over:
            case VaultKind.Kong: return Mathf.Clamp(entrySpeed, 200f, 600f);
            case VaultKind.OverHigh: return 150f;
            default: return 0f;
        }
    }

    /// <summary>Holding jump as the move ends springs off these kinds instead.</summary>
    public static bool CanSpring(VaultKind kind) => kind >= VaultKind.StepUp && kind <= VaultKind.Kong;
    public static float SpringSpeed(float entrySpeed) => Mathf.Clamp(entrySpeed * .75f, 200f, 300f);
}
