using UnityEngine;

/// <summary>
/// How far back along a gun one body's support hand holds it from where KINEMATION's does (a shorter support arm holds a
/// bare handguard further back: GunHold's slide). The palm at the plain hold's place on the handguard and every palm ahead
/// of it comes back by the whole slide, a palm behind it by less and less, and one further back than the fade's length not
/// at all. So a reload's hand on the magazine or the loading port stays where its clip has it, a reload's end comes into
/// the slid hold smoothly, and the hold is the same in every view.
/// Numbers only, in the gun's own frame (z along the barrel, m): the game's gun keeps one (FPSGun.SlideSupport) and the
/// editor's bench makes the same one from the same hold (GunBench), so both move a palm by the same rule.
/// </summary>
internal readonly struct SupportSlide
{
    private readonly float slide, at, fade;

    /// <summary>
    /// <paramref name="slide"/> back along the gun (m) from the plain hold's place on it (<paramref name="at"/>, its z in the
    /// gun's frame), fading out over <paramref name="fade"/> behind that place (m).
    /// </summary>
    internal SupportSlide(float slide, float at, float fade)
    {
        this.slide = Mathf.Max(0f, slide);
        this.at = at;
        // At least twice the slide, so a palm further back never passes one ahead of it (the steepest smoothstep is 1.5).
        this.fade = Mathf.Max(fade, 2f * this.slide, .01f);
    }

    /// <summary>How far the palm at the hold's place comes back (m); 0 for a hold that asks none.</summary>
    internal float Back => slide;

    /// <summary>A support palm's place in the gun's frame, moved back along the gun (its -z) by the slide's share there.</summary>
    internal Vector3 Of(Vector3 palm)
    {
        if (slide <= 0f) return palm;
        float share = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(at - fade, at, palm.z));
        palm.z -= slide * share;
        return palm;
    }
}
