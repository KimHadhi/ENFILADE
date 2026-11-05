using UnityEngine;

/// <summary>
/// How long a wall climb lasts and how fast it rises, as Beatrun's default vertical wall run does it (wallrunme.lua, the
/// "purist" one). Its speeds are in units of a 72-unit-tall player (PlayerMovement.HeightUnit turns them into metres).
/// - It starts at 200 units a second and slows with the time left, down to a tenth, over Ability.wallClimbTime (1.5 s):
///   one climb rises about twice the standing height.
/// - Started while rising it runs up to a tenth longer; started falling at 75 units a second or faster it doesn't start.
/// - Running in, it lasts a little longer (up to 4%), standing still a tenth less.
/// - A climb that starts within 2 s of the planned end of the last one is the next of a chain: the second is whole, the
///   third 60%, any after that a quarter. The ground or a ledge starts the count again.
/// </summary>
public static class WallClimbPace
{
    private const float StartUnits = 200f, LeastShare = .1f, FallUnits = 75f;
    /// <summary>Beatrun pushes the climber into the wall at this speed (units/s), so the hands are on it within a moment.</summary>
    public const float IntoWallUnits = 50f;
    /// <summary>A climb starting within this long of the last one's planned end is part of a chain (s).</summary>
    public const float ChainSeconds = 2f;

    /// <summary>Whether a climb may start at this vertical speed (units/s, up is positive).</summary>
    public static bool CanStart(float verticalUnits) => verticalUnits > -FallUnits;

    /// <summary>The share of the time a climb gets from how it starts: up to 1.1 rising, 1 at the top of a jump.</summary>
    public static float RiseShare(float verticalUnits)
    {
        float down = verticalUnits > -FallUnits ? Mathf.Max(verticalUnits, 10f) : verticalUnits;
        return Mathf.Clamp(Mathf.Max(down * .1f, -10f), .5f, 1.1f);
    }

    /// <summary>The share of the time the climbs of a chain get; <paramref name="before"/> counts those already made.</summary>
    public static float ChainShare(int before) => Mathf.Max(1f - Mathf.Max(before - 1, 0) * .4f, .25f);

    /// <summary>The share of the time from the speed running in (units/s, along the ground).</summary>
    public static float SpeedShare(float horizontalUnits) => Mathf.Max(.9f, Mathf.Min(horizontalUnits, 260f) / 250f);

    /// <summary>Climbing speed with this much of the climb left (units/s).</summary>
    public static float Speed(float remaining, MovementAbilitySettings settings)
        => StartUnits * settings.wallClimbRate * Mathf.Clamp(remaining / Mathf.Max(.05f, settings.wallClimbTime), LeastShare, 1f);
}
