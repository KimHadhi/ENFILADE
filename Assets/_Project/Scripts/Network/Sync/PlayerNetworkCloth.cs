using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps a player's cloth the same on every PC. Their own PC simulates it, and every other PC's copy puts its bones where
/// this says (PlayerBodyCloth). The pose goes out in runs of ClothPose.RunBones bones, so a body may wear any number of
/// swinging bones and each message stays small: a run goes when it changed, or a second after it last went (enough to
/// mend one lost on the way and to show someone who just joined). It goes unreliably, because a late pose is worth
/// nothing once a newer one is out, and a copy drops a run older than the last it took of that place. Alone it never
/// spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNetworkCloth : NetworkBehaviour
{
    // A run of cloth at rest goes out only this often.
    private const float RestInterval = 1f;

    private PlayerBodyCloth cloth;
    // Sending: the pose as captured, each run as it last went, when, and how many times.
    private sbyte[] captured = Array.Empty<sbyte>(), sent = Array.Empty<sbyte>();
    private float[] sentAt = Array.Empty<float>();
    private ushort[] turns = Array.Empty<ushort>();
    private int sentBones = -1;
    private float capturedAt = float.NegativeInfinity;
    private bool warned;
    // Showing: the pose as the runs brought it, and which send of each run it holds.
    private sbyte[] shown = Array.Empty<sbyte>();
    private ushort[] shownTurns = Array.Empty<ushort>();
    private bool[] shownAny = Array.Empty<bool>();
    private int shownBones = -1;

    public override void OnNetworkSpawn()
    {
        cloth = GetComponent<PlayerBodyCloth>();
        if (IsOwner && cloth != null) cloth.Simulated += Send;
    }

    public override void OnNetworkDespawn()
    {
        if (cloth != null) cloth.Simulated -= Send;
    }

    // Right after the simulation, while the bones still hold its result and before the arms press the skirts: every PC
    // presses its copy with the arms it shows. The next frame starts by putting the bones back.
    private void Send()
    {
        float now = Time.unscaledTime;
        if (now - capturedAt < cloth.SendInterval) return;
        capturedAt = now;
        int bones = cloth.Capture(ref captured);
        if (bones == 0) return;
        if (bones > ClothPose.MostBones)
        {
            if (!warned) Debug.LogWarning($"[Cloth] {name}: 흔들리는 뼈가 {bones}개라 처음 {ClothPose.MostBones}개만 다른 PC에 보냅니다.", this);
            warned = true;
            bones = ClothPose.MostBones;
        }
        int runs = ClothPose.Runs(bones);
        if (bones != sentBones)
        {
            // Another skin or outfit: every run goes now. The turns keep counting, so a copy never takes a new run for
            // an old one.
            sentBones = bones;
            sent = new sbyte[bones * 3];
            sentAt = new float[runs];
            for (int run = 0; run < runs; run++) sentAt[run] = float.NegativeInfinity;
            if (turns.Length < runs) Array.Resize(ref turns, runs);
        }
        for (int run = 0; run < runs; run++)
        {
            int start = run * ClothPose.RunBones, count = Mathf.Min(ClothPose.RunBones, bones - start);
            if (Same(captured, sent, start * 3, count * 3) && now - sentAt[run] < RestInterval) continue;
            Array.Copy(captured, start * 3, sent, start * 3, count * 3);
            sentAt[run] = now;
            ShowRpc(new ClothPose(captured, start, count, bones, ++turns[run]));
        }
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    private void ShowRpc(ClothPose pose)
    {
        if (cloth == null) return;
        int bones = Mathf.Min(cloth.PoseBones, ClothPose.MostBones);
        // A copy that hasn't changed to their skin and clothes yet carries other bones: it waits for a pose that fits.
        if (bones == 0 || pose.Total != bones || pose.Start + pose.Count > bones) return;
        int runs = ClothPose.Runs(bones);
        if (bones != shownBones)
        {
            shownBones = bones;
            shown = new sbyte[bones * 3];
            shownTurns = new ushort[runs];
            shownAny = new bool[runs];
        }
        int at = pose.Start / ClothPose.RunBones;
        if (at >= runs || (shownAny[at] && !ClothPose.After(pose.Turn, shownTurns[at]))) return;
        shownAny[at] = true;
        shownTurns[at] = pose.Turn;
        pose.CopyTo(shown);
        cloth.Show(shown, bones);
    }

    private static bool Same(sbyte[] a, sbyte[] b, int from, int length)
    {
        for (int i = from, end = from + length; i < end; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
