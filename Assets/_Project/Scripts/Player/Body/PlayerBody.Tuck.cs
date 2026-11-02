using UnityEngine;

/// <summary>
/// An air crouch draws the capsule's bottom up, and the feet transform stays where it was until landing moves it to the
/// capsule. The body stands on that transform, so left alone it sinks through the floor before the capsule touches, then
/// jumps up as the transform catches up, and the eye camera with it. It rises here only as far as this pose's soles need,
/// whatever the clip or the character.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Min(.1f), Tooltip("How fast the body rises and settles to keep tucked feet on the capsule (m/s).")]
    private float tuckFollowSpeed = 8f;
    private float tuckLift;

    private void LiftTuckedFeet(float dt)
    {
        // Landing moves the transform up; taking that off the lift leaves the body where it was.
        tuckLift -= player.TakeTuckRebase();
        float tuck = player.AirTuck, target = 0f;
        if (tuck <= 0f && Mathf.Abs(tuckLift) < .0001f) { tuckLift = 0f; return; }
        if (tuck > 0f && leftLeg != null && rightLeg != null)
        {
            float soles = Mathf.Min(leftLeg.Foot.position.y - leftLeg.Sole, rightLeg.Foot.position.y - rightLeg.Sole);
            target = Mathf.Max(0f, transform.position.y + tuck - soles);
        }
        tuckLift = Mathf.MoveTowards(tuckLift, target, tuckFollowSpeed * dt);
        body.position += Vector3.up * tuckLift;
    }
}
