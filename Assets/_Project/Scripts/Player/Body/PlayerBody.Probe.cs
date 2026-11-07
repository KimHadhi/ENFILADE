using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

// Temporary, 2026-11-07: the user sees the hands wave in the air over a vault instead of resting on the obstacle, the body
// high, and sliding broken, armed or not. The first vault and the first slide after Play are written to
// Logs/body-probe.txt (no console) every 0.05 s: how fast the clip really plays against its own speed (1 is right, 2
// would mean it advances twice a frame), the hips' height over the feet of the capsule, and each hand's distance from the
// obstacle's edge with how much of it a hold has. Read at the start of the next frame, after last frame's hand IK. Goes
// once the cause is known and fixed.
public sealed partial class PlayerBody
{
    private const float ProbeStep = .05f;
    // Every vault up to this many after Play (2026-11-07: the quick vaults' pull-down needs several kinds read).
    private const int ProbedVaultsMost = 8;
    private int probedVaults;
    private bool probedSlide;
    private MovementState probeState;
    private Channel probeChannel;
    private double probeClipTime;
    private float probeRealTime, probeNext;
    private StringBuilder probeText;
    // The camera's height over the capsule's at the move's start, and its fastest vertical speed since the last line: the
    // smoothing pass (2026-11-07) needs what happens between the lines too.
    private float probeCamBase, probeCamLast, probeCamFastest;
    private bool probeCamSeen;

    private void ProbeBody()
    {
        if (player == null || !player.IsLocal || player.IsNetworkDriven || dead || hips == null) return;
        // Climbing onto a low top (Onto) is a Mantle state; it is read like a vault (2026-11-07, "오두방정").
        bool vaulting = state == MovementState.Vault
            || (state == MovementState.Mantle && player.Abilities.Traversal.Obstacle.Kind == VaultKind.Onto);
        bool sliding = state == MovementState.Sliding;
        if (probeText != null && state != probeState) FinishProbe();
        if (probeText == null)
        {
            if (vaulting && probedVaults < ProbedVaultsMost) { probedVaults++; StartProbe(Find(obstacleClip)); }
            else if (sliding && !probedSlide) { probedSlide = true; StartProbe(Find(slide)); }
            else return;
        }
        if (view != null && Time.deltaTime > 0f)
        {
            float camY = view.position.y;
            if (probeCamSeen) probeCamFastest = Mathf.Max(probeCamFastest, Mathf.Abs(camY - probeCamLast) / Time.deltaTime);
            probeCamLast = camY;
            probeCamSeen = true;
        }
        if (Time.unscaledTime < probeNext) return;
        probeNext = Time.unscaledTime + ProbeStep;
        if (probeChannel == null || !probeChannel.Playable.IsValid()) return;
        double clipTime = probeChannel.Playable.GetTime();
        float real = Time.time - probeRealTime;
        float rate = real > 1e-4f ? (float)(clipTime - probeClipTime) / real : 0f;
        probeClipTime = clipTime;
        probeRealTime = Time.time;
        float hipsUp = hips.position.y - transform.position.y;
        var traversal = player.Abilities.Traversal;
        string Hand(Transform hand, bool isLeft)
        {
            if (hand == null) return "-";
            float edge = vaulting && traversal.Obstacle.Surface != null ? Vector3.Distance(hand.position, traversal.Obstacle.Edge) : -1f;
            float above = vaulting && traversal.Obstacle.Surface != null ? hand.position.y - traversal.Obstacle.Edge.y : 0f;
            return $"edge {edge:0.000} above {above:0.000} held {(hands != null ? hands.Holding(isLeft) : -1f):0.00}";
        }
        probeText.AppendLine($"t {clipTime:0.000}/{probeChannel.Length:0.000} rate {rate:0.00} speed {probeChannel.Playable.GetSpeed():0.00} weight {probeChannel.Weight:0.00} "
            + $"hips over feet {hipsUp:0.000} | left {Hand(leftHand, true)} | right {Hand(rightHand, false)}"
            + (vaulting && hands != null ? $" | {VaultHeightProbe} | {hands.PullProbe}" : "")
            + $" | cam {probeCamLast - probeCamBase:0.000} fastest {probeCamFastest:0.00} m/s");
        probeCamFastest = 0f;
    }

    private void StartProbe(Channel channel)
    {
        probeState = state;
        probeChannel = channel;
        probeClipTime = channel != null && channel.Playable.IsValid() ? channel.Playable.GetTime() : 0d;
        probeRealTime = Time.time;
        probeNext = 0f;
        probeCamBase = transform.position.y;
        probeCamFastest = 0f;
        probeCamSeen = false;
        probeText = new StringBuilder();
        var traversal = player.Abilities.Traversal;
        probeText.AppendLine($"==== {System.DateTime.Now:HH:mm:ss} {state} clip {(channel != null ? channel.Playable.GetAnimationClip()?.name : "none")} "
            + $"kind {traversal.Obstacle.Kind} height {traversal.Obstacle.Height:0.00} armed {(weapons != null && weapons.ShowsWeapon)} "
            + $"standing hips over feet {standingHips:0.000} graph roots {graph.GetRootPlayableCount()} body clips' parents {(layers.IsValid() ? layers.GetOutputCount() : -1)} "
            + $"first output has the clips {(BodyOutput.IsOutputValid() && BodyOutput.GetSourcePlayable().Equals(layers))}");
    }

    private void FinishProbe()
    {
        string path = Path.Combine(Application.dataPath, "..", "Logs", "body-probe.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.AppendAllText(path, probeText.ToString());
        probeText = null;
        probeChannel = null;
    }

    // The hips' height over the capsule's feet while standing still, for comparison.
    private float standingHips;

    private void ProbeStanding()
    {
        if (hips != null && state == MovementState.Standing && player.IsGrounded && player.ActualVelocity.sqrMagnitude < .01f)
            standingHips = hips.position.y - transform.position.y;
    }
}
