using System;
using System.Globalization;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// How far in the past this PC draws the others' moving things (Netcode's NetworkTransform interpolation buffer), chosen
/// from how late their updates really come in rather than set once (user, 2026-11-11: "연결이 안정적이면 지연을 자동으로
/// 줄이고 흔들리면 다시 늘리는 방식 ... 똑똑하게"). Netcode draws a copy TickLatency + InterpolationBufferTickOffset ticks
/// behind its clock of the server, TickLatency being a round trip in ticks; that guess is too much for what the host
/// sends a guest (a guest's clock of the server already trails it by half a round trip, so the host's updates come a
/// tick early) and too little for what a guest sends the host (stamped half a round trip behind, they come a whole
/// round trip late, past a 5 tick buffer once the ping passes about 33 ms). So every player's pose carries the tick its
/// owner took it on (PlayerPose.Tick), every PC measures how many ticks after that each one comes in, from the host
/// (the road zombies come by too) and from guests apart, and sets the one offset that keeps all of them in time: the
/// late end of the last few seconds (bufferPercentile), the gap between two updates of its kind (a near zombie's,
/// ZombieRate), and a margin. It rises at once, as a stutter is worse than a little more delay, and comes back down one
/// tick at a time only once the need has stayed lower for bufferHold seconds. Network.csv smoothing 0 keeps the fixed
/// bufferSeconds as before. Zombies also go out less often than players do (EnemyNetwork, ZombieRate).
/// </summary>
public static class NetSmoothing
{
    private const int Samples = 256, Fewest = 24;
    private const float Every = .25f, Furthest = 300f;

    private static readonly float[] fromHost = new float[Samples], fromGuests = new float[Samples], sorted = new float[Samples];
    private static int hostCount, hostNext, guestCount, guestNext, offset;
    // Zombie send rates and where they change (Network.csv), read when a room begins and as the buffer is chosen.
    private static float nearRate = 60f, farRate = 20f, nearDistance = 15f, farDistance = 45f;
    private static float lookAt, lowerFrom = -1f, need;
    private static bool adaptive;

    /// <summary>The buffer now, for /net: ticks drawn behind, as ms, and whether it adapts.</summary>
    public static string Report(NetworkManager network)
    {
        if (network == null || !network.IsListening) return "";
        int behind = Behind(network, false);
        float ms = behind * 1000f / Mathf.Max(1, (int)network.NetworkConfig.TickRate);
        return adaptive ? $"Buffer {behind} ticks ({ms:F0} ms) auto, need {need:F1}" : $"Buffer {behind} ticks ({ms:F0} ms) fixed";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        hostCount = hostNext = guestCount = guestNext = 0;
        lowerFrom = -1f;
        need = 0f;
    }

    /// <summary>A room begins (GameSession): the fixed offset first, then, if the table says so, what the updates ask.</summary>
    public static void Begin(int startOffset)
    {
        Clear();
        offset = startOffset;
        adaptive = Number("smoothing", 1f) > .5f;
        ReadRates();
        NetworkTransform.InterpolationBufferTickOffset = offset;
    }

    /// <summary>
    /// A copy's pose came in on this PC, taken on <paramref name="stamp"/> of its owner's clock of the server; hostOwned
    /// when the host's own player sent it.
    /// </summary>
    public static void Heard(NetworkManager network, uint stamp, bool hostOwned)
    {
        if (!adaptive || network == null || !network.IsListening || stamp == 0) return;
        double tick = 1d / Mathf.Max(1, (int)network.NetworkConfig.TickRate);
        float late = (float)((network.ServerTime.Time - stamp * tick) / tick);
        if (float.IsNaN(late) || late < -Furthest || late > Furthest) return;
        if (hostOwned) { fromHost[hostNext] = late; hostNext = (hostNext + 1) % Samples; hostCount = Mathf.Min(hostCount + 1, Samples); }
        else { fromGuests[guestNext] = late; guestNext = (guestNext + 1) % Samples; guestCount = Mathf.Min(guestCount + 1, Samples); }
        if (Time.unscaledTime < lookAt) return;
        lookAt = Time.unscaledTime + Every;
        Choose(network);
    }

