using UnityEngine;

/// <summary>
/// A turn on the spot as people step it, worked out from the turns BIPER captured (ClipBench lifts=, 2026-11-21): a turn
/// is two steps, the foot on the side of the turn first, then the other foot closing; the hips turn while the foot is in
/// the air, the standing foot pivoting under them; the first step carries most of the turn (Female Runner's 90° turns
/// 54-58°, its 135° 107°, its 180° all of it; Combat Girls' gun turns about half), and a step takes 0.2-0.4 s with a gun,
/// longer the further it turns. So the legs never turn first and leave the feet to catch up: until then (0.9.279) the hips
/// turned at their own speed and a foot stepped only once they had turned 25° over it, four quick steps to a 90° turn (the
/// user, 2026-11-21: "발을 네 번이나 뚝뚝뚝뚝 하는 건 좀 투머치").
/// Each frame it is handed the view and where the legs face, and gives back where they face now, which foot is stepping
/// and how far through its step (FootPlanting lifts it and puts it down under the hips' new heading). PlayerBody turns its
/// legs with it; StepBench runs the same so its pictures are the game's.
/// </summary>
internal sealed class TurnPlanner
{
    /// <summary>The foot stepping: 0 none, 1 the left, 2 the right.</summary>
    internal int Lead { get; private set; }
    /// <summary>How far through its step the stepping foot is (0-1).</summary>
    internal float Share { get; private set; }

    private float from, angle, duration;
    private int taken;
    // The view's yaw last frame, for how far it went round since (Tick).
    private float lookWas;
    private bool lookKnown;

    // Below this the view is close enough ahead of the legs: no further step (degrees).
    private const float Close = 10f;
    // The closing step keeps this share of the turn, at most CloseMost degrees: the captured first steps carry the rest.
    private const float CloseShare = .4f, CloseMost = 40f;
    // A step that turns the hips this far takes the step time; one turning further takes longer (its square root).
    private const float StepAngle = 45f;

    internal void Reset()
    {
        Lead = 0;
        Share = 0f;
        taken = 0;
    }

    /// <summary>Something else had the legs since the last tick: the view's turn meanwhile is none of this frame's.</summary>
    internal void Forget() => lookKnown = false;

    /// <summary>
    /// Where the legs face after this frame (world yaw), from where they face and the view's yaw. Standing still
    /// (<paramref name="still"/>) they keep their place until the view is <paramref name="startAngle"/> away, then step
    /// round, each step <paramref name="stepTime"/> s for 45° of it. Past <paramref name="limit"/> behind the view, the view
    /// going on round away from them takes them with it.
    /// </summary>
    internal float Tick(float look, float legs, bool still, float dt, float startAngle, float stepTime, float limit)
    {
        float viewStep = lookKnown ? Mathf.DeltaAngle(lookWas, look) : 0f;
        lookWas = look;
        lookKnown = true;
        if (!still)
        {
            Reset();
            return legs;
        }
        float ahead = Mathf.DeltaAngle(legs, look);
        if (Lead == 0 && Mathf.Abs(ahead) > startAngle)
        {
            taken = 0;
            Begin(legs, ahead - Mathf.Sign(ahead) * Mathf.Min(Mathf.Abs(ahead) * CloseShare, CloseMost), ahead > 0f ? 2 : 1, stepTime);
        }
        if (Lead != 0)
        {
            Share = Mathf.Min(1f, Share + dt / duration);
            float s = Share * Share * (3f - 2f * Share);
            legs = from + angle * s;
            if (Share >= 1f)
            {
                float rest = Mathf.DeltaAngle(legs, look);
                // The other foot always steps once (both feet come round under the new heading), and on while the view is
                // still well ahead of the legs.
                if (taken == 1 || Mathf.Abs(rest) > Close) Begin(legs, rest, Lead == 1 ? 2 : 1, stepTime);
                else Reset();
            }
        }
        // The view going on round away from legs already past `limit` behind it, faster than the steps, takes them with it as
        // far as it goes, and the step goes on from there. Legs that came to a stop further round than that (a run ahead that
        // coasted to a stop facing its own way, a run's turn the view went on past) are not pulled in: the steps bring them round.
        // Until 0.9.420 they were put at `limit` in the frame they stopped: after a run's turn that ended 160 to 170 degrees off
        // the view the hips swung 66 to 77 degrees in a frame, the arms 40 degrees, and the head and chest flipped after (the gait
        // and pops records of 2026-11-25 05:00; the user: "모션 바뀔 때 잠깐 버벅거리는 게 한 프레임? 정도").
        float behind = Mathf.DeltaAngle(look, legs);
        if (Mathf.Abs(behind) > limit && behind * viewStep < 0f)
        {
            from += viewStep;
            legs += viewStep;
        }
        return legs;
    }

    private void Begin(float legs, float turn, int foot, float stepTime)
    {
        Lead = foot;
        Share = 0f;
        from = legs;
        angle = turn;
        duration = Mathf.Max(.05f, stepTime) * Mathf.Sqrt(Mathf.Max(Mathf.Abs(turn), 20f) / StepAngle);
        taken++;
    }
}
