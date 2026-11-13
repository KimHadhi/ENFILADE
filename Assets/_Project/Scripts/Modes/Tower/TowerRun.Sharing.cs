using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// TowerRun: Sharing. One part of the class; the rest is in TowerRun.cs and the files beside it.
public sealed partial class TowerRun
{
    private void Share()
    {
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        var now = new Status
        {
            Seed = seed, Phase = (byte)phase, Segment = (byte)segment, Opened = (byte)opened, Resets = (byte)resets,
            Counting = (byte)(counting ? 1 : 0), Count = (ushort)Mathf.Clamp(Mathf.CeilToInt(count * 10f), 0, ushort.MaxValue),
            Hand0 = hands[0], Hand1 = hands[1], Hand2 = hands[2], Hand3 = hands[3],
            State0 = (byte)states[0], State1 = (byte)states[1], State2 = (byte)states[2], State3 = (byte)states[3],
            Floor0 = (byte)floors[0], Floor1 = (byte)floors[1], Floor2 = (byte)floors[2], Floor3 = (byte)floors[3],
            Left0 = (byte)lefts[0], Left1 = (byte)lefts[1], Left2 = (byte)lefts[2], Left3 = (byte)lefts[3]
        };
        if (now.Equals(sent) && Time.unscaledTime < nextShare) return;
        sent = now;
        nextShare = Time.unscaledTime + (counting ? ShareCounting : ShareEvery);
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Status>(), Allocator.Temp);
        writer.WriteValueSafe(now);
        network.CustomMessagingManager.SendNamedMessageToAll(StatusMessage, writer);
    }

    // A guest follows the host's run.
    private void ReceiveStatus(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Status status);
        if (sender != NetworkManager.ServerClientId) return;
        if (plan == null || plan.Seed != status.Seed) plan = new TowerPlan(status.Seed, this);
        seed = status.Seed;
        phase = (Phase)status.Phase;
        segment = Mathf.Clamp(status.Segment, 0, TowerLayout.Segments - 1);
        opened = status.Opened;
        resets = status.Resets;
        counting = status.Counting != 0;
        count = status.Count / 10f;
        hands[0] = status.Hand0; hands[1] = status.Hand1; hands[2] = status.Hand2; hands[3] = status.Hand3;
        states[0] = (LaneState)status.State0; states[1] = (LaneState)status.State1;
        states[2] = (LaneState)status.State2; states[3] = (LaneState)status.State3;
        floors[0] = status.Floor0; floors[1] = status.Floor1; floors[2] = status.Floor2; floors[3] = status.Floor3;
        lefts[0] = status.Left0; lefts[1] = status.Left1; lefts[2] = status.Left2; lefts[3] = status.Left3;
    }
}
