using UnityEngine;

/// <summary>
/// A free physics object (fruit, crate, debris). Its Rigidbody mass alone decides how it reacts: walking leans on it
/// with a force, V shove, tackle and bullets hit it with momentum (N·s), so a light prop flies and a heavy one barely
/// moves. A push never makes it faster than what pushed it. Parkour ignores it, so the player never hangs from
/// something that rolls away. Built from a model with Tools → Models → Create Physics Prop Prefab.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public sealed class PhysicsProp : MonoBehaviour
{
    // Upper bound for any single push, so a very light prop does not shoot off at bullet speed.
    private const float MaxPushSpeed = 12f;

    private Rigidbody body;

    private Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

    public static PhysicsProp Of(Collider collider)
    {
        var attached = collider != null ? collider.attachedRigidbody : null;
        return attached != null && attached.TryGetComponent(out PhysicsProp prop) ? prop : null;
    }

    /// <summary>The walking body leans on the prop with <paramref name="force"/> newtons this frame, never past its own speed.</summary>
    public void PushFromWalker(Vector3 walkerVelocity, float force)
    {
        var rb = Body;
        Vector3 horizontal = Vector3.ProjectOnPlane(walkerVelocity, Vector3.up);
        float speed = horizontal.magnitude;
        if (rb.isKinematic || force <= 0f || speed < .05f) return;
        Vector3 direction = horizontal / speed;
        float missing = speed - Vector3.Dot(rb.linearVelocity, direction);
        if (missing <= 0f) return;
        // Through the centre of mass: leaning on a prop moves it without flicking it into a spin.
        rb.AddForce(direction * Mathf.Min(missing, force * Time.deltaTime / rb.mass), ForceMode.VelocityChange);
    }

    /// <summary>
    /// A body of <paramref name="bodyMass"/> kg moving at <paramref name="speed"/> runs into the prop and both carry on at
    /// one shared speed: a light prop leaves at the body's speed for almost nothing, one as heavy as the body halves both.
    /// Returns the body's speed afterwards (0 when the prop cannot move).
    /// </summary>
    public float Kick(Vector3 direction, float speed, float bodyMass, Vector3 point)
    {
        var rb = Body;
        if (rb.isKinematic) return 0f;
        float target = Mathf.Min(MaxPushSpeed, bodyMass * speed / (bodyMass + rb.mass));
        float change = target - Vector3.Dot(rb.linearVelocity, direction);
        if (change <= 0f) return speed;
        rb.AddForceAtPosition(direction * change * rb.mass, point, ForceMode.Impulse);
        return Mathf.Max(0f, speed - rb.mass * change / bodyMass);
    }

    /// <summary>A hit carrying <paramref name="momentum"/> (N·s) at a point: the prop gains momentum ÷ mass, at most <paramref name="maxSpeed"/>.</summary>
    public void Push(Vector3 momentum, Vector3 point, float maxSpeed = MaxPushSpeed)
    {
        var rb = Body;
        if (rb.isKinematic) return;
        Vector3 change = Vector3.ClampMagnitude(momentum / rb.mass, Mathf.Min(maxSpeed, MaxPushSpeed));
        // At the contact point, so a hit high on the prop also tips or spins it.
        if (change.sqrMagnitude > .0001f) rb.AddForceAtPosition(change * rb.mass, point, ForceMode.Impulse);
    }
}
