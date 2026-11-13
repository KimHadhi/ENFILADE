using UnityEngine;

/// <summary>
/// The left arm led along a path drawn in Blender while its hand holds something: whose arm it is, its bones and hand
/// maths (ArmBones), what the hand holds and how far the pose is in. A thrown grenade (GrenadeArm) and a repair tool
/// (RepairArm) share all of it; each adds what it holds and when it plays.
/// </summary>
public abstract class PathArm : MonoBehaviour
{
    protected PlayerMovement movement;
    protected PlayerBodyHands hands;
    protected Animator animator;
    protected Transform eyes, holder;
    protected readonly ArmBones arm = new ArmBones();
    protected int drivenFrame = -1;

    // own: the throw's own ease (armIn, armOut); weight: what is posed, never ahead of the hand lent off the gun.
    protected float own, weight;
    protected bool bound;

    /// <summary>Finds the player and the arm on their body's model, once; false without a humanoid body or an arm to pose.</summary>
    protected bool Bind()
    {
        if (bound) return true;
        movement = GetComponentInParent<PlayerMovement>();
        hands = movement != null ? movement.GetComponent<PlayerBodyHands>() : null;
        animator = GetComponent<Animator>();
        if (movement == null || animator == null || !animator.isHuman) return false;
        bound = arm.Bind(animator) && arm.HasLength;
        return bound;
    }

    // What the hand holds (under the holder) shows only while the arm has it out.
    protected void ShowHolder(bool show)
    {
        if (holder != null && holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
    }
}
