using UnityEngine;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Advanced Zombie AI Pro/Zombie Ragdoll Controller")]
    public sealed class ZombieRagdollController : MonoBehaviour
    {
        [SerializeField] private Rigidbody[] ragdollBodies = new Rigidbody[0];
        [SerializeField] private Collider[] ragdollColliders = new Collider[0];
        [SerializeField] private bool disableRagdollOnAwake = true;

        public Rigidbody[] RagdollBodies => ragdollBodies;
        public Collider[] RagdollColliders => ragdollColliders;

        private void Reset()
        {
            RefreshRagdollParts();
        }

        private void Awake()
        {
            if (ragdollBodies == null || ragdollBodies.Length == 0 || ragdollColliders == null || ragdollColliders.Length == 0)
            {
                RefreshRagdollParts();
            }

            if (disableRagdollOnAwake)
            {
                SetRagdollActive(false);
            }
        }

        private void OnValidate()
        {
            if (ragdollBodies == null)
            {
                ragdollBodies = new Rigidbody[0];
            }

            if (ragdollColliders == null)
            {
                ragdollColliders = new Collider[0];
            }
        }

        [ContextMenu("Refresh Ragdoll Parts")]
        public void RefreshRagdollParts()
        {
            Rigidbody[] allBodies = GetComponentsInChildren<Rigidbody>(true);
            Collider[] allColliders = GetComponentsInChildren<Collider>(true);

            int bodyCount = 0;
            for (int i = 0; i < allBodies.Length; i++)
            {
                if (allBodies[i] != null && allBodies[i].transform != transform)
                {
                    bodyCount++;
                }
            }

            ragdollBodies = new Rigidbody[bodyCount];
            int bodyIndex = 0;
            for (int i = 0; i < allBodies.Length; i++)
            {
                Rigidbody body = allBodies[i];
                if (body != null && body.transform != transform)
                {
                    ragdollBodies[bodyIndex] = body;
                    bodyIndex++;
                }
            }

            int colliderCount = 0;
            for (int i = 0; i < allColliders.Length; i++)
            {
                if (allColliders[i] != null && allColliders[i].transform != transform)
                {
                    colliderCount++;
                }
            }

            ragdollColliders = new Collider[colliderCount];
            int colliderIndex = 0;
            for (int i = 0; i < allColliders.Length; i++)
            {
                Collider ragdollCollider = allColliders[i];
                if (ragdollCollider != null && ragdollCollider.transform != transform)
                {
                    ragdollColliders[colliderIndex] = ragdollCollider;
                    colliderIndex++;
                }
            }
        }

        public void SetRagdollActive(bool active)
        {
            if (ragdollBodies != null)
            {
                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null)
                    {
                        continue;
                    }

                    body.isKinematic = !active;
                    body.detectCollisions = true;
                }
            }

            if (ragdollColliders == null)
            {
                return;
            }

            for (int i = 0; i < ragdollColliders.Length; i++)
            {
                Collider ragdollCollider = ragdollColliders[i];
                if (ragdollCollider != null)
                {
                    ragdollCollider.enabled = true;
                }
            }
        }

        public void EnableRagdoll(Vector3 force, Vector3 forcePoint, bool hasForcePoint, ForceMode forceMode)
        {
            SetRagdollActive(true);

            if (force.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Rigidbody forceBody = GetForceBody(forcePoint, hasForcePoint);
            if (forceBody == null)
            {
                return;
            }

            if (hasForcePoint)
            {
                forceBody.AddForceAtPosition(force, forcePoint, forceMode);
            }
            else
            {
                forceBody.AddForce(force, forceMode);
            }
        }

        private Rigidbody GetForceBody(Vector3 forcePoint, bool hasForcePoint)
        {
            if (ragdollBodies == null || ragdollBodies.Length == 0)
            {
                return null;
            }

            Rigidbody fallback = null;
            float closestSqrDistance = float.PositiveInfinity;

            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = body;
                }

                if (!hasForcePoint)
                {
                    continue;
                }

                float sqrDistance = (body.worldCenterOfMass - forcePoint).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    fallback = body;
                }
            }

            return fallback;
        }
    }
}
