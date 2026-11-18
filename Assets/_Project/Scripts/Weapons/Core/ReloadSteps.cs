using UnityEngine;

/// <summary>
/// When, in a gun's magazine reload, the hands change the gun: the old magazine leaving it, the new one reaching the
/// pouch, the new one seated, and the gun loaded (an empty gun's bolt or slide worked after the seating; a gun reloaded
/// with a round still chambered is loaded as the magazine seats). Shares of the reload's time, 0 to 1, in that order.
/// <para>This is the one place the rules ask "when in this gun's reload": WeaponRuntime moves the rounds at these
/// moments and goes on from them after a reload was cut short, and the reload sounds of a gun no body is showing land
/// on them (WeaponFeedback). Nothing else may keep a share of its own.</para>
/// <para>Where they come from today: the gun's own reload clip, as the body measured it when it built the gun
/// (ReloadMoments, from where the clip moves the magazine and the bolt), and before that or for a gun whose clip can't
/// be measured, the shares below. A moment's rightful owner is a marker authored on the gun's action asset
/// (Docs/Features/GunHandling.md, stage 5: "런타임에 문턱값으로 재지 않는다"); when the assets carry them, only
/// <see cref="Of"/> changes and every reader follows.</para>
/// </summary>
public readonly struct ReloadSteps
{
    // A reload whose clip was never measured (no body has built the gun yet, or its clip moves no magazine part): the
    // shares the reload sounds were timed by before the clips were measured. Stand-ins, not facts about any gun, and the
    // only copy of them: with a measured clip none of them is read.
    private const float UnmeasuredOut = .2f, UnmeasuredIn = .62f, UnmeasuredEmptyIn = .5f, UnmeasuredBolt = .86f;

    /// <summary>The old magazine starts to leave the gun: from here it holds none.</summary>
    public readonly float MagOut;
    /// <summary>The new magazine is at the pouch, in the hand: a reload cut short with the old one out goes on from here.</summary>
    public readonly float Pouch;
    /// <summary>The new magazine is seated: its rounds are in the gun.</summary>
    public readonly float MagIn;
    /// <summary>The gun is loaded and would fire: the bolt worked after the seating, or the seating itself.</summary>
    public readonly float Loaded;
    /// <summary>They are the clip's own moments, not the shares an unmeasured reload falls back on.</summary>
    public readonly bool Measured;

    private ReloadSteps(float magOut, float pouch, float magIn, float loaded, bool measured)
    {
        MagOut = magOut; Pouch = pouch; MagIn = magIn; Loaded = loaded; Measured = measured;
    }

    /// <summary>The steps of this gun's reload: from empty, or with rounds still in it (a tactical reload).</summary>
    public static ReloadSteps Of(WeaponData gun, bool empty)
    {
        float magOut = UnmeasuredOut;
        float magIn = empty ? UnmeasuredEmptyIn : UnmeasuredIn;
        float loaded = empty ? UnmeasuredBolt : magIn;
        ReloadMoments.Moments moments = default;
        bool measured = gun != null && ReloadMoments.TryGet(gun, empty, out moments);
        if (measured)
        {
            if (moments.MagOut >= 0f) magOut = moments.MagOut;
            if (moments.MagIn >= 0f) magIn = moments.MagIn;
            loaded = magIn;
            if (empty)
            {
                if (moments.BoltBack > magIn) loaded = moments.BoltBack;
                else if (moments.BoltForward > magIn) loaded = moments.BoltForward;
            }
        }
        // In order whatever a clip says: a magazine can't be seated before it left, nor a gun loaded before that.
        magIn = Mathf.Max(magIn, magOut);
        loaded = Mathf.Max(loaded, magIn);
        ReloadMoments.Magazine magazine = default;
        float pouch = gun != null && ReloadMoments.TryGetMagazine(gun, empty, out magazine) && magazine.Far >= magOut && magazine.Far <= magIn
            ? magazine.Far : magOut;
        return new ReloadSteps(magOut, pouch, magIn, loaded, measured);
    }
}
