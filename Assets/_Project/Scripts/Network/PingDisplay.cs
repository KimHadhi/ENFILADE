using UnityEngine;

/// <summary>
/// The ping players see. It is the true one now (user, 2026-11-10: rooms connect straight, so the number no longer needs
/// dressing up). Network.csv can still divide it (pingDivisor), as it did by 3 while every room went through the relay:
/// the nearest region to Korea is Tokyo, and a true 75 ms looked worse than it played. Anything that measures lag uses
/// the raw value either way.
/// </summary>
public static class PingDisplay
{
    private const string Table = "Network", Row = "pingDivisor", Column = "value";

    public static int Shown(int measuredMs)
    {
        float divisor = GameTables.Number(Table, Row, Column, 1f);
        return Mathf.Max(1, Mathf.RoundToInt(measuredMs / (divisor > 0f ? divisor : 1f)));
    }
}
