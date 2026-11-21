using UnityEngine;

namespace ImpactCFX
{
    /// <summary>
    /// Wrapper class for 3D and 2D Rigidbodies and Articulation Bodies that handles tracking the rigidbody state.
    /// </summary>
    public class RigidbodyContainer
    {
        private Rigidbody rigidbody3D;
        private Rigidbody2D rigidbody2D;
        private ArticulationBody articulationBody;
        private PhysicsType physicsType;

        private RigidbodyStateData previousRigidbodyState;

        private Vector3 currentVelocity
        {
            get
            {
                if (physicsType == PhysicsType.Physics2D)
                    return rigidbody2D.linearVelocity;
                else if (physicsType == PhysicsType.Physics3D)
                    return rigidbody3D.linearVelocity;
                else if (physicsType == PhysicsType.ArticulationBody)
                    return articulationBody.linearVelocity;

                return Vector3.zero;
            }
        }

        private Vector3 currentAngularVelocity
        {
            get
            {
                if (physicsType == PhysicsType.Physics2D)
                    return new Vector3(0, 0, rigidbody2D.angularVelocity * Mathf.Deg2Rad);
                else if (physicsType == PhysicsType.Physics3D)
                    return rigidbody3D.angularVelocity;
                else if (physicsType == PhysicsType.ArticulationBody)
                    return articulationBody.angularVelocity;

                return Vector3.zero;
            }
        }

        private Vector3 currentWorldCenterOfMass
        {
            get
            {
                if (physicsType == PhysicsType.Physics2D)
                    return rigidbody2D.worldCenterOfMass;
                else if (physicsType == PhysicsType.Physics3D)
                    return rigidbody3D.worldCenterOfMass;
                else if (physicsType == PhysicsType.ArticulationBody)
                    return articulationBody.worldCenterOfMass;

                return Vector3.zero;
            }
        }

        /// <summary>
        /// Creates an empty wrapper with no reference to a rigidbody.
        /// </summary>
        public RigidbodyContainer()
        {
            physicsType = PhysicsType.Unknown;
        }

        /// <summary>
        /// Creates a wrapper for the given GameObject that has either a Rigidbody or Rigidbody2D component.
        /// </summary>
        /// <param name="gameObject">A GameObject that has either a Rigidbody or Rigidbody2D component</param>
        public RigidbodyContainer(GameObject gameObject)
        {
            FindRigidbody(gameObject);
        }

        /// <summary>
        /// Attempts to find a Rigidbody, Rigidbody2D, or ArticulationBody to reference from the given GameObject or one of its parents.
        /// </summary>
        /// <param name="gameObject">The GameObject to use to find a rigidbody.</param>
        public void FindRigidbody(GameObject gameObject)
        {
            rigidbody3D = null;
            rigidbody2D = null;
            articulationBody = null;

            Rigidbody r3D = gameObject.GetComponentInParent<Rigidbody>();
            if (r3D != null)
            {
                rigidbody3D = r3D;
                physicsType = PhysicsType.Physics3D;
                return;
            }

            Rigidbody2D r2D = gameObject.GetComponentInParent<Rigidbody2D>();
            if (r2D != null)
            {
                rigidbody2D = r2D;
                physicsType = PhysicsType.Physics2D;
                return;
            }

            ArticulationBody ab = gameObject.GetComponentInParent<ArticulationBody>();
            if (ab != null)
            {
                articulationBody = ab;
                physicsType = PhysicsType.ArticulationBody;
                return;
            }

            physicsType = PhysicsType.Unknown;
            ImpactCFXLogger.LogMissingRigidbody(gameObject);

            SyncRigidbodyData();
        }

        /// <summary>
        /// Sets the 3D physics rigidbody referenced by this container.
        /// </summary>
        /// <param name="rigidbody">The rigidbody to reference.</param>
        public void SetRigidbody(Rigidbody rigidbody)
        {
            rigidbody3D = rigidbody;
            rigidbody2D = null;
            articulationBody = null;

            if (rigidbody3D.IsAlive())
                physicsType = PhysicsType.Physics3D;
            else
                physicsType = PhysicsType.Unknown;

            SyncRigidbodyData();
        }

        /// <summary>
        /// Sets the 2D physics rigidbody referenced by this container.
        /// </summary>
        /// <param name="rigidbody">The rigidbody to reference.</param>
        public void SetRigidbody2D(Rigidbody2D rigidbody)
        {
            rigidbody2D = rigidbody;
            rigidbody3D = null;
            articulationBody = null;

            if (rigidbody2D.IsAlive())
                physicsType = PhysicsType.Physics2D;
            else
                physicsType = PhysicsType.Unknown;

            SyncRigidbodyData();
        }

        /// <summary>
        /// Sets the articulation body referenced by this container.
        /// </summary>
        /// <param name="articulationBody">The articulation body to reference.</param>
        public void SetArticulationBody(ArticulationBody articulationBody)
        {
            this.articulationBody = articulationBody;
            rigidbody2D = null;
            rigidbody3D = null;

            if (articulationBody.IsAlive())
                physicsType = PhysicsType.ArticulationBody;
            else
                physicsType = PhysicsType.Unknown;

            SyncRigidbodyData();
        }

        /// <summary>
        /// Clears the rigidbody or articulation body reference from this container (does not destroy the rigidbody).
        /// </summary>
        public void ClearRigidbody()
        {
            rigidbody3D = null;
            rigidbody2D = null;
            articulationBody = null;
            physicsType = PhysicsType.Unknown;
        }

        /// <summary>
        /// Syncs the cached rigidbody data with the current rigidbody state.
        /// </summary>
        public void SyncRigidbodyData()
        {
            previousRigidbodyState.LinearVelocity = currentVelocity;
            previousRigidbodyState.AngularVelocity = currentAngularVelocity;
            previousRigidbodyState.CenterOfMass = currentWorldCenterOfMass;
        }

        /// <summary>
        /// Gets the full rigidbody data with both previous and current states.
        /// </summary>
        public RigidbodyData GetRigidbodyData()
        {
            return new RigidbodyData(previousRigidbodyState, GetCurrentRigidbodyState());
        }

        /// <summary>
        /// Gets the current rigidbody state, using the values directly from the rigidbody.
        /// </summary>
        public RigidbodyStateData GetCurrentRigidbodyState()
        {
            return new RigidbodyStateData(currentVelocity, currentAngularVelocity, currentWorldCenterOfMass);
        }

        /// <summary>
        /// Gets the previous state of the rigidbody from the last time it was synced (i.e. the previous frame).
        /// </summary>
        public RigidbodyStateData GetPreviousRigidbodyState()
        {
            return previousRigidbodyState;
        }
    }
}