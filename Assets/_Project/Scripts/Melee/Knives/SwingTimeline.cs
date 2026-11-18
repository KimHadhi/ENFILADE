using System;

/// <summary>
/// Where a swing's moments fall in its clip (s of the clip): the blade is live from <see cref="From"/> to <see cref="To"/>,
/// the next action may begin at <see cref="Next"/>, and the arm is home at <see cref="Length"/>. An attack clip carries
/// them as marks (KnifeSwing: the knife workspace's 벰, 벰 끝 and 다음).
/// </summary>
public readonly struct SwingMarks
{
    public readonly float Length, From, To, Next;

    public SwingMarks(float length, float from, float to, float next)
    {
        Length = Math.Max(0f, length);
        From = Math.Min(Math.Max(0f, from), Length);
        To = Math.Min(Math.Max(From, to), Length);
        Next = Math.Min(Math.Max(To, next), Length);
    }
}

/// <summary>
/// One melee weapon's swings through time: what a press of the attack button turns into. A swing is not an instant: the
/// button starts a wind-up of the weapon's own length; let go by its end, the swing leaves there; still held, the wind-up
/// goes on into a charge that grows until the button is let go; the swing then plays its clip, whose own marks say when
/// the blade is live and when the next action may begin; a press that comes while the swing is still committed is kept
/// for a moment and begins the next wind-up at that mark. Nothing shortens the recovery: until 2026-11-18 a hit set the
/// wait to nothing, and the swing left on release with no wind-up at all (the user: "근접 적중 시 후딜레이 캔슬 제거",
/// "즉시 발동 대신 무기별 선딜레이 부여 … 꾹 누르면 선딜 이후 손이 떨리며 차징됨").
/// It knows nothing of Unity, players or knives: the same presses give the same moments, so it can be run on a script of
/// presses without playing (KnifeSwingReport). PlayerMeleeCombat feeds it the button on the player's own PC and the
/// owner's words on a copy (Wind, Strike), and every PC's knife arm reads its phase and clip time (MeleeKnife), so the
/// arm, the blade's trace and the recovery go by one clock.
/// </summary>
public sealed class SwingTimeline
{
    public enum Phase : byte { Idle, Windup, Charging, Swing }

    /// <summary>What a step brought, in any combination.</summary>
    [Flags]
    public enum Events : byte
    {
        None = 0,
        /// <summary>A wind-up began.</summary>
        Wound = 1,
        /// <summary>The wind-up went on into a charge (the button still held).</summary>
        Charging = 2,
        /// <summary>A swing left (Leaving was asked for its clip's marks).</summary>
        Left = 4,
        /// <summary>The swing under way let the next action begin (its clip passed Next).</summary>
        Free = 8,
        /// <summary>The swing's clip ran out: the arm is home.</summary>
        Home = 16,
    }

    /// <summary>The weapon's wind-up and the hold to a full charge (s at Speed 1), how fast all of it runs, and how long
    /// before a swing frees the next a press is kept for it (real s).</summary>
    public float Windup = .1f, ChargeTime = .6f, Speed = 1f, Buffer = .35f;

    /// <summary>
    /// A swing leaves now, charged this far (0 a tap, 1 full): whoever plays it picks its clip and says the clip's marks.
    /// </summary>
    public Func<float, SwingMarks> Leaving;

    public Phase Now { get; private set; }
    /// <summary>How far through the wind-up it is, 0 to 1 (1 while charging); 0 otherwise.</summary>
    public float WindShare => Now == Phase.Windup ? Share(phaseTime, WindupTime) : Now == Phase.Charging ? 1f : 0f;
    /// <summary>How charged it is, 0 to 1: growing while charging, kept as the swing that left was.</summary>
    public float Charge { get; private set; }
    /// <summary>Seconds into the clip of the swing under way.</summary>
    public float ClipTime { get; private set; }
    public SwingMarks Marks { get; private set; }
    /// <summary>A press is kept for when the swing under way frees the next.</summary>
    public bool Queued { get; private set; }
    /// <summary>A swing is under way and nothing else may begin yet: its clip has not reached Next.</summary>
    public bool Committed => Now == Phase.Swing && ClipTime < Marks.Next;
    /// <summary>The button is doing something: winding up, charging or committed to a swing.</summary>
    public bool Busy => Now == Phase.Windup || Now == Phase.Charging || Committed;

    private float phaseTime, spanFrom, spanTo;
    private bool spanned;

    private float Pace => Math.Max(.01f, Speed);
    private float WindupTime => Math.Max(0f, Windup) / Pace;
    private float ChargeDuration => Math.Max(0f, ChargeTime) / Pace;

    private static float Share(float time, float whole) => whole <= 1e-6f ? 1f : Math.Min(1f, Math.Max(0f, time / whole));

