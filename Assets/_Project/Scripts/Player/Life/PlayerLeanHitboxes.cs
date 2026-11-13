using UnityEngine;

/// <summary>
/// Separate combat volumes while peeking; the locomotion capsule remains planted behind cover. The upper one goes where the
/// visible body's eyes went holding a gun or the fists, by the lean's whole distance with nothing in hand (BodyLean.Carry),
/// or by the lean's distance where no body poses them.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.LeanHitboxes)]
public sealed class PlayerLeanHitboxes : MonoBehaviour
{
    private PlayerMovement player;
    private CharacterController body;
    private CapsuleCollider lower, upper;
    private Vector3 eyesOffset;
    private int eyesFrame = -10;
    public bool UsesSplitBody => isActiveAndEnabled && upper != null && upper.enabled;

    /// <summary>The visible body's eyes have gone <paramref name="offset"/> sideways this frame; the hitboxes go with them.</summary>
    public void FollowEyes(Vector3 offset)
    {
        eyesOffset = offset;
        eyesFrame = Time.frameCount;
        Sync(offset);
    }

    // The body's measure while it keeps coming (it is from the end of the last frame), else the lean's distance.
    private Vector3 Offset => Time.frameCount - eyesFrame <= 2 ? eyesOffset : player.transform.right * player.LeanOffset;
    private void Awake()
    {
        player = GetComponent<PlayerMovement>(); body = GetComponent<CharacterController>();
        lower = Create("Lower body"); upper = Create("Leaning upper body");
    }
    private CapsuleCollider Create(string label)
    {
        var child = new GameObject(label); child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        var shape = child.AddComponent<CapsuleCollider>(); shape.isTrigger = true; shape.enabled = false;
        child.AddComponent<PlayerCombatHitbox>();
        return shape;
    }
    private void Update()
    {
        if (player == null || body == null || !player.IsInitialized) return;
        Sync(Offset);
    }
    public void Sync(Vector3 offset)
    {
        if (body == null || upper == null) return;
        bool active = body.enabled && offset.sqrMagnitude > .0001f;
        upper.enabled = lower.enabled = active;
        if (!active) return;
        float height = body.height, radius = Mathf.Min(body.radius, height * .25f);
        float halfHeight = height * .5f;
        lower.radius = upper.radius = radius;
        lower.height = upper.height = halfHeight;
        lower.center = body.center - Vector3.up * height * .25f;
        upper.center = body.center + Vector3.up * height * .25f + transform.InverseTransformVector(offset);
        // Enemy attack traces can run before the next physics step.
        Physics.SyncTransforms();
    }
    public static bool Replaces(Collider collider) => collider is CharacterController
        && collider.GetComponent<PlayerLeanHitboxes>().OrNull()?.UsesSplitBody == true;
    private void OnDisable() { if (lower != null) lower.enabled = false; if (upper != null) upper.enabled = false; }
}
