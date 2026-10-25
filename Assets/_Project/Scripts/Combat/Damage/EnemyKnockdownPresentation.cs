using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Temporarily hands the body to physics, then restores navigation and animation.</summary>
internal sealed class EnemyKnockdownPresentation
{
    private readonly Transform root;
    private readonly NavMeshAgent agent;
    private readonly Rigidbody[] bodies;
    private readonly bool articulated;
    private readonly List<BodyState> bodyStates = new List<BodyState>();
    private readonly List<ColliderState> colliderStates = new List<ColliderState>();
    private readonly List<PoseState> poses = new List<PoseState>();
    private readonly List<AnimatorState> animators = new List<AnimatorState>();
    private bool agentEnabled;
    private Vector3 entryPosition;
    private Quaternion uprightRotation;
    private float feetOffset, standingHeight, standingRadius;
    public bool IsActive { get; private set; }

    private struct BodyState
    {
        public Rigidbody body;
        public bool kinematic, gravity;
        public RigidbodyConstraints constraints;
        public RigidbodyInterpolation interpolation;
        public CollisionDetectionMode collision;
    }
    private struct ColliderState { public Collider collider; public bool enabled; }
    private struct PoseState { public Transform bone; public Vector3 position; public Quaternion rotation; }
    private struct AnimatorState { public Animator animator; public bool enabled; }