    /// <summary>
    /// How often the host sends a zombie at this place (a second): ZombieNearRate within zombieNearDistance of the nearest
    /// player, down to zombieFarRate from zombieFarDistance on. Never more than the tick.
    /// </summary>
    public static float ZombieRate(NetworkManager network, Vector3 at)
    {
        float tickRate = network != null ? network.NetworkConfig.TickRate : 60f;
        float nearest = float.MaxValue;
        foreach (var player in PlayerRoster.All)
            if (player != null) nearest = Mathf.Min(nearest, (player.transform.position - at).sqrMagnitude);
        nearest = nearest < float.MaxValue ? Mathf.Sqrt(nearest) : float.MaxValue;
        float rate = Mathf.Lerp(nearRate, farRate, Mathf.InverseLerp(nearDistance, farDistance, nearest));
        return Mathf.Clamp(rate, 1f, tickRate);
    }

    private static void ReadRates()
    {
        nearRate = Mathf.Max(1f, Number("zombieNearRate", 60f));
        farRate = Mathf.Max(1f, Number("zombieFarRate", 20f));
        nearDistance = Mathf.Max(0f, Number("zombieNearDistance", 15f));
        farDistance = Mathf.Max(nearDistance + .1f, Number("zombieFarDistance", 45f));
    }

    // The ticks a near zombie's updates come apart: the buffer covers that gap as well as the lateness.
    private static float ZombieGap(NetworkManager network)
        => Mathf.Max(1f, network.NetworkConfig.TickRate / Mathf.Clamp(nearRate, 1f, network.NetworkConfig.TickRate));

    private static void Choose(NetworkManager network)
    {
        ReadRates();
        int latency = network.NetworkTimeSystem.TickLatency;
        bool server = network.IsServer;
        float margin = Number("bufferMargin", 1f), share = Mathf.Clamp(Number("bufferPercentile", .97f), .5f, 1f);
        int want = int.MinValue;
        need = 0f;
        // The host's player and the zombies: drawn TickLatency + offset behind.
        if (!server && hostCount >= Fewest)
        {
            float wanted = Percentile(fromHost, hostCount, share) + ZombieGap(network) + margin;
            need = Mathf.Max(need, wanted);
            want = Mathf.Max(want, Mathf.CeilToInt(wanted) - latency);
        }
        // Guests' players: on another guest one more tick behind (Netcode's own two-round-trip allowance).
        if (guestCount >= Fewest)
        {
            float wanted = Percentile(fromGuests, guestCount, share) + 1f + margin;
            need = Mathf.Max(need, wanted);
            want = Mathf.Max(want, Mathf.CeilToInt(wanted) - latency - (server ? 0 : 1));
        }
        if (want == int.MinValue) return;
        int most = Mathf.Max(2, Mathf.CeilToInt(Number("bufferMost", .2f) * network.NetworkConfig.TickRate));
        want = Mathf.Clamp(want, 1 - latency, most - latency);
        if (want > offset)
        {
            offset = want;
            lowerFrom = -1f;
        }
        else if (want < offset)
        {
            if (lowerFrom < 0f) lowerFrom = Time.unscaledTime;
            else if (Time.unscaledTime - lowerFrom >= Number("bufferHold", 3f)) { offset--; lowerFrom = Time.unscaledTime; }
        }
        else lowerFrom = -1f;
        NetworkTransform.InterpolationBufferTickOffset = offset;
    }

    // How many ticks behind this PC draws a copy (Netcode's own sum); a guest's player seen on a guest is one more.
    private static int Behind(NetworkManager network, bool guestOnGuest)
        => Mathf.Max(1, network.NetworkTimeSystem.TickLatency + NetworkTransform.InterpolationBufferTickOffset) + (guestOnGuest ? 1 : 0);

    private static float Percentile(float[] samples, int count, float share)
    {
        Array.Copy(samples, sorted, count);
        Array.Sort(sorted, 0, count);
        return sorted[Mathf.Clamp(Mathf.FloorToInt(share * (count - 1)), 0, count - 1)];
    }

    private static float Number(string row, float fallback)
        => GameTables.TryGetRow("Network", row, out var cells) && cells.TryGetValue("value", out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
}
