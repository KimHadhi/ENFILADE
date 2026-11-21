using UnityEngine;

namespace ImpactCFX
{
    /// <summary>
    /// A "cheap" 3D or 2D rigidbody that does not have FixedUpdate.
    /// </summary>
    [AddComponentMenu("Impact CFX/Objects/Impact Object Rigidbody (Cheap)")]
    [DisallowMultipleComponent]
    public class ImpactObjectRigidbodyCheap : ImpactObjectSingleMaterial
    {
        private RigidbodyContainer rigidbodyContainer;

        /// <summary>
        /// Container for handling 2D and 3D rigidbody references.
        /// </summary>
        public RigidbodyContainer RigidbodyContainer => rigidbodyContainer;

        protected virtual void Awake()
        {
            rigidbodyContainer = new RigidbodyContainer(gameObject);
        }

        /// <summary>
        /// Attempts to find a rigidbody to reference from this object or one of its parents.
        /// </summary>
        public void FindRigidbody()
        {
            rigidbodyContainer.FindRigidbody(gameObject);
        }

        /// <summary>
        /// Sets the 3D physics rigidbody referenced by this object.
        /// </summary>
        /// <param name="rigidbody">The rigidbody to reference.</param>
        public void SetRigidbody(Rigidbody rigidbody)
        {
            rigidbodyContainer.SetRigidbody(rigidbody);
        }

        /// <summary>
        /// Sets the 2D physics rigidbody referenced by this object.
        /// </summary>
        /// <param name="rigidbody">The rigidbody to reference.</param>
        public void SetRigidbody2D(Rigidbody2D rigidbody)
        {
            rigidbodyContainer.SetRigidbody2D(rigidbody);
        }

        /// <summary>
        /// Sets the articulation body referenced by this object.
        /// </summary>
        /// <param name="articulationBody">The articulation body to reference.</param>
        public void SetArticulationBody(ArticulationBody articulationBody)
        {
            rigidbodyContainer.SetArticulationBody(articulationBody);
        }

        /// <summary>
        /// Clears the rigidbody reference from this object (does not destroy the rigidbody).
        /// </summary>
        public void ClearRigidbody()
        {
            rigidbodyContainer.ClearRigidbody();
        }

        /// <summary>
        /// Forces the object to sync its cached rigidbody data with the current rigidbody state.
        /// </summary>
        public void SyncRigidbodyData()
        {
            rigidbodyContainer.SyncRigidbodyData();
        }

        public override RigidbodyData GetRigidbodyData()
        {
            RigidbodyStateData currentState = rigidbodyContainer.GetCurrentRigidbodyState();
            return new RigidbodyData(currentState, currentState);
        }
    }
}