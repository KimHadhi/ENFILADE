using UnityEngine;

/// <summary>
/// The one rule that fits a gun's hold to a body's arms, as plain geometry: given where each wrist has to be for its palm to
/// sit on the gun, each shoulder joint and each arm's length, how far the gun comes along its own barrel and how far the
/// support palm comes back along it so that neither arm is straighter than <see cref="Limits.Most"/> of its length nor the
/// trigger arm folded tighter than <see cref="Limits.Least"/>.
/// It only measures a body against itself (a wrist's distance from its own shoulder over its own arm's length), so it means
/// the same on any humanoid: a short-armed character, a long-armed one, a mod's. It never compares a body with KINEMATION's
/// arms rig: that rig's shoulders are put where first-person arms look right (its trigger arm reads 0.2 to 0.5 straight in
/// the guns' poses, which no shoulder joint could do), and holding our bodies to its numbers pulled pistols 6 cm toward the
/// shoulders on one character and not at all on the next (Logs/gun-hold-report.txt, 2026-11-14; the user: "캐릭터마다 총
/// 드는 게 달라서 고장나는데").
/// The gun moves along its own barrel only, so where it points, its sight line and the crosshair's point stay as they are.
/// FPSBody runs it on the body as it really stands every frame (FPSBody.ArmReach), and the editor's hold check runs it on
/// every character and gun (GunHoldProbe).
/// </summary>
internal static class HoldReach
{
    /// <summary>The band an arm is held in (shares of its length) and how far the hold may move for it (m).</summary>
    internal struct Limits
    {
        /// <summary>No arm straighter than this: shoulder joint to wrist over the arm's length.</summary>
        public float Most;
        /// <summary>The trigger arm no more folded than this.</summary>
        public float Least;
        /// <summary>The most the gun comes back toward the body, and goes forward from it.</summary>
        public float PullMost, PushMost;
        /// <summary>The most the support palm comes back along the gun.</summary>
        public float SlideMost;
    }

    /// <summary>
    /// What the fit asks for: the gun moved <see cref="Pull"/> back along its barrel (under zero: forward), the support palm
    /// <see cref="Slide"/> further back on it, and how straight each arm then is.
    /// </summary>
    internal struct Fit
    {
        public float Pull, Slide, Right, Left;
    }

    /// <summary>
    /// Fits the hold. <paramref name="back"/> is the way the gun's butt points (unit); every length is in the same units.
    /// <paramref name="slides"/>: the support hand can hold further back (a bare handguard, or a grip that slides on its rail).
    /// </summary>
    internal static Fit Solve(Vector3 rightWrist, Vector3 rightShoulder, float rightArm, Vector3 leftWrist, Vector3 leftShoulder,
        float leftArm, Vector3 back, in Limits limits, bool slides)
    {
        var fit = default(Fit);
        if (rightArm < 1e-4f || leftArm < 1e-4f || back.sqrMagnitude < 1e-8f) return fit;
        back.Normalize();
        Vector3 right = rightWrist - rightShoulder, left = leftWrist - leftShoulder;
        float mostRight = limits.Most * rightArm, mostLeft = limits.Most * leftArm, leastRight = limits.Least * rightArm;

        // An arm too straight brings the gun back the least way that bends it, as far as the trigger arm has room to fold.
        float needRight = BackFor(right, mostRight, back);
        float pull = Mathf.Max(needRight, BackFor(left, mostLeft, back));
        if (pull > 0f)
            pull = Mathf.Min(Mathf.Min(pull, Mathf.Max(needRight, FoldRoom(right, leastRight, back))), Mathf.Max(0f, limits.PullMost));
        // A trigger arm folded too tight sends it forward, as far as the support arm has room to straighten.
        else if (right.magnitude < leastRight)
            pull = -Mathf.Min(Mathf.Min(ForwardFor(right, leastRight, back), StraightenRoom(left, mostLeft, back)), Mathf.Max(0f, limits.PushMost));

        // A support arm still too straight then holds the gun further back.
        if (slides) fit.Slide = Mathf.Min(BackFor(left + back * pull, mostLeft, back), Mathf.Max(0f, limits.SlideMost));
        fit.Pull = pull;
        fit.Right = (right + back * pull).magnitude / rightArm;
        fit.Left = (left + back * (pull + fit.Slide)).magnitude / leftArm;
        return fit;
    }

    // How far a wrist (from its shoulder) comes back along the gun to be within reach of the shoulder: the nearer root of
    // |from + back s| = reach, or where that line comes nearest when it never gets there. Zero when it is within reach, or
    // when going back only takes it further away.
    private static float BackFor(Vector3 from, float reach, Vector3 back)
    {
        if (from.sqrMagnitude <= reach * reach) return 0f;
        float along = Vector3.Dot(from, back);
        float under = along * along - from.sqrMagnitude + reach * reach;
        return Mathf.Max(0f, under >= 0f ? -along - Mathf.Sqrt(under) : -along);
    }

    // How far back a wrist can come before it is nearer its shoulder than least: the first root of |from + back s| = least
    // ahead of it. No limit when the way back passes the shoulder by more than that.
    private static float FoldRoom(Vector3 from, float least, Vector3 back)
    {
        if (from.sqrMagnitude <= least * least) return 0f;
        float along = Vector3.Dot(from, back);
        float under = along * along - from.sqrMagnitude + least * least;
        if (under < 0f) return float.PositiveInfinity;
        float first = -along - Mathf.Sqrt(under);
        return first > 0f ? first : float.PositiveInfinity;
    }

    // How far forward a wrist nearer its shoulder than least goes to be least from it: the root of |from - back s| = least
    // ahead.
    private static float ForwardFor(Vector3 from, float least, Vector3 back)
    {
        float along = Vector3.Dot(from, back);
        float under = along * along - from.sqrMagnitude + least * least;
        return under > 0f ? Mathf.Max(0f, along + Mathf.Sqrt(under)) : 0f;
    }

    // How far forward a wrist can go before it is further from its shoulder than reach. Zero when it already is.
    private static float StraightenRoom(Vector3 from, float reach, Vector3 back)
    {
        float along = Vector3.Dot(from, back);
        float under = along * along - from.sqrMagnitude + reach * reach;
        return under > 0f ? Mathf.Max(0f, along + Mathf.Sqrt(under)) : 0f;
    }
}
