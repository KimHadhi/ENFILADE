using UnityEngine;

namespace IronSamiStudio.ProceduralHitReactions
{
    /// <summary>
    /// Generic helper that forwards OnCollisionEnter hits to HitReactionController.
    /// Add it to ANY projectile or melee weapon prefab – no code changes needed.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HitReactionTrigger : MonoBehaviour
    {
        [Header("Which target layers should react?")]
        [Tooltip("Only collisions with these layers will forward a React() call.\n" +
                 "Useful to ignore ground, environment, etc.")]
        public LayerMask reactLayers = ~0;                 // default = “Everything”

        [Header("Lifecycle")]
        public bool destroyOnHit = true;                   // auto-destroy like a bullet
        public float destroyDelay = 0f;                    // or explode particles first

        // ───────────────────────────────────────────────────────────────────────
        Rigidbody _rb;
        Vector3   _lastPos;

        void Awake()
        {
            _rb      = GetComponent<Rigidbody>();
            _lastPos = transform.position;
        }

        void Update()   => _lastPos = transform.position;

        void OnCollisionEnter(Collision collision)
        {
            // 0) Is the other object on a layer we care about?
            if ((reactLayers.value & (1 << collision.gameObject.layer)) == 0)
                return;

            // 1) Find the BodyPartReactor right on the hit Transform
            var part = collision.transform.GetComponent<BodyPartReactor>();
            if (part == null) return;

            // 2) Find its owning controller
            var ctrl = collision.transform.GetComponentInParent<HitReactionController>();
            if (ctrl == null) return;

            // 3) Compute the best incoming-direction guess
            Vector3 hitDir;
            if (_rb != null && _rb.linearVelocity.sqrMagnitude > 0.001f)
                hitDir = _rb.linearVelocity.normalized;
            else
            {
                Vector3 move = transform.position - _lastPos;
                hitDir = move.sqrMagnitude > 0.001f ? move.normalized
                                                    : -collision.contacts[0].normal;
            }

            // 4) Forward the hit
            Vector3 hitPt = collision.contacts[0].point;
            ctrl.React(collision.transform.name, hitDir, hitPt);

            // 5) Cleanup
            if (destroyOnHit)
                Destroy(gameObject, destroyDelay);
        }
    }
}
