using UnityEngine;

public abstract class MovementBaseState : IMovementState
{
    protected readonly PlayerMovement ctx;
    public MovementBaseState Parent { get; }
    public virtual string Name => GetType().Name.Replace("State", "");
    public string Path => Parent == null ? Name : Parent.Path + "/" + Name;

    protected MovementBaseState(PlayerMovement context, MovementBaseState parent = null)
    {
        ctx = context;
        Parent = parent;
    }

    public virtual void Enter() { }
    public virtual void Exit() { }
    // Unhandled events bubble upward. Enter/Exit are called only by the machine.
    public virtual void ProcessInput(Vector2 input) => Parent?.ProcessInput(input);
    public virtual void UpdateLogic(float dt) => Parent?.UpdateLogic(dt);
    public virtual void UpdatePhysics(float dt) => Parent?.UpdatePhysics(dt);
    public virtual void OnColliderHit(ControllerColliderHit hit) => Parent?.OnColliderHit(hit);
    public virtual void OnLanding() => Parent?.OnLanding();
    public virtual bool TryJump() => Parent != null && Parent.TryJump();

    public virtual bool CanApplyGravity => Parent == null || Parent.CanApplyGravity;
    public virtual bool AllowsAutomaticPosture => Parent == null || Parent.AllowsAutomaticPosture;
    public virtual bool AllowsBufferedJump => Parent == null || Parent.AllowsBufferedJump;
    public virtual bool ControlsTraversal => Parent != null && Parent.ControlsTraversal;
    public virtual MovementPosture Posture => Parent != null ? Parent.Posture : MovementPosture.Standing;
    public virtual float LocomotionSpeed => Parent != null ? Parent.LocomotionSpeed : 0f;
    public virtual float SlideFriction => Parent != null ? Parent.SlideFriction : 0f;
    public virtual void HandleWallCollision(Vector3 wallNormal, float impactDot) { }
}