    /// <summary>
    /// One step of <paramref name="dt"/> seconds with the attack button as it is (<paramref name="held"/>) and whether
    /// it went down this step (<paramref name="pressed"/>). Time left over when a phase ends inside the step runs on into
    /// the next phase, so no moment waits for a frame's edge.
    /// </summary>
    public Events Tick(float dt, bool held, bool pressed)
    {
        var happened = Events.None;
        spanned = false;
        if (pressed)
        {
            if (Now == Phase.Idle || (Now == Phase.Swing && !Committed)) happened |= BeginWindup();
            // Kept only when the swing frees the next soon: a press at its very start is the same swing's, not another's.
            else if (Committed && (Marks.Next - ClipTime) / Pace <= Buffer) Queued = true;
        }
        float left = Math.Max(0f, dt);
        for (int guard = 0; guard < 8 && left > 0f; guard++)
        {
            switch (Now)
            {
                case Phase.Windup:
                {
                    float need = WindupTime - phaseTime;
                    if (left < need)
                    {
                        phaseTime += left;
                        left = 0f;
                        break;
                    }
                    left -= Math.Max(0f, need);
                    if (held)
                    {
                        Now = Phase.Charging;
                        phaseTime = 0f;
                        Charge = 0f;
                        happened |= Events.Charging;
                    }
                    else happened |= Leave(0f);
                    break;
                }
                case Phase.Charging:
                    if (!held)
                    {
                        happened |= Leave(Charge);
                        break;
                    }
                    phaseTime += left;
                    left = 0f;
                    Charge = Share(phaseTime, ChargeDuration);
                    break;
                case Phase.Swing:
                {
                    float from = ClipTime;
                    bool wasCommitted = Committed;
                    float reach = from + left * Pace;
                    // Up to the mark that frees the next (a kept press begins its wind-up there), else to the clip's end.
                    float stop = wasCommitted ? Marks.Next : Marks.Length;
                    if (reach < stop)
                    {
                        ClipTime = reach;
                        left = 0f;
                        Span(from, ClipTime);
                        break;
                    }
                    left -= (stop - from) / Pace;
                    ClipTime = stop;
                    Span(from, ClipTime);
                    if (wasCommitted)
                    {
                        happened |= Events.Free;
                        if (Queued) happened |= BeginWindup();
                        if (ClipTime < Marks.Length) break;
                    }
                    if (Now == Phase.Swing)
                    {
                        Now = Phase.Idle;
                        happened |= Events.Home;
                    }
                    break;
                }
                default:
                    left = 0f;
                    break;
            }
        }
        return happened;
    }

    /// <summary>A wind-up begins now if nothing is committed (a copy: the owner's word that theirs began).</summary>
    public Events Wind() => Now == Phase.Idle || (Now == Phase.Swing && !Committed) ? BeginWindup() : Events.None;

    /// <summary>
    /// A swing leaves now with no wind-up, whatever was under way (a combo's strike, which is the one thing that may cut
    /// a recovery short; a copy: the owner's word that theirs left).
    /// </summary>
    public Events Strike(float charge) => Leave(Math.Min(1f, Math.Max(0f, charge)));

    /// <summary>Whatever is under way is let go of (the guard goes up, the body is taken by a move, the weapon is put away).</summary>
    public void Drop()
    {
        Now = Phase.Idle;
        Queued = spanned = false;
        phaseTime = 0f;
        Charge = 0f;
    }

    /// <summary>
    /// The part of the swing's clip the last step ran through with the blade live (s of the clip), for whoever traces the
    /// blade: false when the step touched none of its window.
    /// </summary>
    public bool Live(out float from, out float to)
    {
        from = Math.Max(spanFrom, Marks.From);
        to = Math.Min(spanTo, Marks.To);
        return spanned && Marks.To > Marks.From && to > from;
    }

    private Events BeginWindup()
    {
        Now = Phase.Windup;
        Queued = false;
        phaseTime = 0f;
        Charge = 0f;
        return Events.Wound;
    }

    private Events Leave(float charge)
    {
        Charge = charge;
        Queued = false;
        // What an earlier swing ran through this step is not this one's.
        spanned = false;
        phaseTime = 0f;
        ClipTime = 0f;
        Marks = Leaving != null ? Leaving(charge) : default;
        if (Marks.Length <= 0f)
        {
            Now = Phase.Idle;
            return Events.Left | Events.Home;
        }
        Now = Phase.Swing;
        return Events.Left;
    }

    // The clip span this step covered: the first stretch of the step when two swings meet in one (the earlier is over).
    private void Span(float from, float to)
    {
        if (spanned) return;
        spanned = true;
        spanFrom = from;
        spanTo = to;
    }
}
