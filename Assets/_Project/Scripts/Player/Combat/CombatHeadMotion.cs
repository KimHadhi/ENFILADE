using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Short head moves combat asks of this PC's view: a slip's dip (MicroSlip), the back elbow's glance over the shoulder
/// (PlayerPerfectDefense), and turns of the look itself (a slip's lock onto its attacker, MicroSlip). The camera adds
/// Offset (metres: right, up, forward of the body) and Angles (degrees: pitch, yaw, roll) to the eyes after everything
/// else, not scaled by screen shake and without moving the aim; the look takes TakeYaw() every frame, also while combat
/// locks the mouse, and turns by it as if the mouse had. Real time throughout, so slow motion doesn't stretch them.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.CombatHeadMotion)]
public sealed class CombatHeadMotion : MonoBehaviour
{
    private struct Move { public Vector3 offset, angles; public float time, attack, hold, release; }
    private readonly List<Move> moves = new List<Move>(4);
    private float pendingYaw;
    private int pendingFrame = -1;

    /// <summary>Where the eyes sit off their place this frame (m: right, up, forward of the body).</summary>
    public Vector3 Offset { get; private set; }
    /// <summary>How the view turns off the aim this frame (degrees: pitch, yaw, roll).</summary>
    public Vector3 Angles { get; private set; }

    /// <summary>The player's head moves, made on first use.</summary>
    public static CombatHeadMotion For(Component of)
    {
        if (of == null || !Application.isPlaying) return null;
        var player = of.GetComponentInParent<PlayerMovement>();
        var host = player != null ? player.gameObject : of.gameObject;
        return host.TryGetComponent(out CombatHeadMotion motion) ? motion : host.AddComponent<CombatHeadMotion>();
    }

    /// <summary>The head dips by depth (m), out in attack, held, back in release (real seconds).</summary>
    public void Dip(float depth, float attack, float hold, float release) => Add(new Vector3(0f, -depth, 0f), Vector3.zero, attack, hold, release);
    /// <summary>The view turns yaw degrees (+ right) off the aim and back, as a look over the shoulder.</summary>
    public void Glance(float yaw, float attack, float hold, float release) => Add(Vector3.zero, new Vector3(0f, yaw, 0f), attack, hold, release);
    /// <summary>
    /// Any short head move: offset (m) and angles (degrees: pitch, yaw, roll) off the eyes, out, held and back (real s):
    /// the spinning backfist whips the view round with it, the crouched sweep drops and rolls it (FistsView, ShoveMoves).
    /// </summary>
    public void Move(Vector3 offset, Vector3 angles, float attack, float hold, float release) => Add(offset, angles, attack, hold, release);
    /// <summary>Turns the look itself by yaw degrees (+ right), for the look to take this frame.</summary>
    public void Turn(float yaw)
    {
        if (float.IsNaN(yaw) || float.IsInfinity(yaw)) return;
        if (pendingFrame != Time.frameCount) { pendingYaw = 0f; pendingFrame = Time.frameCount; }
        pendingYaw += yaw;
    }
    /// <summary>The yaw asked this frame, for the look to add as mouse input. A turn nobody took is dropped next frame.</summary>
    public float TakeYaw()
    {
        float yaw = pendingFrame == Time.frameCount ? pendingYaw : 0f;
        pendingYaw = 0f;
        return yaw;
    }

    private void Add(Vector3 offset, Vector3 angles, float attack, float hold, float release)
    {
        moves.Add(new Move { offset = offset, angles = angles, attack = Mathf.Max(.01f, attack), hold = Mathf.Max(0f, hold), release = Mathf.Max(.01f, release) });
        Evaluate(0f);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        Evaluate(Mathf.Min(.05f, Time.unscaledDeltaTime));
    }

    private void Evaluate(float dt)
    {
        Vector3 offset = Vector3.zero, angles = Vector3.zero;
        for (int i = moves.Count - 1; i >= 0; i--)
        {
            var move = moves[i];
            move.time += dt;
            float back = move.attack + move.hold;
            if (move.time >= back + move.release) { moves.RemoveAt(i); continue; }
            float weight = move.time < move.attack ? EaseOut(move.time / move.attack)
                : move.time < back ? 1f : 1f - EaseInOut((move.time - back) / move.release);
            offset += move.offset * weight; angles += move.angles * weight;
            moves[i] = move;
        }
        Offset = offset; Angles = angles;
    }

    private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    private static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x < .5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * .5f; }

    private void OnDisable() { moves.Clear(); Offset = Angles = Vector3.zero; pendingYaw = 0f; }
}
