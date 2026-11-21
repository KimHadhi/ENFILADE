using UnityEngine;

namespace ImpactCFX
{
    /// <summary>
    /// Implementation of an Impact Object for objects with a 3D or 2D rigidbody.
    /// </summary>
    [AddComponentMenu("Impact CFX/Objects/Impact Object Rigidbody")]
    [DisallowMultipleComponent]
    public class ImpactObjectRigidbody : ImpactObjectRigidbodyCheap
    {
        protected override void Awake()
        {
            base.Awake();
            RigidbodyContainer.SyncRigidbodyData();
        }

        private void FixedUpdate()
        {
            RigidbodyContainer.SyncRigidbodyData();
        }

        public override RigidbodyData GetRigidbodyData()
        {
            return RigidbodyContainer.GetRigidbodyData();
        }
    }
}