using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A jump's clip follows the body's flight (the user, 2026-11-21: "점프하면 팔이 너무 갑작스럽게 움직이고"). BIPER Female
/// Runner's jumps open with the push off the ground (a dip, the arms thrown up within a tenth of a second), fly about as long
/// as the game's jump, and fall on with the arms going up; its landings start from the pose a jump comes down in
/// (Land_ToRun from RunJump's touchdown, Land_1h from the arms raised). The game's jump leaves the ground at once and flies
/// on its own, so the clip on its own clock threw the arms up in the air, was still high in its flight at the touchdown and
/// handed a pose with the arms up straight to standing (measured on ClipBench, 2026-11-21). Now the clip starts where its body
/// leaves the ground and its moment follows the body's height over where it left: going up, the share of the top this
/// flight will reach; coming down, the share of the top it reached; below where it left, the clip's own fall at its own pace,
/// and the fall pose once that has run out. ClipBench bakes each clip's takeoff, top, touchdown and rise between
/// (GaitMoveBake, Tables/Player/GaitMoves.csv kind jump); a jump clip with no row plays on its own clock as before.
/// </summary>
public sealed partial class PlayerBody
{
    // A jump clip's flight as baked: where its body leaves the ground, tops out and is back down at the height it left (s),
    // and how high it is between (share of the top) at evenly spaced moments from its start to its end.
    private sealed class Flight
    {
        public float Leave, Top, Land;
        public float[] Rise;
    }

    private readonly Dictionary<string, Flight> flights = new Dictionary<string, Flight>();
    // Where the flight left from (height, m), the highest over it the body has been (m), and how long it has been below it (s).
    private float flightFrom, flightTop, flightBelow;

    private void ReadFlights()
    {
        flights.Clear();
        foreach (string id in GameTables.RowIds(GaitMovesTable))
        {
            if (!GameTables.TryGetRow(GaitMovesTable, id, out var row) || row.Text("kind") != "jump") continue;
            var rise = new List<float>();
            foreach (string cell in row.Text("profile").Split(';'))
                if (float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) rise.Add(value);
            if (rise.Count < 2) continue;
            flights[id] = new Flight { Leave = row.Number("leave", 0f), Top = row.Number("top", 0f), Land = row.Number("land", 0f), Rise = rise.ToArray() };
        }
    }

    // The body has just left the ground: its flight is measured from here.
    private void StartFlight()
    {
        flightFrom = player.transform.position.y;
        flightTop = flightBelow = 0f;
    }

    // In the air after a jump, the jump's clip at the moment of its flight the body is at. False when the clip has no baked
    // flight, or its fall has run out.
    private bool FollowFlight(float dt)
    {
        if (jumpClip == null || !flights.TryGetValue(jumpClip, out var flight) || !(Find(jumpClip) is Channel channel)
            || channel.Synced || channel.Length <= 0f)
            return false;
        float height = player.transform.position.y - flightFrom;
        float rising = player.VerticalVelocity;
        flightTop = Mathf.Max(flightTop, height);
        float time;
        if (rising > 0f)
        {
            // Going up, the share of the top this flight reaches at the gravity it flies under.
            float top = height + rising * rising / (2f * Mathf.Max(.1f, Mathf.Abs(player.gravity)));
            time = FlightMoment(flight, channel.Length, top > .01f ? Mathf.Clamp01(height / top) : 0f, true);
            flightBelow = 0f;
        }
        else if (height > 0f)
        {
            time = FlightMoment(flight, channel.Length, flightTop > .01f ? Mathf.Clamp01(height / flightTop) : 0f, false);
            flightBelow = 0f;
        }
        else
        {
            // Below where it left (off an edge, down a step): the clip's own fall from its touchdown on, at its own pace.
            flightBelow += dt;
            time = flight.Land + flightBelow;
            if (time >= channel.Length) return false;
        }
        Drive(jumpClip, time / channel.Length);
        return true;
    }

    // The moment the clip's body is at this share of its top: on the way up (from leaving to the top) or down (from the top
    // to being back at the height it left).
    private static float FlightMoment(Flight flight, float length, float share, bool up)
    {
        var rise = flight.Rise;
        float step = length / (rise.Length - 1);
        float from = up ? flight.Leave : flight.Top, to = up ? flight.Top : flight.Land;
        float lastTime = from, last = up ? 0f : 1f;
        for (int i = 0; i < rise.Length; i++)
        {
            float time = i * step;
            if (time <= from) continue;
            if (time > to) break;
            float value = rise[i];
            if (up ? value >= share : value <= share)
                return Mathf.Lerp(lastTime, time, Mathf.Approximately(value, last) ? 1f : Mathf.InverseLerp(last, value, share));
            lastTime = time;
            last = value;
        }
        return to;
    }
}
