using UnityEngine;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    public sealed class ZombieTarget : MonoBehaviour
    {
        [SerializeField] private Transform top = null;
        [SerializeField] private Transform bottom = null;
        [SerializeField] private Transform center = null;
        [SerializeField] private Transform left = null;
        [SerializeField] private Transform right = null;
        [SerializeField] private Collider primaryCollider = null;

        private Collider[] colliders;
        private Vector3 previousPosition;
        private bool hasPreviousPosition;

        public Vector3 Velocity { get; private set; }

        private void Awake()
        {
            RefreshColliders();
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
            Velocity = Vector3.zero;
            hasPreviousPosition = true;
        }

        private void Update()
        {
            if (!hasPreviousPosition)
            {
                previousPosition = transform.position;
                hasPreviousPosition = true;
                return;
            }

            float deltaTime = Time.deltaTime;
            Velocity = deltaTime > 0f ? (transform.position - previousPosition) / deltaTime : Vector3.zero;
            previousPosition = transform.position;
        }

        public void RefreshColliders()
        {
            colliders = GetComponentsInChildren<Collider>();
            if (primaryCollider == null && colliders.Length > 0)
            {
                primaryCollider = colliders[0];
            }
        }

        public Vector3 GetRegionPosition(ZombieVisionRegion region)
        {
            switch (region)
            {
                case ZombieVisionRegion.Top:
                    return top != null ? top.position : GetFallbackRegionPosition(ZombieVisionRegion.Top);
                case ZombieVisionRegion.Bottom:
                    return bottom != null ? bottom.position : GetFallbackRegionPosition(ZombieVisionRegion.Bottom);
                case ZombieVisionRegion.Center:
                    return center != null ? center.position : GetFallbackRegionPosition(ZombieVisionRegion.Center);
                case ZombieVisionRegion.Left:
                    return left != null ? left.position : GetFallbackRegionPosition(ZombieVisionRegion.Left);
                case ZombieVisionRegion.Right:
                    return right != null ? right.position : GetFallbackRegionPosition(ZombieVisionRegion.Right);
                default:
                    return transform.position;
            }
        }

        public bool ContainsCollider(Collider candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            return candidate.transform == transform || candidate.transform.IsChildOf(transform);
        }

        public bool IsMoving(float speedThreshold)
        {
            return Velocity.sqrMagnitude >= speedThreshold * speedThreshold;
        }

        private Vector3 GetFallbackRegionPosition(ZombieVisionRegion region)
        {
            Bounds bounds = GetBounds();
            Vector3 centerPoint = bounds.center;
            float verticalExtent = Mathf.Max(0.1f, bounds.extents.y);
            float horizontalExtent = Mathf.Max(0.25f, Mathf.Max(bounds.extents.x, bounds.extents.z));

            switch (region)
            {
                case ZombieVisionRegion.Top:
                    return centerPoint + transform.up * verticalExtent;
                case ZombieVisionRegion.Bottom:
                    return centerPoint - transform.up * verticalExtent;
                case ZombieVisionRegion.Left:
                    return centerPoint - transform.right * horizontalExtent;
                case ZombieVisionRegion.Right:
                    return centerPoint + transform.right * horizontalExtent;
                default:
                    return centerPoint;
            }
        }

        private Bounds GetBounds()
        {
            if (primaryCollider != null && primaryCollider.enabled)
            {
                return primaryCollider.bounds;
            }

            if (colliders == null || colliders.Length == 0)
            {
                RefreshColliders();
            }

            bool hasBounds = false;
            Bounds bounds = new Bounds(transform.position + Vector3.up, new Vector3(0.6f, 2f, 0.6f));

            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider candidate = colliders[i];
                    if (candidate == null || !candidate.enabled)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = candidate.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(candidate.bounds);
                    }
                }
            }

            return bounds;
        }
    }
}
