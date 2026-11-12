using UnityEngine;

/// <summary>
/// A gun part that turns with a revolver's cylinder bone by its own share of the bone's turn. The Schießhund's Desperado
/// wears the Fatebringer's six-chamber cylinder on KINEMATION's R08 rig, whose clips turn the cylinder one chamber of eight
/// (45°) a shot and the idle pose then puts it back: the eight-chamber R08 looks the same either way, but six chambers
/// turned 45° and back showed a jump. This turns the part (ratio − 1) more about the bone's axis, a chamber of six (60°) a
/// shot, so the snap back to rest is never seen. The part hangs from its bone as DesperadoSetup placed it (the cylinder
/// from the cylinder bone; the rounds from Ammo_01, which carries them out and back in again in a reload) and keeps that
/// rest pose whenever the cylinder is at rest.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChamberTurn : MonoBehaviour
{
    [SerializeField, Tooltip("The rig's cylinder bone whose turn this part follows.")]
    private Transform cylinder;
    [SerializeField, Tooltip("The cylinder's axis in that bone's own space.")]
    private Vector3 axis = Vector3.forward;
    [SerializeField, Tooltip("How far the part turns per turn of the bone: the rig's chambers over the part's (8/6: the Fatebringer on the R08).")]
    private float ratio = 4f / 3f;

    private Quaternion cylinderRest, restRotation;
    private Vector3 restPosition;
    private bool ready, turned;

    /// <summary>Set up in the editor (DesperadoSetup), with the gun at rest.</summary>
    public void Set(Transform cylinderBone, Vector3 axisInBone, float turnRatio)
    {
        cylinder = cylinderBone;
        axis = axisInBone;
        ratio = turnRatio;
    }

    // The gun wakes at rest (FPSBody builds it from the prefab's pose before any clip plays).
    private void Awake()
    {
        if (cylinder == null) return;
        cylinderRest = cylinder.localRotation;
        restRotation = transform.localRotation;
        restPosition = transform.localPosition;
        ready = true;
    }

    // After the clips have posed the bones.
    private void LateUpdate()
    {
        if (!ready || cylinder == null) return;
        Vector3 along = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.forward;
        (Quaternion.Inverse(cylinderRest) * cylinder.localRotation).ToAngleAxis(out float angle, out Vector3 about);
        float turn = Vector3.Dot(about, along) >= 0f ? angle : -angle;
        if (turn > 180f) turn -= 360f;
        else if (turn < -180f) turn += 360f;
        float extra = turn * (ratio - 1f);
        var parent = transform.parent;
        // At rest (and all through a reload, which swings the crane but doesn't turn the cylinder) the part keeps its place.
        if (Mathf.Abs(extra) < .01f || parent == null || parent.lossyScale.sqrMagnitude < 1e-12f)
        {
            if (turned)
            {
                transform.SetLocalPositionAndRotation(restPosition, restRotation);
                turned = false;
            }
            return;
        }
        // The cylinder's axis line (through the bone's origin) in the part's parent's space: the bone itself, or a round
        // bone under it.
        Vector3 pivot = parent.InverseTransformPoint(cylinder.position);
        Vector3 direction = parent.InverseTransformDirection(cylinder.TransformDirection(along));
        if (direction.sqrMagnitude < 1e-12f) return;
        var spin = Quaternion.AngleAxis(extra, direction.normalized);
        transform.SetLocalPositionAndRotation(pivot + spin * (restPosition - pivot), spin * restRotation);
        turned = true;
    }
}
