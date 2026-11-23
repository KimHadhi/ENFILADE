using UnityEngine;

/// <summary>
/// The player's own legs as four rounded rods, the thighs and the shins, each as thick toward its ends as it is on this body
/// with what is worn on it: the one shape of the legs that what keeps off them goes by, the bent spine and the head
/// (SpineRoom, PlayerBody.Clearance) and the pointing arm (ArmOffLegs). Before the body's mesh is read (PokeBody.Thickness, a
/// slice a frame, the same on every PC) each is as thick as its length says (ClothBody.csv's shares).
/// </summary>
internal sealed class OwnLegs
{
    private struct Limb
    {
        public Transform From, To;
        // How thick toward each end: as it stands on this body, and as its length says.
        public float Start, End, SaidStart, SaidEnd;
    }

    internal const int Count = 4;
    // A leg is never taken thinner or thicker than these shares of what its length says.
    private const float Thinnest = .7f, Thickest = 2f;

    private readonly Limb[] rods = new Limb[Count];
    private PokeBody self;
    private bool sought, measured;

    internal void Bind(Animator animator)
    {
        sought = measured = false;
        self = null;
        Transform Bone(HumanBodyBones bone) => animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        Leg(0, Bone(HumanBodyBones.LeftUpperLeg), Bone(HumanBodyBones.LeftLowerLeg), Share("thighStart", .17f), Share("thighEnd", .165f));
        Leg(1, Bone(HumanBodyBones.RightUpperLeg), Bone(HumanBodyBones.RightLowerLeg), Share("thighStart", .17f), Share("thighEnd", .165f));
        Leg(2, Bone(HumanBodyBones.LeftLowerLeg), Bone(HumanBodyBones.LeftFoot), Share("shinStart", .15f), Share("shinEnd", .13f));
        Leg(3, Bone(HumanBodyBones.RightLowerLeg), Bone(HumanBodyBones.RightFoot), Share("shinStart", .15f), Share("shinEnd", .13f));
    }

    private static float Share(string column, float fallback) => GameTables.Number(PlayerBodyCloth.TableName, PlayerBodyCloth.TableRow, column, fallback);

    private void Leg(int index, Transform from, Transform to, float start, float end)
    {
        float span = from != null && to != null ? Vector3.Distance(from.position, to.position) : 0f;
        rods[index] = new Limb { From = from, To = to, Start = span * start, End = span * end, SaidStart = span * start, SaidEnd = span * end };
    }

    /// <summary>
    /// Reads how thick the legs are on <paramref name="who"/>'s body, a slice a frame the first time it is called; call it once
    /// the legs are in question. Every call after they are read does nothing.
    /// </summary>
    internal void Read(PlayerMovement who)
    {
        if (measured) return;
        if (!sought)
        {
            sought = true;
            self = PokeBody.Of(who);
            if (self != null) self.Recheck();
        }
        if (self == null || !self.Prepare()) return;
        measured = true;
        for (int i = 0; i < Count; i++)
            if (self.Thickness(rods[i].From, rods[i].To, out float start, out float end))
            {
                rods[i].Start = Mathf.Clamp(start, rods[i].SaidStart * Thinnest, rods[i].SaidStart * Thickest);
                rods[i].End = Mathf.Clamp(end, rods[i].SaidEnd * Thinnest, rods[i].SaidEnd * Thickest);
            }
    }

    /// <summary>Leg <paramref name="index"/> where it is now, from its start to its end, and how thick toward each; false
    /// for one this body doesn't have.</summary>
    internal bool Rod(int index, out Vector3 from, out Vector3 to, out float start, out float end)
    {
        var rod = rods[index];
        from = to = Vector3.zero;
        start = rod.Start;
        end = rod.End;
        if (rod.From == null || rod.To == null) return false;
        from = rod.From.position;
        to = rod.To.position;
        return true;
    }

    /// <summary>How far a ball of <paramref name="radius"/> at <paramref name="point"/> is from the nearest leg's skin (m,
    /// below 0 inside it).</summary>
    internal float Gap(Vector3 point, float radius)
    {
        float gap = float.PositiveInfinity;
        for (int i = 0; i < Count; i++)
        {
            if (!Rod(i, out Vector3 from, out Vector3 to, out float start, out float end)) continue;
            Vector3 along = to - from;
            float length2 = along.sqrMagnitude;
            float at = length2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - from, along) / length2) : 0f;
            gap = Mathf.Min(gap, Vector3.Distance(point, from + along * at) - Mathf.Lerp(start, end, at) - radius);
        }
        return gap;
    }
}
