using UnityEngine;

/// <summary>
/// What carrying a thing in both hands is, for everything that can be carried: a campaign fuel can (CampaignCans) and a
/// loose physics prop light enough to lift (PropCarry; the user, 2026-11-21: "F to pick up and left click to throw").
/// One place says where a carried thing sits (in front of this PC's own camera; at the right hand of anyone else, as the
/// cans have hung since C17) and how it leaves the hands: every throw is the same push from the arms (an impulse, N·s,
/// along the aim) and the thing's own mass decides its speed, as a person's throw does, so a soda can flies fast, a fuel
/// can is lobbed (8 kg: about 11 m/s, the cans' old fixed speed) and a sheet of metal barely clears the feet; nothing
/// leaves faster than a hard pitch. Numbers: Tables/Maps/PropCarry.csv.
/// </summary>
public static class CarryHold
{
    public const string TableName = "PropCarry";
    // Where a carried thing sits in this PC's own view, from the camera (m: right, up, ahead): the cans' place since C17.
    // A bigger thing sits further out by its own half size, so it never fills the camera.
    private static readonly Vector3 InView = new Vector3(.28f, -.42f, .62f);

    public static float Number(string row, string field, float fallback) => GameTables.Number(TableName, row, field, fallback);

    /// <summary>
    /// Puts a carried thing where its carrier holds it, as this PC sees it. <paramref name="halfSize"/>: half the thing's
    /// longest side (m), to keep a big thing clear of the camera.
    /// </summary>
    public static void Place(Transform item, PlayerMovement carrier, PlayerMovement mine, Transform view, float halfSize = 0f)
    {
        if (item == null || carrier == null) return;
        if (carrier == mine && view != null)
        {
            item.SetPositionAndRotation(view.TransformPoint(InView + Vector3.forward * Mathf.Max(0f, halfSize - .2f)),
                Quaternion.Euler(0f, view.eulerAngles.y, 0f));
            return;
        }
        var hand = carrier.TryGetComponent(out PlayerBody body) && body.BodyAnimator != null && body.BodyAnimator.isHuman
            ? body.BodyAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        Vector3 at = hand != null ? hand.position + Vector3.down * .22f : carrier.transform.position + Vector3.up * .8f + carrier.transform.right * .25f;
        item.SetPositionAndRotation(at + carrier.transform.forward * Mathf.Max(0f, halfSize - .2f), Quaternion.Euler(0f, carrier.transform.eulerAngles.y, 0f));
    }

    /// <summary>Where a thrown thing starts: in front of the thrower's chest, along the aim.</summary>
    public static Vector3 ThrowFrom(PlayerMovement who, Vector3 aim, float halfSize = 0f)
        => who.transform.position + Vector3.up * 1.4f + aim.normalized * (.6f + Mathf.Max(0f, halfSize - .2f));

    /// <summary>
    /// The velocity a thing of <paramref name="mass"/> kg leaves the hands with, thrown along <paramref name="aim"/>: the
    /// arms' push over its mass, no faster than a hard pitch, plus a little lift.
    /// </summary>
    public static Vector3 ThrowVelocity(float mass, Vector3 aim)
    {
        float speed = Mathf.Min(Number("Throw", "maxSpeed", 18f), Number("Throw", "impulse", 90f) / Mathf.Max(.05f, mass));
        return aim.normalized * speed + Vector3.up * Number("Throw", "lift", 2.5f);
    }

    /// <summary>How fast a thrown thing tumbles (rad/s).</summary>
    public static float ThrowSpin => Number("Throw", "spin", 8f);
}
