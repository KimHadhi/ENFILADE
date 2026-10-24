using UnityEngine;

/// <summary>Separate combat volumes while peeking; the locomotion capsule remains planted behind cover.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-50)]
public sealed class PlayerLeanHitboxes : MonoBehaviour
{
    private PlayerMovement player;
    private CharacterController body;
    private CapsuleCollider lower, upper;
    public bool UsesSplitBody => isActiveAndEnabled && upper != null && upper.enabled;
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
        Sync(player.transform.right * player.LeanOffset);
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
        && collider.GetComponent<PlayerLeanHitboxes>()?.UsesSplitBody == true;
    private void OnDisable() { if (lower != null) lower.enabled = false; if (upper != null) upper.enabled = false; }
}
