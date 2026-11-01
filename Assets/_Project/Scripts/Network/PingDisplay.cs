using System.Globalization;
using UnityEngine;

/// <summary>
/// The ping players see, on purpose not the true one. Relay's nearest region to Korea is Tokyo, and a true 75 ms looks
/// worse than it plays, so Network.csv sets how much smaller the number is. Anything that measures lag uses the raw value.
/// </summary>
public static class PingDisplay
{
    private const string Table = "Network", Row = "pingDivisor", Column = "value";

    public static int Shown(int measuredMs)
    {
        float divisor = GameTables.TryGetRow(Table, Row, out var row) && row.TryGetValue(Column, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value > 0f ? value : 1f;
        return Mathf.Max(1, Mathf.RoundToInt(measuredMs / divisor));
    }
}