    internal EnemyKnockdownPresentation(Transform owner)
    {
        root = owner; agent = owner.GetComponent<NavMeshAgent>();
        var bones = new List<Rigidbody>();
        foreach (var body in owner.GetComponentsInChildren<Rigidbody>(true))
            if (body.transform != owner && body.GetComponent<Collider>() != null) bones.Add(body);
        articulated = bones.Count > 1 && owner.GetComponentInChildren<Joint>(true) != null;
        if (articulated) bodies = bones.ToArray();
        else
        {
            // The current capsule enemy has no skeleton: its actual collider and mesh tumble together.
            var body = owner.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = owner.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = false; body.mass = 70f;
            }
            bodies = new[] { body };
        }
    }

    internal void Fall(Vector3 direction)
    {
        // Repeated knockdowns extend the timer without recapturing a fallen pose as the standing pose.
        if (IsActive) return;
        IsActive = true; entryPosition = root.position;
        uprightRotation = Quaternion.Euler(0f, root.eulerAngles.y, 0f);
        Bounds standing = new Bounds(root.position, Vector3.zero);
        bool hasBounds = false;
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger) continue;
            colliderStates.Add(new ColliderState { collider = collider, enabled = collider.enabled });
            if (!collider.enabled) continue;
            if (!hasBounds) { standing = collider.bounds; hasBounds = true; }
            else standing.Encapsulate(collider.bounds);
        }
        feetOffset = hasBounds ? root.position.y - standing.min.y : 0f;
        standingHeight = hasBounds ? standing.size.y : 1.8f;
        standingRadius = hasBounds ? Mathf.Max(standing.extents.x, standing.extents.z) : .3f;
        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
        {
            animators.Add(new AnimatorState { animator = animator, enabled = animator.enabled });
            animator.enabled = false;
        }
        if (articulated)
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                if (bone != root) poses.Add(new PoseState { bone = bone, position = bone.localPosition, rotation = bone.localRotation });
        agentEnabled = agent != null && agent.enabled;
        if (agentEnabled) agent.enabled = false;

        var rootBody = root.GetComponent<Rigidbody>();
        if (articulated && rootBody != null) Capture(rootBody, false);
        foreach (var body in bodies) Capture(body, true);
        if (articulated)
            foreach (var state in colliderStates)
                state.collider.enabled = System.Array.IndexOf(bodies, state.collider.GetComponentInParent<Rigidbody>()) >= 0;
        Physics.SyncTransforms();

        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (direction.sqrMagnitude < .01f) direction = root.forward;
        Vector3 axis = Vector3.Cross(Vector3.up, direction);
        foreach (var body in bodies)
        {
            // A low trip imparts angular momentum; gravity and contact decide how the body lands.
            body.AddForce(direction * 1.8f + Vector3.up * .35f, ForceMode.VelocityChange);
            body.AddTorque(axis * 5f, ForceMode.VelocityChange);
        }
    }

    private void Capture(Rigidbody body, bool dynamic)
    {
        bodyStates.Add(new BodyState { body = body, kinematic = body.isKinematic, gravity = body.useGravity,
            constraints = body.constraints, interpolation = body.interpolation, collision = body.collisionDetectionMode });
        if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        body.isKinematic = !dynamic; body.useGravity = dynamic;
        body.constraints = RigidbodyConstraints.None;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        if (dynamic) body.WakeUp();
    }

    internal void Tick(float dt, bool down)
    {
        if (!IsActive || down || dt <= 0f) return;
        // Stay down when a player, wall, low ceiling or missing floor prevents safe recovery.
        if (TryRecoveryPosition(out var position)) Restore(position);
    }

    private bool TryRecoveryPosition(out Vector3 position)
    {
        // A feet-pivot root can rotate below the floor; probe from the physical body's center.
        Vector3 current = bodies[0] != null ? bodies[0].worldCenterOfMass : root.position;
        position = current;
        var hits = Physics.RaycastAll(current + Vector3.up * .5f, Vector3.down, standingHeight + 2f,
            ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        bool supported = false;
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(root)) continue;
            if (hit.normal.y < .65f || hit.collider.GetComponentInParent<MeleeCombatTarget>() != null
                || hit.collider.GetComponentInParent<PlayerMovement>() != null) return false;
            position = new Vector3(current.x, hit.point.y + feetOffset, current.z);
            supported = true; break;
        }
        if (!supported) return false;
        if (agentEnabled)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Vector3 feet = position - Vector3.up * feetOffset;
            if (!NavMesh.SamplePosition(feet, out var nav, .25f, filter)
                || Vector3.ProjectOnPlane(nav.position - feet, Vector3.up).sqrMagnitude > .01f
                || Mathf.Abs(nav.position.y - feet.y) > .15f) return false;
            position = nav.position + Vector3.up * agent.baseOffset;
        }
        float radius = Mathf.Max(.05f, standingRadius - .03f);
        Vector3 bottom = position + Vector3.up * (radius - feetOffset + .04f);
        Vector3 top = position + Vector3.up * Mathf.Max(radius - feetOffset + .04f, standingHeight - feetOffset - radius);
        foreach (var collider in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(root)) return false;
        return true;
    }

    private void Restore(Vector3 position)
    {
        foreach (var state in bodyStates)
        {
            if (state.body == null) continue;
            if (!state.body.isKinematic) { state.body.linearVelocity = Vector3.zero; state.body.angularVelocity = Vector3.zero; }
            state.body.isKinematic = true;
        }
        root.SetPositionAndRotation(position, uprightRotation);
        foreach (var pose in poses)
            if (pose.bone != null) { pose.bone.localPosition = pose.position; pose.bone.localRotation = pose.rotation; }
        foreach (var state in colliderStates) if (state.collider != null) state.collider.enabled = state.enabled;
        foreach (var state in bodyStates)
        {
            var body = state.body;
            if (body == null) continue;
            body.constraints = state.constraints; body.useGravity = state.gravity;
            body.interpolation = state.interpolation; body.isKinematic = state.kinematic;
            body.collisionDetectionMode = state.collision;
        }
        Physics.SyncTransforms();
        if (agent != null) agent.enabled = agentEnabled;
        foreach (var state in animators) if (state.animator != null) state.animator.enabled = state.enabled;
        bodyStates.Clear(); colliderStates.Clear(); poses.Clear(); animators.Clear(); IsActive = false;
    }

    internal void Reset()
    {
        if (!IsActive) return;
        Restore(TryRecoveryPosition(out var position) ? position : entryPosition);
    }
}
